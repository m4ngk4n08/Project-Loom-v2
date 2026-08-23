using Loom.Telemetry.Exporters.Interfaces;
using Microsoft.Extensions.Logging;

namespace Loom.Telemetry.Exporters.Console;

/// <summary>
/// Simple exporter that writes batch summaries to the console via ILogger.
/// Useful for development and debugging.
/// </summary>
public sealed class ConsoleExporter(ILogger<ConsoleExporter> logger) : IMetricExporter
{
    public string Name => "Console";

    public Task ExportAsync(MetricBatch batch, CancellationToken ct)
    {
        logger.LogInformation(
            "[Export] Batch collected at {Time} with {EntryCount} metric entries",
            batch.CollectedAtUtc,
            batch.Entries.Length);

        foreach (var entry in batch.Entries)
        {
            logger.LogInformation(
                "  {MetricName} ({Type}): {RecordCount} records",
                entry.MetricName,
                entry.Type,
                entry.Records.Length);
        }

        return Task.CompletedTask;
    }
}
