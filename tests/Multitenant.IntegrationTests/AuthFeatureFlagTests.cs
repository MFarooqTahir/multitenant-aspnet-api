using System.Net;
using Multitenant.Application.Contracts;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class AuthFeatureFlagTests : IntegrationTestBase
{
    public AuthFeatureFlagTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task UnconfirmedUsersCannotLoginWhenConfirmationIsRequired()
    {
        using var factory = Harness.CreateFactory(configuration =>
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
        using var factory = Harness.CreateFactory(configuration =>
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
}
