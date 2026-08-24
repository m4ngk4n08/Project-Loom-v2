using System;
using System.Linq;
using System.Text;
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
            ",\r\n";

        Assert.Equal("text/csv", result.ContentType);
        Assert.EndsWith(expectedRow, csv);
    }

    [Fact]
    public void WriteCsvExport_EmptyResultSet_ProducesHeaderOnlyCsv()
    {
        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([]);
        var csv = Encoding.UTF8.GetString(result.FileContents.Span);

        Assert.Equal("Timestamp,Level,Category,EventId,Message,ExceptionType,ExceptionMessage\r\n", csv);
    }

    [Fact]
    public void WriteCsvExport_SetsAttachmentFileName()
    {
        var result = (FileContentHttpResult)EndpointExtensions.WriteCsvExport([]);

        Assert.NotNull(result.FileDownloadName);
        Assert.EndsWith(".csv", result.FileDownloadName);
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
