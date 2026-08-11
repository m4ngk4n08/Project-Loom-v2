namespace Loom.Web.Contracts.Dtos;


/// <summary>
/// Garbage collection statistics.
/// GC is like a janitor that cleans up unused memory automatically.
/// </summary>
public sealed record GarbageCollectionStats
{
    /// <summary>
    /// Gen 0 collections (frequent, quick cleanups)
    /// </summary>
    public required int Gen0Collections { get; init; }

    /// <summary>
    /// Gen 1 collections (medium-lived objects)
    /// </summary>
    public required int Gen1Collections { get; init; }

    /// <summary>
    /// Gen 2 collections (long-lived objects, expensive)
    /// </summary>
    public required int Gen2Collections { get; init; }

    /// <summary>
    /// Total time spent in garbage collection (milliseconds)
    /// </summary>
    public required double TotalGcTimeMs { get; init; }
}

