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
                Unit = store.GetUnit(kvp.Key).Symbol(),
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

}