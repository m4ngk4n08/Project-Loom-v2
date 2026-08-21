using Loom.Telemetry;
using Loom.Web.Contracts.Dtos;

namespace Loom.Storage;

/// <summary>Builds <see cref="MetricSummaryDto"/> values from an <see cref="IMetricStore"/>.
/// Used by the Metrics Explorer summary endpoints in both the Dashboard and Web.Api hosts.</summary>
public static class MetricSummaryBuilder
{
    public static List<MetricSummaryDto> BuildAll(IMetricStore store)
    {
        var summaries = new List<MetricSummaryDto>();
        var buffers = store.GetBuffers();

        foreach (var kvp in buffers)
        {
            var snapshot = kvp.Value.Snapshot();
            if (snapshot.Length == 0) continue;

            var values = snapshot.Select(s => s.Value).ToArray();
            var sorted = values.OrderBy(v => v).ToArray();

            summaries.Add(new MetricSummaryDto
            {
                Name = kvp.Key,
                Type = GetTypeName(kvp.Value),
                Unit = InferUnit(kvp.Key),
                SampleCount = values.Length,
                LatestValue = values[0],
                Average = values.Average(),
                Min = sorted[0],
                Max = sorted[^1],
                P95 = sorted[PercentileIndex(sorted.Length, 0.95)],
                FirstTimestampUtc = snapshot[^1].Timestamp,
                LastTimestampUtc = snapshot[0].Timestamp
            });
        }

        return summaries.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetTypeName(MetricBuffer buffer)
    {
        var recent = buffer.ReadRecent(1);
        if (recent.Length == 0) return "unknown";
        return recent[0].Type switch
        {
            MetricType.Counter => "counter",
            MetricType.Gauge => "gauge",
            MetricType.Histogram => "histogram",
            MetricType.MethodExecution => "method",
            _ => "unknown"
        };
    }

    private static int PercentileIndex(int length, double percentile) =>
        Math.Clamp((int)Math.Ceiling(percentile * length) - 1, 0, length - 1);

    /// <summary>Infer a human-readable unit from the metric name so mixed-unit lists
    /// (durations, currency, item counts) are unambiguous at a glance.</summary>
    private static string InferUnit(string name)
    {
        if (name.Contains("cpu-usage", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("time-in-gc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gc-fragmentation", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("usage", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("percent", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("%", StringComparison.OrdinalIgnoreCase))
            return "%";

        if (name.Contains("alloc-rate", StringComparison.OrdinalIgnoreCase))
            return "B/s";

        if (name.Contains("working-set", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gc-heap-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gc-committed", StringComparison.OrdinalIgnoreCase))
            return "MB";

        if (name.Contains("loh-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("poh-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gen-0-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gen-1-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gen-2-size", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bytes", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("alloc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("heap", StringComparison.OrdinalIgnoreCase))
            return "B";

        if (name.EndsWith("-time", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-time-", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("latency", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("elapsed", StringComparison.OrdinalIgnoreCase))
            return "ms";

        if (name.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("total", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("price", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("revenue", StringComparison.OrdinalIgnoreCase))
            return "$";

        return "count";
    }
}