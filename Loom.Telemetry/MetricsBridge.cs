using System.Diagnostics.Metrics;

namespace Loom.Telemetry;

/// <summary>
/// Republishes LoomRuntime's ring-buffer writes through System.Diagnostics.Metrics so
/// any EventPipe-aware tool (dotnet-counters, Loom.DevTools, or a generic APM agent)
/// can observe them — without those tools needing to know anything about ring buffers,
/// tag interning, or Loom-specific wire formats.
/// </summary>
internal static class MetricsBridge
{
    private static readonly Meter Meter = new("Loom.Telemetry", "1.0.0");
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Counter<long>> Counters = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Histogram<double>> Histograms = new();

    public static void PublishCounter(string name, long increment) =>
        Counters.GetOrAdd(name, n => Meter.CreateCounter<long>(n)).Add(increment);

    public static void PublishHistogram(string name, double value) =>
        Histograms.GetOrAdd(name, n => Meter.CreateHistogram<double>(n)).Record(value);
}
