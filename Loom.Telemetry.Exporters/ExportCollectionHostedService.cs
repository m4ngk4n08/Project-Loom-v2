using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Loom.Telemetry.Exporters;

/// <summary>
/// Background service that periodically collects metrics from ring buffers
/// and writes batches to the export channel.
/// </summary>
public sealed class ExportCollectionHostedService(
    Channel<MetricBatch> exportChannel,
    ExportOptions options) : BackgroundService
{
    private long _lastCollectionTicks = DateTime.UtcNow.Ticks;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CollectionInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            var batch = CollectBatch(now);

            // Only write if we have data
            if (batch.Entries.Length > 0)
            {
                exportChannel.Writer.TryWrite(batch);
            }

            _lastCollectionTicks = now.Ticks;
        }
    }

    private MetricBatch CollectBatch(DateTime collectedAt)
    {
        var buffers = LoomRuntime.GetBuffersSnapshot();
        var entries = new List<MetricBatchEntry>(buffers.Count);

        foreach (var (metricName, buffer) in buffers)
        {
            var records = buffer.ReadSince(_lastCollectionTicks);

            if (records.Length > 0)
            {
                // Determine metric type from first record
                var metricType = records[0].Type;

                entries.Add(new MetricBatchEntry
                {
                    MetricName = metricName,
                    Type = metricType,
                    Records = records
                });
            }
        }

        return new MetricBatch
        {
            CollectedAtUtc = collectedAt,
            Entries = entries.ToArray()
        };
    }
}
