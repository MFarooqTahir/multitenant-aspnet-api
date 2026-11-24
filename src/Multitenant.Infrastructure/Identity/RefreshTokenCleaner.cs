using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Multitenant.Application.Configuration.Auth;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Identity;

/// <summary>
/// Deletes refresh-token rows only after they are both unusable and older than
/// the retention window. Refresh tokens deliberately have no tenant query filter,
/// so this cleanup is global and safe to run from any replica.
/// </summary>
public sealed class RefreshTokenCleaner(
    AppDbContext dbContext,
    IOptions<RefreshTokenCleanupOptions> options,
    TimeProvider timeProvider)
{
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().Subtract(TimeSpan.FromDays(options.Value.RetentionDays));
        var deleted = 0;

        // Bound each statement and each pass so a large backlog cannot hold one
        // transaction open or monopolize the database during startup.
        for (var batch = 0; batch < options.Value.MaxBatchesPerPass; batch++)
        {
            var ids = await dbContext.RefreshTokens
                .Where(token =>
                    token.ExpiresAtUtc <= cutoff
                    || (token.RevokedAtUtc != null && token.RevokedAtUtc <= cutoff))
                .OrderBy(token => token.ExpiresAtUtc)
                .Take(options.Value.BatchSize)
                .Select(token => token.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
            {
                break;
            }

            var removed = await dbContext.RefreshTokens
                .Where(token => ids.Contains(token.Id))
                .ExecuteDeleteAsync(cancellationToken);

            deleted += removed;

            // Another replica may have won the race for this batch.
            if (removed == 0 || ids.Count < options.Value.BatchSize)
            {
                break;
            }
        }

        return deleted;
    }
}
