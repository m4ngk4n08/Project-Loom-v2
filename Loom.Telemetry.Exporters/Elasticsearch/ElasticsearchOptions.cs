namespace Loom.Telemetry.Exporters.Elasticsearch;

/// <summary>
/// Configuration for Elasticsearch metric export.
/// </summary>
public sealed class ElasticsearchOptions
{
    /// <summary>
    /// Elasticsearch cluster endpoint (e.g., https://localhost:9200).
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// Index pattern with date placeholder.
    /// Default: "loom-metrics-{0:yyyy.MM.dd}"
    /// </summary>
    public string IndexPattern { get; set; } = "loom-metrics-{0:yyyy.MM.dd}";

    /// <summary>
    /// Optional API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }
}
