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
        var betaUser = await Scenarios.CreateUserAsync(client, betaTokens.AccessToken, "theta-outsider@acme.test");

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

        var created = await Scenarios.CreateUserAsync(client, tokens.AccessToken, "iso-zeta-member@acme.test");
        var list = await ListUsersAsync(client, tokens.AccessToken);

        Assert.Contains(list.Items, user => user.Id == created.Id);
        Assert.Equal(2, list.Items.Count); // the owner plus the member created above
    }

    [Fact]
    public async Task CrossTenantProjectTaskListReturns404()
    {
        using var client = CreateClient();

        var alpha = await OnboardAsync(client, "iso-j", "Iso Kappa", "isokappa@acme.test");
        var beta = await OnboardAsync(client, "iso-k", "Iso Lambda", "isolambda@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, alpha.OwnerEmail);
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, beta.OwnerEmail);

        var project = await Scenarios.CreateProjectAsync(client, alphaTokens.AccessToken, "Kappa Rocket");

        var own = await client.GetApi($"/api/v1/projects/{project.Id}/tasks", alphaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        var crossTenant = await client.GetApi(
            $"/api/v1/projects/{project.Id}/tasks",
            betaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        var unknown = await client.GetApi(
            $"/api/v1/projects/{Guid.NewGuid()}/tasks",
            alphaTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("patch")]
    [InlineData("delete")]
    public async Task CrossTenantUserGetPatchOrDelete_ReturnsNotFoundAndLeavesStateUnchanged(string operation)
    {
        using var client = CreateClient();
        var alpha = await OnboardAsync(client, $"iso-user-a-{operation}", "Alpha", $"iso-user-a-{operation}@acme.test");
        var beta = await OnboardAsync(client, $"iso-user-b-{operation}", "Beta", $"iso-user-b-{operation}@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, alpha.OwnerEmail);
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, beta.OwnerEmail);
        var target = await Scenarios.CreateUserAsync(
            client,
            betaTokens.AccessToken,
            $"iso-user-target-{operation}@acme.test");
        var before = await (await client.GetApi($"/api/v1/users/{target.Id}", betaTokens.AccessToken)).ReadAsync<UserResponse>();
        var alphaCountBefore = (await ListUsersAsync(client, alphaTokens.AccessToken)).TotalCount;

        var response = operation switch
        {
            "get" => await client.GetApi($"/api/v1/users/{target.Id}", alphaTokens.AccessToken),
            "patch" => await client.PatchApi(
                $"/api/v1/users/{target.Id}/role",
                new UpdateUserRoleRequest(TenantRoles.Admin),
                alphaTokens.AccessToken),
            _ => await client.DeleteApi($"/api/v1/users/{target.Id}", bearer: alphaTokens.AccessToken),
        };

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("not_found", problem.Extensions["code"]?.ToString());

        var after = await (await client.GetApi($"/api/v1/users/{target.Id}", betaTokens.AccessToken)).ReadAsync<UserResponse>();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.DisplayName, after.DisplayName);
        Assert.Equal(TenantRoles.Member, after.Role);
        Assert.True(after.IsActive);
        Assert.Equal(2, (await ListUsersAsync(client, betaTokens.AccessToken)).TotalCount);
        Assert.Equal(alphaCountBefore, (await ListUsersAsync(client, alphaTokens.AccessToken)).TotalCount);
    }

    [Fact]
    public async Task TaskCreationUnderForeignProject_ReturnsNotFoundWithoutIncreasingTaskCount()
    {
        using var client = CreateClient();
        var alpha = await OnboardAsync(client, "iso-project-task-a", "Alpha", "iso-project-task-a@acme.test");
        var beta = await OnboardAsync(client, "iso-project-task-b", "Beta", "iso-project-task-b@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, alpha.OwnerEmail);
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, beta.OwnerEmail);
        var foreignProject = await Scenarios.CreateProjectAsync(client, betaTokens.AccessToken, "Foreign project");
        var countBefore = (await (await client.GetApi("/api/v1/tasks", alphaTokens.AccessToken)).ReadAsync<PagedResult<TaskResponse>>()).TotalCount;

        var response = await client.PostApi(
            $"/api/v1/projects/{foreignProject.Id}/tasks",
            new CreateTaskRequest("Foreign project task", null),
            alphaTokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("not_found", problem.Extensions["code"]?.ToString());
        var countAfter = (await (await client.GetApi("/api/v1/tasks", alphaTokens.AccessToken)).ReadAsync<PagedResult<TaskResponse>>()).TotalCount;
        Assert.Equal(countBefore, countAfter);
        var foreignTasks = await (await client.GetApi($"/api/v1/projects/{foreignProject.Id}/tasks", betaTokens.AccessToken)).ReadAsync<PagedResult<TaskResponse>>();
        Assert.Empty(foreignTasks.Items);
    }

    [Fact]
    public async Task TaskUpdateWithForeignAssignee_ReturnsNotFoundAndLeavesTaskUnchanged()
    {
        using var client = CreateClient();
        var alpha = await OnboardAsync(client, "iso-assignee-a", "Alpha", "iso-assignee-a@acme.test");
        var beta = await OnboardAsync(client, "iso-assignee-b", "Beta", "iso-assignee-b@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, alpha.OwnerEmail);
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, beta.OwnerEmail);
        var project = await Scenarios.CreateProjectAsync(client, alphaTokens.AccessToken, "Owned project");
        var task = await CreateTaskAsync(client, alphaTokens.AccessToken, project.Id, "Original task");
        var foreignUser = await Scenarios.CreateUserAsync(
            client,
            betaTokens.AccessToken,
            "iso-assignee-foreign@acme.test");

        var response = await client.PutApi(
            $"/api/v1/tasks/{task.Id}",
            new UpdateTaskRequest("Hijacked", null, WorkItemStatus.Done, WorkItemPriority.High, foreignUser.Id, null),
            alphaTokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("not_found", problem.Extensions["code"]?.ToString());
        var after = await (await client.GetApi($"/api/v1/tasks/{task.Id}", alphaTokens.AccessToken)).ReadAsync<TaskResponse>();
        Assert.Equal("Original task", after.Title);
        Assert.Equal(WorkItemStatus.Todo, after.Status);
        Assert.Equal(WorkItemPriority.Medium, after.Priority);
        Assert.Null(after.AssigneeUserId);
        Assert.Single((await (await client.GetApi("/api/v1/tasks", alphaTokens.AccessToken)).ReadAsync<PagedResult<TaskResponse>>()).Items, item => item.Id == task.Id);
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

    private static async Task<PagedResult<UserResponse>> ListUsersAsync(
        HttpClient client,
        string accessToken)
    {
        var response = await client.GetApi("/api/v1/users", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<UserResponse>>();
    }
}
