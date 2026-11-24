using System.Net;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class TextNormalizationTests : IntegrationTestBase
{
    public TextNormalizationTests(IntegrationHarness harness) => Harness = harness;

    [Theory]
    [InlineData("   ", "name")]
    [InlineData(" A ", "name")]
    [InlineData("Valid Tenant", "ownerDisplayName")]
    public async Task Onboarding_RejectsNamesThatAreInvalidAfterTrimming(string name, string field)
    {
        using var client = CreateClient();
        var ownerDisplayName = field == "ownerDisplayName" ? "   " : "Valid Owner";

        var response = await client.PostApi(
            "/api/v1/tenants/onboard",
            new OnboardTenantRequest($"trim-onboard-{field}", name, $"trim-onboard-{field}@acme.test", ownerDisplayName, Scenarios.Password));

        await AssertValidationAsync(response, field);
    }

    [Fact]
    public async Task Onboarding_ValidPaddedTextIsStoredTrimmed()
    {
        using var client = CreateClient();

        var response = await client.PostApi(
            "/api/v1/tenants/onboard",
            new OnboardTenantRequest("trim-onboard-valid", "  Padded Tenant  ", "trim-onboard-valid@acme.test", "  Padded Owner  ", Scenarios.Password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.ReadAsync<TenantResponse>();
        Assert.Equal("Padded Tenant", tenant.Name);
        var tokens = await Scenarios.LoginAsync(client, tenant.Slug, "trim-onboard-valid@acme.test");
        var me = await (await client.GetApi("/api/v1/users/me", tokens.AccessToken)).ReadAsync<UserResponse>();
        Assert.Equal("Padded Owner", me.DisplayName);
    }

    [Theory]
    [InlineData("   ", "name")]
    [InlineData(" A ", "name")]
    public async Task ProjectCreate_RejectsNamesThatAreInvalidAfterTrimming(string name, string field)
    {
        using var client = CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tokens = await Scenarios.OnboardAndLoginAsync(client, $"trim-project-{suffix}", $"trim-project-{suffix}@acme.test");

        var response = await client.PostApi(
            "/api/v1/projects",
            new CreateProjectRequest(name, null),
            tokens.AccessToken);

        await AssertValidationAsync(response, field);
    }

    [Fact]
    public async Task ProjectCreate_ValidPaddedTextIsStoredTrimmed()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "trim-project-valid", "trim-project-valid@acme.test");

        var response = await client.PostApi(
            "/api/v1/projects",
            new CreateProjectRequest("  Padded Project  ", "  Padded description  "),
            tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.ReadAsync<ProjectResponse>();
        Assert.Equal("Padded Project", project.Name);
        Assert.Equal("Padded description", project.Description);
    }

    [Theory]
    [InlineData("   ", "title")]
    [InlineData(" A ", "title")]
    public async Task TaskCreate_RejectsTitlesThatAreInvalidAfterTrimming(string title, string field)
    {
        using var client = CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tokens = await Scenarios.OnboardAndLoginAsync(client, $"trim-task-{suffix}", $"trim-task-{suffix}@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Trim task project");

        var response = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest(title, null),
            tokens.AccessToken);

        await AssertValidationAsync(response, field);
    }

    [Fact]
    public async Task TaskCreate_ValidPaddedTextIsStoredTrimmed()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "trim-task-valid", "trim-task-valid@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Trim task project");

        var response = await client.PostApi(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("  Padded Task  ", "  Padded task description  "),
            tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.ReadAsync<TaskResponse>();
        Assert.Equal("Padded Task", task.Title);
        Assert.Equal("Padded task description", task.Description);
    }

    [Theory]
    [InlineData("   ", "displayName")]
    [InlineData(" A ", "displayName")]
    public async Task UserCreate_RejectsDisplayNamesThatAreInvalidAfterTrimming(string displayName, string field)
    {
        using var client = CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tokens = await Scenarios.OnboardAndLoginAsync(client, $"trim-user-{suffix}", $"trim-user-{suffix}@acme.test");

        var response = await client.PostApi(
            "/api/v1/users",
            new CreateUserRequest($"trim-user-{displayName.Length}-member@acme.test", displayName, Scenarios.Password, "Member"),
            tokens.AccessToken);

        await AssertValidationAsync(response, field);
    }

    [Fact]
    public async Task UserCreate_ValidPaddedTextIsStoredTrimmed()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "trim-user-valid", "trim-user-valid@acme.test");

        var response = await client.PostApi(
            "/api/v1/users",
            new CreateUserRequest("trim-user-valid-member@acme.test", "  Padded User  ", Scenarios.Password, "Member"),
            tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.ReadAsync<UserResponse>();
        Assert.Equal("Padded User", user.DisplayName);
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.Equal("validation_failed", problem.Extensions["code"]?.ToString());
        Assert.Contains(problem.Errors.Keys, key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
    }
}
