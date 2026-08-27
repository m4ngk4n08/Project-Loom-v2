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
