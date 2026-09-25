using System;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>A [Fact] that reports SKIPPED on Windows instead of passing without testing anything.
/// RequireNonRoot = true also skips when running as root, where a read-only directory does not
/// block writes and a permission-denied test would prove nothing.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Unix-only";
    }

    public bool RequireNonRoot
    {
        get => _requireNonRoot;
        set
        {
            _requireNonRoot = value;
            if (value && !OperatingSystem.IsWindows() && Environment.UserName == "root") Skip = "Running as root";
        }
    }

    private bool _requireNonRoot;
}
