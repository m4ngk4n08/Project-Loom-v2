using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Loom.Telemetry.Exporters;

/// <summary>
/// Background service that consumes metric batches from the export channel
/// and dispatches them to all registered exporters with per-exporter error isolation.
/// </summary>
public sealed class ExportDispatchHostedService(
    Channel<MetricBatch> exportChannel,
    IEnumerable<IMetricExporter> exporters,
    ExportStatusTracker statusTracker) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in exportChannel.Reader.ReadAllAsync(stoppingToken))
        {
            foreach (var exporter in exporters)
            {
                try
                {
                    await exporter.ExportAsync(batch, stoppingToken);
                    statusTracker.RecordSuccess(exporter.Name);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Per-exporter error isolation: one failed exporter must not
                    // crash the dispatcher or block other exporters from running.
                    statusTracker.RecordFailure(exporter.Name, ex.Message);
                }
            }
        }
    }
}
