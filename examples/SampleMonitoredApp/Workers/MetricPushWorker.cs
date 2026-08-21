using Loom.Telemetry;
using System.Net.Http.Json;
using System.Text.Json;

namespace SampleMonitoredApp.Workers;

/// <summary>
/// Periodically pushes locally buffered metrics to the Loom.Web.Api ingest endpoint.
/// </summary>
public class MetricPushWorker : BackgroundService
{
    private readonly ILogger<MetricPushWorker> _logger;
    private readonly HttpClient _httpClient;
    private long _lastPushTicks;

    public MetricPushWorker(ILogger<MetricPushWorker> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5209") };
        _lastPushTicks = DateTime.UtcNow.Ticks;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricPushWorker started — pushing to http://localhost:5209/api/metrics/ingest");

        // Wait briefly for other workers to start generating metrics
        await Task.Delay(2000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var records = LoomMetrics.GetMetricsSince(TimeSpan.FromSeconds(2));

                if (records.Length > 0)
                {
                    var payload = new
                    {
                        metrics = records.Select(r => new
                        {
                            name = r.Name,
                            type = r.Type.ToString(),
                            value = r.Value,
                            timestamp = new DateTime(r.TimestampUtcTicks, DateTimeKind.Utc),
                            tags = r.Tags?.ToDictionary(t => t.Key, t => t.Value)
                        }).ToArray()
                    };

                    var response = await _httpClient.PostAsJsonAsync("/api/metrics/ingest", payload, stoppingToken);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogDebug("Pushed {Count} metrics to Loom API", records.Length);
                    }
                    else
                    {
                        _logger.LogWarning("Metric push failed: {Status}", response.StatusCode);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogDebug("Loom API not reachable: {Message}", ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "MetricPushWorker error");
            }

            // Periodically surface sampling statistics (zero-alloc counters read on a
            // non-hot-path) so operators see how much data sampling is suppressing.
            var stats = LoomSampling.GetStats();
            if (stats.EvaluatedCount > 0)
            {
                _logger.LogInformation(
                    "Sampling stats: evaluated={Evaluated} recorded={Recorded} dropped={Dropped} alwaysRecorded={AlwaysRecorded} dropRate={DropRate:P1} rules={Rules}",
                    stats.EvaluatedCount,
                    stats.EvaluatedCount - stats.DroppedCount,
                    stats.DroppedCount,
                    stats.AlwaysRecordedCount,
                    stats.DropRate,
                    stats.RuleCount);
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }
}
