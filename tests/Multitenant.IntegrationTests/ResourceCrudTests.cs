using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class ResourceCrudTests : IntegrationTestBase
{
    public ResourceCrudTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task Project_UpdateAndDelete_CascadeTasks()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "crud-project", "crud-project@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Original project");
        var firstTask = await CreateTaskAsync(client, tokens.AccessToken, project.Id, "First task");
        var secondTask = await CreateTaskAsync(client, tokens.AccessToken, project.Id, "Second task");

        var update = await client.PutApi(
            $"/api/v1/projects/{project.Id}",
            new UpdateProjectRequest("Updated project", "Updated description", ProjectStatus.Archived),
            tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.ReadAsync<ProjectResponse>();
        Assert.Equal(project.Id, updated.Id);
        Assert.Equal("Updated project", updated.Name);
        Assert.Equal("Updated description", updated.Description);
        Assert.Equal(ProjectStatus.Archived, updated.Status);

        var delete = await client.DeleteApi($"/api/v1/projects/{project.Id}", bearer: tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetApi($"/api/v1/projects/{project.Id}", tokens.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetApi($"/api/v1/tasks/{firstTask.Id}", tokens.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetApi($"/api/v1/tasks/{secondTask.Id}", tokens.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task TaskDelete_ByMemberIsForbiddenAndTaskRemains()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "crud-task-member", "crud-task-owner@acme.test");
        var member = await Scenarios.CreateUserAsync(
            client,
            owner.AccessToken,
            "crud-task-member@acme.test",
            TenantRoles.Member);
        var memberTokens = await Scenarios.LoginAsync(client, "crud-task-member", member.Email);
        var project = await Scenarios.CreateProjectAsync(client, owner.AccessToken, "Task project");
        var task = await CreateTaskAsync(client, memberTokens.AccessToken, project.Id, "Protected task");

        var delete = await client.DeleteApi($"/api/v1/tasks/{task.Id}", bearer: memberTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        var problem = await delete.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("forbidden", problem.Extensions["code"]?.ToString());
        var persisted = await (await client.GetApi($"/api/v1/tasks/{task.Id}", owner.AccessToken)).ReadAsync<TaskResponse>();
        Assert.Equal(task.Id, persisted.Id);
        Assert.Equal("Protected task", persisted.Title);
    }

    [Fact]
    public async Task DeletedUser_InvalidatesTokensAndSetsAssignedTaskNull()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            "crud-delete-user",
            "Delete User",
            "crud-delete-owner@acme.test");
        var owner = await Scenarios.LoginAsync(client, tenant.Slug, "crud-delete-owner@acme.test");
        var member = await Scenarios.CreateUserAsync(
            client,
            owner.AccessToken,
            "crud-delete-member@acme.test",
            TenantRoles.Member);
        var memberTokens = await Scenarios.LoginAsync(client, tenant.Slug, member.Email);
        var project = await Scenarios.CreateProjectAsync(client, owner.AccessToken, "Assigned task project");

        var create = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("Assigned task", null, WorkItemStatus.Todo, WorkItemPriority.Medium, member.Id, null),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var task = await create.ReadAsync<TaskResponse>();
        Assert.Equal(member.Id, task.AssigneeUserId);

        var delete = await client.DeleteApi($"/api/v1/users/{member.Id}", bearer: owner.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetApi("/api/v1/users/me", memberTokens.AccessToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostApi("/api/v1/auth/refresh", new RefreshTokenRequest(memberTokens.RefreshToken))).StatusCode);

        var persisted = await (await client.GetApi($"/api/v1/tasks/{task.Id}", owner.AccessToken)).ReadAsync<TaskResponse>();
        Assert.Null(persisted.AssigneeUserId);
        using var scope = Harness.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Id, tenant.Slug);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await dbContext.WorkItems.SingleAsync(item => item.Id == task.Id);
        Assert.Null(stored.AssigneeUserId);
        Assert.Null(stored.AssigneeTenantId);
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
}
