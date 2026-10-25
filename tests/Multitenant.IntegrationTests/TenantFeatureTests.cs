using System.Net;
using Microsoft.AspNetCore.Mvc;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class TenantFeatureTests : IntegrationTestBase
{
    public TenantFeatureTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task TaskAssignmentIsEnabledByDefault()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "flag-default", "Flag Default", "flag-default@acme.test");
        var ownerTokens = await Scenarios.LoginAsync(client, tenant.Slug, "flag-default@acme.test");
        var assignee = await CreateUserAsync(client, ownerTokens.AccessToken, "flag-default-assignee@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, ownerTokens.AccessToken, "Flag Default Project");

        var features = await (await client.GetApi("/api/v1/tenants/current/features", ownerTokens.AccessToken))
            .ReadAsync<List<TenantFeatureResponse>>();

        var assignment = Assert.Single(features, x => x.Key == TenantFeatureKeys.TaskAssignment);
        Assert.True(assignment.IsEnabled);

        var created = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("Assigned by default", null, WorkItemStatus.Todo, WorkItemPriority.Medium, assignee.Id, null),
            ownerTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task OwnerCanDisableAndReenableTaskAssignment()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "flag-toggle", "Flag Toggle", "flag-toggle@acme.test");
        var ownerTokens = await Scenarios.LoginAsync(client, tenant.Slug, "flag-toggle@acme.test");
        var assignee = await CreateUserAsync(client, ownerTokens.AccessToken, "flag-toggle-assignee@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, ownerTokens.AccessToken, "Flag Toggle Project");

        var disabled = await client.PutApi(
            "/api/v1/tenants/current/features/task_assignment",
            new { isEnabled = false },
            ownerTokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var disabledFeature = await disabled.ReadAsync<TenantFeatureResponse>();
        Assert.False(disabledFeature.IsEnabled);

        var rejected = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("Blocked assignment", null, WorkItemStatus.Todo, WorkItemPriority.Medium, assignee.Id, null),
            ownerTokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        var problem = await rejected.ReadAsync<ProblemDetails>();
        Assert.Equal("task_assignment_disabled", problem.Extensions["code"]?.ToString());

        // A task without an assignee is still allowed, so only the assignment
        // capability is gated.
        var unassigned = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("Unassigned still works", null, WorkItemStatus.Todo, WorkItemPriority.Medium, null, null),
            ownerTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Created, unassigned.StatusCode);

        var enabled = await client.PutApi(
            "/api/v1/tenants/current/features/task_assignment",
            new { isEnabled = true },
            ownerTokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);

        var allowed = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("Assignment restored", null, WorkItemStatus.Todo, WorkItemPriority.Medium, assignee.Id, null),
            ownerTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task MemberCanReadButNotChangeFeatures()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "flag-member", "Flag Member", "flag-member@acme.test");
        var ownerTokens = await Scenarios.LoginAsync(client, tenant.Slug, "flag-member@acme.test");
        await CreateUserAsync(client, ownerTokens.AccessToken, "flag-member-user@acme.test");
        var memberTokens = await Scenarios.LoginAsync(client, tenant.Slug, "flag-member-user@acme.test");

        var read = await client.GetApi("/api/v1/tenants/current/features", memberTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await client.PutApi(
            "/api/v1/tenants/current/features/task_assignment",
            new { isEnabled = false },
            memberTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task UnknownFeatureKeyReturnsNotFound()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "flag-unknown", "Flag Unknown", "flag-unknown@acme.test");
        var tokens = await Scenarios.LoginAsync(client, tenant.Slug, "flag-unknown@acme.test");

        var response = await client.PutApi(
            "/api/v1/tenants/current/features/not_a_feature",
            new { isEnabled = true },
            tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Equal("unknown_feature", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task FeatureOverridesAreScopedToOneTenant()
    {
        using var client = CreateClient();
        var alpha = await Scenarios.OnboardAsync(client, "flag-scope-a", "Flag Scope A", "flag-scope-a@acme.test");
        var beta = await Scenarios.OnboardAsync(client, "flag-scope-b", "Flag Scope B", "flag-scope-b@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, "flag-scope-a@acme.test");
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, "flag-scope-b@acme.test");

        var alphaAssignee = await CreateUserAsync(client, alphaTokens.AccessToken, "flag-scope-a-user@acme.test");
        var betaAssignee = await CreateUserAsync(client, betaTokens.AccessToken, "flag-scope-b-user@acme.test");
        var alphaProject = await Scenarios.CreateProjectAsync(client, alphaTokens.AccessToken, "Alpha Scope");
        var betaProject = await Scenarios.CreateProjectAsync(client, betaTokens.AccessToken, "Beta Scope");

        await client.PutApi(
            "/api/v1/tenants/current/features/task_assignment",
            new { isEnabled = false },
            alphaTokens.AccessToken);

        var alphaAssignment = await client.PostApi(
            $"/api/v1/projects/{alphaProject.Id}/tasks",
            new CreateTaskRequest("Blocked in alpha", null, WorkItemStatus.Todo, WorkItemPriority.Medium, alphaAssignee.Id, null),
            alphaTokens.AccessToken);

        var betaAssignment = await client.PostApi(
            $"/api/v1/projects/{betaProject.Id}/tasks",
            new CreateTaskRequest("Allowed in beta", null, WorkItemStatus.Todo, WorkItemPriority.Medium, betaAssignee.Id, null),
            betaTokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, alphaAssignment.StatusCode);
        Assert.Equal(HttpStatusCode.Created, betaAssignment.StatusCode);
    }

    [Fact]
    public async Task UpdatingATaskWithAnAssigneeHonoursTheFeature()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "flag-update", "Flag Update", "flag-update@acme.test");
        var ownerTokens = await Scenarios.LoginAsync(client, tenant.Slug, "flag-update@acme.test");
        var assignee = await CreateUserAsync(client, ownerTokens.AccessToken, "flag-update-assignee@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, ownerTokens.AccessToken, "Flag Update Project");

        var created = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("Initially unassigned", null, WorkItemStatus.Todo, WorkItemPriority.Medium, null, null),
            ownerTokens.AccessToken);

        var task = await created.ReadAsync<TaskResponse>();

        await client.PutApi(
            "/api/v1/tenants/current/features/task_assignment",
            new { isEnabled = false },
            ownerTokens.AccessToken);

        var rejected = await client.PutApi(
            $"/api/v1/tasks/{task.Id}",
            new UpdateTaskRequest("Assign me", null, WorkItemStatus.Todo, WorkItemPriority.Medium, assignee.Id, null),
            ownerTokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        var problem = await rejected.ReadAsync<ProblemDetails>();
        Assert.Equal("task_assignment_disabled", problem.Extensions["code"]?.ToString());

        var allowed = await client.PutApi(
            $"/api/v1/tasks/{task.Id}",
            new UpdateTaskRequest("Still editable", null, WorkItemStatus.Done, WorkItemPriority.Low, null, null),
            ownerTokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    private static async Task<UserResponse> CreateUserAsync(
        HttpClient client,
        string accessToken,
        string email)
    {
        var response = await client.PostApi(
            "/api/v1/users",
            new CreateUserRequest(email, "Feature User", Scenarios.Password, TenantRoles.Member),
            accessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<UserResponse>();
    }
}
