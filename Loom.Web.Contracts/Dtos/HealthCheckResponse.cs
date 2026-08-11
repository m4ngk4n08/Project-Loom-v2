namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Response for health check endpoint.
/// Think of this like a doctor's checkup - is the service alive and healthy?
/// </summary>
public sealed record HealthCheckResponse
{
    /// <summary>
    /// Overall health status: "Healthy", "Degraded", or "Unhealthy"
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// When this health check was performed (UTC timestamp)
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// How long the service has been running (in seconds)
    /// </summary>
    public required long UptimeSeconds { get; init; }

    /// <summary>
    /// Current memory usage in megabytes
    /// </summary>
    public required double MemoryUsageMb { get; init; }
}