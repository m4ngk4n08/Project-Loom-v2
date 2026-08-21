using System.Text.Json;
using Loom.Telemetry;

namespace Loom.Storage;

/// <summary>
/// Parses System.Runtime EventCounter payloads into metric records.
///
/// The System.Runtime EventSource emits one "EventCounters" event per interval
/// whose "Payload" is a JSON array, e.g.:
///   [{"Name":"cpu-usage","Mean":12.5,...},{"Name":"working-set","Mean":65536,...}]
///
/// The .NET runtime is a provider Loom does not control, so these arrive as
/// EventCounters rather than the strongly-typed *ValuePublished events used by
/// the System.Diagnostics.Metrics provider. This is a cold path (sidecar
/// collector / bridge), so JsonDocument allocation is acceptable.
/// </summary>
public static class SystemRuntimeCounters
{
    public const string ProviderName = "System.Runtime";

    /// <summary>
    /// Map a System.Runtime EventCounter payload to (Name, Type, Value) tuples.
    /// Returns empty when the payload is not a recognized counter shape.
    ///
    /// Two shapes exist in the wild:
    ///   .NET 10 runtime:   { "Payload": { "Name":"cpu-usage", "Mean":0.07, ... } }
    ///   Older runtimes:    [ { "Name":"cpu-usage", "Mean":0.07, ... }, ... ]
    /// </summary>
    public static IEnumerable<(string Name, MetricType Type, double Value)> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            yield break;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Payload", out var payloadObj))
        {
            var counter = payloadObj;
            if (counter.ValueKind != JsonValueKind.Object)
                yield break;

            var parsed = TryReadCounter(counter);
            if (parsed is not null)
                yield return parsed.Value;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var counter in root.EnumerateArray())
        {
            if (counter.ValueKind != JsonValueKind.Object)
                continue;

            var parsed = TryReadCounter(counter);
            if (parsed is not null)
                yield return parsed.Value;
        }
    }

    private static (string Name, MetricType Type, double Value)? TryReadCounter(JsonElement counter)
    {
        if (!counter.TryGetProperty("Name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
            return null;

        if (!counter.TryGetProperty("Mean", out var meanProp))
            return null;

        if (!meanProp.TryGetDouble(out var mean))
            return null;

        var name = nameProp.GetString();
        if (string.IsNullOrEmpty(name))
            return null;

        return (name, Classify(name), mean);
    }

    private static MetricType Classify(string name) => name switch
    {
        // Gauge-like: point-in-time samples (CPU %, working set, heap size, thread counts)
        "cpu-usage" or
        "working-set" or
        "gc-heap-size" or
        "gc-committed" or
        "gc-fragmentation" or
        "gen-0-size" or
        "gen-1-size" or
        "gen-2-size" or
        "loh-size" or
        "poh-size" or
        "gen-0-gc-budget" or
        "time-in-gc" or
        "threadpool-thread-count" or
        "threadpool-queue-length" or
        "threadpool-io-queue-length" or
        "threadpool-io-thread-count" or
        "assembly-count" or
        "methods-jitted-count" or
        "il-bytes-jitted" or
        "active-timer-count" or
        "monitor-lock-contention-count" => MetricType.Gauge,
        // Rate/count-like: monotonic or per-interval values
        "alloc-rate" or
        "exception-count" or
        "gen-0-collection-count" or
        "gen-1-collection-count" or
        "gen-2-collection-count" or
        "threadpool-queue-length-delta" => MetricType.Counter,
        _ => MetricType.Gauge
    };
}