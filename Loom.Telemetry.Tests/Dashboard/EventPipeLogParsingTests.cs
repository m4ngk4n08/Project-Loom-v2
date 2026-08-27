using Loom.Dashboard;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

public class EventPipeLogParsingTests
{
    const string ArgsJson =
        "{\"UserId\":\"41\",\"Ms\":\"900\",\"{OriginalFormat}\":\"User {UserId} checkout failed after {Ms}ms\"}";

    [Fact]
    public void BuildLogRecord_FullPayload_PopulatesTemplateArgsAndIds()
    {
        var parser = new LogMessageParser();
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        const string spanId = "00f067aa0ba902b7";

        var record = EventPipeBridge.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            0, 0, null, ArgsJson, traceId, spanId);

        Assert.Equal("User {UserId} checkout failed after {Ms}ms", record.Template);
        Assert.Equal("{\"UserId\":\"41\",\"Ms\":\"900\"}", record.ArgumentsJson);
        Assert.NotEqual(0UL, record.TraceIdHi);
        Assert.NotEqual(0UL, record.TraceIdLo);
        Assert.NotEqual(0UL, record.SpanId);
        Assert.Equal(traceId, W3CTraceId.FormatTraceId(record.TraceIdHi, record.TraceIdLo));
        Assert.Equal(spanId, W3CTraceId.FormatSpanId(record.SpanId));
    }

    [Fact]
    public void BuildLogRecord_EmptyStringIds_AllIdFieldsZero()
    {
        var parser = new LogMessageParser();

        var record = EventPipeBridge.BuildLogRecord(
            parser, "no ids here", "MyApp.Checkout", 2,
            0, 0, null, null, "", "");

        Assert.Equal(0UL, record.TraceIdHi);
        Assert.Equal(0UL, record.TraceIdLo);
        Assert.Equal(0UL, record.SpanId);
    }

    [Fact]
    public void BuildLogRecord_NullArgumentsJson_TemplateAndArgumentsJsonNullMessageUnchanged()
    {
        var parser = new LogMessageParser();

        var record = EventPipeBridge.BuildLogRecord(
            parser, "plain message", "MyApp.Checkout", 2,
            0, 0, null, null, null, null);

        Assert.Null(record.Template);
        Assert.Null(record.ArgumentsJson);
        Assert.Equal("plain message", record.Message);
    }

    [Fact]
    public void BuildLogRecord_TemplatePresent_MessageIsFormattedTextNotTemplate()
    {
        var parser = new LogMessageParser();

        var record = EventPipeBridge.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            0, 0, null, ArgsJson, null, null);

        Assert.Equal("User 41 checkout failed after 900ms", record.Message);
    }

    [Fact]
    public void BuildLogRecord_MalformedArgumentsJson_DoesNotThrowTemplateNullArgsPreserved()
    {
        var parser = new LogMessageParser();

        var record = EventPipeBridge.BuildLogRecord(
            parser, "plain message", "MyApp.Checkout", 2,
            0, 0, null, "{not json", null, null);

        Assert.Null(record.Template);
        Assert.Equal("{not json", record.ArgumentsJson);
    }

    [Fact]
    public void BuildLogRecord_AllZeroIds_IdFieldsZero()
    {
        var parser = new LogMessageParser();
        var traceId = new string('0', 32);
        var spanId = new string('0', 16);

        var record = EventPipeBridge.BuildLogRecord(
            parser, "plain message", "MyApp.Checkout", 2,
            0, 0, null, null, traceId, spanId);

        Assert.Equal(0UL, record.TraceIdHi);
        Assert.Equal(0UL, record.TraceIdLo);
        Assert.Equal(0UL, record.SpanId);
    }

    [Fact]
    public void BuildLogRecord_SharedParserSameTemplate_ReturnsReferenceIdenticalTemplate()
    {
        var parser = new LogMessageParser();

        var record1 = EventPipeBridge.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            0, 0, null, ArgsJson, null, null);
        var record2 = EventPipeBridge.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            0, 0, null, ArgsJson, null, null);

        Assert.Same(record1.Template, record2.Template);
    }

    [Fact]
    public void BuildLogRecord_ExceptionAndArgumentsBothPresent_AllFieldsPopulate()
    {
        var parser = new LogMessageParser();
        var exceptionJson = "{\"TypeName\":\"System.InvalidOperationException\",\"Message\":\"settlement gateway refused\"}";

        var record = EventPipeBridge.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            0, 0, exceptionJson, ArgsJson, null, null);

        Assert.Equal("System.InvalidOperationException", record.ExceptionType);
        Assert.Equal("settlement gateway refused", record.ExceptionMessage);
        Assert.Equal("User {UserId} checkout failed after {Ms}ms", record.Template);
        Assert.Equal("{\"UserId\":\"41\",\"Ms\":\"900\"}", record.ArgumentsJson);
    }

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

    [Fact]
    public void ToInt32_BoxedInt_ReturnsUnchanged()
    {
        var result = EventPipeBridge.ToInt32((object)4, -1);

        Assert.Equal(4, result);
    }

    [Fact]
    public void ToInt32_Null_ReturnsFallback()
    {
        var result = EventPipeBridge.ToInt32(null, -1);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void ToInt32_NumericString_ReturnsParsed()
    {
        var result = EventPipeBridge.ToInt32("3", -1);

        Assert.Equal(3, result);
    }

    [Fact]
    public void ToInt32_NonNumericString_ReturnsFallback()
    {
        var result = EventPipeBridge.ToInt32("Warning", -1);

        Assert.Equal(-1, result);
    }
}
