namespace Loom.Telemetry.Assist;

public sealed record ExplainResult(string Explanation, string ModelUsed, string SentText, int InputTokens, int OutputTokens);

public interface IExplainClient
{
    Task<ExplainResult> ExplainAsync(ExplainPayload payload, CancellationToken ct);
}
