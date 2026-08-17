using System;
using System.Linq;

namespace Loom.Telemetry;

/// <summary>
/// A single metric observation. Struct for cache-friendly storage.
/// </summary>
public readonly struct MetricRecord
{
    /// <summary>Metric name (e.g., "http.request.duration")</summary>
    public string Name { get; }

    /// <summary>Metric type (Counter, Gauge, Histogram, MethodExecution)</summary>
    public MetricType Type { get; }

    /// <summary>Numeric value (count, gauge value, histogram sample)</summary>
    public double Value { get; }

    /// <summary>Timestamp (UTC ticks)</summary>
    public long TimestampUtcTicks { get; }

    /// <summary>Tags/dimensions (optional, null if none)</summary>
    public MetricTag[]? Tags { get; }

    /// <summary>Exception details (only for MethodExecution failures)</summary>
    public string? ExceptionType { get; }

    public MetricRecord(
        string name,
        MetricType type,
        double value,
        long timestampUtcTicks,
        MetricTag[]? tags = null,
        string? exceptionType = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        Value = value;
        TimestampUtcTicks = timestampUtcTicks;
        Tags = tags;
        ExceptionType = exceptionType;
    }

    public DateTime TimestampUtc => new DateTime(TimestampUtcTicks, DateTimeKind.Utc);

    public override string ToString()
    {
        var tagsStr = Tags != null && Tags.Length > 0
            ? $" [{string.Join(", ", Tags.Select(t => t.ToString()))}]"
            : string.Empty;
        var exStr = ExceptionType != null ? $" Exception={ExceptionType}" : string.Empty;
        return $"{Name} ({Type}) = {Value}{tagsStr}{exStr}";
    }
}
