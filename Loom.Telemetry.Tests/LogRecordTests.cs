using System;
using System.Runtime.CompilerServices;
using Loom.Telemetry;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class LogRecordTests
{
    [Fact]
    public void MinimalCtor_LeavesNewFieldsAtDefaults()
    {
        var record = new LogRecord("hello", "MyApp.Category", LoomLogLevel.Information, 12345L);

        Assert.Null(record.Template);
        Assert.Null(record.ArgumentsJson);
        Assert.Equal(0UL, record.TraceIdHi);
        Assert.Equal(0UL, record.TraceIdLo);
        Assert.Equal(0UL, record.SpanId);
    }

    [Fact]
    public void SevenArgumentPositionalForm_StillCompilesAndRoundTrips()
    {
        var record = new LogRecord(
            "message",
            "category",
            LoomLogLevel.Error,
            999L,
            42,
            "System.InvalidOperationException",
            "boom");

        Assert.Equal("message", record.Message);
        Assert.Equal("category", record.Category);
        Assert.Equal(LoomLogLevel.Error, record.Level);
        Assert.Equal(999L, record.TimestampUtcTicks);
        Assert.Equal(42, record.EventId);
        Assert.Equal("System.InvalidOperationException", record.ExceptionType);
        Assert.Equal("boom", record.ExceptionMessage);
    }

    [Fact]
    public void FullTwelveArgumentCtor_RoundTripsEveryValue()
    {
        var record = new LogRecord(
            message: "message",
            category: "category",
            level: LoomLogLevel.Warning,
            timestampUtcTicks: 555L,
            eventId: 7,
            exceptionType: "System.Exception",
            exceptionMessage: "err",
            template: "Order {OrderId} shipped",
            argumentsJson: "{\"OrderId\":123}",
            traceIdHi: 0x0102030405060708UL,
            traceIdLo: 0x1112131415161718UL,
            spanId: 0x2122232425262728UL);

        Assert.Equal("message", record.Message);
        Assert.Equal("category", record.Category);
        Assert.Equal(LoomLogLevel.Warning, record.Level);
        Assert.Equal(555L, record.TimestampUtcTicks);
        Assert.Equal(7, record.EventId);
        Assert.Equal("System.Exception", record.ExceptionType);
        Assert.Equal("err", record.ExceptionMessage);
        Assert.Equal("Order {OrderId} shipped", record.Template);
        Assert.Equal("{\"OrderId\":123}", record.ArgumentsJson);
        Assert.Equal(0x0102030405060708UL, record.TraceIdHi);
        Assert.Equal(0x1112131415161718UL, record.TraceIdLo);
        Assert.Equal(0x2122232425262728UL, record.SpanId);
    }

    [Fact]
    public void ToString_IsUnchanged_WhenNewFieldsArePopulated()
    {
        var record = new LogRecord(
            message: "message",
            category: "category",
            level: LoomLogLevel.Critical,
            timestampUtcTicks: 1L,
            eventId: 1,
            exceptionType: "System.Exception",
            exceptionMessage: "err",
            template: "Order {OrderId} shipped",
            argumentsJson: "{\"OrderId\":123}",
            traceIdHi: 1UL,
            traceIdLo: 2UL,
            spanId: 3UL);

        Assert.Equal("[Critical] category: message Exception=System.Exception: err", record.ToString());
    }

    [Fact]
    public void SizeOf_Is88Bytes_On64BitRuntime()
    {
        if (IntPtr.Size != 8)
            return;

        Assert.Equal(88, Unsafe.SizeOf<LogRecord>());
    }

    [Fact]
    public void NullMessageOrCategory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LogRecord(null!, "category", LoomLogLevel.Information, 0L));
        Assert.Throws<ArgumentNullException>(() =>
            new LogRecord("message", null!, LoomLogLevel.Information, 0L));
    }

    [Fact]
    public void LogBufferRoundTrip_PreservesNewFields()
    {
        var buffer = new LogBuffer(16);
        var record = new LogRecord(
            message: "message",
            category: "category",
            level: LoomLogLevel.Debug,
            timestampUtcTicks: 42L,
            eventId: 0,
            exceptionType: null,
            exceptionMessage: null,
            template: "Order {OrderId} shipped",
            argumentsJson: "{\"OrderId\":123}",
            traceIdHi: 0xAAUL,
            traceIdLo: 0xBBUL,
            spanId: 0xCCUL);

        buffer.Write(record);
        var results = buffer.ReadRecent(1);

        Assert.Single(results);
        var readBack = results[0];
        Assert.Equal("Order {OrderId} shipped", readBack.Template);
        Assert.Equal("{\"OrderId\":123}", readBack.ArgumentsJson);
        Assert.Equal(0xAAUL, readBack.TraceIdHi);
        Assert.Equal(0xBBUL, readBack.TraceIdLo);
        Assert.Equal(0xCCUL, readBack.SpanId);
    }
}
