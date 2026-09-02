namespace Loom.Telemetry;

/// <summary>
/// Telemetry-level configuration for <c>AddLoomTelemetry</c>. Deliberately empty: it is a
/// reserved seam for options that belong to telemetry itself, and nothing needs one yet.
/// </summary>
/// <remarks>
/// This is NOT the extension point for the other tier packages, though it was until
/// 034884e. An extension method here has nowhere durable to put what it is given - the
/// options object is handed to the caller's lambda and discarded once startup finishes -
/// so the alerting package resorted to a process-global static list beside it, which is
/// the defect BACKLOG.md 6.10 was filed about. Each tier package owns its own
/// registration call instead: alert rules go through
/// <c>AddLoomAlerting(registry =&gt; ...)</c>, which hands back a live injectable registry
/// that outlives startup and can be mutated over HTTP.
/// </remarks>
public sealed class LoomTelemetryOptions
{
    // No members yet. See the remarks above before adding an extension method for another
    // package here.
}
