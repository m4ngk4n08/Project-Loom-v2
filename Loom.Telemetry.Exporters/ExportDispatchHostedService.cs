using System.Diagnostics;
using System.Threading.Channels;
using Loom.Telemetry.Exporters.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loom.Telemetry.Exporters;

/// <summary>
/// Background service that consumes metric batches from the export channel
/// and dispatches them to all registered exporters with per-exporter error isolation.
/// </summary>
public sealed class ExportDispatchHostedService(
    Channel<MetricBatch> exportChannel,
    IEnumerable<IMetricExporter> exporters,
    ExportStatusTracker statusTracker,
    ILogger<ExportDispatchHostedService>? logger = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in exportChannel.Reader.ReadAllAsync(stoppingToken))
        {
            foreach (var exporter in exporters)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    await exporter.ExportAsync(batch, stoppingToken);
                    var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    statusTracker.RecordSuccess(exporter.Name);
                    logger?.LogInformation(
                        "Exporter {Exporter} succeeded: {MetricCount} metric(s), {RecordCount} record(s) in {ElapsedMs:F1}ms",
                        exporter.Name, batch.Entries.Length,
                        batch.Entries.Sum(e => e.Records.Length), elapsedMs);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Per-exporter error isolation: one failed exporter must not
                    // crash the dispatcher or block other exporters from running.
                    statusTracker.RecordFailure(exporter.Name, ex.Message);
                    logger?.LogError(ex,
                        "Exporter {Exporter} FAILED: {Message}",
                        exporter.Name, ex.Message);
                }
            }
        }
    }
}
