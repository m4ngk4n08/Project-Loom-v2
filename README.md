# Project Loom v2

A **customizable telemetry platform** for .NET applications. Loom provides live insight into CPU hotpaths, memory allocations, thread blockages, and — critically — **your own business metrics**, with instrumentation powered by C# source generators that allocates nothing on untagged recording paths.

The successor to the original SSH-based design, Loom v2 is a .NET-native observability stack. It is packaged as a Native-AOT-clean library (`LoomDiagnostics.Telemetry`) plus two dotnet tools: `loom` (`LoomDiagnostics.Cli`) and `loom-dashboard` (`LoomDiagnostics.Dashboard`). Nothing is published to NuGet yet.

---

## Overview

Loom is not just a profiler. It's a **telemetry platform** you embed into .NET applications:

- **Custom Metrics** — `RecordMetric`, `RecordCounter`, `RecordGauge`, `RecordHistogram` with tag/dimension support
- **Attribute-Based Instrumentation** — `[LoomProfile]` and `[LoomTrack]` auto-instrument methods at compile time
- **Custom Collectors** — `ILoomCollector` plugin interface for third-party integrations (Redis, RabbitMQ, etc.)
- **Query Language** — SQL-like telemetry queries + fluent code-based `Query()` API
- **Alerting** — `AddAlert()` with window-based conditions; webhook, email and console targets
- **Exporters** — Prometheus, Console interoperability
- **Source Generator** — instrumentation resolved entirely at compile time (no reflection)
- **Sampling** — configuration-driven sampling rules (name-pattern, duration-threshold, uniform, always-record)
- **Log capture** — `ILogger` records captured, searched, tailed and exported through the dashboard
- **CLI** — `loom` attaches to a process over EventPipe: `dev`, `watch`, `explore`, `metrics`, `query`, `logs`, `search`, `auth`

**Allocation cost.** Bytes allocated per call on the calling thread, measured (`GC.GetAllocatedBytesForCurrentThread`, after warm-up, .NET 10, Windows x64, JIT):

| Path | Bytes per call |
|------|---------------:|
| `RecordCounter` / `RecordHistogram`, no tags | 0 |
| `[LoomProfile]` method, normal return | 0 |
| `RecordCounter` / `RecordHistogram`, 1 tag | 40 |
| `RecordCounter` / `RecordHistogram`, 2 tags | 56 |
| `RecordGauge`, no tags / 1 tag | 32 / 296 |
| `[LoomProfile]` method that throws | 552 (a bare throw/catch of the same exception costs 296) |

The tagged cost is the `params MetricTag[]` array, which the ring buffer keeps. Gauges retain a copy of their tags for the observable callback, so they allocate more. The untagged rows are asserted by tests and, natively, by `Loom.AotProbe` in CI.

The telemetry library is Native-AOT-clean with zero runtime reflection, proven in CI.

---

## Key Constraint: Native AOT

Everything is built around **.NET 10 Native AOT** (reflection-free) compilation:

| Constraint | Why |
|-----------|-----|
| Native AOT compatibility, proven by `Loom.AotProbe` and the packaged consumer gate | No shipping AOT binary today (`Loom.Web.Api` retired); binary size is not a gate — see `BACKLOG.md` § 2.1, § 11.4 |
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

**Frontend**
- Angular 21 standalone components + RxJS, Apache ECharts

**Build Tools**
- Native toolchain for AOT publish only: MSVC build tools (Windows) / `clang` + `zlib1g-dev` (Linux)
- Node.js 20+ LTS (frontend build only)

---

## Project Structure

