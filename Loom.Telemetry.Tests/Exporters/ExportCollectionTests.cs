using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Loom.Storage;
using Loom.Telemetry.Exporters;
using Xunit;

namespace Loom.Telemetry.Tests.Exporters;

public sealed class ExportCollectionTests : IDisposable
{
    public ExportCollectionTests()
    {
        LoomMetrics.ResetForTesting();
    }

    public void Dispose()
    {
        LoomMetrics.ResetForTesting();
    }

    [Fact]
    public async Task ExportCollectionHostedService_NoMetrics_WritesEmptyBatch()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(100) };
        var service = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(250); // Wait for at least 2 ticks
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - no batches should be written (empty batches are filtered)
        var hasData = channel.Reader.TryRead(out _);
        Assert.False(hasData);
    }

    [Fact(Skip = "Flaky due to PeriodicTimer timing and test isolation issues")]
    public async Task ExportCollectionHostedService_WithMetrics_CollectsAndWritesBatch()
    {
        // Arrange
        var metricName = $"test.counter.{Guid.NewGuid()}";
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(200) };
        var service = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);

        // Act - Start service first, then record metrics
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50); // Let service initialize

        // Record metrics after service starts
        LoomMetrics.RecordCounter(metricName, 1.0);
        LoomMetrics.RecordCounter(metricName, 2.0);

        await Task.Delay(600); // Wait for collection
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var hasData = channel.Reader.TryRead(out var batch);
        Assert.True(hasData);
        Assert.NotNull(batch);
        Assert.True(batch.Entries.Length > 0);

        var entry = Array.Find(batch.Entries, e => e.MetricName == metricName);
        Assert.NotNull(entry);
        Assert.Equal(MetricType.Counter, entry.Type);
        Assert.True(entry.Records.Length >= 2);
    }

    [Fact]
    public async Task ExportCollectionHostedService_MultipleMetrics_CollectsAll()
    {
        // Arrange
        var counter = $"test.counter.{Guid.NewGuid()}";
        var gauge = $"test.gauge.{Guid.NewGuid()}";
        var histogram = $"test.histogram.{Guid.NewGuid()}";

        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(100) };
        var service = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);

        // Record different metric types
        LoomMetrics.RecordCounter(counter, 5.0);
        LoomMetrics.RecordGauge(gauge, 10.5);
        LoomMetrics.RecordHistogram(histogram, 25.0);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(250);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        var hasData = channel.Reader.TryRead(out var batch);
        Assert.True(hasData);
        Assert.NotNull(batch);
        Assert.True(batch.Entries.Length >= 3);

        // Verify all metric types were collected
        Assert.Contains(batch.Entries, e => e.MetricName == counter && e.Type == MetricType.Counter);
        Assert.Contains(batch.Entries, e => e.MetricName == gauge && e.Type == MetricType.Gauge);
        Assert.Contains(batch.Entries, e => e.MetricName == histogram && e.Type == MetricType.Histogram);
    }

    [Fact]
    public async Task ExportCollectionHostedService_CollectsOnlyNewRecordsSinceLastCollection()
    {
        // Arrange
        var metricName = $"test.counter.{Guid.NewGuid()}";
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(100) };
        var service = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);

        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50); // Let service initialize

        // Record initial metrics after service starts
        LoomMetrics.RecordCounter(metricName, 1.0);

        // Wait for first collection (be generous with timing)
        await Task.Delay(300);
        var hasFirst = channel.Reader.TryRead(out var firstBatch);
        Assert.True(hasFirst, "First collection should have produced a batch");
        Assert.NotNull(firstBatch);
        var firstCount = Array.Find(firstBatch.Entries, e => e.MetricName == metricName)?.Records.Length ?? 0;

        // Record more metrics
        LoomMetrics.RecordCounter(metricName, 2.0);
        LoomMetrics.RecordCounter(metricName, 3.0);

        // Wait for second collection (be generous with timing)
        await Task.Delay(200);
        var hasSecond = channel.Reader.TryRead(out var secondBatch);
        Assert.True(hasSecond, "Second collection should have produced a batch");
        Assert.NotNull(secondBatch);
        var secondEntry = Array.Find(secondBatch.Entries, e => e.MetricName == metricName);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - second batch should only have the 2 new records
        Assert.NotNull(secondEntry);
        Assert.Equal(2, secondEntry.Records.Length);
    }

    [Fact]
    public async Task ExportCollectionHostedService_RespectCollectionInterval()
    {
        // Arrange
        var metricName = $"test.counter.{Guid.NewGuid()}";
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(500) };
        var service = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);

        LoomMetrics.RecordCounter(metricName, 1.0);

        // Act
        var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        // Check at 250ms - should have no batch yet (interval is 500ms)
        await Task.Delay(250);
        var hasEarly = channel.Reader.TryRead(out _);
        Assert.False(hasEarly);

        // Wait for collection to happen (be very generous)
        await Task.Delay(600);

        // Poll for batch with retry (PeriodicTimer can have jitter)
        var hasLate = false;
        for (int i = 0; i < 3; i++)
        {
            if (channel.Reader.TryRead(out _))
            {
                hasLate = true;
                break;
            }
            await Task.Delay(100);
        }

        Assert.True(hasLate);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }
}
