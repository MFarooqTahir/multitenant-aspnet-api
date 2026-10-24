using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;

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

        // Attempt 1: the assignee's tenant does not match the task tenant.
        await AssertAssignmentRejectedAsync(
            alpha,
            alphaProject.Id,
            betaOwner.Id,
            alpha.Id);

        // Attempt 2: the denormalized assignee tenant does not match the task tenant.
        await AssertAssignmentRejectedAsync(
            alpha,
            alphaProject.Id,
            betaOwner.Id,
            beta.Id);
    }

    private async Task AssertAssignmentRejectedAsync(
        TenantResponse tenant,
        Guid projectId,
        Guid assigneeUserId,
        Guid assigneeTenantId)
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

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }
}
