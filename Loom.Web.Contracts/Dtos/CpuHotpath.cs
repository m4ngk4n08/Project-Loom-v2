namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// A single CPU hotpath - a method or code path consuming CPU.
/// </summary>
public sealed record CpuHotpath
{

    /// <summary>
    /// Name of the method or code path
    /// Eg., "OrderProcessor.CalculateTotal"
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// Percentage of total CPU time this path uses (0-100)
    /// </summary>
    public required double CpuPercent { get; init; }

    /// <summary>
    /// Number of times this method was called
    /// </summary>
    public required long InvocationCount { get; init; }

    /// <summary>
    /// Average time spent in this method(milliseconds)
    /// </summary>
    public required double AverageTimeMs { get; init; }
}
