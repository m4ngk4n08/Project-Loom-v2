using System;
using System.Collections.Generic;
using System.Linq;

namespace Loom.Telemetry;

/// <summary>
/// A snapshot of metrics collected from a collector at a specific point in time.
/// </summary>
public sealed class CollectorSnapshot
{
    /// <summary>Collector name that produced this snapshot</summary>
    public string CollectorName { get; }

    /// <summary>When this snapshot was taken (UTC)</summary>
    public DateTime TimestampUtc { get; }

    /// <summary>Metrics collected in this snapshot</summary>
    public IReadOnlyList<MetricRecord> Metrics { get; }

    /// <summary>Error message if collection failed, null if successful</summary>
    public string? ErrorMessage { get; }

    /// <summary>Whether collection was successful</summary>
    public bool IsSuccess => ErrorMessage == null;

    public CollectorSnapshot(
        string collectorName,
        IEnumerable<MetricRecord> metrics,
        DateTime? timestampUtc = null,
        string? errorMessage = null)
    {
        CollectorName = collectorName ?? throw new ArgumentNullException(nameof(collectorName));
        Metrics = metrics?.ToList() ?? throw new ArgumentNullException(nameof(metrics));
        TimestampUtc = timestampUtc ?? DateTime.UtcNow;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Create a failed snapshot with an error message.
    /// </summary>
    public static CollectorSnapshot Failed(string collectorName, string errorMessage)
    {
        return new CollectorSnapshot(
            collectorName,
            Array.Empty<MetricRecord>(),
            DateTime.UtcNow,
            errorMessage);
    }

    public override string ToString()
    {
        if (!IsSuccess)
            return $"{CollectorName}: FAILED - {ErrorMessage}";

        return $"{CollectorName}: {Metrics.Count} metrics at {TimestampUtc:HH:mm:ss}";
    }
}
