using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Multitenant.Api.Configuration;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class PlatformTests : IntegrationTestBase
{
    public PlatformTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task HealthEndpointsReportLiveAndReady()
    {
        using var client = CreateClient();

        var live = await client.GetApi("/health/live");
        var ready = await client.GetApi("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocumentIsServedPerVersion()
    {
        using var client = CreateClient();

        var response = await client.GetApi("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/auth/login", out _));

        var scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");

        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }

    [Fact]
    public async Task OnboardingRejectsAnInvalidSlugWithValidationProblem()
    {
        using var client = CreateClient();

        var response = await client.PostApi(
            "/api/v1/tenants/onboard",
            new OnboardTenantRequest("Not A Slug!", "Bad Tenant", "bad@acme.test", "Owner", Scenarios.Password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.Equal("validation_failed", problem.Extensions["code"]?.ToString());
        Assert.Contains("slug", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnboardingRejectsDuplicateSlugs()
    {
        using var client = CreateClient();

        await Scenarios.OnboardAsync(client, "dup-slug", "Duplicate", "duplicate-one@acme.test");

        var duplicate = await client.PostApi(
            "/api/v1/tenants/onboard",
            new OnboardTenantRequest("dup-slug", "Duplicate", "duplicate-two@acme.test", "Owner", Scenarios.Password));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var problem = await duplicate.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("slug_taken", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task RequestsWithoutATokenAreUnauthorized()
    {
        using var client = CreateClient();

        var response = await client.GetApi("/api/v1/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProtectedResourceRequiresAuthentication()
    {
        using var client = CreateClient();

        var response = await client.GetApi("/api/v1/projects/00000000-0000-0000-0000-000000000001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownRoutesReturnNotFoundProblem()
    {
        using var client = CreateClient();

        var response = await client.GetApi("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("not_found", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task WrongMethodsReturnMethodNotAllowedProblem()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "platform-method", "method@acme.test");

        var response = await client.DeleteApi("/api/v1/projects", bearer: tokens.AccessToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("method_not_allowed", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task UnsupportedApiVersionsReturnNotFound()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "platform-version", "version@acme.test");

        var response = await client.GetApi("/api/v2/projects", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void InvalidEmailProviderFailsStartup()
    {
        using var factory = Harness.CreateFactory(configuration =>
            configuration["Email:Provider"] = "CarrierPigeon");

        Assert.ThrowsAny<Exception>(factory.CreateClient);
    }

    [Fact]
    public void ShortSigningKeyFailsStartup()
    {
        using var factory = Harness.CreateFactory(configuration =>
            configuration["Jwt:SigningKey"] = "too-short");

        Assert.ThrowsAny<Exception>(factory.CreateClient);
    }

    [Fact]
    public void ForwardedProxyConfigurationIsValidated()
    {
        var invalidProxy = new ForwardedHeadersSettings { KnownProxies = ["not-an-ip"] };
        Assert.Throws<InvalidOperationException>(
            () => invalidProxy.Apply(new ForwardedHeadersOptions()));

        var invalidNetwork = new ForwardedHeadersSettings { KnownNetworks = ["10.0.0.0/99"] };
        Assert.Throws<InvalidOperationException>(
            () => invalidNetwork.Apply(new ForwardedHeadersOptions()));

        var valid = new ForwardedHeadersSettings
        {
            KnownProxies = ["10.0.0.1"],
            KnownNetworks = ["10.0.0.0/8", "2001:db8::/32"],
        };

        valid.Apply(new ForwardedHeadersOptions());
    }

    [Fact]
    public async Task CorrelationIdIsEchoed()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "test-correlation-id");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "test-correlation-id",
            response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task TenantScopedRolesProtectProjectWrites()
    {
        using var client = CreateClient();
        await Scenarios.OnboardAndLoginAsync(client, "platform-roles", "platform-owner@acme.test");

        var register = await client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest("platform-roles", "platform-member@acme.test", "Platform Member", Scenarios.Password));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var member = await Scenarios.LoginAsync(
            client,
            "platform-roles",
            "platform-member@acme.test",
            Scenarios.Password);

        var create = await client.PostApi(
            "/api/v1/projects",
            new CreateProjectRequest("Member project", null),
            member.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }
}
