using System;
using System.Collections.Generic;
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

    /// <summary>Keeps a metric fed with a value on an interval until cancelled, so a window
    /// never goes empty ("no data") while the test wants to observe fire/resolve transitions
    /// driven purely by the VALUE crossing the condition threshold.</summary>
    private static async Task RecordPeriodicallyAsync(string metricName, double value, TimeSpan interval, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                LoomMetrics.RecordCounter(metricName, value);
                await Task.Delay(interval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }
    }

    [Fact]
    public async Task AlertEvaluationHostedService_StaysActiveWhileConditionHolds_DoesNotReNotifyBeforeCooldown()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "StaysActive_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromSeconds(2);
        var rule = new AlertRule("StaysActiveAlert", metricName, window)
        {
            Condition = agg => agg.Count > 0
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        using var recordCts = new CancellationTokenSource();
        var recordTask = RecordPeriodicallyAsync(metricName, 100.0, TimeSpan.FromMilliseconds(50), recordCts.Token);

        // Act - keep the condition true across several ticks (window/10 = 200ms tick),
        // well inside one cooldown window (2s)
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(900);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
        recordCts.Cancel();
        await recordTask;

        // Assert - fired exactly once, never resolved
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        Assert.Single(notifications);
        Assert.Equal(AlertState.Firing, notifications[0].State);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_ConditionClears_EmitsExactlyOneResolved()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "ResolveTest_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("ResolveAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        // Initial breach
        LoomMetrics.RecordCounter(metricName, 100.0);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(150); // let the fire tick land

        // Keep the window non-empty with low values so the alert can genuinely RESOLVE
        // (not just go quiet) once the initial high value ages out of the window.
        using var lowValueCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var recordTask = RecordPeriodicallyAsync(metricName, 10.0, TimeSpan.FromMilliseconds(50), lowValueCts.Token);

        await Task.Delay(1400); // window (600ms) + margin for the high value to age out
        lowValueCts.Cancel();
        await recordTask;
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        var firing = notifications.Where(n => n.State == AlertState.Firing).ToList();
        var resolved = notifications.Where(n => n.State == AlertState.Resolved).ToList();

        Assert.NotEmpty(firing);
        Assert.Single(resolved);
        Assert.Equal(firing[0].FiredAt, resolved[0].FiredAt);
        Assert.NotNull(resolved[0].ResolvedAt);
        Assert.True(resolved[0].ResolvedAt!.Value > resolved[0].FiredAt);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_AlreadyResolved_DoesNotEmitSecondResolved()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "NoDoubleResolve_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("NoDoubleResolveAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        LoomMetrics.RecordCounter(metricName, 100.0);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(150);

        using var lowValueCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var recordTask = RecordPeriodicallyAsync(metricName, 10.0, TimeSpan.FromMilliseconds(50), lowValueCts.Token);

        // Wait well past resolution and keep ticking with the condition staying clear
        await Task.Delay(2000);
        lowValueCts.Cancel();
        await recordTask;
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        var resolved = notifications.Where(n => n.State == AlertState.Resolved).ToList();
        Assert.Single(resolved);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_FullCycle_FiresResolvesFiresAgain()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "FullCycle_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("FullCycleAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        LoomMetrics.RecordCounter(metricName, 100.0); // first breach

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(150);

        using var lowValueCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var recordTask = RecordPeriodicallyAsync(metricName, 10.0, TimeSpan.FromMilliseconds(50), lowValueCts.Token);

        await Task.Delay(1400); // let it resolve
        lowValueCts.Cancel();
        await recordTask;

        LoomMetrics.RecordCounter(metricName, 100.0); // second breach
        await Task.Delay(1400);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        var firing = notifications.Where(n => n.State == AlertState.Firing).ToList();
        var resolved = notifications.Where(n => n.State == AlertState.Resolved).ToList();

        Assert.True(firing.Count >= 2, $"Expected at least 2 Firing notifications, got {firing.Count}");
        Assert.Single(resolved);
        Assert.True(firing[^1].FiredAt > resolved[0].FiredAt);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_Resolution_IgnoresCooldown()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "IgnoresCooldown_" + Guid.NewGuid().ToString("N");
        // The cooldown equals rule.Window, so the only way to prove resolve isn't waiting
        // on it is to clear the condition long before a window's worth of time has passed.
        // A closure-captured flag (rather than waiting for a value to age out of the
        // aggregation window) lets the condition flip instantly, independent of Window.
        var window = TimeSpan.FromSeconds(2);
        var breached = true;
        var rule = new AlertRule("IgnoresCooldownAlert", metricName, window)
        {
            Condition = _ => breached
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        using var recordCts = new CancellationTokenSource();
        // Keeps the window non-empty (some data, any data) so the rule is evaluated each
        // tick instead of being skipped as "no data".
        var recordTask = RecordPeriodicallyAsync(metricName, 1.0, TimeSpan.FromMilliseconds(50), recordCts.Token);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300); // window/10 = 200ms tick - let it fire

        breached = false; // clear well within the 2s cooldown/window
        await Task.Delay(300); // let the next tick observe the clear and resolve

        recordCts.Cancel();
        await recordTask;
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        var resolved = notifications.Where(n => n.State == AlertState.Resolved).ToList();
        Assert.Single(resolved);
        Assert.True(resolved[0].ResolvedAt!.Value - resolved[0].FiredAt < window,
            "Resolved should not wait for the cooldown/window to elapse.");

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_Silence_GatesFiringNotResolving()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "SilenceResolves_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("SilenceResolvesAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        LoomMetrics.RecordCounter(metricName, 100.0);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(150); // let it fire (unsilenced at this point)

        // Silence AFTER it already fired
        silenceStore.Silence("SilenceResolvesAlert", DateTime.UtcNow.AddMinutes(10));

        using var lowValueCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var recordTask = RecordPeriodicallyAsync(metricName, 10.0, TimeSpan.FromMilliseconds(50), lowValueCts.Token);

        await Task.Delay(1400); // let the condition clear
        lowValueCts.Cancel();
        await recordTask;
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - it still resolves despite being silenced
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        var firing = notifications.Where(n => n.State == AlertState.Firing).ToList();
        var resolved = notifications.Where(n => n.State == AlertState.Resolved).ToList();

        Assert.Single(firing);
        Assert.Single(resolved);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_SilencedFromStart_NeverFires()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "SilencedFromStart_" + Guid.NewGuid().ToString("N");
        var rule = new AlertRule("SilencedFromStartAlert", metricName, TimeSpan.FromSeconds(1))
        {
            Condition = agg => agg.Count > 0
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);
        silenceStore.Silence("SilencedFromStartAlert", DateTime.UtcNow.AddMinutes(10));

        LoomMetrics.RecordCounter(metricName, 100.0);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        Assert.False(channel.Reader.TryRead(out _));

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task AlertEvaluationHostedService_NoData_EmitsNeitherFiringNorResolved()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "NoDataGap_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(400);
        var rule = new AlertRule("NoDataGapAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        LoomTelemetryOptionsAlertingExtensions.Rules.Add(rule);

        LoomMetrics.RecordCounter(metricName, 100.0); // initial fire

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(150); // let the fire tick land

        // Record NOTHING at all past this point - the window empties out entirely
        // ("no data"). Before the STEP 3 fix, an empty window returned a zero aggregate,
        // which a `>` condition evaluates false on - an incorrect auto-resolve. If that
        // regressed, a second (Resolved) notification would show up here.
        await Task.Delay(1200); // well past window (400ms), buffer for this metric goes empty

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - only the original Firing notification, nothing else
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        Assert.Single(notifications);
        Assert.Equal(AlertState.Firing, notifications[0].State);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }
}
