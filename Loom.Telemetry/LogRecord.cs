using System;

namespace Loom.Telemetry;

/// <summary>
/// A single log observation. Struct for cache-friendly storage.
/// </summary>
public readonly struct LogRecord
{
    /// <summary>Formatted log message</summary>
    public string Message { get; }

    /// <summary>Logger category (e.g., "MyApp.Services.OrderService")</summary>
    public string Category { get; }

    /// <summary>Log severity</summary>
    public LoomLogLevel Level { get; }

    /// <summary>Timestamp (UTC ticks)</summary>
    public long TimestampUtcTicks { get; }

    /// <summary>Event id, if the caller supplied one (0 otherwise)</summary>
    public int EventId { get; }

    /// <summary>Exception type name (only set when the log entry carries an exception)</summary>
    public string? ExceptionType { get; }

    /// <summary>Exception message (only set when the log entry carries an exception)</summary>
    public string? ExceptionMessage { get; }

    public LogRecord(
        string message,
        string category,
        LoomLogLevel level,
        long timestampUtcTicks,
        int eventId = 0,
        string? exceptionType = null,
        string? exceptionMessage = null)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Level = level;
        TimestampUtcTicks = timestampUtcTicks;
        EventId = eventId;
        ExceptionType = exceptionType;
        ExceptionMessage = exceptionMessage;
    }

    public DateTime TimestampUtc => new DateTime(TimestampUtcTicks, DateTimeKind.Utc);

    public override string ToString()
    {
        var exStr = ExceptionType != null ? $" Exception={ExceptionType}: {ExceptionMessage}" : string.Empty;
        return $"[{Level}] {Category}: {Message}{exStr}";
    }
}
