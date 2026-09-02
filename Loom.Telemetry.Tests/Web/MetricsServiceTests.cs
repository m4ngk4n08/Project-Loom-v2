using System;
using System.Linq;
using System.Threading.Tasks;
using Loom.Telemetry;
using Loom.Storage;
using Xunit;

namespace Loom.Telemetry.Tests.Web;

/// <summary>
/// Characterization tests for MetricsService.GetCpuMetricsAsync, written before the
/// allocation-reduction refactor (D4) so behavior is pinned down first.
/// </summary>
public sealed class MetricsServiceTests
{
    private static MetricRecord Record(double value) =>
        new("test-metric", MetricType.MethodExecution, value, DateTime.UtcNow.Ticks);

    [Fact]
    public async Task GetCpuMetricsAsync_EmptyStore_ReturnsNoHotpaths()
    {
        var store = new FakeMetricStore();
        var service = new MetricsService(store);

        var result = await service.GetCpuMetricsAsync();

        Assert.Empty(result.Hotpaths);
        Assert.InRange(result.CpuUsagePercent, 0, 100);
    }

    [Fact]
    public async Task GetCpuMetricsAsync_SingleHotpath_Gets100PercentShare()
    {
        var store = new FakeMetricStore();
        var buffer = store.AddBuffer("order.process.duration");
        buffer.Write(Record(10));
        buffer.Write(Record(20));
        buffer.Write(Record(30));
        var service = new MetricsService(store);

        var result = await service.GetCpuMetricsAsync();

        var hotpath = Assert.Single(result.Hotpaths);
        Assert.Equal("order.process.duration", hotpath.MethodName);
        Assert.Equal(3, hotpath.InvocationCount);
        Assert.Equal(20, hotpath.AverageTimeMs, precision: 6);
        Assert.Equal(100, hotpath.CpuPercent, precision: 6);
    }

    [Fact]
    public async Task GetCpuMetricsAsync_MoreThanThreeHotpaths_ReturnsTop3PercentagesOverFullSet()
    {
        var store = new FakeMetricStore();
        // Averages: 10, 20, 30, 40, 50 - distinct so ranking is unambiguous.
        var averages = new (string Name, double Avg)[]
        {
            ("a.duration", 10),
            ("b.duration", 20),
            ("c.duration", 30),
            ("d.duration", 40),
            ("e.duration", 50),
        };
        foreach (var (name, avg) in averages)
        {
            store.AddBuffer(name).Write(Record(avg));
        }
        var service = new MetricsService(store);
        var totalMs = averages.Sum(a => a.Avg); // 150

        var result = await service.GetCpuMetricsAsync();

        Assert.Equal(3, result.Hotpaths.Length);
        // Top 3 by AverageTimeMs, descending.
        Assert.Equal(new[] { "e.duration", "d.duration", "c.duration" }, result.Hotpaths.Select(h => h.MethodName));
        Assert.Equal(new[] { 50.0, 40.0, 30.0 }, result.Hotpaths.Select(h => h.AverageTimeMs));
        // Percentages are each path's share of ALL 5 paths' total, not just the top 3's.
        Assert.Equal(50.0 / totalMs * 100, result.Hotpaths[0].CpuPercent, precision: 6);
        Assert.Equal(40.0 / totalMs * 100, result.Hotpaths[1].CpuPercent, precision: 6);
        Assert.Equal(30.0 / totalMs * 100, result.Hotpaths[2].CpuPercent, precision: 6);
    }

    [Fact]
    public async Task GetCpuMetricsAsync_NonInstrumentedMethodNames_AreExcluded()
    {
        var store = new FakeMetricStore();
        // Not instrumented per IsInstrumentedMethod - no latency/duration/elapsed/.time.
        store.AddBuffer("cpu-usage").Write(Record(99));
        store.AddBuffer("gen0-collections").Write(Record(5));
        // Instrumented.
        store.AddBuffer("worker.duration").Write(Record(15));
        var service = new MetricsService(store);

        var result = await service.GetCpuMetricsAsync();

        var hotpath = Assert.Single(result.Hotpaths);
        Assert.Equal("worker.duration", hotpath.MethodName);
        Assert.Equal(100, hotpath.CpuPercent, precision: 6);
    }
}
