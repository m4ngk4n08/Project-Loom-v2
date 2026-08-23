using System;
using Loom.Telemetry;
using Microsoft.Extensions.Logging;

namespace Loom.Storage.Logging;

/// <summary>
/// ILogger implementation that writes captured entries into an ILogStore.
/// The buffer write itself is allocation-free (LogBuffer.Write), but formatting the
/// message via the caller-supplied formatter is not - this is a capture path, not a
/// zero-allocation hot path.
/// </summary>
public sealed class LoomLogger : ILogger
{
    private readonly string _category;
    private readonly ILogStore _logStore;

    // Reentrancy guard #2: even for non-"Loom." categories, the act of writing to the
    // store (e.g. a subscriber reading it back synchronously) could trigger another
    // log call on the same thread. A per-thread flag breaks that cycle without a lock.
    [ThreadStatic]
    private static bool _isLogging;

    public LoomLogger(string category, ILogStore logStore)
    {
        _category = category;
        _logStore = logStore;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        if (_isLogging)
            return;

        _isLogging = true;
        try
        {
            // Never state.ToString() / reflection - use the formatter MEL supplies,
            // which is how structured logging templates get rendered correctly.
            var message = formatter(state, exception);

            var record = new LogRecord(
                message,
                _category,
                MapLevel(logLevel),
                DateTime.UtcNow.Ticks,
                eventId.Id,
                exception?.GetType().Name,
                exception?.Message);

            _logStore.Write(in record);
        }
        finally
        {
            _isLogging = false;
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    private static LoomLogLevel MapLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => LoomLogLevel.Trace,
        LogLevel.Debug => LoomLogLevel.Debug,
        LogLevel.Information => LoomLogLevel.Information,
        LogLevel.Warning => LoomLogLevel.Warning,
        LogLevel.Error => LoomLogLevel.Error,
        LogLevel.Critical => LoomLogLevel.Critical,
        // A logger must never throw - an unrecognized value (e.g. a raw cast like
        // (LogLevel)99) falls back to Information rather than crashing the caller.
        _ => LoomLogLevel.Information
    };
}
