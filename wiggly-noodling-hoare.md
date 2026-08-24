# Project Loom v2: Architecture Decisions & Design Rationale

> **Document Role:** Architecture decisions, phase overviews, deployment configuration, and design rationale.
> For step-by-step implementation with code samples, see `IMPLEMENTATION-METHODOLOGY.md` (authoritative build guide).

> ### ⚠️ Corrections (2026-08-24)
>
> This document records decisions as they were made. Delivery diverged in three ways; the
> ADR text below is left intact as the design record, and these override it for anything
> present-tense. `CLAUDE.md` → **Project Structure (Actual)** is authoritative on the
> current codebase.
>
> 1. **`Loom.Core` and `Loom.Host` were never built.** The structure diagram at L35 lists
>    them; neither project exists. `Loom.Web.Api` is the Native AOT publish target, and
>    `Loom.Dashboard` (`loom-dashboard`) and `Loom.DevTools` (`loom`) are the packaged
>    dotnet tools. There is no separate bootstrap host and no SIMD math engine.
> 2. **The Grafana Cloud and Elasticsearch exporters were deleted** (commit `6d8cc2b`).
>    ADR-9's push-exporter table (L273-274) and the interoperability claim at L583 still
>    describe them. Both were built, found non-functional against their real targets, never
>    registered in any host, and removed. **Console plus the Prometheus formatter are the
>    entire exporter surface.** The pull-vs-push distinction ADR-9 draws still holds — only
>    the pull side survives. See `BACKLOG.md` § 9.
> 3. **`Loom.Storage` is in-memory only** — ring buffers for metrics and logs. No
>    memory-mapped binary cache, no RAG ingestor.

## Context

Project Loom v2 is a **customizable telemetry platform** for .NET applications built entirely on .NET 10 Native AOT. The current scope is **.NET backend only** — nine telemetry systems delivered as a single AOT-compiled binary with zero runtime reflection.

### Core Constraints (Inviolable)

- .NET 10 Native AOT compilation (reflection-free)
- Binary size < 15 MB, memory footprint < 20 MB
- Zero-allocation hot paths (`ReadOnlySpan<T>`, `ValueTask`, `ArrayPool<T>`)
- Source-generated JSON only (`LoomJsonSerializerContext`)
- Minimal APIs only, raw WebSockets (no SignalR), manual JWT
- Security hardening (systemd sandboxing, least privilege)

---

## Solution Architecture

### Solution Structure

```
Loom.slnx
├── Loom.Web.Api/                  → ASP.NET Core Minimal APIs (Native AOT production host)
├── Loom.Web.Contracts/            → Shared DTOs + source-generated JSON
├── Loom.Web.RealTime/             → Zero-allocation WebSocket handlers
├── Loom.Storage/                  → Ring-buffer metric store (in-memory)
├── Loom.Telemetry/                → Custom Metrics API runtime (metrics, collectors, sampling)
├── Loom.Telemetry.Generators/     → C# source generator (analyzer project)
├── Loom.Telemetry.Query/          → Query engine (SQL-like tokenizer/parser/planner/executor)
├── Loom.Telemetry.Alerting/       → Alert rules, conditions, notifications
├── Loom.Telemetry.Exporters/      → Prometheus, Grafana Cloud, Elasticsearch, Console
├── Loom.DevTools/                 → `dotnet loom dev` CLI tool
├── Loom.Telemetry.Tests/          → Unit & integration tests
└── Loom.Dashboard/                → `loom-dashboard <pid>` dev-time CLI tool (embeds Angular dashboard)
```

**ADR — undocumented substitutions from the original design:**
1. `Loom.Core` (planned SIMD engine, AVX2/Neon) → superseded; `Loom.Storage`'s ring buffers
   supply real metric data directly, no SIMD math layer was needed.
2. `Loom.Telemetry.Collectors` (planned standalone project) → folded into `Loom.Telemetry`
   (`LoomCollectors.cs`, `CollectorSnapshot.cs`).
3. `Loom.Host` (planned bootstrap entry point) → replaced by `Loom.Web.Api`, which is the
   only project carrying `<PublishAot>true</PublishAot>`.
