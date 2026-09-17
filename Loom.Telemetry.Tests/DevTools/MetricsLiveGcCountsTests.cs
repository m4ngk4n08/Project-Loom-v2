using Loom.DevTools.Commands;
using Loom.Storage;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.DevTools;

/// <summary>
/// Covers BACKLOG.md 6.33: loom metrics --live read gen-N-collection-count, which the
/// runtime never publishes (it publishes gen-N-gc-count). ReadGcCounts must mirror
/// MetricsResponseBuilder's single-pass GetCounterTotals() read.
/// </summary>
public sealed class MetricsLiveGcCountsTests
{
    [Fact]
    public void ReadGcCounts_SumsPublishedCounterTotals()
    {
        var store = new InMemoryMetricStore();

        store.Write(new MetricRecord("gen-0-gc-count", MetricType.Counter, 2, 1L));
        store.Write(new MetricRecord("gen-0-gc-count", MetricType.Counter, 3, 2L));
        store.Write(new MetricRecord("gen-2-gc-count", MetricType.Counter, 1, 3L));

        var (gen0, gen1, gen2) = MetricsLiveCommand.ReadGcCounts(store);

        Assert.Equal(5, gen0);
        Assert.Equal(0, gen1);
        Assert.Equal(1, gen2);
    }

    [Fact]
    public void ReadGcCounts_IgnoresLegacyCollectionCountName()
    {
        var store = new InMemoryMetricStore();

        store.Write(new MetricRecord("gen-0-collection-count", MetricType.Counter, 7, 1L));

        var (gen0, _, _) = MetricsLiveCommand.ReadGcCounts(store);

        Assert.Equal(0, gen0);
    }

    [Fact]
    public void ReadGcCounts_IgnoresTaggedEntries()
    {
        var store = new InMemoryMetricStore();

        store.Write(new MetricRecord(
            "gen-1-gc-count",
            MetricType.Counter,
            4,
            1L,
            new[] { new MetricTag("region", "eu") }));

        var (_, gen1, _) = MetricsLiveCommand.ReadGcCounts(store);

        Assert.Equal(0, gen1);
    }
}
