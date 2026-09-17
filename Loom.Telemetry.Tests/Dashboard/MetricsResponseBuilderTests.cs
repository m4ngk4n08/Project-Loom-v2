using System;
using Loom.Dashboard;
using Loom.Storage;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

/// <summary>
/// Covers PROMPT-dashboard-review-fixes.md items 1, 3, 4, 5: cpu-usage is already a
/// 0-100 percentage (not a 0-1 fraction to multiply), gen-N-gc-count and
/// total-pause-time-by-gc are Counters read via cumulative totals (not the wrong,
/// never-published "gen-N-collection-count" name, and not the latest ring-buffer
/// delta), and a missing working-set sample must not poison the tracked peak.
/// </summary>
public sealed class MetricsResponseBuilderTests
{
    private static InMemoryMetricStore NewStore() => new();

    private static void WriteGauge(IMetricStore store, string name, double value) =>
        store.Write(new MetricRecord(name, MetricType.Gauge, value, DateTime.UtcNow.Ticks));

    private static void WriteCounter(IMetricStore store, string name, double value) =>
        store.Write(new MetricRecord(name, MetricType.Counter, value, DateTime.UtcNow.Ticks));

    [Fact]
    public void CpuUsage_ReadsPercentDirectly_NotClampedFor40()
    {
        var store = NewStore();
        WriteGauge(store, "cpu-usage", 40);

        var response = new MetricsResponseBuilder().BuildCpuResponse(store);

        Assert.Equal(40, response.CpuUsagePercent);
    }

    [Fact]
    public void Gen0Collections_SumsCounterWrites_UsingGcCountName()
    {
        var store = NewStore();
        WriteCounter(store, "gen-0-gc-count", 2);
        WriteCounter(store, "gen-0-gc-count", 3);
        WriteCounter(store, "gen-0-gc-count", 5);

        var response = new MetricsResponseBuilder().BuildMemoryResponse(store);

        Assert.Equal(10, response.GcStats.Gen0Collections);
    }

    [Fact]
    public void Gen0Collections_IgnoresTheNeverPublishedCollectionCountName()
    {
        var store = NewStore();
        // The runtime never publishes this name - writing it must not affect the
        // gen-0-gc-count total (regression guard for the old wrong name).
        WriteCounter(store, "gen-0-collection-count", 99);

        var response = new MetricsResponseBuilder().BuildMemoryResponse(store);

        Assert.Equal(0, response.GcStats.Gen0Collections);
    }

    [Fact]
    public void TotalGcTimeMs_SumsCounterWrites_UsingTotalPauseTimeName()
    {
        var store = NewStore();
        WriteCounter(store, "total-pause-time-by-gc", 1.5);
        WriteCounter(store, "total-pause-time-by-gc", 2.5);

        var response = new MetricsResponseBuilder().BuildMemoryResponse(store);

        Assert.Equal(4.0, response.GcStats.TotalGcTimeMs);
    }

    [Fact]
    public void NoWorkingSetSample_UsedMemoryIsZero_AndPeakIsNotPoisoned()
    {
        var store = NewStore();
        var builder = new MetricsResponseBuilder();

        var first = builder.BuildMemoryResponse(store);
        Assert.Equal(0, first.UsedMemoryMb);

        WriteGauge(store, "working-set", 50);
        var second = builder.BuildMemoryResponse(store);

        Assert.Equal(50, second.TotalMemoryMb);
        Assert.Equal(50, second.UsedMemoryMb);
    }
}
