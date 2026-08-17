namespace Loom.Web.Contracts.Dtos;

public sealed record AlertWebhookPayload
{
    public required string Alert { get; init; }
    public required string Metric { get; init; }
    public required long ObservedCount { get; init; }
    public required double ObservedAverage { get; init; }
    public required DateTime FiredAt { get; init; }
}
