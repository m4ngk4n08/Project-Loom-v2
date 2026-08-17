using Loom.Web.Api.Interfaces;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Loom.Web.RealTime;
using Loom.Telemetry.Query;
using System.Diagnostics;

namespace Loom.Web.Api.Extensions
{
    public static class EndpointExtensions
    {
        public static WebApplication MapApiEndpoints(this WebApplication app)
        {
            app.MapHealthEndpoints();
            app.MapMetricsEndpoints();
            app.MapWebSocketEndpoints();
            app.MapQueryEndpoints();
            return app;
        }

        private static WebApplication MapHealthEndpoints(this WebApplication app)
        {
            var healthGroup = app.MapGroup("/api/health")
            .WithTags("Health");

            healthGroup.MapGet("", () =>
            {
                var process = Process.GetCurrentProcess();
                var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();

                var response = new HealthCheckResponse
                {
                    Status = "Healthy",
                    Timestamp = DateTime.UtcNow,
                    UptimeSeconds = (long)uptime.TotalSeconds,
                    MemoryUsageMb = process.WorkingSet64 / 1_048_576.0
                };

                return Results.Json(
                    response,
                    LoomJsonSerializerContext.Default.HealthCheckResponse,
                    statusCode: 200
                );
            })
            .WithName("GetHealth")
            .Produces<HealthCheckResponse>(200);

            return app;
        }

        private static WebApplication MapMetricsEndpoints(this WebApplication app)
        {
            var metricsGroup = app.MapGroup("/api/metrics")
                .WithTags("Metrics");

            // CPU metrics endpoint
            metricsGroup.MapGet("/cpu", async (IMetricsService metricsService, CancellationToken ct) =>
            {
                var metrics = await metricsService.GetCpuMetricsAsync(ct);

                return Results.Json(
                    metrics,
                    LoomJsonSerializerContext.Default.CpuMetricResponse,
                    statusCode: 200
                );
            })
            .WithName("GetCpuMetrics")
            .Produces<CpuMetricResponse>(200);

            // Memory metrics endpoint
            metricsGroup.MapGet("/memory", async (IMetricsService metricsService, CancellationToken ct) =>
            {
                var metrics = await metricsService.GetMemoryMetricsAsync(ct);

                return Results.Json(
                    metrics,
                    LoomJsonSerializerContext.Default.MemoryMetricResponse,
                    statusCode: 200
                );
            })
            .WithName("GetMemoryMetrics")
            .Produces<MemoryMetricResponse>(200);

            // Thread metrics endpoint
            metricsGroup.MapGet("/thread", async (IMetricsService metricsService, CancellationToken ct) =>
            {
                var metrics = await metricsService.GetThreadMetricsAsync(ct);

                return Results.Json(
                    metrics,
                    LoomJsonSerializerContext.Default.ThreadMetricResponse,
                    statusCode: 200
                );
            })
            .WithName("GetThreadMetrics")
            .Produces<ThreadMetricResponse>(200);

            return app;
        }

        private static WebApplication MapWebSocketEndpoints(this WebApplication app)
        {
            // WebSocket endpoint for real-time metrics streaming
            app.Map("/ws/metrics", async (HttpContext context, IMetricsService metricService) =>
            {
                // Verify this is a WebSocket request
                if(!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("WebSocket connection required");
                    return;
                }

                // Accept the WebSocket connection
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                // Create handler and stream metrics
                using var handler = new MetricsWebSocketHandler(webSocket);

                // Get the metric stream and pass to handler
                var metricStream = metricService.GetMetricsStreamAsync(context.RequestAborted);

                // Stream until client disconnects or cancellation
                await handler.StreamMetricsAsync(metricStream, context.RequestAborted);
            });

            return app;
        }

        private static WebApplication MapQueryEndpoints(this WebApplication app)
        {
            app.MapGet("/api/query", async (string q, IQueryExecutor executor, CancellationToken ct) =>
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
            })
            .WithName("GetQuery")
            .WithTags("Query")
            .Produces<QueryResponse>(200);

            app.MapPost("/api/query", async (QueryRequest request, IQueryExecutor executor, CancellationToken ct) =>
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
            })
            .WithName("PostQuery")
            .WithTags("Query")
            .Produces<QueryResponse>(200);

            return app;
        }
    }
}
