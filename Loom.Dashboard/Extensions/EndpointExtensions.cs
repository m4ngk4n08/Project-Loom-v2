using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Loom.Dashboard;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Telemetry.Alerting;
using Loom.Telemetry.Alerting.Interfaces;
using Loom.Telemetry.Exporters;
using Loom.Telemetry.Exporters.Prometheus;
using Loom.Telemetry.Query;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Loom.Web.RealTime;
using Microsoft.Extensions.FileProviders;

namespace Loom.Dashboard.Extensions
{
    public static class EndpointExtensions
    {
        public static WebApplication MapDashboardEndpoints(
            this WebApplication app,
            int targetPid,
            DateTime sessionStartedAtUtc,
            IFileProvider? embeddedProvider,
            MetricsResponseBuilder metricsBuilder)
        {
            var api = app.MapGroup("/api");

            api.MapHealthEndpoint();
            api.MapSessionEndpoint(targetPid, sessionStartedAtUtc);
            api.MapMetricsEndpoints(metricsBuilder);
            api.MapMetricIngestEndpoint();
            api.MapQueryEndpoints();
            api.MapLogEndpoints();
            api.MapAlertEndpoints();
            api.MapExporterEndpoints();

            app.MapPrometheusEndpoint();
            app.MapWebSocketEndpoint(metricsBuilder);
            app.MapLogsWebSocketEndpoint();
            app.MapSpaFallback(embeddedProvider);

            return app;
        }

        private static RouteGroupBuilder MapHealthEndpoint(this RouteGroupBuilder api)
        {
            api.MapGet("/health", () => Results.Json(new HealthCheckResponse
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                UptimeSeconds = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
                MemoryUsageMb = Process.GetCurrentProcess().WorkingSet64 / 1_048_576.0
            }, LoomJsonSerializerContext.Default.HealthCheckResponse));

            return api;
        }

        private static RouteGroupBuilder MapSessionEndpoint(this RouteGroupBuilder api, int targetPid, DateTime sessionStartedAtUtc)
        {
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

            return api;
        }

        private static RouteGroupBuilder MapMetricsEndpoints(this RouteGroupBuilder api, MetricsResponseBuilder metricsBuilder)
        {
            api.MapGet("/metrics/cpu", (IMetricStore store) =>
                Results.Json(metricsBuilder.BuildCpuResponse(store), LoomJsonSerializerContext.Default.CpuMetricResponse));

            api.MapGet("/metrics/memory", (IMetricStore store) =>
                Results.Json(metricsBuilder.BuildMemoryResponse(store), LoomJsonSerializerContext.Default.MemoryMetricResponse));

            api.MapGet("/metrics/thread", (IMetricStore store) =>
                Results.Json(MetricsResponseBuilder.BuildThreadResponse(store), LoomJsonSerializerContext.Default.ThreadMetricResponse));

            return api;
        }

        private static RouteGroupBuilder MapMetricIngestEndpoint(this RouteGroupBuilder api)
        {
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
                        return Results.Json(
                            new ErrorResponse { Error = $"Unknown metric type: {metric.Type}. Must be Counter, Gauge, or Histogram." },
                            LoomJsonSerializerContext.Default.ErrorResponse,
                            statusCode: 400);

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

            return api;
        }

