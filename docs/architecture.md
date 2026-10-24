# Architecture

## System context

```mermaid
flowchart LR
    Client["Client app / curl / .http"] -->|"HTTPS + JWT"| Api["Multitenant API"]
    Api -->|"EF Core / Npgsql"| Db[("PostgreSQL 16")]
    Api -->|"IEmailSender"| Mail["SMTP or log sink"]
    Api -->|"JSON logs"| Logs["CloudWatch Logs / Azure Log Analytics"]
    Api -->|"OpenAPI + Scalar"| Docs["/openapi/v1.json · /scalar"]
```

## Layers and dependency direction

```mermaid
flowchart TD
    Api["Multitenant.Api<br/>controllers · middleware · auth pipeline · composition root"]
    Infra["Multitenant.Infrastructure<br/>EF Core · Identity · JWT · email · tenancy plumbing"]
    App["Multitenant.Application<br/>contracts · abstractions · validators · options"]
    Domain["Multitenant.Domain<br/>entities · invariants · enums · domain errors"]

    Api --> Infra --> App --> Domain
```

## Request pipeline

```mermaid
flowchart TD
    A["Request"] --> B["CorrelationIdMiddleware<br/>X-Correlation-Id + Serilog context"]
    B --> C["ExceptionHandler<br/>problem+json + code"]
    C --> D["StatusCodePages<br/>401/403/404/405 → problem+json"]
    D --> E["Serilog request logging"]
    E --> F["CORS (if configured)"]
    F --> G["Authentication<br/>JWT validation + live security stamp/role check"]
    G --> H["RateLimiter<br/>tenant partition / auth IP partition"]
    H --> I["Authorization<br/>Owner · Admin · Member policies"]
    I --> J["ValidationFilter<br/>FluentValidation → 400 problem+json"]
    J --> K["Controller → Application service → EF Core"]
    K --> L["Global query filter by TenantId<br/>+ SaveChanges tenant write guard"]
```

## Login and request-time validation

```mermaid
sequenceDiagram
    participant C as Client
    participant A as AuthController
    participant S as AuthService
    participant I as Identity
    participant D as PostgreSQL

    C->>A: POST /api/v1/auth/login (tenantSlug, email, password)
    A->>S: LoginAsync
    S->>D: resolve tenant by slug
    S->>D: find user by TenantId + NormalizedEmail (IgnoreQueryFilters + explicit tenant)
    S->>I: CheckPasswordSignInAsync (lockout on failure)
    I-->>S: success
    S->>I: UpdateSecurityStampAsync (single-session default)
    S->>D: revoke other refresh tokens
    S->>D: store SHA-256 hash of new refresh token
    S-->>C: access token (15 min) + refresh token (30 days)

    C->>A: GET /api/v1/projects + Bearer token
    A->>D: validate user by id + tenant
    A->>D: compare sstamp and role with the database
    A->>D: query projects (filtered by tenant_id)
    A-->>C: 200 or 401 if the stamp/role changed
```

## Refresh-token rotation

```mermaid
stateDiagram-v2
    [*] --> Active: login or refresh
    Active --> Rotated: refresh (new token in same family)
    Rotated --> TheftDetected: old token presented again
    TheftDetected --> RevokedFamily: revoke every token in the family
    Active --> Revoked: logout / logout-all / single-session login
    Active --> Expired: 30 days
```

## Data model

```mermaid
erDiagram
    Tenants ||--o{ AspNetUsers : "owns"
    Tenants ||--o{ Projects : "owns"
    Projects ||--o{ WorkItems : "has"
    AspNetUsers ||--o{ WorkItems : "assigned"
    AspNetUsers ||--o{ RefreshTokens : "owns"
    AspNetUsers ||--o{ AspNetUserRoles : "has"
    AspNetRoles ||--o{ AspNetUserRoles : "granted"

    Tenants {
        uuid Id PK
        varchar Slug UK
        varchar Name
        bool IsActive
        timestamptz CreatedAtUtc
    }
    AspNetUsers {
        uuid Id PK
        uuid TenantId FK
        varchar UserName UK
        varchar NormalizedEmail
        varchar DisplayName
        bool EmailConfirmed
        bool TwoFactorEnabled
        varchar SecurityStamp
        bool IsActive
    }
    Projects {
        uuid Id PK
        uuid TenantId FK
        varchar Name
        varchar Status
        timestamptz CreatedAtUtc
        timestamptz UpdatedAtUtc
    }
    WorkItems {
        uuid Id PK
        uuid TenantId FK
        uuid ProjectId FK
        varchar Title
        varchar Status
        varchar Priority
        uuid AssigneeUserId FK
        uuid AssigneeTenantId
        date DueDate
    }
    RefreshTokens {
        uuid Id PK
        uuid UserId FK
        uuid TenantId
        varchar TokenHash UK
        uuid FamilyId
        timestamptz ExpiresAtUtc
        timestamptz RevokedAtUtc
        uuid ReplacedByTokenId
    }
```

`AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens` and
`AspNetUserPasskeys` are the standard Identity tables and are omitted for readability.
`Projects` also has a unique alternate key on `(TenantId, Id)` so `WorkItems` can reference
it with the composite `(TenantId, ProjectId)` foreign key.

## Where to look

| Concern | File |
| --- | --- |
| Tenant resolution + live token checks | `src/Multitenant.Infrastructure/Identity/JwtBearerConfiguration.cs` |
| Query filters and model configuration | `src/Multitenant.Infrastructure/Persistence/AppDbContext.cs` |
| Write guard + timestamp stamping | `src/Multitenant.Infrastructure/Persistence/TenantAuditingInterceptor.cs` |
| Login, refresh, 2FA, registration | `src/Multitenant.Infrastructure/Identity/AuthService.cs` |
| User creation (registration + admin) | `src/Multitenant.Infrastructure/Identity/UserProvisioningService.cs` |
| Refresh token hashing/rotation helpers | `src/Multitenant.Infrastructure/Identity/RefreshTokenManager.cs` |
| Tenant onboarding transaction | `src/Multitenant.Infrastructure/Services/TenantService.cs` |
| Error contract | `src/Multitenant.Api/Middleware/GlobalExceptionHandler.cs` |
| Rate limiting | `src/Multitenant.Api/RateLimiting/RateLimitingSetup.cs` |
| Demo seeding | `src/Multitenant.Infrastructure/Persistence/DatabaseInitializer.cs` |