```
Loom.slnx                          (16 projects)
├── Loom.Telemetry/                → Custom Metrics API runtime (RecordMetric, LoomCollectors, LoomSampling, etc.)
│                                     Packable: LoomDiagnostics.Telemetry
├── Loom.Telemetry.Generators/     → C# source generator ([LoomProfile] → instrumented code),
│                                     shipped inside the Loom.Telemetry package
├── Loom.Web.Contracts/            → Shared DTOs + source-generated JSON (MANDATORY for AOT)
├── Loom.Web.RealTime/             → Zero-allocation WebSocket handlers
├── Loom.Security/                 → Manual JWT: issuer, validator, PBKDF2 hashing, user
│                                     store, login throttle, auth middleware, token endpoints
├── Loom.Storage/                  → In-memory ring-buffer metric and log stores, ILogger capture
├── Loom.Telemetry.Query/          → Query engine (SQL-like tokenizer/parser/planner/executor)
├── Loom.Telemetry.Alerting/       → Alert rules, window conditions, notification dispatch
├── Loom.Telemetry.Exporters/      → Prometheus, Console
├── Loom.Telemetry.Assist/         → Remote LLM "Explain" client (templates + argument names only)
├── Loom.Dashboard.AspNetCore/     → The dashboard web host as a library: endpoints, EventPipeBridge,
│                                     AddLoomDashboard / UseLoomDashboard / MapLoomDashboard. Not packable yet
├── Loom.Dashboard/                → `loom-dashboard <pid>` dotnet tool; thin wrapper over
│                                     Loom.Dashboard.AspNetCore that embeds the Angular build
├── Loom.DevTools/                 → `loom` dotnet tool (dev, watch, explore, metrics, query, logs, search, auth)
├── Loom.AotProbe/                 → Minimal console app; the Native AOT proof (binary
│                                     size is not a product metric — see BACKLOG.md § 11.4)
├── Loom.TestFixtureApp/           → Console app the EventPipe integration tests attach to
└── Loom.Telemetry.Tests/          → Unit & integration tests (the only test project)

Not in the solution:
  Loom.Web.Frontend/               → Angular 21 dashboard (built with ng, embedded by Loom.Dashboard)
  examples/SampleMonitoredApp/     → Demo app instrumented with [LoomProfile] / [LoomTrack]
  ci/consumer-aot-gate/            → Consumes the packed Loom.Telemetry .nupkg and AOT-publishes it
```

> `Loom.Host/`, `Loom.Core/`, `Loom.Benchmarks/` from earlier design docs were never
> implemented (empty scaffolding, since deleted). `Loom.Web.Api`, the earlier AOT host,
> was retired — the dashboard is now the only web host, and the Native AOT proof
> moved to `Loom.AotProbe` (see `BACKLOG.md` § 11.4). `Loom.Storage`'s ring buffers
> superseded the planned SIMD engine. `Loom.Telemetry.Collectors` was folded directly
> into `Loom.Telemetry`.

**Dependency flow (verified `ProjectReference` edges):**
```
Loom.Web.Contracts, Loom.Telemetry.Generators  (no project refs)
Loom.Telemetry           → Loom.Telemetry.Generators (build ordering only — not an
                           analyzer reference; the package ships the generator itself)
Loom.Web.RealTime        → Loom.Web.Contracts
Loom.Security            → Loom.Web.Contracts
Loom.Telemetry.Assist    → Loom.Web.Contracts
Loom.Storage             → Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry.Query     → Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry.Alerting  → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query, Loom.Web.Contracts
Loom.Telemetry.Exporters → Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.AotProbe            → Loom.Telemetry, Loom.Telemetry.Generators
Loom.TestFixtureApp      → Loom.Telemetry, Loom.Telemetry.Generators
Loom.DevTools            → Loom.Security, Loom.Storage, Loom.Telemetry,
                           Loom.Telemetry.Query, Loom.Web.Contracts
Loom.Dashboard.AspNetCore→ Loom.Security, Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                           Loom.Telemetry.Alerting, Loom.Telemetry.Exporters,
                           Loom.Web.Contracts, Loom.Web.RealTime
Loom.Dashboard           → Loom.Dashboard.AspNetCore, Loom.Security, Loom.Storage,
                           Loom.Telemetry.Assist, Loom.Web.Contracts
```

`Loom.Dashboard.AspNetCore` is the only web host; `Loom.Dashboard` packs it as a dev-time
dotnet tool (`PackAsTool`, not AOT-published) that embeds the Angular frontend and
attaches to a target PID via `EventPipeBridge.cs`. `Loom.AotProbe` proves referencing
`Loom.Telemetry` stays Native-AOT-clean; it is not a web host and has no endpoints of its
own.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100+)
- Node.js 20+ LTS and the Angular CLI, to build the dashboard frontend
- For the Native AOT probe only: MSVC build tools on Windows / `clang` + `zlib1g-dev` on Linux

