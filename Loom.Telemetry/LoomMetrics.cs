using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Loom.Telemetry;

/// <summary>
/// Public API for recording custom metrics.
/// All methods are thread-safe and zero-allocation in hot paths.
/// </summary>
public static class LoomMetrics
{
    private const int DefaultBufferCapacity = 8192;

    // Floor: below this a "ring buffer" stops meaningfully ringing — a handful of slots
    // wrap on nearly every burst and ReadRecent/ReadSince become useless for anything but
    // the last instant. Ceiling: 1,048,576 records/buffer, applied per distinct metric
    // name, so this bounds a single series' memory, not the process total. At ~48 bytes/
    // record (MetricRecord: string ref + enum + double + long + array ref + string ref,
    // padded), that is ~48 MB for one maxed-out series — large but a deliberate, bounded
    // ceiling rather than an unbounded env var letting one bad value exhaust memory.
    private const int MinBufferCapacity = 64;
    private const int MaxBufferCapacity = 1_048_576;

    // Read once and cached: capacity is process-wide and fixed for the process lifetime,
    // same as the hardcoded default it replaces. Re-reading per buffer creation would
    // let different metric names end up with different capacities if the env var changed
    // mid-run (it can't via ResetForTesting either — see LoomMetrics tests).
    private static readonly int ConfiguredCapacity = ReadCapacityFromEnvironment();

    // Per-metric ring buffers (one buffer per metric name)
    private static readonly ConcurrentDictionary<string, MetricBuffer> Buffers = new();

    private static int ReadCapacityFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("LOOM_METRIC_BUFFER_CAPACITY");
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultBufferCapacity;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return DefaultBufferCapacity;

        return Math.Clamp(parsed, MinBufferCapacity, MaxBufferCapacity);
    }

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
        MetricsBridge.PublishCounter(name, (long)value, tags);
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
        MetricsBridge.PublishGauge(name, value, tags);
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
        MetricsBridge.PublishHistogram(name, value, tags);
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
    public static int GetBufferCapacity() => Buffers.Values.FirstOrDefault()?.Capacity ?? DefaultBufferCapacity;

    /// <summary>
    /// Number of records lost to buffer wraparound for the given metric name.
    /// 0 if the name has no buffer yet (nothing has been dropped because nothing has
    /// been recorded).
    /// </summary>
    public static long GetDroppedCount(string name) =>
        Buffers.TryGetValue(name, out var buffer) ? buffer.DroppedCount : 0;

    /// <summary>
    /// Internal: Get or create a buffer for the given metric name.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MetricBuffer GetOrCreateBuffer(string name)
    {
        return Buffers.GetOrAdd(name, static n =>
        {
            var buffer = new MetricBuffer(ConfiguredCapacity);
            MetricsBridge.RegisterBufferDroppedProvider(n, () => buffer.DroppedCount);
            return buffer;
        });
    }

    /// <summary>
    /// Internal: Get snapshot of all metric buffers for query engine (Phase 10).
    /// </summary>
    internal static IReadOnlyDictionary<string, MetricBuffer> GetBuffersSnapshot()
    {
        return Buffers;
    }

    /// <summary>
    /// Test-only: Reset all metric buffers. Call this in test setup/teardown to isolate tests.
    /// </summary>
    public static void ResetForTesting()
    {
        Buffers.Clear();
    }
}
