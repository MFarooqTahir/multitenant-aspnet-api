using System.Net;
using Microsoft.AspNetCore.Mvc;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class TenantManagementTests : IntegrationTestBase
{
    public TenantManagementTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task OwnerCanRenameTenantWithoutChangingTheSlug()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "manage-rename", "Manage Rename", "rename@acme.test");
        var tokens = await Scenarios.LoginAsync(client, tenant.Slug, "rename@acme.test");

        var response = await client.PutApi(
            "/api/v1/tenants/current",
            new UpdateTenantRequest("Renamed Collective"),
            tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<TenantResponse>();
        Assert.Equal("Renamed Collective", updated.Name);
        Assert.Equal(tenant.Slug, updated.Slug);

        var current = await (await client.GetApi("/api/v1/tenants/current", tokens.AccessToken))
            .ReadAsync<TenantResponse>();
        Assert.Equal("Renamed Collective", current.Name);
    }

    [Fact]
    public async Task AdminCannotRenameTenant()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "manage-admin", "Manage Admin", "manage-admin@acme.test");
        var ownerTokens = await Scenarios.LoginAsync(client, tenant.Slug, "manage-admin@acme.test");
        await CreateUserAsync(client, ownerTokens.AccessToken, "manage-admin-user@acme.test", TenantRoles.Admin);
        var adminTokens = await Scenarios.LoginAsync(client, tenant.Slug, "manage-admin-user@acme.test");

        var response = await client.PutApi(
            "/api/v1/tenants/current",
            new UpdateTenantRequest("Admin Rename"),
            adminTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MemberCannotRenameTenant()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "manage-member", "Manage Member", "manage-member@acme.test");
        var ownerTokens = await Scenarios.LoginAsync(client, tenant.Slug, "manage-member@acme.test");
        await CreateUserAsync(client, ownerTokens.AccessToken, "manage-member-user@acme.test", TenantRoles.Member);
        var memberTokens = await Scenarios.LoginAsync(client, tenant.Slug, "manage-member-user@acme.test");

        var response = await client.PutApi(
            "/api/v1/tenants/current",
            new UpdateTenantRequest("Member Rename"),
            memberTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTenantNameReturnsValidationProblem()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "manage-invalid", "Manage Invalid", "manage-invalid@acme.test");
        var tokens = await Scenarios.LoginAsync(client, tenant.Slug, "manage-invalid@acme.test");

        var response = await client.PutApi(
            "/api/v1/tenants/current",
            new UpdateTenantRequest("A"),
            tokens.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        Assert.Equal("validation_failed", problem.Extensions["code"]?.ToString());
    }

    private static async Task CreateUserAsync(
        HttpClient client,
        string accessToken,
        string email,
        string role)
    {
        var response = await client.PostApi(
            "/api/v1/users",
            new CreateUserRequest(email, "Management User", Scenarios.Password, role),
            accessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
