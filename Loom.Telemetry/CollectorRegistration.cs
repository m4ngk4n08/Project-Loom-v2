using System;

namespace Loom.Telemetry;

/// <summary>
/// Metadata about a registered collector.
/// </summary>
public sealed class CollectorRegistration
{
    /// <summary>The collector instance</summary>
    public ILoomCollector Collector { get; }

    /// <summary>When this collector was registered</summary>
    public DateTime RegisteredAtUtc { get; }

    /// <summary>Last successful collection timestamp</summary>
    public DateTime? LastCollectionUtc { get; internal set; }

    /// <summary>Last error message (null if no errors)</summary>
    public string? LastError { get; internal set; }

    /// <summary>Total number of successful collections</summary>
    public long SuccessCount { get; internal set; }

    /// <summary>Total number of failed collections</summary>
    public long FailureCount { get; internal set; }

    /// <summary>Whether this collector is currently enabled</summary>
    public bool IsEnabled { get; internal set; }

    public CollectorRegistration(ILoomCollector collector)
    {
        Collector = collector ?? throw new ArgumentNullException(nameof(collector));
        RegisteredAtUtc = DateTime.UtcNow;
        IsEnabled = true;
    }

    public override string ToString()
    {
        var status = IsEnabled ? "Enabled" : "Disabled";
        var lastRun = LastCollectionUtc?.ToString("HH:mm:ss") ?? "Never";
        return $"{Collector.Name} ({status}) - Last: {lastRun}, Success: {SuccessCount}, Failures: {FailureCount}";
    }
}
