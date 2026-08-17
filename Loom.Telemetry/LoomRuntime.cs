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
        // Record property changes as gauges
        if (value is IConvertible convertible)
        {
            var numericValue = Convert.ToDouble(convertible);
            LoomMetrics.RecordGauge(metricName, numericValue);
        }
    }
}
