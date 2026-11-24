using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Identity;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class AuthLifecycleTests : IntegrationTestBase
{
    public AuthLifecycleTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task SeededDemoUserCanLoginAndReadTenant()
    {
        using var client = CreateClient();

        var tokens = await Scenarios.LoginAsync(client, "acme", "owner@acme.test", "Demo123!Passw0rd");

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.Equal("Bearer", tokens.TokenType);

        var response = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tenant = await response.ReadAsync<TenantResponse>();
        Assert.Equal("acme", tenant.Slug);
    }

    [Fact]
    public async Task LoginResponseIsNotCacheable()
    {
        using var client = CreateClient();

        var response = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("acme", "owner@acme.test", "Demo123!Passw0rd"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("no-store", cacheControl);
        Assert.True(response.Headers.TryGetValues("Pragma", out var pragma));
        Assert.Contains("no-cache", pragma);
    }

    [Fact]
    public async Task UnknownTenantOrUserOrWrongPassword_ReturnsTheSameUnauthorizedResponse()
    {
        using var client = CreateClient();
        var requests = new[]
        {
            new LoginRequest("missing-tenant", "owner@acme.test", "WrongPassword1!"),
            new LoginRequest("acme", "missing@acme.test", "WrongPassword1!"),
            new LoginRequest("acme", "owner@acme.test", "WrongPassword1!"),
        };

        var stableProblems = new List<(int Status, string Title, string Detail, string Code)>();
        foreach (var request in requests)
        {
            var response = await client.PostApi("/api/v1/auth/login", request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            stableProblems.Add((
                problem.Status ?? 0,
                problem.Title ?? string.Empty,
                problem.Detail ?? string.Empty,
                problem.Extensions["code"]?.ToString() ?? string.Empty));
        }

        Assert.Single(stableProblems.Distinct());
        Assert.Equal((401, "Unauthorized", "Invalid credentials.", "unauthorized"), stableProblems[0]);
    }

    [Fact]
    public async Task InvalidCredentialsAreRejected()
    {
        using var client = CreateClient();

        var response = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("acme", "owner@acme.test", "WrongPassword1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RefreshRotatesTheTokenAndDetectsReuse()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "auth-rotate", "rotate@acme.test");

        var rotated = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var rotatedTokens = await rotated.ReadAsync<AuthTokens>();

        Assert.NotEqual(tokens.RefreshToken, rotatedTokens.RefreshToken);

        // Replaying the consumed refresh token must fail...
        var replay = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...and revoke the whole token family, including the newest token.
        var afterReuse = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(rotatedTokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task SecondLoginInvalidatesTheFirstSession()
    {
        using var client = CreateClient();
        var first = await Scenarios.OnboardAndLoginAsync(client, "auth-single", "single@acme.test");
        var second = await Scenarios.LoginAsync(client, "auth-single", "single@acme.test");

        var firstAccess = await client.GetApi("/api/v1/tenants/current", first.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, firstAccess.StatusCode);

        var firstRefresh = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, firstRefresh.StatusCode);

        var secondAccess = await client.GetApi("/api/v1/tenants/current", second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, secondAccess.StatusCode);
    }

    [Fact]
    public async Task LogoutCannotRevokeAnotherUsersSession()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "auth-logout-owner", "logout-owner@acme.test");

        var registered = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-logout-owner", "logout-member@acme.test", "Logout Member", Scenarios.Password),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var member = await Scenarios.LoginAsync(
            client,
            "auth-logout-owner",
            "logout-member@acme.test",
            Scenarios.Password);

        // The member presents the owner's refresh token. Logout is a no-op rather
        // than a way to end someone else's session.
        var logout = await client.PostApi(
            "/api/v1/auth/logout",
            new LogoutRequest(owner.RefreshToken),
            member.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refreshed = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(owner.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task RoleChangeInvalidatesAccessTokensAndRefreshPicksUpTheNewRole()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "auth-role", "role-owner@acme.test");

        var registered = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-role", "role-member@acme.test", "Role Member", Scenarios.Password),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var member = await registered.ReadAsync<UserResponse>();
        Assert.Equal("Member", member.Role);

        var memberTokens = await Scenarios.LoginAsync(client, "auth-role", "role-member@acme.test", Scenarios.Password);

        var promoted = await client.PatchApi(
            $"/api/v1/users/{member.Id}/role",
            new UpdateUserRoleRequest("Admin"),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);

        // Old access token is dead immediately.
        var staleAccess = await client.GetApi("/api/v1/tenants/current", memberTokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, staleAccess.StatusCode);

        // The refresh token survives a role change and issues the new role.
        var refreshed = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(memberTokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var refreshedTokens = await refreshed.ReadAsync<AuthTokens>();

        var users = await client.GetApi("/api/v1/users", refreshedTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
    }

    [Fact]
    public async Task LogoutRevokesTheRefreshToken()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "auth-logout", "logout@acme.test");

        var logout = await client.PostApi(
            "/api/v1/auth/logout",
            new LogoutRequest(tokens.RefreshToken),
            tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task LogoutAllInvalidatesTheCurrentAccessToken()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "auth-logout-all", "logoutall@acme.test");

        var logoutAll = await client.PostApi("/api/v1/auth/logout-all", null, tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);

        var staleAccess = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, staleAccess.StatusCode);
    }

    [Fact]
    public async Task LogoutAllRollsBackTheStampWhenRefreshTokenRevocationFails()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "auth-logout-atomic", "logout-atomic@acme.test");

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                BEGIN;
                DROP TRIGGER IF EXISTS trg_reject_refresh_token_update_for_test ON "RefreshTokens";
                CREATE OR REPLACE FUNCTION reject_refresh_token_update_for_test() RETURNS trigger AS $$
                BEGIN
                    IF NEW."RevokedReason" = 'logout_all' THEN
                        RAISE EXCEPTION 'injected refresh-token revocation failure';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_reject_refresh_token_update_for_test
                BEFORE UPDATE ON "RefreshTokens"
                FOR EACH ROW EXECUTE FUNCTION reject_refresh_token_update_for_test();
                COMMIT;
                """);
        }

        try
        {
            var logoutAll = await client.PostApi("/api/v1/auth/logout-all", null, tokens.AccessToken);
            Assert.Equal(HttpStatusCode.InternalServerError, logoutAll.StatusCode);

            // Both writes belong to one transaction: the failed revocation must
            // roll back the security-stamp change and leave the session usable.
            var access = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);
            Assert.Equal(HttpStatusCode.OK, access.StatusCode);

            var refresh = await client.PostApi(
                "/api/v1/auth/refresh",
                new RefreshTokenRequest(tokens.RefreshToken));
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        }
        finally
        {
            using var scope = Harness.Factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS trg_reject_refresh_token_update_for_test ON "RefreshTokens";
                DROP FUNCTION IF EXISTS reject_refresh_token_update_for_test();
                """);
        }
    }

    [Fact]
    public async Task FailedNewSessionIssuanceRollsBackOldSessionRevocation()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "auth-issue-atomic", "issue-atomic@acme.test");

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                BEGIN;
                DROP TRIGGER IF EXISTS trg_reject_refresh_insert_for_test ON "RefreshTokens";
                CREATE OR REPLACE FUNCTION reject_refresh_insert_for_test() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'injected refresh-token insert failure';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_reject_refresh_insert_for_test
                BEFORE INSERT ON "RefreshTokens"
                FOR EACH ROW EXECUTE FUNCTION reject_refresh_insert_for_test();
                COMMIT;
                """);
        }

        try
        {
            var login = await client.PostApi(
                "/api/v1/auth/login",
                new LoginRequest("auth-issue-atomic", "issue-atomic@acme.test", Scenarios.Password));
            Assert.Equal(HttpStatusCode.InternalServerError, login.StatusCode);

            var access = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);
            Assert.Equal(HttpStatusCode.OK, access.StatusCode);

            using (var scope = Harness.Factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stored = await dbContext.RefreshTokens
                    .SingleAsync(token => token.TokenHash == RefreshTokenManager.Hash(tokens.RefreshToken));
                Assert.Null(stored.RevokedAtUtc);
                Assert.Equal(1, await dbContext.RefreshTokens.CountAsync(token => token.UserId == stored.UserId));
            }

            // Remove the injected failure, then prove the old refresh token was
            // not revoked by the rolled-back single-session transition.
            using (var scope = Harness.Factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER IF EXISTS trg_reject_refresh_insert_for_test ON "RefreshTokens";
                    DROP FUNCTION IF EXISTS reject_refresh_insert_for_test();
                    """);
            }

            var refresh = await client.PostApi(
                "/api/v1/auth/refresh",
                new RefreshTokenRequest(tokens.RefreshToken));
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        }
        finally
        {
            using var scope = Harness.Factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS trg_reject_refresh_insert_for_test ON "RefreshTokens";
                DROP FUNCTION IF EXISTS reject_refresh_insert_for_test();
                """);
        }
    }

    [Fact]
    public async Task RegistrationAddsMembersAndRejectsDuplicates()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "auth-register", "register-owner@acme.test");

        var created = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-register", "new-member@acme.test", "New Member", Scenarios.Password),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var member = await created.ReadAsync<UserResponse>();
        Assert.Equal("Member", member.Role);

        var login = await Scenarios.LoginAsync(client, "auth-register", "new-member@acme.test", Scenarios.Password);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));

        var duplicate = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-register", "new-member@acme.test", "New Member", Scenarios.Password),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // An Owner cannot create users in another tenant by supplying its slug.
        var foreignTenant = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("does-not-exist", "nobody@acme.test", "Nobody", Scenarios.Password),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, foreignTenant.StatusCode);

        var users = await client.GetApi("/api/v1/users", owner.AccessToken);
        var page = await users.ReadAsync<PagedResult<UserResponse>>();
        Assert.Contains(page.Items, user => user.Email == "new-member@acme.test");
    }

    [Fact]
    public async Task RegistrationRequiresATenantAdmin()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "auth-register-guard", "register-guard@acme.test");

        // Anonymous callers can no longer join a tenant by knowing its slug.
        var anonymous = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-register-guard", "anon@acme.test", "Anon", Scenarios.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var created = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-register-guard", "guarded-member@acme.test", "Guarded Member", Scenarios.Password),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var memberTokens = await Scenarios.LoginAsync(
            client,
            "auth-register-guard",
            "guarded-member@acme.test",
            Scenarios.Password);

        var forbidden = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-register-guard", "sneaky@acme.test", "Sneaky", Scenarios.Password),
            memberTokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task OnboardingLocationResolvesToTheTenant()
    {
        using var client = CreateClient();
        var onboard = await client.PostApi(
            "/api/v1/tenants/onboard",
            new OnboardTenantRequest("auth-location", "Auth Location", "location@acme.test", "Owner", Scenarios.Password));
        Assert.Equal(HttpStatusCode.Created, onboard.StatusCode);
        var tenant = await onboard.ReadAsync<TenantResponse>();
        Assert.Equal($"/api/v1/tenants/{tenant.Id}", onboard.Headers.Location?.ToString());
        var tokens = await Scenarios.LoginAsync(client, "auth-location", "location@acme.test");

        var response = await client.GetApi(onboard.Headers.Location!.ToString(), tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Another tenant's id is not reachable, so the endpoint cannot be used to probe.
        var other = await Scenarios.OnboardAsync(client, "auth-location-other", "Other", "other@acme.test");
        var notFound = await client.GetApi($"/api/v1/tenants/{other.Id}", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task MembersCannotManageUsers()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "auth-member-guard", "guard-owner@acme.test");

        var register = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-member-guard", "guard-member@acme.test", "Guard Member", Scenarios.Password),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var memberTokens = await Scenarios.LoginAsync(
            client,
            "auth-member-guard",
            "guard-member@acme.test",
            Scenarios.Password);

        var list = await client.GetApi("/api/v1/users", memberTokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefreshLetsExactlyOneRequestWin()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "auth-concurrent", "concurrent@acme.test");

        var requests = new[]
        {
            client.PostApi("/api/v1/auth/refresh", new RefreshTokenRequest(tokens.RefreshToken)),
            client.PostApi("/api/v1/auth/refresh", new RefreshTokenRequest(tokens.RefreshToken)),
        };

        var responses = await Task.WhenAll(requests);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredRefreshTokenIsRejected()
    {
        await using var factory = Harness.CreateFactory();
        using var client = factory.CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "auth-expiry", "expiry@acme.test");

        var me = await client.GetApi("/api/v1/users/me", tokens.AccessToken);
        var user = await me.ReadAsync<UserResponse>();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
            var tenantId = Guid.Parse(jwt.Claims.Single(claim => claim.Type == AuthClaims.TenantId).Value);
            var slug = jwt.Claims.Single(claim => claim.Type == AuthClaims.TenantSlug).Value;
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId, slug);
            var stored = await dbContext.RefreshTokens
                .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstAsync();

            stored.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        var refresh = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        var problem = await refresh.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("refresh_token_expired", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task SingleSessionCanBeDisabledForMultipleDevices()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Auth:SingleSessionPerUser"] = "false");
        using var client = factory.CreateClient();

        await Scenarios.OnboardAsync(client, "auth-multi", "Multi Device", "multi@acme.test");
        var first = await Scenarios.LoginAsync(client, "auth-multi", "multi@acme.test", Scenarios.Password);
        var second = await Scenarios.LoginAsync(client, "auth-multi", "multi@acme.test", Scenarios.Password);

        var firstAccess = await client.GetApi("/api/v1/tenants/current", first.AccessToken);
        var secondAccess = await client.GetApi("/api/v1/tenants/current", second.AccessToken);

        Assert.Equal(HttpStatusCode.OK, firstAccess.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondAccess.StatusCode);

        var firstRefresh = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(first.RefreshToken));
        var secondRefresh = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(second.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondRefresh.StatusCode);
    }

    [Fact]
    public async Task RepeatedBadPasswordsLockTheAccount()
    {
        using var client = CreateClient();
        await Scenarios.OnboardAsync(client, "auth-lockout", "Lockout", "lockout@acme.test");

        var badLogin = new LoginRequest("auth-lockout", "lockout@acme.test", "WrongPassword1!");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostApi("/api/v1/auth/login", badLogin);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // Even the correct password is rejected while the account is locked out.
        var correct = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("auth-lockout", "lockout@acme.test", Scenarios.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }

    [Fact]
    public async Task FailedRefreshUpdate_RollsBackAndLeavesOldSessionUsable()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(
            client,
            "auth-refresh-rollback",
            "refresh-rollback@acme.test");

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                BEGIN;
                DROP TRIGGER IF EXISTS trg_reject_refresh_rotation_for_test ON "RefreshTokens";
                CREATE OR REPLACE FUNCTION reject_refresh_rotation_for_test() RETURNS trigger AS $$
                BEGIN
                    IF NEW."RevokedReason" = 'rotated' THEN
                        RAISE EXCEPTION 'injected refresh-token rotation failure';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_reject_refresh_rotation_for_test
                BEFORE UPDATE ON "RefreshTokens"
                FOR EACH ROW EXECUTE FUNCTION reject_refresh_rotation_for_test();
                COMMIT;
                """);
        }

        try
        {
            var failed = await client.PostApi(
                "/api/v1/auth/refresh",
                new RefreshTokenRequest(tokens.RefreshToken));
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

            using (var scope = Harness.Factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stored = await dbContext.RefreshTokens
                    .SingleAsync(token => token.TokenHash == RefreshTokenManager.Hash(tokens.RefreshToken));
                Assert.Null(stored.RevokedAtUtc);
                Assert.Null(stored.ReplacedByTokenId);
                Assert.Null(stored.RevokedReason);
                Assert.Equal(1, await dbContext.RefreshTokens.CountAsync(token => token.UserId == stored.UserId));
            }

            var access = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);
            Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        }
        finally
        {
            using var scope = Harness.Factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS trg_reject_refresh_rotation_for_test ON "RefreshTokens";
                DROP FUNCTION IF EXISTS reject_refresh_rotation_for_test();
                """);
        }

        var refreshed = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task LogoutAll_InvalidatesAccessAndEverySessionRefreshToken()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Auth:SingleSessionPerUser"] = "false");
        using var client = factory.CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            "auth-logout-all-sessions",
            "Logout All Sessions",
            "logout-all-sessions@acme.test");
        var first = await Scenarios.LoginAsync(client, tenant.Slug, "logout-all-sessions@acme.test");
        var second = await Scenarios.LoginAsync(client, tenant.Slug, "logout-all-sessions@acme.test");

        var logoutAll = await client.PostApi("/api/v1/auth/logout-all", null, second.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);

        foreach (var accessToken in new[] { first.AccessToken, second.AccessToken })
        {
            var access = await client.GetApi("/api/v1/tenants/current", accessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        }

        foreach (var refreshToken in new[] { first.RefreshToken, second.RefreshToken })
        {
            var refresh = await client.PostApi(
                "/api/v1/auth/refresh",
                new RefreshTokenRequest(refreshToken));
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }
    }

    [Fact]
    public async Task Registration_ReturnsLocationThatResolvesToCreatedUser()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(
            client,
            "auth-register-location",
            "register-location-owner@acme.test");

        var response = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest(
                "auth-register-location",
                "register-location-member@acme.test",
                "Registration Location",
                Scenarios.Password),
            owner.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<UserResponse>();
        Assert.Equal($"/api/v1/users/{created.Id}", response.Headers.Location?.ToString());

        var follow = await client.GetApi(response.Headers.Location!.ToString(), owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);
        var fetched = await follow.ReadAsync<UserResponse>();
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.Email, fetched.Email);
        Assert.Equal(created.DisplayName, fetched.DisplayName);
        Assert.Equal(created.Role, fetched.Role);
        Assert.Equal(created.IsActive, fetched.IsActive);
        Assert.InRange(Math.Abs((created.CreatedAtUtc - fetched.CreatedAtUtc).TotalMilliseconds), 0, 1);
    }
}
