using Loom.Web.Contracts.Dtos;

namespace Loom.Storage;

/// <summary>
/// For Retrieving diagnostic metrics.
/// <para>
/// <b>Measures the current process only.</b> Every member reads this process's own
/// counters - it has no notion of a target PID. Do not inject it into a host that
/// observes another process; register it explicitly via
/// <c>AddLoomSelfMetrics()</c>, never as part of <c>AddLoomStorage()</c>.
/// </para>
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
