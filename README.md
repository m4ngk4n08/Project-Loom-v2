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
- **Exporters** — Prometheus, Grafana Cloud, Elasticsearch, Console interoperability
- **Source Generator** — zero-allocation instrumentation resolved entirely at compile time (no reflection)
- **Sampling** — configuration-driven sampling rules (path-based, duration-based)
- **Local Dev Mode** — `dotnet loom dev` for zero-config live metrics during development

All built on .NET 10 Native AOT with zero runtime reflection.

---

## Key Constraint: Native AOT

Everything is built around **.NET 10 Native AOT** (reflection-free) compilation:

| Constraint | Why |
|-----------|-----|
| Binary size **< 15 MB** | Single-binary deployment |
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
Loom.sln
├── Loom.Web.Api/                  → ASP.NET Core Minimal APIs (Native AOT host)
├── Loom.Web.Contracts/            → Shared DTOs + source-generated JSON (MANDATORY for AOT)
├── Loom.Web.RealTime/             → Zero-allocation WebSocket handlers
├── Loom.Core/                     → SIMD math engine (AVX2/Neon)
├── Loom.Storage/                  → Memory-mapped binary cache
├── Loom.Host/                     → Bootstrap entry point
├── Loom.Telemetry/                → Custom Metrics API runtime (RecordMetric, etc.)
├── Loom.Telemetry.Generators/     → C# source generator ([LoomProfile] → instrumented code)
├── Loom.Telemetry.Collectors/     → ILoomCollector interface + plugin registration
├── Loom.Telemetry.Query/          → Query engine (SQL-like parser + fluent API)
├── Loom.Telemetry.Alerting/       → Alert rules, window conditions, notification dispatch
├── Loom.Telemetry.Exporters/      → Prometheus, Grafana Cloud, Elasticsearch, Console
├── Loom.DevTools/                 → `dotnet loom dev` CLI tool (local dev mode)
├── Loom.Tests/                    → Unit & integration tests
└── Loom.Benchmarks/               → BenchmarkDotNet performance benchmarks
```

**Dependency flow:**
```
Loom.Host → Loom.Web.Api → Loom.Web.Contracts
                         → Loom.Web.RealTime → Loom.Web.Contracts
                         → Loom.Telemetry → Loom.Web.Contracts
                         → Loom.Telemetry.Collectors → Loom.Telemetry
                         → Loom.Telemetry.Query → Loom.Telemetry
                         → Loom.Telemetry.Alerting → Loom.Telemetry.Query
                         → Loom.Telemetry.Exporters → Loom.Telemetry
                         → Loom.Core ← Loom.Storage

Loom.Telemetry.Generators (analyzer, referenced by consuming projects, no runtime dep)

Loom.DevTools (standalone CLI, references Loom.Telemetry)
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100+)
- Native compilation tools (MSVC v143 on Windows / Clang 19 on Linux)

### Backend (development, fast iteration)

```bash
dotnet test Loom.sln --configuration Debug
dotnet watch run --project Loom.Host --no-hot-reload
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
| `GET /api/metrics/cpu` | CPU hotpath metrics |
| `GET /api/metrics/memory` | Memory allocation & GC stats |
| `GET /api/metrics/thread` | Thread activity & blockage analysis |
| `WS /ws/metrics` | Real-time infrastructure metric stream (~10 Hz) |

### Custom Telemetry

| Endpoint | Description |
|----------|-------------|
| `POST /api/metrics/ingest` | Batch metric ingestion (Counter/Gauge/Histogram) |
| `GET /api/exporters/metrics/names` | List registered metric names |
| `GET /prometheus` | Prometheus scrape endpoint (OpenMetrics format) |

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
| `GET /prometheus` | Prometheus scrape endpoint (OpenMetrics format) |

### Sampling

Collectors and sampling are **library-level APIs** (via `LoomMetrics`, `LoomSampling.Configure`, `LoomCollectors.Register`) — no HTTP endpoints are exposed for these.

---

## Native AOT Production Build

```bash
dotnet publish Loom.Host/Loom.Host.csproj \
  --configuration Release \
  -r linux-x64 \
  /p:PublishAot=true \
  /p:StripSymbols=true

strip --strip-debug \
  Loom.Host/bin/Release/net10.0/linux-x64/publish/Loom.Host

