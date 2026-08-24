# Project Loom v2

A **customizable telemetry platform** for .NET applications. Loom provides live insight into CPU hotpaths, memory allocations, thread blockages, and — critically — **your own business metrics**, with zero-allocation instrumentation powered by C# source generators.

The successor to the original SSH-based design, Loom v2 is a .NET-native observability stack that ships as a single AOT-compiled binary.

---

## Overview

Loom is not just a profiler. It's a **telemetry platform** you embed into .NET applications:

- **Custom Metrics** — `RecordMetric`, `RecordCounter`, `RecordGauge`, `RecordHistogram` with tag/dimension support
- **Attribute-Based Instrumentation** — `[LoomProfile]` and `[LoomTrack]` auto-instrument methods at compile time
- **Custom Collectors** — `ILoomCollector` plugin interface for third-party integrations (Redis, RabbitMQ, etc.)
- **Query Language** — SQL-like telemetry queries + fluent code-based `Query()` API
- **Alerting** — `AddAlert()` with window-based conditions, webhook/email notifications
- **Exporters** — Prometheus, Console interoperability
- **Source Generator** — zero-allocation instrumentation resolved entirely at compile time (no reflection)
- **Sampling** — configuration-driven sampling rules (path-based, duration-based)
- **Local Dev Mode** — `dotnet loom dev` for zero-config live metrics during development

All built on .NET 10 Native AOT with zero runtime reflection.

---

## Key Constraint: Native AOT

Everything is built around **.NET 10 Native AOT** (reflection-free) compilation:

| Constraint | Why |
|-----------|-----|
| Binary size **< 17 MB** | Single-binary deployment (see `BACKLOG.md` § 2.1) |
| Memory footprint **< 20 MB** background | Minimal overhead on host app |
| **No reflection** | AOT can't do runtime codegen |
| **Zero-allocation hot paths** | `Span<T>`, `ValueTask`, `ArrayPool<T>` |
| **Source-generated JSON** | All DTOs registered in `LoomJsonSerializerContext` |
| **Minimal APIs only** | No MVC controllers (reflection-heavy) |
| **Raw WebSockets, no SignalR** | SignalR uses reflection at runtime |
| **Manual JWT** | No `System.IdentityModel.Tokens.Jwt` (reflection-heavy) |

---

## Technology Stack

**Backend**
- .NET 10 SDK (10.0.100+)
- ASP.NET Core **Minimal APIs** (Native AOT)
- Kestrel HTTP server
- System.Text.Json with **source generators**
- Raw **WebSockets** (native, not SignalR)
- Manual JWT authentication
- C# Source Generators (Roslyn analyzer project)

**Build Tools**
- MSVC v143 (Windows) / LLVM-Clang 19 (Linux) native compilation
- Node.js 20+ LTS (build tooling only)

---

## Project Structure

```
Loom.slnx
├── Loom.Web.Api/                  → ASP.NET Core Minimal APIs (Native AOT production host)
├── Loom.Web.Contracts/            → Shared DTOs + source-generated JSON (MANDATORY for AOT)
├── Loom.Web.RealTime/             → Zero-allocation WebSocket handlers
├── Loom.Storage/                  → Ring-buffer metric store (in-memory)
├── Loom.Telemetry/                → Custom Metrics API runtime (RecordMetric, LoomCollectors, LoomSampling, etc.)
├── Loom.Telemetry.Generators/     → C# source generator ([LoomProfile] → instrumented code)
├── Loom.Telemetry.Query/          → Query engine (SQL-like tokenizer/parser/planner/executor)
├── Loom.Telemetry.Alerting/       → Alert rules, window conditions, notification dispatch
├── Loom.Telemetry.Exporters/      → Prometheus, Console
├── Loom.DevTools/                 → `dotnet loom dev` CLI tool (local dev mode)
├── Loom.Telemetry.Tests/          → Unit & integration tests
└── Loom.Dashboard/                → `loom-dashboard <pid>` dev-time CLI tool; embeds the Angular dashboard, attaches to a target process via EventPipe
```

> `Loom.Host/`, `Loom.Core/`, `Loom.Benchmarks/` from earlier design docs were never
> implemented (empty scaffolding, since deleted). `Loom.Web.Api` is the real AOT host;
> `Loom.Storage`'s ring buffers superseded the planned SIMD engine. `Loom.Telemetry.Collectors`
> was folded directly into `Loom.Telemetry`.

