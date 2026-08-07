namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Memory allocation metrics - what's using RAM and how much.
/// Like checking which files are taking up space on your hard drive.
/// </summary>
public sealed record MemoryMetricResponse
{
    /// <summary>
    /// Total memory allocated (in megabytes)
    /// </summary>
    public required double TotalMemoryMb { get; init; }

    /// <summary>
    /// Memory currently in use (in megabytes)
    /// </summary>
    public required double UsedMemoryMb { get; init; }

    /// <summary>
    /// Number of garbage collections that occurred
    /// </summary>
    public required GarbageCollectionStats GcStats { get; init; }

    /// <summary>
    /// Top memory allocations by type
    /// </summary>
    public required MemoryAllocation[] TopAllocations { get; init; }

    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

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

/// <summary>
/// A single memory allocation entry.
/// </summary>
public sealed record MemoryAllocation
{
    /// <summary>
    /// Type name that's allocating memory
    /// Example: "System.String", "OrderData[]"
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// Number of instances allocated
    /// </summary>
    public required long Count { get; init; }

    /// <summary>
    /// Total memory used by these instances (in bytes)
    /// </summary>
    public required long TotalBytes { get; init; }
}