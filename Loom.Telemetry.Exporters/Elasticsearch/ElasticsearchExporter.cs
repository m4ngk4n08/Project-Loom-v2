using System.Text;

namespace Loom.Telemetry.Exporters.Elasticsearch;

/// <summary>
/// Exporter that pushes metrics to Elasticsearch using the Bulk API (NDJSON format).
/// Uses date-partitioned indices for time-series data.
/// </summary>
public sealed class ElasticsearchExporter(HttpClient httpClient, ElasticsearchOptions options) : IMetricExporter
{
    public string Name => "Elasticsearch";

    public async Task ExportAsync(MetricBatch batch, CancellationToken ct)
    {
        var bulkBody = BuildBulkRequestBody(batch);

        using var content = new StringContent(bulkBody, Encoding.UTF8, "application/x-ndjson");

        var bulkEndpoint = $"{options.Endpoint.TrimEnd('/')}/_bulk";
        using var response = await httpClient.PostAsync(bulkEndpoint, content, ct);
        response.EnsureSuccessStatusCode();
    }

    private string BuildBulkRequestBody(MetricBatch batch)
    {
        var sb = new StringBuilder();
        var indexName = string.Format(options.IndexPattern, batch.CollectedAtUtc);

        foreach (var entry in batch.Entries)
        {
            foreach (var record in entry.Records)
            {
                // Bulk action line
                sb.AppendLine($"{{\"index\":{{\"_index\":\"{indexName}\"}}}}");

                // Document line
                var timestamp = new DateTime(record.TimestampUtcTicks, DateTimeKind.Utc);
                sb.Append("{");
                sb.Append($"\"@timestamp\":\"{timestamp:O}\",");
                sb.Append($"\"metric_name\":\"{record.Name}\",");
                sb.Append($"\"metric_type\":\"{record.Type}\",");
                sb.Append($"\"value\":{record.Value:F2}");

                if (record.Tags is { Length: > 0 })
                {
                    sb.Append(",\"tags\":{");
                    for (int i = 0; i < record.Tags.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append($"\"{record.Tags[i].Key}\":\"{record.Tags[i].Value}\"");
                    }
                    sb.Append('}');
                }

                if (record.ExceptionType != null)
                {
                    sb.Append($",\"exception_type\":\"{record.ExceptionType}\"");
                }

                sb.AppendLine("}");
            }
        }

        return sb.ToString();
    }
}
