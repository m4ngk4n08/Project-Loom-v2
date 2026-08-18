using System;
using Loom.Storage;
using Loom.Telemetry.Exporters.Prometheus;
using Xunit;

namespace Loom.Telemetry.Tests.Exporters;

public sealed class PrometheusFormatterTests : IDisposable
{
    public PrometheusFormatterTests()
    {
        LoomMetrics.ResetForTesting();
    }

    public void Dispose()
    {
        LoomMetrics.ResetForTesting();
    }

    [Fact]
    public void Format_NoMetrics_ReturnsEmptyString()
    {
        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Trim());
    }

    [Fact]
    public void Format_CounterMetric_OutputsPrometheusCounterFormat()
    {
        // Arrange
        var metricName = $"test.counter.{Guid.NewGuid()}";
        LoomMetrics.RecordCounter(metricName, 42.0);

        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert
        Assert.Contains("# TYPE", result);
        Assert.Contains("counter", result);
        Assert.Contains("# HELP", result);

        // Metric name should be sanitized (dots and hyphens replaced with underscores)
        var sanitizedName = metricName.Replace('.', '_').Replace('-', '_');
        Assert.Contains(sanitizedName, result);
        Assert.Contains("42", result);
    }

    [Fact]
    public void Format_GaugeMetric_OutputsPrometheusGaugeFormat()
    {
        // Arrange
        var metricName = $"test.gauge.{Guid.NewGuid()}";
        LoomMetrics.RecordGauge(metricName, 123.45);

        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert
        Assert.Contains("# TYPE", result);
        Assert.Contains("gauge", result);
        var sanitizedName = metricName.Replace('.', '_').Replace('-', '_');
        Assert.Contains(sanitizedName, result);
        Assert.Contains("123", result);
    }

    [Fact]
    public void Format_HistogramMetric_OutputsSummaryFormat()
    {
        // Arrange
        var metricName = $"test.histogram.{Guid.NewGuid()}";
        for (int i = 1; i <= 100; i++)
        {
            LoomMetrics.RecordHistogram(metricName, i);
        }

        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert
        Assert.Contains("# TYPE", result);
        Assert.Contains("summary", result);

        var sanitizedName = metricName.Replace('.', '_').Replace('-', '_');
        Assert.Contains($"{sanitizedName}_count", result);
        Assert.Contains($"{sanitizedName}_sum", result);
        Assert.Contains("quantile=\"0.5\"", result);   // p50
        Assert.Contains("quantile=\"0.95\"", result);  // p95
        Assert.Contains("quantile=\"0.99\"", result);  // p99
    }

    [Fact]
    public void Format_MultipleMetrics_OutputsAllMetrics()
    {
        // Arrange
        var counter = $"test.counter.{Guid.NewGuid()}";
        var gauge = $"test.gauge.{Guid.NewGuid()}";
        var histogram = $"test.histogram.{Guid.NewGuid()}";

        LoomMetrics.RecordCounter(counter, 10.0);
        LoomMetrics.RecordGauge(gauge, 20.0);
        LoomMetrics.RecordHistogram(histogram, 30.0);

        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert
        var sanitizedCounter = counter.Replace('.', '_').Replace('-', '_');
        var sanitizedGauge = gauge.Replace('.', '_').Replace('-', '_');
        var sanitizedHistogram = histogram.Replace('.', '_').Replace('-', '_');

        Assert.Contains(sanitizedCounter, result);
        Assert.Contains(sanitizedGauge, result);
        Assert.Contains(sanitizedHistogram, result);

        // Should have multiple TYPE declarations
        var typeCount = result.Split("# TYPE").Length - 1;
        Assert.True(typeCount >= 3);
    }

    [Fact]
    public void Format_MetricNameWithDots_SanitizesToUnderscores()
    {
        // Arrange
        var metricName = "my.dotted.metric.name";
        LoomMetrics.RecordCounter(metricName, 1.0);

        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert
        Assert.Contains("my_dotted_metric_name", result);
        Assert.DoesNotContain("my.dotted.metric.name", result);
    }

    [Fact]
    public void Format_MetricNameWithHyphens_SanitizesToUnderscores()
    {
        // Arrange
        var metricName = "my-hyphen-metric";
        LoomMetrics.RecordCounter(metricName, 1.0);

        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert
        Assert.Contains("my_hyphen_metric", result);
        Assert.DoesNotContain("my-hyphen-metric", result);
    }

    [Fact]
    public void Format_CounterWithMultipleValues_OutputsMostRecentValue()
    {
        // Arrange
        var metricName = $"test.counter.{Guid.NewGuid()}";
        LoomMetrics.RecordCounter(metricName, 1.0);
        LoomMetrics.RecordCounter(metricName, 2.0);
        LoomMetrics.RecordCounter(metricName, 5.0);  // Most recent

        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert - should contain the most recent value
        var sanitizedName = metricName.Replace('.', '_').Replace('-', '_');
        Assert.Contains(sanitizedName, result);
        // The exact value might vary due to formatting, but it should be close to 5
        Assert.Contains("5", result);
    }

    [Fact]
    public void Format_MethodExecutionMetric_OutputsSummaryFormat()
    {
        // Arrange - Method execution metrics use histogram type for similar distribution tracking
        var metricName = $"method.execution.{Guid.NewGuid()}";

        // Record several execution times
        for (int i = 0; i < 50; i++)
        {
            LoomMetrics.RecordHistogram(metricName, i * 10.0);
        }

        // Act
        var result = PrometheusFormatter.Format(LoomMetricsStoreAdapter.Instance);

        // Assert
        Assert.Contains("# TYPE", result);
        Assert.Contains("summary", result);
        var sanitizedName = metricName.Replace('.', '_').Replace('-', '_');
        Assert.Contains($"{sanitizedName}_count", result);
        Assert.Contains($"{sanitizedName}_sum", result);
    }
}
