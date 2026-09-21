using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Loom.Dashboard;
using Loom.Security;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Telemetry.Alerting;
using Loom.Telemetry.Alerting.Interfaces;
using Loom.Telemetry.Exporters;
using Loom.Telemetry.Exporters.Prometheus;
using Loom.Telemetry.Query;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Loom.Web.Contracts.Explain;
using Loom.Web.RealTime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
            MetricsResponseBuilder metricsBuilder,
            bool mapFallback = true)
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

            if (mapFallback)
            {
                app.MapSpaFallback(embeddedProvider);
            }

            return app;
        }

        // Static, allocation-free response headers, ported from Loom.Web.Api/Program.cs:83-85
        // (originally inlined again in Loom.Dashboard/Program.cs; moved here so a host
        // embedding this library gets the same protection without copying the block itself -
        // BACKLOG.md § 6.29). Must be callable BEFORE UseStaticFiles and UseRouting: the whole
        // point is a short-circuited static-file response still carries the headers. Does not
        // depend on anything UseLoomDashboard()/MapLoomDashboard() sets up.
        //
        // The CSP is NOT Web.Api's. That host served JSON only, so "default-src 'none'" was
        // correct there and would render this one blank. This policy is written against what the
        // production Angular bundle actually emits, verified by reading dist/.../index.html:
        //   script-src 'self'  - the bundle is one external <script type="module"> plus
        //                        modulepreload links. No inline script and no inline event
        //                        handler, which holds only because critical-CSS inlining is
        //                        turned off in angular.json (it emitted a <style> block and an
        //                        onload= attribute). Re-enabling it breaks this line.
        //   style-src adds 'unsafe-inline' - Angular injects component styles as <style>
        //                        elements at runtime. Removing it needs a per-request nonce
        //                        (ngCspNonce), which means generating index.html per request
        //                        instead of serving it statically. Not worth it on a loopback
        //                        host; revisit if this is ever fronted by a proxy.
        //   connect-src 'self'  - covers the REST API and, per CSP3, same-origin ws:// too.
        //   img-src adds data:  - chart canvases export to data URIs.
        // Everything else is denied: no plugins, no framing, no form posts, no <base> rewrite.
        private const string ContentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'none'; " +
            "frame-ancestors 'none'";

        // Prefixes this library actually maps, per MapDashboardEndpoints/MapLoomTokenEndpoints/
        // MapPrometheusEndpoint/MapWebSocketEndpoint/MapLogsWebSocketEndpoint below and
        // Loom.Security/TokenEndpoints.cs. Checked with StartsWithSegments (path-segment aware,
        // so "/apix" does not match "/api"), matching the existing style at MapSpaFallback.
        // Routing has not run yet at this middleware's position (it must stay ahead of
        // UseRouting/UseStaticFiles - see the doc comment below), so this is the only signal
        // available to tell a Loom request from a host's own.
        private static readonly string[] LoomPathPrefixes =
        [
            "/api",
            "/ws/metrics",
            "/ws/logs",
            "/prometheus"
        ];

        private static bool IsLoomRequestPath(PathString path)
        {
            foreach (var prefix in LoomPathPrefixes)
            {
                if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // Shared by UseLoomDashboardSecurityHeaders's middleware and MapSpaFallback's handler so
        // the header set and the CSP string live in exactly one place.
        private static void ApplyLoomSecurityHeaders(IHeaderDictionary headers)
        {
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
        }

        // Consumer-facing: a host embedding this library calls this ahead of UseStaticFiles
        // (and UseWebSockets/UseRouting) to get the same CSP/frame/sniff protection the
        // loom-dashboard tool applies to itself. Independent of UseLoomDashboard/MapLoomDashboard
        // on purpose - a host may want these headers even if it never maps Loom's endpoints.
        //
        // Scoped to Loom's own paths only: /api (covers every endpoint under MapDashboardEndpoints
        // plus the two token endpoints in Loom.Security/TokenEndpoints.cs), /ws/metrics, /ws/logs,
        // and /prometheus. A host's own routes are never touched - a page the host serves that
        // relies on inline scripts or framing is unaffected by this call. Routing has not run yet
        // at this pipeline position, so the check is on the raw request path, not endpoint
        // metadata. The SPA fallback response (MapSpaFallback) is not reachable through this
        // prefix check - its path is whatever went unmatched - so it sets the same four headers
        // directly in its own handler via ApplyLoomSecurityHeaders.
        //
        // Does NOT cover static files served by a separately-registered UseStaticFiles call,
        // even Loom's own embedded Angular bundle - that middleware can short-circuit the
        // request before this prefix check would ever see a matching path (the bundle is mounted
        // at the app root with content-hashed filenames, none of which are in LoomPathPrefixes).
        // A host serving Loom's embedded UI - or its own static content it wants protected the
        // same way - must register that specific mount through
        // UseLoomDashboardStaticAssets(provider) below instead of a bare
        // app.UseStaticFiles(...). A host's other, unrelated static-file mounts are correctly
        // left untouched by both methods - that's the design, not a gap.
        public static WebApplication UseLoomDashboardSecurityHeaders(this WebApplication app)
        {
            app.Use(async (context, next) =>
            {
                if (IsLoomRequestPath(context.Request.Path))
                {
                    ApplyLoomSecurityHeaders(context.Response.Headers);
                }
                await next();
            });
            return app;
        }

        // Serves one specific static-file mount with Loom's security headers. This exists
        // because the file paths served this way (an Angular build's content-hashed bundle
        // filenames, e.g. main-XXXX.js, plus index.html) can't be expressed as a static prefix
        // list the way Loom's own fixed API/WS/prometheus routes can: the actual set of paths
        // depends on what this specific IFileProvider's build output happens to contain, which
        // is only knowable by reading its manifest at runtime, not by guessing filenames ahead of
        // time. Rather than recognizing these responses by a static path list, this checks
        // fileProvider.GetFileInfo(...).Exists directly - the same information UseStaticFiles
        // itself will use to decide whether to serve the request - and only applies the headers
        // when that specific provider actually has a file at the request path.
        //
        // This is a file-existence check, not a pipeline-position check: a request path that
        // doesn't resolve to a file in this fileProvider gets no headers here, regardless of
        // whether it 404s or falls through to a host route mapped later via UseRouting/Map*
        // (which, per the ordering rules above, is always registered after this call). A host's
        // own routes and its own, separately-registered static-file mounts are therefore never
        // touched by this method, regardless of registration order relative to it.
        public static WebApplication UseLoomDashboardStaticAssets(this WebApplication app, IFileProvider fileProvider)
        {
            app.Use(async (context, next) =>
            {
                var relativePath = context.Request.Path.Value?.TrimStart('/') ?? string.Empty;
                if (relativePath.Length > 0
                    && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
                {
                    var fileInfo = fileProvider.GetFileInfo(relativePath);
                    if (fileInfo.Exists && !fileInfo.IsDirectory)
                    {
                        ApplyLoomSecurityHeaders(context.Response.Headers);
                    }
                }
                await next();
            });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
            return app;
        }

        private const string AuthenticationRegisteredKey = "Loom.Dashboard.AuthenticationRegistered";

        // Installs the auth middleware and records that it happened, so MapLoomDashboard can
        // refuse to map an unprotected dashboard. Does NOT call UseRouting() - the host owns
        // pipeline order and must call it first; see UseLoomAuthentication's doc comment.
        public static WebApplication UseLoomDashboard(this WebApplication app)
        {
            app.UseLoomAuthentication();
            ((IApplicationBuilder)app).Properties[AuthenticationRegisteredKey] = true;
            return app;
        }

        // Consumer-facing entry point: resolves MetricsResponseBuilder from DI and refuses to
        // map anything until UseLoomDashboard has installed the auth middleware - without it,
        // the LoomAllowAnonymous markers below are inert and every endpoint would be served to
        // anonymous callers.
        //
        // mapFallback (default true, matching prior behavior): whether to register the anonymous
        // root-level MapFallback (serves index.html / a "build Angular and repack" 404 for
        // anything under /api - see MapSpaFallback). Pass false when the host already maps its
        // own SPA/catch-all fallback route (e.g. app.MapFallbackToFile("index.html")) - two
        // MapFallback registrations have equal route precedence, and ASP.NET Core throws
        // AmbiguousMatchException on every unmatched request when both are present
        // (BACKLOG.md § 6.30).
        public static WebApplication MapLoomDashboard(
            this WebApplication app,
            int targetPid,
            IFileProvider? embeddedProvider = null,
            DateTime? sessionStartedAtUtc = null,
            bool mapTokenEndpoints = true,
            bool mapFallback = true)
        {
            if (!((IApplicationBuilder)app).Properties.ContainsKey(AuthenticationRegisteredKey))
            {
                throw new InvalidOperationException(
                    "Call app.UseLoomDashboard() before app.MapLoomDashboard(). Without it no Loom " +
                    "endpoint is authenticated: the LoomAllowAnonymous markers are only read by the " +
                    "UseLoomAuthentication middleware, so every metric, log, query and alert endpoint " +
                    "would be served to anonymous callers.");
            }

            var metricsBuilder = app.Services.GetRequiredService<MetricsResponseBuilder>();
            if (mapTokenEndpoints)
            {
                app.MapLoomTokenEndpoints();
            }
            app.MapDashboardEndpoints(targetPid, sessionStartedAtUtc ?? DateTime.UtcNow, embeddedProvider, metricsBuilder, mapFallback);
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
            }, LoomJsonSerializerContext.Default.HealthCheckResponse))
            .WithMetadata(new LoomAllowAnonymous());

            return api;
        }

        private static RouteGroupBuilder MapSessionEndpoint(this RouteGroupBuilder api, int targetPid, DateTime sessionStartedAtUtc)
        {
            api.MapGet("/session", (IMetricStore store) =>
            {
                var processName = ResolveProcessName(targetPid);
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

        // Process.GetProcessById throws ArgumentException for a PID with no running
        // process - it never returns null - so the old `?.ProcessName ?? ...` pattern
        // never actually ran its fallback and instead 500'd /api/session once the
        // target exited. InvalidOperationException is also caught: the process can exit
        // between the successful lookup and reading ProcessName.
        internal static string ResolveProcessName(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.ProcessName;
            }
            catch (ArgumentException)
            {
                return $"pid-{pid} (exited)";
            }
            catch (InvalidOperationException)
            {
                return $"pid-{pid} (exited)";
            }
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

        // internal rather than private so MetricIngestEndpointTests can map just this group
        // onto a bare WebApplication, same rationale as MapAlertEndpoints/MapLogEndpoints.
        internal static RouteGroupBuilder MapMetricIngestEndpoint(this RouteGroupBuilder api)
        {
            api.MapPost("/metrics/ingest", (MetricIngestRequest request, IMetricStore store) =>
            {
                if (request.Metrics is null)
                    return Results.Json(
                        new ErrorResponse { Error = "Metrics is required." },
                        LoomJsonSerializerContext.Default.ErrorResponse,
                        statusCode: 400);

                // Pass 1: validate every metric and build the records to write. On the
                // first invalid entry, return 400 with nothing written - a batch must not
                // partially commit.
                var records = new MetricRecord[request.Metrics.Length];
                for (var i = 0; i < request.Metrics.Length; i++)
                {
                    var metric = request.Metrics[i];

                    if (metric is null)
                        return Results.Json(
                            new ErrorResponse { Error = $"Metric at index {i} is null." },
                            LoomJsonSerializerContext.Default.ErrorResponse,
                            statusCode: 400);

                    if (string.IsNullOrEmpty(metric.Name))
                        return Results.Json(
                            new ErrorResponse { Error = "Metric name is required." },
                            LoomJsonSerializerContext.Default.ErrorResponse,
                            statusCode: 400);

                    // JSON `null` passes `required` validation, so metric.Type can be null
                    // here even though the DTO declares it non-nullable.
                    if (metric.Type is null)
                        return Results.Json(
                            new ErrorResponse { Error = "Metric type is required. Must be Counter, Gauge, or Histogram." },
                            LoomJsonSerializerContext.Default.ErrorResponse,
                            statusCode: 400);

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

                    MetricTag[] tags;
                    if (metric.Tags is null)
                    {
                        tags = Array.Empty<MetricTag>();
                    }
                    else
                    {
                        tags = new MetricTag[metric.Tags.Count];
                        var tagIndex = 0;
                        foreach (var kvp in metric.Tags)
                        {
                            if (kvp.Value is null)
                                return Results.Json(
                                    new ErrorResponse { Error = $"Tag '{kvp.Key}' on metric '{metric.Name}' has a null value." },
                                    LoomJsonSerializerContext.Default.ErrorResponse,
                                    statusCode: 400);

                            tags[tagIndex++] = new MetricTag(kvp.Key, kvp.Value);
                        }
                    }

                    var timestampUtcTicks = ToUtcTicks(metric.Timestamp) ?? DateTime.UtcNow.Ticks;

                    records[i] = new MetricRecord(
                        metric.Name,
                        type.Value,
                        metric.Value,
                        timestampUtcTicks,
                        tags.Length > 0 ? tags : null
                    );
                }

                // Pass 2: everything validated, write them all.
                foreach (var record in records)
                {
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

        internal static RouteGroupBuilder MapLogEndpoints(this RouteGroupBuilder api)
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
                var currentSequence = store.CurrentSequence;
                // A cursor ahead of the buffer (kept across a dashboard restart) or negative
                // must not be echoed back to the caller.
                var afterSequence = Math.Clamp(after ?? 0, 0, currentSequence);
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

            // Mapped only when the host has registered an IExplainClient. This library has no
            // Anthropic (or any provider) reference at all - the host decides whether the
            // feature exists by what it puts in the container, and this checks the built
            // service provider directly rather than any provider-specific configuration.
            // An unconfigured deployment returns 404 from the router rather than 501 from a
            // handler - there is no endpoint, not a disabled one.
            // IsService checks registration without constructing: resolving here would build
            // a client at startup, and a scoped registration throws from the root provider
            // under Development's scope validation.
            if (((IEndpointRouteBuilder)api).ServiceProvider.GetRequiredService<IServiceProviderIsService>()
                    .IsService(typeof(IExplainClient)))
            {
                api.MapPost("/logs/explain", async (
                    ExplainRequest request,
                    IExplainClient client,
                    HttpContext context) =>
                {
                    var payload = ExplainPayloadBuilder.Build(
                        request.Template, request.ArgumentsJson,
                        request.Category, request.Level, request.ExceptionType);

                    if (payload is null)
                        return Results.BadRequest("A message template is required to explain an entry.");

                    ExplainResult result;
                    try
                    {
                        result = await client.ExplainAsync(payload, context.RequestAborted);
                    }
                    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                    {
                        // Client went away; nothing to answer.
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        return Results.Json(
                            new ErrorResponse { Error = "The explain provider timed out." },
                            LoomJsonSerializerContext.Default.ErrorResponse,
                            statusCode: 504);
                    }
                    catch (HttpRequestException)
                    {
                        // Never echo ex.Message here - it can carry host/URL detail.
                        return Results.Json(
                            new ErrorResponse { Error = "The explain provider could not be reached." },
                            LoomJsonSerializerContext.Default.ErrorResponse,
                            statusCode: 502);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Authored in AnthropicExplainClient; safe to show, never contains the key.
                        return Results.Json(
                            new ErrorResponse { Error = ex.Message },
                            LoomJsonSerializerContext.Default.ErrorResponse,
                            statusCode: 502);
                    }
                    catch (JsonException)
                    {
                        // A 200 whose body is not the provider's JSON (proxy, captive portal).
                        // Never echo ex.Message - it can quote the body.
                        return Results.Json(
                            new ErrorResponse { Error = "The explain provider returned an unreadable response." },
                            LoomJsonSerializerContext.Default.ErrorResponse,
                            statusCode: 502);
                    }

                    return Results.Json(new ExplainResponse
                    {
                        Explanation = result.Explanation,
                        ModelUsed = result.ModelUsed,
                        SentText = result.SentText,
                        InputTokens = result.InputTokens,
                        OutputTokens = result.OutputTokens
                    }, LoomJsonSerializerContext.Default.ExplainResponse);
                })
                .WithName("ExplainLogEntry")
                .Produces<ExplainResponse>(200)
                .Produces(502)
                .Produces(504);
            }

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
                  .Append(CsvField(W3CTraceId.FormatTraceId(record.TraceIdHi, record.TraceIdLo) ?? string.Empty)).Append(',')
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
            TraceId = W3CTraceId.FormatTraceId(record.TraceIdHi, record.TraceIdLo),
            SpanId = W3CTraceId.FormatSpanId(record.SpanId)
        };

        // internal rather than private so AlertEndpointTests can map just this group onto a
        // bare WebApplication. Going through MapLoomDashboard instead would drag in the whole
        // service graph and the security bootstrap, which needs key material CI does not have.
        internal static RouteGroupBuilder MapAlertEndpoints(this RouteGroupBuilder api)
        {
            var alertGroup = api.MapGroup("/alerts")
                .WithTags("Alerts");

            alertGroup.MapGet("", (IAlertRuleRegistry registry) =>
            {
                var rules = registry.Snapshot()
                    .Select(r => new AlertConfigDto { Name = r.Name, MetricName = r.MetricName, Window = r.Window })
                    .ToList();
                return Results.Json(rules, LoomJsonSerializerContext.Default.ListAlertConfigDto);
            })
            .WithName("GetAlerts")
            .Produces<List<AlertConfigDto>>(200);

            alertGroup.MapGet("/{name}", (string name, IAlertRuleRegistry registry) =>
            {
                var rule = registry.Snapshot().FirstOrDefault(r => r.Name == name);
                if (rule is null) return Results.NotFound();

                return Results.Json(
                    new AlertConfigDto { Name = rule.Name, MetricName = rule.MetricName, Window = rule.Window },
                    LoomJsonSerializerContext.Default.AlertConfigDto);
            })
            .WithName("GetAlert")
            .Produces<AlertConfigDto>(200)
            .Produces(404);

            alertGroup.MapPost("/{name}/test", async (string name, IAlertRuleRegistry registry, Channel<AlertNotification> channel) =>
            {
                var rule = registry.Snapshot().FirstOrDefault(r => r.Name == name);
                if (rule is null) return Results.NotFound();

                var testAggregate = new MetricAggregate(rule.MetricName, Count: 1, Average: 0, Max: 0, P99: 0);
                await channel.Writer.WriteAsync(new AlertNotification(rule, testAggregate, DateTime.UtcNow));
                return Results.Accepted();
            })
            .WithName("TestAlert")
            .Produces(202)
            .Produces(404);

            alertGroup.MapPut("/{name}/silence", (string name, TimeSpan duration, IAlertRuleRegistry registry, ISilenceStore silenceStore) =>
            {
                var rule = registry.Snapshot().FirstOrDefault(r => r.Name == name);
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
            }).WithMetadata(new LoomMetricsScopeAllowed());

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

                using var webSocket = await context.WebSockets.AcceptWebSocketAsync(AuthenticationMiddleware.WebSocketSubprotocol);
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

                using var webSocket = await context.WebSockets.AcceptWebSocketAsync(AuthenticationMiddleware.WebSocketSubprotocol);
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

        internal static WebApplication MapSpaFallback(this WebApplication app, IFileProvider? embeddedProvider)
        {
            app.MapFallback(async context =>
            {
                // This handler's own response - a 404 for an unmatched /api path, the SPA's
                // index.html, or a "build Angular" 404 - is always Loom's own content, so it
                // always gets the same headers. Its path is whatever went unmatched (could be
                // "/", could be anything a host also leaves unmatched), so it cannot rely on
                // IsLoomRequestPath the way UseLoomDashboardSecurityHeaders does.
                ApplyLoomSecurityHeaders(context.Response.Headers);

                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 404;
                    return;
                }

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
            }).WithMetadata(new LoomAllowAnonymous());

            return app;
        }
    }
}
