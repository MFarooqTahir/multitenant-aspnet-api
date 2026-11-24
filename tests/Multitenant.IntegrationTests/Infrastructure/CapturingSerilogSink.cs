using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace Multitenant.IntegrationTests.Infrastructure;

internal sealed class CapturingSerilogSink : ILogEventSink
{
    public ConcurrentQueue<(LogEventLevel Level, int EventId, string Message, string? Exception)> Entries { get; } = new();

    public TaskCompletionSource<bool> WarningWritten { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Emit(LogEvent logEvent)
    {
        Entries.Enqueue((
            logEvent.Level,
            ExtractEventId(logEvent.Properties.GetValueOrDefault("EventId")),
            logEvent.RenderMessage(CultureInfo.InvariantCulture),
            logEvent.Exception?.ToString()));

        if (logEvent.Level >= LogEventLevel.Warning)
        {
            WarningWritten.TrySetResult(true);
        }
    }

    private static int ExtractEventId(LogEventPropertyValue? value) => value switch
    {
        ScalarValue { Value: int id } => id,
        SequenceValue sequence when sequence.Elements.Count == 1 => ExtractEventId(sequence.Elements[0]),
        DictionaryValue dictionary => dictionary.Elements
            .Where(element => element.Key is ScalarValue { Value: string key }
                && string.Equals(key, "Id", StringComparison.OrdinalIgnoreCase))
            .Select(element => ExtractEventId(element.Value))
            .FirstOrDefault(),
        _ => 0,
    };
}
