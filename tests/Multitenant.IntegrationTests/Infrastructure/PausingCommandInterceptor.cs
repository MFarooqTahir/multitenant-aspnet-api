using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Multitenant.IntegrationTests.Infrastructure;

internal sealed class PausingCommandInterceptor : DbCommandInterceptor
{
    private volatile bool _armed;
    private int _remainingSkips;
    private TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Arm(int skipMatches = 0)
    {
        _entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _remainingSkips, skipMatches);
        _armed = true;
    }

    public Task WaitUntilPausedAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public void Resume() => _release.TrySetResult(true);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (_armed
            && command.CommandText.Contains("\"AspNetUserRoles\"", StringComparison.Ordinal)
            && command.CommandText.Contains("\"AspNetRoles\"", StringComparison.Ordinal)
            && !command.CommandText.Contains("\"Tenants\"", StringComparison.Ordinal))
        {
            if (Volatile.Read(ref _remainingSkips) > 0)
            {
                Interlocked.Decrement(ref _remainingSkips);
                return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            }

            _armed = false;
            _entered.TrySetResult(true);
            return ContinueAfterPauseAsync(command, eventData, result, cancellationToken);
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private async ValueTask<InterceptionResult<DbDataReader>> ContinueAfterPauseAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken)
    {
        await _release.Task.WaitAsync(cancellationToken);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
