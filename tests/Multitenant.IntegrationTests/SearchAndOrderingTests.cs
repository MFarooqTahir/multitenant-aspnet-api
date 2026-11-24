using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class SearchAndOrderingTests : IntegrationTestBase
{
    public SearchAndOrderingTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task Lists_ClampPageAboveTenThousand()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "page-cap", "page-cap@acme.test");

        var capped = await ReadProjectsAsync(client, tokens.AccessToken, "/api/v1/projects?page=10001&pageSize=5");
        var minimum = await ReadProjectsAsync(client, tokens.AccessToken, "/api/v1/projects?page=0&pageSize=5");

        Assert.Equal(10_000, capped.Page);
        Assert.Equal(5, capped.PageSize);
        Assert.Empty(capped.Items);
        Assert.Equal(1, minimum.Page);
    }

    [Fact]
    public async Task TaskLists_FilterDueAfterProjectAndAssigneeWithInclusiveBoundary()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "task-filter-matrix", "task-filter@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Filter project");
        var otherProject = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Other filter project");
        var assignee = await Scenarios.CreateUserAsync(client, tokens.AccessToken, "task-filter-assignee@acme.test");
        var first = await CreateTaskAsync(client, tokens.AccessToken, project.Id, "First dated", new DateOnly(2026, 11, 1), null);
        var boundary = await CreateTaskAsync(client, tokens.AccessToken, project.Id, "Boundary dated", new DateOnly(2026, 11, 2), assignee.Id);
        var last = await CreateTaskAsync(client, tokens.AccessToken, project.Id, "Last dated", new DateOnly(2026, 11, 3), null);
        await CreateTaskAsync(client, tokens.AccessToken, otherProject.Id, "Other project dated", new DateOnly(2026, 11, 4), null);

        var dueAfter = await ReadTasksAsync(client, tokens.AccessToken, $"/api/v1/tasks?dueAfter=2026-11-02&search=dated&projectId={project.Id}&pageSize=100");
        Assert.Equal(2, dueAfter.Items.Count);
        Assert.Contains(dueAfter.Items, task => task.Id == boundary.Id);
        Assert.Contains(dueAfter.Items, task => task.Id == last.Id);

        var forProject = await ReadTasksAsync(client, tokens.AccessToken, $"/api/v1/tasks?projectId={project.Id}&pageSize=100");
        Assert.Equal(3, forProject.Items.Count);
        Assert.Contains(forProject.Items, task => task.Id == first.Id);
        Assert.Contains(forProject.Items, task => task.Id == boundary.Id);
        Assert.Contains(forProject.Items, task => task.Id == last.Id);

        var forAssignee = await ReadTasksAsync(client, tokens.AccessToken, $"/api/v1/tasks?assigneeUserId={assignee.Id}&pageSize=100");
        Assert.Single(forAssignee.Items);
        Assert.Equal(boundary.Id, forAssignee.Items[0].Id);
    }

    [Fact]
    public async Task LikeSearch_TreatsWildcardsLiterallyAcrossResources()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "search-wildcards", "search-wildcards@acme.test");

        await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "100% Ready");
        await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "100X Ready");
        await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "under_score");
        await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "underXscore");
        await Scenarios.CreateProjectAsync(client, tokens.AccessToken, @"back\slash");
        await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "backslash");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Wildcard tasks");

        await CreateTaskAsync(client, tokens.AccessToken, project.Id, "100% Ready", null, null);
        await CreateTaskAsync(client, tokens.AccessToken, project.Id, "100X Ready", null, null);
        await CreateTaskAsync(client, tokens.AccessToken, project.Id, "under_score", null, null);
        await CreateTaskAsync(client, tokens.AccessToken, project.Id, "underXscore", null, null);
        await CreateTaskAsync(client, tokens.AccessToken, project.Id, @"back\slash", null, null);
        await CreateTaskAsync(client, tokens.AccessToken, project.Id, "backslash", null, null);

        await Scenarios.CreateUserAsync(client, tokens.AccessToken, "wildcard-percent@acme.test", displayName: "100% User");
        await Scenarios.CreateUserAsync(client, tokens.AccessToken, "wildcard-other@acme.test", displayName: "100X User");
        await Scenarios.CreateUserAsync(client, tokens.AccessToken, "wildcard-underscore@acme.test", displayName: "under_score User");
        await Scenarios.CreateUserAsync(client, tokens.AccessToken, "wildcard-x@acme.test", displayName: "underXscore User");
        await Scenarios.CreateUserAsync(client, tokens.AccessToken, "wildcard-backslash@acme.test", displayName: @"back\slash User");
        await Scenarios.CreateUserAsync(client, tokens.AccessToken, "wildcard-plain@acme.test", displayName: "backslash User");

        await AssertLiteralMatchesAsync(client, tokens.AccessToken, "/api/v1/projects", "%", "100% Ready");
        await AssertLiteralMatchesAsync(client, tokens.AccessToken, "/api/v1/projects", "_", "under_score");
        await AssertLiteralMatchesAsync(client, tokens.AccessToken, "/api/v1/projects", @"\", @"back\slash");
        await AssertLiteralMatchesAsync(client, tokens.AccessToken, "/api/v1/tasks", "%", "100% Ready");
        await AssertLiteralMatchesAsync(client, tokens.AccessToken, "/api/v1/tasks", "_", "under_score");
        await AssertLiteralMatchesAsync(client, tokens.AccessToken, "/api/v1/tasks", @"\", @"back\slash");
        await AssertUserLiteralMatchAsync(client, tokens.AccessToken, "%", "100% User");
        await AssertUserLiteralMatchAsync(client, tokens.AccessToken, "_", "under_score User");
        await AssertUserLiteralMatchAsync(client, tokens.AccessToken, @"\", @"back\slash User");
    }

    [Fact]
    public async Task Sorts_WithEqualKeys_UseDeterministicIdTieBreaker()
    {
        var now = DateTimeOffset.UtcNow;
        await using var factory = Harness.CreateFactory(configureServices: services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(now)));
        using var client = factory.CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "sort-ties", "sort-ties@acme.test");

        var projects = new List<ProjectResponse>();
        var tasks = new List<TaskResponse>();
        var users = new List<Multitenant.Application.Contracts.Users.UserResponse>();
        for (var index = 0; index < 3; index++)
        {
            projects.Add(await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Tie Project"));
            users.Add(await Scenarios.CreateUserAsync(
                client,
                tokens.AccessToken,
                $"sort-tie-user-{index}@acme.test",
                displayName: "TieUser"));
        }

        var taskProject = projects[0];
        for (var index = 0; index < 3; index++)
        {
            tasks.Add(await CreateTaskAsync(client, tokens.AccessToken, taskProject.Id, "Tie Task", null, null));
        }

        var ascendingProjects = await ReadProjectsAsync(client, tokens.AccessToken, "/api/v1/projects?search=Tie&sort=name");
        var descendingProjects = await ReadProjectsAsync(client, tokens.AccessToken, "/api/v1/projects?search=Tie&sort=-name");
        Assert.Equal(projects.Select(item => item.Id).Order(), ascendingProjects.Items.Select(item => item.Id));
        Assert.Equal(projects.Select(item => item.Id).OrderDescending(), descendingProjects.Items.Select(item => item.Id));

        var ascendingTasks = await ReadTasksAsync(client, tokens.AccessToken, "/api/v1/tasks?search=Tie&sort=title");
        var descendingTasks = await ReadTasksAsync(client, tokens.AccessToken, "/api/v1/tasks?search=Tie&sort=-title");
        Assert.Equal(tasks.Select(item => item.Id).Order(), ascendingTasks.Items.Select(item => item.Id));
        Assert.Equal(tasks.Select(item => item.Id).OrderDescending(), descendingTasks.Items.Select(item => item.Id));

        var ascendingUsers = await ReadUsersAsync(client, tokens.AccessToken, "/api/v1/users?search=TieUser&sort=displayname");
        var descendingUsers = await ReadUsersAsync(client, tokens.AccessToken, "/api/v1/users?search=TieUser&sort=-displayname");
        Assert.Equal(users.Select(item => item.Id).Order(), ascendingUsers.Items.Select(item => item.Id));
        Assert.Equal(users.Select(item => item.Id).OrderDescending(), descendingUsers.Items.Select(item => item.Id));
    }

    private static async Task AssertLiteralMatchesAsync(
        HttpClient client,
        string accessToken,
        string resource,
        string search,
        string expectedName)
    {
        var response = await client.GetApi($"{resource}?search={Uri.EscapeDataString(search)}", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var items = json.RootElement.GetProperty("items");
        Assert.Single(items.EnumerateArray());
        var nameProperty = resource.EndsWith("projects", StringComparison.Ordinal) ? "name" : "title";
        Assert.Equal(expectedName, items[0].GetProperty(nameProperty).GetString());
    }

    private static async Task AssertUserLiteralMatchAsync(
        HttpClient client,
        string accessToken,
        string search,
        string expectedDisplayName)
    {
        var page = await ReadUsersAsync(client, accessToken, $"/api/v1/users?search={Uri.EscapeDataString(search)}");
        Assert.Single(page.Items);
        Assert.Equal(expectedDisplayName, page.Items[0].DisplayName);
    }

    private static async Task<PagedResult<ProjectResponse>> ReadProjectsAsync(
        HttpClient client,
        string accessToken,
        string path)
    {
        var response = await client.GetApi(path, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<ProjectResponse>>();
    }

    private static async Task<PagedResult<TaskResponse>> ReadTasksAsync(
        HttpClient client,
        string accessToken,
        string path)
    {
        var response = await client.GetApi(path, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<TaskResponse>>();
    }

    private static async Task<PagedResult<Multitenant.Application.Contracts.Users.UserResponse>> ReadUsersAsync(
        HttpClient client,
        string accessToken,
        string path)
    {
        var response = await client.GetApi(path, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<Multitenant.Application.Contracts.Users.UserResponse>>();
    }

    private static async Task<TaskResponse> CreateTaskAsync(
        HttpClient client,
        string accessToken,
        Guid projectId,
        string title,
        DateOnly? dueDate,
        Guid? assigneeId)
    {
        var response = await client.PostApi(
            $"/api/v1/projects/{projectId}/tasks",
            new CreateTaskRequest(title, null, WorkItemStatus.Todo, WorkItemPriority.Medium, assigneeId, dueDate),
            accessToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<TaskResponse>();
    }
}
