using Loom.DevTools.Rendering;
using Xunit;

namespace Loom.Telemetry.Tests.DevTools;

public sealed class MetricCategoryFilterTests
{
    [Theory]
    [InlineData("cpu-usage", true)]
    [InlineData("order.processing.duration", true)]
    [InlineData("worker.elapsed", true)]
    [InlineData("gc-heap-size", false)]
    [InlineData("threadpool-thread-count", false)]
    public void IsCpu_MatchesExpected(string name, bool expected)
    {
        Assert.Equal(expected, MetricCategoryFilter.IsCpu(name));
    }

    [Theory]
    [InlineData("gc-heap-size", true)]
    [InlineData("alloc-rate", true)]
    [InlineData("working-set", false)] // "memory" isn't in the name, and neither is alloc/gc/heap
    [InlineData("loh-size", false)]
    [InlineData("gen-0-size", false)]
    [InlineData("cpu-usage", false)]
    public void IsMemory_MatchesExpected(string name, bool expected)
    {
        Assert.Equal(expected, MetricCategoryFilter.IsMemory(name));
    }

    [Theory]
    [InlineData("threadpool-thread-count", true)]
    [InlineData("monitor-lock-contention-count", true)]
    [InlineData("cpu-usage", false)]
    [InlineData("gc-heap-size", false)]
    public void IsThread_MatchesExpected(string name, bool expected)
    {
        Assert.Equal(expected, MetricCategoryFilter.IsThread(name));
    }

    [Theory]
    [InlineData("assembly-count")]
    [InlineData("methods-jitted-count")]
    [InlineData("orders.pending")]
    public void UnrelatedMetric_MatchesNoCategory(string name)
    {
        Assert.False(MetricCategoryFilter.IsCpu(name));
        Assert.False(MetricCategoryFilter.IsMemory(name));
        Assert.False(MetricCategoryFilter.IsThread(name));
    }
}
