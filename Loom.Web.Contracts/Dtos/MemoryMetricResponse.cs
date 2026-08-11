namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Memory allocation metrics - what's using RAM and how much.
/// Like checking which files are taking up space on the hard drive.
/// </summary>
public sealed record MemoryMetricResponse
{
    /// <summary>
    /// Total memory allocated (in mb)
    /// </summary>
    public required double TotalMemoryMb { get; init; }

    /// <summary>
    /// Memory currently in use (in mb)
    /// </summary>
    public required double UsedMemoryMb { get; init; }

    /// <summary>
    /// Number of gc that occurred
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

