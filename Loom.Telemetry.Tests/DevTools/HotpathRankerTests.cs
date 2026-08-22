using System;
using System.Linq;
using Loom.DevTools.Rendering;
using Loom.Storage;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.DevTools;

public sealed class HotpathRankerTests
{
    private static readonly long Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private static void Write(InMemoryMetricStore store, string name, params double[] values)
    {
        for (var i = 0; i < values.Length; i++)
            store.Write(new MetricRecord(name, MetricType.Histogram, values[i], Base + i));
    }

    [Fact]
    public void Rank_OrdersByAverageDescending()
    {
        using var store = new InMemoryMetricStore();
        Write(store, "OrderService.Process.duration", 10, 20, 30); // avg 20
        Write(store, "PaymentService.Charge.latency", 40, 50, 60); // avg 50
        Write(store, "gc-heap-size", 999); // not instrumented (no matching keyword)

        var result = HotpathRanker.Rank(store, top: 3);

        Assert.Equal(2, result.Count);
        Assert.Equal("PaymentService.Charge.latency", result[0].Name);
        Assert.Equal("OrderService.Process.duration", result[1].Name);
    }

    [Fact]
    public void Rank_ExcludesNonInstrumentedMetrics()
    {
        using var store = new InMemoryMetricStore();
        Write(store, "cpu-usage", 50);
        Write(store, "working-set", 100);

        var result = HotpathRanker.Rank(store);

        Assert.Empty(result);
    }

    [Fact]
    public void Rank_FewerEntriesThanTop_ReturnsWhatExists()
    {
        using var store = new InMemoryMetricStore();
        Write(store, "OrderService.Process.duration", 10, 20);

        var result = HotpathRanker.Rank(store, top: 5);

        Assert.Single(result);
    }

    [Fact]
    public void Rank_TiedAverages_BreakTiesByNameForDeterminism()
    {
        using var store = new InMemoryMetricStore();
        Write(store, "Zebra.Method.duration", 10, 10);
        Write(store, "Alpha.Method.duration", 10, 10);

        var result = HotpathRanker.Rank(store, top: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha.Method.duration", result[0].Name);
        Assert.Equal("Zebra.Method.duration", result[1].Name);
    }

    [Fact]
    public void Rank_ComputesP99FromSampleWindow()
    {
        using var store = new InMemoryMetricStore();
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToArray(); // 1..100
        Write(store, "Method.duration", values);

        var result = HotpathRanker.Rank(store, top: 1);

        Assert.Single(result);
        Assert.Equal(50.5, result[0].AverageMs);
        Assert.Equal(100, result[0].P99Ms); // index (int)(100*0.99)=99 -> last element, value 100
    }
}
