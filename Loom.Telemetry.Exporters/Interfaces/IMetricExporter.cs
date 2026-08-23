using Loom.Telemetry.Exporters;

namespace Loom.Telemetry.Exporters.Interfaces;

/// <summary>
/// Interface for metric exporters that push or expose metrics to external systems.
/// </summary>
public interface IMetricExporter
{
    /// <summary>
    /// Unique name identifying this exporter (e.g., "Prometheus", "Console").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Export a batch of metrics to the external system.
    /// </summary>
    /// <param name="batch">The batch of metrics to export.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ExportAsync(MetricBatch batch, CancellationToken ct);
}
