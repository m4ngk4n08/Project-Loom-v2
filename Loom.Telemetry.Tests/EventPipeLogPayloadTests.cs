using Xunit;

namespace Loom.Telemetry.Tests;

public class EventPipeLogPayloadTests
{
    const string ArgsJson =
        "{\"UserId\":\"41\",\"Ms\":\"900\",\"{OriginalFormat}\":\"User {UserId} checkout failed after {Ms}ms\"}";

    [Fact]
    public void BuildLogRecord_FullPayload_PopulatesTemplateArgsAndIds()
    {
        var parser = new LogMessageParser();
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        const string spanId = "00f067aa0ba902b7";

        var record = EventPipeLogPayload.BuildLogRecord(
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

        var record = EventPipeLogPayload.BuildLogRecord(
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

        var record = EventPipeLogPayload.BuildLogRecord(
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

        var record = EventPipeLogPayload.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            0, 0, null, ArgsJson, null, null);

        Assert.Equal("User 41 checkout failed after 900ms", record.Message);
    }

    [Fact]
    public void BuildLogRecord_MalformedArgumentsJson_DoesNotThrowTemplateNullArgsPreserved()
    {
        var parser = new LogMessageParser();

        var record = EventPipeLogPayload.BuildLogRecord(
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

        var record = EventPipeLogPayload.BuildLogRecord(
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

        var record1 = EventPipeLogPayload.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            0, 0, null, ArgsJson, null, null);
        var record2 = EventPipeLogPayload.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            0, 0, null, ArgsJson, null, null);

        Assert.Same(record1.Template, record2.Template);
    }

    [Fact]
    public void BuildLogRecord_ExceptionAndArgumentsBothPresent_AllFieldsPopulate()
    {
        var parser = new LogMessageParser();
        var exceptionJson = "{\"TypeName\":\"System.InvalidOperationException\",\"Message\":\"settlement gateway refused\"}";

        var record = EventPipeLogPayload.BuildLogRecord(
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
        var (type, message) = EventPipeLogPayload.ParseExceptionJson(null);

        Assert.Null(type);
        Assert.Null(message);
    }

    [Fact]
    public void ParseExceptionJson_Empty_ReturnsNulls()
    {
        var (type, message) = EventPipeLogPayload.ParseExceptionJson("");

        Assert.Null(type);
        Assert.Null(message);
    }

    [Fact]
    public void ParseExceptionJson_EmptyObject_ReturnsNulls()
    {
        var (type, message) = EventPipeLogPayload.ParseExceptionJson("{}");

        Assert.Null(type);
        Assert.Null(message);
    }

    [Fact]
    public void ParseExceptionJson_WellFormedPayload_ExtractsTypeAndMessage()
    {
        var json = "{\"TypeName\":\"System.InvalidOperationException\",\"Message\":\"settlement gateway refused\",\"HResult\":\"-2146233079\",\"VerboseMessage\":\"...\"}";

        var (type, message) = EventPipeLogPayload.ParseExceptionJson(json);

        Assert.Equal("System.InvalidOperationException", type);
        Assert.Equal("settlement gateway refused", message);
    }

    [Fact]
    public void ParseExceptionJson_MalformedJson_ReturnsNulls()
    {
        var (type, message) = EventPipeLogPayload.ParseExceptionJson("{not json");

        Assert.Null(type);
        Assert.Null(message);
    }

    [Fact]
    public void ParseExceptionJson_ValidJsonMissingFields_DoesNotThrowAndReturnsNulls()
    {
        var (type, message) = EventPipeLogPayload.ParseExceptionJson("{\"HResult\":\"-1\"}");

        Assert.Null(type);
        Assert.Null(message);
    }

    static readonly string[] LogNames =
        ["LoggerName", "Level", "EventId", "FormattedMessage", "ExceptionJson", "ArgumentsJson", "ActivityTraceId", "ActivitySpanId"];

    static object?[] LogValues(object? level = null, object? eventId = null) =>
    [
        "MyApp.Checkout", level ?? 2, eventId ?? 7, "User 41 checkout failed after 900ms", null, ArgsJson, null, null
    ];

    static bool BuildLog(string[]? names, object?[] values, out LogRecord record) =>
        EventPipeLogPayload.TryBuildLogRecord(new LogMessageParser(), names, i => values[i], 99L, out record);

    [Fact]
    public void TryBuildLogRecord_HappyPath_PopulatesRecord()
    {
        Assert.True(BuildLog(LogNames, LogValues(), out var record));

        Assert.Equal("MyApp.Checkout", record.Category);
        Assert.Equal("User 41 checkout failed after 900ms", record.Message);
        Assert.Equal(LoomLogLevel.Information, record.Level);
        Assert.Equal(7, record.EventId);
        Assert.Equal(99L, record.TimestampUtcTicks);
        Assert.Equal("User {UserId} checkout failed after {Ms}ms", record.Template);
    }

    [Fact]
    public void TryBuildLogRecord_NullPayloadNames_ReturnsFalse()
    {
        Assert.False(BuildLog(null, [], out _));
    }

    [Fact]
    public void TryBuildLogRecord_MissingLoggerName_ReturnsFalse()
    {
        string[] names = ["Level", "FormattedMessage"];
        object?[] values = [2, "msg"];

        Assert.False(BuildLog(names, values, out _));
    }

    [Fact]
    public void TryBuildLogRecord_MissingFormattedMessage_ReturnsFalse()
    {
        string[] names = ["LoggerName", "Level"];
        object?[] values = ["Cat", 2];

        Assert.False(BuildLog(names, values, out _));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void TryBuildLogRecord_LevelRange_Gated(int level, bool expected)
    {
        Assert.Equal(expected, BuildLog(LogNames, LogValues(level: level), out _));
    }

    [Fact]
    public void TryBuildLogRecord_MissingLevel_ReturnsFalse()
    {
        string[] names = ["LoggerName", "FormattedMessage"];
        object?[] values = ["Cat", "msg"];

        Assert.False(BuildLog(names, values, out _));
    }

    [Fact]
    public void TryBuildLogRecord_LevelAndEventIdAsInt_Read()
    {
        Assert.True(BuildLog(LogNames, LogValues(level: 3, eventId: 12), out var record));

        Assert.Equal((LoomLogLevel)3, record.Level);
        Assert.Equal(12, record.EventId);
    }

    [Fact]
    public void TryBuildLogRecord_LevelAndEventIdAsString_Read()
    {
        Assert.True(BuildLog(LogNames, LogValues(level: "4", eventId: "13"), out var record));

        Assert.Equal((LoomLogLevel)4, record.Level);
        Assert.Equal(13, record.EventId);
    }

    [Fact]
    public void TryBuildLogRecord_UnknownPayloadNames_Ignored()
    {
        string[] names = ["Bogus", "LoggerName", "Level", "FormattedMessage", "AlsoBogus"];
        object?[] values = ["x", "Cat", 1, "msg", "y"];

        Assert.True(BuildLog(names, values, out var record));

        Assert.Equal("Cat", record.Category);
        Assert.Equal("msg", record.Message);
    }

    [Fact]
    public void ToInt32_BoxedInt_ReturnsUnchanged()
    {
        var result = EventPipeLogPayload.ToInt32((object)4, -1);

        Assert.Equal(4, result);
    }

    [Fact]
    public void ToInt32_Null_ReturnsFallback()
    {
        var result = EventPipeLogPayload.ToInt32(null, -1);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void ToInt32_NumericString_ReturnsParsed()
    {
        var result = EventPipeLogPayload.ToInt32("3", -1);

        Assert.Equal(3, result);
    }

    [Fact]
    public void ToInt32_NonNumericString_ReturnsFallback()
    {
        var result = EventPipeLogPayload.ToInt32("Warning", -1);

        Assert.Equal(-1, result);
    }
}
