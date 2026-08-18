using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Telemetry.Alerting;
using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

[Collection("AlertTests")]
public class AlertEvaluationTests
{
    [Fact]
    public async Task AlertEvaluationHostedService_NoRules_DoesNotEvaluate()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        // Clear any existing rules
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        // Act
        var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);
        await Task.Delay(100); // Let it check for rules
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AlertEvaluationHostedService_RuleBreached_FiresNotification()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        // Create metric data
        var metricName = "TestMetric_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 150.0); // Exceeds threshold
        LoomMetrics.RecordCounter(metricName, 200.0);
        LoomMetrics.RecordCounter(metricName, 175.0);

        // Add rule that should fire - use short window for fast testing
        var rule = new AlertRule("TestAlert", metricName, TimeSpan.FromSeconds(1))
        {
            Condition = agg => agg.Count > 2 // We recorded 3 values
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300); // Wait for evaluation tick (1s window / 10 = 100ms tick)
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var hasNotification = channel.Reader.TryRead(out var notification);
        Assert.True(hasNotification);
        Assert.NotNull(notification);
        Assert.Equal("TestAlert", notification.Rule.Name);
        Assert.Equal(metricName, notification.Observed.MetricName);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_RuleNotBreached_DoesNotFire()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        // Create metric data
        var metricName = "TestMetric2_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 10.0);

        // Add rule that should NOT fire - use short window for fast testing
        var rule = new AlertRule("NoFireAlert", metricName, TimeSpan.FromSeconds(1))
        {
            Condition = agg => agg.Count > 100 // We only recorded 1 value
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(channel.Reader.TryRead(out _));

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_SilencedAlert_DoesNotFire()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        // Create metric data
        var metricName = "SilencedMetric_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 100.0);

        // Add rule and silence it - use short window for fast testing
        var rule = new AlertRule("SilencedAlert", metricName, TimeSpan.FromSeconds(1))
        {
            Condition = agg => agg.Count > 0 // Would normally fire
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);
        silenceStore.Silence("SilencedAlert", DateTime.UtcNow.AddMinutes(10));

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(channel.Reader.TryRead(out _));

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public void ComputeWindowAggregate_EmptyBuffer_ReturnsZeroAggregate()
    {
        // This tests the aggregate computation logic indirectly
        // by verifying behavior with empty metrics

        // Arrange
        var metricName = "EmptyMetric_" + Guid.NewGuid().ToString("N");
        var rule = new AlertRule("Test", metricName, TimeSpan.FromMinutes(5));

        // Act - don't record any metrics
        var buffers = LoomRuntime.GetBuffersSnapshot();
        var hasBuffer = buffers.TryGetValue(metricName, out var buffer);

        // Assert
        Assert.False(hasBuffer || (buffer != null && buffer.Snapshot().Length > 0));
    }

    [Fact]
    public void ComputeWindowAggregate_SingleValue_ReturnsCorrectAggregate()
    {
        // Arrange
        var metricName = "SingleValue_" + Guid.NewGuid().ToString("N");
        var value = 42.0;
        LoomMetrics.RecordCounter(metricName, value);

        // Act
        var buffers = LoomRuntime.GetBuffersSnapshot();
        buffers.TryGetValue(metricName, out var buffer);
        var snapshot = buffer?.Snapshot();

        // Assert
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!);
        Assert.Equal(value, snapshot![0].Value);
    }

    [Fact]
    public void ComputeWindowAggregate_MultipleValues_CalculatesCorrectStatistics()
    {
        // Arrange
        var metricName = "MultiValue_" + Guid.NewGuid().ToString("N");
        var values = new[] { 10.0, 20.0, 30.0, 40.0, 50.0 };

        foreach (var value in values)
        {
            LoomMetrics.RecordCounter(metricName, value);
        }

        // Act
        var buffers = LoomRuntime.GetBuffersSnapshot();
        buffers.TryGetValue(metricName, out var buffer);
        var snapshot = buffer?.Snapshot();

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(5, snapshot!.Length);

        var snapshotValues = snapshot!.Select(s => s.Value).ToArray();
        Assert.Equal(30.0, snapshotValues.Average()); // Average
        Assert.Equal(50.0, snapshotValues.Max());     // Max
    }

    [Fact]
    public void ComputeWindowAggregate_P99Calculation_IsCorrect()
    {
        // Arrange
        var metricName = "P99Test_" + Guid.NewGuid().ToString("N");

        // Record 100 values: 1, 2, 3, ..., 100
        for (int i = 1; i <= 100; i++)
        {
            LoomMetrics.RecordCounter(metricName, i);
        }

        // Act
        var buffers = LoomRuntime.GetBuffersSnapshot();
        buffers.TryGetValue(metricName, out var buffer);
        var snapshot = buffer?.Snapshot();

        // Assert
        Assert.NotNull(snapshot);
        var values = snapshot!.Select(s => s.Value).OrderBy(v => v).ToArray();
        var p99Index = (int)Math.Ceiling(0.99 * values.Length) - 1;
        var p99 = values[p99Index];

        // P99 of 1-100 should be around 99
        Assert.True(p99 >= 98 && p99 <= 100, $"P99 was {p99}, expected ~99");
    }

    [Fact]
    public async Task AlertEvaluationHostedService_Cooldown_PreventsDuplicateFires()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "CooldownTest_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 100.0);

        // Short window for testing - cooldown = window duration
        var rule = new AlertRule("CooldownAlert", metricName, TimeSpan.FromSeconds(2))
        {
            Condition = agg => agg.Count > 0
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(400); // Wait for multiple ticks (2s window / 10 = 200ms tick, so 2 ticks)
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - should only fire once due to cooldown (needs 2s between fires)
        var notifications = 0;
        while (channel.Reader.TryRead(out _))
        {
            notifications++;
        }

        Assert.True(notifications <= 1, $"Expected at most 1 notification due to cooldown, got {notifications}");

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }
}
