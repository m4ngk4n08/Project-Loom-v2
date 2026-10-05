using Loom.DevTools.Rendering;
using Xunit;

namespace Loom.Telemetry.Tests.DevTools;

public sealed class UnitFormatterTests
{
    [Fact]
    public void Format_Percent_UsesOneDecimalWithSuffix()
    {
        Assert.Equal("12.4%", UnitFormatter.Format(MetricUnit.Percent, 12.44));
    }

    [Fact]
    public void Format_Milliseconds_UsesOneDecimalWithSuffix()
    {
        Assert.Equal("8.8ms", UnitFormatter.Format(MetricUnit.Milliseconds, 8.828));
    }

    [Fact]
    public void Format_BytesPerSecond_UsesHumanBytesWithPerSecondSuffix()
    {
        Assert.Equal("1.0KB/s", UnitFormatter.Format(MetricUnit.BytesPerSecond, 1024));
    }

    [Fact]
    public void Format_Bytes_ScalesToLargestSensibleUnit()
    {
        Assert.Equal("1.0MB", UnitFormatter.Format(MetricUnit.Bytes, 1024 * 1024));
    }

    [Fact]
    public void Format_PerSecond_UsesTwoDecimalsWithSuffix()
    {
        Assert.Equal("3.00/s", UnitFormatter.Format(MetricUnit.PerSecond, 3));
    }

    // The regression this replaces: a unitless value used to get a unit guessed from
    // its name ("order.total" -> "$", a [LoomProfile] timing -> "count").
    [Theory]
    [InlineData(42, "42")]
    [InlineData(129.9, "129.9")]
    public void Format_NoDeclaredUnit_IsAPlainNumber(double value, string expected)
    {
        Assert.Equal(expected, UnitFormatter.Format(MetricUnit.None, value));
    }
}
