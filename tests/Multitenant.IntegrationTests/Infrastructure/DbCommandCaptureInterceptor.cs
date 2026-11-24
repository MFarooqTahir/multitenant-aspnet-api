using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Multitenant.IntegrationTests.Infrastructure;

internal sealed class DbCommandCaptureInterceptor : DbCommandInterceptor
{
    public ConcurrentQueue<string> SelectCommands { get; } = new();

    public void Clear() =>
        SelectCommands.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            SelectCommands.Enqueue(command.CommandText);
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
