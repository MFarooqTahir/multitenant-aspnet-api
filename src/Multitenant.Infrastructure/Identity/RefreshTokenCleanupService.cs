using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Multitenant.Application.Configuration.Auth;

namespace Multitenant.Infrastructure.Identity;

/// <summary>
/// Runs refresh-token retention once at startup and then on a fixed interval.
/// A failed pass is logged and retried on the next tick; it must not take down
/// the API (on .NET 11 an unhandled BackgroundService exception stops the host).
/// </summary>
public sealed partial class RefreshTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<RefreshTokenCleanupOptions> options,
    ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    private readonly RefreshTokenCleanupOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.IntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogCleanupFailed(logger, exception);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task CleanupOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cleaner = scope.ServiceProvider.GetRequiredService<RefreshTokenCleaner>();
        var deleted = await cleaner.DeleteExpiredAsync(cancellationToken);

        if (deleted > 0)
        {
            LogDeleted(logger, deleted);
        }
    }

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Deleted {DeletedCount} expired or long-revoked refresh tokens.")]
    private static partial void LogDeleted(ILogger logger, int deletedCount);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Refresh-token cleanup failed; the next scheduled pass will retry.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
