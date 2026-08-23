using System;
using Loom.Telemetry.Interfaces;

namespace Loom.Telemetry.Sampling;

/// <summary>
/// Escape hatch rule that always records metrics matching a predicate.
/// Used to ensure important metrics (errors, slow operations, critical paths) are never skipped.
/// </summary>
public sealed class AlwaysRecordRule : ISamplingRule
{
    private readonly Func<string, TimeSpan?, Exception?, bool> _predicate;

    public int Priority => 100; // High priority (evaluated first)

    /// <summary>
    /// Create an always-record rule with a predicate.
    /// </summary>
    /// <param name="predicate">Predicate that returns true for metrics that should always be recorded</param>
    public AlwaysRecordRule(Func<string, TimeSpan?, Exception?, bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool ShouldRecord(string metricName, TimeSpan? duration, Exception? exception)
    {
        // This is just a marker - actual short-circuit logic is in LoomSampling
        return _predicate(metricName, duration, exception);
    }

    /// <summary>
    /// Get the predicate for external evaluation (used by LoomSampling).
    /// </summary>
    internal Func<string, TimeSpan?, Exception?, bool> Predicate => _predicate;
}
