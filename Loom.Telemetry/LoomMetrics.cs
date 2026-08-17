using System;
using System.Collections.Concurrent;
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
    // Per-metric ring buffers (one buffer per metric name)
    private static readonly ConcurrentDictionary<string, MetricBuffer> Buffers = new();

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
        GetOrCreateBuffer(name).Write(in record);
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
        GetOrCreateBuffer(name).Write(in record);
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
        GetOrCreateBuffer(name).Write(in record);
    }

    /// <summary>
    /// Get the N most recent metrics (non-blocking snapshot).
    /// </summary>
    public static MetricRecord[] GetRecentMetrics(int count = 100)
    {
        var allRecords = new List<MetricRecord>();
        foreach (var buffer in Buffers.Values)
        {
            allRecords.AddRange(buffer.ReadRecent(count));
        }
        return allRecords.OrderByDescending(r => r.TimestampUtcTicks).Take(count).ToArray();
    }

    /// <summary>
    /// Query metrics by name within a time window.
    /// </summary>
    public static IEnumerable<MetricRecord> QueryMetrics(string name, TimeSpan lookback)
    {
        if (!Buffers.TryGetValue(name, out var buffer))
            return Array.Empty<MetricRecord>();

        var cutoff = DateTime.UtcNow.Ticks - lookback.Ticks;
        return buffer.ReadSince(cutoff);
    }

    /// <summary>
    /// Query metrics by name and type within a time window.
    /// </summary>
    public static IEnumerable<MetricRecord> QueryMetrics(
        string name,
        MetricType type,
        TimeSpan lookback)
    {
        if (!Buffers.TryGetValue(name, out var buffer))
            return Array.Empty<MetricRecord>();

        var cutoff = DateTime.UtcNow.Ticks - lookback.Ticks;
        var records = buffer.ReadSince(cutoff);
        return records.Where(r => r.Type == type);
    }

    /// <summary>
    /// Get all metrics within a time window.
    /// </summary>
    public static MetricRecord[] GetMetricsSince(TimeSpan lookback)
    {
        var cutoff = DateTime.UtcNow.Ticks - lookback.Ticks;
        var allRecords = new List<MetricRecord>();
        foreach (var buffer in Buffers.Values)
        {
            allRecords.AddRange(buffer.ReadSince(cutoff));
        }
        return allRecords.OrderByDescending(r => r.TimestampUtcTicks).ToArray();
    }

    /// <summary>
    /// Internal: Get buffer capacity for diagnostics.
    /// </summary>
    public static int GetBufferCapacity() => Buffers.Values.FirstOrDefault()?.Capacity ?? 8192;

    /// <summary>
    /// Internal: Get or create a buffer for the given metric name.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MetricBuffer GetOrCreateBuffer(string name)
    {
        return Buffers.GetOrAdd(name, static _ => new MetricBuffer());
    }

    /// <summary>
    /// Internal: Get snapshot of all metric buffers for query engine (Phase 10).
    /// </summary>
    internal static IReadOnlyDictionary<string, MetricBuffer> GetBuffersSnapshot()
    {
        return Buffers;
    }
}
