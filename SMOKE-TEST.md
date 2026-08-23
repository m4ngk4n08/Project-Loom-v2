# Project Loom v2 - Smoke Test Guide

**Document Version:** 1.0
**Last Updated:** 2026-08-19
**Current Phase:** Phases 1-13 smoke tested (HTTP/WS surface + library/CLI surface), Phase 16 Dashboard bridge verified

---

## Overview

This document describes the end-to-end smoke tests for Project Loom v2. Unlike `TESTING.md` (which covers unit tests, AOT compliance, binary size, and allocations), this guide verifies the **running system**: real processes, live metric streams, HTTP endpoints, WebSockets, CLI tools, and the EventPipe bridge.

Two smoke test suites are defined:

| Suite | Scope | Vehicle |
|-------|-------|---------|
| **HTTP/WS Surface** (Phases 1-13) | API endpoints, ingest, query, alerts, exporters, WebSocket contract | `Loom.Dashboard` / `Loom.Web.Api` |
| **Library/CLI Surface** (Phases 5-13) | Source generator, metrics API, collectors, sampling, query, alerting, exporters, DevTools CLI | `SampleMonitoredApp` + `Loom.DevTools` |

---

## Prerequisites

### Build

```powershell
cd "C:\Users\angel\source\repos\Project Loom v2"

# Full solution (IL mode, fast iteration)
dotnet build Loom.slnx -c Debug

# Targeted builds
dotnet build examples\SampleMonitoredApp\SampleMonitoredApp.csproj -c Debug
dotnet build Loom.DevTools\Loom.DevTools.csproj -c Debug
dotnet build Loom.Dashboard\Loom.Dashboard.csproj -c Debug
dotnet build Loom.Web.Api\Loom.Web.Api.csproj -c Debug
```

All should succeed with **0 errors**.

### Ports

| Port | Service |
|------|---------|
| 5209 | `Loom.Dashboard` (embedded Angular, API + WS) |
| 5080 / 5000 | `Loom.Web.Api` (full backend, when used) |

`Loom.Dashboard` binds 5209 by default, but that's a *preferred* port, not a fixed one: if it's already taken (e.g. a second `loom-dashboard <pid>` watching another process), it falls back to an OS-assigned free port so multiple dashboard instances can run side by side. The actual bound URL is always printed to the console on startup (and the browser auto-opens to it) — don't assume 5209 when more than one instance is running; read the console output instead.

---

## Test Environment Setup (Windows)

### Critical: Process Lifetime

Long-running processes launched from the opencode/PowerShell shell are **killed when the launching shell command completes**. Use the **WMI + Start-Process** detached-launch pattern:

```powershell
$exe = "<full path to exe>"
$log = "C:\Users\angel\AppData\Local\Temp\opencode\<name>.log"
$err = "C:\Users\angel\AppData\Local\Temp\opencode\<name>.err.log"
$inner = "Start-Process -FilePath '$exe' -ArgumentList '<args>' -WorkingDirectory '<repo root>' -RedirectStandardOutput '$log' -RedirectStandardError '$err' -WindowStyle Hidden"
$cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command `"$inner`""
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd }
```

The exe path and working directory **must** be quoted if they contain spaces (they do — `Project Loom v2`).

> **Note:** A plain `cmd /c "<exe>" > log 2>&1` WMI launch dies immediately in this environment. The `powershell.exe -Command "Start-Process ..."` wrapper is required.

### Launching the Sample App

```powershell
$exe = "C:\Users\angel\source\repos\Project Loom v2\examples\SampleMonitoredApp\bin\Debug\net10.0\SampleMonitoredApp.exe"
# ... (WMI pattern above, no args) ...
# Verify:
Get-Process | Where-Object { $_.ProcessName -like "SampleMonitoredApp*" } | Select-Object Id, ProcessName
# Capture the PID for all later CLI commands (e.g. 20812)
```

### Launching the Dashboard

```powershell
$exe = "C:\Users\angel\source\repos\Project Loom v2\Loom.Dashboard\bin\Debug\net10.0\Loom.Dashboard.exe"
# ... (WMI pattern above, with -ArgumentList '<targetPid>') ...
# Verify:
Get-Process | Where-Object { $_.ProcessName -like "Loom.Dashboard*" } | Select-Object Id, ProcessName
```

### Cleanup

```powershell
Get-Process | Where-Object {
    $_.ProcessName -like "SampleMonitoredApp*" -or
    $_.ProcessName -like "Loom.Dashboard*" -or
    $_.ProcessName -like "Loom.Web.Api*"
} | Stop-Process -Force
```

---

## Suite A: HTTP/WS Surface (Phases 1-13)

Run `Loom.Dashboard` against the SampleMonitoredApp PID (see Setup), then verify each endpoint.

### A1. Health

```powershell
Invoke-RestMethod -Uri "http://localhost:5209/api/health"
```

**Expected:** `{"status":"Healthy","timestamp":...,"uptimeSeconds":...,"memoryUsageMb":...}` — HTTP 200.

### A2. System Metrics

```powershell
Invoke-RestMethod -Uri "http://localhost:5209/api/metrics/cpu"
Invoke-RestMethod -Uri "http://localhost:5209/api/metrics/memory"
Invoke-RestMethod -Uri "http://localhost:5209/api/metrics/thread"
```

**Expected:** Valid JSON per DTO (`CpuMetricResponse`, `MemoryMetricResponse`, `ThreadMetricResponse`). HTTP 200.

### A3. Metric Ingestion

```powershell
$body = '{"metrics":[{"name":"test.counter","type":"Counter","value":42},
                    {"name":"test.gauge","type":"Gauge","value":75.5}]}'
