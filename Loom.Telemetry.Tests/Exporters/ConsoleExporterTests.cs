using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Loom.Telemetry.Exporters;
using Loom.Telemetry.Exporters.Console;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Exporters;

public sealed class ConsoleExporterTests
{
    [Fact]
    public async Task ConsoleExporter_ExportAsync_LogsBatchSummary()
    {
        // Arrange
        var logger = new FakeLogger<ConsoleExporter>();
        var exporter = new ConsoleExporter(logger);

        var batch = new MetricBatch
        {
            CollectedAtUtc = DateTime.UtcNow,
            Entries = new[]
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
            }
        };

        // Act
        await exporter.ExportAsync(batch, CancellationToken.None);

        // Assert
        Assert.True(logger.LogCount > 0);
        Assert.Contains(logger.Messages, m => m.Contains("Batch collected"));
        Assert.Contains(logger.Messages, m => m.Contains("test.metric"));
    }

    [Fact]
    public async Task ConsoleExporter_EmptyBatch_StillLogs()
    {
        // Arrange
        var logger = new FakeLogger<ConsoleExporter>();
        var exporter = new ConsoleExporter(logger);

        var batch = new MetricBatch
        {
            CollectedAtUtc = DateTime.UtcNow,
            Entries = Array.Empty<MetricBatchEntry>()
        };

        // Act
        await exporter.ExportAsync(batch, CancellationToken.None);

        // Assert
        Assert.True(logger.LogCount > 0);
        Assert.Contains(logger.Messages, m => m.Contains("0 metric entries"));
    }

    [Fact]
    public async Task ConsoleExporter_MultipleBatches_LogsEach()
    {
        // Arrange
        var logger = new FakeLogger<ConsoleExporter>();
        var exporter = new ConsoleExporter(logger);

        var batch1 = CreateTestBatch("metric1");
        var batch2 = CreateTestBatch("metric2");

        // Act
        await exporter.ExportAsync(batch1, CancellationToken.None);
        await exporter.ExportAsync(batch2, CancellationToken.None);

        // Assert
        Assert.Equal(4, logger.LogCount); // 2 batches × 2 log calls each
        Assert.Contains(logger.Messages, m => m.Contains("metric1"));
        Assert.Contains(logger.Messages, m => m.Contains("metric2"));
    }

    [Fact]
    public void ConsoleExporter_Name_ReturnsConsole()
    {
        // Arrange
        var logger = new FakeLogger<ConsoleExporter>();
        var exporter = new ConsoleExporter(logger);

        // Act & Assert
        Assert.Equal("Console", exporter.Name);
    }

    // Helper
    private static MetricBatch CreateTestBatch(string metricName)
    {
        return new MetricBatch
        {
            CollectedAtUtc = DateTime.UtcNow,
            Entries = new[]
            {
                new MetricBatchEntry
                {
                    MetricName = metricName,
                    Type = MetricType.Gauge,
                    Records = new[]
                    {
                        new MetricRecord(metricName, MetricType.Gauge, 10.0, DateTime.UtcNow.Ticks, null, null)
                    }
                }
            }
        };
    }
}

// Test helper - fake logger
public sealed class FakeLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = new();

    public List<string> Messages => _messages;
    public int LogCount => _messages.Count;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _messages.Add(message);
    }
}
