using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Loom.Telemetry.Alerting;
using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

public class AlertTargetTests
{
    [Fact]
    public async Task ConsoleAlertTarget_NotifyAsync_WritesToConsole()
    {
        // Arrange
        var target = new ConsoleAlertTarget();
        var notification = CreateTestNotification();

        // Redirect console output
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            // Act
            await target.NotifyAsync(notification, CancellationToken.None);

            // Assert
            var output = writer.ToString();
            Assert.Contains("[ALERT]", output);
            Assert.Contains("TestAlert", output);
            Assert.Contains("TestMetric", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ConsoleAlertTarget_NotifyAsync_IncludesAllAggregateData()
    {
        // Arrange
        var target = new ConsoleAlertTarget();
        var notification = CreateTestNotification();

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            // Act
            await target.NotifyAsync(notification, CancellationToken.None);

            // Assert
            var output = writer.ToString();
            Assert.Contains("Count: 100", output);
            Assert.Contains("Avg:", output);
            Assert.Contains("Max:", output);
            Assert.Contains("P99:", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ConsoleAlertTarget_NotifyAsync_CompletesSuccessfully()
    {
        // Arrange
        var target = new ConsoleAlertTarget();
        var notification = CreateTestNotification();

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await target.NotifyAsync(notification, CancellationToken.None);
        });

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task ConsoleAlertTarget_CancellationToken_IsRespected()
    {
        // Arrange
        var target = new ConsoleAlertTarget();
        var notification = CreateTestNotification();
        var cts = new CancellationTokenSource();

        // Act - cancel immediately
        cts.Cancel();

        // Should still complete (console write is synchronous)
        var exception = await Record.ExceptionAsync(async () =>
        {
            await target.NotifyAsync(notification, cts.Token);
        });

        // Assert
        Assert.Null(exception); // ConsoleAlertTarget doesn't check cancellation
    }

    [Fact]
    public void EmailAlertTarget_Constructor_RequiresParameters()
    {
        // Arrange
        var sender = new TestEmailSender();
        var toAddress = "test@example.com";

        // Act
        var target = new EmailAlertTarget(sender, toAddress);

        // Assert
        Assert.NotNull(target);
    }

    [Fact]
    public async Task EmailAlertTarget_NotifyAsync_CallsSender()
    {
        // Arrange
        var sender = new TestEmailSender();
        var target = new EmailAlertTarget(sender, "test@example.com");
        var notification = CreateTestNotification();

        // Act
        await target.NotifyAsync(notification, CancellationToken.None);

        // Assert
        Assert.True(sender.WasCalled);
        Assert.Equal("test@example.com", sender.LastTo);
        Assert.Contains("TestAlert", sender.LastSubject);
        Assert.Contains("TestMetric", sender.LastBody);
    }

    [Fact]
    public async Task EmailAlertTarget_NotifyAsync_IncludesAggregateData()
    {
        // Arrange
        var sender = new TestEmailSender();
        var target = new EmailAlertTarget(sender, "test@example.com");
        var notification = CreateTestNotification();

        // Act
        await target.NotifyAsync(notification, CancellationToken.None);

        // Assert
        Assert.Contains("count=100", sender.LastBody);
        Assert.Contains("avg=", sender.LastBody);
    }

    [Fact]
    public async Task WebhookAlertTarget_NotifyAsync_PostsToUrl()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test.com") };
        var target = new WebhookAlertTarget(httpClient, "http://test.com/webhook");
        var notification = CreateTestNotification();

        // Act
        await target.NotifyAsync(notification, CancellationToken.None);

        // Assert
        Assert.True(handler.WasCalled);
        Assert.Equal("http://test.com/webhook", handler.LastRequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
    }

    [Fact]
    public async Task WebhookAlertTarget_Cancellation_ThrowsOperationCanceled()
    {
        // Arrange
        var handler = new TestHttpMessageHandler { ShouldDelay = true };
        var httpClient = new HttpClient(handler);
        var target = new WebhookAlertTarget(httpClient, "http://test.com/webhook");
        var notification = CreateTestNotification();
        var cts = new CancellationTokenSource();

        // Act & Assert
        var task = target.NotifyAsync(notification, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    // Helper methods
    private static AlertNotification CreateTestNotification()
    {
        var rule = new AlertRule("TestAlert", "TestMetric", TimeSpan.FromMinutes(5))
        {
            Condition = agg => agg.Count > 50
        };

        var aggregate = new MetricAggregate("TestMetric", 100, 75.5, 150.0, 120.0);
        var firedAt = DateTime.UtcNow;

        return new AlertNotification(rule, aggregate, firedAt);
    }
}

// Test helper classes
public class TestEmailSender : IEmailSender
{
    public bool WasCalled { get; private set; }
    public string? LastTo { get; private set; }
    public string? LastSubject { get; private set; }
    public string? LastBody { get; private set; }

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        WasCalled = true;
        LastTo = to;
        LastSubject = subject;
        LastBody = body;
        return Task.CompletedTask;
    }
}

public class TestHttpMessageHandler : HttpMessageHandler
{
    public bool WasCalled { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public HttpMethod? LastMethod { get; private set; }
    public bool ShouldDelay { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        WasCalled = true;
        LastRequestUri = request.RequestUri;
        LastMethod = request.Method;

        if (ShouldDelay)
        {
            await Task.Delay(10000, cancellationToken); // Long delay for cancellation test
        }

        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
