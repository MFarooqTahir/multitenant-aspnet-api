using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
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
        var expiredTenant = await Scenarios.OnboardAsync(client, "cleanup-expired", "Cleanup Expired", "cleanup-expired@acme.test");
        var revokedTenant = await Scenarios.OnboardAsync(client, "cleanup-revoked", "Cleanup Revoked", "cleanup-revoked@acme.test");
        await Scenarios.OnboardAsync(client, "cleanup-fresh", "Cleanup Fresh", "cleanup-fresh@acme.test");
        var expired = await Scenarios.LoginAsync(client, expiredTenant.Slug, "cleanup-expired@acme.test");
        var revoked = await Scenarios.LoginAsync(client, revokedTenant.Slug, "cleanup-revoked@acme.test");
        var fresh = await Scenarios.LoginAsync(client, "cleanup-fresh", "cleanup-fresh@acme.test");
        var cutoff = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(31));

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var expiredToken = await dbContext.RefreshTokens.SingleAsync(
                token => token.TokenHash == RefreshTokenManager.Hash(expired.RefreshToken));
            expiredToken.ExpiresAtUtc = cutoff;
            tenantContext.SetTenant(expiredTenant.Id, expiredTenant.Slug);
            await dbContext.SaveChangesAsync();

            var revokedToken = await dbContext.RefreshTokens.SingleAsync(
                token => token.TokenHash == RefreshTokenManager.Hash(revoked.RefreshToken));
            revokedToken.RevokedAtUtc = cutoff;
            revokedToken.RevokedReason = "test_retention";
            tenantContext.SetTenant(revokedTenant.Id, revokedTenant.Slug);

            await dbContext.SaveChangesAsync();
        }

        int deleted;

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var cleaner = scope.ServiceProvider.GetRequiredService<RefreshTokenCleaner>();
            deleted = await cleaner.DeleteExpiredAsync(CancellationToken.None);
        }

        Assert.True(deleted >= 2);

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var remainingHashes = await dbContext.RefreshTokens
                .IgnoreQueryFilters()
                .Select(token => token.TokenHash)
                .ToListAsync();

            Assert.DoesNotContain(RefreshTokenManager.Hash(expired.RefreshToken), remainingHashes);
            Assert.DoesNotContain(RefreshTokenManager.Hash(revoked.RefreshToken), remainingHashes);
            Assert.Contains(RefreshTokenManager.Hash(fresh.RefreshToken), remainingHashes);
        }
    }

    [Fact]
    public async Task Cleaner_UsesInclusiveCutoffAtExactBoundaryAndAdjacentValues()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FakeTimeProvider(now);
        await using var factory = Harness.CreateFactory(
            configuration => configuration["Auth:RefreshTokenCleanup:Enabled"] = "false",
            configureServices: services => services.AddSingleton<TimeProvider>(timeProvider));
        using var client = factory.CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "cleanup-boundary", "cleanup-boundary@acme.test");
        var userId = Guid.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken).Subject);
        var cutoff = now.Subtract(TimeSpan.FromDays(30));
        var adjacent = TimeSpan.FromMicroseconds(1);
        var ids = await AddRetentionTokensAsync(
            factory,
            userId,
            now,
            [
                (cutoff, null),
                (cutoff - adjacent, null),
                (cutoff + adjacent, null),
                (now, cutoff),
                (now, cutoff + adjacent),
            ]);

        using (var scope = factory.Services.CreateScope())
        {
            var cleaner = scope.ServiceProvider.GetRequiredService<RefreshTokenCleaner>();
            await cleaner.DeleteExpiredAsync(CancellationToken.None);
        }

        using var verificationScope = factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remainingIds = await verificationContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => ids.Contains(token.Id))
            .Select(token => token.Id)
            .ToListAsync();
        Assert.Equal(2, remainingIds.Count);
        Assert.Contains(ids[2], remainingIds);
        Assert.Contains(ids[4], remainingIds);

        using (var scope = factory.Services.CreateScope())
        {
            var cleaner = scope.ServiceProvider.GetRequiredService<RefreshTokenCleaner>();
            await cleaner.DeleteExpiredAsync(CancellationToken.None);
        }
        verificationContext.ChangeTracker.Clear();
        var remainingAfterSecondPass = await verificationContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => ids.Contains(token.Id))
            .Select(token => token.Id)
            .ToListAsync();
        Assert.Equivalent(remainingIds, remainingAfterSecondPass);
    }

    [Fact]
    public async Task Cleaner_DeletesAtMostBatchSizeTimesMaxBatches()
    {
        var now = DateTimeOffset.UtcNow;
        await using var factory = Harness.CreateFactory(configuration =>
        {
            configuration["Auth:RefreshTokenCleanup:Enabled"] = "false";
            configuration["Auth:RefreshTokenCleanup:BatchSize"] = "100";
            configuration["Auth:RefreshTokenCleanup:MaxBatchesPerPass"] = "2";
            configuration["Auth:RefreshTokenCleanup:RetentionDays"] = "30";
        }, configureServices: services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(now)));
        using var client = factory.CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "cleanup-batches", "cleanup-batches@acme.test");
        var userId = Guid.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken).Subject);
        var oldExpiry = now.Subtract(TimeSpan.FromDays(1_000));
        var inputs = Enumerable.Range(0, 250).Select(_ => (oldExpiry, (DateTimeOffset?)null)).ToArray();
        var ids = await AddRetentionTokensAsync(factory, userId, now, inputs);

        using var verificationScope = factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        using (var scope = factory.Services.CreateScope())
        {
            var cleaner = scope.ServiceProvider.GetRequiredService<RefreshTokenCleaner>();
            await cleaner.DeleteExpiredAsync(CancellationToken.None);
        }
        Assert.Equal(50, await verificationContext.RefreshTokens.IgnoreQueryFilters().CountAsync(token => ids.Contains(token.Id)));

        using (var scope = factory.Services.CreateScope())
        {
            var cleaner = scope.ServiceProvider.GetRequiredService<RefreshTokenCleaner>();
            await cleaner.DeleteExpiredAsync(CancellationToken.None);
        }
        verificationContext.ChangeTracker.Clear();
        Assert.Equal(0, await verificationContext.RefreshTokens.IgnoreQueryFilters().CountAsync(token => ids.Contains(token.Id)));
    }

    [Fact]
    public async Task HostedCleanup_FailureLogsWarningAndDoesNotStopHost()
    {
        using var setupClient = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(setupClient, "cleanup-hosted", "cleanup-hosted@acme.test");
        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
            var tenantId = Guid.Parse(jwt.Claims.Single(claim => claim.Type == Multitenant.Application.Common.AuthClaims.TenantId).Value);
            var slug = jwt.Claims.Single(claim => claim.Type == Multitenant.Application.Common.AuthClaims.TenantSlug).Value;
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId, slug);
            var stored = await dbContext.RefreshTokens.SingleAsync(token =>
                token.TokenHash == RefreshTokenManager.Hash(tokens.RefreshToken));
            stored.ExpiresAtUtc = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(31));
            await dbContext.SaveChangesAsync();
            await dbContext.Database.ExecuteSqlRawAsync("""
                BEGIN;
                DROP TRIGGER IF EXISTS trg_reject_refresh_delete_for_test ON "RefreshTokens";
                CREATE OR REPLACE FUNCTION reject_refresh_delete_for_test() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'injected refresh-token cleanup failure';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_reject_refresh_delete_for_test
                BEFORE DELETE ON "RefreshTokens"
                FOR EACH ROW EXECUTE FUNCTION reject_refresh_delete_for_test();
                COMMIT;
                """);
        }

        var loggerProvider = new CapturingSerilogSink();
        try
        {
            await using var factory = Harness.CreateFactory(configuration =>
            {
                configuration["Auth:RefreshTokenCleanup:Enabled"] = "true";
                configuration["Auth:RefreshTokenCleanup:IntervalHours"] = "1";
            }, configureServices: services =>
                services.AddSingleton<Serilog.Core.ILogEventSink>(loggerProvider));
            using var client = factory.CreateClient();

            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline
                && !loggerProvider.Entries.Any(entry =>
                    entry.Level == Serilog.Events.LogEventLevel.Warning
                    && entry.Message.Contains("Refresh-token cleanup failed", StringComparison.Ordinal)))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            var warning = Assert.Single(loggerProvider.Entries, entry =>
                entry.Level == Serilog.Events.LogEventLevel.Warning
                && entry.Message.Contains("Refresh-token cleanup failed", StringComparison.Ordinal));
            Assert.Contains("next scheduled pass will retry", warning.Message, StringComparison.Ordinal);
            Assert.Contains("injected refresh-token cleanup failure", warning.Exception, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await client.GetApi("/health/live")).StatusCode);
        }
        finally
        {
            using var scope = Harness.Factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS trg_reject_refresh_delete_for_test ON "RefreshTokens";
                DROP FUNCTION IF EXISTS reject_refresh_delete_for_test();
                """);
        }
    }

    private static async Task<List<Guid>> AddRetentionTokensAsync(
        WebApplicationFactory<Multitenant.Api.IApiMarker> factory,
        Guid userId,
        DateTimeOffset now,
        IReadOnlyList<(DateTimeOffset ExpiresAtUtc, DateTimeOffset? RevokedAtUtc)> tokens)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = await dbContext.Users.IgnoreQueryFilters()
            .Where(user => user.Id == userId)
            .Select(user => user.TenantId)
            .SingleAsync();
        var slug = await dbContext.Tenants.Where(tenant => tenant.Id == tenantId).Select(tenant => tenant.Slug).SingleAsync();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId, slug);
        var entities = tokens.Select(token => new RefreshToken
        {
            TenantId = tenantId,
            UserId = userId,
            TokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant(),
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = now,
            ExpiresAtUtc = token.ExpiresAtUtc,
            RevokedAtUtc = token.RevokedAtUtc,
            RevokedReason = token.RevokedAtUtc is null ? null : "retention_test",
        }).ToArray();
        dbContext.RefreshTokens.AddRange(entities);
        await dbContext.SaveChangesAsync();
        return entities.Select(entity => entity.Id).ToList();
    }
}
