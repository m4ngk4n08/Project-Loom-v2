using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Loom.Telemetry;
using Loom.Telemetry.Alerting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

[Collection("AlertTests")]
public class AlertIntegrationTests
{
    [Fact]
    public void ServiceCollectionExtensions_AddLoomAlerting_RegistersAllServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddLoomAlerting();
        var provider = services.BuildServiceProvider();

        // Assert
        var channel = provider.GetService<Channel<AlertNotification>>();
        var silenceStore = provider.GetService<ISilenceStore>();

        Assert.NotNull(channel);
        Assert.NotNull(silenceStore);
        Assert.IsType<InMemorySilenceStore>(silenceStore);
    }

    [Fact]
    public void ServiceCollectionExtensions_AddAlertTarget_RegistersTarget()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddLoomAlerting();
        services.AddAlertTarget<ConsoleAlertTarget>();
        var provider = services.BuildServiceProvider();

        // Assert
        var targets = provider.GetServices<IAlertTarget>().ToList();
        Assert.Single(targets);
        Assert.IsType<ConsoleAlertTarget>(targets[0]);
    }

    [Fact]
    public void ServiceCollectionExtensions_AddMultipleTargets_RegistersAll()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddLoomAlerting();
        services.AddAlertTarget<ConsoleAlertTarget>();
        services.AddAlertTarget<TrackingAlertTarget>();
        var provider = services.BuildServiceProvider();

        // Assert
        var targets = provider.GetServices<IAlertTarget>().ToList();
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public void LoomTelemetryOptions_AddAlert_AddsRuleToGlobalList()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
        var options = new LoomTelemetryOptions();

        // Act
        options.AddAlert("TestAlert", alert => alert
            .When("Metric", agg => agg.Count > 100)
            .InWindow(TimeSpan.FromMinutes(5))
            .Notify<ConsoleAlertTarget>());

        // Assert
        Assert.Single(LoomTelemetryOptionsAlertingExtensions.Rules);
        var rule = LoomTelemetryOptionsAlertingExtensions.Rules[0];
        Assert.Equal("TestAlert", rule.Name);
        Assert.Equal("Metric", rule.MetricName);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public void LoomTelemetryOptions_AddMultipleAlerts_AddsAllRules()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
        var options = new LoomTelemetryOptions();

        // Act
        options.AddAlert("Alert1", alert => alert
            .When("Metric1", agg => agg.Count > 100)
            .Notify<ConsoleAlertTarget>());

        options.AddAlert("Alert2", alert => alert
            .When("Metric2", agg => agg.Average > 50)
            .Notify<ConsoleAlertTarget>());

        // Assert
        Assert.Equal(2, LoomTelemetryOptionsAlertingExtensions.Rules.Count);
        Assert.Contains(LoomTelemetryOptionsAlertingExtensions.Rules, r => r.Name == "Alert1");
        Assert.Contains(LoomTelemetryOptionsAlertingExtensions.Rules, r => r.Name == "Alert2");

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task EndToEnd_MetricExceedsThreshold_AlertFires()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var services = new ServiceCollection();
        services.AddLoomAlerting();
        services.AddAlertTarget<TrackingAlertTarget>();

        var provider = services.BuildServiceProvider();
        var channel = provider.GetRequiredService<Channel<AlertNotification>>();
        var targets = provider.GetServices<IAlertTarget>().ToList();
        var silenceStore = provider.GetRequiredService<ISilenceStore>();

        var metricName = "E2EMetric_" + Guid.NewGuid().ToString("N");

        // Configure alert first - use short window for fast testing
        var options = new LoomTelemetryOptions();
        options.AddAlert("E2EAlert", alert => alert
            .When(metricName, agg => agg.Count > 5)
            .InWindow(TimeSpan.FromSeconds(2))
            .Notify<TrackingAlertTarget>());

        // Record metrics AFTER configuring alert
        for (int i = 0; i < 10; i++)
        {
            LoomMetrics.RecordCounter(metricName, 100.0);
        }

        // Act - start services
        var evaluationService = new AlertEvaluationHostedService(channel, silenceStore);
        var dispatchService = new AlertDispatchHostedService(channel, targets);

        var cts = new CancellationTokenSource();
        await evaluationService.StartAsync(cts.Token);
        await dispatchService.StartAsync(cts.Token);

        await Task.Delay(600); // Wait for evaluation and dispatch (2s window / 10 = 200ms tick, need 3 ticks)

        cts.Cancel();
        await evaluationService.StopAsync(CancellationToken.None);
        await dispatchService.StopAsync(CancellationToken.None);

        // Assert
        var trackingTarget = targets.OfType<TrackingAlertTarget>().First();
        Assert.Equal(1, trackingTarget.NotificationCount);
        Assert.Equal("E2EAlert", trackingTarget.LastNotification?.Rule.Name);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public async Task EndToEnd_SilencedAlert_DoesNotFire()
    {
        // Arrange
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();

        var services = new ServiceCollection();
        services.AddLoomAlerting();
        services.AddAlertTarget<TrackingAlertTarget>();

        var provider = services.BuildServiceProvider();
        var channel = provider.GetRequiredService<Channel<AlertNotification>>();
        var targets = provider.GetServices<IAlertTarget>().ToList();
        var silenceStore = provider.GetRequiredService<ISilenceStore>();

        var metricName = "SilencedE2E_" + Guid.NewGuid().ToString("N");
        LoomMetrics.RecordCounter(metricName, 100.0);

        // Configure and silence alert - use short window for fast testing
        var options = new LoomTelemetryOptions();
        options.AddAlert("SilencedE2EAlert", alert => alert
            .When(metricName, agg => agg.Count > 0)
            .InWindow(TimeSpan.FromSeconds(1))
            .Notify<TrackingAlertTarget>());

        silenceStore.Silence("SilencedE2EAlert", DateTime.UtcNow.AddMinutes(10));

        // Act
        var evaluationService = new AlertEvaluationHostedService(channel, silenceStore);
        var dispatchService = new AlertDispatchHostedService(channel, targets);

        var cts = new CancellationTokenSource();
        await evaluationService.StartAsync(cts.Token);
        await dispatchService.StartAsync(cts.Token);

        await Task.Delay(500);

        cts.Cancel();
        await evaluationService.StopAsync(CancellationToken.None);
        await dispatchService.StopAsync(CancellationToken.None);

        // Assert
        var trackingTarget = targets.OfType<TrackingAlertTarget>().First();
        Assert.Equal(0, trackingTarget.NotificationCount);

        // Cleanup
        LoomTelemetryOptionsAlertingExtensions.Rules.Clear();
    }

    [Fact]
    public void Channel_BoundedWithDropOldest_WorksCorrectly()
    {
        // Arrange
        var channel = Channel.CreateBounded<AlertNotification>(new BoundedChannelOptions(3)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var notifications = new[]
        {
            CreateTestNotification("Alert1"),
            CreateTestNotification("Alert2"),
            CreateTestNotification("Alert3"),
            CreateTestNotification("Alert4"), // Should drop Alert1
            CreateTestNotification("Alert5"), // Should drop Alert2
        };

        // Act
        foreach (var notification in notifications)
        {
            channel.Writer.TryWrite(notification);
        }

        // Assert - should have Alert3, Alert4, Alert5
        var results = new System.Collections.Generic.List<string>();
        while (channel.Reader.TryRead(out var notification))
        {
            results.Add(notification.Rule.Name);
        }

        Assert.Equal(3, results.Count);
        Assert.Contains("Alert3", results);
        Assert.Contains("Alert4", results);
        Assert.Contains("Alert5", results);
        Assert.DoesNotContain("Alert1", results);
        Assert.DoesNotContain("Alert2", results);
    }

    private static AlertNotification CreateTestNotification(string alertName)
    {
        var rule = new AlertRule(alertName, "TestMetric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.Count > 0
        };
        var aggregate = new MetricAggregate("TestMetric", 100, 50, 100, 90);
        return new AlertNotification(rule, aggregate, DateTime.UtcNow);
    }
}
