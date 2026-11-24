using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Identity;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class TokenLifetimeTests : IntegrationTestBase
{
    public TokenLifetimeTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task AccessAndRefreshTokens_UseExactConfiguredLifetimes()
    {
        var current = DateTimeOffset.UtcNow;
        var now = new DateTimeOffset(current.Ticks - current.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        await using var factory = Harness.CreateFactory(configureServices: services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(now)));
        using var client = factory.CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            "token-lifetimes",
            "Token Lifetimes",
            "token-lifetimes@acme.test");

        var response = await client.PostApi(
            "/api/v1/auth/login",
            new Multitenant.Application.Contracts.Auth.LoginRequest(tenant.Slug, "token-lifetimes@acme.test", Scenarios.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.ReadAsync<Multitenant.Application.Contracts.Auth.AuthTokens>();
        Assert.Equal(900, tokens.ExpiresIn);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal(now.UtcDateTime, jwt.ValidFrom);
        Assert.Equal(now.AddMinutes(15).UtcDateTime, jwt.ValidTo);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshToken = await dbContext.RefreshTokens.SingleAsync(token =>
            token.TokenHash == RefreshTokenManager.Hash(tokens.RefreshToken));
        Assert.Equal(now, refreshToken.CreatedAtUtc);
        Assert.Equal(now.AddDays(30), refreshToken.ExpiresAtUtc);
    }
}
