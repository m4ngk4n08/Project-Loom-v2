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
- **Local Dev Mode** — `loom dev` for zero-config live metrics during development

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
Loom.slnx                          (14 projects)
├── Loom.Web.Api/                  → ASP.NET Core Minimal APIs (Native AOT production host)
├── Loom.Web.Contracts/            → Shared DTOs + source-generated JSON (MANDATORY for AOT)
├── Loom.Web.RealTime/             → Zero-allocation WebSocket handlers
├── Loom.Security/                 → Manual JWT: issuer, validator, PBKDF2 hashing, user
│                                     store, login throttle, auth middleware, token endpoints
├── Loom.Storage/                  → Ring-buffer metric store (in-memory)
├── Loom.Telemetry/                → Custom Metrics API runtime (RecordMetric, LoomCollectors, LoomSampling, etc.)
├── Loom.Telemetry.Generators/     → C# source generator ([LoomProfile] → instrumented code)
├── Loom.Telemetry.Query/          → Query engine (SQL-like tokenizer/parser/planner/executor)
├── Loom.Telemetry.Alerting/       → Alert rules, window conditions, notification dispatch
├── Loom.Telemetry.Exporters/      → Prometheus, Console
├── Loom.Telemetry.Assist/         → Remote LLM "Explain" client (templates + argument names only)
├── Loom.DevTools/                 → `loom dev` CLI tool (local dev mode)
├── Loom.Telemetry.Tests/          → Unit & integration tests
└── Loom.Dashboard/                → `loom-dashboard <pid>` dev-time CLI tool; embeds the Angular dashboard, attaches to a target process via EventPipe
```

> `Loom.Host/`, `Loom.Core/`, `Loom.Benchmarks/` from earlier design docs were never
> implemented (empty scaffolding, since deleted). `Loom.Web.Api` is the real AOT host;
> `Loom.Storage`'s ring buffers superseded the planned SIMD engine. `Loom.Telemetry.Collectors`
> was folded directly into `Loom.Telemetry`.

**Dependency flow (verified `ProjectReference` edges):**
```
Loom.Web.Api           → Loom.Security, Loom.Storage, Loom.Telemetry.Exporters,
                          Loom.Telemetry.Query, Loom.Telemetry.Alerting,
                          Loom.Web.Contracts, Loom.Web.RealTime
Loom.Web.RealTime       → Loom.Web.Contracts
Loom.Security           → Loom.Web.Contracts
Loom.Storage            → Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry           (no project refs; DI abstractions package only)
Loom.Telemetry.Query    → Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry.Alerting → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query, Loom.Web.Contracts
Loom.Telemetry.Exporters→ Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.DevTools           → Loom.Security, Loom.Storage, Loom.Telemetry,
                          Loom.Telemetry.Query, Loom.Web.Contracts