4. `Loom.Dashboard` (undocumented in the original design) → **decided: kept**, as the
   Phase 16 dashboard host. It is a separate dev-time CLI tool (`PackAsTool`), not a rival
   to `Loom.Web.Api`; it embeds the Angular frontend and attaches to a target PID via
   `EventPipeBridge.cs`. See Phase 16 status below.

### Dependency Flow

```
Loom.Web.Api            → Loom.Storage, Loom.Telemetry.Exporters, Loom.Telemetry.Query,
                           Loom.Telemetry.Alerting, Loom.Web.Contracts, Loom.Web.RealTime
Loom.Web.RealTime       → Loom.Web.Contracts
Loom.Storage             → Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry            (no project refs; DI abstractions package only)
Loom.Telemetry.Query    → Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry.Alerting → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query, Loom.Web.Contracts
Loom.Telemetry.Exporters→ Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.DevTools           → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query, Loom.Web.Contracts
Loom.Dashboard          → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                           Loom.Telemetry.Alerting, Loom.Telemetry.Exporters,
                           Loom.Web.Contracts, Loom.Web.RealTime

Loom.Telemetry.Generators (analyzer — referenced as <Analyzer>, no runtime dependency)
```

---

## Implementation Phase Overview

### Foundation (Phases 0–4) — Complete

| Phase | System | Notes |
|-------|--------|-------|
| 0 | Project Setup & Tooling | SDK, solution structure |
| 1 | Contracts & JSON Serialization | DTOs + LoomJsonSerializerContext |
| 2 | Web API Core | Minimal API, health endpoint, Kestrel |
| 3 | Core Metrics Endpoints | CPU, Memory, Thread APIs |
| 4 | WebSocket Real-Time Streaming | Zero-alloc WebSocket layer |

### Telemetry Platform (Phases 5–13) — Current Focus

| Phase | System | Depends On |
|-------|--------|-----------|
| 5 | Source Generator | — (enables 7, 8) |
| 6 | Custom Metrics API | — |
| 7 | Attribute-Based Instrumentation | Phase 5 |
| 8 | Custom Collectors/Plugins | Phases 5, 6 |
| 9 | Configuration-Driven Sampling | Phase 6 |
| 10 | Query Language | Phase 6 |
| 11 | Alerting/Thresholds | Phase 10 |
| 12 | Exporters | Phase 6 |
| 13 | Local Development Mode | All above |

### Production Hardening (Phases 14–15)

| Phase | System |
|-------|--------|
| 14 | Security Hardening (JWT, HTTPS, systemd) |
| 15 | Production Build & Deployment (CI/CD) |

---

## Architecture Decisions

### ADR-1: Why Minimal APIs Instead of MVC Controllers?

**Chosen:** ASP.NET Core Minimal APIs with explicit lambda-based registration.

**Rejected:** MVC Controllers.

**Why:** Native AOT cannot compile MVC controllers due to:
- Attribute-based routing requires reflection for endpoint discovery
- Model binding uses `TypeDescriptor` and runtime type inspection
- Filter pipeline relies on `IActionFilter` discovery via assembly scanning

Minimal APIs use explicit `app.MapGet(...)` calls — zero reflection, fully AOT-compatible.

### ADR-2: Why Raw WebSockets Instead of SignalR?

**Chosen:** Native .NET WebSocket API with manual frame handling.

**Rejected:** SignalR.

**Why:** SignalR requires:
- Reflection-based hub method discovery (`HubMethodNameAttribute`)
- Dynamic proxy generation for strongly-typed client calls
- Runtime protocol negotiation (inspects types at runtime)

Raw WebSockets give complete control over serialization (source-generated JSON into pooled buffers) with zero allocation.

### ADR-3: Why Manual JWT Instead of `System.IdentityModel.Tokens.Jwt`?

**Chosen:** Manual HS256 implementation using `System.Security.Cryptography.HMACSHA256`.

**Rejected:** `System.IdentityModel.Tokens.Jwt`, `Microsoft.AspNetCore.Authentication.JwtBearer`.