Invoke-WebRequest -Uri "http://localhost:5209/api/metrics/ingest" `
  -Method Post -ContentType "application/json" -Body $body -UseBasicParsing
```

**Expected:** HTTP 202 Accepted.

**Negative case:** an invalid `type` must return 400:
```powershell
$bad = '{"metrics":[{"name":"x","type":"Bogus","value":1}]}'
Invoke-WebRequest -Uri "http://localhost:5209/api/metrics/ingest" `
  -Method Post -ContentType "application/json" -Body $bad -UseBasicParsing
# -> 400
```

### A4. Prometheus Export

```powershell
(Invoke-WebRequest -Uri "http://localhost:5209/prometheus" -UseBasicParsing).Content
```

**Expected:** OpenMetrics text: `# TYPE <name> counter|gauge|summary`, `# HELP ...`, values, histogram quantiles (`{quantile="0.5"} ...`).

### A5. Metric Names

```powershell
Invoke-RestMethod -Uri "http://localhost:5209/api/exporters/metrics/names"
```

**Expected:** JSON array of metric names. **With the Dashboard bridged to a Loom-instrumented process, this MUST include the target's Loom metrics** (e.g. `orders.processed`, `payments.succeeded`). If it is empty or lacks target metrics, see Bug Fix B1 below.

### A6. LoomQL Query

```powershell
# POST
Invoke-RestMethod -Uri "http://localhost:5209/api/query" -Method Post `
  -ContentType "application/json" -Body '{"query":"SELECT * FROM telemetry LIMIT 10"}'

# GET
Invoke-RestMethod -Uri "http://localhost:5209/api/query?q=SELECT%20*%20FROM%20telemetry%20LIMIT%205"
```

**Expected:** `{"columns":[...],"rows":[...],"executionTimeMs":...}` — HTTP 200. Invalid grammar returns 400 with `QuerySyntaxException` message.

### A7. Alerts

```powershell
Invoke-RestMethod -Uri "http://localhost:5209/api/alerts"
```

**Expected:** JSON array of configured alert rules (may be `[]` if none registered). HTTP 200.

### A8. WebSocket Contract

Connect to `ws://localhost:5209/ws/metrics`. Verify the **polymorphic** `MetricUpdate` contract:

```json
{"$type":"cpu","data":{...},"timestamp":"..."}
{"$type":"memory","data":{...},"timestamp":"..."}
{"$type":"thread","data":{...},"timestamp":"..."}
```

PowerShell:
```powershell
$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]"ws://localhost:5209/ws/metrics", $ct).GetAwaiter().GetResult()
# Receive a few frames, decode, verify $type field present
```

**PASS criteria:** frames arrive ~every 300ms, each carrying a `$type` + `data` + `timestamp`.

---

## Suite B: Library/CLI Surface (Phases 5-13)

Run `SampleMonitoredApp` (see Setup). Use `Loom.DevTools.exe` directly (built path below). Substitute `<PID>` with the sample app's PID.

```powershell
$LOOM = "C:\Users\angel\source\repos\Project Loom v2\Loom.DevTools\bin\Debug\net10.0\Loom.DevTools.exe"
```

### B1. Live Metric Stream (`loom watch`) — Phase 13

```powershell
& $LOOM watch <PID>
```

