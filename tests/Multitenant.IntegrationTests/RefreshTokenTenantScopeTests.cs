using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Identity;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class RefreshTokenTenantScopeTests : IntegrationTestBase
{
    public RefreshTokenTenantScopeTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task Revocation_WithMismatchedTenantCannotTouchAnotherTenantsTokens()
    {
        using var client = CreateClient();
        var alpha = await Scenarios.OnboardAsync(client, "refresh-scope-a", "Refresh A", "refresh-scope-a@acme.test");
        var beta = await Scenarios.OnboardAsync(client, "refresh-scope-b", "Refresh B", "refresh-scope-b@acme.test");
        var betaTokens = await Scenarios.LoginAsync(client, beta.Slug, "refresh-scope-b@acme.test");

        Guid familyId;
        Guid userId;
        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await dbContext.RefreshTokens.SingleAsync(token =>
                token.TokenHash == RefreshTokenManager.Hash(betaTokens.RefreshToken));
            familyId = stored.FamilyId;
            userId = stored.UserId;
        }

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(alpha.Id, alpha.Slug);
            var manager = scope.ServiceProvider.GetRequiredService<RefreshTokenManager>();
            Assert.Equal(0, await manager.RevokeFamilyAsync(alpha.Id, familyId, "mismatched", CancellationToken.None));
            Assert.Equal(0, await manager.RevokeAllForUserAsync(alpha.Id, userId, "mismatched", CancellationToken.None));
        }

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await dbContext.RefreshTokens.SingleAsync(token => token.FamilyId == familyId);
            Assert.Null(stored.RevokedAtUtc);
            Assert.Null(stored.RevokedReason);
        }

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(beta.Id, beta.Slug);
            var manager = scope.ServiceProvider.GetRequiredService<RefreshTokenManager>();
            Assert.Equal(1, await manager.RevokeFamilyAsync(beta.Id, familyId, "correct_tenant", CancellationToken.None));
        }

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await dbContext.RefreshTokens.SingleAsync(token => token.FamilyId == familyId);
            Assert.NotNull(stored.RevokedAtUtc);
            Assert.Equal("correct_tenant", stored.RevokedReason);
        }
    }
}
