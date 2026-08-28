using System;

namespace Loom.Telemetry.Tests;

/// <summary>Controllable clock for expiry tests. No Thread.Sleep anywhere in this suite.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}
