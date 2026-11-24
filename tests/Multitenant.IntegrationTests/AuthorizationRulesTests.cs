using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;
using Npgsql;

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
        var admin = await Scenarios.CreateUserAsync(client, owner.AccessToken, "rules-admin@acme.test", TenantRoles.Admin);
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

        var member = await Scenarios.CreateUserAsync(client, owner.AccessToken, "delete-me@acme.test", TenantRoles.Member);

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
        Assert.NotEqual(first.AccessToken, second.AccessToken);

        var firstClaims = new JwtSecurityTokenHandler().ReadJwtToken(first.AccessToken).Claims;
        var secondClaims = new JwtSecurityTokenHandler().ReadJwtToken(second.AccessToken).Claims;
        Assert.NotEqual(
            firstClaims.Single(claim => claim.Type == AuthClaims.TenantId).Value,
            secondClaims.Single(claim => claim.Type == AuthClaims.TenantId).Value);
        Assert.Equal("rules-shared-a", firstClaims.Single(claim => claim.Type == AuthClaims.TenantSlug).Value);
        Assert.Equal("rules-shared-b", secondClaims.Single(claim => claim.Type == AuthClaims.TenantSlug).Value);
    }

    [Fact]
    public async Task ConcurrentCrossDemotionsCannotLeaveTheTenantWithoutAnOwner()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "rules-race", "Rules Race", "race-first@acme.test");
        var firstOwner = await Scenarios.LoginAsync(client, tenant.Slug, "race-first@acme.test");
        var firstOwnerId = await GetCurrentUserIdAsync(client, firstOwner.AccessToken);

        var secondOwner = await Scenarios.CreateUserAsync(
            client,
            firstOwner.AccessToken,
            "race-second@acme.test",
            TenantRoles.Owner);
        var secondOwnerTokens = await Scenarios.LoginAsync(
            client,
            tenant.Slug,
            "race-second@acme.test",
            Scenarios.Password);

        // Two Owners demote each other at the same time. The tenant row lock
        // serializes the guard, so exactly one demotion wins and one Owner remains.
        var firstDemotesSecond = client.PatchApi(
            $"/api/v1/users/{secondOwner.Id}/role",
            new UpdateUserRoleRequest(TenantRoles.Member),
            firstOwner.AccessToken);

        var secondDemotesFirst = client.PatchApi(
            $"/api/v1/users/{firstOwnerId}/role",
            new UpdateUserRoleRequest(TenantRoles.Member),
            secondOwnerTokens.AccessToken);

        var responses = await Task.WhenAll(firstDemotesSecond, secondDemotesFirst);

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.All(
            responses.Where(response => response.StatusCode != HttpStatusCode.OK),
            response => Assert.True(
                response.StatusCode is HttpStatusCode.Conflict
                    or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden,
                $"Unexpected status {response.StatusCode}."));

        var conflict = responses.SingleOrDefault(response => response.StatusCode == HttpStatusCode.Conflict);
        if (conflict is not null)
        {
            var problem = await conflict.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            Assert.Equal("last_owner", problem.Extensions["code"]?.ToString());
        }

        // The database, not the API, is the source of truth for the invariant.
        using var scope = Harness.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Id, tenant.Slug);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var owners = await userManager.GetUsersInRoleAsync(TenantRoles.Owner);
        Assert.Single(owners);
    }

    [Theory]
    [InlineData("demote")]
    [InlineData("delete")]
    public async Task LastOwnerGuard_ConcurrentActorDemotionReturnsConflict(string operation)
    {
        var interceptor = new PausingCommandInterceptor();
        await using var factory = Harness.CreateFactory(configureServices: services =>
        {
            services.AddSingleton<DbCommandInterceptor>(interceptor);
            services.AddDbContext<AppDbContext>((_, options) => options.AddInterceptors(interceptor));
        });
        using var client = factory.CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, $"last-owner-race-{operation}", "Last Owner Race", $"last-owner-actor-{operation}@acme.test");
        var actorTokens = await Scenarios.LoginAsync(client, tenant.Slug, $"last-owner-actor-{operation}@acme.test");
        var actor = await (await client.GetApi("/api/v1/users/me", actorTokens.AccessToken)).ReadAsync<UserResponse>();
        var target = await Scenarios.CreateUserAsync(
            client,
            actorTokens.AccessToken,
            $"last-owner-target-{operation}@acme.test",
            TenantRoles.Owner);

        interceptor.Arm(skipMatches: 1);
        var mutation = operation == "demote"
            ? client.PatchApi(
                $"/api/v1/users/{target.Id}/role",
                new UpdateUserRoleRequest(TenantRoles.Member),
                actorTokens.AccessToken)
            : client.DeleteApi($"/api/v1/users/{target.Id}", bearer: actorTokens.AccessToken);
        await interceptor.WaitUntilPausedAsync();

        await using var connection = new NpgsqlConnection(Harness.ConnectionString);
        await connection.OpenAsync();
        await using (var updateCommand = new NpgsqlCommand("""
            UPDATE "AspNetUserRoles"
            SET "RoleId" = (SELECT "Id" FROM "AspNetRoles" WHERE "NormalizedName" = 'MEMBER')
            WHERE "UserId" = @id;
            """, connection))
        {
            updateCommand.Parameters.AddWithValue("id", actor.Id);
            Assert.Equal(1, await updateCommand.ExecuteNonQueryAsync());
        }
        interceptor.Resume();
        var response = await mutation;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("last_owner", problem.Extensions["code"]?.ToString());
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Id, tenant.Slug);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var persistedTarget = await userManager.FindByIdAsync(target.Id.ToString());
        Assert.NotNull(persistedTarget);
        Assert.Equal([TenantRoles.Owner], await userManager.GetRolesAsync(persistedTarget));
    }

    [Theory]
    [InlineData("demote", "self_role_change")]
    [InlineData("delete", "self_delete")]
    public async Task SoleOwner_SelfDemoteOrDelete_IsRejectedAndStateUnchanged(string operation, string expectedCode)
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            $"rules-last-owner-{operation}",
            "Last Owner",
            $"last-owner-{operation}@acme.test");
        var tokens = await Scenarios.LoginAsync(client, tenant.Slug, $"last-owner-{operation}@acme.test");
        var ownerId = await GetCurrentUserIdAsync(client, tokens.AccessToken);

        var response = operation == "demote"
            ? await client.PatchApi(
                $"/api/v1/users/{ownerId}/role",
                new UpdateUserRoleRequest(TenantRoles.Member),
                tokens.AccessToken)
            : await client.DeleteApi($"/api/v1/users/{ownerId}", bearer: tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal(expectedCode, problem.Extensions["code"]?.ToString());

        using var scope = Harness.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Id, tenant.Slug);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await userManager.FindByIdAsync(ownerId.ToString());
        Assert.NotNull(owner);
        Assert.Equal([TenantRoles.Owner], await userManager.GetRolesAsync(owner));
        Assert.Equal(HttpStatusCode.OK, (await client.GetApi("/api/v1/users/me", tokens.AccessToken)).StatusCode);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("demote")]
    public async Task OwnerPromotionVersusAdminMutation_AdminCannotMutateNowOwner(string adminOperation)
    {
        var interceptor = new PausingCommandInterceptor();
        await using var factory = Harness.CreateFactory(configureServices: services =>
        {
            services.AddSingleton<DbCommandInterceptor>(interceptor);
            services.AddDbContext<AppDbContext>((_, options) => options.AddInterceptors(interceptor));
        });
        using var client = factory.CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            $"rules-promotion-race-{adminOperation}",
            "Promotion Race",
            $"promotion-owner-{adminOperation}@acme.test");
        var ownerTokens = await Scenarios.LoginAsync(
            client,
            tenant.Slug,
            $"promotion-owner-{adminOperation}@acme.test");
        var admin = await Scenarios.CreateUserAsync(
            client,
            ownerTokens.AccessToken,
            $"promotion-admin-{adminOperation}@acme.test",
            TenantRoles.Admin);
        var adminTokens = await Scenarios.LoginAsync(
            client,
            tenant.Slug,
            $"promotion-admin-{adminOperation}@acme.test");
        var target = await Scenarios.CreateUserAsync(
            client,
            ownerTokens.AccessToken,
            $"promotion-target-{adminOperation}@acme.test",
            TenantRoles.Member);

        interceptor.Arm();
        var ownerPromotion = client.PatchApi(
            $"/api/v1/users/{target.Id}/role",
            new UpdateUserRoleRequest(TenantRoles.Owner),
            ownerTokens.AccessToken);
        await interceptor.WaitUntilPausedAsync();

        var adminMutation = adminOperation == "delete"
            ? client.DeleteApi($"/api/v1/users/{target.Id}", bearer: adminTokens.AccessToken)
            : client.PatchApi(
                $"/api/v1/users/{target.Id}/role",
                new UpdateUserRoleRequest(TenantRoles.Member),
                adminTokens.AccessToken);

        interceptor.Resume();
        var responses = await Task.WhenAll(ownerPromotion, adminMutation);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, responses[1].StatusCode);
        var problem = await responses[1].ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("role_not_allowed", problem.Extensions["code"]?.ToString());

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Id, tenant.Slug);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var persistedTarget = await userManager.FindByIdAsync(target.Id.ToString());
        Assert.NotNull(persistedTarget);
        Assert.Equal([TenantRoles.Owner], await userManager.GetRolesAsync(persistedTarget));
    }

    [Fact]
    public async Task Registration_WithRoleDemotedUnderTenantLock_IsRejected()
    {
        var interceptor = new PausingCommandInterceptor();
        await using var factory = Harness.CreateFactory(configureServices: services =>
        {
            services.AddSingleton<DbCommandInterceptor>(interceptor);
            services.AddDbContext<AppDbContext>((_, options) => options.AddInterceptors(interceptor));
        });
        using var client = factory.CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, "register-stale-role", "Register Stale", "register-owner@acme.test");
        var owner = await Scenarios.LoginAsync(client, tenant.Slug, "register-owner@acme.test");
        var admin = await Scenarios.CreateUserAsync(
            client,
            owner.AccessToken,
            "register-admin@acme.test",
            TenantRoles.Admin);
        var adminTokens = await Scenarios.LoginAsync(client, tenant.Slug, admin.Email);

        interceptor.Arm();
        var registration = client.PostApi(
            "/api/v1/auth/register",
            new RegisterRequest(tenant.Slug, "stale-register@acme.test", "Stale Register", Scenarios.Password),
            adminTokens.AccessToken);
        await interceptor.WaitUntilPausedAsync();
        var demotion = client.PatchApi(
            $"/api/v1/users/{admin.Id}/role",
            new UpdateUserRoleRequest(TenantRoles.Member),
            owner.AccessToken);

        await using var connection = new NpgsqlConnection(Harness.ConnectionString);
        await connection.OpenAsync();
        await using (var updateCommand = new NpgsqlCommand("""
            UPDATE "AspNetUserRoles"
            SET "RoleId" = (SELECT "Id" FROM "AspNetRoles" WHERE "NormalizedName" = 'MEMBER')
            WHERE "UserId" = @id;
            """, connection))
        {
            updateCommand.Parameters.AddWithValue("id", admin.Id);
            Assert.Equal(1, await updateCommand.ExecuteNonQueryAsync());
        }
        interceptor.Resume();
        var responses = await Task.WhenAll(registration, demotion);

        Assert.Equal(HttpStatusCode.Forbidden, responses[0].StatusCode);
        var problem = await responses[0].ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("role_not_allowed", problem.Extensions["code"]?.ToString());
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        var users = await (await client.GetApi("/api/v1/users", owner.AccessToken)).ReadAsync<Multitenant.Application.Contracts.Common.PagedResult<UserResponse>>();
        Assert.Equal(2, users.TotalCount);
        Assert.DoesNotContain(users.Items, user => user.Email == "stale-register@acme.test");
    }

    private static async Task<Guid> GetCurrentUserIdAsync(HttpClient client, string accessToken)
    {
        var response = await client.GetApi("/api/v1/users/me", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.ReadAsync<UserResponse>();
        return user.Id;
    }
}