**Why:** The standard JWT libraries use:
- Reflection for claim deserialization
- `TypeDescriptor` for token validation
- Assembly scanning for authentication scheme discovery

Manual implementation uses `Span<byte>`, `stackalloc`, and explicit parsing — fully AOT-compatible and zero-allocation.

### ADR-4: Why a Roslyn Source Generator for Instrumentation?

**Chosen:** C# Incremental Source Generator (`IIncrementalGenerator`) in a separate analyzer project.

**Rejected alternatives:**
- **Fody/IL Weaving** — post-compilation IL rewriting is fragile with Native AOT's trimmer; trimmed methods may disappear before weaving runs. Also requires `Mono.Cecil` which has AOT compatibility issues.
- **PostSharp** — commercial, uses runtime reflection for aspect activation, heavy binary overhead.
- **Runtime reflection (`MethodInfo.Invoke`)** — impossible under Native AOT.
- **`System.Reflection.Emit`** — impossible under Native AOT (no JIT).
- **ConditionalWeakTable / DynamicMethod** — runtime codegen, not AOT-compatible.

**Why source generators win:**
- Run at **compile time** — output is plain C# that AOT compiles normally
- Roslyn incremental generators are cached (fast rebuild)
- Output is inspectable (generated `.g.cs` files in `obj/`)
- No runtime dependency — the generator assembly is an analyzer, not shipped in the binary
- Zero overhead: generated code is as fast as hand-written code

**How it avoids reflection:** The generator reads `[LoomProfile]`/`[LoomTrack]` attributes via Roslyn's `SyntaxNode`/`SemanticModel` API at compile time, then emits wrapper methods with explicit `Stopwatch.GetTimestamp()` calls. No `MethodInfo`, no `Attribute.GetCustomAttributes()` at runtime.

**Key constraint:** The generator project targets `netstandard2.0` (Roslyn analyzer requirement) and uses `Microsoft.CodeAnalysis.CSharp` APIs only.

### ADR-5: Custom Metrics API — Ring Buffer Storage

**Chosen:** Fixed-size ring buffer (`MetricRingBuffer<T>`) per metric, backed by pre-allocated arrays.

**Why:**
- **Zero-allocation writes** — the ring buffer overwrites the oldest entry; no resize, no GC pressure
- **Bounded memory** — configurable window size (default 4096 entries per metric) keeps memory under 20 MB
- **Lock-free reads** — use `Interlocked` for head/tail pointer; readers get a snapshot without blocking writers
- **AOT-compatible** — generic over value types (`struct`), no boxing

**Design:**
- Each metric name + tag combination maps to one ring buffer instance
- Tags are interned (deduplicated) at registration time via a `ConcurrentDictionary<TagKey, int>`
- `TagKey` is a readonly struct with precomputed hash (no per-write allocation)
- Metric values are stored as `MetricEntry` structs: `{ long Ticks, double Value, int TagIndex }`

**Rejected:** Time-series databases (InfluxDB, TimescaleDB) — external dependency, network overhead, binary size. The ring buffer is purpose-built for "recent window" queries at zero cost.

### ADR-6: Collector Plugin Architecture

**Chosen:** `ILoomCollector` interface with explicit `AddCollector<T>()` DI registration.

**Rejected:**
- Assembly scanning (`[Export]` attributes, MEF) — requires reflection
- Plugin DLLs loaded at runtime (`Assembly.LoadFrom`) — incompatible with AOT trimming

**How it avoids reflection:** Users call `services.AddLoomCollector<RedisCollector>()` which is a generic extension method resolved at compile time. The DI container stores a `Func<IServiceProvider, ILoomCollector>` factory — no runtime type discovery.

**Interface contract:**
```csharp
public interface ILoomCollector
{
    string Name { get; }
    TimeSpan CollectionInterval { get; }
    ValueTask<CollectorSnapshot> CollectAsync(CancellationToken ct);
}
```

**Key constraints:**
- Collectors are `sealed class` (AOT devirtualization)
- `CollectAsync` returns `ValueTask` (zero-alloc when synchronous)
- `CollectorSnapshot` uses pre-allocated `MetricTag[]` arrays

