using Xunit;

namespace Loom.Telemetry.Tests;

public class EventPipeTagPayloadTests
{
    [Fact]
    public void Parse_Null_ReturnsNull()
    {
        Assert.Null(EventPipeTagPayload.Parse(null));
    }

    [Fact]
    public void Parse_Empty_ReturnsNull()
    {
        Assert.Null(EventPipeTagPayload.Parse(string.Empty));
    }

    [Fact]
    public void Parse_Whitespace_ReturnsNull()
    {
        Assert.Null(EventPipeTagPayload.Parse("   "));
    }

    [Fact]
    public void Parse_OneTag_ReturnsSingleEntry()
    {
        var tags = EventPipeTagPayload.Parse("route=checkout");

        Assert.NotNull(tags);
        var tag = Assert.Single(tags);
        Assert.Equal("route", tag.Key);
        Assert.Equal("checkout", tag.Value);
    }

    [Fact]
    public void Parse_SeveralTags_ReturnsAllEntriesInOrder()
    {
        var tags = EventPipeTagPayload.Parse("region=us,shard=3");

        Assert.NotNull(tags);
        Assert.Equal(2, tags.Length);
        Assert.Equal(new MetricTag("region", "us"), tags[0]);
        Assert.Equal(new MetricTag("shard", "3"), tags[1]);
    }

    [Fact]
    public void Parse_MalformedSegment_SkipsItWithoutThrowing()
    {
        var tags = EventPipeTagPayload.Parse("region=us,broken,shard=3");

        Assert.NotNull(tags);
        Assert.Equal(2, tags.Length);
        Assert.Equal(new MetricTag("region", "us"), tags[0]);
        Assert.Equal(new MetricTag("shard", "3"), tags[1]);
    }

    [Fact]
    public void Parse_OnlyMalformedSegments_ReturnsNull()
    {
        Assert.Null(EventPipeTagPayload.Parse("broken,alsobroken"));
    }

    [Fact]
    public void Parse_ValueContainingEquals_SplitsOnFirstEqualsOnly()
    {
        var tags = EventPipeTagPayload.Parse("query=a=b");

        Assert.NotNull(tags);
        var tag = Assert.Single(tags);
        Assert.Equal("query", tag.Key);
        Assert.Equal("a=b", tag.Value);
    }
}
