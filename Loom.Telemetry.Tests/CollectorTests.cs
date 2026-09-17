using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Loom.Telemetry.Interfaces;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class CollectorTests : IDisposable
{
    public CollectorTests()
    {
        // Clean up before each test
        LoomCollectors.Shutdown();
    }

    public void Dispose()
    {
        // Clean up after each test
        LoomCollectors.Shutdown();
    }

    [Fact]
    public void Register_AddsCollectorToRegistry()
    {
        // Arrange
        var collector = new SampleCollector("TestCollector");

        // Act
        LoomCollectors.Register(collector);

        // Assert
        var registrations = LoomCollectors.GetRegistrations();
        Assert.Single(registrations);
        Assert.Equal("TestCollector", registrations[0].Collector.Name);
    }

    [Fact]
    public void Register_DuplicateName_ThrowsException()
    {
        // Arrange
        var collector1 = new SampleCollector("TestCollector");
        var collector2 = new SampleCollector("TestCollector");
        LoomCollectors.Register(collector1);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => LoomCollectors.Register(collector2));
    }

    [Fact]
    public async Task CollectAsync_ExecutesCollector()
    {
        // Arrange
        var collector = new SampleCollector("TestCollector");
        LoomCollectors.Register(collector);

        // Act
        var snapshot = await LoomCollectors.CollectAsync("TestCollector");

        // Assert
        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsSuccess);
        Assert.Equal("TestCollector", snapshot.CollectorName);
        Assert.NotEmpty(snapshot.Metrics);
    }

    [Fact]
    public async Task CollectAsync_WritesMetricsToBuffer()
    {
        // Arrange
        var collector = new SampleCollector("TestCollector");
        LoomCollectors.Register(collector);

        // Act
        await LoomCollectors.CollectAsync("TestCollector");

        // Assert - Check metrics were written to Phase 6 storage
        var recent = LoomMetrics.GetRecentMetrics(100);
        var collectorMetrics = recent.Where(m => m.Name.StartsWith("test."));

        Assert.NotEmpty(collectorMetrics);
    }

    [Fact]
    public async Task CollectAsync_UpdatesRegistrationMetadata()
    {
        // Arrange
        var collector = new SampleCollector("TestCollector");
        LoomCollectors.Register(collector);

        // Disable automatic collection to avoid race conditions
        LoomCollectors.SetEnabled("TestCollector", false);
        await Task.Delay(100); // Wait for any in-flight collections

        // Re-enable and collect manually
        LoomCollectors.SetEnabled("TestCollector", true);

        // Act
        await LoomCollectors.CollectAsync("TestCollector");

        // Assert
        var registration = LoomCollectors.GetRegistration("TestCollector");
        Assert.NotNull(registration);
        Assert.NotNull(registration.LastCollectionUtc);
        Assert.True(registration.SuccessCount >= 1); // At least 1 (may be more from scheduler)
        Assert.Equal(0, registration.FailureCount);
    }

    [Fact]
    public async Task CollectAsync_HandlesFailures()
    {
        // Arrange
        var collector = new FailingCollector();
        LoomCollectors.Register(collector);

        // Disable automatic collection to avoid race conditions
        LoomCollectors.SetEnabled("FailingCollector", false);
        await Task.Delay(100); // Wait for any in-flight collections

        // Re-enable and collect manually
        LoomCollectors.SetEnabled("FailingCollector", true);

        // Act
        var snapshot = await LoomCollectors.CollectAsync("FailingCollector");

        // Assert
        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsSuccess);
        Assert.NotNull(snapshot.ErrorMessage);

        var registration = LoomCollectors.GetRegistration("FailingCollector");
        Assert.Equal(0, registration!.SuccessCount);
        Assert.True(registration.FailureCount >= 1); // At least 1 (may be more from scheduler)
    }

    [Fact]
    public async Task CollectAsync_WithSnapshotThatReturnsFailed_CountsAsAFailureNotASuccess()
    {
        // Arrange
        var collector = new FailedSnapshotCollector();
        LoomCollectors.Register(collector);

        // Disable automatic collection to avoid race conditions
        LoomCollectors.SetEnabled("FailedSnapshotCollector", false);
        await Task.Delay(100); // Wait for any in-flight collections

        // Re-enable and collect manually
        LoomCollectors.SetEnabled("FailedSnapshotCollector", true);

        // Act - CollectAsync returns a failed snapshot without throwing
        var snapshot = await LoomCollectors.CollectAsync("FailedSnapshotCollector");

        // Assert
        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsSuccess);

        var registration = LoomCollectors.GetRegistration("FailedSnapshotCollector");
        Assert.Equal(0, registration!.SuccessCount);
        Assert.True(registration.FailureCount >= 1);
    }

    [Fact]
    public void SetEnabled_DisablesCollector()
    {
        // Arrange
        var collector = new SampleCollector("TestCollector");
        LoomCollectors.Register(collector);

        // Act
        LoomCollectors.SetEnabled("TestCollector", false);

        // Assert
        var registration = LoomCollectors.GetRegistration("TestCollector");
        Assert.False(registration!.IsEnabled);
    }

    [Fact]
    public void Unregister_RemovesCollector()
    {
        // Arrange
        var collector = new SampleCollector("TestCollector");
        LoomCollectors.Register(collector);

        // Act
        var removed = LoomCollectors.Unregister("TestCollector");

        // Assert
        Assert.True(removed);
        Assert.Empty(LoomCollectors.GetRegistrations());
    }

    [Fact]
    public void SetCollectionInterval_UpdatesInterval()
    {
        // Arrange & Act
        LoomCollectors.SetCollectionInterval(TimeSpan.FromSeconds(5));

        // Assert - No exception thrown, interval updated
    }

    [Fact]
    public void SetCollectionInterval_TooShort_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LoomCollectors.SetCollectionInterval(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Scheduler_DoesNotOverlapCollectionsOfTheSameCollector()
    {
        // Arrange - each tick takes ~2.5s but ticks every 1s, so without gating the
        // scheduler will start a second (overlapping) run of the same collector while
        // the first is still in flight.
        var collector = new ConcurrencyTrackingCollector(TimeSpan.FromMilliseconds(2500));
        LoomCollectors.Register(collector);
        LoomCollectors.SetCollectionInterval(TimeSpan.FromSeconds(1));

        // Act
        await Task.Delay(TimeSpan.FromSeconds(4));
        LoomCollectors.Shutdown();

        // Assert
        Assert.Equal(1, Volatile.Read(ref collector.MaxConcurrency));
    }
}

