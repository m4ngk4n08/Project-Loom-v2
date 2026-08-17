using System.Net.Http.Headers;
using System.Text.Json;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Exporters.GrafanaCloud;

/// <summary>
/// Exporter that pushes metrics to Grafana Cloud via HTTP POST.
/// Uses source-generated JSON serialization for Native AOT compatibility.
/// </summary>
public sealed class GrafanaCloudExporter(HttpClient httpClient, GrafanaCloudOptions options) : IMetricExporter
{
    public string Name => "GrafanaCloud";

    public async Task ExportAsync(MetricBatch batch, CancellationToken ct)
    {
        var payload = MapToGrafanaPayload(batch);

        using var request = new HttpRequestMessage(HttpMethod.Post, options.PushEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        var json = JsonSerializer.Serialize(payload, LoomJsonSerializerContext.Default.GrafanaMetricPayload);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static GrafanaMetricPayload MapToGrafanaPayload(MetricBatch batch)
    {
        var series = new List<GrafanaMetricSeries>();

        foreach (var entry in batch.Entries)
        {
            var dataPoints = entry.Records
                .Select(r => new GrafanaDataPoint
                {
                    Value = r.Value,
                    TimestampMs = r.TimestampUtcTicks / TimeSpan.TicksPerMillisecond
                })
                .ToArray();

            // Extract labels from tags
            var labels = entry.Records.FirstOrDefault().Tags
                ?.ToDictionary(t => t.Key, t => t.Value);

            series.Add(new GrafanaMetricSeries
            {
                Name = entry.MetricName,
                Type = entry.Type.ToString().ToLowerInvariant(),
                DataPoints = dataPoints,
                Labels = labels
            });
        }

        return new GrafanaMetricPayload { Series = series.ToArray() };
    }
}
