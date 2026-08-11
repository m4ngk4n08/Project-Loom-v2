using System;
using System.Collections.Generic;
using System.Text;

namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// CPU hotpath metrics - which code is using the most CPU time.
/// Like a report showing which apps drain your phone battery the most.
/// </summary>
public sealed record CpuMetricResponse
{
    /// <summary>
    /// Overall CPU usage percentage (0-100)
    /// </summary>
    public required double CpuUsagePercent { get; init; }

    /// <summary>
    /// List of top CPU-consuming threads/methods.
    /// </summary>
    public required CpuHotpath[] Hotpaths { get; init; }

    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public required DateTime Timestamp { get; init; }
}
