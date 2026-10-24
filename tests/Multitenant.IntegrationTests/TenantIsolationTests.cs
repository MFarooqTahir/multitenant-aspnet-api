using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Multitenant.Application.Contracts;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class TenantIsolationTests : IntegrationTestBase
{
    private const string DemoPassword = "Password1!aaaa";

    [Fact]
    public async Task CrossTenantProjectAccess_Returns404()
    {
        using var client = CreateClient();

        var tenantA = await OnboardAsync(client, "iso-a", "Iso Alpha", "isoalpha@acme.test");
        var tenantB = await OnboardAsync(client, "iso-b", "Iso Beta", "isobeta@acme.test");
        var tokensA = await LoginAsync(client, tenantA.Slug, tenantA.OwnerEmail, DemoPassword);
        var tokensB = await LoginAsync(client, tenantB.Slug, tenantB.OwnerEmail, DemoPassword);

        var project = await CreateProjectAsync(client, tokensA.AccessToken, "Acme Rocket");

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/projects/{project.Id}"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokensA.AccessToken);
            var response = await client.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, $"Expected 200, got {(int)response.StatusCode}.");
        }

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/projects/{project.Id}"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokensB.AccessToken);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task CrossTenantTaskAccess_Returns404()
    {
        using var client = CreateClient();

        var tenant = await OnboardAsync(client, "iso-e", "Iso Epsilon", "isoepsilon@acme.test");
        var tokens = await LoginAsync(client, tenant.Slug, tenant.OwnerEmail, DemoPassword);
        var project = await CreateProjectAsync(client, tokens.AccessToken, "Epsilon Rocket");
        var task = await CreateTaskAsync(client, tokens.AccessToken, project.Id, "Launch epsilon");

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tasks/{task.Id}"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            var response = await client.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, $"Expected 200, got {(int)response.StatusCode}.");
        }

        var otherTokens = await LoginAsync(client, tenant.Slug, tenant.OwnerEmail, DemoPassword);
        using (var crossTenantRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tasks/{task.Id}"))
        {
            crossTenantRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                otherTokens.AccessToken);
            var response = await client.SendAsync(crossTenantRequest);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task UserLists_AreScopedToTheTenant()
    {
        using var client = CreateClient();

        var tenant = await OnboardAsync(client, "iso-f", "Iso Zeta", "isozeta@acme.test");
        var tokens = await LoginAsync(client, tenant.Slug, tenant.OwnerEmail, DemoPassword);

        var created = await CreateUserAsync(client, tokens.AccessToken, "iso-zeta-member@acme.test", TenantRoles.Member);
        var list = await ListUsersAsync(client, tokens.AccessToken);

        Assert.Contains(list.Items, user => user.Id == created.Id);
        Assert.Single(list.Items);
    }

    private static async Task<TenantResponse> OnboardAsync(
        HttpClient client,
        string slug,
        string name,
        string ownerEmail)
    {
        var response = await PostAsync(
            client,
            "/api/v1/tenants/onboard",
            new OnboardTenantRequest(slug, name, ownerEmail, "Owner", DemoPassword));

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TenantResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Onboarding response was empty.");
    }

    private static async Task<AuthTokens> LoginAsync(HttpClient client, string slug, string email, string password)
    {
        var response = await PostAsync(client, "/api/v1/auth/login", new LoginRequest(slug, email, password));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions)
            ?? throw new InvalidOperationException("Login response was empty.");
    }

    private static async Task<ProjectResponse> CreateProjectAsync(
        HttpClient client,
        string accessToken,
        string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects");
        request.Content = JsonContent(new CreateProjectRequest(name, null));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Project response was empty.");
    }

    private static async Task<TaskResponse> CreateTaskAsync(
        HttpClient client,
        string accessToken,
        Guid projectId,
        string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{projectId}/tasks");
        request.Content = JsonContent(
            new CreateTaskRequest(
                title,
                null));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Task response was empty.");
    }

    private static async Task<UserResponse> CreateUserAsync(
        HttpClient client,
        string accessToken,
        string email,
        string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        request.Content = JsonContent(new CreateUserRequest(email, "Iso Member", DemoPassword, role));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserResponse>(JsonOptions)
            ?? throw new InvalidOperationException("User response was empty.");
    }

    private static async Task<PagedResult<UserResponse>> ListUsersAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PagedResult<UserResponse>>(JsonOptions)
            ?? throw new InvalidOperationException("User list response was empty.");
    }
}
