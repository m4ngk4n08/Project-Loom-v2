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

    // Discovery (loom dev / loom dashboard) has nothing to observe until an instrument
    // exists on this meter: Counters/Histograms below are created lazily on first
    // Record*, so a running, correctly-instrumented process that hasn't recorded
    // anything yet is indistinguishable from one that never referenced Loom at all. This
    // ObservableGauge is polled by the runtime on every RefreshInterval regardless of
    // whether the app records anything, so the meter always has something to report.
    // It looks like dead code (constant value) — it is not. Deleting it silently
    // restores the "not Loom-instrumented" false negative against an idle app.
    private static readonly ObservableGauge<int> Up =
        Meter.CreateObservableGauge("loom.telemetry.up", () => 1);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Counter<long>> Counters = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Histogram<double>> Histograms = new();

    public static void PublishCounter(string name, long increment) =>
        Counters.GetOrAdd(name, n => Meter.CreateCounter<long>(n)).Add(increment);

    public static void PublishHistogram(string name, double value) =>
        Histograms.GetOrAdd(name, n => Meter.CreateHistogram<double>(n)).Record(value);
}