/// <summary>
/// Sample collector for testing.
/// </summary>
internal sealed class SampleCollector : ILoomCollector
{
    public string Name { get; }

    public SampleCollector(string name)
    {
        Name = name;
    }

    public Task<CollectorSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var metrics = new[]
        {
            new MetricRecord("test.counter", MetricType.Counter, 1, DateTime.UtcNow.Ticks),
            new MetricRecord("test.gauge", MetricType.Gauge, 42.5, DateTime.UtcNow.Ticks),
            new MetricRecord("test.histogram", MetricType.Histogram, 123.45, DateTime.UtcNow.Ticks)
        };

        var snapshot = new CollectorSnapshot(Name, metrics);
        return Task.FromResult(snapshot);
    }
}

/// <summary>
/// Collector that always fails for testing error handling.
/// </summary>
internal sealed class FailingCollector : ILoomCollector
{
    public string Name => "FailingCollector";

    public Task<CollectorSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated collector failure");
    }
}

/// <summary>
/// Collector that reports failure by RETURNING CollectorSnapshot.Failed(...) rather than
/// throwing - exercises the non-exceptional failure path in
/// LoomCollectors.CollectFromRegistrationAsync.
/// </summary>
internal sealed class FailedSnapshotCollector : ILoomCollector
{
    public string Name => "FailedSnapshotCollector";

    public Task<CollectorSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(CollectorSnapshot.Failed(Name, "Simulated non-throwing failure"));
    }
}

/// <summary>
/// Collector that tracks how many concurrent CollectAsync calls are in flight at once
/// (and the high-water mark), and holds each call open for a fixed delay to make overlap
/// observable.
/// </summary>
internal sealed class ConcurrencyTrackingCollector : ILoomCollector
{
    private readonly TimeSpan _delay;
    private int _current;

    public int MaxConcurrency;

    public ConcurrencyTrackingCollector(TimeSpan delay)
    {
        _delay = delay;
    }

    public string Name => "ConcurrencyTrackingCollector";

    public async Task<CollectorSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref _current);
        InterlockedMax(ref MaxConcurrency, current);
        try
        {
            await Task.Delay(_delay, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _current);
        }

        return new CollectorSnapshot(Name, Array.Empty<MetricRecord>());
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int initial;
        do
        {
            initial = Volatile.Read(ref location);
            if (value <= initial)
                return;
        } while (Interlocked.CompareExchange(ref location, value, initial) != initial);
    }
}
