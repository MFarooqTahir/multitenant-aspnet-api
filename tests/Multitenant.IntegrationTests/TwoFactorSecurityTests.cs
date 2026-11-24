using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class TwoFactorSecurityTests : IntegrationTestBase
{
    public TwoFactorSecurityTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task Setup_RotatesKeyAndStampAndRejectsEnrolledUser()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            "two-factor-setup-rotation",
            "Two Factor Setup",
            "two-factor-setup@acme.test");
        var originalTokens = await Scenarios.LoginAsync(client, tenant.Slug, "two-factor-setup@acme.test");
        var userId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(originalTokens.AccessToken).Subject);
        var originalStamp = await GetSecurityStampAsync(factory, userId);
        var originalKey = await GetAuthenticatorKeyAsync(factory, userId);

        var firstSetup = await client.PostApi("/api/v1/auth/2fa/setup", null, originalTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, firstSetup.StatusCode);
        var first = await firstSetup.ReadAsync<TwoFactorSetupResponse>();
        Assert.NotEqual(originalKey, first.SharedKey);
        Assert.NotEqual(originalStamp, await GetSecurityStampAsync(factory, userId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetApi("/api/v1/users/me", originalTokens.AccessToken)).StatusCode);

        var afterFirstSetup = await Scenarios.LoginAsync(client, tenant.Slug, "two-factor-setup@acme.test");
        var secondSetup = await client.PostApi("/api/v1/auth/2fa/setup", null, afterFirstSetup.AccessToken);
        var second = await secondSetup.ReadAsync<TwoFactorSetupResponse>();
        Assert.NotEqual(first.SharedKey, second.SharedKey);

        var beforeEnable = await Scenarios.LoginAsync(client, tenant.Slug, "two-factor-setup@acme.test");
        var enable = await client.PostApi(
            "/api/v1/auth/2fa/enable",
            new TwoFactorEnableRequest(Totp.Compute(second.SharedKey, DateTimeOffset.UtcNow)),
            beforeEnable.AccessToken);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        var verified = await LoginAndSolveAsync(client, tenant.Slug, "two-factor-setup@acme.test", second.SharedKey);
        var alreadyEnabled = await client.PostApi("/api/v1/auth/2fa/setup", null, verified.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, alreadyEnabled.StatusCode);
        var problem = await alreadyEnabled.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("two_factor_already_enabled", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task SetupAndEnable_AreNotCacheable()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(
            client,
            "two-factor-no-store",
            "two-factor-no-store@acme.test");

        var setup = await client.PostApi("/api/v1/auth/2fa/setup", null, tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        AssertNoStore(setup);
        var setupBody = await setup.ReadAsync<TwoFactorSetupResponse>();

        var afterSetup = await Scenarios.LoginAsync(
            client,
            "two-factor-no-store",
            "two-factor-no-store@acme.test");
        var enable = await client.PostApi(
            "/api/v1/auth/2fa/enable",
            new TwoFactorEnableRequest(Totp.Compute(setupBody.SharedKey, DateTimeOffset.UtcNow)),
            afterSetup.AccessToken);

        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        AssertNoStore(enable);
    }

    [Theory]
    [InlineData("wrong-purpose")]
    [InlineData("tampered")]
    public async Task Challenge_WithWrongPurposeOrTamper_ReturnsUnauthorized(string challengeCase)
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();
        var enabled = await EnableTwoFactorAsync(client, $"two-factor-{challengeCase}");
        var verified = await LoginAndSolveAsync(client, enabled.Slug, enabled.Email, enabled.SharedKey);
        var challenge = await LoginChallengeAsync(client, enabled.Slug, enabled.Email);
        var token = challengeCase == "wrong-purpose" ? verified.AccessToken : challenge.Token + "x";

        var response = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(token, Totp.Compute(enabled.SharedKey, DateTimeOffset.UtcNow), null));

        await AssertInvalidChallengeAsync(response);
    }

    [Fact]
    public async Task Challenge_ExpiredWithFakeTime_ReturnsUnauthorized()
    {
        EnabledTwoFactor enabled;
        await using (var setupFactory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true"))
        using (var setupClient = setupFactory.CreateClient())
        {
            enabled = await EnableTwoFactorAsync(setupClient, "two-factor-expiry");
        }

        var current = DateTimeOffset.UtcNow;
        var now = new DateTimeOffset(
            current.AddMinutes(-10).Ticks - current.Ticks % TimeSpan.TicksPerSecond,
            TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        await using var factory = Harness.CreateFactory(
            configuration => configuration["Identity:EnableTwoFactor"] = "true",
            services => services.AddSingleton<TimeProvider>(timeProvider));
        using var client = factory.CreateClient();
        var challenge = await LoginChallengeAsync(client, enabled.Slug, enabled.Email);

        Assert.Equal(300, challenge.ExpiresIn);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(challenge.Token);
        Assert.Equal(now.AddMinutes(5).UtcDateTime, jwt.ValidTo);
        Assert.Equal(AuthClaims.TwoFactorPurpose, jwt.Claims.Single(claim => claim.Type == AuthClaims.Purpose).Value);

        var response = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(
                challenge.Token,
                Totp.Compute(enabled.SharedKey, timeProvider.GetUtcNow()),
                null));

        await AssertInvalidChallengeAsync(response);
    }

    [Fact]
    public async Task Challenge_IsRejectedAfterSecurityStampRotation()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();
        var enabled = await EnableTwoFactorAsync(client, "two-factor-stamp-rotation");
        var challenge = await LoginChallengeAsync(client, enabled.Slug, enabled.Email);
        var userId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(challenge.Token).Subject);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await dbContext.Users.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == userId);
            var slug = await dbContext.Tenants.Where(tenant => tenant.Id == user.TenantId).Select(tenant => tenant.Slug).SingleAsync();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(user.TenantId, slug);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var result = await userManager.UpdateSecurityStampAsync(user);
            Assert.True(result.Succeeded);
        }

        var response = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(
                challenge.Token,
                Totp.Compute(enabled.SharedKey, DateTimeOffset.UtcNow),
                null));

        await AssertInvalidChallengeAsync(response);
    }

    [Fact]
    public async Task DisableFailures_ShareLockoutCounterWithVerification()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();
        var enabled = await EnableTwoFactorAsync(client, "two-factor-disable-lockout");
        var verified = await LoginAndSolveAsync(client, enabled.Slug, enabled.Email, enabled.SharedKey);
        var correctCode = Totp.Compute(enabled.SharedKey, DateTimeOffset.UtcNow);
        var wrongCode = correctCode == "000000" ? "111111" : "000000";
        var challenge = await LoginChallengeAsync(client, enabled.Slug, enabled.Email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostApi(
                "/api/v1/auth/2fa/disable",
                new TwoFactorDisableRequest(wrongCode),
                verified.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var correctDisable = await client.PostApi(
            "/api/v1/auth/2fa/disable",
            new TwoFactorDisableRequest(correctCode),
            verified.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, correctDisable.StatusCode);

        var correctVerify = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(challenge.Token, correctCode, null));
        Assert.Equal(HttpStatusCode.Unauthorized, correctVerify.StatusCode);
    }

    [Fact]
    public async Task RecoveryCode_IsSingleUse()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();
        var enabled = await EnableTwoFactorAsync(client, "two-factor-recovery-single-use");
        var recoveryCode = enabled.RecoveryCodes[0];

        var firstChallenge = await LoginChallengeAsync(client, enabled.Slug, enabled.Email);
        var firstUse = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(firstChallenge.Token, null, recoveryCode));
        Assert.Equal(HttpStatusCode.OK, firstUse.StatusCode);

        var secondChallenge = await LoginChallengeAsync(client, enabled.Slug, enabled.Email);
        var secondUse = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(secondChallenge.Token, null, recoveryCode));
        Assert.Equal(HttpStatusCode.Unauthorized, secondUse.StatusCode);
        var problem = await secondUse.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("invalid_two_factor_code", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Disable_WithValidCode_RestoresPasswordLogin()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();
        var enabled = await EnableTwoFactorAsync(client, "two-factor-disable");
        var verified = await LoginAndSolveAsync(client, enabled.Slug, enabled.Email, enabled.SharedKey);

        var disabled = await client.PostApi(
            "/api/v1/auth/2fa/disable",
            new TwoFactorDisableRequest(Totp.Compute(enabled.SharedKey, DateTimeOffset.UtcNow)),
            verified.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        var plainLogin = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest(enabled.Slug, enabled.Email, Scenarios.Password));
        Assert.Equal(HttpStatusCode.OK, plainLogin.StatusCode);
        var tokens = await plainLogin.ReadAsync<AuthTokens>();
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
    }

    private static async Task<EnabledTwoFactor> EnableTwoFactorAsync(
        HttpClient client,
        string suffix,
        DateTimeOffset? now = null)
    {
        var slug = suffix;
        var email = $"{suffix}@acme.test";
        var tokens = await Scenarios.OnboardAndLoginAsync(client, slug, email);
        var setupResponse = await client.PostApi("/api/v1/auth/2fa/setup", null, tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setup = await setupResponse.ReadAsync<TwoFactorSetupResponse>();
        var afterSetup = await Scenarios.LoginAsync(client, slug, email);
        var enableResponse = await client.PostApi(
            "/api/v1/auth/2fa/enable",
            new TwoFactorEnableRequest(Totp.Compute(setup.SharedKey, now ?? DateTimeOffset.UtcNow)),
            afterSetup.AccessToken);
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
        var enable = await enableResponse.ReadAsync<TwoFactorEnabledResponse>();
        Assert.Equal(10, enable.RecoveryCodes.Count);
        Assert.All(enable.RecoveryCodes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
        return new EnabledTwoFactor(slug, email, setup.SharedKey, enable.RecoveryCodes);
    }

    private static async Task<(string Token, int ExpiresIn)> LoginChallengeAsync(
        HttpClient client,
        string slug,
        string email)
    {
        var response = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest(slug, email, Scenarios.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("twoFactorRequired").GetBoolean());
        return (
            root.GetProperty("twoFactorToken").GetString()!,
            root.GetProperty("expiresIn").GetInt32());
    }

    private static async Task<AuthTokens> LoginAndSolveAsync(HttpClient client, string slug, string email, string sharedKey)
    {
        var challenge = await LoginChallengeAsync(client, slug, email);
        var response = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(
                challenge.Token,
                Totp.Compute(sharedKey, DateTimeOffset.UtcNow),
                null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<AuthTokens>();
    }

    private static async Task<string> GetSecurityStampAsync(WebApplicationFactory<Multitenant.Api.IApiMarker> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var user = await GetUserAsync(scope, userId);
        return user.SecurityStamp!;
    }

    private static async Task<string?> GetAuthenticatorKeyAsync(
        WebApplicationFactory<Multitenant.Api.IApiMarker> factory,
        Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var user = await GetUserAsync(scope, userId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await userManager.GetAuthenticatorKeyAsync(user);
    }

    private static async Task<AppUser> GetUserAsync(IServiceScope scope, Guid userId)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await dbContext.Users.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == userId);
        var slug = await dbContext.Tenants.Where(tenant => tenant.Id == user.TenantId).Select(tenant => tenant.Slug).SingleAsync();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(user.TenantId, slug);
        return user;
    }

    private static void AssertNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("no-store", cacheControl);
        Assert.True(response.Headers.TryGetValues("Pragma", out var pragma));
        Assert.Contains("no-cache", pragma);
    }

    private static async Task AssertInvalidChallengeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("invalid_two_factor_challenge", problem.Extensions["code"]?.ToString());
    }

    private sealed record EnabledTwoFactor(
        string Slug,
        string Email,
        string SharedKey,
        IReadOnlyList<string> RecoveryCodes);
}
