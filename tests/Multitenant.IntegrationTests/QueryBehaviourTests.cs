using System.Net;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class QueryBehaviourTests : IntegrationTestBase
{
    public QueryBehaviourTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task ProjectListsSupportPagingSortingAndFiltering()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "query-projects", "query-projects@acme.test");

        await CreateProjectAsync(client, tokens.AccessToken, "Alpha", ProjectStatus.Active);
        await CreateProjectAsync(client, tokens.AccessToken, "Beta", ProjectStatus.Active);
        await CreateProjectAsync(client, tokens.AccessToken, "Gamma", ProjectStatus.Archived);

        var firstPage = await client.GetApi(
            "/api/v1/projects?page=1&pageSize=2&sort=name",
            tokens.AccessToken);
        var page = await firstPage.ReadAsync<PagedResult<ProjectResponse>>();

        Assert.Equal(2, page.PageSize);
        Assert.Equal(4, page.TotalCount); // three explicit projects + the onboarding starter project
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(["Alpha", "Beta"], page.Items.Select(item => item.Name));

        var secondPage = await client.GetApi(
            "/api/v1/projects?page=2&pageSize=2&sort=name",
            tokens.AccessToken);
        var remaining = await secondPage.ReadAsync<PagedResult<ProjectResponse>>();
        Assert.Equal(2, remaining.Items.Count);
        Assert.Equal("Gamma", remaining.Items[0].Name);
        Assert.Contains(remaining.Items, item => item.Name == "Getting started");

        var search = await client.GetApi("/api/v1/projects?search=bet", tokens.AccessToken);
        var matches = await search.ReadAsync<PagedResult<ProjectResponse>>();
        Assert.Single(matches.Items);
        Assert.Equal("Beta", matches.Items[0].Name);

        var archived = await client.GetApi("/api/v1/projects?status=Archived", tokens.AccessToken);
        var archivedPage = await archived.ReadAsync<PagedResult<ProjectResponse>>();
        Assert.Single(archivedPage.Items);
        Assert.Equal("Gamma", archivedPage.Items[0].Name);

        // Unknown sort fields fall back to the default instead of failing.
        var unknownSort = await client.GetApi("/api/v1/projects?sort=notAField", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, unknownSort.StatusCode);

        // Oversized pages are clamped.
        var oversized = await client.GetApi("/api/v1/projects?pageSize=1000", tokens.AccessToken);
        var clamped = await oversized.ReadAsync<PagedResult<ProjectResponse>>();
        Assert.Equal(100, clamped.PageSize);
    }

    [Fact]
    public async Task AbsurdPageNumbersDoNotOverflow()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "query-overflow", "overflow@acme.test");
        await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Only project");

        var response = await client.GetApi(
            $"/api/v1/projects?page={int.MaxValue}&pageSize=100",
            tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsync<PagedResult<ProjectResponse>>();
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task TaskListsSupportStatusPriorityProjectAndDueDateFilters()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "query-tasks", "query-tasks@acme.test");

        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Task project");
        var otherProject = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Other project");

        await CreateTaskAsync(
            client,
            tokens.AccessToken,
            project.Id,
            "Write docs",
            WorkItemStatus.Done,
            WorkItemPriority.High,
            new DateOnly(2026, 11, 1));

        await CreateTaskAsync(
            client,
            tokens.AccessToken,
            project.Id,
            "Fix login",
            WorkItemStatus.InProgress,
            WorkItemPriority.Urgent,
            new DateOnly(2026, 12, 1));

        await CreateTaskAsync(
            client,
            tokens.AccessToken,
            otherProject.Id,
            "Plan release",
            WorkItemStatus.Todo,
            WorkItemPriority.Low,
            null);

        // The onboarding starter data includes a completed task, so narrow by title.
        var done = await client.GetApi("/api/v1/tasks?status=Done&search=docs", tokens.AccessToken);
        var donePage = await done.ReadAsync<PagedResult<TaskResponse>>();
        Assert.Single(donePage.Items);
        Assert.Equal("Write docs", donePage.Items[0].Title);

        var urgent = await client.GetApi("/api/v1/tasks?priority=Urgent", tokens.AccessToken);
        var urgentPage = await urgent.ReadAsync<PagedResult<TaskResponse>>();
        Assert.Single(urgentPage.Items);
        Assert.Equal("Fix login", urgentPage.Items[0].Title);

        var forProject = await client.GetApi(
            $"/api/v1/projects/{project.Id}/tasks",
            tokens.AccessToken);
        var projectPage = await forProject.ReadAsync<PagedResult<TaskResponse>>();
        Assert.Equal(2, projectPage.TotalCount);
        Assert.All(projectPage.Items, task => Assert.Equal(project.Id, task.ProjectId));

        var dueBefore = await client.GetApi("/api/v1/tasks?dueBefore=2026-11-30", tokens.AccessToken);
        var duePage = await dueBefore.ReadAsync<PagedResult<TaskResponse>>();
        Assert.Single(duePage.Items);
        Assert.Equal("Write docs", duePage.Items[0].Title);

        var search = await client.GetApi("/api/v1/tasks?search=release", tokens.AccessToken);
        var searchPage = await search.ReadAsync<PagedResult<TaskResponse>>();
        Assert.Single(searchPage.Items);
        Assert.Equal("Plan release", searchPage.Items[0].Title);
    }

    private static async Task<ProjectResponse> CreateProjectAsync(
        HttpClient client,
        string accessToken,
        string name,
        ProjectStatus status)
    {
        var response = await client.PostApi(
            "/api/v1/projects",
            new CreateProjectRequest(name, null, status),
            accessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ProjectResponse>();
    }

    private static async Task<TaskResponse> CreateTaskAsync(
        HttpClient client,
        string accessToken,
        Guid projectId,
        string title,
        WorkItemStatus status,
        WorkItemPriority priority,
        DateOnly? dueDate)
    {
        var response = await client.PostApi(
            $"/api/v1/projects/{projectId}/tasks",
            new CreateTaskRequest(title, null, status, priority, null, dueDate),
            accessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<TaskResponse>();
    }
}
