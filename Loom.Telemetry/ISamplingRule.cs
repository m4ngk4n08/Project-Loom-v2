using System;

namespace Loom.Telemetry;

/// <summary>
/// Interface for sampling rules that determine whether a metric should be recorded.
/// Rules are evaluated BEFORE recording to filter out low-value, high-volume metrics.
/// </summary>
public interface ISamplingRule
{
    /// <summary>
    /// Determine whether a metric should be recorded based on its characteristics.
    /// </summary>
    /// <param name="metricName">Name of the metric being evaluated</param>
    /// <param name="duration">Duration (for timing metrics), null if not applicable</param>
    /// <param name="exception">Exception (if any), null for successful operations</param>
    /// <returns>True if the metric should be recorded, false to skip</returns>
    bool ShouldRecord(string metricName, TimeSpan? duration, Exception? exception);

    /// <summary>
    /// Priority of this rule (higher priority evaluated first).
    /// AlwaysRecord rules should have high priority to override sampling rules.
    /// </summary>
    int Priority { get; }
}
