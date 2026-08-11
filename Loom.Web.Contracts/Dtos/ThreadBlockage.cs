namespace Loom.Web.Contracts.Dtos;


public sealed record ThreadBlockage
{
    /// <summary>
    /// Thread ID
    /// </summary>
    public required int ThreadId { get; init; }

    /// <summary>
    /// Thread name (if available)
    /// </summary>
    public string? ThreadName { get; init; } // Note: nullable

    /// <summary>
    /// What the thread is blocked on
    /// Eg., "Waiting for database", "lock contention"
    /// </summary>
    public required string BlockedOn { get; init; }

    /// <summary>
    /// How long the thread has been blocked (in milliseconds)
    /// </summary>
    public required double BlockedDurationMs { get; init; }

    /// <summary>
    /// Stack trace showing where the thread is blocked
    /// </summary>
    public string? StackTrace { get; init; }

}