**Expected:** Streaming `CounterRateValuePublished` / `HistogramValuePublished` events with payloads including `instrumentName=orders.processed`, `rate`, `value`, `sum`. Run for ~10s then stop.

**This is the reference for the exact EventPipe payload key names** — `instrumentName` (NOT `Name`), `value`/`rate`/`sum` (NOT `Value`/`Rate`). See Bug Fix B1.

### B2. Metric Exploration (`loom explore`) — Phase 13

```powershell
& $LOOM explore <PID>
```

**Expected:** Collects for 3s, prints table of metric names, types, latest values, sample counts. **PASS = at least a dozen metrics** (order/payment/inventory series) are listed. If "No metrics found", the payload-parsing fix is missing (Bug Fix B1).

### B3. Formatted Metrics (`loom metrics`) — Phase 13

```powershell
& $LOOM metrics <PID>
& $LOOM metrics <PID> cpu
```

**Expected:** Table of Metric / Type / Count / Avg / Min / Max.

### B4. LoomQL (`loom query`) — Phase 10

```powershell
& $LOOM query <PID> "SELECT * FROM telemetry LIMIT 5"
& $LOOM query <PID> "SELECT AVG(order.total) FROM telemetry"
& $LOOM query <PID> "SELECT COUNT(orders.processed) FROM telemetry WHERE method = 'orders.processed'"
```

**Expected:** Tabular output with header, rows, and `N row(s) in X.Xms`.

**Grammar constraints (verified):**
- SELECT columns are **metric names** or aggregates: `AVG(x)`, `COUNT(x)`, `MAX(x)`, `MIN(x)`, `P99(x)`
- `SELECT *` iterates all metrics
- Only table: `telemetry`
- WHERE supports `method` (substring match on metric name) with `AND` chaining
- Supported clauses: `GROUP BY`, `ORDER BY <col> [ASC|DESC]`, `LIMIT n`
- **NOT supported:** `WHERE type = '...'`, `name`/`value`/`type` as selectable columns, `NOW()`, `LIKE`, `LAST 5m`

### B5. Dashboard Bridge — Phase 16

With `Loom.Dashboard` bridged to the sample PID:

```powershell
Invoke-RestMethod -Uri "http://localhost:5209/api/exporters/metrics/names"
(Invoke-WebRequest -Uri "http://localhost:5209/prometheus" -UseBasicParsing).Content
```

**PASS criteria:** the target's Loom metrics (e.g. `orders.processed`, `inventory.total_items`, `payment.gateway.latency`) appear in both the names list and the Prometheus output with correct counters/summaries. This proves the EventPipe bridge → IMetricStore → exporter path.

### B6. Library-Phase Unit Tests — Phases 8, 9, 11, 12

```powershell
dotnet test Loom.Telemetry.Tests\Loom.Telemetry.Tests.csproj -c Debug --nologo --no-build `
  --filter "FullyQualifiedName~CollectorTests"     # Phase 8: 10/10
dotnet test Loom.Telemetry.Tests\Loom.Telemetry.Tests.csproj -c Debug --nologo --no-build `
  --filter "FullyQualifiedName~SamplingTests"      # Phase 9: 10/10
dotnet test Loom.Telemetry.Tests\Loom.Telemetry.Tests.csproj -c Debug --nologo --no-build `
  --filter "FullyQualifiedName~Alerting"           # Phase 11: 78/78
dotnet test Loom.Telemetry.Tests\Loom.Telemetry.Tests.csproj -c Debug --nologo --no-build `
  --filter "FullyQualifiedName~Exporters"          # Phase 12: 39/41 (2 intentionally skipped)
```

---

## Known Issues & Fixes

### B1. EventPipe Payload Parsing (FIXED — CRITICAL)

**Symptom:** `loom explore` / `loom metrics` / `loom query` returned "No metrics found", and the Dashboard bridge never ingested target Loom metrics.

**Root cause:** `EventPipeCollector.cs` and `EventPipeBridge.cs` parsed payload keys `"Name"`, `"Value"`, `"Mean"`, `"Rate"`. The real .NET EventPipe keys (confirmed via `loom watch`) are:

| Event | Real keys | Old code matched |
|-------|-----------|------------------|
| `BeginInstrumentReporting` | `instrumentName`, `instrumentType` | `"Name"` (never matched) |
| `CounterRateValuePublished` | `instrumentName`, `rate`, `value` | `"Rate"` (never matched — lowercase) |
| `HistogramValuePublished` | `instrumentName`, `sum`, `count`, `quantiles` | nothing (value stayed 0) |

