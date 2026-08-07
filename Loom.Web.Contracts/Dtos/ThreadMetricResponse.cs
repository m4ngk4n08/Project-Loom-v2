namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Thread activity and blockage metrics.
/// Threads are like workers in a factory - we want to know if any are blocked/waiting.
/// </summary>
public sealed record ThreadMetricResponse
{
    /// <summary>
    /// Total number of threads in the process
    /// </summary>
    public required int TotalThreads { get; init; }

    /// <summary>
    /// Number of threads currently running
    /// </summary>
    public required int ActiveThreads { get; init; }

    /// <summary>
    /// Number of threads blocked/waiting
    /// </summary>
    public required int BlockedThreads { get; init; }

    /// <summary>
    /// Details about blocked threads
    /// </summary>
    public required ThreadBlockage[] Blockages { get; init; }

    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Information about a blocked thread.
/// </summary>
public sealed record ThreadBlockage
{
    /// <summary>
    /// Thread ID
    /// </summary>
    public required int ThreadId { get; init; }

    /// <summary>
    /// Thread name (if available)
    /// </summary>
    public string? ThreadName { get; init; }  // Note: nullable

    /// <summary>
    /// What the thread is blocked on
    /// Example: "Waiting for database", "Lock contention"
    /// </summary>
    public required string BlockedOn { get; init; }

    /// <summary>
    /// How long the thread has been blocked (milliseconds)
    /// </summary>
    public required double BlockedDurationMs { get; init; }

    /// <summary>
    /// Stack trace showing where the thread is blocked
    /// </summary>
    public string? StackTrace { get; init; }  // Note: nullable
}