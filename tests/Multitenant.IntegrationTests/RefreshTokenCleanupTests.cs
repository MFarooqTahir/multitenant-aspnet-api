using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Identity;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class RefreshTokenCleanupTests : IntegrationTestBase
{
    public RefreshTokenCleanupTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task CleanerDeletesOnlyExpiredOrLongRevokedTokens()
    {
        using var client = CreateClient();
        var expired = await Scenarios.OnboardAndLoginAsync(client, "cleanup-expired", "cleanup-expired@acme.test");
        var revoked = await Scenarios.OnboardAndLoginAsync(client, "cleanup-revoked", "cleanup-revoked@acme.test");
        var fresh = await Scenarios.OnboardAndLoginAsync(client, "cleanup-fresh", "cleanup-fresh@acme.test");
        var cutoff = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(31));

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var expiredToken = await dbContext.RefreshTokens.SingleAsync(
                token => token.TokenHash == RefreshTokenManager.Hash(expired.RefreshToken));
            expiredToken.ExpiresAtUtc = cutoff;

            var revokedToken = await dbContext.RefreshTokens.SingleAsync(
                token => token.TokenHash == RefreshTokenManager.Hash(revoked.RefreshToken));
            revokedToken.RevokedAtUtc = cutoff;
            revokedToken.RevokedReason = "test_retention";

            await dbContext.SaveChangesAsync();
        }

        int deleted;

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var cleaner = scope.ServiceProvider.GetRequiredService<RefreshTokenCleaner>();
            deleted = await cleaner.DeleteExpiredAsync(CancellationToken.None);
        }

        Assert.Equal(2, deleted);

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var remainingHashes = await dbContext.RefreshTokens
                .Select(token => token.TokenHash)
                .ToListAsync();

            Assert.DoesNotContain(RefreshTokenManager.Hash(expired.RefreshToken), remainingHashes);
            Assert.DoesNotContain(RefreshTokenManager.Hash(revoked.RefreshToken), remainingHashes);
            Assert.Contains(RefreshTokenManager.Hash(fresh.RefreshToken), remainingHashes);
        }
    }
}
