# Phase 12 Exporter Tests

This directory contains comprehensive test coverage for the Loom metric export system.

## Test Files

### 1. ExportDispatchTests.cs
**Coverage:** `ExportDispatchHostedService` - channel consumer and dispatcher

Tests:
- No batches → no dispatch
- Single batch → dispatches to exporter
- Multiple batches → dispatches all
- Multiple exporters → all receive batches
- Exporter throws → continues dispatching to others (error isolation)
- Status tracking on success
- Cancellation token handling

**Key Test Helpers:**
- `TrackingExporter` - counts exports, stores last batch
- `FailingExporter` - always throws
- `SlowExporter` - delays to test cancellation

### 2. ExportCollectionTests.cs
**Coverage:** `ExportCollectionHostedService` - periodic collection timer

Tests:
- No metrics → no batch written (empty batches filtered)
- With metrics → collects and writes batch
- Multiple metric types → collects all
- Incremental collection → only new records since last collection
- Respects collection interval timing
- Proper disposal and cleanup

### 3. PrometheusFormatterTests.cs
**Coverage:** `PrometheusFormatter` - OpenMetrics text format

Tests:
- Empty metrics → empty output
- Counter → `# TYPE counter` format
- Gauge → `# TYPE gauge` format
- Histogram → `summary` with quantiles (p50, p95, p99)
- Multiple metrics → all included
- Metric name sanitization (dots → underscores, hyphens → underscores)
- Most recent value for counters/gauges
- Summary statistics for histograms

### 4. ConsoleExporterTests.cs
**Coverage:** `ConsoleExporter` - ILogger-based exporter

Tests:
- Export logs batch summary
- Empty batch still logs
- Multiple batches → logs each
- Name property returns "Console"

**Test Helper:**
- `FakeLogger<T>` - captures log messages for assertion

### 5. ExporterStatusTrackerTests.cs
**Coverage:** `ExportStatusTracker` - health monitoring

Tests:
- First success → creates healthy status
- Multiple successes → increments count
- First failure → creates unhealthy status
- Multiple failures → increments count, updates error message
- Success after failure → marks healthy (preserves failure timestamp)
- Failure after success → marks unhealthy (preserves success timestamp)
- Multiple exporters tracked independently
- Empty statuses when no records
- Timestamp updates on each call
- Record type supports `with` expressions

### 6. ExporterIntegrationTests.cs
**Coverage:** Full pipeline end-to-end

Tests:
- Record metrics → collection → dispatch → exporters receive batches
- Multiple exporters → all receive same batches
- One exporter fails → others still work (isolation verified)
- Bounded channel overflow → drop-oldest behavior (no crashes)
- Continuous metrics → collects new records only

## Running Tests

```bash
# Run all exporter tests
dotnet test --filter "FullyQualifiedName~Loom.Telemetry.Tests.Exporters"

# Run specific test file
dotnet test --filter "FullyQualifiedName~ExportDispatchTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Test Patterns

All tests follow xUnit conventions and existing Loom test patterns:

1. **Cleanup:** Tests that record metrics implement `IDisposable` and call `LoomMetrics.ResetForTesting()` to avoid test pollution
2. **GUID metric names:** Use `$"test.metric.{Guid.NewGuid()}"` to ensure isolation
3. **Inline test helpers:** Prefer custom test classes over Moq for clarity
4. **BackgroundService testing:** Use `StartAsync` + delay + cancel + `StopAsync` pattern
5. **Channel testing:** Use unbounded channels for test simplicity, bounded for overflow tests

## Coverage Summary

| Component | Coverage |
|-----------|----------|
| IMetricExporter interface | ✅ Via test helpers |
| MetricBatch/MetricBatchEntry | ✅ Via all tests |
| ExportOptions | ✅ Via collection tests |
| ExportStatusTracker | ✅ Dedicated test file |
| ExportCollectionHostedService | ✅ Dedicated test file |
| ExportDispatchHostedService | ✅ Dedicated test file |
| ConsoleExporter | ✅ Dedicated test file |
| PrometheusFormatter | ✅ Dedicated test file |
| ServiceCollectionExtensions | ⚠️ Tested via integration (DI registration not unit tested) |

**Note:** GrafanaCloudExporter and ElasticsearchExporter were removed as non-functional dead code (never registered in any host) — see BACKLOG.md § 9.

## Verification

Phase 12 tests verify:
- ✅ Channel-based backpressure (bounded, drop-oldest)
- ✅ Per-exporter error isolation
- ✅ Status tracking for monitoring
- ✅ Periodic collection timer
- ✅ Incremental metric collection (only new records)
- ✅ OpenMetrics text format correctness
- ✅ Full pipeline integration
- ✅ Cancellation token propagation
- ✅ Thread safety (concurrent access to tracker/buffers)
