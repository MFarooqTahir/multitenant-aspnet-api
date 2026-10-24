# Handover

A practical guide for the next engineer or AI agent picking up this repository.

---

## 1. Status snapshot

| Area | State |
| --- | --- |
| Build | `dotnet build multitenant-aspnet-api.slnx` succeeds with 0 warnings (warnings are errors) |
| Unit tests | 32 passing |
| Integration tests | 54 passing against Testcontainers PostgreSQL 16 |
| API surface | Auth, tenants, users, projects, tasks, health, OpenAPI |
| Migrations | One squashed `InitialCreate` migration, applied automatically in Development |
| Docker | `Dockerfile` and `compose.yaml` are current; image builds |
| CI | `.github/workflows/ci.yml` builds, tests and publishes to GHCR on `main`/`master` and tags |

Nothing in the working tree is intentionally half-finished. The "known limitations" section in
`README.md` is the authoritative backlog.

---

## 2. Golden rules

Read these before changing anything touching data or auth.

1. **Tenant context is not optional.** Every tenant-owned write requires
   `ITenantContext.IsResolved`. Authenticated requests get it from the JWT via
   `JwtBearerConfiguration`; anonymous flows (login, refresh, onboarding) must
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
   after its transaction commits). `POST /auth/register` is Owner/Admin only and validates the
   slug against the caller's own tenant — never make it anonymous again: a slug is not a
   credential, and open joining exposes the tenant's data.
8. **The last Owner is protected by a lock, not just a count.** `UserService` opens a
   transaction and takes `SELECT ... FOR UPDATE` on the tenant row before counting Owners, so
   two concurrent demotions/deletions cannot both pass the guard.
9. **One type per file, namespace mirrors the folder.** New interfaces go in
   `Application/Abstractions/<Area>` (Identity, Messaging, Tenancy), DTOs in
   `Contracts/<Feature>`, validators in `Validation/<Feature>`, options in
   `Configuration/<Section>`, and exceptions in `Errors/Exceptions`. The namespace is the
   folder path (`Multitenant.Application.Contracts.Auth`, `Multitenant.Application.Configuration.Jwt`),
   and Rider's namespace check should stay clean.
10. **Migrations are generated from the Infrastructure project**, not the API:
   `dotnet ef migrations add X --project src/Multitenant.Infrastructure --startup-project src/Multitenant.Infrastructure --output-dir Persistence/Migrations`.
11. **Analyzers are strict.** `TreatWarningsAsErrors=true` and `AnalysisLevel=latest-recommended`.
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

# Start the local database only (loopback-bound, data kept in the postgres-data volume).
# Compose needs a .env first: cp .env.example .env and set JWT_SIGNING_KEY.
docker compose up -d postgres

# Run the API against that database; it creates the DB, migrates and seeds on start
dotnet run --project src/Multitenant.Api --launch-profile http

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
| Forwarded headers (proxy support) | `src/Multitenant.Api/Configuration/ForwardedHeadersSettings.cs` |
| OpenAPI bearer scheme | `src/Multitenant.Api/OpenApi/BearerSecuritySchemeTransformer.cs` |
| Confirmation page (email link) | `src/Multitenant.Api/Endpoints/ConfirmEmailPage.cs` |
| Error handling | `src/Multitenant.Api/Middleware/GlobalExceptionHandler.cs`, `ProblemDetailsStatusCodePages.cs` |
| Startup validation (validators, hosts) | `src/Multitenant.Api/Program.cs`, `src/Multitenant.Api/Filters/ValidationFilterAssertion.cs` |
| Rate limiting | `src/Multitenant.Api/RateLimiting/RateLimitingSetup.cs` |
| Test harness | `tests/Multitenant.IntegrationTests/Infrastructure/IntegrationHarness.cs` |
| Test scenarios/helpers | `tests/Multitenant.IntegrationTests/Infrastructure/Scenarios.cs` |
| Docs | `README.md` (humans), `HANDOVER.md` (agents), `docs/architecture.md`, `docs/adr/` |

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
- The integration suite takes ~20 seconds once the PostgreSQL image is cached.

