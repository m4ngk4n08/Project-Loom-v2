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
            return Array.Empty<(string, MetricType, double)>();

        // Parsed outside the iterator: yield return cannot appear inside a try block
        // that has a catch clause, and truly malformed (non-JSON) text must be skipped,
        // never thrown - same tolerance TryReadCounter already gives a well-formed but
        // unrecognized payload shape.
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Array.Empty<(string, MetricType, double)>();
        }

        return ParseDocument(doc);
    }

    private static IEnumerable<(string Name, MetricType Type, double Value)> ParseDocument(JsonDocument doc)
    {
        using (doc)
        {
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
    }

    private static (string Name, MetricType Type, double Value)? TryReadCounter(JsonElement counter)
    {
        if (!counter.TryGetProperty("Name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
            return null;

        // The runtime publishes two counter shapes (see the class doc): a polling
        // counter's payload carries "Mean" (a point-in-time sample), an incrementing
        // counter's carries "Increment" instead (measured, against a live session, to be
        // a PER-INTERVAL DELTA, not a cumulative total - see BACKLOG.md 6.11). Checking
        // "Mean" first and falling back to "Increment" - rather than requiring "Mean" -
        // is Bug A's fix: every incrementing counter (gen-N-gc-count,
        // monitor-lock-contention-count, alloc-rate, exception-count, and others) was
        // being rejected here before Classify was ever consulted.
        double value;
        if (counter.TryGetProperty("Mean", out var meanProp) && meanProp.TryGetDouble(out var mean))
        {
            value = mean;
        }
        else if (counter.TryGetProperty("Increment", out var incrementProp) && incrementProp.TryGetDouble(out var increment))
        {
            value = increment;
        }
        else
        {
            return null;
        }

        var name = nameProp.GetString();
        if (string.IsNullOrEmpty(name))
            return null;

        return (name, Classify(name), value);
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
        "active-timer-count" => MetricType.Gauge,
        // Counter-like: the runtime's "Increment" field, measured to be a per-interval
        // delta (see TryReadCounter) - summing deltas reconstructs the total, which is
        // exactly what InMemoryMetricStore.RecordCounterTotal's accumulator wants (the
        // same "rate" decision made for CounterRateValuePublished on 2026-09-07). Names
        // below are exactly what a live .NET 10 System.Runtime session was measured to
        // publish (BACKLOG.md 6.11) - "gen-N-gc-count", NOT "gen-N-collection-count"
        // (Bug B: the runtime has never published that name), and
        // "monitor-lock-contention-count" moved here from the Gauge list above because it
        // was measured carrying "Increment", not "Mean". "threadpool-queue-length-delta"
        // was removed: it never appeared in a live session, on any shape.
        "alloc-rate" or
        "exception-count" or
        "gen-0-gc-count" or
        "gen-1-gc-count" or
        "gen-2-gc-count" or
        "monitor-lock-contention-count" or
        "threadpool-completed-items-count" or
        "total-pause-time-by-gc" or
        "time-in-jit" => MetricType.Counter,
        _ => MetricType.Gauge
    };
}