namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// CPU hotpath metrics - which code is using the most CPU time.
/// Like a report showing which apps drain your phone battery the most.
/// </summary>
public sealed record CpuMetricResponse
{
    /// <summary>
    /// Overall CPU usage percentage (0-100)
    /// </summary>
    public required double CpuUsagePercent { get; init; }

    /// <summary>
    /// List of top CPU-consuming threads/methods
    /// </summary>
    public required CpuHotpath[] Hotpaths { get; init; }

    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// A single CPU hotpath - a method or code path consuming CPU.
/// </summary>
public sealed record CpuHotpath
{
    /// <summary>
    /// Name of the method or code path
    /// Example: "OrderProcessor.CalculateTotal"
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
    /// Average time spent in this method (milliseconds)
    /// </summary>
    public required double AverageTimeMs { get; init; }
}