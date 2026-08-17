using System;
using Loom.Telemetry.Alerting;
using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

public class AlertRuleTests
{
    [Fact]
    public void AlertRule_Constructor_SetsProperties()
    {
        // Arrange
        var name = "TestAlert";
        var metricName = "TestMetric";
        var window = TimeSpan.FromMinutes(5);

        // Act
        var rule = new AlertRule(name, metricName, window);

        // Assert
        Assert.Equal(name, rule.Name);
        Assert.Equal(metricName, rule.MetricName);
        Assert.Equal(window, rule.Window);
        Assert.NotNull(rule.Condition);
        Assert.NotNull(rule.TargetTypes);
        Assert.Empty(rule.TargetTypes);
    }

    [Fact]
    public void AlertRule_DefaultCondition_ReturnsFalse()
    {
        // Arrange
        var rule = new AlertRule("Test", "Metric", TimeSpan.FromMinutes(5));
        var aggregate = new MetricAggregate("Metric", 100, 50.0, 100.0, 90.0);

        // Act
        var result = rule.Condition(aggregate);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AlertRule_CustomCondition_Evaluates()
    {
        // Arrange
        var rule = new AlertRule("Test", "Metric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.Count > 50
        };
        var aggregate = new MetricAggregate("Metric", 100, 50.0, 100.0, 90.0);

        // Act
        var result = rule.Condition(aggregate);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MetricAggregate_Constructor_SetsAllProperties()
    {
        // Arrange & Act
        var aggregate = new MetricAggregate("TestMetric", 100, 50.5, 99.9, 95.0);

        // Assert
        Assert.Equal("TestMetric", aggregate.MetricName);
        Assert.Equal(100, aggregate.Count);
        Assert.Equal(50.5, aggregate.Average);
        Assert.Equal(99.9, aggregate.Max);
        Assert.Equal(95.0, aggregate.P99);
    }

    [Fact]
    public void MetricAggregate_ZeroValues_IsValid()
    {
        // Arrange & Act
        var aggregate = new MetricAggregate("Metric", 0, 0, 0, 0);

        // Assert
        Assert.Equal(0, aggregate.Count);
        Assert.Equal(0, aggregate.Average);
        Assert.Equal(0, aggregate.Max);
        Assert.Equal(0, aggregate.P99);
    }

    [Theory]
    [InlineData(100, 50.0, 100.0, 90.0, true)]   // Count > 50
    [InlineData(25, 50.0, 100.0, 90.0, false)]   // Count <= 50
    [InlineData(75, 30.0, 100.0, 90.0, true)]    // Count > 50
    public void AlertRule_CountCondition_EvaluatesCorrectly(long count, double avg, double max, double p99, bool expected)
    {
        // Arrange
        var rule = new AlertRule("Test", "Metric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.Count > 50
        };
        var aggregate = new MetricAggregate("Metric", count, avg, max, p99);

        // Act
        var result = rule.Condition(aggregate);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(100, 75.0, 100.0, 90.0, true)]   // Average > 50
    [InlineData(100, 30.0, 100.0, 90.0, false)]  // Average <= 50
    [InlineData(100, 50.1, 100.0, 90.0, true)]   // Average > 50
    public void AlertRule_AverageCondition_EvaluatesCorrectly(long count, double avg, double max, double p99, bool expected)
    {
        // Arrange
        var rule = new AlertRule("Test", "Metric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.Average > 50
        };
        var aggregate = new MetricAggregate("Metric", count, avg, max, p99);

        // Act
        var result = rule.Condition(aggregate);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(100, 50.0, 150.0, 90.0, true)]   // Max > 100
    [InlineData(100, 50.0, 75.0, 90.0, false)]   // Max <= 100
    [InlineData(100, 50.0, 100.1, 90.0, true)]   // Max > 100
    public void AlertRule_MaxCondition_EvaluatesCorrectly(long count, double avg, double max, double p99, bool expected)
    {
        // Arrange
        var rule = new AlertRule("Test", "Metric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.Max > 100
        };
        var aggregate = new MetricAggregate("Metric", count, avg, max, p99);

        // Act
        var result = rule.Condition(aggregate);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(100, 50.0, 100.0, 95.0, true)]   // P99 > 90
    [InlineData(100, 50.0, 100.0, 85.0, false)]  // P99 <= 90
    [InlineData(100, 50.0, 100.0, 90.1, true)]   // P99 > 90
    public void AlertRule_P99Condition_EvaluatesCorrectly(long count, double avg, double max, double p99, bool expected)
    {
        // Arrange
        var rule = new AlertRule("Test", "Metric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.P99 > 90
        };
        var aggregate = new MetricAggregate("Metric", count, avg, max, p99);

        // Act
        var result = rule.Condition(aggregate);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AlertRule_CombinedCondition_EvaluatesCorrectly()
    {
        // Arrange
        var rule = new AlertRule("Test", "Metric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.Count > 50 && agg.Average > 100
        };

        // Act & Assert
        Assert.True(rule.Condition(new MetricAggregate("M", 100, 150, 200, 180)));
        Assert.False(rule.Condition(new MetricAggregate("M", 100, 50, 200, 180)));   // Avg too low
        Assert.False(rule.Condition(new MetricAggregate("M", 25, 150, 200, 180)));   // Count too low
        Assert.False(rule.Condition(new MetricAggregate("M", 25, 50, 200, 180)));    // Both too low
    }
}
