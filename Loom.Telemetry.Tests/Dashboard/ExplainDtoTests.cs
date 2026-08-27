using System.Text.Json;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

public class ExplainDtoTests
{
    [Fact]
    public void Serialize_ExplainResponse_ContainsAllFiveCamelCaseKeys()
    {
        var response = new ExplainResponse
        {
            Explanation = "This means the payment gateway timed out.",
            ModelUsed = "claude-opus-5",
            SentText = "Log message template: processing {UserId}\nArgument names: UserId\n",
            InputTokens = 123,
            OutputTokens = 45
        };

        var json = JsonSerializer.Serialize(response, LoomJsonSerializerContext.Default.ExplainResponse);

        Assert.Contains("\"explanation\"", json);
        Assert.Contains("\"modelUsed\"", json);
        Assert.Contains("\"sentText\"", json);
        Assert.Contains("\"inputTokens\"", json);
        Assert.Contains("\"outputTokens\"", json);
    }

    [Fact]
    public void Serialize_ExplainResponse_SentTextRoundTripsExactlyIncludingNewlines()
    {
        var sentText = "Log message template: processing {UserId} in {Ms}ms\nArgument names: UserId, Ms\nCategory: MyApp.Checkout\n";
        var response = new ExplainResponse
        {
            Explanation = "explanation",
            ModelUsed = "claude-opus-5",
            SentText = sentText,
            InputTokens = 1,
            OutputTokens = 1
        };

        var json = JsonSerializer.Serialize(response, LoomJsonSerializerContext.Default.ExplainResponse);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(sentText, doc.RootElement.GetProperty("sentText").GetString());
    }

    [Fact]
    public void Deserialize_ExplainRequest_AllFiveFieldsPopulated()
    {
        var json = "{\"template\":\"processing {UserId}\",\"argumentsJson\":\"{\\\"UserId\\\":\\\"41\\\"}\"," +
                    "\"category\":\"MyApp.Checkout\",\"level\":\"Error\",\"exceptionType\":\"System.InvalidOperationException\"}";

        var request = JsonSerializer.Deserialize(json, LoomJsonSerializerContext.Default.ExplainRequest);

        Assert.NotNull(request);
        Assert.Equal("processing {UserId}", request.Template);
        Assert.Equal("{\"UserId\":\"41\"}", request.ArgumentsJson);
        Assert.Equal("MyApp.Checkout", request.Category);
        Assert.Equal("Error", request.Level);
        Assert.Equal("System.InvalidOperationException", request.ExceptionType);
    }

    [Fact]
    public void Deserialize_ExplainRequest_OnlyTemplate_OptionalFieldsNullDoesNotThrow()
    {
        var json = "{\"template\":\"plain message\"}";

        ExplainRequest? request = null;
        var ex = Record.Exception(() =>
            request = JsonSerializer.Deserialize(json, LoomJsonSerializerContext.Default.ExplainRequest));

        Assert.Null(ex);
        Assert.NotNull(request);
        Assert.Equal("plain message", request.Template);
        Assert.Null(request.ArgumentsJson);
        Assert.Null(request.Category);
        Assert.Null(request.Level);
        Assert.Null(request.ExceptionType);
    }

    [Fact]
    public void Serialize_ExplainRequest_OptionalFieldsNull_OmitsAllFourKeys()
    {
        var request = new ExplainRequest { Template = "plain message" };

        var json = JsonSerializer.Serialize(request, LoomJsonSerializerContext.Default.ExplainRequest);

        Assert.DoesNotContain("\"argumentsJson\"", json);
        Assert.DoesNotContain("\"category\"", json);
        Assert.DoesNotContain("\"level\"", json);
        Assert.DoesNotContain("\"exceptionType\"", json);
    }
}
