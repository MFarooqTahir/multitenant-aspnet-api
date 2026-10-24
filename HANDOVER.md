# Handover

A practical guide for the next engineer or AI agent picking up this repository.

---

## 1. Status snapshot

| Area | State |
| --- | --- |
| Build | `dotnet build multitenant-aspnet-api.slnx` succeeds with 0 warnings (warnings are errors) |
| Unit tests | 31 passing |
| Integration tests | 26 passing against Testcontainers PostgreSQL 16 |
| API surface | Auth, tenants, users, projects, tasks, health, OpenAPI |
| Migrations | `Persistence/Migrations/20261024141223_InitialCreate` applied automatically in Development |
| Docker | `Dockerfile` and `compose.yaml` are current; image builds |
| CI | `.github/workflows/ci.yml` builds, tests and publishes to GHCR on `main`/tags |
| Release tag | Intended `v1.0.0` (create after merge) |

Nothing in the working tree is intentionally half-finished. The "known limitations" section in
`README.md` is the authoritative backlog.

---

## 2. Golden rules

Read these before changing anything touching data or auth.

1. **Tenant context is not optional.** Every tenant-owned write requires
   `ITenantContext.IsResolved`. Authenticated requests get it from the JWT via
   `JwtBearerConfiguration`; anonymous flows (login, refresh, registration, onboarding) must
   call `SetTenant` before touching tenant-owned tables. `ITenantContext` exists only because
   query filters and the write guard cannot be handed a tenant by each caller — data access
   itself always goes through `AppDbContext`. There is deliberately no tenant repository.
2. **Never capture `ITenantContext` in an EF query filter.** The filters use
   `AppDbContext.CurrentTenantId` because EF caches the model per context type. A closure
   would freeze the first request's tenant for the process lifetime.
3. **`IgnoreQueryFilters()` must be paired with an explicit `TenantId` predicate.** It appears
   only in `AuthService`, `UserProvisioningService`, `UserService` role lookups,
   `DatabaseInitializer` and `JwtBearerConfiguration`. Do not add it casually.
4. **Do not bypass the security-stamp check.** Access tokens are validated against the live
   user, role and security stamp on every request. Removing that makes role changes and
   logout-everywhere non-immediate.
5. **`SingleSessionPerUser` defaults to true.** Any change to login/refresh must preserve the
   behaviour: a new session invalidates older access tokens and refresh tokens.
6. **Cross-tenant resources return 404.** Do not "improve" this to 403; it leaks existence.
7. **Create users in one place.** Both `POST /auth/register` and `POST /users` call
   `IUserProvisioningService`; add email sending at the call site (onboarding sends only
   after its transaction commits).
8. **One type per file, namespace mirrors the folder.** New interfaces go in
   `Application/Abstractions/<Area>` (Identity, Messaging, Tenancy), DTOs in
   `Contracts/<Feature>`, validators in `Validation/<Feature>`, options in
   `Configuration/<Section>`, and exceptions in `Errors/Exceptions`. The namespace is the
   folder path (`Multitenant.Application.Contracts.Auth`, `Multitenant.Application.Configuration.Jwt`),
   and Rider's namespace check should stay clean.
9. **Migrations are generated from the Infrastructure project**, not the API:
   `dotnet ef migrations add X --project src/Multitenant.Infrastructure --startup-project src/Multitenant.Infrastructure --output-dir Persistence/Migrations`.
10. **Analyzers are strict.** `TreatWarningsAsErrors=true` and `AnalysisLevel=latest-recommended`.
    Use `[LoggerMessage]` partial methods (CA1848), and keep CA1861/CA1707 suppressions where
    they are (migrations and test names only).

---

## 3. Commands

```bash
# Restore + build
dotnet build multitenant-aspnet-api.slnx

# Fast tests (no Docker)
dotnet test tests/Multitenant.UnitTests

# Integration tests (Docker daemon must be running)
dotnet test tests/Multitenant.IntegrationTests

# Everything
dotnet test multitenant-aspnet-api.slnx

# Run the API against local PostgreSQL
dotnet run --project src/Multitenant.Api

# Run the whole stack
docker compose up --build

# Formatting / style check
dotnet format multitenant-aspnet-api.slnx --verify-no-changes
```