### ADR-7: Query Engine Design (Tokenizer → Parser → AST → Executor)

**Chosen:** Hand-written recursive descent parser that produces an AST, executed in-memory over the ring buffer.

**Rejected:**
- **ANTLR** — generates reflection-heavy parsers; runtime `Type.GetType()` calls for AST nodes
- **Sprache/Superpower** — LINQ-heavy (allocations in hot paths), limited AOT compatibility
- **Roslyn scripting** — requires JIT, impossible under AOT
- **SQLite** — external native dependency, binary size (~1.5 MB), overkill for in-memory metric queries

**Design:**
1. **Tokenizer** — `ReadOnlySpan<char>` lexer, yields `Token` structs (no string allocation for keywords)
2. **Parser** — recursive descent, produces `QueryAst` (discriminated union of node types)
3. **Planner** — resolves metric names to ring buffer references, validates time ranges
4. **Executor** — iterates ring buffer entries matching `WHERE` predicates, applies `GROUP BY`/aggregates

**How it avoids reflection:** All AST node types are `sealed record` structs known at compile time. The executor uses `switch` on a `NodeKind` enum, not `visitor.Visit(dynamic node)`.

**Fluent API:**
```csharp
var results = await _loom.Query()
    .Where(e => e.Name == "OrderProcessingTime")
    .Last(TimeSpan.FromMinutes(15))
    .GroupBy(e => e.Tags["Region"])
    .OrderByDescending(g => g.Average(e => e.Value))
    .Take(10)
    .ExecuteAsync();
```

The fluent API builds the same `QueryAst` as the SQL parser, then hands it to the executor. Expression trees are NOT used (`Expression<Func<>>` requires `System.Linq.Expressions` which uses reflection). Instead, the fluent API uses method chaining that builds the AST via explicit method calls.

### ADR-8: Alert System — Sliding Window Evaluation

**Chosen:** Time-bucketed sliding window with periodic tick evaluation.

**Design:**
- Alert conditions are registered at startup via `AddAlert()` in DI configuration
- A background `Timer` (period = smallest window / 10) ticks and evaluates all conditions
- Each alert maintains a circular buffer of recent values within its window
- Condition evaluation: compare aggregate (count, avg, max, p99) against threshold
- Notification dispatch: fire-and-forget via `Channel<AlertNotification>` to avoid blocking evaluation

**Notification targets:**
- **Webhook** — `HttpClient` POST with JSON body (pooled, zero-alloc serialization)
- **Email** — SMTP via `SmtpClient` (or raw socket for AOT safety)
- **Console/Log** — `ILogger` output (for dev mode)

**How it avoids reflection:** Alert conditions are `Func<MetricAggregate, bool>` delegates compiled at registration time (not expression trees). Notification targets implement `IAlertTarget` interface, registered via `AddAlertTarget<T>()`.

**Key constraint:** The evaluation loop must complete in < 1ms for 100 alerts with 5-minute windows. Ring buffer enables O(1) window access.

### ADR-9: Exporter Design — Push vs Pull

**Chosen:** Both push and pull models, configurable per exporter.

| Exporter | Model | Protocol |
|----------|-------|----------|
| Prometheus | **Pull** | GET `/metrics` returns OpenMetrics text format |
| Grafana Cloud | **Push** | HTTP POST to remote write endpoint (batched) |
| Elasticsearch | **Push** | Bulk index API (batched, buffered) |
| Console | **Push** | `ILogger` / stdout (immediate) |

**Batching strategy:**
- Push exporters accumulate metrics in a `Channel<MetricBatch>` (bounded, backpressure)
- A background flush loop sends batches every N seconds or when buffer is full
- On backpressure: drop oldest metrics (ring buffer semantics — newest wins)

**How it avoids reflection:**
- Prometheus format: hand-written `Utf8JsonWriter`-style text formatter (no `ToString()` reflection)
- JSON exporters: use `LoomJsonSerializerContext` source-generated serialization
- No plugin discovery — exporters are registered via `options.Export.ToPrometheus()` (compile-time generic)

**Key constraint:** Export must never block the metrics hot path. The `Channel<T>` decouples production from export.

