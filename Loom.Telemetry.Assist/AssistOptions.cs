using System.Globalization;

namespace Loom.Telemetry.Assist;

/// <summary>Configuration for the explain feature, read from the environment. The
/// feature is absent, not broken, when no key is set: callers check IsConfigured and
/// simply do not expose the capability.
///
/// ApiKey must never be logged, never appear in an exception message, and never be
/// returned from any method.</summary>
public sealed record AssistOptions
{
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public required string BaseUrl { get; init; }
    public required TimeSpan Timeout { get; init; }

    public const string DefaultModel = "claude-opus-5";
    public const string DefaultBaseUrl = "https://api.anthropic.com";

    private const int DefaultTimeoutSeconds = 30;
    private const int MinTimeoutSeconds = 5;
    private const int MaxTimeoutSeconds = 120;

    public static AssistOptions? FromEnvironment()
    {
        var apiKey = Environment.GetEnvironmentVariable("LOOM_LLM_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var model = Environment.GetEnvironmentVariable("LOOM_LLM_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = DefaultModel;

        var baseUrl = Environment.GetEnvironmentVariable("LOOM_LLM_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DefaultBaseUrl;
        baseUrl = baseUrl.TrimEnd('/');

        var timeoutSeconds = DefaultTimeoutSeconds;
        var timeoutRaw = Environment.GetEnvironmentVariable("LOOM_LLM_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(timeoutRaw) &&
            int.TryParse(timeoutRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            timeoutSeconds = Math.Clamp(parsed, MinTimeoutSeconds, MaxTimeoutSeconds);
        }

        return new AssistOptions
        {
            ApiKey = apiKey,
            Model = model,
            BaseUrl = baseUrl,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
    }
}
