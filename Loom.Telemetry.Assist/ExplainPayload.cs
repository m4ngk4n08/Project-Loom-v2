using System.Text;
using System.Text.Json;

namespace Loom.Telemetry.Assist;

/// <summary>What actually leaves the process. v1 sends the message TEMPLATE and the
/// argument NAMES only — never argument values, never the rendered message. The model
/// sees "Payment processed: ${Amount:F2} via {Method}" and the names Amount and Method,
/// but never $482.00 or PayPal.
///
/// This is why LogRecord stores Template separately from the rendered Message: the split
/// that exists for display doubles as the redaction boundary.</summary>
public sealed record ExplainPayload
{
    public required string Template { get; init; }
    public required IReadOnlyList<string> ArgumentNames { get; init; }
    public string? Category { get; init; }
    public string? Level { get; init; }
    public string? ExceptionType { get; init; }
}

public static class ExplainPayloadBuilder
{
    public static ExplainPayload? Build(
        string? template,
        string? argumentsJson,
        string? category,
        string? level,
        string? exceptionType)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        return new ExplainPayload
        {
            Template = template,
            ArgumentNames = ExtractArgumentNames(argumentsJson),
            Category = category,
            Level = level,
            ExceptionType = exceptionType
        };
    }

    private static IReadOnlyList<string> ExtractArgumentNames(string? argumentsJson)
    {
        if (string.IsNullOrEmpty(argumentsJson))
            return [];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            return [];
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return [];

            var names = new List<string>();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "{OriginalFormat}")
                    continue;
                names.Add(property.Name);
            }
            return names;
        }
    }

    public static string ToPromptText(ExplainPayload payload)
    {
        var sb = new StringBuilder();
        sb.Append("Log message template: ").Append(payload.Template).Append('\n');

        sb.Append("Argument names: ");
        sb.Append(payload.ArgumentNames.Count > 0 ? string.Join(", ", payload.ArgumentNames) : "(none)");
        sb.Append('\n');

        if (payload.Category != null)
            sb.Append("Category: ").Append(payload.Category).Append('\n');
        if (payload.Level != null)
            sb.Append("Level: ").Append(payload.Level).Append('\n');
        if (payload.ExceptionType != null)
            sb.Append("Exception type: ").Append(payload.ExceptionType).Append('\n');

        return sb.ToString();
    }
}
