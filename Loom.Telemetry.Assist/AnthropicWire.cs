using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loom.Telemetry.Assist;

internal sealed record AnthropicRequest
{
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("max_tokens")] public required int MaxTokens { get; init; }
    [JsonPropertyName("system")] public required string System { get; init; }
    [JsonPropertyName("messages")] public required List<AnthropicMessage> Messages { get; init; }
}

internal sealed record AnthropicMessage
{
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("content")] public required string Content { get; init; }
}

internal sealed record AnthropicResponse
{
    [JsonPropertyName("content")] public List<AnthropicContentBlock>? Content { get; init; }
    [JsonPropertyName("stop_reason")] public string? StopReason { get; init; }
    [JsonPropertyName("usage")] public AnthropicUsage? Usage { get; init; }
}

internal sealed record AnthropicContentBlock
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("text")] public string? Text { get; init; }
}

internal sealed record AnthropicUsage
{
    [JsonPropertyName("input_tokens")] public int InputTokens { get; init; }
    [JsonPropertyName("output_tokens")] public int OutputTokens { get; init; }
}

internal sealed record AnthropicErrorResponse
{
    [JsonPropertyName("error")] public AnthropicErrorDetail? Error { get; init; }
}

internal sealed record AnthropicErrorDetail
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}

[JsonSerializable(typeof(AnthropicRequest))]
[JsonSerializable(typeof(AnthropicMessage))]
[JsonSerializable(typeof(AnthropicResponse))]
[JsonSerializable(typeof(AnthropicContentBlock))]
[JsonSerializable(typeof(AnthropicUsage))]
[JsonSerializable(typeof(AnthropicErrorResponse))]
[JsonSerializable(typeof(AnthropicErrorDetail))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization,
    WriteIndented = false
)]
internal partial class AssistJsonSerializerContext : JsonSerializerContext
{
}
