using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Loom.Telemetry;

/// <summary>
/// Public API for recording custom metrics.
/// All methods are thread-safe and zero-allocation in hot paths.
/// </summary>
public static class LoomMetrics
{
    private static readonly MetricBuffer Buffer = new MetricBuffer();

    /// <summary>
    /// Record a counter metric (monotonically increasing value).
    /// Use for: request counts, error counts, items processed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordCounter(string name, double value, params MetricTag[] tags)
    {
        var record = new MetricRecord(
            name,
            MetricType.Counter,
            value,
            DateTime.UtcNow.Ticks,
            tags.Length > 0 ? tags : null,
            null
        );
        Buffer.Write(in record);
    }

    /// <summary>
    /// Record a gauge metric (point-in-time value that can go up or down).
    /// Use for: CPU percentage, memory usage, queue depth, active connections.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordGauge(string name, double value, params MetricTag[] tags)
    {
        var record = new MetricRecord(
            name,
            MetricType.Gauge,
            value,
            DateTime.UtcNow.Ticks,
            tags.Length > 0 ? tags : null,
            null
        );
        Buffer.Write(in record);
    }

    /// <summary>
    /// Record a histogram sample (value distribution).
    /// Use for: request latencies, response sizes, order amounts.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordHistogram(string name, double value, params MetricTag[] tags)
    {
        var record = new MetricRecord(
            name,
            MetricType.Histogram,
            value,
            DateTime.UtcNow.Ticks,
            tags.Length > 0 ? tags : null,
            null
        );
        Buffer.Write(in record);
    }

    /// <summary>
    /// Get the N most recent metrics (non-blocking snapshot).
    /// </summary>
    public static MetricRecord[] GetRecentMetrics(int count = 100)
    {
        return Buffer.ReadRecent(count);
    }

    /// <summary>
    /// Query metrics by name within a time window.
    /// </summary>
    public static IEnumerable<MetricRecord> QueryMetrics(string name, TimeSpan lookback)
    {
        var cutoff = DateTime.UtcNow.Ticks - lookback.Ticks;
        var records = Buffer.ReadSince(cutoff);
        return records.Where(r => r.Name == name);
    }

    /// <summary>
    /// Query metrics by name and type within a time window.
    /// </summary>
    public static IEnumerable<MetricRecord> QueryMetrics(
        string name,
        MetricType type,
        TimeSpan lookback)
    {
        var cutoff = DateTime.UtcNow.Ticks - lookback.Ticks;
        var records = Buffer.ReadSince(cutoff);
        return records.Where(r => r.Name == name && r.Type == type);
    }

    /// <summary>
    /// Get all metrics within a time window.
    /// </summary>
    public static MetricRecord[] GetMetricsSince(TimeSpan lookback)
    {
        var cutoff = DateTime.UtcNow.Ticks - lookback.Ticks;
        return Buffer.ReadSince(cutoff);
    }

    /// <summary>
    /// Internal: Get buffer capacity for diagnostics.
    /// </summary>
    public static int GetBufferCapacity() => Buffer.Capacity;
}
