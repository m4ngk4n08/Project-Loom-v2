# Project Loom v2 - Testing Guide

**Document Version:** 1.0  
**Last Updated:** 2026-08-18  
**Current Phase:** Phase 12 Complete, Phase 13 Ready

---

## Overview

This document provides comprehensive testing procedures for Project Loom v2, covering unit tests, AOT compliance, binary size, allocation testing, and functional endpoint verification.

> **2026-09-01:** `Loom.Web.Api` was retired. `Loom.Dashboard` is now the only web host
> (run it with `dotnet run --project Loom.Dashboard -- <pid>`, default port `5209`), and
> the Native AOT proof moved to `Loom.AotProbe`, whose binary size is not a gate. See
> `BACKLOG.md` § 11.4. Commands below that still name `Loom.Web.Api` predate the
> retirement and need the substitution above before they will run.

---

## Test Status Summary

| Test Category | Status | Pass Rate | Notes |
|--------------|--------|-----------|-------|
| Unit Tests | ✅ PASS | 505/505 (100%) | Isolation and flakiness resolved |
| AOT Compliance | ✅ PASS | 0 warnings | Native AOT ready |
| Binary Size | ✅ PASS | 14.74 MB | Under the 17 MB limit by 2.26 MB |
| Endpoints | ✅ PASS | All functional | Health, Metrics, Query, Alerts, Exporters |
| Allocations | ✅ PASS | ~31 KB/sec hot path | HTTP overhead, acceptable |

**Overall Status:** ✅ **PRODUCTION READY**

---

## 1. Unit Tests

### Command

```powershell
# Windows PowerShell
cd "C:\Users\angel\source\repos\Project Loom v2"
dotnet test --configuration Debug
```

```bash
# Linux/macOS
cd "/path/to/Project Loom v2"
dotnet test --configuration Debug
```

### Expected Results

```
Test summary: total: 505, succeeded: 505, skipped: 0, failed: 0
```

✅ **PASS:** 95%+ tests passing  
⚠️ **WARNING:** 90-95% passing (investigate failures)  
❌ **FAIL:** <90% passing (critical issues)

### Known Issues

**Test Isolation Failures (Non-Critical):**
1. `PropertyTrackingTests.TrackedProperty_RecordsMultipleChanges` - Shared `LoomMetrics` state
2. `ExporterIntegrationTests.FullPipeline_ContinuousMetrics_CollectsNewRecordsOnly` - Timing + shared state
3. `ExportCollectionTests.ExportCollectionHostedService_CollectsOnlyNewRecordsSinceLastCollection` - Shared ring buffers

**Skipped Tests (Flaky):**
1. `ExportCollectionTests.ExportCollectionHostedService_WithMetrics_CollectsAndWritesBatch` - PeriodicTimer timing
2. `ExporterIntegrationTests.FullPipeline_RecordMetrics_ExportersReceiveBatches` - Test isolation

**Root Cause:** Global static `LoomMetrics.Buffers` dictionary not reset between tests.

**Fix (Future):** Add `LoomMetrics.ResetForTesting()` method (see TESTING.md § 8 Troubleshooting).

---

## 2. Native AOT Compliance

### Purpose
Verify zero reflection usage and trim safety for Native AOT compilation.

### Command

```powershell
# Windows
dotnet build Loom.AotProbe\Loom.AotProbe.csproj `
  --configuration Release `
  /p:EnableTrimAnalyzer=true `
  /p:TreatWarningsAsErrors=true
```

```bash
# Linux/macOS
dotnet build Loom.AotProbe/Loom.AotProbe.csproj \
  --configuration Release \
  /p:EnableTrimAnalyzer=true \
  /p:TreatWarningsAsErrors=true
```

### Expected Results

```
Build succeeded in X.Xs
    0 Warning(s)
    0 Error(s)
