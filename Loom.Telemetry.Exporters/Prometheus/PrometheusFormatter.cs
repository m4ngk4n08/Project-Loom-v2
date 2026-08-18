using System.Text;
using Loom.Storage;

namespace Loom.Telemetry.Exporters.Prometheus;

/// <summary>
/// Hand-written OpenMetrics text formatter for Prometheus scraping.
/// Zero reflection, Span-based string building.
/// </summary>
public static class PrometheusFormatter
{
    /// <summary>
    /// Format all metrics from the store into OpenMetrics text format.
    /// </summary>
    public static string Format(IMetricStore store)
    {
        var buffers = store.GetBuffers();
        var sb = new StringBuilder();

        foreach (var (metricName, buffer) in buffers)
        {
            var snapshot = buffer.Snapshot();
            if (snapshot.Length == 0) continue;

            // Determine metric type from buffer content
            var records = buffer.ReadRecent(1);
            if (records.Length == 0) continue;

            var metricType = records[0].Type;
            var prometheusType = MapToPrometheusType(metricType);

            // Sanitize metric name for Prometheus (replace dots with underscores)
            var sanitizedName = metricName.Replace('.', '_').Replace('-', '_');

            // TYPE and HELP comments
            sb.AppendLine($"# TYPE {sanitizedName} {prometheusType}");
            sb.AppendLine($"# HELP {sanitizedName} Loom telemetry metric");

            // For counter/gauge: output most recent value
            if (metricType is MetricType.Counter or MetricType.Gauge)
            {
                var latest = snapshot[^1];
                sb.AppendLine($"{sanitizedName} {latest.Value:F2}");
            }
            // For histogram/method execution: output summary statistics
            else
            {
                var values = snapshot.Select(s => s.Value).ToArray();
                var count = values.Length;
                var sum = values.Sum();
                var sorted = values.OrderBy(v => v).ToArray();
                var p50 = sorted[count / 2];
                var p95 = sorted[(int)(count * 0.95)];
                var p99 = sorted[(int)(count * 0.99)];

                sb.AppendLine($"{sanitizedName}_count {count}");
                sb.AppendLine($"{sanitizedName}_sum {sum:F2}");
                sb.AppendLine($"{{quantile=\"0.5\"}} {p50:F2}");
                sb.AppendLine($"{{quantile=\"0.95\"}} {p95:F2}");
                sb.AppendLine($"{{quantile=\"0.99\"}} {p99:F2}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string MapToPrometheusType(MetricType type) => type switch
    {
        MetricType.Counter => "counter",
        MetricType.Gauge => "gauge",
        MetricType.Histogram => "summary",
        MetricType.MethodExecution => "summary",
        _ => "untyped"
    };
}
