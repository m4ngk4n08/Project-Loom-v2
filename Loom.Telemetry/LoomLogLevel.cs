namespace Loom.Telemetry;

/// <summary>
/// Loom-owned log severity levels. Deliberately independent of
/// Microsoft.Extensions.Logging.LogLevel - Loom.Telemetry must not take a
/// dependency on any logging abstraction package.
/// </summary>
public enum LoomLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