        private static RouteGroupBuilder MapQueryEndpoints(this RouteGroupBuilder api)
        {
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
                    return Results.Json(
                        new QueryErrorResponse { Error = ex.Message },
                        LoomJsonSerializerContext.Default.QueryErrorResponse,
                        statusCode: 400);
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
                    return Results.Json(
                        new QueryErrorResponse { Error = ex.Message },
                        LoomJsonSerializerContext.Default.QueryErrorResponse,
                        statusCode: 400);
                }
            });

            return api;
        }

        private static RouteGroupBuilder MapLogEndpoints(this RouteGroupBuilder api)
        {
            api.MapGet("/logs", (int? count, string? category, ILogStore store) =>
            {
                var clampedCount = Math.Clamp(count ?? 100, 1, 1000);
                var records = category is null
                    ? store.ReadRecent(clampedCount)
                    : store.ReadRecent(category, clampedCount);

                return Results.Json(records.Select(ToDto).ToArray(), LoomJsonSerializerContext.Default.LogEntryDtoArray);
            })
            .WithName("GetLogs")
            .Produces<LogEntryDto[]>(200);

            api.MapGet("/logs/categories", (ILogStore store) =>
                Results.Json(store.GetCategories().ToList(), LoomJsonSerializerContext.Default.ListString))
            .WithName("GetLogCategories")
            .Produces<List<string>>(200);

            api.MapGet("/logs/tail", (long? after, int? count, ILogStore store) =>
            {
                var afterSequence = after ?? 0;
                var clampedCount = Math.Clamp(count ?? 100, 1, 1000);
                var result = store.ReadAfter(afterSequence);

                // ReadAfter can hand back up to the buffer's whole capacity in one
                // page; clamp the response, but the cursor MUST advance only past
                // what was actually returned - if we trimmed to clampedCount but
                // still reported result.NextSequence (the buffer's true head), the
                // client would skip every record between the trim point and the
                // head on its next poll. Same class of bug as the ReadSince ">" vs
                // ">=" cursor bug this whole fix started from. The cursor must also
                // advance past whatever ReadAfter itself skipped (DroppedCount) -
                // otherwise, when the caller's cursor has fallen out of the live
                // window, the returned records start higher than afterSequence + 1
                // and the next poll re-delivers records the client already got.
                var entries = result.Records.Length > clampedCount
                    ? result.Records[..clampedCount]
                    : result.Records;
                var nextSequence = afterSequence + result.DroppedCount + entries.Length;

                return Results.Json(new LogTailResponse
                {
                    Entries = entries.Select(ToDto).ToArray(),
                    NextSequence = nextSequence,
                    DroppedCount = result.DroppedCount
                }, LoomJsonSerializerContext.Default.LogTailResponse);
            })
            .WithName("GetLogTail")
            .Produces<LogTailResponse>(200);

            api.MapGet("/logs/export", (
                string? format, string? category, LoomLogLevel? minLevel,
                DateTime? from, DateTime? to, int? limit, ILogStore store) =>
            {
                var clampedLimit = Math.Clamp(limit ?? 1000, 1, 10_000);
                var filter = new LogQueryFilter(
                    ToUtcTicks(from), ToUtcTicks(to),
                    category, minLevel, clampedLimit);
                var records = store.Query(filter);

                return (format?.ToLowerInvariant()) switch
                {
                    "csv" => WriteCsvExport(records),
                    "text" => WriteTextExport(records),
                    _ => Results.Json(records.Select(ToDto).ToArray(),
                                        LoomJsonSerializerContext.Default.LogEntryDtoArray)
                };
            })
            .WithName("ExportLogs")
            .Produces(200);

            api.MapPost("/logs/search", (DiagnosticSearchRequest request, ILogStore store) =>
            {
                var clampedMaxResults = Math.Clamp(request.MaxResults, 1, 100);
                var corpus = store.Query(new LogQueryFilter(null, null, null, null, 10_000));

                var started = Stopwatch.GetTimestamp();
                var results = Bm25LogSearch.Search(corpus, request.Query, clampedMaxResults, request.MinScore);
                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                return Results.Json(new DiagnosticSearchResponse
                {
                    Query = request.Query,
                    TotalResults = results.Length,
                    SearchTimeMs = elapsedMs,
                    Results = results
                }, LoomJsonSerializerContext.Default.DiagnosticSearchResponse);
            })
            .WithName("SearchLogs")
            .Produces<DiagnosticSearchResponse>(200);

            return api;
        }

        // A query value carrying no timezone designator binds as Unspecified, and
        // ToUniversalTime() would then apply the SERVER's offset - making the export window
        // depend on where the process is deployed. Naive input is documented as already-UTC.
        internal static long? ToUtcTicks(DateTime? value) => value is null
            ? null
            : (value.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                : value.Value.ToUniversalTime()).Ticks;

        internal static IResult WriteCsvExport(LogRecord[] records)
        {
            var sb = new StringBuilder();
            // Appended, never inserted. The header is a positional contract - a consumer
            // reading row[4] for Message must keep getting Message. SpanId is
            // deliberately omitted: a flat log export is joined and pivoted on trace id
            // and template, and every column costs width forever.
            sb.Append("Timestamp,Level,Category,EventId,Message,ExceptionType,ExceptionMessage,TraceId,Template\r\n");
            foreach (var record in records)
            {
                sb.Append(CsvField(record.TimestampUtc.ToString("O"))).Append(',')
                  .Append(CsvField(record.Level.ToString())).Append(',')
                  .Append(CsvField(record.Category)).Append(',')
                  .Append(CsvField(record.EventId.ToString())).Append(',')
                  .Append(CsvField(record.Message)).Append(',')
                  .Append(CsvField(record.ExceptionType ?? string.Empty)).Append(',')
                  .Append(CsvField(record.ExceptionMessage ?? string.Empty)).Append(',')
                  .Append(CsvField(LogMessageParser.FormatTraceId(record.TraceIdHi, record.TraceIdLo) ?? string.Empty)).Append(',')
                  .Append(CsvField(record.Template ?? string.Empty))
                  .Append("\r\n");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"loom-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return Results.File(bytes, "text/csv", fileName);
        }

        internal static IResult WriteTextExport(LogRecord[] records)
        {
            var text = string.Join('\n', records.Select(r => r.ToString()));
            var bytes = Encoding.UTF8.GetBytes(text);
            var fileName = $"loom-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt";
            return Results.File(bytes, "text/plain", fileName);
        }

        // RFC 4180: a field must be quoted if it contains a comma, a double-quote, or a
        // line break; an embedded double-quote is escaped by doubling it. LogRecord.Message
        // is free text from real exceptions/stack traces and WILL contain all three.
        internal static string CsvField(string value)
        {
            var needsQuoting = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
            if (!needsQuoting) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        internal static LogEntryDto ToDto(LogRecord record) => new()
        {
            Message = record.Message,
            Category = record.Category,
            Level = record.Level.ToString(),
            TimestampUtc = record.TimestampUtc,
            EventId = record.EventId,
            ExceptionType = record.ExceptionType,
            ExceptionMessage = record.ExceptionMessage,
            Template = record.Template,
            ArgumentsJson = record.ArgumentsJson,
            // FormatTraceId/FormatSpanId return null rather than a string of zeros for
            // an absent id. Paired with DefaultIgnoreCondition.WhenWritingNull on the
            // serializer context, an untraced line emits no traceId key at all.
            TraceId = LogMessageParser.FormatTraceId(record.TraceIdHi, record.TraceIdLo),
            SpanId = LogMessageParser.FormatSpanId(record.SpanId)
        };

        private static RouteGroupBuilder MapAlertEndpoints(this RouteGroupBuilder api)
        {
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

            return api;
        }

        internal static List<ExporterStatusDto> BuildExporterStatuses(ExportStatusTracker tracker) =>
            tracker.GetStatuses().Values
                .Select(s => new ExporterStatusDto
                {
                    Name = s.Name,
                    IsHealthy = s.IsHealthy,
                    LastSuccessUtc = s.LastSuccessUtc,
                    LastFailureUtc = s.LastFailureUtc,
                    LastError = s.LastError,
                    TotalExports = s.TotalExports,
                    TotalFailures = s.TotalFailures
                })
                .ToList();

        private static RouteGroupBuilder MapExporterEndpoints(this RouteGroupBuilder api)
        {
            api.MapGet("/exporters/status", (ExportStatusTracker tracker) =>
                Results.Json(BuildExporterStatuses(tracker), LoomJsonSerializerContext.Default.ListExporterStatusDto));

            api.MapGet("/exporters/metrics/names", (IMetricStore store) =>
                Results.Json(store.GetMetricNames().ToList(), LoomJsonSerializerContext.Default.ListString));

            api.MapGet("/exporters/metrics/summary", (IMetricStore store) =>
                Results.Json(MetricSummaryBuilder.BuildAll(store), LoomJsonSerializerContext.Default.ListMetricSummaryDto));

            return api;
        }

        private static WebApplication MapPrometheusEndpoint(this WebApplication app)
        {
            // Moved to /prometheus to avoid conflict with Angular's /metrics route.
            app.MapGet("/prometheus", async (HttpContext context, IMetricStore store, CancellationToken ct) =>
            {
                context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                // HttpResponse.BodyWriter is a PipeWriter, which implements
                // IBufferWriter<byte> - write straight to it, no intermediate string.
                PrometheusFormatter.Format(store, context.Response.BodyWriter);
                await context.Response.BodyWriter.FlushAsync(ct);
            });

            return app;
        }

        private static WebApplication MapWebSocketEndpoint(this WebApplication app, MetricsResponseBuilder metricsBuilder)
        {
            // Streams polymorphic MetricUpdate messages to the frontend.
            app.Map("/ws/metrics", async (HttpContext context, IMetricStore store) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                using var handler = new MetricsWebSocketHandler(webSocket);
                await handler.StreamMetricsAsync(
                    metricsBuilder.GetMetricsStreamAsync(store, context.RequestAborted),
                    context.RequestAborted);
            });

            return app;
        }

        private static WebApplication MapLogsWebSocketEndpoint(this WebApplication app)
        {
            // Logs are push-based (ILogStore.Subscribe), unlike metrics which are polled -
            // no Task.Delay loop needed here.
            app.Map("/ws/logs", async (HttpContext context, ILogStore store) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                using var handler = new MetricsWebSocketHandler(webSocket);
                await handler.StreamAsync(
                    ReadLogStreamAsync(store, context.RequestAborted),
                    LoomJsonSerializerContext.Default.LogEntryDto,
                    context.RequestAborted);
            });

            return app;
        }

        private static async IAsyncEnumerable<LogEntryDto> ReadLogStreamAsync(
            ILogStore store,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var reader = store.Subscribe();
            try
            {
                await foreach (var record in reader.ReadAllAsync(ct))
                {
                    yield return ToDto(record);
                }
            }
            finally
            {
                store.Unsubscribe(reader);
            }
        }

        private static WebApplication MapSpaFallback(this WebApplication app, IFileProvider? embeddedProvider)
        {
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

            return app;
        }
    }
}
