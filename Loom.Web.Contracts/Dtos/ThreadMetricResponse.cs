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