using System.Text.Json;
using System.Text.Json.Serialization;
using Loom.Web.Contracts.Dtos;

namespace Loom.Web.Contracts;

/// <summary>
/// JSON serialization context for Native AOT.
/// THIS IS CRITICAL! Every DTO type MUST be registered here.
///
/// Think of this like a registry or phonebook - the compiler needs to know
/// at compile-time which types will be serialized to/from JSON.
/// </summary>
[JsonSerializable(typeof(HealthCheckResponse))]
[JsonSerializable(typeof(CpuMetricResponse))]
[JsonSerializable(typeof(CpuHotpath))]
[JsonSerializable(typeof(MemoryMetricResponse))]
[JsonSerializable(typeof(GarbageCollectionStats))]
[JsonSerializable(typeof(MemoryAllocation))]
[JsonSerializable(typeof(ThreadMetricResponse))]
[JsonSerializable(typeof(ThreadBlockage))]
[JsonSerializable(typeof(DiagnosticSearchRequest))]
[JsonSerializable(typeof(DiagnosticSearchResponse))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(TelemetryIngestRequest))]
[JsonSerializable(typeof(MetricUpdate))]
[JsonSerializable(typeof(CpuMetricUpdate))]
[JsonSerializable(typeof(MemoryMetricUpdate))]
[JsonSerializable(typeof(ThreadMetricUpdate))]
[JsonSerializable(typeof(QueryRequest))]
[JsonSerializable(typeof(QueryResponse))]
[JsonSerializable(typeof(QueryResultRow))]
[JsonSerializable(typeof(List<QueryResultRow>))]
[JsonSerializable(typeof(QueryValue))]
[JsonSerializable(typeof(List<QueryValue>))]
[JsonSerializable(typeof(QueryColumn))]
[JsonSerializable(typeof(AlertWebhookPayload))]
[JsonSerializable(typeof(AlertConfigDto))]
[JsonSerializable(typeof(List<AlertConfigDto>))]
[JsonSerializable(typeof(AlertConditionDto))]
[JsonSerializable(typeof(AlertStatusDto))]
[JsonSerializable(typeof(AlertHistoryEntry))]
[JsonSerializable(typeof(List<AlertHistoryEntry>))]
[JsonSerializable(typeof(ExporterStatusDto))]
[JsonSerializable(typeof(List<ExporterStatusDto>))]
[JsonSerializable(typeof(GrafanaMetricPayload))]
[JsonSerializable(typeof(GrafanaMetricSeries))]
[JsonSerializable(typeof(GrafanaMetricSeries[]))]
[JsonSerializable(typeof(GrafanaDataPoint))]
[JsonSerializable(typeof(GrafanaDataPoint[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization,
    WriteIndented = false
)]
public partial class LoomJsonSerializerContext : JsonSerializerContext
{
    // This class is partial and will be completed by the source generator at compile time.
    // No code needed here - the attributes above do all the work!
}