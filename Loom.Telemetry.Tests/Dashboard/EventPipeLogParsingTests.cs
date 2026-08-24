using Loom.Dashboard;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

public class EventPipeLogParsingTests
{
    [Fact]
    public void ParseExceptionJson_Null_ReturnsNulls()
    {
        var (type, message) = EventPipeBridge.ParseExceptionJson(null);

        Assert.Null(type);
        Assert.Null(message);
    }

    [Fact]
    public void ParseExceptionJson_Empty_ReturnsNulls()
    {
        var (type, message) = EventPipeBridge.ParseExceptionJson("");

        Assert.Null(type);
        Assert.Null(message);
    }

    [Fact]
    public void ParseExceptionJson_EmptyObject_ReturnsNulls()
    {
        var (type, message) = EventPipeBridge.ParseExceptionJson("{}");

        Assert.Null(type);
        Assert.Null(message);
    }

    [Fact]
    public void ParseExceptionJson_WellFormedPayload_ExtractsTypeAndMessage()
    {
        var json = "{\"TypeName\":\"System.InvalidOperationException\",\"Message\":\"settlement gateway refused\",\"HResult\":\"-2146233079\",\"VerboseMessage\":\"...\"}";

        var (type, message) = EventPipeBridge.ParseExceptionJson(json);

        Assert.Equal("System.InvalidOperationException", type);
        Assert.Equal("settlement gateway refused", message);
    }

    [Fact]
    public void ParseExceptionJson_MalformedJson_ReturnsNulls()
    {
        var (type, message) = EventPipeBridge.ParseExceptionJson("{not json");

        Assert.Null(type);
        Assert.Null(message);
    }

    [Fact]
    public void ParseExceptionJson_ValidJsonMissingFields_DoesNotThrowAndReturnsNulls()
    {
        var (type, message) = EventPipeBridge.ParseExceptionJson("{\"HResult\":\"-1\"}");

        Assert.Null(type);
        Assert.Null(message);
    }
}
