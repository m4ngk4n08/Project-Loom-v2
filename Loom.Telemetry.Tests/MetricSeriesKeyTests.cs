using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class MetricSeriesKeyTests
{
    [Fact]
    public void Build_TagsInOppositeDeclarationOrder_ProduceTheSameKey()
    {
        var tagsAb = MetricSeriesKey.SortTags([new MetricTag("a", "1"), new MetricTag("b", "2")]);
        var tagsBa = MetricSeriesKey.SortTags([new MetricTag("b", "2"), new MetricTag("a", "1")]);

        var keyAb = MetricSeriesKey.Build("requests", tagsAb);
        var keyBa = MetricSeriesKey.Build("requests", tagsBa);

        Assert.Equal(keyAb, keyBa);
    }

    [Fact]
    public void Build_SeparatorCharactersInValue_DoesNotCollideWithDistinctTagSet()
    {
        var single = MetricSeriesKey.SortTags([new MetricTag("a", "b,c=d")]);
        var pair = MetricSeriesKey.SortTags([new MetricTag("a", "b"), new MetricTag("c", "d")]);

        var keySingle = MetricSeriesKey.Build("requests", single);
        var keyPair = MetricSeriesKey.Build("requests", pair);

        Assert.NotEqual(keySingle, keyPair);
    }

    [Fact]
    public void Build_NoTags_ReturnsMetricNameItself()
    {
        var key = MetricSeriesKey.Build("requests", MetricSeriesKey.SortTags(null));

        Assert.Equal("requests", key);
    }
}