Loom.Dashboard          → Loom.Security, Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                          Loom.Telemetry.Alerting, Loom.Telemetry.Assist, Loom.Telemetry.Exporters,
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
- Native compilation tools (MSVC v143 on Windows / Clang 19 + `zlib1g-dev` on Linux)

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
dotnet test Loom.slnx --configuration Debug
dotnet watch run --project Loom.Web.Api --no-hot-reload
```

The API listens on `http://localhost:5080`, **loopback only**. Loom does not terminate
TLS — it binds `127.0.0.1` in code, so no environment variable can publish it to an
external interface. A remote operator reaches it through an SSH tunnel. See
[Security](#security).

```bash
# Log in, then call a protected route
TOKEN=$(curl -s -X POST http://localhost:5080/api/token \
  -H 'Content-Type: application/json' \
  -d '{"username":"operator","password":"..."}' | jq -r .token)

curl -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/metrics/cpu
```

### Local Dev Mode

```bash
loom dev
# Auto-discovers .NET apps on localhost
# Streams live metrics to console/JSON
```

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

Anonymous by design: `/api/token`, `/api/token/refresh`, `/api/health` (both hosts), and
the Dashboard's SPA fallback. `/metrics` and `/prometheus` accept a metrics-scoped token.
Everything else needs a full-authority token.

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
| `GET /api/health` | Health check (status, uptime, memory). **Anonymous on both hosts** so liveness probes work — a probe cannot hold a 60-minute JWT |
| `GET /api/session` | Session/attach metadata — **`Loom.Dashboard` only**, not served by `Loom.Web.Api` (see `Loom.Dashboard/Extensions/EndpointExtensions.cs:66`) |
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
| `Loom.Dashboard` (dev CLI, embeds the Angular SPA) | `GET /prometheus` | The Angular app owns a client-side `/metrics` page (`metrics-explorer`); its `MapFallback` serves `index.html` for unmatched routes, so mapping the scrape endpoint to `/metrics` here would shadow that route and break deep-link/refresh on the Angular page. See `Loom.Dashboard/Extensions/EndpointExtensions.cs:473`. |

Both routes accept a **metrics-scoped** token.

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

Must be run **on Linux** — Native AOT cannot cross-compile. `PublishAot`,
`PublishTrimmed`, `TrimMode=link` and `InvariantGlobalization` already live in
`Loom.Web.Api.csproj`; don't re-pass them, or the csproj stops being the single source of
truth for how the shipped binary is built.

```bash
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj --configuration Release -r linux-x64

# Verify the binary stays under the 17 MB hard limit (see BACKLOG.md § 2.1)
ls -lh Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api
```

The output is already stripped, with debug symbols split into a separate
`Loom.Web.Api.dbg`, so `strip --strip-debug` buys nothing. A correct AOT publish contains
**no managed assemblies** — if a `Loom.Web.Api.dll` appears beside the native binary, the
publish silently fell back to a managed build and any size check is measuring the wrong
file.

**Measured:** 14.706 MB `linux-x64`, 15.108 MB `win-x64`. Linux runs ~400 KB smaller;
the two RIDs are not directly comparable, so measure whichever you publish.

---

## Verification Checklist

```bash
# 1. No trim/AOT warnings, solution-wide. This passes as of 3c6a660: Loom.Dashboard now
#    sets EnableRequestDelegateGenerator, which cleared 24 IL2026 errors by replacing
#    reflective delegate binding with compile-time interceptors. Expect 0 errors and 4
#    known warnings - 2 xUnit1031 in InMemoryMetricStoreTests, and 2 NETSDK1212 because
#    the netstandard2.0 generator project cannot use the trim analyzer. Leave all four.
dotnet build Loom.slnx -c Release /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true

# 2. Native AOT compiles (on Linux; AOT properties live in the csproj)
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj -c Release -r linux-x64

# 3. Binary size - hard limit 17 MB. The original <15 MB target covered only the
#    Phases 0-4 diagnostic core, before the query parser, alerting engine, exporters and
#    collector plugin system existed; that budget was retired (see BACKLOG.md §2.1).
#    Currently 14.706 MB linux-x64 / 15.108 MB win-x64.
ls -lh Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api

# 4. IL + AOT tests pass - baseline 592 passing, 0 skipped
dotnet test Loom.slnx --configuration Debug

# 5. Frontend - baseline 3 files, 94 passing
cd Loom.Web.Frontend && npx ng test

# 6. Zero allocations in hot paths
dotnet-counters monitor --process-id $(pidof Loom.Web.Api) System.Runtime
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
| 11 | Alerting/Thresholds | ✅ Complete | `Loom.Telemetry.Alerting/` + `Alerting/` tests + `PHASE-11-COMPLETE.md` |
| 12 | Exporters | ✅ Complete | Prometheus, Console. Grafana Cloud and Elasticsearch were removed as non-functional dead code — see BACKLOG.md § 9 |
| 13 | Local Development Mode | ✅ Complete | `loom dev` — `Loom.DevTools/Commands/DevCommand.cs` |

### Production Hardening

| Phase | System | Status | Description |
|-------|--------|--------|-------------|
| 14 | Security Hardening | ✅ Complete | Manual JWT in `Loom.Security` — login endpoint, PBKDF2 credentials, every endpoint enforced, scoped service tokens, Angular auth. Loopback bind in code; **in-process TLS was evaluated and rejected** (see Security below) |
| 15 | Production Build & Deployment | In progress | **15.1 build** ✅ — Linux AOT binary built and smoke-tested. **15.3 CI/CD** ✅ — see below. **15.2 systemd** ⏳ — units, the `loomd` user, and secrets provisioning remain |

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
  verified on both Windows and Linux, where `ASPNETCORE_URLS=http://0.0.0.0:5080` is
  overridden and Kestrel logs that it discarded the value. A remote operator reaches it
  through an SSH tunnel, which already encrypts the only hop that leaves the machine.
  In-process TLS was built, measured at **+0.946 MB** against a 17 MB ceiling, and
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
- Key material **fails closed**: a missing signing key, a missing users file, or a users
  file defining zero users aborts startup with an actionable message. No
  generated-on-the-fly fallback exists in any environment
- Strict CORS whitelist (no wildcards)
- Security headers (CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy),
  applied at the front of the pipeline so short-circuiting middleware cannot skip them
- systemd sandboxing (`ProtectSystem=strict`, `MemoryDenyWriteExecute`, etc.)
- Runs as unprivileged user `loomd` with least privilege
- JWT secret in `/var/secrets/loom/jwt.key` (mode 400)

---

## Continuous Integration

`.github/workflows/ci.yml` runs on every push to `main` and every PR:

| Job | What it does |
|-----|--------------|
| Build & test (ubuntu + windows) | Restore, strict Release build with trim/AOT analyzers as errors, full test suite, source-generator tests |
| Angular tests | `npm ci`, `ng test`, and a production bundle build |
| Native AOT publish (linux-x64) | Installs `clang` + `zlib1g-dev`, publishes, enforces the 17 MB size gate, asserts the output is genuinely native, uploads the binary as an artifact |

The AOT job exists because Native AOT cannot cross-compile — it is the only way the Linux
artifact gets built in CI.

---

## Documentation

| Document | Role |
|----------|------|
| [`IMPLEMENTATION-METHODOLOGY.md`](./IMPLEMENTATION-METHODOLOGY.md) | Authoritative step-by-step build guide (all 9 systems, full code) |
| [`wiggly-noodling-hoare.md`](./wiggly-noodling-hoare.md) | Architecture decisions, deployment config, design rationale |
| [`BACKLOG.md`](./BACKLOG.md) | Open items, decision log, and the measurements behind each decision |
| [`TESTING.md`](./TESTING.md) | Test strategy and coverage |
| [`SMOKE-TEST.md`](./SMOKE-TEST.md) | Manual end-to-end verification steps |
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
