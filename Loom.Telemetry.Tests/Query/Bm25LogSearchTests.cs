using System;
using System.Linq;
using Loom.Telemetry;
using Loom.Telemetry.Query;
using Xunit;

namespace Loom.Telemetry.Tests.Query;

public class Bm25LogSearchTests
{
    private static readonly long Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private static LogRecord Record(
        string message,
        string category = "Test",
        long ticks = 0,
        LoomLogLevel level = LoomLogLevel.Information,
        int eventId = 0,
        string? exceptionType = null,
        string? exceptionMessage = null) =>
        new(message, category, level, Base + ticks, eventId, exceptionType, exceptionMessage);

    [Fact]
    public void Search_RareTermVsCommonTerm_RareTermOutranksCommon()
    {
        var docs = new LogRecord[20];
        for (var i = 0; i < 20; i++)
        {
            var hasCommon = i < 18;
            var text = hasCommon ? "common alpha" : "filler beta";
            docs[i] = Record(text);
        }
        docs[0] = Record("common alpha");
        docs[19] = Record("filler rare");

        var commonResults = Bm25LogSearch.Search(docs, "common", 20, 0.0);
        var rareResults = Bm25LogSearch.Search(docs, "rare", 20, 0.0);

        Assert.NotEmpty(commonResults);
        Assert.NotEmpty(rareResults);
        Assert.True(rareResults[0].Score > commonResults[0].Score);
    }

    [Fact]
    public void Search_TermInEveryDocument_ScoreIsNonNegative()
    {
        var docs = Enumerable.Range(0, 10).Select(i => Record("ubiquitous term " + i)).ToArray();

        var results = Bm25LogSearch.Search(docs, "ubiquitous", 10, double.MinValue);

        Assert.All(results, r => Assert.True(r.Score >= 0));
    }

    [Fact]
    public void Search_TermFrequencySaturates_RatioBelowLinear()
    {
        var docs = new[]
        {
            Record(string.Join(' ', Enumerable.Repeat("target", 10)) + " filler filler filler"),
            Record("target filler filler filler filler filler filler filler filler filler filler filler")
        };

        var results = Bm25LogSearch.Search(docs, "target", 10, 0.0);
        var highFrequencyScore = results.Single(r => r.Content.StartsWith("target target")).Score;
        var lowFrequencyScore = results.Single(r => !r.Content.StartsWith("target target")).Score;

        Assert.True(highFrequencyScore > lowFrequencyScore);
        Assert.True(highFrequencyScore / lowFrequencyScore < 10);
    }

    [Fact]
    public void Search_ShorterDocumentRanksFirst_ForSameOccurrenceCount()
    {
        var docs = new[]
        {
            Record("keyword padding padding padding padding padding padding padding padding padding"),
            Record("keyword")
        };

        var results = Bm25LogSearch.Search(docs, "keyword", 10, 0.0);

        Assert.Equal("keyword", results[0].Content);
    }

    [Fact]
    public void Search_ResultsAreSortedStrictlyDescendingByScore()
    {
        var docs = new[]
        {
            Record("alpha alpha alpha"),
            Record("alpha padding padding padding padding"),
            Record("alpha beta padding padding padding padding padding padding")
        };

        var results = Bm25LogSearch.Search(docs, "alpha", 10, 0.0);

        for (var i = 1; i < results.Length; i++)
            Assert.True(results[i - 1].Score >= results[i].Score);
    }