---

## 6. Behaviour worth knowing

- **2FA setup rotates the security stamp** (`ResetAuthenticatorKeyAsync`). A client must log in
  again between `/auth/2fa/setup` and `/auth/2fa/enable`. The test documents this.
- **Registration does not confirm email, but it does send the link.** `POST /auth/register`
  creates the user with `EmailConfirmed=false` and sends the confirmation email, like
  `/users` and onboarding. They can log in unless `Identity:RequireConfirmedEmail=true`.
- **Email confirmation is checked after the password.** `Identity`'s own
  `SignIn.RequireConfirmedEmail`/`RequireConfirmedAccount` gates are deliberately off: they run
  before the password, which let an anonymous caller tell an unconfirmed address (`403
  email_not_confirmed`) from an unknown one (`401`). `AuthService` enforces the flag itself, and
  `RefreshAsync` rejects unconfirmed users too, so old sessions die when the flag is switched on.
- **Demo seeding requires an explicit password.** `Database:SeedDemoData=true` with an empty
  `Demo:Password` throws at startup instead of silently skipping, and `compose.yaml` ships with
  seeding off (`SEED_DEMO_DATA`). Seeded accounts are pre-confirmed so they work with either
  flag value, and every one of them uses `Demo:Password`.
- **Onboarding sends the confirmation email after the transaction commits.** A failure is
  logged and does not roll back the tenant.
- **Role changes keep refresh tokens valid**, so the next refresh issues a token with the new
  role. Access tokens are invalidated immediately by the security-stamp bump.
- **Login is by tenant slug + email**, not email alone.
- **Status codes**: validation 400 (`validation_failed`), auth 401, role 403, missing/foreign
  resource 404, duplicates 409, rate limit 429 (`rate_limited`), disabled feature 404
  (`feature_disabled`).
- **Health checks** are anonymous: `/health`, `/health/live`, `/health/ready`. They bypass
  rate limiting so probes never report false failures.
- **Email confirmation** works even when `Identity:RequireConfirmedEmail=false`; resend is a
  no-op `202` in that mode. The emailed link puts the token in the URL fragment and the page
  POSTs `{ "userId", "token" }` to `/auth/confirm-email` (never a query string).
- **Security hardening** lives in startup and middleware: `AllowedHosts` must be explicit
  outside Development (startup throws on `*`), CORS allows only standard headers/methods with
  credentials disabled, `UseHttpsRedirection` is enabled, client correlation ids are restricted
  to `[A-Za-z0-9._-]`, confirmation tokens are redacted in `LoggingEmailSender`, and SMTP with
  `UseStartTls=false` logs a plaintext warning.
- **Validation is fail-closed.** `ValidationFilterAssertion` runs at startup and throws if any
  complex action parameter lacks an `IValidator<T>`; add a validator when adding an endpoint.
- **Provisioning and demo seeding are transactional.** `UserProvisioningService` joins a
  caller's transaction when one exists (onboarding) and owns one otherwise; the initializer
  wraps the whole demo tenant seed so a partial failure rolls back. A concurrent duplicate
  registration that beats the pre-check is mapped from the unique-index violation to the same
  `409 email_taken` instead of a `500`.
- **The database refuses half-set assignees.** `CK_WorkItems_AssigneeTenant` requires both
  `AssigneeTenantId` and `AssigneeUserId` to be set together; the composite FK is MATCH SIMPLE,
  so a NULL in either column would otherwise skip the tenant check and accept another tenant's
  user id (`TenantIntegrityTests` covers this).
- **`UserName` is an internal key, not a login name** (`{slug}:{email}`), so Identity's
  whitelist of allowed username characters is disabled in `DependencyInjection`; otherwise valid
  addresses containing apostrophes or non-ASCII characters would fail user creation.