> **Native AOT cannot cross-compile between operating systems.** Publishing `linux-x64`
> from Windows fails with `Cross-OS native compilation is not supported`. Build the Linux
> binary on Linux — WSL, a container, or the `ubuntu-latest` CI job.

### First run: provision credentials

Every endpoint requires a token, so the host will not start without key material. It
**fails closed** — there is no generated-on-the-fly key in any environment, deliberately,
because an ephemeral dev key is exactly the convenience that reaches production by
accident.

```bash
dotnet run --project Loom.DevTools -- auth init          # writes jwt.key + users
dotnet run --project Loom.DevTools -- auth add-user operator

# Windows (PowerShell): auth init prints these for you
$env:LOOM_JWT_KEY_FILE  = "$env:LOCALAPPDATA\Loom\dev-secrets\jwt.key"
$env:LOOM_AUTH_USERS_FILE = "$env:LOCALAPPDATA\Loom\dev-secrets\users"

# Linux / WSL
export LOOM_JWT_KEY_FILE=~/.local/share/Loom/dev-secrets/jwt.key
export LOOM_AUTH_USERS_FILE=~/.local/share/Loom/dev-secrets/users
```

Secrets live **outside the repo**. Never commit a key, and never paste a token into an
issue or a commit message.

### Backend (development, fast iteration)

```bash
cd Loom.Web.Frontend && ng build && cd ..   # the dashboard embeds this; skip it and the UI is empty
dotnet test Loom.slnx --configuration Debug
dotnet watch run --project Loom.Dashboard --no-hot-reload -- <pid> [--port <n>]
```

