using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;
using Npgsql;

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
        await Scenarios.CreateUserAsync(client, ownerTokens.AccessToken, "manage-admin-user@acme.test", TenantRoles.Admin);
        var adminTokens = await Scenarios.LoginAsync(client, tenant.Slug, "manage-admin-user@acme.test");

        var response = await client.PutApi(
            "/api/v1/tenants/current",
            new UpdateTenantRequest("Admin Rename"),
            adminTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var current = await (await client.GetApi("/api/v1/tenants/current", ownerTokens.AccessToken)).ReadAsync<TenantResponse>();
        Assert.Equal("Manage Admin", current.Name);
        Assert.Equal(tenant.Slug, current.Slug);
    }

    [Fact]
    public async Task MemberCannotRenameTenant()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "manage-member", "Manage Member", "manage-member@acme.test");
        var ownerTokens = await Scenarios.LoginAsync(client, tenant.Slug, "manage-member@acme.test");
        await Scenarios.CreateUserAsync(client, ownerTokens.AccessToken, "manage-member-user@acme.test", TenantRoles.Member);
        var memberTokens = await Scenarios.LoginAsync(client, tenant.Slug, "manage-member-user@acme.test");

        var response = await client.PutApi(
            "/api/v1/tenants/current",
            new UpdateTenantRequest("Member Rename"),
            memberTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var current = await (await client.GetApi("/api/v1/tenants/current", ownerTokens.AccessToken)).ReadAsync<TenantResponse>();
        Assert.Equal("Manage Member", current.Name);
        Assert.Equal(tenant.Slug, current.Slug);
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

    [Fact]
    public async Task OwnerRename_WithRoleDemotedUnderTenantLock_IsRejected()
    {
        var interceptor = new PausingCommandInterceptor();
        await using var factory = Harness.CreateFactory(configureServices: services =>
        {
            services.AddSingleton<DbCommandInterceptor>(interceptor);
            services.AddDbContext<AppDbContext>((_, options) => options.AddInterceptors(interceptor));
        });
        using var client = factory.CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "rename-stale-role", "Original Tenant", "rename-owner-one@acme.test");
        var firstOwner = await Scenarios.LoginAsync(client, tenant.Slug, "rename-owner-one@acme.test");
        var secondOwner = await Scenarios.CreateUserAsync(
            client,
            firstOwner.AccessToken,
            "rename-owner-two@acme.test",
            TenantRoles.Owner);
        var secondOwnerTokens = await Scenarios.LoginAsync(client, tenant.Slug, secondOwner.Email);

        interceptor.Arm();
        var rename = client.PutApi(
            "/api/v1/tenants/current",
            new UpdateTenantRequest("Demoted Owner Rename"),
            secondOwnerTokens.AccessToken);
        await interceptor.WaitUntilPausedAsync();
        var demote = client.PatchApi(
            $"/api/v1/users/{secondOwner.Id}/role",
            new UpdateUserRoleRequest(TenantRoles.Member),
            firstOwner.AccessToken);

        await using var connection = new NpgsqlConnection(Harness.ConnectionString);
        await connection.OpenAsync();
        await using (var updateCommand = new NpgsqlCommand("""
            UPDATE "AspNetUserRoles"
            SET "RoleId" = (SELECT "Id" FROM "AspNetRoles" WHERE "NormalizedName" = 'MEMBER')
            WHERE "UserId" = @id;
            """, connection))
        {
            updateCommand.Parameters.AddWithValue("id", secondOwner.Id);
            Assert.Equal(1, await updateCommand.ExecuteNonQueryAsync());
        }
        interceptor.Resume();
        var responses = await Task.WhenAll(rename, demote);

        Assert.Equal(HttpStatusCode.Forbidden, responses[0].StatusCode);
        var problem = await responses[0].ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("role_not_allowed", problem.Extensions["code"]?.ToString());
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        var current = await (await client.GetApi("/api/v1/tenants/current", firstOwner.AccessToken)).ReadAsync<TenantResponse>();
        Assert.Equal("Original Tenant", current.Name);
    }

}