- **OpenAPI** is versioned (`/openapi/v1.json`); Scalar is at `/scalar` and shows a bearer
  authentication panel from the declared scheme.

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

- Container listens on `8080`, non-root, plain HTTP. Terminate TLS at the proxy/ingress; the
  app enables `UseHttpsRedirection`, so forwarded scheme headers must be configured.
- Production configuration:
  - `Database:AutoMigrate=false`, `Database:SeedDemoData=false`
  - `AllowedHosts` set to the explicit public host name(s); startup fails on `*` outside Development
  - `ConnectionStrings__Postgres`, `Jwt__SigningKey` (32+ chars) from a secret store
  - `Auth:PublicBaseUrl` set to the public HTTPS URL used in confirmation links
  - `Email:Provider=Smtp` (or implement another `IEmailSender`)
  - optionally `Serilog:AzureMonitor:ConnectionString` for App Insights
- Configure a shared Data Protection key ring (Redis, Key Vault or a persistent volume) when
  running more than one replica, or confirmation tokens minted by one instance will not
  validate on another.
- Run migrations as an explicit release step.
- The image is published to `ghcr.io/<owner>/<repo>` on `main`/tags. The current release is
  `v1.0.1`, which produces container tags `1.0.1` and `1.0` (`latest` follows the default
  branch).
- Multi-replica caveat: rate limiter state is per process, and Data Protection keys are local
  unless a shared ring is configured.

---

## 9. Known gaps / sensible next tasks

1. Add public sign-up as a real invite flow (signed invites or an email-domain allow-list);
   `POST /auth/register` is admin-only today and should stay that way otherwise.
2. Add a background job to purge expired/revoked refresh tokens.
3. Distributed rate limiting (Redis) for multi-replica deployments.
4. Per-resource permissions (project membership) instead of tenant-wide roles.
5. Persisted audit trail for security events.
6. Templated emails and a real provider (SES / Azure Communication Services).
7. Optional caching of the per-request user/security-stamp lookup.
8. Per-tenant feature flags (PDF stretch item; only deployment-wide flags exist today).
9. Tenant management endpoints (list/update/deactivate/delete); onboarding, `current` and
   get-by-id exist today.

---

## 10. Git and release

- Default branch: `master` (the workflow also accepts `main`).
- Commit style used here: short imperative subject, body explaining the why.
- To cut a release:

  ```bash
  git tag -a v1.0.1 -m "Multitenant SaaS API v1.0.1"
  git push origin v1.0.1
  ```

  CI publishes `ghcr.io/<owner>/<repo>:1.0.1` and `:1.0`.

- Do not commit `.env`, `appsettings*.json` (other than `appsettings.json`), tokens or
  passwords. `appsettings.Development.json` is intentionally git-ignored; create it locally
  (see README `Local database`) or pass the values as environment variables.

---

## 11. First 15 minutes checklist

1. `git status` — expect a clean tree.
2. `dotnet build multitenant-aspnet-api.slnx` — expect 0 warnings.
3. `dotnet test tests/Multitenant.UnitTests` — expect 32 passing.
4. Start Docker, then `dotnet test tests/Multitenant.IntegrationTests` — expect 54 passing.
5. `cp .env.example .env` and adjust `JWT_SIGNING_KEY`/`POSTGRES_PASSWORD`. Demo accounts are
   opt-in: set `SEED_DEMO_DATA=true` and a `DEMO_PASSWORD` of your choice, then
   `docker compose up --build`, open <http://localhost:8080/scalar>, log in as
   `owner@acme.test` with that password. To run with `dotnet run` instead, first create the
   git-ignored `src/Multitenant.Api/appsettings.Development.json` (see README `Configuration`)
   or export the equivalent `ConnectionStrings__Postgres`, `Jwt__SigningKey`,
   `Demo__Password`, `Database__AutoMigrate` and `Database__SeedDemoData` variables.
6. Skim `docs/architecture.md` and the ADRs before changing tenant or auth behaviour.
