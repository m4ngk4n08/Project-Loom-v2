using System;
using Loom.Storage;
using Loom.Telemetry.Exporters.Prometheus;
using Xunit;

namespace Loom.Telemetry.Tests.Exporters;

/// <summary>
/// Golden-output equivalence tests: exact strings captured from the pre-rewrite
/// StringBuilder-based PrometheusFormatter (D2), pinned here so the IBufferWriter&lt;byte&gt;
/// rewrite is verified byte-for-byte identical, not just "contains the right substrings".
/// Line endings are \r\n because that's what Environment.NewLine produced on the machine
/// the baseline was captured on - both old and new implementations use the same
/// Environment.NewLine, so this is stable across the rewrite (would differ per-OS, but
/// so did the pre-rewrite implementation).
/// The histogram golden value below has been updated from the raw pre-rewrite capture to
/// reflect a deliberate post-rewrite fix: quantile lines now carry the metric-name prefix
/// (e.g. "capture_histogram_fixed{quantile=...}"), which the original StringBuilder
/// implementation omitted - a real OpenMetrics conformance bug, fixed with explicit
/// user confirmation rather than preserved as "equivalent".
/// </summary>
public sealed class PrometheusFormatterEquivalenceTests : IDisposable
{
    public PrometheusFormatterEquivalenceTests()
    {
        LoomMetrics.ResetForTesting();
    }

    public void Dispose()
    {
        LoomMetrics.ResetForTesting();
    }

    [Fact]
    public void Format_Counter_MatchesPreRewriteGoldenOutput()
    {
        LoomMetrics.RecordCounter("capture.counter.fixed", 42.0);

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(
            "# TYPE capture_counter_fixed counter\r\n" +
            "# HELP capture_counter_fixed Loom telemetry metric\r\n" +
            "capture_counter_fixed 42.00\r\n" +
            "\r\n",
            result);
    }

    [Fact]
    public void Format_Gauge_MatchesPreRewriteGoldenOutput()
    {
        LoomMetrics.RecordGauge("capture.gauge.fixed", 123.45);

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(
            "# TYPE capture_gauge_fixed gauge\r\n" +
            "# HELP capture_gauge_fixed Loom telemetry metric\r\n" +
            "capture_gauge_fixed 123.45\r\n" +
            "\r\n",
            result);
    }

    [Fact]
    public void Format_Histogram_MatchesPostFixGoldenOutput()
    {
        for (int i = 1; i <= 100; i++)
        {
            LoomMetrics.RecordHistogram("capture.histogram.fixed", i);
        }

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(
            "# TYPE capture_histogram_fixed summary\r\n" +
            "# HELP capture_histogram_fixed Loom telemetry metric\r\n" +
            "capture_histogram_fixed_count 100\r\n" +
            "capture_histogram_fixed_sum 5050.00\r\n" +
            "capture_histogram_fixed{quantile=\"0.5\"} 51.00\r\n" +
            "capture_histogram_fixed{quantile=\"0.95\"} 96.00\r\n" +
            "capture_histogram_fixed{quantile=\"0.99\"} 100.00\r\n" +
            "\r\n",
            result);
    }

    [Fact]
    public void Format_NoMetrics_MatchesPreRewriteGoldenOutput()
    {
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(string.Empty, result);
    }
}
