using Loom.Dashboard.Extensions;
using Loom.Telemetry.Exporters;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

public class ExporterStatusTests
{
    [Fact]
    public void BuildExporterStatuses_EmptyTracker_ReturnsEmptyList()
    {
        var tracker = new ExportStatusTracker();

        var result = EndpointExtensions.BuildExporterStatuses(tracker);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildExporterStatuses_AfterSuccess_ReturnsHealthyEntry()
    {
        var tracker = new ExportStatusTracker();
        tracker.RecordSuccess("Console");

        var result = EndpointExtensions.BuildExporterStatuses(tracker);

        var entry = Assert.Single(result);
        Assert.Equal("Console", entry.Name);
        Assert.True(entry.IsHealthy);
        Assert.Equal(1, entry.TotalExports);
    }

    [Fact]
    public void BuildExporterStatuses_AfterSuccessThenFailure_CarriesFailureState()
    {
        var tracker = new ExportStatusTracker();
        tracker.RecordSuccess("Console");
        tracker.RecordFailure("Console", "boom");

        var result = EndpointExtensions.BuildExporterStatuses(tracker);

        var entry = Assert.Single(result);
        Assert.False(entry.IsHealthy);
        Assert.Equal("boom", entry.LastError);
        Assert.Equal(1, entry.TotalExports);
        Assert.Equal(1, entry.TotalFailures);
    }
}
