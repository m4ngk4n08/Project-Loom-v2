using System;
using System.Threading;
using System.Threading.Tasks;
using Loom.Telemetry.Alerting;
using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

public class AlertBuilderTests
{
    [Fact]
    public void AlertBuilder_Constructor_SetsName()
    {
        // Arrange & Act
        var builder = new AlertBuilder("TestAlert");

        // Assert - name is verified through Build()
        var rule = builder.Build();
        Assert.Equal("TestAlert", rule.Name);
    }

    [Fact]
    public void AlertBuilder_When_SetsMetricNameAndCondition()
    {
        // Arrange
        var builder = new AlertBuilder("TestAlert");

        // Act
        builder.When("TestMetric", agg => agg.Count > 100);
        var rule = builder.Build();

        // Assert
        Assert.Equal("TestMetric", rule.MetricName);
        Assert.True(rule.Condition(new MetricAggregate("TestMetric", 150, 0, 0, 0)));
        Assert.False(rule.Condition(new MetricAggregate("TestMetric", 50, 0, 0, 0)));
    }

    [Fact]
    public void AlertBuilder_InWindow_SetsWindow()
    {
        // Arrange
        var builder = new AlertBuilder("TestAlert");
        var window = TimeSpan.FromMinutes(10);

        // Act
        builder.InWindow(window);
        var rule = builder.Build();

        // Assert
        Assert.Equal(window, rule.Window);
    }

    [Fact]
    public void AlertBuilder_Notify_AddsTargetType()
    {
        // Arrange
        var builder = new AlertBuilder("TestAlert");

        // Act
        builder.Notify<ConsoleAlertTarget>();
        var rule = builder.Build();

        // Assert
        Assert.Single(rule.TargetTypes);
        Assert.Contains(typeof(ConsoleAlertTarget), rule.TargetTypes);
    }

    [Fact]
    public void AlertBuilder_MultipleNotify_AddsMultipleTargets()
    {
        // Arrange
        var builder = new AlertBuilder("TestAlert");

        // Act
        builder.Notify<ConsoleAlertTarget>();
        builder.Notify<TestAlertTarget>();
        var rule = builder.Build();

        // Assert
        Assert.Equal(2, rule.TargetTypes.Count);
        Assert.Contains(typeof(ConsoleAlertTarget), rule.TargetTypes);
        Assert.Contains(typeof(TestAlertTarget), rule.TargetTypes);
    }

    [Fact]
    public void AlertBuilder_FluentChaining_Works()
    {
        // Arrange & Act
        var rule = new AlertBuilder("ChainTest")
            .When("TestMetric", agg => agg.Average > 50)
            .InWindow(TimeSpan.FromMinutes(15))
            .Notify<ConsoleAlertTarget>()
            .Build();

        // Assert
        Assert.Equal("ChainTest", rule.Name);
        Assert.Equal("TestMetric", rule.MetricName);
        Assert.Equal(TimeSpan.FromMinutes(15), rule.Window);
        Assert.Single(rule.TargetTypes);
        Assert.True(rule.Condition(new MetricAggregate("TestMetric", 1, 75, 100, 90)));
    }

    [Fact]
    public void AlertBuilder_DefaultWindow_IsFiveMinutes()
    {
        // Arrange & Act
        var rule = new AlertBuilder("DefaultTest").Build();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), rule.Window);
    }

    [Fact]
    public void AlertBuilder_WithoutWhen_HasDefaultCondition()
    {
        // Arrange & Act
        var rule = new AlertBuilder("NoCondition").Build();

        // Assert
        Assert.False(rule.Condition(new MetricAggregate("M", 100, 100, 100, 100)));
    }

    [Fact]
    public void AlertBuilder_ComplexCondition_Works()
    {
        // Arrange & Act
        var rule = new AlertBuilder("ComplexTest")
            .When("Metric", agg => agg.Count > 100 && agg.P99 > 1000 || agg.Max > 5000)
            .Build();

        // Assert
        Assert.True(rule.Condition(new MetricAggregate("M", 150, 500, 3000, 1500)));  // Count and P99
        Assert.True(rule.Condition(new MetricAggregate("M", 50, 500, 6000, 500)));     // Max only
        Assert.False(rule.Condition(new MetricAggregate("M", 150, 500, 3000, 500)));   // Count but not P99 or Max
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(10, false)]
    [InlineData(15, false)]
    [InlineData(30, false)]
    [InlineData(60, false)]
    public void AlertBuilder_WindowDurations_AreValid(int minutes, bool shouldFail)
    {
        // Arrange & Act
        var exception = Record.Exception(() =>
        {
            var rule = new AlertBuilder("WindowTest")
                .InWindow(TimeSpan.FromMinutes(minutes))
                .Build();
            Assert.Equal(TimeSpan.FromMinutes(minutes), rule.Window);
        });

        // Assert
        if (shouldFail)
            Assert.NotNull(exception);
        else
            Assert.Null(exception);
    }
}

// Test helper alert target
public class TestAlertTarget : IAlertTarget
{
    public Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
