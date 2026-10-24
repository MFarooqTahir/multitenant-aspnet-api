using System.Net;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class TenantIsolationTests : IntegrationTestBase
{
    public TenantIsolationTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task CrossTenantProjectAccessReturns404()
    {
        using var client = CreateClient();

        var alpha = await OnboardAsync(client, "iso-a", "Iso Alpha", "isoalpha@acme.test");
        var beta = await OnboardAsync(client, "iso-b", "Iso Beta", "isobeta@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, alpha.OwnerEmail);
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, beta.OwnerEmail);

        var project = await Scenarios.CreateProjectAsync(client, alphaTokens.AccessToken, "Alpha Rocket");

        var ownResponse = await client.GetApi($"/api/v1/projects/{project.Id}", alphaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

        var crossTenantResponse = await client.GetApi($"/api/v1/projects/{project.Id}", betaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);

        var crossTenantUpdate = await client.PutApi(
            $"/api/v1/projects/{project.Id}",
            new UpdateProjectRequest("Hijacked", null, ProjectStatus.Archived),
            betaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantUpdate.StatusCode);

        var crossTenantDelete = await client.DeleteApi(
            $"/api/v1/projects/{project.Id}",
            bearer: betaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantDelete.StatusCode);
    }

    [Fact]
    public async Task CrossTenantTaskAccessReturns404()
    {
        using var client = CreateClient();

        var alpha = await OnboardAsync(client, "iso-e", "Iso Epsilon", "isoepsilon@acme.test");
        var beta = await OnboardAsync(client, "iso-g", "Iso Eta", "isoeta@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, alpha.OwnerEmail);
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, beta.OwnerEmail);

        var project = await Scenarios.CreateProjectAsync(client, alphaTokens.AccessToken, "Epsilon Rocket");
        var task = await CreateTaskAsync(client, alphaTokens.AccessToken, project.Id, "Launch epsilon");

        var ownResponse = await client.GetApi($"/api/v1/tasks/{task.Id}", alphaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

        var crossTenantResponse = await client.GetApi($"/api/v1/tasks/{task.Id}", betaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);

        var crossTenantUpdate = await client.PutApi(
            $"/api/v1/tasks/{task.Id}",
            new UpdateTaskRequest("Hijacked", null, WorkItemStatus.Done, WorkItemPriority.Low, null, null),
            betaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantUpdate.StatusCode);

        var crossTenantDelete = await client.DeleteApi($"/api/v1/tasks/{task.Id}", bearer: betaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantDelete.StatusCode);
    }

    [Fact]
    public async Task TaskCreationRejectsAssigneeFromAnotherTenant()
    {
        using var client = CreateClient();

        var alpha = await OnboardAsync(client, "iso-h", "Iso Theta", "isotheta@acme.test");
        var beta = await OnboardAsync(client, "iso-i", "Iso Iota", "isoiota@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, alpha.OwnerEmail);
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, beta.OwnerEmail);

        var project = await Scenarios.CreateProjectAsync(client, alphaTokens.AccessToken, "Theta Rocket");
        var betaUser = await CreateUserAsync(client, betaTokens.AccessToken, "theta-outsider@acme.test");

        var response = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest(
                "Cross tenant assignment",
                null,
                WorkItemStatus.Todo,
                WorkItemPriority.Medium,
                betaUser.Id,
                null),
            alphaTokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UserListsAreScopedToTheTenant()
    {
        using var client = CreateClient();

        var tenant = await OnboardAsync(client, "iso-f", "Iso Zeta", "isozeta@acme.test");
        var tokens = await Scenarios.LoginAsync(client, tenant.Slug, tenant.OwnerEmail);

        var created = await CreateUserAsync(client, tokens.AccessToken, "iso-zeta-member@acme.test");
        var list = await ListUsersAsync(client, tokens.AccessToken);

        Assert.Contains(list.Items, user => user.Id == created.Id);
        Assert.Equal(2, list.Items.Count); // the owner plus the member created above
    }

    private static async Task<OnboardedTenant> OnboardAsync(
        HttpClient client,
        string slug,
        string name,
        string ownerEmail)
    {
        var tenant = await Scenarios.OnboardAsync(client, slug, name, ownerEmail);
        return new OnboardedTenant(tenant.Slug, ownerEmail);
    }

    private static async Task<TaskResponse> CreateTaskAsync(
        HttpClient client,
        string accessToken,
        Guid projectId,
        string title)
    {
        var response = await client.PostApi(
            $"/api/v1/projects/{projectId}/tasks",
            new CreateTaskRequest(title, null, WorkItemStatus.Todo, WorkItemPriority.Medium, null, null),
            accessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<TaskResponse>();
    }

    private static async Task<UserResponse> CreateUserAsync(
        HttpClient client,
        string accessToken,
        string email)
    {
        var response = await client.PostApi(
            "/api/v1/users",
            new CreateUserRequest(email, "Iso Member", Scenarios.Password, TenantRoles.Member),
            accessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<UserResponse>();
    }

    private static async Task<PagedResult<UserResponse>> ListUsersAsync(
        HttpClient client,
        string accessToken)
    {
        var response = await client.GetApi("/api/v1/users", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<UserResponse>>();
    }
}
