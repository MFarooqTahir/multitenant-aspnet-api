# Agent notes

The rules that are not obvious from the code. Setup, commands, the endpoint list, test counts
and current status live in `README.md`; decisions and their trade-offs live in `docs/adr/`.
Read those, plus `docs/architecture.md`, before changing tenant or auth behaviour. Start with
ADRs 0003 (isolation), 0004 (tokens), and 0013–0016 (toolchain, retention and concurrency).

---

## Golden rules

1. **Tenant context is not optional for tracked writes or tenant-filtered queries.** Every
   tracked tenant-owned `SaveChanges` write requires `ITenantContext.IsResolved`. Authenticated
   requests get it from the JWT via `JwtBearerConfiguration`; anonymous flows must call `SetTenant`
   before tracked writes or tenant-filtered reads. Deliberate pre-resolution reads use globally
   unique ids/token hashes, `IgnoreQueryFilters()` with an explicit tenant predicate, or the
   unfiltered `RefreshTokens` table with tenant-scoped set-based writes. Retention is globally
   scoped by design. `ITenantContext` exists only because query filters and the write guard cannot
   be handed a tenant by each caller — data access itself always goes through `AppDbContext`.
   There is deliberately no tenant repository.
2. **Never capture `ITenantContext` in an EF query filter.** The filters use
   `AppDbContext.CurrentTenantId` because EF caches the model per context type. A closure
   would freeze the first request's tenant for the process lifetime.
3. **`IgnoreQueryFilters()` must be paired with an explicit `TenantId` predicate.** It appears
   only in `AuthService`, `UserProvisioningService`, `JwtBearerConfiguration` and
   `DatabaseInitializer`. The one exception is `AuthService.ConfirmEmailAsync`, which looks a
   user up by its globally unique id because the tenant is unknown at that point; a valid
   per-user token is still required. Do not add it casually.
4. **Do not bypass the security-stamp check.** Access tokens are validated against the live
   user, role and security stamp on every request. Removing that makes role changes and
   logout-everywhere non-immediate.
5. **`SingleSessionPerUser` defaults to true.** Any change to login/refresh must preserve the
   behaviour: a new session invalidates older access tokens and refresh tokens.
6. **Cross-tenant resources return 404.** Do not "improve" this to 403; it leaks existence.
7. **Create users in one place.** Both `POST /auth/register` and `POST /users` call
   `IUserProvisioningService`; add email sending at the call site (onboarding sends only after
   its transaction commits). `POST /auth/register` is Owner/Admin only and validates the slug
   against the caller's own tenant — never make it anonymous again: a slug is not a credential,
   and open joining exposes the tenant's data.
8. **The last Owner is protected by a lock, not just a count.** `UserService` opens a
   transaction and takes `SELECT ... FOR UPDATE` on the tenant row before counting Owners, so
   two concurrent demotions/deletions cannot both pass the guard.
9. **One type per file, namespace mirrors the folder.** New interfaces go in
   `Application/Abstractions/<Area>` (Identity, Messaging, Tenancy), DTOs in
   `Contracts/<Feature>`, validators in `Validation/<Feature>`, options in
   `Configuration/<Section>`, and exceptions in `Errors/Exceptions`. The namespace is the
   folder path (`Multitenant.Application.Contracts.Auth`,
   `Multitenant.Application.Configuration.Jwt`), and Rider's namespace check should stay clean.
10. **Migrations are generated from the Infrastructure project**, not the API. The EF CLI is
    pinned in `.config/dotnet-tools.json`; restore it with `dotnet tool restore`, then run:
    ```
    dotnet tool run dotnet-ef migrations add X --project src/Multitenant.Infrastructure --startup-project src/Multitenant.Infrastructure --output-dir Persistence/Migrations
    ```
11. **Analyzers are strict.** `TreatWarningsAsErrors=true` and
    `AnalysisLevel=latest-recommended`. Use `[LoggerMessage]` partial methods (CA1848), and keep
    the CA1861/CA1707 suppressions where they are (migrations and test names only).
