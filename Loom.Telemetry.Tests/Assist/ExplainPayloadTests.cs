using System;
using Loom.Telemetry.Assist;
using Xunit;

namespace Loom.Telemetry.Tests.Assist;

public sealed class ExplainPayloadTests
{
    [Fact]
    public void Build_NullTemplate_ReturnsNull()
    {
        var result = ExplainPayloadBuilder.Build(null, null, null, null, null);

        Assert.Null(result);
    }

    [Fact]
    public void Build_EmptyOrWhitespaceTemplate_ReturnsNull()
    {
        Assert.Null(ExplainPayloadBuilder.Build("", null, null, null, null));
        Assert.Null(ExplainPayloadBuilder.Build("   ", null, null, null, null));
    }

    [Fact]
    public void Build_ValidTemplateWithArguments_ArgumentNamesInOrder()
    {
        var result = ExplainPayloadBuilder.Build(
            "processing {UserId} in {Ms}ms", "{\"UserId\":\"41\",\"Ms\":\"900\"}", null, null, null);

        Assert.NotNull(result);
        Assert.Equal(["UserId", "Ms"], result.ArgumentNames);
    }

    [Fact]
    public void ToPromptText_ArgumentValuesNeverAppear_RedactionGuarantee()
    {
        var payload = ExplainPayloadBuilder.Build(
            "processing {UserId} in {Ms}ms", "{\"UserId\":\"41\",\"Ms\":\"900\"}", null, null, null);

        var text = ExplainPayloadBuilder.ToPromptText(payload!);

        Assert.DoesNotContain("41", text);
        Assert.DoesNotContain("900", text);
    }

    [Fact]
    public void Build_OriginalFormatProperty_ExcludedFromArgumentNames()
    {
        var result = ExplainPayloadBuilder.Build(
            "processing {UserId}",
            "{\"UserId\":\"41\",\"{OriginalFormat}\":\"processing {UserId}\"}",
            null, null, null);

        Assert.NotNull(result);
        Assert.Equal(["UserId"], result.ArgumentNames);
    }

    [Fact]
    public void Build_MalformedArgumentsJson_EmptyArgumentNamesDoesNotThrow()
    {
        var ex = Record.Exception(() => ExplainPayloadBuilder.Build(
            "plain message", "{not json", null, null, null));

        Assert.Null(ex);
        var result = ExplainPayloadBuilder.Build("plain message", "{not json", null, null, null);
        Assert.NotNull(result);
        Assert.Empty(result.ArgumentNames);
    }

    [Fact]
    public void Build_ArgumentsJsonNotAnObject_EmptyArgumentNames()
    {
        Assert.Empty(ExplainPayloadBuilder.Build("m", "[1,2,3]", null, null, null)!.ArgumentNames);
        Assert.Empty(ExplainPayloadBuilder.Build("m", "\"a string\"", null, null, null)!.ArgumentNames);
        Assert.Empty(ExplainPayloadBuilder.Build("m", "null", null, null, null)!.ArgumentNames);
    }

    [Fact]
    public void Build_NullArgumentsJson_EmptyArgumentNamesPayloadStillBuilt()
    {
        var result = ExplainPayloadBuilder.Build("plain message", null, null, null, null);

        Assert.NotNull(result);
        Assert.Empty(result.ArgumentNames);
    }

    [Fact]
    public void Build_CategoryLevelExceptionType_PassThroughUnchanged()
    {
        var result = ExplainPayloadBuilder.Build(
            "template", null, "MyApp.Checkout", "Error", "System.InvalidOperationException");

        Assert.NotNull(result);
        Assert.Equal("MyApp.Checkout", result.Category);
        Assert.Equal("Error", result.Level);
        Assert.Equal("System.InvalidOperationException", result.ExceptionType);
    }

    [Fact]
    public void ToPromptText_ContainsTemplateVerbatim()
    {
        var payload = ExplainPayloadBuilder.Build("processing {UserId} order", null, null, null, null)!;

        var text = ExplainPayloadBuilder.ToPromptText(payload);

        Assert.Contains("processing {UserId} order", text);
    }

    [Fact]
    public void ToPromptText_ContainsEveryArgumentName()
    {
        var payload = ExplainPayloadBuilder.Build(
            "processing {UserId} in {Ms}ms", "{\"UserId\":\"41\",\"Ms\":\"900\"}", null, null, null)!;

        var text = ExplainPayloadBuilder.ToPromptText(payload);

        Assert.Contains("UserId", text);
        Assert.Contains("Ms", text);
    }

    [Fact]
    public void ToPromptText_AbsentSections_OmittedFromOutput()
    {
        var payload = ExplainPayloadBuilder.Build("template", null, null, null, null)!;

        var text = ExplainPayloadBuilder.ToPromptText(payload);

        Assert.DoesNotContain("Category:", text);
        Assert.DoesNotContain("Level:", text);
        Assert.DoesNotContain("Exception type:", text);
    }

    [Fact]
    public void FromEnvironment_NoApiKey_ReturnsNull()
    {
        var original = Environment.GetEnvironmentVariable("LOOM_LLM_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("LOOM_LLM_API_KEY", null);

            var result = AssistOptions.FromEnvironment();

            Assert.Null(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOOM_LLM_API_KEY", original);
        }
    }

    [Fact]
    public void FromEnvironment_WhitespaceOnlyApiKey_ReturnsNull()
    {
        var original = Environment.GetEnvironmentVariable("LOOM_LLM_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("LOOM_LLM_API_KEY", "   ");

            var result = AssistOptions.FromEnvironment();

            Assert.Null(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOOM_LLM_API_KEY", original);
        }
    }

    [Fact]
    public void FromEnvironment_KeySetModelUnset_ModelEqualsDefault()
    {
        var originalKey = Environment.GetEnvironmentVariable("LOOM_LLM_API_KEY");
        var originalModel = Environment.GetEnvironmentVariable("LOOM_LLM_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("LOOM_LLM_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("LOOM_LLM_MODEL", null);

            var result = AssistOptions.FromEnvironment();

            Assert.NotNull(result);
            Assert.Equal(AssistOptions.DefaultModel, result.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOOM_LLM_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("LOOM_LLM_MODEL", originalModel);
        }
    }

    [Fact]
    public void FromEnvironment_MalformedTimeout_DefaultsTo30SecondsNoThrow()
    {
        var originalKey = Environment.GetEnvironmentVariable("LOOM_LLM_API_KEY");
        var originalTimeout = Environment.GetEnvironmentVariable("LOOM_LLM_TIMEOUT_SECONDS");
        try
        {
            Environment.SetEnvironmentVariable("LOOM_LLM_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("LOOM_LLM_TIMEOUT_SECONDS", "not-a-number");

            AssistOptions? result = null;
            var ex = Record.Exception(() => result = AssistOptions.FromEnvironment());

            Assert.Null(ex);
            Assert.NotNull(result);
            Assert.Equal(TimeSpan.FromSeconds(30), result.Timeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOOM_LLM_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("LOOM_LLM_TIMEOUT_SECONDS", originalTimeout);
        }
    }
}
