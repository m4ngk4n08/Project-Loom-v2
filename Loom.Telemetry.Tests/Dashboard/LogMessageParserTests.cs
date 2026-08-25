using Loom.Dashboard;
using System.Text.Json;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

public class LogMessageParserTests
{
    [Fact]
    public void ExtractTemplateAndArgs_Null_ReturnsNulls()
    {
        var parser = new LogMessageParser();

        var (template, args) = parser.ExtractTemplateAndArgs(null);

        Assert.Null(template);
        Assert.Null(args);
    }

    [Fact]
    public void ExtractTemplateAndArgs_Empty_ReturnsNulls()
    {
        var parser = new LogMessageParser();

        var (template, args) = parser.ExtractTemplateAndArgs("");

        Assert.Null(template);
        Assert.Null(args);
    }

    [Fact]
    public void ExtractTemplateAndArgs_EmptyObject_ReturnsNulls()
    {
        var parser = new LogMessageParser();

        var (template, args) = parser.ExtractTemplateAndArgs("{}");

        Assert.Null(template);
        Assert.Null(args);
    }

    [Fact]
    public void ExtractTemplateAndArgs_MissingProperty_ReturnsNullTemplateAndOriginalArgs()
    {
        var parser = new LogMessageParser();
        var json = "{\"Path\":\"x\"}";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Null(template);
        Assert.Equal(json, args);
    }

    [Fact]
    public void ExtractTemplateAndArgs_MalformedJson_ReturnsNullTemplateAndOriginalArgs()
    {
        var parser = new LogMessageParser();
        var json = "{not json";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Null(template);
        Assert.Equal(json, args);
    }

    [Fact]
    public void ExtractTemplateAndArgs_SimpleCase_SplitsTemplateAndArgs()
    {
        var parser = new LogMessageParser();
        var json = "{\"Path\":\"x\",\"{OriginalFormat}\":\"processing {Path}\"}";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Equal("processing {Path}", template);
        Assert.NotNull(args);
        using var doc = JsonDocument.Parse(args);
        Assert.Equal("x", doc.RootElement.GetProperty("Path").GetString());
        Assert.False(doc.RootElement.TryGetProperty("{OriginalFormat}", out _));
    }

    [Fact]
    public void ExtractTemplateAndArgs_EscapedValues_SplitsTemplateAndValidJsonArgs()
    {
        var parser = new LogMessageParser();
        var json = "{\"Path\":\"C:\\\\temp\\\\a\\\"b\",\"{OriginalFormat}\":\"bad path \\\"{Path}\\\" rejected\"}";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Equal("bad path \"{Path}\" rejected", template);
        Assert.NotNull(args);
        using var doc = JsonDocument.Parse(args);
        Assert.Equal("C:\\temp\\a\"b", doc.RootElement.GetProperty("Path").GetString());
    }

    [Fact]
    public void ExtractTemplateAndArgs_NonAsciiValues_SplitsTemplateAndValidJsonArgs()
    {
        var parser = new LogMessageParser();
        var json = "{\"Nom\":\"caf\u00e9 \u00fcber\",\"{OriginalFormat}\":\"hello {Nom} \u2014 done\"}";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Equal("hello {Nom} \u2014 done", template);
        Assert.NotNull(args);
        using var doc = JsonDocument.Parse(args);
        Assert.Equal("caf\u00e9 \u00fcber", doc.RootElement.GetProperty("Nom").GetString());
    }

    [Fact]
    public void ExtractTemplateAndArgs_TemplateOnly_ArgsBecomeNull()
    {
        var parser = new LogMessageParser();
        var json = "{\"{OriginalFormat}\":\"no args here\"}";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Equal("no args here", template);
        Assert.Null(args);
    }

    [Fact]
    public void ExtractTemplateAndArgs_OriginalFormatFirst_SplitsTemplateAndArgs()
    {
        var parser = new LogMessageParser();
        var json = "{\"{OriginalFormat}\":\"processing {Path}\",\"Path\":\"x\"}";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Equal("processing {Path}", template);
        Assert.NotNull(args);
        using var doc = JsonDocument.Parse(args);
        Assert.Equal("x", doc.RootElement.GetProperty("Path").GetString());
        Assert.False(doc.RootElement.TryGetProperty("{OriginalFormat}", out _));
    }

    [Fact]
    public void ExtractTemplateAndArgs_SameTemplateTwice_ReturnsPooledReferenceEqualInstance()
    {
        var parser = new LogMessageParser();
        var json = "{\"Path\":\"x\",\"{OriginalFormat}\":\"processing {Path}\"}";

        var (template1, _) = parser.ExtractTemplateAndArgs(json);
        var (template2, _) = parser.ExtractTemplateAndArgs(json);

        Assert.Same(template1, template2);
    }

    [Fact]
    public void ExtractTemplateAndArgs_PastCap_StopsPoolingButStillReturnsCorrectTemplate()
    {
        var parser = new LogMessageParser();

        for (var i = 0; i < 1100; i++)
        {
            var json = $"{{\"{{OriginalFormat}}\":\"template number {i}\"}}";
            var (template, _) = parser.ExtractTemplateAndArgs(json);
            Assert.Equal($"template number {i}", template);
        }

        Assert.Equal(LogMessageParser.MaxPooledTemplates, parser.PooledTemplateCount);

        var (lastTemplate, _) = parser.ExtractTemplateAndArgs("{\"{OriginalFormat}\":\"template number 1099\"}");
        Assert.Equal("template number 1099", lastTemplate);
    }

