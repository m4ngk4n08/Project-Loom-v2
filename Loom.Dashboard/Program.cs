using Loom.Dashboard;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Telemetry.Query;
using Loom.Telemetry.Exporters.Prometheus;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Reflection;

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

// Build the web application
var builder = WebApplication.CreateBuilder(Array.Empty<string>());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

// Core services
builder.Services.AddLoomStorage();
builder.Services.AddSingleton<IQueryExecutor, QueryExecutor>();

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

builder.Logging.SetMinimumLevel(LogLevel.Warning);

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

// API endpoints
var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Json(new HealthCheckResponse
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    UptimeSeconds = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
    MemoryUsageMb = Process.GetCurrentProcess().WorkingSet64 / 1_048_576.0
}, LoomJsonSerializerContext.Default.HealthCheckResponse));

api.MapGet("/metrics/cpu", (IMetricStore store) =>
{
    var buffers = store.GetBuffers();
    var hotpaths = new List<CpuHotpath>();
    foreach (var kvp in buffers.Where(b => b.Key.Contains("cpu") || b.Key.Contains("elapsed")))
    {
        var recent = kvp.Value.ReadRecent(10);
        if (recent.Length > 0)
            hotpaths.Add(new CpuHotpath { MethodName = kvp.Key, CpuPercent = recent.Average(r => r.Value), InvocationCount = recent.Length, AverageTimeMs = recent.Average(r => r.Value) });
    }
    return Results.Json(new CpuMetricResponse { CpuUsagePercent = 0, Hotpaths = hotpaths.ToArray(), Timestamp = DateTime.UtcNow }, LoomJsonSerializerContext.Default.CpuMetricResponse);
});

api.MapGet("/metrics/memory", () =>
{
    var proc = Process.GetCurrentProcess();
    proc.Refresh();
    return Results.Json(new MemoryMetricResponse
    {
        TotalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1_048_576.0,
        UsedMemoryMb = proc.WorkingSet64 / 1_048_576.0,
        GcStats = new GarbageCollectionStats { Gen0Collections = GC.CollectionCount(0), Gen1Collections = GC.CollectionCount(1), Gen2Collections = GC.CollectionCount(2), TotalGcTimeMs = GC.GetTotalPauseDuration().TotalMilliseconds },
        TopAllocations = Array.Empty<MemoryAllocation>(),
        Timestamp = DateTime.UtcNow
    }, LoomJsonSerializerContext.Default.MemoryMetricResponse);
});

api.MapGet("/metrics/thread", () =>
{
    var proc = Process.GetCurrentProcess();
    proc.Refresh();
    return Results.Json(new ThreadMetricResponse
    {
        TotalThreads = proc.Threads.Count,
        ActiveThreads = proc.Threads.Count,
        BlockedThreads = 0,
        Blockages = Array.Empty<ThreadBlockage>(),
        Timestamp = DateTime.UtcNow
    }, LoomJsonSerializerContext.Default.ThreadMetricResponse);
});

api.MapPost("/query", async (QueryRequest request, IQueryExecutor executor, CancellationToken ct) =>
{
    try
    {
        var result = await executor.ExecuteAsync(request.Query, ct);
        return Results.Json(result, LoomJsonSerializerContext.Default.QueryResponse);
    }
    catch (QuerySyntaxException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

api.MapGet("/query", async (string q, IQueryExecutor executor, CancellationToken ct) =>
{
    try
    {
        var result = await executor.ExecuteAsync(q, ct);
        return Results.Json(result, LoomJsonSerializerContext.Default.QueryResponse);
    }
    catch (QuerySyntaxException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

api.MapGet("/exporters/status", () =>
    Results.Json(new List<ExporterStatusDto>(), LoomJsonSerializerContext.Default.ListExporterStatusDto));

api.MapGet("/exporters/metrics/names", (IMetricStore store) =>
    Results.Json(store.GetMetricNames().ToList(), LoomJsonSerializerContext.Default.ListString));

app.MapGet("/metrics", (IMetricStore store) =>
    Results.Text(PrometheusFormatter.Format(store), "text/plain; version=0.0.4; charset=utf-8"));

// WebSocket endpoint — streams metrics from store subscriptions
app.Map("/ws/metrics", async (HttpContext context, IMetricStore store) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var reader = store.Subscribe();
    var buffer = new byte[4096];

    try
    {
        while (!context.RequestAborted.IsCancellationRequested &&
               webSocket.State == System.Net.WebSockets.WebSocketState.Open)
        {
            if (await reader.WaitToReadAsync(context.RequestAborted))
            {
                while (reader.TryRead(out var record))
                {
                    var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                        new { name = record.Name, type = record.Type.ToString(), value = record.Value, timestamp = new DateTime(record.TimestampUtcTicks, DateTimeKind.Utc) });

                    await webSocket.SendAsync(json, System.Net.WebSockets.WebSocketMessageType.Text, true, context.RequestAborted);
                }
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (System.Net.WebSockets.WebSocketException) { }
    finally
    {
        store.Unsubscribe(reader);
    }
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
