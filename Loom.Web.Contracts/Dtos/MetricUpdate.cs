using System.Text.Json.Serialization;

namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Base type for all real-time metric updates sent via WebSocket.
/// Uses [JsonDerivedType] so the source generator knows all possible concrete types
/// at compile time - REQUIRED for Native AOT!
/// </summary>
[JsonDerivedType(typeof(CpuMetricUpdate), typeDiscriminator: "cpu")]
[JsonDerivedType(typeof(MemoryMetricUpdate), typeDiscriminator: "memory")]
[JsonDerivedType(typeof(ThreadMetricUpdate), typeDiscriminator: "thread")]
public abstract record MetricUpdate
{
    /// <summary>
    /// When this update occurred
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// CPU metric update for WebSocket streaming.
/// </summary>
public sealed record CpuMetricUpdate : MetricUpdate
{
    public required CpuMetricResponse Data { get; init; }
}

/// <summary>
/// Memory metric update for WebSocket streaming.
/// </summary>
public sealed record MemoryMetricUpdate : MetricUpdate
{
    public required MemoryMetricResponse Data { get; init; }
}

/// <summary>
/// Thread metric update for WebSocket streaming.
/// </summary>
public sealed record ThreadMetricUpdate : MetricUpdate
{
    public required ThreadMetricResponse Data { get; init; }
}