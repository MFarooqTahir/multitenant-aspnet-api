using System.Net;
using Multitenant.Application.Contracts.Auth;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class AuthFeatureFlagTests : IntegrationTestBase
{
    public AuthFeatureFlagTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task UnconfirmedUsersCannotLoginWhenConfirmationIsRequired()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:RequireConfirmedEmail"] = "true");
        using var client = factory.CreateClient();

        await Scenarios.OnboardAsync(client, "flag-email", "Flag Email", "unconfirmed@acme.test");

        var blocked = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("flag-email", "unconfirmed@acme.test", Scenarios.Password));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        var problem = await blocked.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("email_not_confirmed", problem.Extensions["code"]?.ToString());

        // Seeded demo accounts are pre-confirmed, so they still work.
        var demo = await Scenarios.LoginAsync(client, "acme", "owner@acme.test", "Demo123!Passw0rd");
        Assert.False(string.IsNullOrWhiteSpace(demo.AccessToken));
    }

    [Fact]
    public async Task RefreshIsRejectedWhenConfirmationBecomesRequired()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "flag-refresh", "refresh@acme.test");

        // Sessions minted while confirmation was optional must not survive the
        // flag being switched on.
        await using var strict = Harness.CreateFactory(configuration =>
            configuration["Identity:RequireConfirmedEmail"] = "true");
        using var strictClient = strict.CreateClient();

        var refreshed = await strictClient.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.Forbidden, refreshed.StatusCode);
        var problem = await refreshed.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("email_not_confirmed", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task TwoFactorEndpointsAreHiddenByDefault()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "flag-2fa-off", "off@acme.test");

        var setup = await client.PostApi("/api/v1/auth/2fa/setup", null, tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, setup.StatusCode);

        var problem = await setup.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("feature_disabled", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task TwoFactorCanBeEnabledAndIsRequiredAtLogin()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();

        var tokens = await Scenarios.OnboardAndLoginAsync(client, "flag-2fa-on", "on@acme.test");

        var setupResponse = await client.PostApi("/api/v1/auth/2fa/setup", null, tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setup = await setupResponse.ReadAsync<TwoFactorSetupResponse>();
        Assert.False(string.IsNullOrWhiteSpace(setup.SharedKey));

        // Setup rotates the security stamp (security-sensitive change), so the
        // client re-authenticates before confirming the first code.
        var postSetupTokens = await Scenarios.LoginAsync(client, "flag-2fa-on", "on@acme.test", Scenarios.Password);

        var enable = await client.PostApi(
            "/api/v1/auth/2fa/enable",
            new TwoFactorEnableRequest(Totp.Compute(setup.SharedKey, DateTimeOffset.UtcNow)),
            postSetupTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var enabled = await enable.ReadAsync<TwoFactorEnabledResponse>();
        Assert.NotEmpty(enabled.RecoveryCodes);

        var loginResponse = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("flag-2fa-on", "on@acme.test", Scenarios.Password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var loginJson = System.Text.Json.JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.True(loginJson.RootElement.GetProperty("twoFactorRequired").GetBoolean());
        var challenge = loginJson.RootElement.GetProperty("twoFactorToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(challenge));

        var verify = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(
                challenge!,
                Totp.Compute(setup.SharedKey, DateTimeOffset.UtcNow),
                null));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var verified = await verify.ReadAsync<AuthTokens>();
        Assert.False(string.IsNullOrWhiteSpace(verified.AccessToken));
    }

    [Fact]
    public async Task TwoFactorCanBeDisabledAndRecoveryCodesAreSingleUse()
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();

        var tokens = await Scenarios.OnboardAndLoginAsync(client, "flag-2fa-recovery", "recovery@acme.test");

        var setupResponse = await client.PostApi("/api/v1/auth/2fa/setup", null, tokens.AccessToken);
        var setup = await setupResponse.ReadAsync<TwoFactorSetupResponse>();

        var postSetupTokens = await Scenarios.LoginAsync(
            client,
            "flag-2fa-recovery",
            "recovery@acme.test",
            Scenarios.Password);

        var enable = await client.PostApi(
            "/api/v1/auth/2fa/enable",
            new TwoFactorEnableRequest(Totp.Compute(setup.SharedKey, DateTimeOffset.UtcNow)),
            postSetupTokens.AccessToken);
        var enabled = await enable.ReadAsync<TwoFactorEnabledResponse>();
        Assert.NotEmpty(enabled.RecoveryCodes);

        var recoveryCode = enabled.RecoveryCodes[0];

        // A recovery code works once.
        var firstUse = await VerifyWithRecoveryCodeAsync(client, recoveryCode);
        Assert.Equal(HttpStatusCode.OK, firstUse.StatusCode);

        // The same code must be rejected afterwards.
        var secondUse = await VerifyWithRecoveryCodeAsync(client, recoveryCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondUse.StatusCode);

        // Log in with TOTP and disable the second factor.
        var latestTokens = await LoginAndSolveChallengeAsync(client, setup.SharedKey);

        var disable = await client.PostApi(
            "/api/v1/auth/2fa/disable",
            new TwoFactorDisableRequest(Totp.Compute(setup.SharedKey, DateTimeOffset.UtcNow)),
            latestTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        // Password login is enough again.
        var plainLogin = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("flag-2fa-recovery", "recovery@acme.test", Scenarios.Password));

        Assert.Equal(HttpStatusCode.OK, plainLogin.StatusCode);
        var plainTokens = await plainLogin.ReadAsync<AuthTokens>();
        Assert.False(string.IsNullOrWhiteSpace(plainTokens.AccessToken));
    }

    private static async Task<HttpResponseMessage> VerifyWithRecoveryCodeAsync(
        HttpClient client,
        string recoveryCode)
    {
        var login = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("flag-2fa-recovery", "recovery@acme.test", Scenarios.Password));

        using var json = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var challenge = json.RootElement.GetProperty("twoFactorToken").GetString();

        return await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(challenge!, null, recoveryCode));
    }

    private static async Task<AuthTokens> LoginAndSolveChallengeAsync(HttpClient client, string sharedKey)
    {
        var login = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("flag-2fa-recovery", "recovery@acme.test", Scenarios.Password));

        using var json = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var challenge = json.RootElement.GetProperty("twoFactorToken").GetString();

        var verify = await client.PostApi(
            "/api/v1/auth/2fa/verify",
            new TwoFactorVerifyRequest(
                challenge!,
                Totp.Compute(sharedKey, DateTimeOffset.UtcNow),
                null));

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        return await verify.ReadAsync<AuthTokens>();
    }
}
