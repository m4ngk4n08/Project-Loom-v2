using System;
using System.Text.Json;
using Loom.Dashboard;
using Loom.Dashboard.Extensions;
using Loom.Telemetry;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

public class LogDtoProjectionTests
{
    const string TraceHex = "4bf92f3577b34da6a3ce929d0e0e4736";
    const string SpanHex = "00f067aa0ba902b7";

    private static readonly long Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    [Fact]
    public void ToDto_AllFourSourcesPopulated_ProjectsThemUnchanged()
    {
        var record = new LogRecord(
            "User 41 checkout failed after 900ms", "MyApp.Checkout", LoomLogLevel.Warning, Base,
            0, null, null,
            "User {UserId} checkout failed after {Ms}ms", "{\"UserId\":\"41\",\"Ms\":\"900\"}",
            0x4bf92f3577b34da6, 0xa3ce929d0e0e4736, 0x00f067aa0ba902b7);

        var dto = EndpointExtensions.ToDto(record);

        Assert.Equal("User {UserId} checkout failed after {Ms}ms", dto.Template);
        Assert.Equal("{\"UserId\":\"41\",\"Ms\":\"900\"}", dto.ArgumentsJson);
        Assert.Equal(TraceHex, dto.TraceId);
        Assert.Equal(SpanHex, dto.SpanId);
    }

    [Fact]
    public void ToDto_AllIdComponentsZero_TraceIdAndSpanIdAreNull()
    {
        var record = new LogRecord(
            "plain message", "MyApp.Checkout", LoomLogLevel.Warning, Base,
            0, null, null, null, null, 0, 0, 0);

        var dto = EndpointExtensions.ToDto(record);

        Assert.Null(dto.TraceId);
        Assert.Null(dto.SpanId);
    }

    [Fact]
    public void ToDto_NullTemplateAndArgumentsJson_OtherFieldsStillCorrect()
    {
        var record = new LogRecord(
            "plain message", "MyApp.Checkout", LoomLogLevel.Error, Base, 42);

        var dto = EndpointExtensions.ToDto(record);

        Assert.Null(dto.Template);
        Assert.Null(dto.ArgumentsJson);
        Assert.Equal("plain message", dto.Message);
        Assert.Equal("MyApp.Checkout", dto.Category);
        Assert.Equal("Error", dto.Level);
        Assert.Equal(record.TimestampUtc, dto.TimestampUtc);
        Assert.Equal(42, dto.EventId);
    }

    [Fact]
    public void Serialize_FullyPopulatedDto_ContainsAllFourCamelCaseKeys()
    {
        var dto = new LogEntryDto
        {
            Message = "msg",
            Category = "cat",
            Level = "Warning",
            TimestampUtc = DateTime.UtcNow,
            EventId = 0,
            Template = "User {UserId} checkout failed after {Ms}ms",
            ArgumentsJson = "{\"UserId\":\"41\",\"Ms\":\"900\"}",
            TraceId = TraceHex,
            SpanId = SpanHex
        };

        var json = JsonSerializer.Serialize(dto, LoomJsonSerializerContext.Default.LogEntryDto);

        Assert.Contains("\"template\"", json);
        Assert.Contains("\"argumentsJson\"", json);
        Assert.Contains("\"traceId\"", json);
        Assert.Contains("\"spanId\"", json);
    }

    [Fact]
    public void Serialize_AllFourNewValuesNull_OmitsAllFourKeys()
    {
        var dto = new LogEntryDto
        {
            Message = "msg",
            Category = "cat",
            Level = "Warning",
            TimestampUtc = DateTime.UtcNow,
            EventId = 0
        };

        var json = JsonSerializer.Serialize(dto, LoomJsonSerializerContext.Default.LogEntryDto);

        Assert.DoesNotContain("\"template\"", json);
        Assert.DoesNotContain("\"argumentsJson\"", json);
        Assert.DoesNotContain("\"traceId\"", json);
        Assert.DoesNotContain("\"spanId\"", json);
    }

    [Fact]
    public void ToDto_MalformedArgumentsJson_SerializedResponseStaysValidJsonAndPreservesTextVerbatim()
    {
        var record = new LogRecord(
            "plain message", "MyApp.Checkout", LoomLogLevel.Warning, Base,
            0, null, null, null, "{not json", 0, 0, 0);

        var dto = EndpointExtensions.ToDto(record);
        var json = JsonSerializer.Serialize(dto, LoomJsonSerializerContext.Default.LogEntryDto);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("{not json", doc.RootElement.GetProperty("argumentsJson").GetString());
    }

    [Fact]
    public void EndToEnd_BuildLogRecordThenToDto_HexInHexOut()
    {
        var parser = new LogMessageParser();

        var record = EventPipeBridge.BuildLogRecord(
            parser, "User 41 checkout failed after 900ms", "MyApp.Checkout", 2,
            Base, 0, null, null, TraceHex, SpanHex);

        var dto = EndpointExtensions.ToDto(record);

        Assert.Equal(TraceHex, dto.TraceId);
        Assert.Equal(SpanHex, dto.SpanId);
    }
}