### ADR-10: Sampling Strategy — Decision Point & Hot-Reload

**Chosen:** Decision at metric write time, driven by `IOptionsMonitor<SamplingConfig>` (hot-reloadable from `appsettings.json`).

**Design:**
- Default sample rate (e.g., 0.1 = 10% of events recorded)
- Rule overrides by path pattern or duration threshold
- Duration-based rules apply retroactively: if a request takes > threshold, force-record even if initially skipped (requires buffering the decision until completion)

**Configuration shape:**
```json
{
  "Loom": {
    "Sampling": {
      "Default": 0.1,
      "Rules": [
        { "Path": "/api/critical/*", "Rate": 1.0 },
        { "Path": "/health", "Rate": 0.01 },
        { "Duration": "> 1000ms", "Rate": 1.0 }
      ]
    }
  }
}
```

**How it avoids reflection:** `IOptionsMonitor<T>` with source-generated JSON binding (`JsonSerializerContext` includes `SamplingConfigDto`). Path matching uses `ReadOnlySpan<char>.StartsWith()` — no regex, no allocation.

**Hot-reload:** ASP.NET Core's `IOptionsMonitor` fires `OnChange` when `appsettings.json` is modified. The sampling decision table is rebuilt atomically (swap a reference, no locking on the hot path).

### ADR-11: Local Dev Mode — Process Discovery

**Chosen:** `dotnet loom dev` as a .NET global tool that discovers running .NET apps via diagnostics IPC.

**Design:**
- Uses .NET Diagnostics IPC (named pipes on Windows, Unix domain sockets on Linux) to discover running .NET processes
- Attaches to `EventPipe` sessions for real-time metric streaming
- Outputs to terminal (structured JSON or formatted table — no browser needed)
- Zero configuration: discovers all .NET apps on localhost automatically

**Rejected:**
- HTTP polling of each app — requires Loom to be pre-installed in target apps
- Shared memory — platform-specific, complex setup
- gRPC — binary size overhead (~2 MB), reflection in serialization

**How it avoids reflection:** `EventPipe` and diagnostics IPC are low-level APIs that use binary protocols (not reflection). The dev tool emits pre-formatted output via `Console.Write` with pooled `StringBuilder`.

**Key constraint:** The dev tool is a SEPARATE project (`Loom.DevTools`) that can run standalone. It doesn't require the full `Loom.Host` server — it's a lightweight CLI.

---

## Technology Stack Detail

### Backend (.NET 10 Native AOT)

- **ASP.NET Core 10.0 Minimal APIs** — only Native AOT-compatible API approach
- **Kestrel HTTP server** — Native AOT optimized
- **System.Text.Json with source generators** — zero reflection JSON serialization
- **WebSockets (native)** — for real-time streaming (NOT SignalR)
- **Manual JWT authentication** — HS256, Span-based
- **Roslyn Source Generators** — compile-time instrumentation
- **System.Threading.Channels** — backpressure-aware async producer/consumer
- **IOptionsMonitor** — configuration hot-reload (built-in, AOT-safe)

### Build Tools

- .NET 10 SDK (10.0.100+)
- LLVM/Clang 19 (Linux native compilation)
- MSVC v143 (Windows native compilation)

---

## Testing Strategy

### Unit Tests (IL mode, fast iteration)

```bash
dotnet test Loom.slnx --configuration Debug
```

- DTO serialization/deserialization roundtrip
- Source generator output verification (compile generated code, verify behavior)
- Query parser: tokenize → parse → AST correctness
- Alert condition evaluation logic
- Sampling decision logic
- Exporter format correctness (Prometheus text format, JSON batch)

### Integration Tests

- API endpoint responses (status codes, JSON format)
- WebSocket connection lifecycle
- Custom metric recording → query → result pipeline
- Alert triggering → notification dispatch
- Exporter batching and flush behavior
- Collector registration and periodic collection

### Native AOT Verification

```bash
dotnet publish Loom.Web.Api -c Release -r linux-x64 /p:PublishAot=true
# Run compiled binary and exercise all endpoints
```

