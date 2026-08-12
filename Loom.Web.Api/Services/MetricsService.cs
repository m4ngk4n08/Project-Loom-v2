using Loom.Web.Api.Interfaces;
using Loom.Web.Contracts.Dtos;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Loom.Web.Api.Services
{
    /// <summary>
    /// Implementation of metrics collection.
    /// </summary>
    public sealed class MetricsService : IMetricsService
    {
        // Cache the current process to avoid repeated lookups
        private readonly Process _currentProcess = Process.GetCurrentProcess();

        // MOCK ONLY: Real implementation MUST use pooled/cached arrays (zero-allocation)
        // These static arrays avoid per-call allocation for mock data
        private static readonly CpuHotpath[] MockHotpaths = new[]
        {
        new CpuHotpath
        {
            MethodName = "OrderProcessor.CalculateTotal",
            CpuPercent = 15.3,
            InvocationCount = 1523,
            AverageTimeMs = 2.4
        },
        new CpuHotpath
        {
            MethodName = "Database.ExecuteQuery",
            CpuPercent = 8.7,
            InvocationCount = 892,
            AverageTimeMs = 5.1
        }
    };

        /// <summary>
        /// Get CPU metrics.
        /// </summary>
        public ValueTask<CpuMetricResponse> GetCpuMetricsAsync(CancellationToken ct = default)
        {
            // For now, return mock data
            // TODO Phase 7: Integrate with Loom.Core for real SIMD-based profiling

            var response = new CpuMetricResponse
            {
                CpuUsagePercent = _currentProcess.TotalProcessorTime.TotalMilliseconds /
                                 (Environment.ProcessorCount * Environment.TickCount) * 100.0,
                Hotpaths = MockHotpaths,  // Use cached array - zero allocation!
                Timestamp = DateTime.UtcNow
            };

            // Return synchronously wrapped in ValueTask
            return ValueTask.FromResult(response);
        }

        /// <summary>
        /// Get memory metrics.
        /// </summary>
        public ValueTask<MemoryMetricResponse> GetMemoryMetricsAsync(CancellationToken ct = default)
        {
            // Refresh process info to get current values
            _currentProcess.Refresh();

            // Get GC information
            var gcInfo = GC.GetGCMemoryInfo();

            var response = new MemoryMetricResponse
            {
                TotalMemoryMb = gcInfo.TotalAvailableMemoryBytes / 1_048_576.0,
                UsedMemoryMb = _currentProcess.WorkingSet64 / 1_048_576.0,
                GcStats = new GarbageCollectionStats
                {
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2),
                    TotalGcTimeMs = GC.GetTotalPauseDuration().TotalMilliseconds
                },
                TopAllocations = new[]
                {
                new MemoryAllocation
                {
                    TypeName = "System.String",
                    Count = 50000,
                    TotalBytes = 2_500_000
                },
                new MemoryAllocation
                {
                    TypeName = "System.Byte[]",
                    Count = 1200,
                    TotalBytes = 1_800_000
                }
            },
                Timestamp = DateTime.UtcNow
            };

            return ValueTask.FromResult(response);
        }

        /// <summary>
        /// Get thread metrics.
        /// </summary>
        public ValueTask<ThreadMetricResponse> GetThreadMetricsAsync(CancellationToken ct = default)
        {
            // Get thread information
            _currentProcess.Refresh();
            var threadCount = _currentProcess.Threads.Count;

            // For now, mock blocked threads
            // TODO Phase 7: Real thread profiling

            var response = new ThreadMetricResponse
            {
                TotalThreads = threadCount,
                ActiveThreads = threadCount - 2,
                BlockedThreads = 2,
                Blockages = new[]
                {
                new ThreadBlockage
                {
                    ThreadId = 12345,
                    ThreadName = "WorkerThread-1",
                    BlockedOn = "Waiting for database connection",
                    BlockedDurationMs = 1250.5,
                    StackTrace = "at Database.WaitForConnection()\nat OrderProcessor.Process()"
                },
                new ThreadBlockage
                {
                    ThreadId = 12346,
                    ThreadName = null,  // Unnamed thread
                    BlockedOn = "Lock contention on OrderLock",
                    BlockedDurationMs = 500.2,
                    StackTrace = null  // Sometimes we don't capture stack trace
                }
            },
                Timestamp = DateTime.UtcNow
            };

            return ValueTask.FromResult(response);
        }

        /// <summary>
        /// Stream metric updates continuosly at ~10 Hz (10 updates per second)
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async IAsyncEnumerable<MetricUpdate> GetMetricsStreamAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            // Stream metrics until client disconnects or cancellation is requested
            while (!ct.IsCancellationRequested)
            {
                // Get current CPU metrics
                var cpuMetrics = await GetCpuMetricsAsync(ct); // Blocking call for simplicity in this example

                // Warp in MetricUpdate envelope
                yield return new CpuMetricUpdate
                {
                    Timestamp = DateTime.UtcNow,
                    Data = cpuMetrics
                };

                // Wait for ~100ms to achieve ~10 Hz update rate
                await Task.Delay(100, ct);
            }
        }
    }
}
