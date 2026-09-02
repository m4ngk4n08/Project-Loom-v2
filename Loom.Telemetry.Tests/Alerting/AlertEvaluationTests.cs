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

public class AlertEvaluationTests
{
    [Fact]
    public async Task AlertEvaluationHostedService_NoRules_FiresNothing()
    {
        // Arrange
        var registry = new AlertRuleRegistry();
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

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
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

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
        registry.Add(rule);

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
    }

    [Fact]
    public async Task AlertEvaluationHostedService_RuleNotBreached_DoesNotFire()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        // Create metric data
        var metricName = "TestMetric2_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 10.0);

        // Add rule that should NOT fire - use short window for fast testing
        var rule = new AlertRule("NoFireAlert", metricName, TimeSpan.FromSeconds(1))
        {
            Condition = agg => agg.Count > 100 // We only recorded 1 value
        };
        registry.Add(rule);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AlertEvaluationHostedService_SilencedAlert_DoesNotFire()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        // Create metric data
        var metricName = "SilencedMetric_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 100.0);

        // Add rule and silence it - use short window for fast testing
        var rule = new AlertRule("SilencedAlert", metricName, TimeSpan.FromSeconds(1))
        {
            Condition = agg => agg.Count > 0 // Would normally fire
        };
        registry.Add(rule);
        silenceStore.Silence("SilencedAlert", DateTime.UtcNow.AddMinutes(10));

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(channel.Reader.TryRead(out _));
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
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "CooldownTest_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 100.0);