- Verify zero trim warnings (IL2026, IL3050)
- Verify all source-generated JSON works at runtime
- Verify source generator output compiles under AOT

### Performance Tests

```bash
# NOT YET IMPLEMENTED — Loom.Benchmarks project does not exist
```

**Targets:**
- `RecordMetric()`: < 100ns, 0 allocations
- `GET /api/metrics/cpu`: < 5ms, zero *avoidable* allocations (the response DTO graph
  itself — `CpuMetricResponse`, `CpuHotpath` — is excluded; returning a `record` response
  allocates by definition, the target is no allocation *beyond* that graph)
- WebSocket frame send: < 10ms, 0 allocations
- Query execution (10K entries, GROUP BY): < 10ms
- Alert evaluation (100 alerts): < 1ms
- Prometheus scrape (1000 metrics): < 50ms

### Load Tests

```bash
# NOT YET IMPLEMENTED — load-tests/ directory does not exist
# Target: 10k req/sec with p95 latency < 50ms
```

---

## Deployment

### Systemd Service

```ini
[Unit]
Description=Loom Telemetry Platform
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
User=loomd
Group=loomd
WorkingDirectory=/opt/loom

ExecStart=/opt/loom/Loom.Web.Api
Restart=on-failure
RestartSec=10

Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="ASPNETCORE_URLS=https://+:5443;http://+:5080"
Environment="LOOM_JWT_SECRET_PATH=/var/secrets/loom/jwt.key"

# Security Hardening
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
MemoryDenyWriteExecute=true
NoNewPrivileges=true
PrivateDevices=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true

# File System Access
ReadWritePaths=/var/cache/loom
ReadOnlyPaths=/var/secrets/loom

# Network
RestrictAddressFamilies=AF_INET AF_INET6

# Capabilities
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE

# Resource Limits
LimitNOFILE=65536
MemoryMax=512M

[Install]
WantedBy=multi-user.target
```

### Deployment Script

```bash
#!/bin/bash
set -e

# Build production binary
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj \
  --configuration Release \
  -r linux-x64 \
  /p:PublishAot=true \
  /p:StripSymbols=true

strip --strip-debug Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api

# Create loomd user
sudo useradd -r -s /bin/false loomd

# Create directories
sudo mkdir -p /opt/loom /var/cache/loom /var/secrets/loom

# Copy binary
sudo cp Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api /opt/loom/
sudo chown root:root /opt/loom/Loom.Web.Api
sudo chmod 555 /opt/loom/Loom.Web.Api

# Permissions
sudo chown loomd:loomd /var/cache/loom
sudo chmod 700 /var/cache/loom
sudo chown root:loomd /var/secrets/loom
sudo chmod 550 /var/secrets/loom

# JWT secret
openssl rand -base64 32 | sudo tee /var/secrets/loom/jwt.key > /dev/null
sudo chmod 400 /var/secrets/loom/jwt.key
sudo chown root:loomd /var/secrets/loom/jwt.key

# Install service
sudo cp loom-web.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now loom-web.service
```

### Security Model

- Unprivileged user: `loomd`
- Systemd sandboxing (strict)
- File access: `/var/cache/loom/` (700), `/var/secrets/loom/jwt.key` (400)
- Network: HTTPS (port 5443), HTTP redirect (port 5080)
- Authentication: Manual JWT (HS256)
- CORS: Strict whitelist (no wildcards)

---

## Risk Register