12. **Feature flags are per-tenant kill switches.** A key must be added to `TenantFeatureKeys`
    and have a real consumer before it is exposed; an absent row means enabled, and a disabled
    feature returns `404` with its own code (ADR 0009).
13. **NuGet versions live in `Directory.Packages.props`, and only stable releases are allowed.**
    CPM is on: add a `<PackageVersion>` there and a bare `<PackageReference>` in the project.
    Never put a `Version` attribute on a `PackageReference` (NU1010 if the version is missing),
    and never add a preview/RC/beta package. The application and data stack are on .NET 11 GA;
    only the Microsoft.OpenApi 2 / Asp.Versioning.OpenAPI integration remains on 10.x
    (ADR 0014). NuGet lock files are committed; CI and Docker restore with `--locked-mode`.
    Regenerate every affected `packages.lock.json` after changing the graph.
14. **`SaveChanges` guards do not cover set-based or raw writes.** `ExecuteUpdateAsync`,
    `ExecuteDeleteAsync` and `ExecuteSql*` bypass `TenantAuditingInterceptor` and query filters.
    Never use them on a tenant-owned entity without an explicit tenant predicate and an
    integration test. Set-based refresh-token revocation always includes `TenantId`; the
    retention job is deliberately global across expired rows. The raw SQL tenant lock is
    centralized in `TenantRowLock` and parameterized.
15. **Tenant ownership is immutable after insert.** The interceptor rejects `TenantId` changes;
    refresh tokens also have a composite `(TenantId, UserId)` foreign key.
16. **User administration locks before authorizing.** Create/role/delete operations take the
    tenant row lock, then re-read the actor and target roles. Registration, tenant rename and
    feature changes use the same lock-before-recheck rule. Do not move role reads back before
    the lock.
17. **2FA secrets and challenges are single-purpose.** Setup always rotates the key, rejects an
    already-enrolled user, and invalidates sessions. Challenges carry the security stamp and die
    on stamp rotation; all TOTP management paths share lockout counters.

---

## Adding a tenant-owned resource

1. Entity in `Multitenant.Domain/Entities`, implement `ITenantOwned` (and `IHasTimestamps` if
   it has timestamps).
2. Configure it in `AppDbContext` with the table name, lengths, indexes and
   `HasQueryFilter(x => x.TenantId == CurrentTenantId)`.
3. If it references another tenant-owned entity, use a composite FK on `(TenantId, Id)`.
4. Contracts + FluentValidation validator in Application.
5. Service in Infrastructure using `dbContext` (filters apply automatically).
6. Controller with `[Authorize(Policy = Policies.Member)]`; write actions guarded by
   `ManageTenant` where appropriate.
7. Add the migration with the command in rule 10.
8. Integration test proving a second tenant gets 404, plus unit tests for any invariants.
9. Update the `README.md` endpoint table and, if the decision is significant, add an ADR.

---

## Testing traps

- Every integration test class must carry `[Collection(IntegrationHarness.CollectionName)]`
  and take `IntegrationHarness` in its constructor, assigning it to `Harness`.
- `Harness.CreateFactory(dict => ...)` applies configuration **late** (WebApplicationFactory).
  That is why the DbContext connection string and the rate-limit options are read lazily; do
  not move those back into eager startup reads.
- `Scenarios.cs` holds the reusable API flows and the minimal RFC 6238 TOTP used to drive
  Identity's authenticator provider.
- Testcontainers needs a running Docker daemon; unit tests do not.

---

## Definition of done

- `dotnet build multitenant-aspnet-api.slnx` has 0 warnings (warnings are errors).
- Unit and integration tests pass; `dotnet format multitenant-aspnet-api.slnx --verify-no-changes` is clean.
- User-visible facts (endpoints, counts, status) go in `README.md`, not in these notes; a
  significant decision gets an ADR.
- No secrets, no employer code, no `appsettings.Development.json` or `.env` in a commit.

---

## Commit style

Short imperative subject, body explaining the why. Keep unrelated changes out of the same
commit.
