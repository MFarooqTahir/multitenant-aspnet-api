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

        return await dbContext.RefreshTokens
            .Where(token =>
                token.ExpiresAtUtc <= cutoff
                || (token.RevokedAtUtc != null && token.RevokedAtUtc <= cutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
