using Loom.Web.Contracts.Dtos;

namespace Loom.Web.Api.Interfaces
{
    /// <summary>
    /// For Retrieving diagnostic metrics.
    /// </summary>
    public interface IMetricsService
    {
        /// <summary>
        /// Get current CPU metrics.
        /// ValueTask = optimzed Task for performance-critical code
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<CpuMetricResponse> GetCpuMetricsAsync(CancellationToken ct = default);

        /// <summary>
        /// Get current Memory metrics.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<MemoryMetricResponse> GetMemoryMetricsAsync(CancellationToken ct = default);

        /// <summary>
        /// Get current thread metrics.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<ThreadMetricResponse> GetThreadMetricsAsync(CancellationToken ct = default);

        /// <summary>
        /// Stream metrics updates continuosly.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        IAsyncEnumerable<MetricUpdate> GetMetricsStreamAsync(CancellationToken ct = default);
    }
}