The Dashboard listens on `http://localhost:5209` by default, falling back to a free port
if 5209 is taken. `--port <n>` or `LOOM_DASHBOARD_PORT` sets it explicitly, and then a
taken port is an error rather than a fallback. **Loopback only**. Loom does not terminate TLS — it binds `127.0.0.1` in code,
so no environment variable can publish it to an external interface. A remote operator
reaches it through an SSH tunnel. See [Security](#security).

```bash
# Log in, then call a protected route
TOKEN=$(curl -s -X POST http://localhost:5209/api/token \
  -H 'Content-Type: application/json' \
  -d '{"username":"operator","password":"..."}' | jq -r .token)

curl -H "Authorization: Bearer $TOKEN" http://localhost:5209/api/metrics/cpu
```

### The `loom` CLI

```bash
loom dev [--all]                         # discover Loom-instrumented processes (--all: every .NET process)
loom dev --dashboard                     # launch the dashboard (requires the loom-dashboard tool)
loom watch <pid> [--raw]                 # stream metric events
loom explore <pid>                       # list all metrics and latest values
loom metrics <pid> [cpu|memory|thread]   # formatted metrics; --live for a refreshing terminal view
loom query <pid> "SELECT ..."            # run a LoomQL query
loom logs <pid> [--count N] [--category X] [--seconds N]
loom search <pid> "<query>" [--max N] [--seconds N]   # BM25 search over captured logs
loom auth init | add-user | hash | token # credentials (see First run above)
```

Running `loom` with no arguments prints the full usage.

`loom` attaches directly to a target process via EventPipe and has **no network
surface**, so no token applies to it. Its security boundary is the OS user owning the
target process — which makes "never run `loom` elevated" a security control, not a style
preference.

---

## API Endpoints

**Every endpoint requires a bearer token unless listed as anonymous below.** An
unauthenticated request returns **401**; a request whose token carries the wrong scope
returns **403**.

### Authentication

| Endpoint | Auth | Description |
|----------|------|-------------|
| `POST /api/token` | anonymous | Log in with `{"username","password"}`, returns `{"token","expiresIn"}` |
| `POST /api/token/refresh` | anonymous | Exchange a valid token for a fresh one. Bounded by the original session start, so a session cannot be renewed indefinitely |

Anonymous by design: `/api/token`, `/api/token/refresh`, `/api/health`, and the
Dashboard's SPA fallback. `/prometheus` accepts a metrics-scoped token. Everything else
needs a full-authority token.

Service tokens for unattended scrapers are minted offline — no login round-trip:

```bash
loom auth token --sub prometheus --scope metrics --ttl 90d
```

The `--scope` flag is load-bearing. Without it the token carries full operator authority,
which on a 90-day unattended credential would hand a scraper the run of the API. Only
`metrics` and `full` are accepted; anything else is rejected rather than quietly widened.

### Infrastructure Metrics

| Endpoint | Description |
|----------|-------------|
| `GET /api/health` | Health check (status, uptime, memory). **Anonymous** so liveness probes work — a probe cannot hold a 60-minute JWT |
| `GET /api/session` | Session/attach metadata |
| `GET /api/metrics/cpu` | CPU hotpath metrics |
| `GET /api/metrics/memory` | Memory allocation & GC stats |
| `GET /api/metrics/thread` | Thread activity & blockage analysis |
| `WS /ws/metrics` | Real-time infrastructure metric stream (~10 Hz) |

### Custom Telemetry

| Endpoint | Description |
|----------|-------------|
| `POST /api/metrics/ingest` | Batch metric ingestion (Counter/Gauge/Histogram) |

### Logs

| Endpoint | Description |
|----------|-------------|
| `GET /api/logs` | Recent captured log records |
| `GET /api/logs/categories` | Distinct log categories |
| `GET /api/logs/tail` | Records after a cursor, for incremental polling |
| `GET /api/logs/export` | Export captured records |
| `POST /api/logs/search` | Search captured records |
| `POST /api/logs/explain` | LLM explanation of a record. **Only mapped when an explain client is configured** (`LOOM_LLM_API_KEY` for `loom-dashboard`); otherwise 404 |
| `WS /ws/logs` | Real-time log stream |

All routes are defined in `Loom.Dashboard.AspNetCore/Extensions/EndpointExtensions.cs`;
the token routes in `Loom.Security/TokenEndpoints.cs`. An unmapped `/api/*` path returns
404 rather than the SPA page.

### Prometheus scrape endpoint

`GET /prometheus`, not `/metrics`: the Angular app owns a client-side `/metrics` page
(`metrics-explorer`), and its `MapFallback` serves `index.html` for unmatched routes, so
mapping the scrape endpoint to `/metrics` would shadow that route and break deep-link/
refresh on the Angular page.
Accepts a **metrics-scoped** token.

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

Prometheus scrape route: see "Prometheus scrape endpoint" above.

### Sampling

Collectors and sampling are **library-level APIs** (via `LoomMetrics`, `LoomSampling.Configure`, `LoomCollectors.Register`) — no HTTP endpoints are exposed for these.

---

## Native AOT Proof

There is no longer a shipping Native AOT binary — `Loom.Web.Api` was retired and
`Loom.Dashboard` (the only remaining web host) is not AOT-published. `Loom.AotProbe` is
a minimal console app that proves referencing `Loom.Telemetry` stays Native-AOT-clean;
its binary size is deliberately not a product metric (see `BACKLOG.md` § 11.4). Must be
run **on Linux** for the Linux RID — Native AOT cannot cross-compile. `PublishAot`,
`PublishTrimmed`, `TrimMode=link` and `InvariantGlobalization` already live in
`Loom.AotProbe.csproj`; don't re-pass them, or the csproj stops being the single source
of truth for how the probe is built.

```bash
dotnet publish Loom.AotProbe/Loom.AotProbe.csproj --configuration Release -r linux-x64

ls -lh Loom.AotProbe/bin/Release/net10.0/linux-x64/publish/Loom.AotProbe
```

A correct AOT publish contains **no managed assemblies** — if a `Loom.AotProbe.dll`
appears beside the native binary, the publish silently fell back to a managed build and
the probe is proving nothing.

---

## Verification Checklist

```bash
# 1. No trim/AOT warnings, solution-wide. Expect 0 errors and 4 known warnings -
#    2 xUnit1031 in InMemoryMetricStoreTests, and 2 NETSDK1212 because the
#    netstandard2.0 generator project cannot use the trim analyzer. Leave all four.
#    (A fresh clone or worktree without a frontend build adds a fifth, from
#    GenerateEmbeddedFilesManifest finding nothing to embed.)
dotnet build Loom.slnx -c Release /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true

# 2. Native AOT compiles (on the target OS; AOT properties live in the csproj)
dotnet publish Loom.AotProbe/Loom.AotProbe.csproj -c Release -r linux-x64

# 3. No managed assembly beside the native output (Loom.AotProbe's size itself is not
#    a product metric - see BACKLOG.md § 11.4)
ls -l Loom.AotProbe/bin/Release/net10.0/linux-x64/publish/

# 4. Backend tests - 820 passing, 0 skipped (measured 2026-09-17, Windows and Linux)
dotnet test Loom.slnx --configuration Debug

# 5. Frontend - 4 files, 102 passing (last measured 2026-09-17)
cd Loom.Web.Frontend && npx ng test

# 6. Allocation and GC behaviour of a running dashboard
dotnet-counters monitor --process-id $(pidof Loom.Dashboard) System.Runtime
```

All of the above run in CI on every push to `main` and every PR — see
[Continuous Integration](#continuous-integration).

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
| 11 | Alerting/Thresholds | ✅ Complete | `Loom.Telemetry.Alerting/` + `Alerting/` tests |
| 12 | Exporters | ✅ Complete | Prometheus, Console. Grafana Cloud and Elasticsearch were removed as non-functional dead code — see BACKLOG.md § 9 |
| 13 | Local Development Mode | ✅ Complete | `loom` CLI — `Loom.DevTools/Commands/` |

### Production Hardening

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 14 | Security Hardening | ✅ Complete | Manual JWT in `Loom.Security` — login endpoint, PBKDF2 credentials, every endpoint enforced, scoped service tokens, Angular auth. Loopback bind in code; **in-process TLS was evaluated and rejected** (see Security below) |
| 15 | Production Build & Deployment | In progress | **15.1 build** — the Linux AOT binary was built and smoke-tested on the since-retired `Loom.Web.Api`; today only `Loom.AotProbe` is AOT-published. **15.3 CI/CD** ✅ — see below. **15.2 systemd** ⏳ — units, the `loomd` user, and secrets provisioning remain. **Packaging** ⏳ — pre-publish API review in progress, version number not yet chosen (`BACKLOG.md` § 11) |

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

- **Loom does not terminate TLS, deliberately.** It binds `127.0.0.1` in code via
  `ListenLocalhost`, so no environment variable can publish it to an external interface —
  verified on both Windows and Linux, where `ASPNETCORE_URLS=http://0.0.0.0:5209` is
  overridden and Kestrel logs that it discarded the value. A remote operator reaches it
  through an SSH tunnel, which already encrypts the only hop that leaves the machine.
  In-process TLS was measured (on the now-retired `Loom.Web.Api`) at **+0.946 MB**, and
  rejected: it would defend a hop that never crosses a network, and would add certificate
  provisioning, file permissions and renewal. `UseHsts()` and `UseHttpsRedirection()` are
  deleted rather than left in place, because leaving them would imply a protection the
  process does not provide. If non-tunnel access is ever needed, front the port with a
  reverse proxy and let it own the certificate lifecycle. See `BACKLOG.md` § 3.3.
- Manual JWT authentication (HS256, Span-based, zero-allocation) — no
  `System.IdentityModel.Tokens.Jwt`, which is reflection-heavy and not AOT-clean
- **Every endpoint is protected**; anonymous access is opt-in per endpoint, never a
  default. Scoped tokens return **403** on a scope mismatch, not 401
- Passwords hashed with PBKDF2-SHA256, 600,000 iterations (~74 ms per verification).
  Unknown usernames are compared against a fixed dummy record so "no such user" and
  "wrong password" take the same time — otherwise the login endpoint is a user-enumeration
  oracle
- Login throttle: 5 failed attempts per client per 15 minutes, counted atomically before
  the password check so concurrent guesses cannot exceed it. The dashboard binds loopback,
  so in practice every client is `127.0.0.1` and the throttle is global — the ~74 ms
  PBKDF2 cost is the real brute-force control
- Token refresh preserves the original session start (12-hour cap) and re-checks that the
  user still exists in the users file
- Key material **fails closed**: a missing signing key, a missing users file, or a users
  file defining zero users aborts startup with an actionable message. No
  generated-on-the-fly fallback exists in any environment. Default location on Linux is
  `/var/secrets/loom/` (`LOOM_JWT_KEY_FILE` / `LOOM_AUTH_USERS_FILE` override it)
- CORS: none. The dashboard serves its UI from its own origin, so same-origin is correct
  and no CORS policy is needed
- Security headers (Content-Security-Policy, X-Frame-Options, X-Content-Type-Options,
  Referrer-Policy), applied at the front of the pipeline in `loom-dashboard`'s own host
  (`Loom.Dashboard/Program.cs`) so short-circuiting middleware cannot skip them — see
  `BACKLOG.md` § 11.5. They are not part of `Loom.Dashboard.AspNetCore`, so an app
  embedding the library must set its own

**Planned, not yet implemented** (Phase 15.2): systemd unit with sandboxing
(`ProtectSystem=strict`, `MemoryDenyWriteExecute`, etc.), a dedicated unprivileged `loomd`
user, and provisioned secrets with mode 400.

---

## Continuous Integration

`.github/workflows/ci.yml` runs on every push to `main` and every PR:

| Job | What it does |
|-----|--------------|
| Build & test (ubuntu, windows, macos) | Restore, strict Release build with trim/AOT analyzers as errors, full test suite, source-generator tests |
| Angular tests | `npm ci`, `ng test`, and a production bundle build |
| Native AOT probe (linux-x64) | Installs `clang` + `zlib1g-dev`, publishes `Loom.AotProbe`, asserts the output is genuinely native, runs it (no size gate — see `BACKLOG.md` § 11.4) |
| Packaged consumer AOT gate (linux-x64) | Packs `Loom.Telemetry`, restores it from a folder feed into `ci/consumer-aot-gate`, AOT-publishes and runs it. The only check that exercises the package layout rather than project references (`BACKLOG.md` § 11.3) |

The AOT jobs exist because Native AOT cannot cross-compile — they are the only way the
Linux artifacts get built in CI.

---

## Documentation

| Document | Role |
|----------|------|
| [`IMPLEMENTATION-METHODOLOGY.md`](./IMPLEMENTATION-METHODOLOGY.md) | Original step-by-step build guide (design intent; written before delivery, so some project names in it no longer exist) |
| [`wiggly-noodling-hoare.md`](./wiggly-noodling-hoare.md) | Architecture decisions, deployment config, design rationale |
| [`BACKLOG.md`](./BACKLOG.md) | Open items, decision log, and the measurements behind each decision |
| [`TESTING.md`](./TESTING.md) | Test strategy and coverage |
| [`SMOKE-TEST.md`](./SMOKE-TEST.md) | Manual end-to-end verification steps |
| [`Loom.Telemetry/PACKAGE.md`](./Loom.Telemetry/PACKAGE.md) | README shipped inside the `LoomDiagnostics.Telemetry` package |
| [`CLAUDE.md`](./CLAUDE.md) | AI behavior constraints, commands, and project rules |

---

## Deferred / Not in Current Scope

The following are explicitly **not in scope** for the current implementation pass. They are documented here so they aren't lost:

| Feature | Reason Deferred | Future Phase |
|---------|----------------|--------------|
| Custom Dashboard Widgets (`@LoomWidget` plugin system) | Requires Angular plugin infrastructure | Phase 17+ |
| Query Builder UI (autocomplete, visual query composer) | UI-shaped affordance; underlying query engine (#10) IS in scope | Phase 16+ |
| Mobile app (PWA/React Native) | Depends on frontend | Phase 17+ |

**Note:** The Angular frontend (Phase 16) was un-deferred to provide a modern dashboard with Apache ECharts visualizations and dark theme; see its status in the Frontend table above.

The query engine (Phase 10), alert conditions (Phase 11), and all other backend capabilities are fully built and testable via curl/API without any frontend. The Local Development Mode (Phase 13) provides terminal/console output for day-to-day use without a browser.

---

## License

Licensed under the MIT License. See the [LICENSE](./LICENSE) file for details.