# Verify the binary stays under 15 MB
ls -lh Loom.Host/bin/Release/net10.0/linux-x64/publish/Loom.Host
```

---

## Verification Checklist

```bash
# 1. No trim/AOT warnings
dotnet build Loom.sln -c Release /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true

# 2. Native AOT compiles
dotnet publish Loom.Host -c Release -r linux-x64 /p:PublishAot=true

# 3. Binary size < 15 MB
ls -lh Loom.Host/bin/Release/net10.0/linux-x64/publish/Loom.Host

# 4. IL + AOT tests pass
dotnet test Loom.sln --configuration Debug

# 5. Zero allocations in hot paths
dotnet-counters monitor --process-id $(pidof Loom.Host) System.Runtime
```

---

## Implementation Phases

### Foundation (Existing Infrastructure)

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 0 | Project Setup & Tooling | Done | SDK, solution structure, project scaffolding |
| 1 | Contracts & JSON Serialization | Done | DTOs + LoomJsonSerializerContext |
| 2 | Web API Core | Done | Minimal API, health endpoint, Kestrel config |
| 3 | Core Metrics Endpoints | Done | CPU, Memory, Thread metric APIs |
| 4 | WebSocket Real-Time Streaming | Done | Zero-allocation WebSocket layer |

### Telemetry Platform (Current Focus)

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 5 | Source Generator | In Progress | `Loom.Telemetry.Generators` — compile-time instrumentation rewriting |
| 6 | Custom Metrics API | Planned | `RecordMetric`/`Counter`/`Gauge`/`Histogram` + tags |
| 7 | Attribute-Based Instrumentation | Planned | `[LoomProfile]`, `[LoomTrack]` via source gen (depends on Phase 5) |
| 8 | Custom Collectors/Plugins | Planned | `ILoomCollector`, `AddCollector<T>()` (depends on Phase 5) |
| 9 | Configuration-Driven Sampling | Planned | `appsettings.json` sampling rules, path/duration overrides |
| 10 | Query Language | Planned | SQL-like parser + fluent `Query()` API, `POST /api/query` |
| 11 | Alerting/Thresholds | Planned | `AddAlert()`, window conditions, webhook/email targets |
| 12 | Exporters | ✅ Complete | Prometheus, Grafana Cloud, Elasticsearch, Console |
| 13 | Local Development Mode | Planned | `dotnet loom dev`, auto-discovery, zero-config |

### Production Hardening

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 14 | Security Hardening | Planned | Manual JWT, HTTPS enforcement, systemd sandbox |
| 15 | Production Build & Deployment | Planned | Binary optimization, systemd service, CI/CD |

### Frontend (Phase 16)

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 16 | Dashboard Modernization | 🚧 In Progress | Angular 21 + Apache ECharts, dark theme with teal accent, multi-page SPA |

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
- `HealthCheckResponse`, `CpuMetricResponse`, `CpuHotpath`
- `MemoryMetricResponse`, `GarbageCollectionStats`, `MemoryAllocation`
- `ThreadMetricResponse`, `ThreadBlockage`
- `MetricUpdate`, `CpuMetricUpdate`, `MemoryMetricUpdate`, `ThreadMetricUpdate`
- `DiagnosticSearchRequest`, `DiagnosticSearchResponse`, `SearchResult`
- `TelemetryIngestRequest`

### Custom Metrics API (Phase 6)
- `MetricRecord`, `MetricTag`
- `CounterValue`, `GaugeValue`, `HistogramValue`, `HistogramBucket`
- `MetricBatch`, `MetricRegistration`

### Collectors (Phase 8)
- `CollectorSnapshot`, `CollectorRegistration`, `CollectorStatus`

### Query (Phase 10)
- `QueryRequest`, `QueryResponse`, `QueryResultRow`, `QueryColumn`

### Alerting (Phase 11)
- `AlertConfigDto`, `AlertConditionDto`, `AlertNotificationTarget`
- `AlertStatusDto`, `AlertHistoryEntry`

### Exporters (Phase 12)
- `ExporterStatusDto`, `ExportBatchResult`

### Sampling (Phase 9)
- `SamplingConfigDto`, `SamplingRuleDto`

### Dev Mode (Phase 13)
- `DevModeStatusDto`, `DiscoveredAppDto`

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
