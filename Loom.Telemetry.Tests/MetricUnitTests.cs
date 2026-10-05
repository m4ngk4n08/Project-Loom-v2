using System;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class MetricUnitTests
{
    // Every member must survive the wire: Symbol() is what the collectors parse back.
    [Fact]
    public void Parse_OfSymbol_RoundTripsEveryUnit()
    {
        foreach (var unit in Enum.GetValues<MetricUnit>())
            Assert.Equal(unit, MetricUnits.Parse(unit.Symbol()));
    }

    // The four DisplayUnits values a live .NET 10 System.Runtime session publishes
    // (measured 2026-09-30), plus the empty one.
    [Theory]
    [InlineData("%", MetricUnit.Percent)]
    [InlineData("B", MetricUnit.Bytes)]
    [InlineData("MB", MetricUnit.Megabytes)]
    [InlineData("ms", MetricUnit.Milliseconds)]
    [InlineData("", MetricUnit.None)]
    [InlineData(null, MetricUnit.None)]
    public void Parse_RuntimeDisplayUnits(string? symbol, MetricUnit expected)
    {
        Assert.Equal(expected, MetricUnits.Parse(symbol));
    }

    [Theory]
    [InlineData("mb")]
    [InlineData("By")]
    [InlineData("count")]
    public void Parse_UnrecognisedOrWrongCase_IsNone(string symbol)
    {
        Assert.Equal(MetricUnit.None, MetricUnits.Parse(symbol));
    }

    [Theory]
    [InlineData(MetricUnit.None, MetricUnit.PerSecond)]
    [InlineData(MetricUnit.Bytes, MetricUnit.BytesPerSecond)]
    [InlineData(MetricUnit.Milliseconds, MetricUnit.MillisecondsPerSecond)]
    [InlineData(MetricUnit.Megabytes, MetricUnit.None)]
    public void ToRate(MetricUnit unit, MetricUnit expected)
    {
        Assert.Equal(expected, unit.ToRate());
    }
}
