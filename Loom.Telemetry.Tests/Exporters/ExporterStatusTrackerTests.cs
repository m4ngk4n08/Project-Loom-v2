using System;
using Loom.Telemetry.Exporters;
using Xunit;

namespace Loom.Telemetry.Tests.Exporters;

public sealed class ExporterStatusTrackerTests
{
    [Fact]
    public void RecordSuccess_FirstSuccess_CreatesHealthyStatus()
    {
        // Arrange
        var tracker = new ExportStatusTracker();

        // Act
        tracker.RecordSuccess("TestExporter");

        // Assert
        var statuses = tracker.GetStatuses();
        Assert.True(statuses.ContainsKey("TestExporter"));

        var status = statuses["TestExporter"];
        Assert.Equal("TestExporter", status.Name);
        Assert.True(status.IsHealthy);
        Assert.NotNull(status.LastSuccessUtc);
        Assert.Equal(1, status.TotalExports);
        Assert.Equal(0, status.TotalFailures);
        Assert.Null(status.LastFailureUtc);
        Assert.Null(status.LastError);
    }

    [Fact]
    public void RecordSuccess_MultipleSuccesses_IncrementsCount()
    {
        // Arrange
        var tracker = new ExportStatusTracker();

        // Act
        tracker.RecordSuccess("TestExporter");
        tracker.RecordSuccess("TestExporter");
        tracker.RecordSuccess("TestExporter");

        // Assert
        var status = tracker.GetStatuses()["TestExporter"];
        Assert.Equal(3, status.TotalExports);
        Assert.Equal(0, status.TotalFailures);
        Assert.True(status.IsHealthy);
    }

    [Fact]
    public void RecordFailure_FirstFailure_CreatesUnhealthyStatus()
    {
        // Arrange
        var tracker = new ExportStatusTracker();

        // Act
        tracker.RecordFailure("TestExporter", "Network timeout");

        // Assert
        var statuses = tracker.GetStatuses();
        Assert.True(statuses.ContainsKey("TestExporter"));

        var status = statuses["TestExporter"];
        Assert.Equal("TestExporter", status.Name);
        Assert.False(status.IsHealthy);
        Assert.NotNull(status.LastFailureUtc);
        Assert.Equal("Network timeout", status.LastError);
        Assert.Equal(0, status.TotalExports);
        Assert.Equal(1, status.TotalFailures);
        Assert.Null(status.LastSuccessUtc);
    }

    [Fact]
    public void RecordFailure_MultipleFailures_IncrementsCount()
    {
        // Arrange
        var tracker = new ExportStatusTracker();

        // Act
        tracker.RecordFailure("TestExporter", "Error 1");
        tracker.RecordFailure("TestExporter", "Error 2");
        tracker.RecordFailure("TestExporter", "Error 3");

        // Assert
        var status = tracker.GetStatuses()["TestExporter"];
        Assert.Equal(3, status.TotalFailures);
        Assert.Equal(0, status.TotalExports);
        Assert.False(status.IsHealthy);
        Assert.Equal("Error 3", status.LastError); // Most recent error
    }

    [Fact]
    public void RecordSuccess_AfterFailure_MarksHealthy()
    {
        // Arrange
        var tracker = new ExportStatusTracker();
        tracker.RecordFailure("TestExporter", "Network timeout");

        // Act
        tracker.RecordSuccess("TestExporter");

        // Assert
        var status = tracker.GetStatuses()["TestExporter"];
        Assert.True(status.IsHealthy);
        Assert.NotNull(status.LastSuccessUtc);
        Assert.NotNull(status.LastFailureUtc); // Failure timestamp preserved
        Assert.Equal(1, status.TotalExports);
        Assert.Equal(1, status.TotalFailures);
    }

    [Fact]
    public void RecordFailure_AfterSuccess_MarksUnhealthy()
    {
        // Arrange
        var tracker = new ExportStatusTracker();
        tracker.RecordSuccess("TestExporter");

        // Act
        tracker.RecordFailure("TestExporter", "Timeout");

        // Assert
        var status = tracker.GetStatuses()["TestExporter"];
        Assert.False(status.IsHealthy);
        Assert.NotNull(status.LastFailureUtc);
        Assert.NotNull(status.LastSuccessUtc); // Success timestamp preserved
        Assert.Equal("Timeout", status.LastError);
        Assert.Equal(1, status.TotalExports);
        Assert.Equal(1, status.TotalFailures);
    }

    [Fact]
    public void GetStatuses_MultipleExporters_ReturnsAll()
    {
        // Arrange
        var tracker = new ExportStatusTracker();

        // Act
        tracker.RecordSuccess("Exporter1");
        tracker.RecordSuccess("Exporter2");
        tracker.RecordFailure("Exporter3", "Error");

        // Assert
        var statuses = tracker.GetStatuses();
        Assert.Equal(3, statuses.Count);
        Assert.True(statuses["Exporter1"].IsHealthy);
        Assert.True(statuses["Exporter2"].IsHealthy);
        Assert.False(statuses["Exporter3"].IsHealthy);
    }

    [Fact]
    public void GetStatuses_NoRecords_ReturnsEmpty()
    {
        // Arrange
        var tracker = new ExportStatusTracker();

        // Act
        var statuses = tracker.GetStatuses();

        // Assert
        Assert.Empty(statuses);
    }

    [Fact]
    public void RecordSuccess_UpdatesTimestamp()
    {
        // Arrange
        var tracker = new ExportStatusTracker();
        tracker.RecordSuccess("TestExporter");
        var firstTimestamp = tracker.GetStatuses()["TestExporter"].LastSuccessUtc;

        // Act - small delay to ensure different timestamp
        System.Threading.Thread.Sleep(10);
        tracker.RecordSuccess("TestExporter");
        var secondTimestamp = tracker.GetStatuses()["TestExporter"].LastSuccessUtc;

        // Assert
        Assert.NotNull(firstTimestamp);
        Assert.NotNull(secondTimestamp);
        Assert.True(secondTimestamp > firstTimestamp);
    }

    [Fact]
    public void RecordFailure_UpdatesTimestamp()
    {
        // Arrange
        var tracker = new ExportStatusTracker();
        tracker.RecordFailure("TestExporter", "Error 1");
        var firstTimestamp = tracker.GetStatuses()["TestExporter"].LastFailureUtc;

        // Act
        System.Threading.Thread.Sleep(10);
        tracker.RecordFailure("TestExporter", "Error 2");
        var secondTimestamp = tracker.GetStatuses()["TestExporter"].LastFailureUtc;

        // Assert
        Assert.NotNull(firstTimestamp);
        Assert.NotNull(secondTimestamp);
        Assert.True(secondTimestamp > firstTimestamp);
    }

    [Fact]
    public void ExporterStatus_IsRecord_SupportsWithExpression()
    {
        // Arrange
        var tracker = new ExportStatusTracker();
        tracker.RecordSuccess("TestExporter");

        // Act
        var originalStatus = tracker.GetStatuses()["TestExporter"];
        var modifiedStatus = originalStatus with { TotalExports = 99 };

        // Assert - original unchanged, modified has new value
        Assert.Equal(1, originalStatus.TotalExports);
        Assert.Equal(99, modifiedStatus.TotalExports);
    }
}
