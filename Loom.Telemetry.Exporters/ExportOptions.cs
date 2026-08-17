namespace Loom.Telemetry.Exporters;

/// <summary>
/// Configuration options for the metric export system.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>
    /// How often to collect and batch metrics for export.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan CollectionInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of pending batches in the export channel.
    /// When full, oldest batches are dropped (bounded backpressure).
    /// Default: 64 batches.
    /// </summary>
    public int ChannelCapacity { get; set; } = 64;
}