**Dependency flow (verified `ProjectReference` edges):**
```
Loom.Web.Api           → Loom.Storage, Loom.Telemetry.Exporters, Loom.Telemetry.Query,
                          Loom.Telemetry.Alerting, Loom.Web.Contracts, Loom.Web.RealTime
Loom.Web.RealTime       → Loom.Web.Contracts
Loom.Storage            → Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry           (no project refs; DI abstractions package only)
Loom.Telemetry.Query    → Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry.Alerting → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query, Loom.Web.Contracts
Loom.Telemetry.Exporters→ Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.DevTools           → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query, Loom.Web.Contracts
Loom.Dashboard          → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                          Loom.Telemetry.Alerting, Loom.Telemetry.Exporters,
                          Loom.Web.Contracts, Loom.Web.RealTime

Loom.Telemetry.Generators (analyzer, referenced by consuming projects, no runtime dep)
```

`Loom.Web.Api` is the AOT production host; `Loom.Dashboard` is a separate dev-time CLI
(`PackAsTool`, exempt from AOT constraints) that embeds the Angular frontend and attaches
to a target PID via `EventPipeBridge.cs`. The two intentionally duplicate some endpoints —
they are not rivals.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100+)
- Native compilation tools (MSVC v143 on Windows / Clang 19 on Linux)

### Backend (development, fast iteration)

```bash
dotnet test Loom.slnx --configuration Debug
dotnet watch run --project Loom.Web.Api --no-hot-reload
```

The API listens on `http://localhost:5080` (HTTPS: `https://localhost:5443` in production).

### Local Dev Mode

```bash
dotnet loom dev
# Auto-discovers .NET apps on localhost
# Streams live metrics to console/JSON
```

---

## API Endpoints

### Infrastructure Metrics

| Endpoint | Description |
|----------|-------------|
| `GET /api/health` | Health check (status, uptime, memory) |
| `GET /api/session` | Session/attach metadata — **`Loom.Dashboard` only**, not served by `Loom.Web.Api` (see `Loom.Dashboard/Program.cs:106`) |
| `GET /api/metrics/cpu` | CPU hotpath metrics |
| `GET /api/metrics/memory` | Memory allocation & GC stats |
| `GET /api/metrics/thread` | Thread activity & blockage analysis |
| `WS /ws/metrics` | Real-time infrastructure metric stream (~10 Hz) |

### Custom Telemetry

| Endpoint | Description |
|----------|-------------|
| `POST /api/metrics/ingest` | Batch metric ingestion (Counter/Gauge/Histogram) |
| `GET /api/exporters/metrics/names` | List registered metric names |
| `GET /api/exporters/metrics/summary` | Summary stats for registered metrics |

### Prometheus scrape endpoint — route differs by host

The scrape route is intentionally **not** the same on both hosts:

| Host | Route | Why |
|------|-------|-----|
| `Loom.Web.Api` (production API, no SPA) | `GET /metrics` | No conflicting route to avoid |
| `Loom.Dashboard` (dev CLI, embeds the Angular SPA) | `GET /prometheus` | The Angular app owns a client-side `/metrics` page (`metrics-explorer`); its `MapFallback` serves `index.html` for unmatched routes, so mapping the scrape endpoint to `/metrics` here would shadow that route and break deep-link/refresh on the Angular page. See `Loom.Dashboard/Program.cs:261`. |

Do not "fix" this into a single shared route — the divergence is required by the SPA routing, not an oversight.

### Query

| Endpoint | Description |
|----------|-------------|
| `POST /api/query` | Execute SQL-like telemetry query (body: query string) |
| `GET /api/query?q=...` | Execute query via GET (URL-encoded) |

### Alerting

| Endpoint | Description |
|----------|-------------|
| `GET /api/alerts` | List configured alerts + current status |
| `GET /api/alerts/{name}` | Get specific alert status and history |
| `POST /api/alerts/{name}/test` | Trigger test notification |
| `PUT /api/alerts/{name}/silence` | Silence alert for duration |

### Exporters

| Endpoint | Description |
|----------|-------------|
| `GET /api/exporters/status` | Exporter health and throughput |
| `GET /api/exporters/metrics/names` | List registered metric names |
| `GET /api/exporters/metrics/summary` | Summary stats for registered metrics |

Prometheus scrape route: see "Prometheus scrape endpoint — route differs by host" above.

### Sampling

Collectors and sampling are **library-level APIs** (via `LoomMetrics`, `LoomSampling.Configure`, `LoomCollectors.Register`) — no HTTP endpoints are exposed for these.

---

## Native AOT Production Build

```bash
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj \
  --configuration Release \
  -r linux-x64 \
  /p:PublishAot=true \
  /p:StripSymbols=true

strip --strip-debug \
  Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api

# Verify the binary stays under 17 MB (see BACKLOG.md § 2.1 for why the target moved
# from 15 MB; 16.3 MB was the last measurement, taken before two exporters were deleted)
ls -lh Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api
```

