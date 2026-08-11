
namespace Loom.Web.Contracts.Dtos;


/// <summary>
/// A single memory allocation entry.
/// </summary>
public sealed record MemoryAllocation
{
    /// <summary>
    /// Type name that's allocating memory
    /// Eg., "System.String", "OrderData[]"
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