Windows/PowerShell is the primary local environment, but everything except the shell syntax is
cross-platform.

---

## 4. Where things live

| Concern | File |
| --- | --- |
| Composition root and pipeline | `src/Multitenant.Api/Program.cs` |
| DI for infrastructure | `src/Multitenant.Infrastructure/DependencyInjection.cs` |
| Token validation + live user checks | `src/Multitenant.Infrastructure/Identity/JwtBearerConfiguration.cs` |
| Login/refresh/2FA/registration | `src/Multitenant.Infrastructure/Identity/AuthService.cs` |
| Refresh hashing/rotation | `src/Multitenant.Infrastructure/Identity/RefreshTokenManager.cs` |
| EF model, filters, indexes | `src/Multitenant.Infrastructure/Persistence/AppDbContext.cs` |
| Write guard + timestamps | `src/Multitenant.Infrastructure/Persistence/TenantAuditingInterceptor.cs` |
| Seeding | `src/Multitenant.Infrastructure/Persistence/DatabaseInitializer.cs` |
| Tenant onboarding transaction | `src/Multitenant.Infrastructure/Services/TenantService.cs` |
| User/role rules | `src/Multitenant.Infrastructure/Services/UserService.cs` |
| User creation (all paths) | `src/Multitenant.Infrastructure/Identity/UserProvisioningService.cs` |
| Project/task queries | `src/Multitenant.Infrastructure/Services/ProjectService.cs`, `TaskService.cs` |
| Contracts (DTOs) | `src/Multitenant.Application/Contracts/` (one file per DTO, grouped by feature) |
| Service interfaces | `src/Multitenant.Application/Abstractions/` (one interface per file) |
| Validators | `src/Multitenant.Application/Validation/` (one validator per file) |
| Config options | `src/Multitenant.Application/Configuration/` (one options class per file) |
| Policies | `src/Multitenant.Api/Authorization/Policies.cs` |
| Error handling | `src/Multitenant.Api/Middleware/GlobalExceptionHandler.cs`, `ProblemDetailsStatusCodePages.cs` |
| Rate limiting | `src/Multitenant.Api/RateLimiting/RateLimitingSetup.cs` |
| Test harness | `tests/Multitenant.IntegrationTests/Infrastructure/IntegrationHarness.cs` |
| Test scenarios/helpers | `tests/Multitenant.IntegrationTests/Infrastructure/Scenarios.cs` |
| Docs | `README.md`, `docs/architecture.md`, `docs/adr/` |

---

## 5. Testing notes

- `IntegrationHarness` is an xUnit **collection fixture** (`[CollectionDefinition]` +
  `ICollectionFixture`). It starts one PostgreSQL container per run. Every integration test
  class must:
  - carry `[Collection(IntegrationHarness.CollectionName)]`; and
  - take `IntegrationHarness` in its constructor and assign it to `Harness`.
- Each test onboards a tenant with a **unique slug**. The database is shared across tests in a
  run, so reusing a slug causes a 409. The demo tenants `acme`/`globex` are seeded once at app
  startup.
- Test-host configuration overrides are passed via `Harness.CreateFactory(dict => ...)`. The
  settings are applied late (WebApplicationFactory), which is why the DbContext connection
  string and rate-limit options are resolved lazily. Do not move those back into eager startup
  reads.
- `Scenarios.cs` contains the reusable API flows and a minimal RFC 6238 TOTP implementation
  used to drive Identity's authenticator provider.
- If Testcontainers cannot find Docker, unit tests still pass; integration tests fail at
  container startup. Start Docker Desktop (or the engine) first.
- The integration suite currently takes ~10 seconds after the image is cached.

---

## 6. Behaviour worth knowing

- **2FA setup rotates the security stamp** (`ResetAuthenticatorKeyAsync`). A client must log in
  again between `/auth/2fa/setup` and `/auth/2fa/enable`. The test documents this.
- **Registration does not confirm email.** New users start with `EmailConfirmed=false`. They
  can log in unless `Identity:RequireConfirmedEmail=true`.
- **Demo users are seeded pre-confirmed** so they work with either flag value.
- **Onboarding sends the confirmation email after the transaction commits.** A failure is
  logged and does not roll back the tenant.
