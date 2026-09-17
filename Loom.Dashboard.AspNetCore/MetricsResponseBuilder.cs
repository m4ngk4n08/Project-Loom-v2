using System.Runtime.CompilerServices;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Web.Contracts.Dtos;

namespace Loom.Dashboard;

/// <summary>
/// Builds the CPU/memory/thread response DTOs from the local IMetricStore.
/// Tracks peak working-set across calls since System.Runtime exposes no
/// total-available-memory counter to normalize against.
/// </summary>
public sealed class MetricsResponseBuilder
{
    private readonly Lock _peakLock = new();
    private double _peakWorkingSetMb;

    public CpuMetricResponse BuildCpuResponse(IMetricStore store)
    {
        // Target process CPU usage from System.Runtime EventCounters (0-100 percent,
        // NOT a 0-1 fraction: measured via dotnet-counters against a single pinned
        // thread on a 12-core box, which read ~8.5, matching 100/12 - see
        // PROMPT-dashboard-review-fixes.md Step 0).
        // Average the last few 1s samples: a single sample can read 0 between work bursts.
        var cpuUsage = AverageRecent(store, "cpu-usage", 5);
        var cpuPercent = cpuUsage ?? 0;

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
        // No fallback to the DASHBOARD's own process memory: that value belongs to a
        // different process and would silently poison both the current reading and the
        // tracked peak below. With no sample yet, used memory reads 0 and the peak is
        // left untouched.
        var workingSetMb = LatestValue(store, "working-set") ?? 0;
        var gcHeapMb = LatestValue(store, "gc-heap-size");

        // Track peak so the frontend's "used / total" bar stays meaningful without
        // a target memory budget (System.Runtime exposes no total-available counter).
        // The builder is a singleton hit concurrently by HTTP requests and the WebSocket
        // stream, so the read-modify-write on _peakWorkingSetMb must be locked.
        double peakWorkingSetMb;
        lock (_peakLock)
        {
            if (workingSetMb > 0)
            {
                _peakWorkingSetMb = Math.Max(_peakWorkingSetMb, workingSetMb);
            }
            peakWorkingSetMb = _peakWorkingSetMb;
        }

        // GC collection counters since the bridge attached to the target. gen-N-gc-count
        // is a Counter (per-interval delta accumulated into a running total by the
        // store) - "gen-N-collection-count" is never published by the runtime.
        // gen-N-gc-count and total-pause-time-by-gc are Counters: InMemoryMetricStore
        // accumulates their per-interval deltas into a running total in
        // GetCounterTotals(), which survives ring-buffer wrap - if the store has no
        // total for a name yet (IMetricStore.cs allows an empty collection), report 0
        // rather than guess. Scanned once here rather than once per field.
        double gen0 = 0, gen1 = 0, gen2 = 0, gcPauseMs = 0;
        foreach (var total in store.GetCounterTotals())
        {
            if (total.Tags.Length != 0) continue;

            switch (total.MetricName)
            {
                case "gen-0-gc-count":
                    gen0 = total.Total;
                    break;
                case "gen-1-gc-count":
                    gen1 = total.Total;
                    break;
                case "gen-2-gc-count":
                    gen2 = total.Total;
                    break;
                case "total-pause-time-by-gc":
                    gcPauseMs = total.Total;
                    break;
            }
        }

        return new MemoryMetricResponse
        {
            TotalMemoryMb = peakWorkingSetMb,
            UsedMemoryMb = workingSetMb,
            GcStats = new GarbageCollectionStats
            {
                Gen0Collections = (int)gen0,
                Gen1Collections = (int)gen1,
                Gen2Collections = (int)gen2,
                TotalGcTimeMs = gcPauseMs
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
