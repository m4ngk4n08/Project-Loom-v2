using Loom.Dashboard;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Telemetry.Alerting;
using Loom.Telemetry.Query;
using Loom.Telemetry.Exporters.Prometheus;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Loom.Web.RealTime;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

// Parse arguments
if (args.Length == 0 || !int.TryParse(args[0], out var targetPid))
{
    if (args is ["--version"])
    {
        Console.WriteLine("loom-dashboard 1.0.0");
        return;
    }
    Console.WriteLine("Usage: loom-dashboard <pid>");
    Console.WriteLine("  Starts the Loom dashboard, pulling metrics from the specified process.");
    return;
}

// Verify target process exists
try
{
    using var proc = Process.GetProcessById(targetPid);
    Console.WriteLine($"Connecting to: {proc.ProcessName} (pid {targetPid})");
}
catch
{
    Console.WriteLine($"Error: Process {targetPid} not found.");
    return;
}

var sessionStartedAtUtc = DateTime.UtcNow;

// Build the web application
var builder = WebApplication.CreateBuilder(Array.Empty<string>());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

// Core services
builder.Services.AddLoomStorage();
builder.Services.AddSingleton<IQueryExecutor, QueryExecutor>();

// Alerting services
builder.Services.AddLoomAlerting();
builder.Services.AddAlertTarget<ConsoleAlertTarget>();

// EventPipe bridge — pulls metrics from target process into the store
builder.Services.AddSingleton(sp =>
    new EventPipeBridge(targetPid, sp.GetRequiredService<IMetricStore>(),
        sp.GetRequiredService<ILogger<EventPipeBridge>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<EventPipeBridge>());

// Kestrel config
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.ListenLocalhost(5209);
});

builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// WebSocket support
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// Serve embedded Angular assets (if available)
IFileProvider? embeddedProvider = null;
try
{
    embeddedProvider = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
    app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedProvider });
}
catch (InvalidOperationException)
{
    Console.WriteLine("  Warning: No dashboard assets embedded. API-only mode.");
    Console.WriteLine("  Build Angular first: cd Loom.Web.Frontend && ng build");
    Console.WriteLine("  Then repack: dotnet pack Loom.Dashboard -c Release\n");
}

double peakWorkingSetMb = 0;

// API endpoints
var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Json(new HealthCheckResponse
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    UptimeSeconds = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
    MemoryUsageMb = Process.GetCurrentProcess().WorkingSet64 / 1_048_576.0
}, LoomJsonSerializerContext.Default.HealthCheckResponse));

api.MapGet("/session", (IMetricStore store) =>
{
    var processName = Process.GetProcessById(targetPid)?.ProcessName ?? $"pid-{targetPid}";
    return Results.Json(new SessionInfoResponse
    {
        TargetProcessId = targetPid,
        TargetProcessName = processName,
        StartedAtUtc = sessionStartedAtUtc,
        UptimeSeconds = (long)(DateTime.UtcNow - sessionStartedAtUtc).TotalSeconds,
        MetricCount = store.GetMetricNames().Count
    }, LoomJsonSerializerContext.Default.SessionInfoResponse);
});

api.MapGet("/metrics/cpu", (IMetricStore store) =>
    Results.Json(BuildCpuResponse(store), LoomJsonSerializerContext.Default.CpuMetricResponse));

api.MapGet("/metrics/memory", (IMetricStore store) =>
    Results.Json(BuildMemoryResponse(store), LoomJsonSerializerContext.Default.MemoryMetricResponse));

api.MapGet("/metrics/thread", (IMetricStore store) =>
    Results.Json(BuildThreadResponse(store), LoomJsonSerializerContext.Default.ThreadMetricResponse));