---

## Verification Checklist

```bash
# 1. No trim/AOT warnings - scoped to the AOT target only. Running this against the
#    whole solution (Loom.slnx) forces trim analysis onto Loom.Dashboard too, which
#    deliberately has no IsAotCompatible (PackAsTool dev CLI on reflection-heavy
#    diagnostics libraries) and will produce spurious IL2026 errors that are not a
#    regression. Loom.Web.Api already sets EnableTrimAnalyzer in its own csproj.
dotnet build Loom.Web.Api/Loom.Web.Api.csproj -c Release /p:TreatWarningsAsErrors=true

# 2. Native AOT compiles
dotnet publish Loom.Web.Api -c Release -r linux-x64 /p:PublishAot=true

# 3. Binary size - the original <15 MB target covered only the Phases 0-4 diagnostic
#    core, before the query parser, alerting engine, 4 exporters, and collector plugin
#    system existed; that budget was retired (see BACKLOG.md §2.1). Last measured
#    win-x64 AOT publish: 16.57 MB. Windows AOT binaries also run larger than linux-x64
#    ones, so the two are not directly comparable - measure whichever RID you publish.
ls -lh Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api

# 4. IL + AOT tests pass
dotnet test Loom.slnx --configuration Debug

# 5. Zero allocations in hot paths
dotnet-counters monitor --process-id $(pidof Loom.Web.Api) System.Runtime
```

---

## Implementation Phases

### Foundation (Existing Infrastructure)

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 0 | Project Setup & Tooling | Partial | SDK, solution structure; scaffolded `Loom.Host`/`Loom.Core`/`Loom.Benchmarks` were never implemented (removed) |
| 1 | Contracts & JSON Serialization | Done | DTOs + LoomJsonSerializerContext |
| 2 | Web API Core | Done | Minimal API, health endpoint, Kestrel config |
| 3 | Core Metrics Endpoints | Done | CPU, Memory, Thread metric APIs |
| 4 | WebSocket Real-Time Streaming | Done | Zero-allocation WebSocket layer |

### Telemetry Platform (Current Focus)

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 5 | Source Generator | ✅ Complete | `Loom.Telemetry.Generators/LoomProfileGenerator.cs` — emits C# `[InterceptsLocation]` interceptors at compile time; covered by `GeneratorTests.cs` |
| 6 | Custom Metrics API | ✅ Complete | `RecordMetric`/`Counter`/`Gauge`/`Histogram` + tags — `Loom.Telemetry/LoomMetrics.cs`, `MetricRecord.cs`, `MetricBuffer.cs` |
| 7 | Attribute-Based Instrumentation | ✅ Complete | `[LoomProfile]`, `[LoomTrack]` via source gen — used throughout `examples/SampleMonitoredApp` (`OrderService.cs`, `PaymentService.cs`); covered by `GeneratorTests.cs` and `PropertyTrackingTests.cs` |
| 8 | Custom Collectors/Plugins | ✅ Complete | `ILoomCollector` — `Loom.Telemetry/LoomCollectors.cs`, `CollectorSnapshot.cs`, `CollectorTests.cs` |
| 9 | Configuration-Driven Sampling | ✅ Complete | `Loom.Telemetry/LoomSampling.cs`, `SamplingTests.cs` |
| 10 | Query Language | ✅ Complete | `Loom.Telemetry.Query/` (Tokenizer, Parser, Ast, Planner, Executor) + 4 test files |
| 11 | Alerting/Thresholds | ✅ Complete | `Loom.Telemetry.Alerting/` + `Alerting/` tests + `PHASE-11-COMPLETE.md` |
| 12 | Exporters | ✅ Complete | Prometheus, Console. Grafana Cloud and Elasticsearch were removed as non-functional dead code — see BACKLOG.md § 9 |
| 13 | Local Development Mode | ✅ Complete | `dotnet loom dev` — `Loom.DevTools/Commands/DevCommand.cs` |

### Production Hardening

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 14 | Security Hardening | Planned | Manual JWT, HTTPS enforcement, systemd sandbox |
| 15 | Production Build & Deployment | Planned | Binary optimization, systemd service, CI/CD |

### Frontend (Phase 16)

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 16 | Dashboard Modernization | Implementation complete, pending browser verification | Angular 21 + Apache ECharts, dark theme with teal accent, multi-page SPA. HTTP/WS surface (routes, `/prometheus`, WebSocket upgrade) smoke-tested; visual/UX checks (contrast, responsive breakpoints, chart rendering, keyboard nav) require a browser and have not been run |

### Dependency Graph (Telemetry Phases)