    [Fact]
    public void TryParseTraceId_ValidHex_RoundTripsThroughFormat()
    {
        var hex = "4bf92f3577b34da6a3ce929d0e0e4736";

        var parsed = LogMessageParser.TryParseTraceId(hex, out var hi, out var lo);

        Assert.True(parsed);
        var formatted = LogMessageParser.FormatTraceId(hi, lo);
        Assert.Equal(hex, formatted);
    }

    [Fact]
    public void TryParseTraceId_Empty_ReturnsFalseAndZeros()
    {
        var parsed = LogMessageParser.TryParseTraceId("", out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void TryParseTraceId_WrongLength_ReturnsFalse()
    {
        var parsed = LogMessageParser.TryParseTraceId("abcd", out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void TryParseTraceId_NonHex_ReturnsFalse()
    {
        var parsed = LogMessageParser.TryParseTraceId("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void FormatTraceId_BothZero_ReturnsNull()
    {
        var formatted = LogMessageParser.FormatTraceId(0, 0);

        Assert.Null(formatted);
    }

    [Fact]
    public void TryParseSpanId_ValidHex_RoundTripsThroughFormat()
    {
        var hex = "00f067aa0ba902b7";

        var parsed = LogMessageParser.TryParseSpanId(hex, out var id);

        Assert.True(parsed);
        var formatted = LogMessageParser.FormatSpanId(id);
        Assert.Equal(hex, formatted);
    }

    [Fact]
    public void TryParseSpanId_Empty_ReturnsFalseAndZero()
    {
        var parsed = LogMessageParser.TryParseSpanId("", out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void TryParseSpanId_WrongLength_ReturnsFalse()
    {
        var parsed = LogMessageParser.TryParseSpanId("abcd", out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void TryParseSpanId_NonHex_ReturnsFalse()
    {
        var parsed = LogMessageParser.TryParseSpanId("zzzzzzzzzzzzzzzz", out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void FormatSpanId_Zero_ReturnsNull()
    {
        var formatted = LogMessageParser.FormatSpanId(0);

        Assert.Null(formatted);
    }

    [Fact]
    public void ExtractTemplateAndArgs_OversizedTemplate_ReturnedInFull()
    {
        var parser = new LogMessageParser();
        var template = new string('a', 600);
        var json = $"{{\"{{OriginalFormat}}\":\"{template}\"}}";

        var (result, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Equal(template, result);
        Assert.Null(args);
    }

    [Fact]
    public void ExtractTemplateAndArgs_OversizedTemplateTwice_StillPoolsReferenceEqual()
    {
        var parser = new LogMessageParser();
        var template = new string('b', 600);
        var json = $"{{\"{{OriginalFormat}}\":\"{template}\"}}";

        var (result1, _) = parser.ExtractTemplateAndArgs(json);
        var (result2, _) = parser.ExtractTemplateAndArgs(json);

        Assert.Same(result1, result2);
    }

    [Fact]
    public void TryParseTraceId_AllZeroHex_ReturnsFalse()
    {
        var parsed = LogMessageParser.TryParseTraceId(new string('0', 32), out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void TryParseSpanId_AllZeroHex_ReturnsFalse()
    {
        var parsed = LogMessageParser.TryParseSpanId(new string('0', 16), out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void TryParseTraceId_EmbeddedWhitespace_ReturnsFalseAndZeros()
    {
        var parsed = LogMessageParser.TryParseTraceId(" af7651916cd43d  af7651916cd43d ", out var hi, out var lo);

        Assert.False(parsed);
        Assert.Equal(0UL, hi);
        Assert.Equal(0UL, lo);
    }

    [Fact]
    public void TryParseSpanId_PaddedWithSpaces_ReturnsFalseAndZero()
    {
        var parsed = LogMessageParser.TryParseSpanId(" af7651916cd43 ", out var id);

        Assert.False(parsed);
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void TryParseTraceId_UppercaseHex_ParsesAndFormatsLowercase()
    {
        var lower = "4bf92f3577b34da6a3ce929d0e0e4736";
        var upper = lower.ToUpperInvariant();

        var parsed = LogMessageParser.TryParseTraceId(upper, out var hi, out var lo);

        Assert.True(parsed);
        var formatted = LogMessageParser.FormatTraceId(hi, lo);
        Assert.Equal(lower, formatted);
    }

    [Fact]
    public void ExtractTemplateAndArgs_OriginalFormatInMiddle_BothSiblingsSurvive()
    {
        var parser = new LogMessageParser();
        var json = "{\"Before\":\"x\",\"{OriginalFormat}\":\"processing {Before} then {After}\",\"After\":\"y\"}";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Equal("processing {Before} then {After}", template);
        Assert.NotNull(args);
        using var doc = JsonDocument.Parse(args);
        Assert.Equal("x", doc.RootElement.GetProperty("Before").GetString());
        Assert.Equal("y", doc.RootElement.GetProperty("After").GetString());
        Assert.False(doc.RootElement.TryGetProperty("{OriginalFormat}", out _));
    }

    [Fact]
    public void ExtractTemplateAndArgs_NestedOriginalFormat_TopLevelExtractedNestedSurvives()
    {
        var parser = new LogMessageParser();
        var json = "{\"Inner\":{\"{OriginalFormat}\":\"nested template\"},\"{OriginalFormat}\":\"top level template\"}";

        var (template, args) = parser.ExtractTemplateAndArgs(json);

        Assert.Equal("top level template", template);
        Assert.NotNull(args);
        using var doc = JsonDocument.Parse(args);
        var inner = doc.RootElement.GetProperty("Inner");
        Assert.Equal("nested template", inner.GetProperty("{OriginalFormat}").GetString());
        Assert.False(doc.RootElement.TryGetProperty("{OriginalFormat}", out _));
    }
}
