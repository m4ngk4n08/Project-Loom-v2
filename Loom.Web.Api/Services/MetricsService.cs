using Loom.Web.Api.Interfaces;
using Loom.Web.Contracts.Dtos;
using Loom.Storage;
using Loom.Telemetry;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Loom.Web.Api.Services
{
    /// <summary>
    /// Real metrics collection from Loom.Storage.
    /// </summary>
    public sealed class MetricsService : IMetricsService
    {
        private readonly IMetricStore _store;
        private readonly Process _currentProcess = Process.GetCurrentProcess();

        public MetricsService(IMetricStore store)
        {
            _store = store;
        }

        /// <summary>
        /// Get CPU metrics from ring buffers.
        /// </summary>
        public ValueTask<CpuMetricResponse> GetCpuMetricsAsync(CancellationToken ct = default)
        {
            // Calculate real CPU usage
            _currentProcess.Refresh();
            var cpuPercent = _currentProcess.TotalProcessorTime.TotalMilliseconds /
                           (Environment.ProcessorCount * Environment.TickCount) * 100.0;

            // Read hotpaths from ring buffer (instrumented method metrics only)
            var hotpaths = new List<CpuHotpath>();
            var buffers = _store.GetBuffers();

            foreach (var kvp in buffers)
            {
                if (!IsInstrumentedMethod(kvp.Key)) continue;
                var recent = kvp.Value.ReadRecent(10);
                if (recent.Length > 0)
                {
                    var avg = recent.Average(r => r.Value);
                    hotpaths.Add(new CpuHotpath
                    {
                        MethodName = kvp.Key,
                        CpuPercent = 0, // normalized below
                        InvocationCount = recent.Length,
                        AverageTimeMs = avg
                    });
                }
            }

            // Each path's share of total observed instrumented time.
            var totalMs = hotpaths.Sum(h => h.AverageTimeMs);
            hotpaths = hotpaths
                .Select(h => h with { CpuPercent = totalMs > 0 ? h.AverageTimeMs / totalMs * 100 : 0 })
                .OrderByDescending(h => h.AverageTimeMs)
                .Take(3)
                .ToList();

            var response = new CpuMetricResponse
            {
                CpuUsagePercent = Math.Max(0, Math.Min(100, cpuPercent)),
                Hotpaths = hotpaths.ToArray(),
                Timestamp = DateTime.UtcNow
            };

            return ValueTask.FromResult(response);
        }

        private static bool IsInstrumentedMethod(string name)
        {
            // Instrumented method metrics carry duration semantics in their name.
            // System.Runtime gauges (cpu-usage, gc-*, gen-*, threadpool-*) are excluded.
            return name.Contains("latency", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("elapsed", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(".time", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get memory metrics from GC and ring buffers.
        /// </summary>
        public ValueTask<MemoryMetricResponse> GetMemoryMetricsAsync(CancellationToken ct = default)
        {
            _currentProcess.Refresh();
            var gcInfo = GC.GetGCMemoryInfo();

            // Read memory allocations from ring buffer
            var allocations = new List<MemoryAllocation>();
            var buffers = _store.GetBuffers();

            foreach (var kvp in buffers)
            {
                if (kvp.Key.StartsWith("memory.") && kvp.Value.Capacity > 0)
                {
                    var recent = kvp.Value.ReadRecent(5);
                    if (recent.Length > 0)
                    {
                        var totalBytes = (long)recent.Sum(r => r.Value);
                        allocations.Add(new MemoryAllocation
                        {
                            TypeName = kvp.Key.Replace("memory.", ""),
                            Count = recent.Length,
                            TotalBytes = totalBytes
                        });
                    }
                }
            }

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
                TopAllocations = allocations.Take(10).ToArray(),
                Timestamp = DateTime.UtcNow
            };

            return ValueTask.FromResult(response);
        }

        /// <summary>
        /// Get thread metrics from Process and ring buffers.
        /// </summary>
        public ValueTask<ThreadMetricResponse> GetThreadMetricsAsync(CancellationToken ct = default)
        {
            _currentProcess.Refresh();
            var threadCount = _currentProcess.Threads.Count;

            // Read thread blockages from ring buffer
            var blockages = new List<ThreadBlockage>();
            var buffers = _store.GetBuffers();

            foreach (var kvp in buffers)
            {
                if (kvp.Key.StartsWith("thread.blocked") && kvp.Value.Capacity > 0)
                {
                    var recent = kvp.Value.ReadRecent(5);
                    foreach (var record in recent)
                    {
                        blockages.Add(new ThreadBlockage
                        {
                            ThreadId = (int)record.Value,
                            ThreadName = kvp.Key.Replace("thread.blocked.", ""),
                            BlockedOn = "Lock contention",
                            BlockedDurationMs = record.Value,
                            StackTrace = null
                        });
                    }
                }
            }

            var blockedCount = blockages.Count;

            var response = new ThreadMetricResponse
            {
                TotalThreads = threadCount,
                ActiveThreads = threadCount - blockedCount,
                BlockedThreads = blockedCount,
                Blockages = blockages.Take(10).ToArray(),
                Timestamp = DateTime.UtcNow
            };

            return ValueTask.FromResult(response);
        }

        /// <summary>
        /// Stream metric updates continuously at ~3 Hz per metric type.
        /// </summary>
        public async IAsyncEnumerable<MetricUpdate> GetMetricsStreamAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var metricIndex = 0;

            while (!ct.IsCancellationRequested)
            {
                // Cycle through metric types
                switch (metricIndex % 3)
                {
                    case 0:
                        var cpuMetrics = await GetCpuMetricsAsync(ct);
                        yield return new CpuMetricUpdate
                        {
                            Timestamp = DateTime.UtcNow,
                            Data = cpuMetrics
                        };
                        break;

                    case 1:
                        var memoryMetrics = await GetMemoryMetricsAsync(ct);
                        yield return new MemoryMetricUpdate
                        {
                            Timestamp = DateTime.UtcNow,
                            Data = memoryMetrics
                        };
                        break;

                    case 2:
                        var threadMetrics = await GetThreadMetricsAsync(ct);
                        yield return new ThreadMetricUpdate
                        {
                            Timestamp = DateTime.UtcNow,
                            Data = threadMetrics
                        };
                        break;
                }

                metricIndex++;
                await Task.Delay(300, ct);
            }
        }
    }
}
