using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Loom.Telemetry;

/// <summary>
/// Registry and scheduler for custom metric collectors.
/// </summary>
public static class LoomCollectors
{
    private static readonly Dictionary<string, CollectorRegistration> Collectors = new();
    private static readonly object Lock = new object();
    private static Timer? _schedulerTimer;
    private static TimeSpan _collectionInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Register a collector. It will be polled automatically at the configured interval.
    /// </summary>
    public static void Register(ILoomCollector collector)
    {
        if (collector == null)
            throw new ArgumentNullException(nameof(collector));

        lock (Lock)
        {
            if (Collectors.ContainsKey(collector.Name))
                throw new InvalidOperationException($"Collector '{collector.Name}' is already registered.");

            Collectors[collector.Name] = new CollectorRegistration(collector);

            // Start scheduler on first registration
            if (_schedulerTimer == null)
            {
                _schedulerTimer = new Timer(
                    SchedulerCallback,
                    null,
                    TimeSpan.Zero,  // Start immediately
                    _collectionInterval);
            }
        }
    }

    /// <summary>
    /// Unregister a collector by name.
    /// </summary>
    public static bool Unregister(string collectorName)
    {
        lock (Lock)
        {
            return Collectors.Remove(collectorName);
        }
    }

    /// <summary>
    /// Get all registered collectors.
    /// </summary>
    public static IReadOnlyList<CollectorRegistration> GetRegistrations()
    {
        lock (Lock)
        {
            return Collectors.Values.ToList();
        }
    }

    /// <summary>
    /// Get a specific collector registration by name.
    /// </summary>
    public static CollectorRegistration? GetRegistration(string collectorName)
    {
        lock (Lock)
        {
            return Collectors.TryGetValue(collectorName, out var reg) ? reg : null;
        }
    }

    /// <summary>
    /// Manually trigger collection for a specific collector.
    /// </summary>
    public static async Task<CollectorSnapshot> CollectAsync(string collectorName, CancellationToken cancellationToken = default)
    {
        CollectorRegistration? registration;

        lock (Lock)
        {
            if (!Collectors.TryGetValue(collectorName, out registration))
                throw new InvalidOperationException($"Collector '{collectorName}' is not registered.");
        }

        return await CollectFromRegistrationAsync(registration, cancellationToken);
    }

    /// <summary>
    /// Set the collection interval for all collectors.
    /// </summary>
    public static void SetCollectionInterval(TimeSpan interval)
    {
        if (interval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1 second.");

        lock (Lock)
        {
            _collectionInterval = interval;

            // Restart timer with new interval
            if (_schedulerTimer != null)
            {
                _schedulerTimer.Change(TimeSpan.Zero, _collectionInterval);
            }
        }
    }

    /// <summary>
    /// Enable or disable a specific collector.
    /// </summary>
    public static void SetEnabled(string collectorName, bool enabled)
    {
        lock (Lock)
        {
            if (Collectors.TryGetValue(collectorName, out var registration))
            {
                registration.IsEnabled = enabled;
            }
        }
    }

    /// <summary>
    /// Stop all collectors and shutdown the scheduler.
    /// </summary>
    public static void Shutdown()
    {
        lock (Lock)
        {
            _schedulerTimer?.Dispose();
            _schedulerTimer = null;
            Collectors.Clear();
        }
    }

    private static void SchedulerCallback(object? state)
    {
        List<CollectorRegistration> activeCollectors;

        lock (Lock)
        {
            activeCollectors = Collectors.Values.Where(r => r.IsEnabled).ToList();
        }

        // Collect from all active collectors in parallel
        var tasks = activeCollectors.Select(reg =>
            Task.Run(async () => await CollectFromRegistrationAsync(reg, CancellationToken.None)));

        // Fire and forget - don't block the timer callback
        Task.WhenAll(tasks).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                // Log error (in a real implementation, use proper logging)
                Console.Error.WriteLine($"Collector scheduler error: {t.Exception?.GetBaseException().Message}");
            }
        });
    }

    private static async Task<CollectorSnapshot> CollectFromRegistrationAsync(
        CollectorRegistration registration,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await registration.Collector.CollectAsync(cancellationToken);

            // Update registration metadata
            lock (Lock)
            {
                registration.LastCollectionUtc = DateTime.UtcNow;
                registration.SuccessCount++;
                registration.LastError = snapshot.ErrorMessage;
            }

            // Write metrics to Phase 6 storage
            if (snapshot.IsSuccess)
            {
                foreach (var metric in snapshot.Metrics)
                {
                    // Write directly to MetricBuffer via internal access
                    // (Metrics already have correct names, types, values from collector)
                    WriteMetricToBuffer(metric);
                }
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            // Update registration metadata
            lock (Lock)
            {
                registration.LastError = ex.Message;
                registration.FailureCount++;
            }

            return CollectorSnapshot.Failed(registration.Collector.Name, ex.Message);
        }
    }

    private static void WriteMetricToBuffer(MetricRecord metric)
    {
        // Write to appropriate metric type based on the record
        switch (metric.Type)
        {
            case MetricType.Counter:
                LoomMetrics.RecordCounter(metric.Name, metric.Value, metric.Tags ?? Array.Empty<MetricTag>());
                break;
            case MetricType.Gauge:
                LoomMetrics.RecordGauge(metric.Name, metric.Value, metric.Tags ?? Array.Empty<MetricTag>());
                break;
            case MetricType.Histogram:
                LoomMetrics.RecordHistogram(metric.Name, metric.Value, metric.Tags ?? Array.Empty<MetricTag>());
                break;
        }
    }
}
