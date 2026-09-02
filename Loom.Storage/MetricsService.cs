using Loom.Web.Contracts.Dtos;
using Loom.Telemetry;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Loom.Storage;

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
        // Calculate real CPU usage (cumulative average since process start, not
        // instantaneous). Environment.TickCount is milliseconds since system boot,
        // not process start - using it here made a freshly started process on a
        // long-uptime machine report ~0% forever, and it wraps negative at ~24.9
        // days of uptime as an int.
        _currentProcess.Refresh();
        var elapsedMs = (DateTime.UtcNow - _currentProcess.StartTime.ToUniversalTime()).TotalMilliseconds;
        var cpuPercent = elapsedMs > 0
            ? _currentProcess.TotalProcessorTime.TotalMilliseconds /
              (Environment.ProcessorCount * elapsedMs) * 100.0
            : 0;

        // Read hotpaths from ring buffer (instrumented method metrics only).
        // Bounded top-3 insertion scan (O(n), 3 slots) instead of a full sort - a
        // full sort to pick 3 items is the wrong algorithm regardless of allocation.
        // totalMs still accumulates over every instrumented path (not just the top
        // 3), since each path's reported share is of the FULL observed set.
        var buffers = _store.GetBuffers();

        Span<string> topNames = new string[3];
        Span<double> topAvgMs = stackalloc double[3];
        Span<long> topInvocations = stackalloc long[3];
        var topCount = 0;
        double totalMs = 0;

        var scratch = ArrayPool<MetricRecord>.Shared.Rent(10);
        try
        {
            var scratchSpan = scratch.AsSpan(0, 10);
            foreach (var kvp in buffers)
            {
                if (!IsInstrumentedMethod(kvp.Key)) continue;

                var written = kvp.Value.TryReadRecent(scratchSpan);
                if (written == 0) continue;

                double sum = 0;
                for (var i = 0; i < written; i++) sum += scratchSpan[i].Value;
                var avg = sum / written;

                totalMs += avg;
                InsertTop3(topNames, topAvgMs, topInvocations, ref topCount, kvp.Key, avg, written);
            }
        }
        finally
        {
            ArrayPool<MetricRecord>.Shared.Return(scratch);
        }

        var hotpaths = new CpuHotpath[topCount];
        for (var i = 0; i < topCount; i++)
        {
            hotpaths[i] = new CpuHotpath
            {
                MethodName = topNames[i],
                CpuPercent = totalMs > 0 ? topAvgMs[i] / totalMs * 100 : 0,
                InvocationCount = topInvocations[i],
                AverageTimeMs = topAvgMs[i]
            };
        }

        var response = new CpuMetricResponse
        {
            CpuUsagePercent = Math.Max(0, Math.Min(100, cpuPercent)),
            Hotpaths = hotpaths,
            Timestamp = DateTime.UtcNow
        };

        return ValueTask.FromResult(response);
    }

    /// <summary>
    /// Inserts (name, avg, invocations) into the top-3 slots (descending by avg) if
    /// it qualifies, shifting lower entries down and dropping the smallest on overflow.
    /// </summary>
    private static void InsertTop3(
        Span<string> names, Span<double> avgMs, Span<long> invocations, ref int count,
        string name, double avg, long invocationCount)
    {
        var insertAt = count;
        for (var i = 0; i < count; i++)
        {
            if (avg > avgMs[i]) { insertAt = i; break; }
        }
        if (insertAt >= 3) return; // doesn't make the top 3

        var shiftEnd = Math.Min(count, 2);
        for (var i = shiftEnd; i > insertAt; i--)
        {
            names[i] = names[i - 1];
            avgMs[i] = avgMs[i - 1];
            invocations[i] = invocations[i - 1];
        }

        names[insertAt] = name;
        avgMs[insertAt] = avg;
        invocations[insertAt] = invocationCount;
        if (count < 3) count++;
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
