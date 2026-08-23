using System.Threading;
using System.Threading.Tasks;

namespace Loom.Telemetry.Interfaces;

/// <summary>
/// Plugin interface for custom metric collectors.
/// Implement this to collect metrics from external systems (Redis, RabbitMQ, databases, etc.).
/// </summary>
public interface ILoomCollector
{
    /// <summary>
    /// Unique name for this collector (e.g., "Redis", "RabbitMQ", "PostgreSQL").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Collect metrics from the external system.
    /// Called periodically by the Loom scheduler (default: every 10 seconds).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown</param>
    /// <returns>Snapshot containing collected metrics</returns>
    Task<CollectorSnapshot> CollectAsync(CancellationToken cancellationToken);
}