- **Role changes keep refresh tokens valid**, so the next refresh issues a token with the new
  role. Access tokens are invalidated immediately by the security-stamp bump.
- **Login is by tenant slug + email**, not email alone. `UserName` is `{slug}:{email}`.
- **`Demo:Password` is used for every seeded account** in both demo tenants.
- **Status codes**: validation 400 (`validation_failed`), auth 401, role 403, missing/foreign
  resource 404, duplicates 409, rate limit 429 (`rate_limited`), disabled feature 404
  (`feature_disabled`).
- **Health checks** are anonymous: `/health`, `/health/live`, `/health/ready`.
- **OpenAPI** is versioned (`/openapi/v1.json`); Scalar is at `/scalar`. The bearer scheme is
  not declared in the document yet, so paste the token into Scalar's auth UI manually.

---

## 7. Adding a new tenant-owned resource

1. Entity in `Multitenant.Domain/Entities`, implement `ITenantOwned` (and `IHasTimestamps` if
   it has timestamps).
2. Configure it in `AppDbContext` with the table name, lengths, indexes and
   `HasQueryFilter(x => x.TenantId == CurrentTenantId)`.
3. If it references another tenant-owned entity, use a composite FK on `(TenantId, Id)`.
4. Contracts + FluentValidation validator in Application.
5. Service in Infrastructure using `dbContext` (filters apply automatically).
6. Controller with `[Authorize(Policy = Policies.Member)]` and write actions guarded by
   `ManageTenant` where appropriate.
7. Migration (see commands above).
8. Integration test proving a second tenant gets 404, plus unit tests for any invariants.
9. Update `README.md` endpoint table and, if the decision is significant, add an ADR.

---

## 8. Deployment notes

- Container listens on `8080`, non-root, plain HTTP. Terminate TLS at the proxy/ingress.
- Production configuration:
  - `Database:AutoMigrate=false`, `Database:SeedDemoData=false`
  - `ConnectionStrings__Postgres`, `Jwt__SigningKey` (32+ chars) from a secret store
  - `Email:Provider=Smtp` (or implement another `IEmailSender`)
  - optionally `Serilog:AzureMonitor:ConnectionString` for App Insights
- Run migrations as an explicit release step.
- The image is published to `ghcr.io/<owner>/<repo>` on `main`/tags. Tag `v1.0.0` produces
  container tags `1.0.0` and `1.0`.
- Multi-replica caveat: rate limiter state is per process.

---

## 9. Known gaps / sensible next tasks

1. Replace slug-based self-registration with signed invites (the only "open door").
2. Add a background job to purge expired/revoked refresh tokens.
3. Distributed rate limiting (Redis) for multi-replica deployments.
4. OpenAPI bearer security scheme + Scalar prefill.
5. Per-resource permissions (project membership) instead of tenant-wide roles.
6. Persisted audit trail for security events.
7. Templated emails and a real provider (SES / Azure Communication Services).
8. Optional caching of the per-request user/security-stamp lookup.

---

## 10. Git and release

- Default branch: `main` (the workflow also accepts `master`).
- Commit style used here: short imperative subject, body explaining the why.
- To cut the release after merging:

  ```bash
  git tag -a v1.0.0 -m "Multitenant SaaS API v1.0.0"
  git push origin v1.0.0
  ```

  CI publishes `ghcr.io/<owner>/<repo>:1.0.0` and `:1.0`.

- Do not commit `.env`, `appsettings.*.local.json`, tokens or passwords. The committed
  `appsettings.Development.json` contains only local demo values.

---

## 11. First 15 minutes checklist

1. `git status` — expect a clean tree.
2. `dotnet build multitenant-aspnet-api.slnx` — expect 0 warnings.
3. `dotnet test tests/Multitenant.UnitTests` — expect 31 passing.
4. Start Docker, then `dotnet test tests/Multitenant.IntegrationTests` — expect 26 passing.
5. `docker compose up --build`, open <http://localhost:8080/scalar>, log in as
   `owner@acme.test` / `Demo123!Passw0rd`.
6. Skim `docs/architecture.md` and the ADRs before changing tenant or auth behaviour.