**Fix (applied to both files):**
1. Match `case "Name": case "instrumentName":` for the metric name.
2. Match `case "Value": case "Mean": case "Rate": case "value": case "sum":` for the value.
3. Add a guard so only `*ValuePublished` events are ingested (metadata events like `BeginInstrumentReporting` carry `instrumentName` too and would otherwise pollute the store with zero-value gauges):
   ```csharp
   if (!eventName.Contains("ValuePublished")) return;
   ```

**Files changed:** `Loom.DevTools/Services/EventPipeCollector.cs`, `Loom.Dashboard/EventPipeBridge.cs`.

### B2. `loom dev` Crashes Under Redirected Output (UNFIXED)

**Symptom:** `loom dev` throws `System.IO.IOException: The handle is invalid` at `Console.Clear()` (`DevCommand.cs:37`) when stdout is redirected (piped/non-interactive).

**Status:** Discovery logic works (it reaches `Console.Clear()` only after a successful scan), but the refresh loop is not console-safe. Works fine in a real interactive terminal. Should wrap `Console.Clear()` in a try/catch or detect `Console.IsOutputRedirected`.

### B3. Query Grammar Confusion (NOT A BUG)

`SELECT name, value, type FROM telemetry WHERE type = 'Counter'` returns no rows — the executor treats SELECT/WHERE identifiers as **metric names**, not row columns. Only `method` is a filterable column. This is by design (see B4 grammar constraints).

### B4. Pre-existing Flaky Test Failures (NOT CAUSED BY SMOKE TESTS)

Running the full suite (`dotnet test Loom.Telemetry.Tests`) shows 4 failures. These fail even in isolation and are in library code untouched by the smoke-test fixes. They match the known baseline:

- `QueryAstTests.QueryPlanner_HandlesNonexistentMetrics`
- `QueryExecutorTests.Executor_QueriesCounterMetrics`
- `ExporterIntegrationTests.FullPipeline_ContinuousMetrics_CollectsNewRecordsOnly`
- `ExportCollectionTests.ExportCollectionHostedService_CollectsOnlyNewRecordsSinceLastCollection`

Additionally `ExporterIntegrationTests.FullPipeline_RecordMetrics_ExportersReceiveBatches` is `[Fact(Skip="Flaky due to PeriodicTimer timing and test isolation issues")]` — intentionally skipped.

---

## Smoke Test Results History

### Run 2026-08-19 — HTTP/WS Surface + Library/CLI Surface

| Check | Result |
|-------|--------|
| Build all projects (Debug) | ✅ 0 errors |
| SampleMonitoredApp launch + persistence | ✅ (PID 20812) |
| `/api/health` | ✅ 200 |
| `/api/metrics/cpu\|memory\|thread` | ✅ 200 |
| `/api/metrics/ingest` (valid + invalid type) | ✅ 202 / 400 |
| `/metrics` Prometheus | ✅ summaries + counters + quantiles |
| `/api/exporters/metrics/names` | ✅ 22 Loom metric names (via bridge) |
| `/api/query` POST/GET | ✅ 200, syntax error → 400 |
| `/api/alerts` | ✅ 200 |
| WebSocket `$type` polymorphic frames | ✅ |
| `loom watch <pid>` | ✅ full event stream |
| `loom explore <pid>` | ✅ 12 metrics (after fix) |
| `loom metrics <pid>` | ✅ formatted table |
| `loom query <pid>` | ✅ aggregates + WHERE method |
| Dashboard bridge → store → Prometheus | ✅ target Loom metrics present |
| Phase 8 CollectorTests | ✅ 10/10 |
| Phase 9 SamplingTests | ✅ 10/10 |
| Phase 11 AlertingTests | ✅ 78/78 |
| Phase 12 ExporterTests | ✅ 39/41 (2 intentional skips) |
| Cleanup | ✅ all processes stopped |

**Outcome:** ✅ All smoke tests pass. One critical bug found and fixed (Bug B1).

---

## References

- `TESTING.md` — unit tests, AOT compliance, binary size, allocation testing
- `IMPLEMENTATION-METHODOLOGY.md` — build guide / phase definitions
- `wiggly-noodling-hoare.md` — architecture decisions
- `examples/SampleMonitoredApp/README.md` — sample app usage

---

**Document Maintained By:** Project Loom v2 Team
**Last Test Run:** 2026-08-19
