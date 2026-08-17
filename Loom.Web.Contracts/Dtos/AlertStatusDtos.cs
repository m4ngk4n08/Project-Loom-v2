namespace Loom.Web.Contracts.Dtos;

public sealed record AlertConfigDto
{
    public required string Name { get; init; }
    public required string MetricName { get; init; }
    public required TimeSpan Window { get; init; }
}

public sealed record AlertConditionDto { public required string Description { get; init; } }

public sealed record AlertStatusDto
{
    public required string Name { get; init; }
    public required bool CurrentlyBreached { get; init; }
    public DateTime? LastFiredAt { get; init; }
    public DateTime? SilencedUntil { get; init; }
}

public sealed record AlertHistoryEntry
{
    public required string AlertName { get; init; }
    public required DateTime FiredAt { get; init; }
    public required long ObservedCount { get; init; }
}
