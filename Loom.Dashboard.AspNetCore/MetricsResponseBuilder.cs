using System.Diagnostics;
using System.Runtime.CompilerServices;
using Loom.Storage;
using Loom.Web.Contracts.Dtos;

namespace Loom.Dashboard;

/// <summary>
/// Builds the CPU/memory/thread response DTOs from the local IMetricStore.
/// Tracks peak working-set across calls since System.Runtime exposes no
/// total-available-memory counter to normalize against.
/// </summary>
public sealed class MetricsResponseBuilder
{
    private double _peakWorkingSetMb;

    public CpuMetricResponse BuildCpuResponse(IMetricStore store)
    {
        // Target process CPU usage from System.Runtime EventCounters (0-1 fraction).
        // Average the last few 1s samples: a single sample can read 0 between work bursts.
        var cpuUsage = AverageRecent(store, "cpu-usage", 5);
        var cpuPercent = cpuUsage is { } cpu ? cpu * 100 : 0;

        // CPU hotpaths: instrumented method execution metrics (latency/duration/elapsed
        // histograms recording ms). Each path's share of total observed time is its
        // percentage - the sample app has no true per-method CPU sampler, so shares of
        // instrumented execution time is the honest, meaningful metric available.
        var buffers = store.GetBuffers();
        var hotpaths = new List<CpuHotpath>();
        foreach (var kvp in buffers)
        {
            if (!IsInstrumentedMethod(kvp.Key)) continue;
            var recent = kvp.Value.ReadRecent(10);
            if (recent.Length == 0) continue;
            hotpaths.Add(new CpuHotpath
            {
                MethodName = kvp.Key,
                CpuPercent = 0, // normalized below
                InvocationCount = recent.Length,
                AverageTimeMs = recent.Average(r => r.Value)
            });
        }

        // Normalize each path's average time into a share of the total observed time.
        var totalMs = hotpaths.Sum(h => h.AverageTimeMs);
        hotpaths = hotpaths
            .Select(h => h with { CpuPercent = totalMs > 0 ? h.AverageTimeMs / totalMs * 100 : 0 })
            .OrderByDescending(h => h.AverageTimeMs)
            .Take(3)
            .ToList();

        return new CpuMetricResponse
        {
            CpuUsagePercent = Math.Max(0, Math.Min(100, cpuPercent)),
            Hotpaths = hotpaths.ToArray(),
            Timestamp = DateTime.UtcNow
        };
    }

    public MemoryMetricResponse BuildMemoryResponse(IMetricStore store)
    {
        // Target process memory from System.Runtime EventCounters (working-set in MB).
        var workingSetMb = LatestValue(store, "working-set") ?? Process.GetCurrentProcess().WorkingSet64 / 1_048_576.0;
        var gcHeapMb = LatestValue(store, "gc-heap-size");

        // Track peak so the frontend's "used / total" bar stays meaningful without
        // a target memory budget (System.Runtime exposes no total-available counter).
        _peakWorkingSetMb = Math.Max(_peakWorkingSetMb, workingSetMb);

        // GC collection counters from the target.
        var gen0 = LatestValue(store, "gen-0-collection-count") ?? 0;
        var gen1 = LatestValue(store, "gen-1-collection-count") ?? 0;
        var gen2 = LatestValue(store, "gen-2-collection-count") ?? 0;

        return new MemoryMetricResponse
        {
            TotalMemoryMb = _peakWorkingSetMb,
            UsedMemoryMb = workingSetMb,
            GcStats = new GarbageCollectionStats
            {
                Gen0Collections = (int)gen0,
                Gen1Collections = (int)gen1,
                Gen2Collections = (int)gen2,
                TotalGcTimeMs = LatestValue(store, "time-in-gc") ?? 0
            },
            TopAllocations = Array.Empty<MemoryAllocation>(),
            Timestamp = DateTime.UtcNow
        };
    }

    public static ThreadMetricResponse BuildThreadResponse(IMetricStore store)
    {
        // Target process thread pool from System.Runtime EventCounters.
        var workerThreads = LatestValue(store, "threadpool-thread-count") ?? 0;
        var ioThreads = LatestValue(store, "threadpool-io-thread-count") ?? 0;
        var queueLength = LatestValue(store, "threadpool-queue-length") ?? 0;

        var totalThreads = (int)(workerThreads + ioThreads);
        var blockedCount = Math.Min((int)queueLength, totalThreads);

        return new ThreadMetricResponse
        {
            TotalThreads = totalThreads,
            ActiveThreads = totalThreads - blockedCount,
            BlockedThreads = blockedCount,
            Blockages = Array.Empty<ThreadBlockage>(),
            Timestamp = DateTime.UtcNow
        };
    }

    public async IAsyncEnumerable<MetricUpdate> GetMetricsStreamAsync(IMetricStore store, [EnumeratorCancellation] CancellationToken ct)
    {
        var metricIndex = 0;

        while (!ct.IsCancellationRequested)
        {
            switch (metricIndex % 3)
            {
                case 0:
                    yield return new CpuMetricUpdate { Timestamp = DateTime.UtcNow, Data = BuildCpuResponse(store) };
                    break;
                case 1:
                    yield return new MemoryMetricUpdate { Timestamp = DateTime.UtcNow, Data = BuildMemoryResponse(store) };
                    break;
                case 2:
                    yield return new ThreadMetricUpdate { Timestamp = DateTime.UtcNow, Data = BuildThreadResponse(store) };
                    break;
            }

            metricIndex++;
            await Task.Delay(300, ct);
        }
    }

    private static bool IsInstrumentedMethod(string name)
    {
        // Instrumented method metrics carry duration semantics in their name. The
        // System.Runtime gauges (cpu-usage, gc-*, gen-*, threadpool-*) are excluded.
        return name.Contains("latency", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("elapsed", StringComparison.OrdinalIgnoreCase) ||
               name.Contains(".time", StringComparison.OrdinalIgnoreCase);
    }

    private static double? LatestValue(IMetricStore store, string name)
    {
        var records = store.ReadRecent(name, 1);
        return records.Length > 0 ? records[0].Value : null;
    }

    private static double? AverageRecent(IMetricStore store, string name, int count)
    {
        var records = store.ReadRecent(name, count);
        if (records.Length == 0) return null;
        return records.Average(r => r.Value);
    }
}