api.MapPost("/metrics/ingest", (MetricIngestRequest request, IMetricStore store) =>
{
    foreach (var metric in request.Metrics)
    {
        var tags = metric.Tags?.Select(kvp => new MetricTag(kvp.Key, kvp.Value)).ToArray()
            ?? Array.Empty<MetricTag>();

        var timestamp = metric.Timestamp ?? DateTime.UtcNow;

        var type = metric.Type.ToLowerInvariant() switch
        {
            "counter" => MetricType.Counter,
            "gauge" => MetricType.Gauge,
            "histogram" => MetricType.Histogram,
            _ => (MetricType?)null
        };

        if (type is null)
            return Results.BadRequest(new { error = $"Unknown metric type: {metric.Type}. Must be Counter, Gauge, or Histogram." });

        var record = new MetricRecord(
            metric.Name,
            type.Value,
            metric.Value,
            timestamp.Ticks,
            tags.Length > 0 ? tags : null
        );
        store.Write(in record);
    }

    return Results.Accepted();
})
.WithName("IngestMetrics")
.WithTags("Metrics")
.Produces(202)
.Produces(400);

api.MapPost("/query", async (QueryRequest request, IQueryExecutor executor, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("Loom.Query");
    try
    {
        var result = await executor.ExecuteAsync(request.Query, ct);
        logger.LogInformation(
            "Query executed: {Query} -> {RowCount} row(s), {ElapsedMs:F1}ms",
            request.Query, result.Rows.Count, result.ExecutionTimeMs);
        return Results.Json(result, LoomJsonSerializerContext.Default.QueryResponse);
    }
    catch (QuerySyntaxException ex)
    {
        logger.LogWarning("Query rejected: {Query} -> {Error}", request.Query, ex.Message);
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

api.MapGet("/query", async (string q, IQueryExecutor executor, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("Loom.Query");
    try
    {
        var result = await executor.ExecuteAsync(q, ct);
        logger.LogInformation(
            "Query executed: {Query} -> {RowCount} row(s), {ElapsedMs:F1}ms",
            q, result.Rows.Count, result.ExecutionTimeMs);
        return Results.Json(result, LoomJsonSerializerContext.Default.QueryResponse);
    }
    catch (QuerySyntaxException ex)
    {
        logger.LogWarning("Query rejected: {Query} -> {Error}", q, ex.Message);
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

var alertGroup = api.MapGroup("/alerts")
    .WithTags("Alerts");

alertGroup.MapGet("", () =>
{
    var rules = LoomTelemetryOptionsAlertingExtensions.Rules
        .Select(r => new AlertConfigDto { Name = r.Name, MetricName = r.MetricName, Window = r.Window })
        .ToList();
    return Results.Json(rules, LoomJsonSerializerContext.Default.ListAlertConfigDto);
})
.WithName("GetAlerts")
.Produces<List<AlertConfigDto>>(200);

alertGroup.MapGet("/{name}", (string name) =>
{
    var rule = LoomTelemetryOptionsAlertingExtensions.Rules.FirstOrDefault(r => r.Name == name);
    if (rule is null) return Results.NotFound();

    return Results.Json(
        new AlertConfigDto { Name = rule.Name, MetricName = rule.MetricName, Window = rule.Window },
        LoomJsonSerializerContext.Default.AlertConfigDto);
})
.WithName("GetAlert")
.Produces<AlertConfigDto>(200)
.Produces(404);

alertGroup.MapPost("/{name}/test", async (string name, Channel<AlertNotification> channel) =>
{
    var rule = LoomTelemetryOptionsAlertingExtensions.Rules.FirstOrDefault(r => r.Name == name);
    if (rule is null) return Results.NotFound();

    var testAggregate = new MetricAggregate(rule.MetricName, Count: 1, Average: 0, Max: 0, P99: 0);
    await channel.Writer.WriteAsync(new AlertNotification(rule, testAggregate, DateTime.UtcNow));
    return Results.Accepted();
})
.WithName("TestAlert")
.Produces(202)
.Produces(404);

alertGroup.MapPut("/{name}/silence", (string name, TimeSpan duration, ISilenceStore silenceStore) =>
{
    var rule = LoomTelemetryOptionsAlertingExtensions.Rules.FirstOrDefault(r => r.Name == name);
    if (rule is null) return Results.NotFound();

    silenceStore.Silence(name, DateTime.UtcNow + duration);
    return Results.NoContent();
})
.WithName("SilenceAlert")
.Produces(204)
.Produces(404);

api.MapGet("/exporters/status", () =>
    Results.Json(new List<ExporterStatusDto>(), LoomJsonSerializerContext.Default.ListExporterStatusDto));

api.MapGet("/exporters/metrics/names", (IMetricStore store) =>
    Results.Json(store.GetMetricNames().ToList(), LoomJsonSerializerContext.Default.ListString));

api.MapGet("/exporters/metrics/summary", (IMetricStore store) =>
    Results.Json(MetricSummaryBuilder.BuildAll(store), LoomJsonSerializerContext.Default.ListMetricSummaryDto));

// Prometheus metrics endpoint - moved to /prometheus to avoid conflict with Angular /metrics route
app.MapGet("/prometheus", (IMetricStore store) =>
    Results.Text(PrometheusFormatter.Format(store), "text/plain; version=0.0.4; charset=utf-8"));

// WebSocket endpoint — streams polymorphic MetricUpdate messages to the frontend
app.Map("/ws/metrics", async (HttpContext context, IMetricStore store) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    using var handler = new MetricsWebSocketHandler(webSocket);
    await handler.StreamMetricsAsync(GetMetricsStreamAsync(store, context.RequestAborted), context.RequestAborted);
});

// SPA fallback — serve index.html for all unmatched routes
app.MapFallback(async context =>
{
    if (embeddedProvider != null)
    {
        var file = embeddedProvider.GetFileInfo("index.html");
        if (file.Exists)
        {
            context.Response.ContentType = "text/html";
            await using var stream = file.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
            return;
        }
    }
    context.Response.StatusCode = 404;
    await context.Response.WriteAsync("Dashboard assets not found. Build Angular and repack: cd Loom.Web.Frontend && ng build");
});

Console.WriteLine($"\n  Loom Dashboard running at http://localhost:5209");
Console.WriteLine($"  Pulling metrics from PID {targetPid}");
Console.WriteLine($"  Press Ctrl+C to stop.\n");

// Auto-open browser
try
{
    Process.Start(new ProcessStartInfo("http://localhost:5209") { UseShellExecute = true });
}
catch { }

await app.RunAsync();

// Session summary — emitted on clean shutdown so external consumers
// (logs collectors, AI analysis) get a digest of the whole observation window.
var summaryStore = app.Services.GetRequiredService<IMetricStore>();
var summaryLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Loom.SessionSummary");
var metricNames = summaryStore.GetMetricNames();
long totalRecords = 0;
foreach (var name in metricNames)
{
    totalRecords += summaryStore.ReadRecent(name, 8192).Length;
}
summaryLogger.LogInformation(
    "Session summary: targetPid={TargetPid} uptime={UptimeSeconds}s metrics={MetricCount} totalRecords={TotalRecords}",
    targetPid,
    (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
    metricNames.Count,
    totalRecords);

static CpuMetricResponse BuildCpuResponse(IMetricStore store)
{
    // Target process CPU usage from System.Runtime EventCounters (0-1 fraction).
    // Average the last few 1s samples: a single sample can read 0 between work bursts.
    var cpuUsage = AverageRecent(store, "cpu-usage", 5);
    var cpuPercent = cpuUsage is { } cpu ? cpu * 100 : 0;

    // CPU hotpaths: instrumented method execution metrics (latency/duration/elapsed
    // histograms recording ms). Each path's share of total observed time is its
    // percentage - the sample app has no true per-method CPU sampler, so shares of
    // instrumented execution time is the honest, meaningful metric available.
    var buffers = store.GetBuffers();
    var hotpaths = new List<CpuHotpath>();
    foreach (var kvp in buffers)
    {
        if (!IsInstrumentedMethod(kvp.Key)) continue;
        var recent = kvp.Value.ReadRecent(10);
        if (recent.Length == 0) continue;
        hotpaths.Add(new CpuHotpath
        {
            MethodName = kvp.Key,
            CpuPercent = 0, // normalized below
            InvocationCount = recent.Length,
            AverageTimeMs = recent.Average(r => r.Value)
        });
    }

    // Normalize each path's average time into a share of the total observed time.
    var totalMs = hotpaths.Sum(h => h.AverageTimeMs);
    hotpaths = hotpaths
        .Select(h => h with { CpuPercent = totalMs > 0 ? h.AverageTimeMs / totalMs * 100 : 0 })
        .OrderByDescending(h => h.AverageTimeMs)
        .Take(3)
        .ToList();
    return new CpuMetricResponse { CpuUsagePercent = Math.Max(0, Math.Min(100, cpuPercent)), Hotpaths = hotpaths.ToArray(), Timestamp = DateTime.UtcNow };
}

static bool IsInstrumentedMethod(string name)
{
    // Instrumented method metrics carry duration semantics in their name. The
    // System.Runtime gauges (cpu-usage, gc-*, gen-*, threadpool-*) are excluded.
    return name.Contains("latency", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("elapsed", StringComparison.OrdinalIgnoreCase) ||
           name.Contains(".time", StringComparison.OrdinalIgnoreCase);
}

MemoryMetricResponse BuildMemoryResponse(IMetricStore store)
{
    // Target process memory from System.Runtime EventCounters (working-set in MB).
    var workingSetMb = LatestValue(store, "working-set") ?? Process.GetCurrentProcess().WorkingSet64 / 1_048_576.0;
    var gcHeapMb = LatestValue(store, "gc-heap-size");

    // Track peak so the frontend's "used / total" bar stays meaningful without
    // a target memory budget (System.Runtime exposes no total-available counter).
    peakWorkingSetMb = Math.Max(peakWorkingSetMb, workingSetMb);

    // GC collection counters from the target.
    var gen0 = LatestValue(store, "gen-0-collection-count") ?? 0;
    var gen1 = LatestValue(store, "gen-1-collection-count") ?? 0;
    var gen2 = LatestValue(store, "gen-2-collection-count") ?? 0;

    return new MemoryMetricResponse
    {
        TotalMemoryMb = peakWorkingSetMb,
        UsedMemoryMb = workingSetMb,
        GcStats = new GarbageCollectionStats
        {
            Gen0Collections = (int)gen0,
            Gen1Collections = (int)gen1,
            Gen2Collections = (int)gen2,
            TotalGcTimeMs = LatestValue(store, "time-in-gc") ?? 0
        },
        TopAllocations = Array.Empty<MemoryAllocation>(),
        Timestamp = DateTime.UtcNow
    };
}

static ThreadMetricResponse BuildThreadResponse(IMetricStore store)
{
    // Target process thread pool from System.Runtime EventCounters.
    var workerThreads = LatestValue(store, "threadpool-thread-count") ?? 0;
    var ioThreads = LatestValue(store, "threadpool-io-thread-count") ?? 0;
    var queueLength = LatestValue(store, "threadpool-queue-length") ?? 0;

    var totalThreads = (int)(workerThreads + ioThreads);
    var blockedCount = Math.Min((int)queueLength, totalThreads);

    return new ThreadMetricResponse
    {
        TotalThreads = totalThreads,
        ActiveThreads = totalThreads - blockedCount,
        BlockedThreads = blockedCount,
        Blockages = Array.Empty<ThreadBlockage>(),
        Timestamp = DateTime.UtcNow
    };
}

static double? LatestValue(IMetricStore store, string name)
{
    var records = store.ReadRecent(name, 1);
    return records.Length > 0 ? records[0].Value : null;
}

static double? AverageRecent(IMetricStore store, string name, int count)
{
    var records = store.ReadRecent(name, count);
    if (records.Length == 0) return null;
    return records.Average(r => r.Value);
}

async IAsyncEnumerable<MetricUpdate> GetMetricsStreamAsync(IMetricStore store, [EnumeratorCancellation] CancellationToken ct)
{
    var metricIndex = 0;

    while (!ct.IsCancellationRequested)
    {
        switch (metricIndex % 3)
        {
            case 0:
                yield return new CpuMetricUpdate { Timestamp = DateTime.UtcNow, Data = BuildCpuResponse(store) };
                break;
            case 1:
                yield return new MemoryMetricUpdate { Timestamp = DateTime.UtcNow, Data = BuildMemoryResponse(store) };
                break;
            case 2:
                yield return new ThreadMetricUpdate { Timestamp = DateTime.UtcNow, Data = BuildThreadResponse(store) };
                break;
        }

        metricIndex++;
        await Task.Delay(300, ct);
    }
}