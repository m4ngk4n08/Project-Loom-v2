using Loom.Web.Api.Interfaces;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Loom.Web.RealTime;
using Loom.Telemetry.Query;
using Loom.Telemetry.Alerting;
using System.Diagnostics;
using System.Threading.Channels;

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
            app.MapAlertEndpoints();
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

        private static WebApplication MapAlertEndpoints(this WebApplication app)
        {
            var alertGroup = app.MapGroup("/api/alerts")
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

            return app;
        }
    }
}
