using System;
using System.Linq;
using Xunit;

namespace Loom.Telemetry.Tests;

public sealed class SamplingTests : IDisposable
{
    public SamplingTests()
    {
        // Clear rules before each test
        LoomSampling.ClearRules();
    }

    public void Dispose()
    {
        // Clear rules after each test
        LoomSampling.ClearRules();
    }

    [Fact]
    public void NoRules_RecordsEverything()
    {
        // Arrange - no rules configured
        var metricName = $"test.norules.{Guid.NewGuid()}";

        // Act
        LoomRuntime.RecordMethodExecution(metricName, TimeSpan.FromMilliseconds(50), null);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
    }

    [Fact]
    public void DurationRule_SamplesFastRequests()
    {
        // Arrange
        LoomSampling.Configure(c =>
        {
            // Sample fast requests at 0% (never record)
            c.SampleByDuration(TimeSpan.FromMilliseconds(100), rate: 0.0);
        });

        var metricName = $"test.duration.{Guid.NewGuid()}";

        // Act - Fast request
        LoomRuntime.RecordMethodExecution(metricName, TimeSpan.FromMilliseconds(50), null);

        // Assert - Should NOT be recorded (0% sample rate)
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.Null(found.Name); // Not recorded
    }

    [Fact]
    public void DurationRule_AlwaysRecordsSlowRequests()
    {
        // Arrange
        LoomSampling.Configure(c =>
        {
            c.SampleByDuration(TimeSpan.FromMilliseconds(100), rate: 0.0);
        });

        var metricName = $"test.slow.{Guid.NewGuid()}";

        // Act - Slow request (above threshold)
        LoomRuntime.RecordMethodExecution(metricName, TimeSpan.FromMilliseconds(200), null);

        // Assert - Should be recorded (slow = always record)
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.NotNull(found.Name);
        Assert.Equal(200, found.Value, 1); // Within 1ms tolerance
    }

    [Fact]
    public void NameRule_SamplesMatchingNames()
    {
        // Arrange
        LoomSampling.Configure(c =>
        {
            c.SampleByName("HealthCheck", rate: 0.0); // Never record health checks
        });

        var healthCheckName = $"HealthCheck.{Guid.NewGuid()}";
        var orderName = $"ProcessOrder.{Guid.NewGuid()}";

        // Act
        LoomRuntime.RecordMethodExecution(healthCheckName, TimeSpan.FromMilliseconds(10), null);
        LoomRuntime.RecordMethodExecution(orderName, TimeSpan.FromMilliseconds(10), null);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);

        var healthCheck = recent.FirstOrDefault(m => m.Name == healthCheckName);
        var order = recent.FirstOrDefault(m => m.Name == orderName);

