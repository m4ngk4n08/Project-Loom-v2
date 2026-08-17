using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Loom.Telemetry.Alerting;
using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

public class AlertDispatchTests
{
    [Fact]
    public async Task AlertDispatchHostedService_NoNotifications_DoesNotDispatch()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var targets = new List<IAlertTarget>();
        var testTarget = new TrackingAlertTarget();
        targets.Add(testTarget);

        var service = new AlertDispatchHostedService(channel, targets);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, testTarget.NotificationCount);
    }

    [Fact]
    public async Task AlertDispatchHostedService_SingleNotification_DispatchesToTarget()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var targets = new List<IAlertTarget>();
        var testTarget = new TrackingAlertTarget();
        targets.Add(testTarget);

        var service = new AlertDispatchHostedService(channel, targets);

        var notification = CreateTestNotification(typeof(TrackingAlertTarget));

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(notification);
        await Task.Delay(100); // Give time to process
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, testTarget.NotificationCount);
        Assert.Equal("TestAlert", testTarget.LastNotification?.Rule.Name);
    }

    [Fact]
    public async Task AlertDispatchHostedService_MultipleNotifications_DispatchesAll()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var targets = new List<IAlertTarget>();
        var testTarget = new TrackingAlertTarget();
        targets.Add(testTarget);

        var service = new AlertDispatchHostedService(channel, targets);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        for (int i = 0; i < 5; i++)
        {
            var notification = CreateTestNotification(typeof(TrackingAlertTarget));
            await channel.Writer.WriteAsync(notification);
        }

        await Task.Delay(200); // Give time to process all
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(5, testTarget.NotificationCount);
    }

    [Fact]
    public async Task AlertDispatchHostedService_MultipleTargets_DispatchesToAll()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var targets = new List<IAlertTarget>();
        var target1 = new TrackingAlertTarget();
        var target2 = new TrackingAlertTarget();
        targets.Add(target1);
        targets.Add(target2);

        var service = new AlertDispatchHostedService(channel, targets);

        var notification = CreateTestNotification(typeof(TrackingAlertTarget));

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(notification);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, target1.NotificationCount);
        Assert.Equal(1, target2.NotificationCount);
    }

    [Fact]
    public async Task AlertDispatchHostedService_TargetThrows_ContinuesDispatching()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var targets = new List<IAlertTarget>();
        var failingTarget = new FailingAlertTarget();
        var successTarget = new TrackingAlertTarget();
        targets.Add(failingTarget);
        targets.Add(successTarget);

        var service = new AlertDispatchHostedService(channel, targets);

        var notification = CreateTestNotification(typeof(FailingAlertTarget), typeof(TrackingAlertTarget));

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(notification);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - success target should still receive notification
        Assert.Equal(1, successTarget.NotificationCount);
    }

    [Fact]
    public async Task AlertDispatchHostedService_OnlyDispatchesToMatchingTargets()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var targets = new List<IAlertTarget>();
        var target1 = new TrackingAlertTarget();
        var target2 = new SecondTrackingTarget();
        targets.Add(target1);
        targets.Add(target2);

        var service = new AlertDispatchHostedService(channel, targets);

        // Notification only for TrackingAlertTarget
        var notification = CreateTestNotification(typeof(TrackingAlertTarget));

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(notification);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, target1.NotificationCount);
        Assert.Equal(0, target2.NotificationCount); // Should not receive
    }

    [Fact]
    public async Task AlertDispatchHostedService_CancellationToken_StopsDispatching()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<AlertNotification>();
        var targets = new List<IAlertTarget>();
        var testTarget = new SlowAlertTarget();
        targets.Add(testTarget);

        var service = new AlertDispatchHostedService(channel, targets);

        var notification = CreateTestNotification(typeof(SlowAlertTarget));

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(notification);
        await Task.Delay(50); // Cancel while processing
        cts.Cancel();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await service.StopAsync(CancellationToken.None);
        });

        // Assert - should not throw
        Assert.Null(exception);
    }

    // Helper methods and classes
    private static AlertNotification CreateTestNotification(params Type[] targetTypes)
    {
        var rule = new AlertRule("TestAlert", "TestMetric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.Count > 0
        };

        foreach (var targetType in targetTypes)
        {
            rule.TargetTypes.Add(targetType);
        }

        var aggregate = new MetricAggregate("TestMetric", 100, 75.0, 150.0, 120.0);
        return new AlertNotification(rule, aggregate, DateTime.UtcNow);
    }
}

// Test helper targets
public class TrackingAlertTarget : IAlertTarget
{
    public int NotificationCount { get; private set; }
    public AlertNotification? LastNotification { get; private set; }

    public Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        NotificationCount++;
        LastNotification = notification;
        return Task.CompletedTask;
    }
}

public class SecondTrackingTarget : IAlertTarget
{
    public int NotificationCount { get; private set; }

    public Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        NotificationCount++;
        return Task.CompletedTask;
    }
}

public class FailingAlertTarget : IAlertTarget
{
    public Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        throw new InvalidOperationException("Simulated failure");
    }
}

public class SlowAlertTarget : IAlertTarget
{
    public async Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        await Task.Delay(5000, ct); // Long delay
    }
}
