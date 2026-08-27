using System;
using System.Linq;
using System.Text;
using Loom.Dashboard;
using Loom.Dashboard.Extensions;
using Loom.Telemetry;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

public class LogExportTests
{
    private static readonly long Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private static LogRecord Record(string message, string category, LoomLogLevel level, long ticks) =>
        new(message, category, level, ticks);

    [Fact]
    public void CsvField_PlainValue_IsNotQuoted()
    {
        Assert.Equal("plain", EndpointExtensions.CsvField("plain"));
    }

    [Fact]
    public void CsvField_ValueWithComma_IsQuoted()
    {
        Assert.Equal("\"a,b\"", EndpointExtensions.CsvField("a,b"));
    }

    [Fact]
    public void CsvField_ValueWithCommaQuoteAndNewline_RoundTripsExactly()
    {
        var value = "error: \"disk full\", retrying\nsecond line";
        var expected = "\"error: \"\"disk full\"\", retrying\nsecond line\"";

        Assert.Equal(expected, EndpointExtensions.CsvField(value));
    }

    [Fact]
    public void WriteCsvExport_MessageWithCommaQuoteAndNewline_RoundTripsExactly()
    {
        var record = Record("error: \"disk full\", retrying\nsecond line", "cat", LoomLogLevel.Error, Base);
        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([record]);
        var csv = Encoding.UTF8.GetString(result.FileContents.Span);

        var expectedRow =
            $"{EndpointExtensions.CsvField(record.TimestampUtc.ToString("O"))}," +
            "Error,cat,0," +
            "\"error: \"\"disk full\"\", retrying\nsecond line\"," +
            ",,,\r\n";

        Assert.Equal("text/csv", result.ContentType);
        Assert.EndsWith(expectedRow, csv);
    }

    [Fact]
    public void WriteCsvExport_EmptyResultSet_ProducesHeaderOnlyCsv()
    {
        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([]);
        var csv = Encoding.UTF8.GetString(result.FileContents.Span);

        Assert.Equal("Timestamp,Level,Category,EventId,Message,ExceptionType,ExceptionMessage,TraceId,Template\r\n", csv);
    }

    [Fact]
    public void WriteCsvExport_SetsAttachmentFileName()
    {
        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([]);

        Assert.NotNull(result.FileDownloadName);
        Assert.EndsWith(".csv", result.FileDownloadName);
    }

    [Fact]
    public void ToUtcTicks_UtcInput_TicksUnchanged()
    {
        var value = new DateTime(2026, 3, 5, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal(value.Ticks, EndpointExtensions.ToUtcTicks(value));
    }

    [Fact]
    public void ToUtcTicks_UnspecifiedInput_TreatedAsUtcNotShifted()
    {
        var value = new DateTime(2026, 3, 5, 9, 0, 0, DateTimeKind.Unspecified);
        var expected = new DateTime(2026, 3, 5, 9, 0, 0, DateTimeKind.Utc).Ticks;

        Assert.Equal(expected, EndpointExtensions.ToUtcTicks(value));
    }

    [Fact]
    public void ToUtcTicks_LocalInput_ConvertedToUniversalTime()
    {
        var value = DateTime.SpecifyKind(new DateTime(2026, 3, 5, 9, 0, 0), DateTimeKind.Local);

        Assert.Equal(value.ToUniversalTime().Ticks, EndpointExtensions.ToUtcTicks(value));
    }

    [Fact]
    public void ToUtcTicks_Null_ReturnsNull()
    {
        Assert.Null(EndpointExtensions.ToUtcTicks(null));
    }

    private const string TraceHex = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public void WriteCsvExport_RecordWithTraceIdAndTemplate_EmitsBothInTrailingFields()
    {
        W3CTraceId.TryParseTraceId(TraceHex, out var hi, out var lo);
        var record = new LogRecord(
            "User 42 logged in", "cat", LoomLogLevel.Information, Base,
            template: "User {UserId} logged in", traceIdHi: hi, traceIdLo: lo);

        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([record]);
        var csv = Encoding.UTF8.GetString(result.FileContents.Span);

        Assert.EndsWith($"{TraceHex},User {{UserId}} logged in\r\n", csv);
    }

    [Fact]
    public void WriteCsvExport_RecordWithNeitherTraceIdNorTemplate_EmitsTwoEmptyTrailingFields()
    {
        var record = new LogRecord("plain message", "cat", LoomLogLevel.Information, Base);

        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([record]);
        var csv = Encoding.UTF8.GetString(result.FileContents.Span);

        Assert.EndsWith(",,\r\n", csv);
    }

    [Fact]
    public void WriteCsvExport_TemplateWithComma_IsRfc4180Quoted()
    {
        const string template = "User {UserId}, order {OrderId} failed";
        var record = new LogRecord(
            "message", "cat", LoomLogLevel.Error, Base, template: template);

        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([record]);
        var csv = Encoding.UTF8.GetString(result.FileContents.Span);

        Assert.EndsWith($",{EndpointExtensions.CsvField(template)}\r\n", csv);
    }

    [Fact]
    public void WriteCsvExport_PartiallyPopulatedRecords_HaveSameFieldCountAsFullyPopulated()
    {
        W3CTraceId.TryParseTraceId(TraceHex, out var hi, out var lo);

        var full = new LogRecord(
            "full", "cat", LoomLogLevel.Information, Base,
            template: "Full {X}", traceIdHi: hi, traceIdLo: lo);
        var traceOnly = new LogRecord(
            "trace only", "cat", LoomLogLevel.Information, Base,
            traceIdHi: hi, traceIdLo: lo);
        var templateOnly = new LogRecord(
            "template only", "cat", LoomLogLevel.Information, Base,
            template: "Template {Y}");

        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([full, traceOnly, templateOnly]);
        var csv = Encoding.UTF8.GetString(result.FileContents.Span);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var dataRows = lines.Skip(1).ToArray();

        Assert.Equal(3, dataRows.Length);
        var commaCounts = dataRows.Select(row => row.Count(c => c == ',')).ToArray();
        Assert.All(commaCounts, count => Assert.Equal(commaCounts[0], count));
    }

    [Fact]
    public void WriteCsvExport_MixedRecords_HeaderAndDataRowsAgreeOnColumnCount()
    {
        W3CTraceId.TryParseTraceId(TraceHex, out var hi, out var lo);

        var records = new[]
        {
            new LogRecord("a", "cat", LoomLogLevel.Information, Base, template: "A {X}", traceIdHi: hi, traceIdLo: lo),
            new LogRecord("b", "cat", LoomLogLevel.Warning, Base + 1),
            new LogRecord("c", "cat", LoomLogLevel.Error, Base + 2, template: "C {Z}")
        };

        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport(records);
        var csv = Encoding.UTF8.GetString(result.FileContents.Span);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        var headerColumnCount = lines[0].Count(c => c == ',') + 1;
        foreach (var dataRow in lines.Skip(1))
        {
            Assert.Equal(headerColumnCount, dataRow.Count(c => c == ',') + 1);
        }
    }

    [Fact]
    public void WriteTextExport_OneLinePerRecord()
    {
        var records = new[]
        {
            Record("first", "catA", LoomLogLevel.Information, Base),
            Record("second", "catB", LoomLogLevel.Warning, Base + 1)
        };

        var result = (FileContentHttpResult)EndpointExtensions.WriteTextExport(records);
        var text = Encoding.UTF8.GetString(result.FileContents.Span);

        Assert.Equal("[Information] catA: first\n[Warning] catB: second", text);
        Assert.Equal("text/plain", result.ContentType);
    }
}
