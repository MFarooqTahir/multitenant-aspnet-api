using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.IntegrationTests.Infrastructure;
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
    public async Task RoleChangeInvalidatesAccessTokensAndRefreshPicksUpTheNewRole()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "auth-role", "role-owner@acme.test");

        var registered = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-role", "role-member@acme.test", "Role Member", Scenarios.Password));
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
    public async Task RegistrationAddsMembersAndRejectsDuplicates()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "auth-register", "register-owner@acme.test");

        var created = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-register", "new-member@acme.test", "New Member", Scenarios.Password));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var member = await created.ReadAsync<UserResponse>();
        Assert.Equal("Member", member.Role);

        var login = await Scenarios.LoginAsync(client, "auth-register", "new-member@acme.test", Scenarios.Password);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));

        var duplicate = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-register", "new-member@acme.test", "New Member", Scenarios.Password));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var unknownTenant = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("does-not-exist", "nobody@acme.test", "Nobody", Scenarios.Password));
        Assert.Equal(HttpStatusCode.NotFound, unknownTenant.StatusCode);

        var users = await client.GetApi("/api/v1/users", owner.AccessToken);
        var page = await users.ReadAsync<PagedResult<UserResponse>>();
        Assert.Contains(page.Items, user => user.Email == "new-member@acme.test");
    }

    [Fact]
    public async Task MembersCannotManageUsers()
    {
        using var client = CreateClient();
        await Scenarios.OnboardAndLoginAsync(client, "auth-member-guard", "guard-owner@acme.test");

        var register = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("auth-member-guard", "guard-member@acme.test", "Guard Member", Scenarios.Password));
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
}