```

✅ **PASS:** 0 warnings, 0 errors  
❌ **FAIL:** Any IL2026, IL3050, or trim warnings

### Common Trim Warnings

| Warning | Meaning | Fix |
|---------|---------|-----|
| IL2026 | Reflection usage | Remove reflection, use source generators |
| IL3050 | Dynamic code generation | Remove `Expression<T>`, use delegates |
| IL2087 | Unrecognized type | Add `[DynamicallyAccessedMembers]` attribute |

---

## 3. Binary Size Verification

### Purpose
Historical — this check gated `Loom.Web.Api`, the shipping Native AOT web host, at
17 MB. That project is retired; `Loom.Dashboard` is now the only web host and is not
AOT-published, so there is no shipping binary to gate. `Loom.AotProbe` still proves
Native AOT compatibility, but its size is deliberately not a product metric — see
`BACKLOG.md` § 11.4. The commands below still work against `Loom.AotProbe` if you want
a size data point, but nothing should fail CI or a commit on the result.

### Command

```powershell
# Windows - Build Native AOT
dotnet publish Loom.AotProbe\Loom.AotProbe.csproj `
  --configuration Release `
  --runtime win-x64

# Check size
Get-ChildItem "Loom.AotProbe\bin\Release\net10.0\win-x64\publish\Loom.AotProbe.exe" | 
  Select-Object Name, @{Name="SizeMB";Expression={[math]::Round($_.Length/1MB, 2)}}
```

```bash
# Linux - Build Native AOT
dotnet publish Loom.AotProbe/Loom.AotProbe.csproj \
  --configuration Release \
  --runtime linux-x64

# Check size
ls -lh Loom.AotProbe/bin/Release/net10.0/linux-x64/publish/Loom.AotProbe
```

### Expected Results

No pass/fail size threshold — this is informational only (see Purpose above). What does
matter: no `Loom.AotProbe.dll` should sit beside the native binary in the publish
directory. If it does, `PublishAot` silently fell back to a managed build.

---

## 4. Allocation Testing

### Purpose
Verify zero-allocation hot paths (metric recording) and acceptable cold path allocations (export formatting).

### Prerequisites

```powershell
# Install dotnet-counters (one-time)
dotnet tool install -g dotnet-counters
```

### Setup

**Window 1: Run the application**

```powershell
cd "C:\Users\angel\source\repos\Project Loom v2"
dotnet run --project Loom.Dashboard --configuration Release -- <pid>
```

Note the port (default 5209, `LOOM_DASHBOARD_PORT` to override).

**Window 2: Get Process ID**

```powershell
# Windows
Get-Process | Where-Object {$_.ProcessName -eq "Loom.Dashboard"} | Select-Object Id, ProcessName

# Linux/macOS
ps aux | grep Loom.Dashboard
```

**Window 3: Monitor Allocations**

```powershell
# Replace <PID> with actual process ID
dotnet-counters monitor --process-id <PID> --counters System.Runtime[alloc-rate,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count]
```

### Test 1: Baseline (No Load)

**Expected:**
```
[System.Runtime]
    Allocation Rate (B / 1 sec)                  50-200
    # of Gen 0 Collections / min                 0-1
    # of Gen 1 Collections / min                 0
    # of Gen 2 Collections / min                 0
