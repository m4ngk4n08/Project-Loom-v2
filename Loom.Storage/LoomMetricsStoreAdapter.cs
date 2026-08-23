using System.Collections.Concurrent;
using System.Threading.Channels;
using Loom.Telemetry;

namespace Loom.Storage;

/// <summary>
/// Adapter that exposes the static LoomMetrics.Buffers as an IMetricStore.
/// Used in tests that still write via LoomMetrics.RecordCounter/Gauge/Histogram.
/// </summary>
public sealed class LoomMetricsStoreAdapter : IMetricStore
{
    public static readonly LoomMetricsStoreAdapter Instance = new();

    public void Write(in MetricRecord record) => LoomMetrics.RecordCounter(record.Name, record.Value);

    public MetricRecord[] ReadRecent(string metricName, int count) =>
        LoomMetrics.QueryMetrics(metricName, TimeSpan.FromHours(1)).Take(count).ToArray();

    public MetricRecord[] ReadRecent(int count) => LoomMetrics.GetRecentMetrics(count);

    public MetricRecord[] ReadSince(string metricName, long timestampUtcTicks) =>
        LoomMetrics.QueryMetrics(metricName, TimeSpan.FromHours(1))
            .Where(r => r.TimestampUtcTicks >= timestampUtcTicks).ToArray();

    public MetricRecord[] ReadAll(int limit = 1000) => ReadRecent(limit);

    public IReadOnlyCollection<string> GetMetricNames() => GetStaticBuffers().Keys.ToList();

    public (double Value, DateTime Timestamp)[] Snapshot(string metricName)
    {
        var buffers = GetStaticBuffers();
        if (!buffers.TryGetValue(metricName, out var buffer))
            return Array.Empty<(double, DateTime)>();
        return buffer.Snapshot();
    }

    public IReadOnlyDictionary<string, MetricBuffer> GetBuffers() => GetStaticBuffers();

    public ChannelReader<MetricRecord> Subscribe() =>
        Channel.CreateUnbounded<MetricRecord>().Reader;

    public void Unsubscribe(ChannelReader<MetricRecord> reader) { }

    private static IReadOnlyDictionary<string, MetricBuffer> GetStaticBuffers() =>
        LoomRuntime.GetBuffersSnapshot();
}
