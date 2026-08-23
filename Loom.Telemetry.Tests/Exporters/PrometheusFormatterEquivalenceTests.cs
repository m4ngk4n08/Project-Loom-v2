using System;
using Loom.Storage;
using Loom.Telemetry.Exporters.Prometheus;
using Xunit;

namespace Loom.Telemetry.Tests.Exporters;

/// <summary>
/// Golden-output tests pinning PrometheusFormatter's exact byte-for-byte output.
/// Line endings are "\n" throughout: the Prometheus text exposition format (0.0.4)
/// requires LF regardless of host OS, so these are NOT OS-dependent captures - unlike
/// an earlier version of this file, which pinned "\r\n" because that's what
/// Environment.NewLine produced on the machine the original baseline was captured on.
/// That was the bug BACKLOG.md § 4.3 fixed (PrometheusFormatter now hardcodes "\n"),
/// so these goldens changed with it.
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
    public void Format_Counter_MatchesGoldenOutput()
    {
        LoomMetrics.RecordCounter("capture.counter.fixed", 42.0);

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(
            "# HELP capture_counter_fixed Loom telemetry metric\n" +
            "# TYPE capture_counter_fixed counter\n" +
            "capture_counter_fixed 42.00\n" +
            "\n",
            result);
    }

    [Fact]
    public void Format_Gauge_MatchesGoldenOutput()
    {
        LoomMetrics.RecordGauge("capture.gauge.fixed", 123.45);

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(
            "# HELP capture_gauge_fixed Loom telemetry metric\n" +
            "# TYPE capture_gauge_fixed gauge\n" +
            "capture_gauge_fixed 123.45\n" +
            "\n",
            result);
    }

    [Fact]
    public void Format_Histogram_MatchesGoldenOutput()
    {
        for (int i = 1; i <= 100; i++)
        {
            LoomMetrics.RecordHistogram("capture.histogram.fixed", i);
        }

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(
            "# HELP capture_histogram_fixed Loom telemetry metric\n" +
            "# TYPE capture_histogram_fixed summary\n" +
            "capture_histogram_fixed_count 100\n" +
            "capture_histogram_fixed_sum 5050.00\n" +
            "capture_histogram_fixed{quantile=\"0.5\"} 51.00\n" +
            "capture_histogram_fixed{quantile=\"0.95\"} 96.00\n" +
            "capture_histogram_fixed{quantile=\"0.99\"} 100.00\n" +
            "\n",
            result);
    }

    [Fact]
    public void Format_NoMetrics_MatchesGoldenOutput()
    {
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Format_CounterWithDifferentTagSets_EmitsTwoDistinctLabelledSeries()
    {
        LoomMetrics.RecordCounter("capture.counter.tagged", 10.0, new MetricTag("route", "a"));
        LoomMetrics.RecordCounter("capture.counter.tagged", 5.0, new MetricTag("route", "a"));
        LoomMetrics.RecordCounter("capture.counter.tagged", 7.0, new MetricTag("route", "b"));

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(
            "# HELP capture_counter_tagged Loom telemetry metric\n" +
            "# TYPE capture_counter_tagged counter\n" +
            "capture_counter_tagged{route=\"a\"} 15.00\n" +
            "capture_counter_tagged{route=\"b\"} 7.00\n" +
            "\n",
            result);
    }

    [Fact]
    public void Format_CounterWithSameTagsInDifferentOrder_MergesIntoOneSeries()
    {
        LoomMetrics.RecordCounter("capture.counter.reordered", 3.0,
            new MetricTag("a", "1"), new MetricTag("b", "2"));
        LoomMetrics.RecordCounter("capture.counter.reordered", 4.0,
            new MetricTag("b", "2"), new MetricTag("a", "1"));

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Equal(
            "# HELP capture_counter_reordered Loom telemetry metric\n" +
            "# TYPE capture_counter_reordered counter\n" +
            "capture_counter_reordered{a=\"1\",b=\"2\"} 7.00\n" +
            "\n",
            result);
    }

    [Fact]
    public void Format_CounterWithThreeIncrements_SumsRatherThanUsingLatest()
    {
        LoomMetrics.RecordCounter("capture.counter.increments", 1.0);
        LoomMetrics.RecordCounter("capture.counter.increments", 1.0);
        LoomMetrics.RecordCounter("capture.counter.increments", 1.0);

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Contains("capture_counter_increments 3.00\n", result);
        Assert.DoesNotContain("capture_counter_increments 1.00\n", result);
    }

    [Fact]
    public void Format_LabelValueWithQuoteAndBackslash_IsEscaped()
    {
        // Raw tag value: C:\temp\"quoted" - contains a backslash immediately followed
        // by a quote, which catches a formatter that escapes quotes before backslashes
        // (that ordering would double-escape the backslash the quote-escape introduces).
        var rawValue = "C:" + "\\" + "temp" + "\\" + "\"" + "quoted" + "\"";
        LoomMetrics.RecordCounter("capture.counter.escaped", 1.0, new MetricTag("path", rawValue));

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Built independently of the formatter's own escaping code: walk the raw value
        // and hand-apply backslash-then-quote escaping.
        var expected = new System.Text.StringBuilder();
        foreach (var ch in rawValue)
        {
            if (ch == '\\') expected.Append("\\\\");
            else if (ch == '"') expected.Append("\\\"");
            else expected.Append(ch);
        }

        Assert.Contains($"path=\"{expected}\"", result);
    }

    [Fact]
    public void Format_SummaryWithUserLabels_MergesQuantileIntoSameBraceGroup()
    {
        for (int i = 1; i <= 10; i++)
        {
            LoomMetrics.RecordHistogram("capture.histogram.labelled", i, new MetricTag("env", "prod"));
        }

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.Contains("capture_histogram_labelled_count{env=\"prod\"} 10\n", result);
        Assert.Contains("capture_histogram_labelled_sum{env=\"prod\"} 55.00\n", result);
        Assert.Contains("capture_histogram_labelled{env=\"prod\",quantile=\"0.5\"}", result);
        // Never split into two separate brace groups.
        Assert.DoesNotContain("}{", result);
    }

    [Fact]
    public void Format_MetricWithNoTags_EmitsNoEmptyBraces()
    {
        LoomMetrics.RecordCounter("capture.counter.untagged", 1.0);

        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        Assert.DoesNotContain("{}", result);
    }
}