```
Phase 5 (Source Generator) ──┬──→ Phase 7 (Attributes)
                             └──→ Phase 8 (Collectors)
Phase 6 (Metrics API) ──┬──→ Phase 8 (Collectors)
                        ├──→ Phase 9 (Sampling)
                        ├──→ Phase 10 (Query)
                        ├──→ Phase 12 (Exporters)
                        └──→ Phase 13 (Dev Mode)
Phase 10 (Query) ────────────→ Phase 11 (Alerting)
All Phases 5-12 ─────────────→ Phase 13 (Dev Mode)
```

---

## DTOs Registered in LoomJsonSerializerContext

All DTO types used by the 9 telemetry systems must be registered at compile time:

### Infrastructure (existing)
- `HealthCheckResponse`, `SessionInfoResponse`, `CpuMetricResponse`, `CpuHotpath`
- `MemoryMetricResponse`, `GarbageCollectionStats`, `MemoryAllocation`
- `ThreadMetricResponse`, `ThreadBlockage`
- `MetricUpdate`, `CpuMetricUpdate`, `MemoryMetricUpdate`, `ThreadMetricUpdate`
- `DiagnosticSearchRequest`, `DiagnosticSearchResponse`, `SearchResult`
- `TelemetryIngestRequest`, `MetricIngestRequest`, `MetricIngestDto`

### Query (Phase 10)
- `QueryRequest`, `QueryResponse`, `QueryResultRow`, `QueryValue`, `QueryColumn`

### Alerting (Phase 11)
- `AlertConfigDto`, `AlertConditionDto`
- `AlertStatusDto`, `AlertHistoryEntry`, `AlertWebhookPayload`

### Exporters (Phase 12)
- `ExporterStatusDto`, `MetricSummaryDto`

### Dev Mode (Phase 13)
- `DevModeStatusDto`, `DiscoveredAppDto`

> The ingest DTO actually used on the wire is `MetricIngestRequest`/`MetricIngestDto`.
> `TelemetryIngestRequest` is registered but unused.
>
> Not currently registered in `LoomJsonSerializerContext` (no live serialization need):
> `MetricRecord`, `MetricTag`, `CounterValue`, `GaugeValue`, `HistogramValue`,
> `HistogramBucket`, `MetricBatch`, `MetricRegistration`, `CollectorSnapshot`,
> `CollectorRegistration`, `CollectorStatus`, `SamplingConfigDto`, `SamplingRuleDto`,
> `ExportBatchResult`, `AlertNotificationTarget`. Register these before putting them on
> any API/WebSocket payload.

---

## Security

- HTTPS enforcement + HSTS in production
- Manual JWT authentication (HS256, Span-based, zero-allocation)
- Strict CORS whitelist (no wildcards)
- Security headers (CSP, X-Frame-Options, etc.)
- systemd sandboxing (`ProtectSystem=strict`, `MemoryDenyWriteExecute`, etc.)
- Runs as unprivileged user `loomd` with least privilege
- JWT secret in `/var/secrets/loom/jwt.key` (mode 400)

---

## Documentation

| Document | Role |
|----------|------|
| [`IMPLEMENTATION-METHODOLOGY.md`](./IMPLEMENTATION-METHODOLOGY.md) | Authoritative step-by-step build guide (all 9 systems, full code) |
| [`wiggly-noodling-hoare.md`](./wiggly-noodling-hoare.md) | Architecture decisions, deployment config, design rationale |
| [`commands.md`](./commands.md) | Development commands reference |
| [`skills.md`](./skills.md) | AI assistant skills guide |
| [`CLAUDE.md`](./CLAUDE.md) | AI behavior constraints and project rules |

---

## Deferred / Not in Current Scope

The following are explicitly **not in scope** for the current implementation pass. They are documented here so they aren't lost:

| Feature | Reason Deferred | Future Phase |
|---------|----------------|--------------|
| Custom Dashboard Widgets (`@LoomWidget` plugin system) | Requires Angular plugin infrastructure | Phase 17+ |
| Query Builder UI (autocomplete, visual query composer) | UI-shaped affordance; underlying query engine (#10) IS in scope | Phase 16+ |
| Mobile app (PWA/React Native) | Depends on frontend | Phase 17+ |

**Note:** The Angular frontend (Phase 16) is now **in progress** — it was un-deferred to provide a modern dashboard with Apache ECharts visualizations and dark theme.

The query engine (Phase 10), alert conditions (Phase 11), and all other backend capabilities are fully built and testable via curl/API without any frontend. The Local Development Mode (Phase 13) provides terminal/console output for day-to-day use without a browser.

---

## License

Licensed under the MIT License. See the [LICENSE](./LICENSE) file for details.