        // Short window for testing - cooldown = window duration
        var rule = new AlertRule("CooldownAlert", metricName, TimeSpan.FromSeconds(2))
        {
            Condition = agg => agg.Count > 0
        };
        registry.Add(rule);

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
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "StaysActive_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromSeconds(2);
        var rule = new AlertRule("StaysActiveAlert", metricName, window)
        {
            Condition = agg => agg.Count > 0
        };
        registry.Add(rule);

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
    }

    [Fact]
    public async Task AlertEvaluationHostedService_ConditionClears_EmitsExactlyOneResolved()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "ResolveTest_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("ResolveAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        registry.Add(rule);

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
    }

    [Fact]
    public async Task AlertEvaluationHostedService_AlreadyResolved_DoesNotEmitSecondResolved()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "NoDoubleResolve_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("NoDoubleResolveAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        registry.Add(rule);

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
    }

    [Fact]
    public async Task AlertEvaluationHostedService_FullCycle_FiresResolvesFiresAgain()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "FullCycle_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("FullCycleAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        registry.Add(rule);

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
    }

    [Fact]
    public async Task AlertEvaluationHostedService_Resolution_IgnoresCooldown()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

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
        registry.Add(rule);

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
    }

    [Fact]
    public async Task AlertEvaluationHostedService_Silence_GatesFiringNotResolving()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "SilenceResolves_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("SilenceResolvesAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        registry.Add(rule);

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
    }

    [Fact]
    public async Task AlertEvaluationHostedService_SilencedFromStart_NeverFires()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "SilencedFromStart_" + Guid.NewGuid().ToString("N");
        var rule = new AlertRule("SilencedFromStartAlert", metricName, TimeSpan.FromSeconds(1))
        {
            Condition = agg => agg.Count > 0
        };
        registry.Add(rule);
        silenceStore.Silence("SilencedFromStartAlert", DateTime.UtcNow.AddMinutes(10));

        LoomMetrics.RecordCounter(metricName, 100.0);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AlertEvaluationHostedService_NoData_EmitsNeitherFiringNorResolved()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "NoDataGap_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(400);
        var rule = new AlertRule("NoDataGapAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        registry.Add(rule);

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
    }

    [Fact]
    public void ComputeTickInterval_EmptyList_ReturnsIdleTickInterval()
    {
        var result = AlertEvaluationHostedService.ComputeTickInterval([]);

        Assert.Equal(AlertEvaluationHostedService.IdleTickInterval, result);
    }

    [Fact]
    public void ComputeTickInterval_OneRule_ReturnsWindowDividedByTen()
    {
        var rule = new AlertRule("SingleRule", "SomeMetric", TimeSpan.FromSeconds(10));

        var result = AlertEvaluationHostedService.ComputeTickInterval([rule]);

        Assert.Equal(TimeSpan.FromSeconds(1), result);
    }

    [Fact]
    public void ComputeTickInterval_ThreeRules_ReturnsSmallestWindowDividedByTen()
    {
        var ruleA = new AlertRule("RuleA", "MetricA", TimeSpan.FromSeconds(10));
        var ruleB = new AlertRule("RuleB", "MetricB", TimeSpan.FromSeconds(2)); // smallest, not first
        var ruleC = new AlertRule("RuleC", "MetricC", TimeSpan.FromSeconds(20));

        var result = AlertEvaluationHostedService.ComputeTickInterval([ruleA, ruleB, ruleC]);

        Assert.Equal(TimeSpan.FromMilliseconds(200), result);
    }

    [Fact]
    public async Task AlertEvaluationHostedService_RuleRegisteredAfterStart_IsEvaluated()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        // Act - start with an EMPTY registry, so the old code would have returned
        // immediately and never entered its loop.
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        var metricName = "RegisteredAfterStart_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 100.0);

        var rule = new AlertRule("RegisteredAfterStartAlert", metricName, TimeSpan.FromSeconds(1))
        {
            Condition = agg => agg.Count > 0
        };
        registry.Add(rule);

        await Task.Delay(700); // idle tick picks up the rule, retimed tick evaluates it

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var hasNotification = channel.Reader.TryRead(out var notification);
        Assert.True(hasNotification);
        Assert.NotNull(notification);
        Assert.Equal("RegisteredAfterStartAlert", notification.Rule.Name);
    }

    [Fact]
    public void ResolveNoDataGrace_NoDataGraceUnset_ReturnsWindowTimesThree()
    {
        var rule = new AlertRule("GraceUnset", "SomeMetric", TimeSpan.FromSeconds(10));

        var result = AlertEvaluationHostedService.ResolveNoDataGrace(rule);

        Assert.Equal(TimeSpan.FromSeconds(30), result);
    }

    [Fact]
    public void ResolveNoDataGrace_NoDataGraceSet_ReturnsThatValueIgnoringWindow()
    {
        var rule = new AlertRule("GraceSet", "SomeMetric", TimeSpan.FromSeconds(10))
        {
            NoDataGrace = TimeSpan.FromSeconds(5)
        };

        var result = AlertEvaluationHostedService.ResolveNoDataGrace(rule);

        Assert.Equal(TimeSpan.FromSeconds(5), result);
    }

    [Fact]
    public void HasExpired_ElapsedLessThanGrace_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 10, DateTimeKind.Utc);
        var lastDataSeen = new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc);
        var grace = TimeSpan.FromSeconds(10);

        Assert.False(AlertEvaluationHostedService.HasExpired(now, lastDataSeen, grace));
    }

    [Fact]
    public void HasExpired_ElapsedEqualsGrace_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 15, DateTimeKind.Utc);
        var lastDataSeen = new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc);
        var grace = TimeSpan.FromSeconds(10);

        Assert.True(AlertEvaluationHostedService.HasExpired(now, lastDataSeen, grace));
    }

    [Fact]
    public async Task AlertEvaluationHostedService_MetricGoesSilentPastGrace_ResolvesAsNoData()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "GoesSilent_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromSeconds(1);
        var rule = new AlertRule("GoesSilentAlert", metricName, window)
        {
            Condition = agg => agg.Count > 0,
            NoDataGrace = TimeSpan.FromMilliseconds(400)
        };
        registry.Add(rule);

        using var recordCts = new CancellationTokenSource();
        var recordTask = RecordPeriodicallyAsync(metricName, 100.0, TimeSpan.FromMilliseconds(50), recordCts.Token);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300); // let it fire (window/10 = 100ms tick)

        // Stop recording - metric goes silent
        recordCts.Cancel();
        await recordTask;

        await Task.Delay(2000); // window drains, then NoDataGrace (400ms) elapses

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        var firing = notifications.Where(n => n.State == AlertState.Firing).ToList();
        var resolved = notifications.Where(n => n.State == AlertState.Resolved).ToList();

        Assert.NotEmpty(firing);
        Assert.Single(resolved);
        Assert.Equal(AlertResolutionReason.NoData, resolved[0].ResolutionReason);
        Assert.Equal(firing[0].FiredAt, resolved[0].FiredAt);
    }

    [Fact]
    public async Task AlertEvaluationHostedService_MetricSilentWithinGrace_DoesNotResolve()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "SilentWithinGrace_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromSeconds(1);
        var rule = new AlertRule("SilentWithinGraceAlert", metricName, window)
        {
            Condition = agg => agg.Count > 0,
            NoDataGrace = TimeSpan.FromSeconds(30)
        };
        registry.Add(rule);

        using var recordCts = new CancellationTokenSource();
        var recordTask = RecordPeriodicallyAsync(metricName, 100.0, TimeSpan.FromMilliseconds(50), recordCts.Token);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(300); // let it fire

        // Stop recording - metric goes silent, but well within the 30s grace
        recordCts.Cancel();
        await recordTask;

        await Task.Delay(2000);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - no Resolved notification (30s grace far exceeds the wait). The
        // rule's Window also doubles as its re-notify cooldown (pre-existing,
        // unrelated behavior), so a redundant Firing re-notification landing in
        // this window is not itself a failure - only a Resolved would be.
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        Assert.NotEmpty(notifications);
        Assert.All(notifications, n => Assert.Equal(AlertState.Firing, n.State));
    }

    [Fact]
    public async Task AlertEvaluationHostedService_ConditionClears_ResolvesAsConditionCleared()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "ClearsAsCondition_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("ClearsAsConditionAlert", metricName, window)
        {
            Condition = agg => agg.Max > 50
        };
        registry.Add(rule);

        LoomMetrics.RecordCounter(metricName, 100.0);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(150); // let the fire tick land

        // Keep the window non-empty with low values so the alert resolves via
        // condition-cleared, not no-data.
        using var lowValueCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var recordTask = RecordPeriodicallyAsync(metricName, 10.0, TimeSpan.FromMilliseconds(50), lowValueCts.Token);

        await Task.Delay(1400);
        lowValueCts.Cancel();
        await recordTask;
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        var resolved = notifications.Where(n => n.State == AlertState.Resolved).ToList();
        Assert.Single(resolved);
        Assert.Equal(AlertResolutionReason.ConditionCleared, resolved[0].ResolutionReason);
    }

    [Fact]
    public async Task AlertEvaluationHostedService_RuleRemovedWhileFiring_DoesNotResolveOrThrow()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "RemovedWhileFiring_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        var rule = new AlertRule("RemovedWhileFiringAlert", metricName, window)
        {
            Condition = agg => agg.Count > 0
        };
        registry.Add(rule);

        using var recordCts = new CancellationTokenSource();
        var recordTask = RecordPeriodicallyAsync(metricName, 100.0, TimeSpan.FromMilliseconds(50), recordCts.Token);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(250); // window/10 = 60ms tick - let it fire

        // Deleting a rule is not a request to be told the alert recovered: pruning its
        // state must stay silent rather than synthesising a Resolved notification.
        Assert.True(registry.Remove("RemovedWhileFiringAlert"));
        await Task.Delay(600); // several more ticks with the rule gone

        recordCts.Cancel();
        await recordTask;
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        Assert.NotEmpty(notifications);
        Assert.DoesNotContain(notifications, n => n.State == AlertState.Resolved);
        // Still running: the loop survived the rule vanishing mid-flight.
        Assert.Null(service.ExecuteTask?.Exception);
    }

    [Fact]
    public async Task AlertEvaluationHostedService_RuleReAddedAfterRemoval_FiresAgainAsANewAlert()
    {
        // Arrange
        var registry = new AlertRuleRegistry();

        var channel = Channel.CreateUnbounded<AlertNotification>();
        var silenceStore = new InMemorySilenceStore();
        var service = new AlertEvaluationHostedService(channel, registry, silenceStore, LoomMetricsStoreAdapter.Instance);

        var metricName = "ReAddedAfterRemoval_" + Guid.NewGuid().ToString("N");
        var window = TimeSpan.FromMilliseconds(600);
        const string ruleName = "ReAddedAfterRemovalAlert";
        registry.Add(new AlertRule(ruleName, metricName, window) { Condition = agg => agg.Count > 0 });

        using var recordCts = new CancellationTokenSource();
        var recordTask = RecordPeriodicallyAsync(metricName, 100.0, TimeSpan.FromMilliseconds(50), recordCts.Token);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(250); // let it fire the first time

        Assert.True(registry.Remove(ruleName));
        await Task.Delay(200); // a few ticks with the rule absent, so its state is pruned

        // Re-added under the SAME name while the condition still holds. Without the prune,
        // _activeAlerts still carries the old entry, the rule is treated as already firing,
        // and the only notification that can follow is a cooldown re-notify carrying the
        // ORIGINAL FiredAt - never a fresh Firing transition.
        registry.Add(new AlertRule(ruleName, metricName, window) { Condition = agg => agg.Count > 0 });
        await Task.Delay(400);

        recordCts.Cancel();
        await recordTask;
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var notifications = new List<AlertNotification>();
        while (channel.Reader.TryRead(out var n)) notifications.Add(n);

        var firing = notifications.Where(n => n.State == AlertState.Firing).ToList();
        Assert.True(firing.Count >= 2, $"Expected at least 2 Firing notifications, got {firing.Count}");
        Assert.True(firing[^1].FiredAt > firing[0].FiredAt,
            $"Re-added rule reported a stale FiredAt: first={firing[0].FiredAt:O} last={firing[^1].FiredAt:O}");
    }
}
