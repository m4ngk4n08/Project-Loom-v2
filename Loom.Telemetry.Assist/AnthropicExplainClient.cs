using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Loom.Telemetry.Assist;

public sealed class AnthropicExplainClient(HttpClient httpClient, AssistOptions options) : IExplainClient
{
    // Two or three sentences, no speculation about withheld values - argument values
    // never leave the process, so the model must not guess at them.
    private const string SystemPrompt =
        "You are explaining a log event to an engineer. In two or three sentences, " +
        "explain what this type of event typically means and what commonly causes it. " +
        "Argument values have been withheld for privacy, so do not speculate about " +
        "specific values - discuss the event in general terms.";

    public async Task<ExplainResult> ExplainAsync(ExplainPayload payload, CancellationToken ct)
    {
        var sentText = ExplainPayloadBuilder.ToPromptText(payload);

        var request = new AnthropicRequest
        {
            Model = options.Model,
            // A short bounded explanation, not a long generation - do not raise this.
            MaxTokens = 1024,
            System = SystemPrompt,
            Messages = [new AnthropicMessage { Role = "user", Content = sentText }]
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl}/v1/messages");
        httpRequest.Headers.Add("x-api-key", options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(request, AssistJsonSerializerContext.Default.AnthropicRequest),
            Encoding.UTF8,
            "application/json");

        using var timeoutCts = new CancellationTokenSource(options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var httpResponse = await httpClient.SendAsync(httpRequest, linkedCts.Token);
        var body = await httpResponse.Content.ReadAsStringAsync(linkedCts.Token);

        if (!httpResponse.IsSuccessStatusCode)
        {
            string? errorType = null;
            string? errorMessage = null;
            try
            {
                var error = JsonSerializer.Deserialize(body, AssistJsonSerializerContext.Default.AnthropicErrorResponse);
                errorType = error?.Error?.Type;
                errorMessage = error?.Error?.Message;
            }
            catch (JsonException)
            {
                // Body wasn't the expected error shape; fall through with just the status.
            }

            var detail = errorType != null || errorMessage != null
                ? $" ({errorType}: {errorMessage})"
                : string.Empty;
            throw new InvalidOperationException(
                $"Anthropic API request failed with status {(int)httpResponse.StatusCode}{detail}.");
        }

        var response = JsonSerializer.Deserialize(body, AssistJsonSerializerContext.Default.AnthropicResponse)
            ?? throw new InvalidOperationException("Anthropic API returned an empty response.");

        if (response.StopReason == "refusal")
            throw new InvalidOperationException("The model declined to generate an explanation.");

        var textBlock = response.Content?.FirstOrDefault(c => c.Type == "text");
        if (textBlock?.Text == null)
            throw new InvalidOperationException("Anthropic API response contained no text content.");

        return new ExplainResult(
            textBlock.Text,
            options.Model,
            sentText,
            response.Usage?.InputTokens ?? 0,
            response.Usage?.OutputTokens ?? 0);
    }
}
