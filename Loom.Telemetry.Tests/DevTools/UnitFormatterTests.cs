using Loom.DevTools.Rendering;
using Xunit;

namespace Loom.Telemetry.Tests.DevTools;

public sealed class UnitFormatterTests
{
    [Theory]
    [InlineData("cpu-usage", "%")]
    [InlineData("gc-fragmentation", "%")]
    [InlineData("percent-complete", "%")]
    [InlineData("working-set", "MB")]
    [InlineData("gc-heap-size", "MB")]
    [InlineData("gen-0-size", "B")]
    [InlineData("loh-size", "B")]
    [InlineData("request.duration", "ms")]
    [InlineData("order.latency", "ms")]
    [InlineData("order.elapsed", "ms")]
    [InlineData("order.total", "$")]
    [InlineData("order.revenue", "$")]
    [InlineData("gc-time", "/s")]
    [InlineData("lock-contention", "/s")]
    [InlineData("unrecognized-metric-name", "count")]
    public void InferUnit_MatchesExpectedCategory(string name, string expectedUnit)
    {
        Assert.Equal(expectedUnit, UnitFormatter.InferUnit(name));
    }

    // Pins the fix: MetricsCommand.InferUnit had "alloc-rate"/"gc-time"/"lock-contention" -> "/s"
    // checked before a dead "alloc-rate" -> "B/s" branch, so alloc-rate was mislabeled "/s".
    [Fact]
    public void InferUnit_AllocRate_ResolvesToBytesPerSecond()
    {
        Assert.Equal("B/s", UnitFormatter.InferUnit("alloc-rate"));
    }

    [Fact]
    public void Format_Percent_UsesOneDecimalWithSuffix()
    {
        Assert.Equal("12.4%", UnitFormatter.Format("cpu-usage", 12.44));
    }

    [Fact]
    public void Format_AllocRate_UsesHumanBytesWithPerSecondSuffix()
    {
        Assert.Equal("1.0KB/s", UnitFormatter.Format("alloc-rate", 1024));
    }

    [Fact]
    public void Format_Bytes_ScalesToLargestSensibleUnit()
    {
        Assert.Equal("1.0MB", UnitFormatter.Format("gen-0-size", 1024 * 1024));
    }

    [Fact]
    public void Format_UnknownCategory_FallsBackToPlainNumber()
    {
        Assert.Equal("42", UnitFormatter.Format("unrecognized-metric-name", 42));
    }
}