| Risk | Severity | Mitigation |
|------|----------|------------|
| Binary size bloat (> 15 MB) | HIGH | Aggressive trimming, `InvariantGlobalization`, strip symbols, minimal API surface |
| Source generator complexity / debugging | MEDIUM | Incremental generator with unit tests on generated output; inspectable `.g.cs` files |
| Query engine performance on large datasets | MEDIUM | Ring buffer bounds total data; indexes on metric name + tag; benchmark in CI |
| WebSocket connection leaks | MEDIUM | `CancellationToken` cleanup, connection tracking, `IDisposable` pattern |
| Alert evaluation blocking hot path | HIGH | Decouple via `Channel<T>`; evaluation runs on dedicated thread |
| Exporter backpressure causing memory growth | MEDIUM | Bounded channels; drop-oldest semantics on overflow |
| Sampling decision overhead on every request | LOW | Decision is one `Random.Shared.NextDouble()` comparison; < 10ns |
| `IOptionsMonitor` hot-reload race condition | LOW | Atomic reference swap; readers see consistent snapshot |
| Dev mode named pipe permissions (cross-platform) | MEDIUM | Fall back to TCP localhost if IPC unavailable; document required permissions |
| Native AOT trim removes collector types | HIGH | Explicit `AddCollector<T>()` registration preserves types; no assembly scanning |
| Query parser injection attacks | MEDIUM | Parser produces typed AST (no string concatenation); input sanitized at tokenizer level |
| CORS misconfiguration | MEDIUM | Strict whitelist, validate Origin header, test with security scanners |
| JWT secret compromise | HIGH | `/var/secrets/loom/` with 400 perms, rotate regularly, strong random keys |

---

## Verification Checklist

### Functional Requirements
- [ ] All 9 telemetry systems operational
- [ ] Custom metrics recordable and queryable
- [ ] Source generator produces correct instrumentation wrappers
- [ ] Collectors run on schedule and report snapshots
- [ ] Query engine handles SQL-like and fluent API
- [ ] Alerts fire and dispatch notifications
- [ ] Exporters push/pull metrics to external systems
- [ ] Sampling reduces throughput without data loss for slow requests
- [ ] Dev mode discovers and streams from local apps

### Performance Requirements
- [ ] Binary size < 15 MB
- [ ] Memory usage < 20 MB idle
- [ ] Zero allocations in: RecordMetric, WebSocket send, Prometheus scrape
- [ ] API latency < 10ms (p95)
- [ ] Query execution < 10ms for standard queries
- [ ] Alert evaluation < 1ms for 100 active alerts

### AOT Requirements
- [ ] Zero trim warnings (IL2026, IL3050)
- [ ] All DTOs registered in `LoomJsonSerializerContext`
- [ ] No reflection usage anywhere in runtime code
- [ ] Source generator output compiles cleanly under AOT

---

## Success Criteria

- **Functional:** All 9 telemetry systems testable via curl/API without any frontend
- **Performance:** Binary < 15 MB, memory < 20 MB idle, zero allocations in hot paths
- **Security:** JWT authentication, HTTPS, systemd sandboxing
- **Developer Experience:** `dotnet loom dev` provides zero-config local metrics
- **Interoperability:** Metrics exportable to Prometheus, Grafana, Elasticsearch

---

## Phase 16: Frontend (Now In Scope)

**Status Change:** Frontend work has been **un-deferred** as of Phase 16.

The Angular dashboard is now being built using the existing scaffolding at `Loom.Web.Frontend/`:
- Angular 21 with standalone components
- **Apache ECharts** replacing Chart.js (richer visualizations)
- **Dark theme with teal accent** (`#14b8a6`)
- WebSocket-based real-time streaming
- Multi-page SPA: Dashboard, Metrics Explorer, Query Builder, Alerts, Exporters

See `PHASE-16-DASHBOARD.md` for complete implementation details.

### Still Deferred (Future Phases)

The following remain out of scope for Phase 16:

**Custom Dashboard Widgets (`@LoomWidget` plugin system):**
- Component-based widget registration
- Custom chart renderers
- Widget layout persistence
- **Reason:** Requires Angular plugin infrastructure (Phase 17+)

**Query Builder UI Enhancements:**
- Field autocomplete from registered metrics
- Visual query condition builder
- **Reason:** Phase 16 builds a basic LoomQL editor; advanced features are Phase 17+

**Mobile App (PWA/React Native):**
- **Reason:** Depends on frontend (Phase 17+)

### Backend API Readiness

All backend APIs needed for Phase 16 are complete:
- Phases 1-4: Core metrics, health, WebSocket streaming
- Phase 10: Query API (`POST /api/query`)
- Phase 11: Alerts API (`GET /api/alerts`, test, silence)
- Phase 12: Exporters API (`GET /api/exporters/status`, metric names)
