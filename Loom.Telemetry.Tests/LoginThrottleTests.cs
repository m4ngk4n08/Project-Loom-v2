using System;
using Loom.Security;
using Xunit;

namespace Loom.Telemetry.Tests;

public class LoginThrottleTests
{
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void FourFailures_NotBlocked()
    {
        var throttle = new LoginThrottle(_clock);
        for (var i = 0; i < 4; i++) throttle.RecordFailure("1.2.3.4");

        Assert.False(throttle.IsBlocked("1.2.3.4", out _));
    }

    [Fact]
    public void FiveFailures_Blocked_WithPositiveRetryAfter()
    {
        var throttle = new LoginThrottle(_clock);
        for (var i = 0; i < 5; i++) throttle.RecordFailure("1.2.3.4");

        var blocked = throttle.IsBlocked("1.2.3.4", out var retryAfter);
        Assert.True(blocked);
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void FiveFailures_ClockAdvancedPast15Minutes_NotBlocked()
    {
        var throttle = new LoginThrottle(_clock);
        for (var i = 0; i < 5; i++) throttle.RecordFailure("1.2.3.4");

        _clock.Advance(LoginThrottle.Window + TimeSpan.FromSeconds(1));

        Assert.False(throttle.IsBlocked("1.2.3.4", out _));
    }

    [Fact]
    public void Reset_AfterFiveFailures_NotBlocked()
    {
        var throttle = new LoginThrottle(_clock);
        for (var i = 0; i < 5; i++) throttle.RecordFailure("1.2.3.4");

        throttle.Reset("1.2.3.4");

        Assert.False(throttle.IsBlocked("1.2.3.4", out _));
    }

    [Fact]
    public void TwoDifferentClients_TrackedIndependently()
    {
        var throttle = new LoginThrottle(_clock);
        for (var i = 0; i < 5; i++) throttle.RecordFailure("1.2.3.4");

        Assert.True(throttle.IsBlocked("1.2.3.4", out _));
        Assert.False(throttle.IsBlocked("5.6.7.8", out _));
    }

    [Fact]
    public void ElevenHundredDistinctClients_TableNeverExceedsCap_MostRecentStillTracked()
    {
        var throttle = new LoginThrottle(_clock);
        for (var i = 0; i < 1100; i++) throttle.RecordFailure($"client-{i}");

        Assert.True(throttle.TrackedClients <= 1024);

        for (var i = 0; i < 4; i++) throttle.RecordFailure("client-1099");
        Assert.True(throttle.IsBlocked("client-1099", out _));
    }
}