        Assert.Null(healthCheck.Name); // Health check skipped
        Assert.NotNull(order.Name);     // Order recorded
    }

    [Fact]
    public void AlwaysRecordWhen_OverridesSampling()
    {
        // Arrange
        LoomSampling.Configure(c =>
        {
            // Sample everything at 0% (skip everything)
            c.SampleByDuration(TimeSpan.Zero, rate: 0.0);

            // EXCEPT errors (always record)
            c.AlwaysRecordWhen((_, __, ex) => ex != null);
        });

        var successName = $"test.success.{Guid.NewGuid()}";
        var errorName = $"test.error.{Guid.NewGuid()}";

        // Act
        LoomRuntime.RecordMethodExecution(successName, TimeSpan.FromMilliseconds(10), null);
        LoomRuntime.RecordMethodExecution(errorName, TimeSpan.FromMilliseconds(10), new Exception("Test"));

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);

        var success = recent.FirstOrDefault(m => m.Name == successName);
        var error = recent.FirstOrDefault(m => m.Name == errorName);

        Assert.Null(success.Name);  // Success skipped (0% rate)
        Assert.NotNull(error.Name); // Error recorded (AlwaysRecord override)
    }

    [Fact]
    public void CompositeRules_EvaluatedByPriority()
    {
        // Arrange
        LoomSampling.Configure(c =>
        {
            // Base rule: Skip fast requests
            c.SampleByDuration(TimeSpan.FromMilliseconds(100), rate: 0.0);

            // Override 1: Always record errors (high priority)
            c.AlwaysRecordWhen((_, __, ex) => ex != null);

            // Override 2: Always record slow requests
            c.AlwaysRecordWhen((_, duration, __) =>
                duration.HasValue && duration.Value > TimeSpan.FromMilliseconds(500));
        });

        var fastSuccess = $"test.fast.success.{Guid.NewGuid()}";
        var fastError = $"test.fast.error.{Guid.NewGuid()}";
        var verySlow = $"test.veryslow.{Guid.NewGuid()}";

        // Act
        LoomRuntime.RecordMethodExecution(fastSuccess, TimeSpan.FromMilliseconds(50), null);
        LoomRuntime.RecordMethodExecution(fastError, TimeSpan.FromMilliseconds(50), new Exception());
        LoomRuntime.RecordMethodExecution(verySlow, TimeSpan.FromMilliseconds(600), null);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);

        Assert.Null(recent.FirstOrDefault(m => m.Name == fastSuccess).Name);  // Skipped
        Assert.NotNull(recent.FirstOrDefault(m => m.Name == fastError).Name); // Error override
        Assert.NotNull(recent.FirstOrDefault(m => m.Name == verySlow).Name);  // Slow override
    }

    [Fact]
    public void PropertyTracking_RespectsSampling()
    {
        // Arrange
        var uniqueMarker = Guid.NewGuid().ToString();
        LoomSampling.Configure(c =>
        {
            c.SampleByName("Temperature", rate: 0.0); // Skip Temperature properties
        });

        var instance = new TrackedPropertySample();

        // Act
        instance.Temperature_Tracked = 42.5;

        // Assert - Should be skipped due to sampling
        // Look for Temperature metrics recorded AFTER this point
        var timestampBefore = DateTime.UtcNow.Ticks;
        System.Threading.Thread.Sleep(10); // Small delay to ensure timestamp difference

        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m =>
            m.Name.Contains("Temperature") &&
            m.TimestampUtcTicks > timestampBefore &&
            Math.Abs(m.Value - 42.5) < 0.1);

        Assert.Null(found.Name);
    }

    [Fact]
    public void SampleAll_AppliesUniformSampling()
    {
        // Arrange
        LoomSampling.Configure(c =>
        {
            c.SampleAll(rate: 0.0); // Skip everything
        });

        var metricName = $"test.uniform.{Guid.NewGuid()}";

        // Act
        LoomRuntime.RecordMethodExecution(metricName, TimeSpan.FromMilliseconds(100), null);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);
        var found = recent.FirstOrDefault(m => m.Name == metricName);

        Assert.Null(found.Name);
    }

    [Fact]
    public void HighVolumeScenario_ReducesMetricCount()
    {
        // Arrange
        LoomSampling.Configure(c =>
        {
            // Sample fast requests at 10%
            c.SampleByDuration(TimeSpan.FromMilliseconds(100), rate: 0.10);

            // Always record slow
            c.AlwaysRecordWhen((_, duration, __) =>
                duration.HasValue && duration.Value > TimeSpan.FromMilliseconds(100));
        });

        var metricNameFast = $"test.highvolume.fast.{Guid.NewGuid()}";
        var metricNameSlow = $"test.highvolume.slow.{Guid.NewGuid()}";

        // Act - Simulate 100 fast + 10 slow requests
        for (int i = 0; i < 100; i++)
        {
            LoomRuntime.RecordMethodExecution(metricNameFast, TimeSpan.FromMilliseconds(50), null);
        }

        for (int i = 0; i < 10; i++)
        {
            LoomRuntime.RecordMethodExecution(metricNameSlow, TimeSpan.FromMilliseconds(200), null);
        }

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(500);
        var fast = recent.Where(m => m.Name == metricNameFast).ToList();
        var slow = recent.Where(m => m.Name == metricNameSlow).ToList();

        // Fast: ~10 recorded (10% of 100)
        Assert.True(fast.Count < 30, $"Expected ~10 fast metrics, got {fast.Count}");

        // Slow: All 10 recorded
        Assert.Equal(10, slow.Count);
    }

    [Fact]
    public void ClearRules_ResetsToRecordEverything()
    {
        // Arrange
        LoomSampling.Configure(c => c.SampleAll(rate: 0.0));

        var metricName1 = $"test.clear1.{Guid.NewGuid()}";
        var metricName2 = $"test.clear2.{Guid.NewGuid()}";

        // Act
        LoomRuntime.RecordMethodExecution(metricName1, TimeSpan.FromMilliseconds(10), null);

        LoomSampling.ClearRules(); // Reset

        LoomRuntime.RecordMethodExecution(metricName2, TimeSpan.FromMilliseconds(10), null);

        // Assert
        var recent = LoomMetrics.GetRecentMetrics(100);

        Assert.Null(recent.FirstOrDefault(m => m.Name == metricName1).Name);    // Skipped
        Assert.NotNull(recent.FirstOrDefault(m => m.Name == metricName2).Name); // Recorded
    }
}
