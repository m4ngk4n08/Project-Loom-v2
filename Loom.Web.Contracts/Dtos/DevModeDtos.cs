namespace Loom.Web.Contracts.Dtos;

public sealed record DevModeStatusDto
{
    public required int DiscoveredProcessCount { get; init; }
    public required int LoomInstrumentedCount { get; init; }
}

public sealed record DiscoveredAppDto
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required bool IsLoomInstrumented { get; init; }
}
