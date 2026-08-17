using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Loom.Telemetry.Exporters;
using Xunit;

namespace Loom.Telemetry.Tests.Exporters;

public sealed class ExportDispatchTests
{
    [Fact]
    public async Task ExportDispatchHostedService_NoBatches_DoesNotDispatch()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var exporters = new List<IMetricExporter>();
        var testExporter = new TrackingExporter();
        exporters.Add(testExporter);
        var tracker = new ExportStatusTracker();

        var service = new ExportDispatchHostedService(channel, exporters, tracker);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, testExporter.ExportCount);
    }

    [Fact]
    public async Task ExportDispatchHostedService_SingleBatch_DispatchesToExporter()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var exporters = new List<IMetricExporter>();
        var testExporter = new TrackingExporter();
        exporters.Add(testExporter);
        var tracker = new ExportStatusTracker();

        var service = new ExportDispatchHostedService(channel, exporters, tracker);

        var batch = CreateTestBatch();

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(batch);
        await Task.Delay(100); // Give time to process
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, testExporter.ExportCount);
        Assert.NotNull(testExporter.LastBatch);
        Assert.Equal(batch.CollectedAtUtc, testExporter.LastBatch.CollectedAtUtc);
    }

    [Fact]
    public async Task ExportDispatchHostedService_MultipleBatches_DispatchesAll()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var exporters = new List<IMetricExporter>();
        var testExporter = new TrackingExporter();
        exporters.Add(testExporter);
        var tracker = new ExportStatusTracker();

        var service = new ExportDispatchHostedService(channel, exporters, tracker);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        for (int i = 0; i < 5; i++)
        {
            var batch = CreateTestBatch();
            await channel.Writer.WriteAsync(batch);
        }

        await Task.Delay(200); // Give time to process all
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(5, testExporter.ExportCount);
    }

    [Fact]
    public async Task ExportDispatchHostedService_MultipleExporters_DispatchesToAll()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var exporters = new List<IMetricExporter>();
        var exporter1 = new TrackingExporter();
        var exporter2 = new TrackingExporter();
        exporters.Add(exporter1);
        exporters.Add(exporter2);
        var tracker = new ExportStatusTracker();

        var service = new ExportDispatchHostedService(channel, exporters, tracker);

        var batch = CreateTestBatch();

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(batch);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, exporter1.ExportCount);
        Assert.Equal(1, exporter2.ExportCount);
    }

    [Fact]
    public async Task ExportDispatchHostedService_ExporterThrows_ContinuesDispatching()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var exporters = new List<IMetricExporter>();
        var failingExporter = new FailingExporter();
        var successExporter = new TrackingExporter();
        exporters.Add(failingExporter);
        exporters.Add(successExporter);
        var tracker = new ExportStatusTracker();

        var service = new ExportDispatchHostedService(channel, exporters, tracker);

        var batch = CreateTestBatch();

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(batch);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - success exporter should still receive batch
        Assert.Equal(1, successExporter.ExportCount);

        // Verify failure was tracked
        var statuses = tracker.GetStatuses();
        Assert.True(statuses.ContainsKey("Failing"));
        Assert.False(statuses["Failing"].IsHealthy);
        Assert.Equal(1, statuses["Failing"].TotalFailures);
    }

    [Fact]
    public async Task ExportDispatchHostedService_TracksSuccessStatus()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var exporters = new List<IMetricExporter>();
        var testExporter = new TrackingExporter();
        exporters.Add(testExporter);
        var tracker = new ExportStatusTracker();

        var service = new ExportDispatchHostedService(channel, exporters, tracker);

        var batch = CreateTestBatch();

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(batch);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var statuses = tracker.GetStatuses();
        Assert.True(statuses.ContainsKey("TestExporter"));
        Assert.True(statuses["TestExporter"].IsHealthy);
        Assert.Equal(1, statuses["TestExporter"].TotalExports);
        Assert.Equal(0, statuses["TestExporter"].TotalFailures);
        Assert.NotNull(statuses["TestExporter"].LastSuccessUtc);
    }

    [Fact]
    public async Task ExportDispatchHostedService_CancellationToken_StopsDispatching()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var exporters = new List<IMetricExporter>();
        var testExporter = new SlowExporter();
        exporters.Add(testExporter);
        var tracker = new ExportStatusTracker();

        var service = new ExportDispatchHostedService(channel, exporters, tracker);

        var batch = CreateTestBatch();

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(batch);
        await Task.Delay(50); // Cancel while processing
        cts.Cancel();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await service.StopAsync(CancellationToken.None);
        });

        // Assert - should not throw
        Assert.Null(exception);
    }

    // Helper method
    private static MetricBatch CreateTestBatch()
    {
        var entries = new[]
        {
            new MetricBatchEntry
            {
                MetricName = "test.metric",
                Type = MetricType.Counter,
                Records = new[]
                {
                    new MetricRecord("test.metric", MetricType.Counter, 42.0, DateTime.UtcNow.Ticks, null, null)
                }
            }
        };

        return new MetricBatch
        {
            CollectedAtUtc = DateTime.UtcNow,
            Entries = entries
        };
    }
}

// Test helper exporters
public sealed class TrackingExporter : IMetricExporter
{
    public string Name => "TestExporter";
    public int ExportCount { get; private set; }
    public MetricBatch? LastBatch { get; private set; }

    public Task ExportAsync(MetricBatch batch, CancellationToken ct)
    {
        ExportCount++;
        LastBatch = batch;
        return Task.CompletedTask;
    }
}

public sealed class FailingExporter : IMetricExporter
{
    public string Name => "Failing";

    public Task ExportAsync(MetricBatch batch, CancellationToken ct)
    {
        throw new InvalidOperationException("Simulated export failure");
    }
}

public sealed class SlowExporter : IMetricExporter
{
    public string Name => "Slow";

    public async Task ExportAsync(MetricBatch batch, CancellationToken ct)
    {
        await Task.Delay(5000, ct); // Long delay
    }
}
