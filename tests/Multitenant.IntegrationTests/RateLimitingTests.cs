using System.Net;
using Multitenant.Application.Contracts;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class RateLimitingTests : IntegrationTestBase
{
    public RateLimitingTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task AuthEndpointsAreLimitedPerIp()
    {
        using var factory = Harness.CreateFactory(configuration =>
        {
            configuration["RateLimiting:Auth:PermitLimit"] = "2";
            configuration["RateLimiting:Auth:WindowSeconds"] = "60";
        });
        using var client = factory.CreateClient();

        var login = new LoginRequest("acme", "owner@acme.test", "Demo123!Passw0rd");

        var first = await client.PostApi("/api/v1/auth/login", login);
        var second = await client.PostApi("/api/v1/auth/login", login);
        var third = await client.PostApi("/api/v1/auth/login", login);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);

        var problem = await third.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("rate_limited", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task AuthenticatedTrafficIsLimitedPerTenant()
    {
        using var factory = Harness.CreateFactory(configuration =>
        {
            configuration["RateLimiting:Tenant:PermitLimit"] = "3";
            configuration["RateLimiting:Tenant:WindowSeconds"] = "60";
            configuration["RateLimiting:Auth:PermitLimit"] = "100";
        });
        using var client = factory.CreateClient();

        var tokens = await Scenarios.OnboardAndLoginAsync(client, "rate-tenant", "rate@acme.test");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
