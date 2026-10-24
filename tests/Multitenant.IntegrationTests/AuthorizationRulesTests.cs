using System.Net;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class AuthorizationRulesTests : IntegrationTestBase
{
    public AuthorizationRulesTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task AdminCannotManageOwners()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "rules-admin", "rules-owner@acme.test");
        var admin = await CreateUserAsync(client, owner.AccessToken, "rules-admin@acme.test", TenantRoles.Admin);
        var adminTokens = await Scenarios.LoginAsync(client, "rules-admin", "rules-admin@acme.test", Scenarios.Password);

        var createOwner = await client.PostApi(
            "/api/v1/users",
            new CreateUserRequest("rules-owner2@acme.test", "Second Owner", Scenarios.Password, TenantRoles.Owner),
            adminTokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, createOwner.StatusCode);

        var ownerId = await GetCurrentUserIdAsync(client, owner.AccessToken);

        var demoteOwner = await client.PatchApi(
            $"/api/v1/users/{ownerId}/role",
            new UpdateUserRoleRequest(TenantRoles.Member),
            adminTokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, demoteOwner.StatusCode);

        var deleteOwner = await client.DeleteApi(
            $"/api/v1/users/{ownerId}",
            bearer: adminTokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, deleteOwner.StatusCode);

        Assert.Equal(TenantRoles.Admin, admin.Role);
    }

    [Fact]
    public async Task UsersCannotChangeTheirOwnRoleOrDeleteThemselves()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "rules-self", "rules-self@acme.test");
        var userId = await GetCurrentUserIdAsync(client, owner.AccessToken);

        var changeOwnRole = await client.PatchApi(
            $"/api/v1/users/{userId}/role",
            new UpdateUserRoleRequest(TenantRoles.Member),
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, changeOwnRole.StatusCode);

        var deleteSelf = await client.DeleteApi($"/api/v1/users/{userId}", bearer: owner.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, deleteSelf.StatusCode);
    }

    [Fact]
    public async Task DeletingAUserRemovesAccessAndResources()
    {
        using var client = CreateClient();
        var owner = await Scenarios.OnboardAndLoginAsync(client, "rules-delete", "rules-delete@acme.test");

        var member = await CreateUserAsync(client, owner.AccessToken, "delete-me@acme.test", TenantRoles.Member);

        var fetch = await client.GetApi($"/api/v1/users/{member.Id}", owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);

        var delete = await client.DeleteApi($"/api/v1/users/{member.Id}", bearer: owner.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var afterDelete = await client.GetApi($"/api/v1/users/{member.Id}", owner.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task TheSameEmailCanBelongToDifferentTenants()
    {
        using var client = CreateClient();

        await Scenarios.OnboardAsync(client, "rules-shared-a", "Shared A", "shared@acme.test");
        await Scenarios.OnboardAsync(client, "rules-shared-b", "Shared B", "shared@acme.test");

        var first = await Scenarios.LoginAsync(client, "rules-shared-a", "shared@acme.test", Scenarios.Password);
        var second = await Scenarios.LoginAsync(client, "rules-shared-b", "shared@acme.test", Scenarios.Password);

        Assert.False(string.IsNullOrWhiteSpace(first.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(second.AccessToken));
    }

    private static async Task<UserResponse> CreateUserAsync(
        HttpClient client,
        string accessToken,
        string email,
        string role)
    {
        var response = await client.PostApi(
            "/api/v1/users",
            new CreateUserRequest(email, "Rules User", Scenarios.Password, role),
            accessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<UserResponse>();
    }

    private static async Task<Guid> GetCurrentUserIdAsync(HttpClient client, string accessToken)
    {
        var response = await client.GetApi("/api/v1/users/me", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.ReadAsync<UserResponse>();
        return user.Id;
    }
}
