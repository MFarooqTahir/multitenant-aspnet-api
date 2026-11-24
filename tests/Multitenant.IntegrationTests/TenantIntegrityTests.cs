using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;
using Npgsql;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class TenantIntegrityTests : IntegrationTestBase
{
    public TenantIntegrityTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task DatabaseRejectsCrossTenantTaskAssignees()
    {
        using var client = CreateClient();

        var alpha = await Scenarios.OnboardAsync(client, "integrity-a", "Integrity A", "integrity-a@acme.test");
        var beta = await Scenarios.OnboardAsync(client, "integrity-b", "Integrity B", "integrity-b@acme.test");

        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, "integrity-a@acme.test");
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, "integrity-b@acme.test");

        var alphaProject = await Scenarios.CreateProjectAsync(client, alphaTokens.AccessToken, "Integrity project");

        var betaMe = await client.GetApi("/api/v1/users/me", betaTokens.AccessToken);
        var betaOwner = await betaMe.ReadAsync<UserResponse>();

        await AssertAssignmentRejectedAsync(
            alpha,
            alphaProject.Id,
            betaOwner.Id,
            alpha.Id,
            "23503");

        await AssertAssignmentRejectedAsync(
            alpha,
            alphaProject.Id,
            betaOwner.Id,
            beta.Id,
            "23514");

        await AssertAssignmentRejectedAsync(
            alpha,
            alphaProject.Id,
            betaOwner.Id,
            assigneeTenantId: null,
            "23514");
    }

    [Fact]
    public async Task DatabaseRejectsCrossTenantProjectReference()
    {
        using var client = CreateClient();
        var alpha = await Scenarios.OnboardAsync(client, "integrity-project-a", "Integrity A", "project-a@acme.test");
        var beta = await Scenarios.OnboardAsync(client, "integrity-project-b", "Integrity B", "project-b@acme.test");
        var alphaTokens = await Scenarios.LoginAsync(client, alpha.Slug, "project-a@acme.test");
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, "project-b@acme.test");
        var betaProject = await Scenarios.CreateProjectAsync(client, betaTokens.AccessToken, "Foreign project");

        using var scope = Harness.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(alpha.Id, alpha.Slug);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.WorkItems.Add(new WorkItem
        {
            TenantId = alpha.Id,
            ProjectId = betaProject.Id,
            Title = "Cross-tenant project",
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        AssertSqlState(exception, "23503");
    }

    [Theory]
    [InlineData("user-only")]
    [InlineData("tenant-only")]
    public async Task DatabaseRejectsHalfSetAssigneeColumns(string halfSetCase)
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(client, $"integrity-half-{halfSetCase}", "Half Set", $"half-{halfSetCase}@acme.test");
        var tokens = await Scenarios.LoginAsync(client, tenant.Slug, $"half-{halfSetCase}@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Half-set project");
        var me = await (await client.GetApi("/api/v1/users/me", tokens.AccessToken)).ReadAsync<UserResponse>();

        using var scope = Harness.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Id, tenant.Slug);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.WorkItems.Add(new WorkItem
        {
            TenantId = tenant.Id,
            ProjectId = project.Id,
            Title = "Half-set assignee",
            AssigneeUserId = halfSetCase == "user-only" ? me.Id : null,
            AssigneeTenantId = halfSetCase == "tenant-only" ? tenant.Id : null,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        AssertSqlState(exception, "23514");
    }

    [Fact]
    public async Task DatabaseRejectsRefreshTokenForUserFromAnotherTenant()
    {
        using var client = CreateClient();
        var alpha = await Scenarios.OnboardAsync(client, "integrity-refresh-a", "Integrity A", "refresh-a@acme.test");
        var beta = await Scenarios.OnboardAsync(client, "integrity-refresh-b", "Integrity B", "refresh-b@acme.test");
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, "refresh-b@acme.test");
        var betaUser = await (await client.GetApi("/api/v1/users/me", betaTokens.AccessToken)).ReadAsync<UserResponse>();

        using var scope = Harness.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(alpha.Id, alpha.Slug);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            TenantId = alpha.Id,
            UserId = betaUser.Id,
            TokenHash = new string('a', 64),
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        AssertSqlState(exception, "23503");
    }

    private async Task AssertAssignmentRejectedAsync(
        TenantResponse tenant,
        Guid projectId,
        Guid assigneeUserId,
        Guid? assigneeTenantId,
        string expectedSqlState)
    {
        using var scope = Harness.Factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenant.Id, tenant.Slug);

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.WorkItems.Add(new WorkItem
        {
            TenantId = tenant.Id,
            ProjectId = projectId,
            Title = "Cross-tenant assignment",
            AssigneeUserId = assigneeUserId,
            AssigneeTenantId = assigneeTenantId,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        AssertSqlState(exception, expectedSqlState);
    }

    private static void AssertSqlState(DbUpdateException exception, string expectedSqlState)
    {
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(expectedSqlState, postgresException.SqlState);
    }
}