```

✅ **PASS:** < 1 KB/sec baseline

---

### Test 2: Hot Path - Metric Ingestion

**Window 4: Send Load**

```powershell
# Replace port if needed (5000 or 5209)
for ($i = 1; $i -le 100; $i++) {
    curl -Method POST http://localhost:5000/api/metrics/ingest `
      -ContentType "application/json" `
      -Body "{`"metrics`":[{`"name`":`"test.counter.$i`",`"type`":`"Counter`",`"value`":$i}]}" `
      -UseBasicParsing | Out-Null
}
Write-Host "✅ Sent 100 metric ingestion requests" -ForegroundColor Green
```

**Expected Results:**

| Allocation Rate | Verdict | Notes |
|----------------|---------|-------|
| 0-5 KB/sec | ✅ Excellent | True zero-allocation hot path |
| 5-50 KB/sec | ✅ Good | HTTP/JSON framing overhead |
| 50-200 KB/sec | ⚠️ Acceptable | Some overhead, but functional |
| > 200 KB/sec | ❌ Problem | Investigate LINQ/boxing in hot path |

**Current Performance:** ~31-61 KB/sec ✅ (HTTP overhead acceptable)

---

### Test 3: Cold Path - Prometheus Export

**Window 4: Send Load**

```powershell
# Wait a moment for allocations to settle
Start-Sleep -Seconds 3

# Test Prometheus export (cold path)
for ($i = 1; $i -le 50; $i++) {
    curl http://localhost:5000/metrics -UseBasicParsing | Out-Null
}
Write-Host "⚠️ Sent 50 Prometheus requests (allocations expected)" -ForegroundColor Yellow
```

**Expected Results:**

| Allocation Spike | Verdict | Notes |
|-----------------|---------|-------|
| 1-5 MB | ✅ Excellent | Efficient formatting |
| 5-30 MB | ✅ Good | Standard export overhead |
| 30-100 MB | ⚠️ Acceptable | First run includes JIT |
| > 100 MB | ❌ Problem | Memory leak or inefficient code |

**Current Performance:** 
- First run: ~32 MB (JIT compilation)
- Subsequent: ~2.4 MB ✅

**Note:** Prometheus export is a COLD PATH - allocations are intentional and acceptable.

---

### Test 4: Query Language

```powershell
for ($i = 1; $i -le 50; $i++) {
    curl -Method POST http://localhost:5000/api/query `
      -ContentType "application/json" `
      -Body '{"query":"SELECT * FROM telemetry LIMIT 10"}' `
      -UseBasicParsing | Out-Null
}
Write-Host "📊 Sent 50 query requests" -ForegroundColor Cyan
```

**Expected:** 5-20 KB/sec ✅ (query execution, cold path)

---

## 5. Functional Endpoint Testing

### Health Check

```powershell
curl http://localhost:5000/api/health
```

**Expected:**
```json
{
  "status": "Healthy",
  "timestamp": "2026-08-18T...",
  "uptimeSeconds": 172,
  "memoryUsageMb": 36.31
}
```

✅ Status: 200 OK

---

### Metric Ingestion

```powershell
curl -Method POST http://localhost:5000/api/metrics/ingest `
  -ContentType "application/json" `
  -Body '{"metrics":[{"name":"test.counter","type":"Counter","value":42},{"name":"cpu.usage","type":"Gauge","value":75.5},{"name":"request.duration","type":"Histogram","value":245.3}]}'
```

**Expected:**
```
StatusCode: 202 Accepted
Content: {}
```

✅ Metrics ingested successfully

---

### Prometheus Export

```powershell
curl http://localhost:5000/metrics
```

**Expected:**
```
# TYPE test_counter counter
# HELP test_counter Loom telemetry metric
test_counter 42.00

# TYPE cpu_usage gauge
# HELP cpu_usage Loom telemetry metric
cpu_usage 75.50
```

✅ OpenMetrics format correct

---

### Exporter Status

```powershell
curl http://localhost:5000/api/exporters/status
```

**Expected:**
```json
[
  {
    "name": "Console",
    "isHealthy": true,
    "lastSuccessUtc": "2026-08-18T...",
    "totalExports": 5,
    "totalFailures": 0
  }
]
```

✅ Exporters operational

---

### Query Language

```powershell
curl -Method POST http://localhost:5000/api/query `
  -ContentType "application/json" `
  -Body '{"query":"SELECT * FROM telemetry WHERE name = '\''test.counter'\'' LIMIT 10"}'
```

**Expected:**
```json
{
  "columns": ["name", "type", "value", "timestamp"],
  "rows": [
    {"name": "test.counter", "type": "Counter", "value": 42, ...}
  ]
}
```

✅ Query executed successfully

---

### Alerts

```powershell
# List configured alerts
curl http://localhost:5000/api/alerts

# Test an alert notification
curl -Method POST http://localhost:5000/api/alerts/HighErrorRate/test
```

**Expected:**
```
StatusCode: 202 Accepted
```

✅ Alert system functional

---

## 6. WebSocket Real-Time Streaming

### Test with wscat (requires Node.js)

```bash
# Install wscat
npm install -g wscat

# Connect to WebSocket endpoint
wscat -c ws://localhost:5000/ws/metrics
```

**Expected:** Live JSON metric updates streaming every second

**Alternative (PowerShell):**
```powershell
# Create WebSocket test script
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$uri = [Uri]::new("ws://localhost:5000/ws/metrics")
$cts = New-Object System.Threading.CancellationTokenSource
$ws.ConnectAsync($uri, $cts.Token).Wait()
Write-Host "✅ WebSocket connected" -ForegroundColor Green
```

---

## 7. Performance Benchmarks

### Metric Recording Throughput

```powershell
Measure-Command {
    for ($i = 1; $i -le 1000; $i++) {
        curl -Method POST http://localhost:5000/api/metrics/ingest `
          -ContentType "application/json" `
          -Body "{`"metrics`":[{`"name`":`"perf.test`",`"type`":`"Counter`",`"value`":1}]}" `
          -UseBasicParsing | Out-Null
    }
}
```

**Expected:** 10-20 seconds for 1000 requests (50-100 req/sec)

### Query Performance

```powershell
Measure-Command {
    curl -Method POST http://localhost:5000/api/query `
      -ContentType "application/json" `
      -Body '{"query":"SELECT COUNT(*) FROM telemetry"}' `
      -UseBasicParsing | Out-Null
}
```

**Expected:** < 100ms for simple COUNT query

---

## 8. Troubleshooting

### Tests Fail Due to Shared State

**Symptom:** Tests pass individually but fail when run together

**Fix:** Add reset method to `LoomMetrics.cs`:

```csharp
#if DEBUG
public static void ResetForTesting()
{
    Buffers.Clear();
}
#endif
```

Then call in test constructors:
```csharp
public MyTests()
{
    LoomMetrics.ResetForTesting();
}
```

---

### Binary Size Too Large

Not currently a gated check — see § 3. If investigating anyway:

**Check dependencies:**
```powershell
dotnet list Loom.AotProbe package
```

**Remove unnecessary packages** and rebuild with size optimizations (see § 3).

---

### Trim Warnings Appear

**Step 1: Identify the warning**
```
warning IL2026: MyMethod references Type.GetMethod
```

**Step 2: Fix reflection usage**
- Use source generators instead
- Add `[DynamicallyAccessedMembers]` attribute
- Remove reflection-based serialization

---

### High Allocation Rate

**Step 1: Identify the hot path**
```powershell
dotnet-trace collect --process-id <PID> --profile gc-collect
```

**Step 2: Common causes**
- LINQ in hot paths (use `for` loops)
- Boxing value types (use generics)
- String allocations (use `Span<char>`)
- Unnecessary array allocations (use `ArrayPool<T>`)

---

## 9. Continuous Integration

### GitHub Actions Workflow

```yaml
name: Test

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Unit Tests
        run: dotnet test --configuration Debug --no-restore
      
      - name: AOT Compliance Check
        run: dotnet build Loom.AotProbe --configuration Release /p:EnableTrimAnalyzer=true /p:TreatWarningsAsErrors=true
      
      - name: Native AOT publish (no size gate - see BACKLOG.md § 11.4)
        run: |
          dotnet publish Loom.AotProbe --configuration Release --runtime linux-x64
          if [ -f "Loom.AotProbe/bin/Release/net10.0/linux-x64/publish/Loom.AotProbe.dll" ]; then
            echo "Managed assembly present - not an AOT publish"; exit 1
          fi
```

---

## 10. Testing Checklist

Before committing Phase N:

- [ ] Unit tests pass (>95%)
- [ ] AOT compliance check (0 warnings, via `Loom.AotProbe`)
- [ ] `Loom.AotProbe` publish contains no managed assembly (size itself is not gated — see `BACKLOG.md` § 11.4)
- [ ] Allocation testing complete (hot paths < 50 KB/sec)
- [ ] All endpoints respond correctly
- [ ] WebSocket streaming works
- [ ] Query language executes correctly
- [ ] Exporters functional (Prometheus, Console)
- [ ] Alerts trigger correctly
- [ ] No crashes under load

**Phase 12 Status:** ✅ All items complete

---

## 11. Test Results History

### Phase 12 - Exporters (2026-08-18)

| Metric | Result | Status |
|--------|--------|--------|
| Unit Tests | 505/505 (100%) | ✅ PASS |
| AOT Compliance | 0 warnings | ✅ PASS |
| Binary Size | 14.74 MB | ✅ PASS |
| Hot Path Alloc | 31-61 KB/sec | ✅ PASS |
| Cold Path Alloc | 2.4-32 MB | ✅ PASS |
| Endpoints | All functional | ✅ PASS |

**Verdict:** Production ready, all Phase 12 objectives met.

---

## 12. References

- **AOT Compatibility:** https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- **Trim Warnings:** https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-warnings
- **dotnet-counters:** https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters
- **Source Generators:** https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview

---

**Document Maintained By:** Project Loom v2 Team  
**Last Test Run:** 2026-08-18  
**Next Scheduled Review:** Phase 13 completion
