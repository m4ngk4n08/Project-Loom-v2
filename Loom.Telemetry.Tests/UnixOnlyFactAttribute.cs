using System;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>A [Fact] that reports SKIPPED on Windows instead of passing without testing anything.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Unix-only";
    }
}