    [Fact]
    public void Search_MaxResults_ClampsResultCount()
    {
        var docs = Enumerable.Range(0, 50).Select(i => Record("needle document " + i)).ToArray();

        var results = Bm25LogSearch.Search(docs, "needle", 5, 0.0);

        Assert.Equal(5, results.Length);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmptyArray()
    {
        var docs = new[] { Record("some content") };

        var results = Bm25LogSearch.Search(docs, "", 10, 0.0);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_EmptyCorpus_ReturnsEmptyArray()
    {
        var results = Bm25LogSearch.Search(Array.Empty<LogRecord>(), "query", 10, 0.0);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmptyArray()
    {
        var docs = new[] { Record("completely unrelated text") };

        var results = Bm25LogSearch.Search(docs, "nonexistentterm", 10, 0.0);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_MapsSourceAndTimestamp()
    {
        var docs = new[]
        {
            Record(
                "mapped term",
                category: "OrderService",
                ticks: 12345,
                level: LoomLogLevel.Error,
                eventId: 42,
                exceptionType: "InvalidOperationException",
                exceptionMessage: "boom")
        };

        var results = Bm25LogSearch.Search(docs, "mapped", 10, 0.0);

        Assert.Single(results);
        Assert.Equal("OrderService", results[0].Source);
        Assert.Equal(Base + 12345, results[0].Timestamp.Ticks);
        Assert.Equal(DateTimeKind.Utc, results[0].Timestamp.Kind);
        Assert.Equal("Error", results[0].Level);
        Assert.Equal(42, results[0].EventId);
        Assert.Equal("InvalidOperationException", results[0].ExceptionType);
        Assert.Equal("boom", results[0].ExceptionMessage);
    }

    private const string TraceHex = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string SpanHex = "00f067aa0ba902b7";

    [Fact]
    public void Search_MapsTemplateAndArgumentsJson()
    {
        var docs = new[]
        {
            new LogRecord(
                "processing order 42", "Test", LoomLogLevel.Information, Base,
                template: "processing order {OrderId}", argumentsJson: "{\"OrderId\":42}")
        };

        var results = Bm25LogSearch.Search(docs, "processing", 10, 0.0);

        Assert.Single(results);
        Assert.Equal("processing order {OrderId}", results[0].Template);
        Assert.Equal("{\"OrderId\":42}", results[0].ArgumentsJson);
    }

    [Fact]
    public void Search_MapsTraceIdAndSpanId_WhenPopulated()
    {
        W3CTraceId.TryParseTraceId(TraceHex, out var traceHi, out var traceLo);
        W3CTraceId.TryParseSpanId(SpanHex, out var spanId);

        var docs = new[]
        {
            new LogRecord(
                "traced message", "Test", LoomLogLevel.Information, Base,
                traceIdHi: traceHi, traceIdLo: traceLo, spanId: spanId)
        };

        var results = Bm25LogSearch.Search(docs, "traced", 10, 0.0);

        Assert.Single(results);
        Assert.Equal(TraceHex, results[0].TraceId);
        Assert.Equal(SpanHex, results[0].SpanId);
    }

    [Fact]
    public void Search_AllIdFieldsZero_TraceIdAndSpanIdAreNull()
    {
        var docs = new[]
        {
            new LogRecord("untraced message", "Test", LoomLogLevel.Information, Base)
        };

        var results = Bm25LogSearch.Search(docs, "untraced", 10, 0.0);

        Assert.Single(results);
        Assert.Null(results[0].TraceId);
        Assert.Null(results[0].SpanId);
    }

    [Fact]
    public void Search_NoTemplate_TemplateAndArgumentsJsonAreNull()
    {
        var docs = new[]
        {
            new LogRecord("plain message", "Test", LoomLogLevel.Information, Base)
        };

        var results = Bm25LogSearch.Search(docs, "plain", 10, 0.0);

        Assert.Single(results);
        Assert.Null(results[0].Template);
        Assert.Null(results[0].ArgumentsJson);
    }

    [Fact]
    public void Search_WordOnlyInTemplate_DoesNotMatch()
    {
        var docs = new[]
        {
            new LogRecord(
                "rendered message", "Test", LoomLogLevel.Information, Base,
                template: "distinctivetemplateword {Value}")
        };

        var results = Bm25LogSearch.Search(docs, "distinctivetemplateword", 10, 0.0);

        Assert.Empty(results);
    }
}
