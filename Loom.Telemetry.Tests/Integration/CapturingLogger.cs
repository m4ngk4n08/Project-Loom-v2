using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Loom.Telemetry.Tests.Integration;

/// <summary>
/// ILogger&lt;T&gt; that records every formatted message. EventPipeBridge exposes no
/// counters and no error property - unlike EventPipeCollector, which grew
/// LastError/IsFaulted/RecordsIngested precisely because an empty store is
/// indistinguishable from a target that published nothing - so this log capture is the
/// bridge's only account of itself: it logs when it connects, each reconnect, each parse
/// failure, and its ingest totals when a session ends.
/// Thread-safe: EventPipeBridge logs from the EventPipeEventSource.Process() callback
/// thread, not from the test thread that reads DumpLast.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly object _lock = new();
    private readonly List<string> _messages = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = exception is null
            ? $"[{logLevel}] {formatter(state, exception)}"
            : $"[{logLevel}] {formatter(state, exception)} ({exception.GetType().Name}: {exception.Message})";

        lock (_lock)
        {
            _messages.Add(message);
        }
    }

    /// <summary>
    /// The last <paramref name="count"/> captured messages, newest last, joined for
    /// inclusion in an assertion failure message.
    /// </summary>
    public string DumpLast(int count = 12)
    {
        lock (_lock)
        {
            return _messages.Count == 0
                ? "(no log messages captured)"
                : string.Join(Environment.NewLine, _messages.TakeLast(count));
        }
    }

    public bool Any(Func<string, bool> predicate)
    {
        lock (_lock)
        {
            return _messages.Any(predicate);
        }
    }
}
