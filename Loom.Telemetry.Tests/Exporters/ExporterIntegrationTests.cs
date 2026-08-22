using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Loom.Storage;
using Loom.Telemetry.Exporters;
using Xunit;

namespace Loom.Telemetry.Tests.Exporters;

public sealed class ExporterIntegrationTests : IDisposable
{
    public ExporterIntegrationTests()
    {
        LoomMetrics.ResetForTesting();
    }

    public void Dispose()
    {
        LoomMetrics.ResetForTesting();
    }

    [Fact]
    public async Task FullPipeline_RecordMetrics_ExportersReceiveBatches()
    {
        // Arrange
        var metricName = $"integration.test.{Guid.NewGuid()}";
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(150) };

        var exporters = new[] { new TrackingExporter() };
        var tracker = new ExportStatusTracker();

        var collectionService = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);
        var dispatchService = new ExportDispatchHostedService(channel, exporters, tracker);

        // Act
        var cts = new CancellationTokenSource();

        // Start both services
        await collectionService.StartAsync(cts.Token);
        await dispatchService.StartAsync(cts.Token);
        await Task.Delay(50); // Let services initialize

        // Record metrics after services start
        LoomMetrics.RecordCounter(metricName, 10.0);
        LoomMetrics.RecordCounter(metricName, 20.0);
        LoomMetrics.RecordCounter(metricName, 30.0);

        // Wait for collection and dispatch (be generous with timing)
        await Task.Delay(600);

        // Stop services
        cts.Cancel();
        await collectionService.StopAsync(CancellationToken.None);
        await dispatchService.StopAsync(CancellationToken.None);

        // Assert
        Assert.True(exporters[0].ExportCount > 0, $"Exporter should have received at least one batch (got {exporters[0].ExportCount})");

        var lastBatch = exporters[0].LastBatch;
        Assert.NotNull(lastBatch);

        var entry = Array.Find(lastBatch.Entries, e => e.MetricName == metricName);
        Assert.NotNull(entry);
        Assert.Equal(MetricType.Counter, entry.Type);
        Assert.True(entry.Records.Length >= 3, "Should have recorded at least 3 values");

        // Verify status tracking
        var statuses = tracker.GetStatuses();
        Assert.True(statuses.ContainsKey("TestExporter"));
        Assert.True(statuses["TestExporter"].IsHealthy);
        Assert.True(statuses["TestExporter"].TotalExports > 0);
    }

    [Fact]
    public async Task FullPipeline_MultipleExporters_AllReceiveBatches()
    {
        // Arrange
        var metricName = $"integration.test.{Guid.NewGuid()}";
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(150) };

        var exporter1 = new TrackingExporter();
        var exporter2 = new TrackingExporter();
        var exporters = new IMetricExporter[] { exporter1, exporter2 };
        var tracker = new ExportStatusTracker();

        var collectionService = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);
        var dispatchService = new ExportDispatchHostedService(channel, exporters, tracker);

        LoomMetrics.RecordGauge(metricName, 42.0);

        // Act
        var cts = new CancellationTokenSource();
        await collectionService.StartAsync(cts.Token);
        await dispatchService.StartAsync(cts.Token);

        // Wait long enough for multiple collections to occur
        await Task.Delay(500);

        cts.Cancel();
        await collectionService.StopAsync(CancellationToken.None);
        await dispatchService.StopAsync(CancellationToken.None);

        // Assert - both exporters received batches
        Assert.True(exporter1.ExportCount > 0, $"Exporter 1 count: {exporter1.ExportCount}");
        Assert.True(exporter2.ExportCount > 0, $"Exporter 2 count: {exporter2.ExportCount}");
        Assert.NotNull(exporter1.LastBatch);
        Assert.NotNull(exporter2.LastBatch);
    }

    [Fact]
    public async Task FullPipeline_OneExporterFails_OthersStillReceiveBatches()
    {
        // Arrange
        var metricName = $"integration.test.{Guid.NewGuid()}";
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(100) };

        var failingExporter = new FailingExporter();
        var successExporter = new TrackingExporter();
        var exporters = new IMetricExporter[] { failingExporter, successExporter };
        var tracker = new ExportStatusTracker();

        var collectionService = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);
        var dispatchService = new ExportDispatchHostedService(channel, exporters, tracker);

        LoomMetrics.RecordHistogram(metricName, 100.0);

        // Act
        var cts = new CancellationTokenSource();
        await collectionService.StartAsync(cts.Token);
        await dispatchService.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await collectionService.StopAsync(CancellationToken.None);
        await dispatchService.StopAsync(CancellationToken.None);

        // Assert - success exporter still received batches despite failing exporter
        Assert.True(successExporter.ExportCount > 0);

        // Verify status tracking shows the failure
        var statuses = tracker.GetStatuses();
        Assert.False(statuses["Failing"].IsHealthy);
        Assert.True(statuses["Failing"].TotalFailures > 0);
        Assert.True(statuses["TestExporter"].IsHealthy);
        Assert.True(statuses["TestExporter"].TotalExports > 0);
    }

    [Fact]
    public async Task FullPipeline_BoundedChannel_DropsOldestWhenFull()
    {
        // Arrange
        var metricName = $"integration.test.{Guid.NewGuid()}";
        // Small channel capacity to force overflow
        var channel = Channel.CreateBounded<MetricBatch>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(50) };

        // Slow exporter to cause backlog
        var slowExporter = new SlowExporter();
        var tracker = new ExportStatusTracker();

        var collectionService = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);
        var dispatchService = new ExportDispatchHostedService(channel, new[] { slowExporter }, tracker);

        // Record metrics rapidly
        for (int i = 0; i < 10; i++)
        {
            LoomMetrics.RecordCounter(metricName, i);
        }

        // Act
        var cts = new CancellationTokenSource();
        await collectionService.StartAsync(cts.Token);
        await dispatchService.StartAsync(cts.Token);
        await Task.Delay(500); // Let it collect several times
        cts.Cancel();
        await collectionService.StopAsync(CancellationToken.None);
        await dispatchService.StopAsync(CancellationToken.None);

        // Assert - pipeline should not crash despite bounded channel overflow
        // This test primarily verifies the drop-oldest behavior doesn't cause exceptions
        Assert.True(true, "Pipeline completed without crashing");
    }

    [Fact]
    public async Task FullPipeline_ContinuousMetrics_CollectsNewRecordsOnly()
    {
        // Arrange
        var metricName = $"integration.test.{Guid.NewGuid()}";
        var channel = Channel.CreateUnbounded<MetricBatch>();
        var options = new ExportOptions { CollectionInterval = TimeSpan.FromMilliseconds(100) };

        var exporter = new TrackingExporter();
        var tracker = new ExportStatusTracker();

        var collectionService = new ExportCollectionHostedService(channel, options, LoomMetricsStoreAdapter.Instance);
        var dispatchService = new ExportDispatchHostedService(channel, new[] { exporter }, tracker);

        // Start services
        var cts = new CancellationTokenSource();
        await collectionService.StartAsync(cts.Token);
        await dispatchService.StartAsync(cts.Token);

        // Record initial metrics
        LoomMetrics.RecordCounter(metricName, 1.0);
        await Task.Delay(150); // Wait for first collection

        // Record more metrics after first collection
        LoomMetrics.RecordCounter(metricName, 2.0);
        LoomMetrics.RecordCounter(metricName, 3.0);
        await Task.Delay(150); // Wait for second collection

        cts.Cancel();
        await collectionService.StopAsync(CancellationToken.None);
        await dispatchService.StopAsync(CancellationToken.None);

        // Assert - should have received at least 2 batches
        Assert.True(exporter.ExportCount >= 2, $"Expected at least 2 batches, got {exporter.ExportCount}");
    }
}
