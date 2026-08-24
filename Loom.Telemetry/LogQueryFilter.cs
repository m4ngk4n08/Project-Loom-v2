namespace Loom.Telemetry;

/// <summary>
/// Composable filter for ILogStore.Query - combines what ReadRecent/ReadSince/
/// ReadRecent(category,count) each apply individually.
/// </summary>
public readonly record struct LogQueryFilter(
    long? SinceUtcTicks,
    long? UntilUtcTicks,
    string? Category,
    LoomLogLevel? MinLevel,
    int Limit);
