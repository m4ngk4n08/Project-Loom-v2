using System;
using System.Runtime.CompilerServices;

namespace Loom.Telemetry;

/// <summary>
/// Internal runtime for recording telemetry from source-generated instrumentation.
/// Used by [LoomProfile] and [LoomTrack] generated code.
/// </summary>
public static class LoomRuntime
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordMethodExecution(string metricName, TimeSpan elapsed, Exception? exception)
    {
        // Check sampling rules before recording
        if (!LoomSampling.ShouldRecord(metricName, elapsed, exception))
            return;

        // Record as MethodExecution metric
        if (exception != null)
        {
            LoomMetrics.RecordHistogram(
                metricName,
                elapsed.TotalMilliseconds,
                new MetricTag("exception", exception.GetType().Name)
            );
            LoomMetrics.RecordCounter($"{metricName}.errors", 1);
        }
        else
        {
            LoomMetrics.RecordHistogram(metricName, elapsed.TotalMilliseconds);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordPropertyChange<T>(string metricName, T value)
    {
        // Check sampling rules before recording (no duration for properties)
        if (!LoomSampling.ShouldRecord(metricName, null, null))
            return;

        // Record property changes as gauges
        if (value is IConvertible convertible)
        {
            var numericValue = Convert.ToDouble(convertible);
            LoomMetrics.RecordGauge(metricName, numericValue);
        }
    }

    /// <summary>
    /// Get snapshot of all metric buffers for query engine (Phase 10).
    /// Returns a dictionary mapping metric name to its ring buffer.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyDictionary<string, MetricBuffer> GetBuffersSnapshot()
    {
        return LoomMetrics.GetBuffersSnapshot();
    }
}
