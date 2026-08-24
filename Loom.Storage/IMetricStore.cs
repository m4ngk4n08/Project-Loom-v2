using System.Threading.Channels;
using Loom.Telemetry;

namespace Loom.Storage;

/// <summary>
/// Centralized metric storage. All writes go here, all reads come from here.
/// Replaces the static LoomMetrics.Buffers with an injectable service.
/// </summary>
public interface IMetricStore
{
    void Write(in MetricRecord record);

    MetricRecord[] ReadRecent(string metricName, int count);

    MetricRecord[] ReadRecent(int count);

    MetricRecord[] ReadSince(string metricName, long timestampUtcTicks);

    MetricRecord[] ReadAll(int limit = 1000);

    IReadOnlyCollection<string> GetMetricNames();

    (double Value, DateTime Timestamp)[] Snapshot(string metricName);

    IReadOnlyDictionary<string, MetricBuffer> GetBuffers();

    ChannelReader<MetricRecord> Subscribe();

    void Unsubscribe(ChannelReader<MetricRecord> reader);

    /// <summary>
    /// Cumulative per-series counter totals. Monotonic: unaffected by ring-buffer wrap.
    /// May be empty, or may omit series past the cardinality cap - callers must fall back.
    /// </summary>
    IReadOnlyCollection<CounterTotal> GetCounterTotals();
}
