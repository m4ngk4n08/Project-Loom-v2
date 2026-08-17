namespace Loom.Telemetry.Exporters.GrafanaCloud;

/// <summary>
/// Configuration for Grafana Cloud metric export.
/// </summary>
public sealed class GrafanaCloudOptions
{
    /// <summary>
    /// Grafana Cloud push endpoint URL (e.g., https://prometheus-prod-XX.grafana.net/api/prom/push).
    /// </summary>
    public required string PushEndpoint { get; set; }

    /// <summary>
    /// Grafana Cloud API key for authentication (Bearer token).
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// Optional tenant/instance ID.
    /// </summary>
    public string? TenantId { get; set; }
}
