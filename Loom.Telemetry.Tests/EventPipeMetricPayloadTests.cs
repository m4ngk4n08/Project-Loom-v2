using System.Globalization;
using Xunit;

namespace Loom.Telemetry.Tests;

public class EventPipeMetricPayloadTests
{
    static readonly string[] AllFields = ["Name", "rate", "value", "lastValue", "sum", "tags"];

    // rate=1, value=2, lastValue=3, sum=4 - distinct so a wrong field read shows up.
    static readonly object?[] AllValues = ["req.count", "1", "2", "3", "4", "route=a"];

    static bool Build(string eventName, string[]? names, object?[] values, out MetricRecord record, long ticks = 42) =>
        EventPipeMetricPayload.TryBuildRecord(eventName, names, i => values[i], ticks, out record);

    [Theory]
    [InlineData("CounterRateValuePublished", MetricType.Counter, 1.0)]
    [InlineData("GaugeValuePublished", MetricType.Gauge, 3.0)]
    [InlineData("HistogramValuePublished", MetricType.Histogram, 4.0)]
    [InlineData("UpDownCounterRateValuePublished", MetricType.Gauge, 2.0)]
    public void TryBuildRecord_KnownEvent_ReadsOnlyItsOwnValueField(string eventName, MetricType type, double expected)
    {
        Assert.True(Build(eventName, AllFields, AllValues, out var record));

        Assert.Equal(type, record.Type);
        Assert.Equal(expected, record.Value);
    }

    [Fact]
    public void TryBuildRecord_UpDownCounter_ReadsValueNotRate()
    {
        string[] names = ["Name", "rate", "value"];
        object?[] values = ["pool", "0", "10"];

        Assert.True(Build("UpDownCounterRateValuePublished", names, values, out var record));

        Assert.Equal(10.0, record.Value);
        Assert.Equal(MetricType.Gauge, record.Type);
    }

    [Fact]
    public void TryBuildRecord_UnrecognisedValuePublished_GaugeWithZeroValue()
    {
        Assert.True(Build("SomethingNewValuePublished", AllFields, AllValues, out var record));

        Assert.Equal(MetricType.Gauge, record.Type);
        Assert.Equal(0.0, record.Value);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("instrumentName")]
    public void TryBuildRecord_NameOrInstrumentName_SuppliesName(string nameField)
    {
        string[] names = [nameField, "rate"];
        object?[] values = ["my.metric", "5"];

        Assert.True(Build("CounterRateValuePublished", names, values, out var record));

        Assert.Equal("my.metric", record.Name);
    }

    [Fact]
    public void TryBuildRecord_NoName_ReturnsFalse()
    {
        string[] names = ["rate"];
        object?[] values = ["5"];

        Assert.False(Build("CounterRateValuePublished", names, values, out _));
    }

    [Theory]
    [InlineData("BeginInstrumentReporting")]
    [InlineData("EventCounters")]
    [InlineData("")]
    public void TryBuildRecord_NotValuePublished_ReturnsFalse(string eventName)
    {
        Assert.False(Build(eventName, AllFields, AllValues, out _));
    }

    [Fact]
    public void TryBuildRecord_NullPayloadNames_ReturnsFalse()
    {
        Assert.False(Build("CounterRateValuePublished", null, [], out _));
    }

    [Fact]
    public void TryBuildRecord_NonNumericValue_LeavesZero()
    {
        string[] names = ["Name", "rate"];
        object?[] values = ["m", "not a number"];

        Assert.True(Build("CounterRateValuePublished", names, values, out var record));

        Assert.Equal(0.0, record.Value);
    }

    [Fact]
    public void TryBuildRecord_DecimalValue_ParsesUnderNonInvariantCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            // de-DE uses ',' as the decimal separator; "1.5" only parses as 1.5 under InvariantCulture.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string[] names = ["Name", "lastValue"];
            object?[] values = ["m", "1.5"];

            Assert.True(Build("GaugeValuePublished", names, values, out var record));

            Assert.Equal(1.5, record.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void TryBuildRecord_TagsAndTimestamp_FlowThrough()
    {
        Assert.True(Build("CounterRateValuePublished", AllFields, AllValues, out var record, ticks: 123456789L));

        Assert.Equal(123456789L, record.TimestampUtcTicks);
        Assert.NotNull(record.Tags);
        var tag = Assert.Single(record.Tags);
        Assert.Equal("route", tag.Key);
        Assert.Equal("a", tag.Value);
    }
}
