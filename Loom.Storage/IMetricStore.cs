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

    /// <summary>
    /// Records the unit a metric's source declared. Units belong to the instrument, not to
    /// each record, so they are stored per name. Call before writing the record, so a
    /// subscriber that looks the unit up on receipt finds it.
    /// </summary>
    void SetUnit(string metricName, MetricUnit unit);

    /// <summary>The declared unit, or <see cref="MetricUnit.None"/> when the source declared
    /// none. Never guessed from the name.</summary>
    MetricUnit GetUnit(string metricName);
}
