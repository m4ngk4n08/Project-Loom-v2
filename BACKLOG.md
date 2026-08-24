# Project Loom v2 - Technical Debt & Backlog

**Document Version:** 1.1  
**Last Updated:** 2026-08-24  
**Current Phase:** Phase 12 Complete, Phase 13 Ready

---

## Overview

This document tracks known issues, technical debt, and improvement opportunities that are **non-blocking** for production but should be addressed in future iterations.

**Priority Levels:**
- 🔴 **HIGH** - Should fix before 1.0 release
- 🟡 **MEDIUM** - Fix when convenient, impacts maintainability
- 🟢 **LOW** - Nice to have, cosmetic improvements

---

## 1. Testing Infrastructure

### 1.1 Test Isolation Issues 🔴 HIGH (COMPLETED)

**Issue:** 3 unit tests failed due to shared global state in `LoomMetrics` and related
static telemetry singletons.

**Formerly Failing Tests (now passing):**
1. `PropertyTrackingTests.TrackedProperty_RecordsMultipleChanges`
2. `ExporterIntegrationTests.FullPipeline_ContinuousMetrics_CollectsNewRecordsOnly`
3. `ExportCollectionTests.ExportCollectionHostedService_CollectsOnlyNewRecordsSinceLastCollection`

**Status:** ✅ **RESOLVED**

**Root Cause (confirmed):** Loom's telemetry surface is deliberately static and
process-global (`LoomMetrics.Buffers`, `LoomSampling` rules, `LoomCollectors`, the
alerting rule registry — see ADR-5). Most test classes already reset that state in
their own constructor/`Dispose`, but xUnit runs test *classes* in parallel by default,
so the cleanup itself became the race: one class's `Buffers.Clear()` wiped records
another class was mid-assertion on.

**Actual Fix (neither of the two options this entry originally proposed):**
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` in
`Loom.Telemetry.Tests/AssemblyInfo.cs`. Serializing the whole assembly makes the
existing per-class resets sufficient — no `ResetForTesting()` API was added, and no
DI-based buffer factory was introduced. The suite is small and I/O-light, so running
serially has negligible wall-clock cost against a source of non-deterministic
failures.

**No further action needed.**

---

### 1.2 Flaky PeriodicTimer Tests 🟡 MEDIUM (COMPLETED)

**Issue:** 2 tests were disabled with `[Fact(Skip = "...")]` due to apparent timing
sensitivity.

**Formerly Skipped Tests (now plain `[Fact]`, passing):**
1. `ExportCollectionTests.ExportCollectionHostedService_WithMetrics_CollectsAndWritesBatch`
2. `ExporterIntegrationTests.FullPipeline_RecordMetrics_ExportersReceiveBatches`

**Status:** ✅ **RESOLVED**

**Root Cause (confirmed):** Not `PeriodicTimer` flakiness — the same cross-class
parallelism race as § 1.1. Once the assembly serialized (§ 1.1's fix), the timing
premise for skipping these tests no longer held, and both were re-enabled as plain
`[Fact]`s. Zero real `[Fact(Skip = ...)]` remain in the suite; the only "Skip ="
text left in the repo is the explanatory comment in
`Loom.Telemetry.Tests/AssemblyInfo.cs`.

**No further action needed.**

---

### 1.3 PrometheusFormatter Test Failure 🟢 LOW (COMPLETED)

**Issue:** `PrometheusFormatterTests.Format_NoMetrics_ReturnsEmptyString` failed
sporadically.

**Status:** ✅ **RESOLVED** — resolved by the § 1.1 fix (same isolation race: other
test classes left metrics in the global ring buffers before this test ran). Full
suite is 326/326 passing.

**No further action needed.**

---

### 1.4 No Test Coverage for `/api/logs/tail` Clamping 🟢 LOW

**Issue:** The resumable log-tail endpoint's count-clamping and cursor-derivation
logic (`Loom.Dashboard/Extensions/EndpointExtensions.cs`, `MapLogEndpoints`) has no
test coverage exercising it directly.

**Context:** An earlier test,
`InMemoryLogStoreTests.ReadAfter_ClampedToCount_NextCursorReflectsOnlyRecordsActuallyReturned`,
was removed in `db794ec` — it re-implemented the endpoint's clamping algorithm
inline against `ILogStore` directly, using a buffer sized so `DroppedCount` was
always 0. That meant it asserted a copy of the algorithm's arithmetic, not the
endpoint, and could never reach (or catch a regression in) the dropped-records case
that a prior commit had to fix.

**Proposal:** An endpoint-level test (e.g. via `WebApplicationFactory` or an
in-process test host) that issues two HTTP polls against `/api/logs/tail` across a
buffer that has wrapped, asserting the second poll's cursor and `droppedCount` are
correct and no records are skipped or repeated.

**Effort:** 1-2 hours  
**Priority:** 🟢 LOW (the underlying `ILogStore`/`LogBuffer` arithmetic is covered;
this is specifically about the endpoint's own clamping wrapper)

---

## 2. Binary Size & Build Optimization

### 2.1 Binary Size Target Adjustment 🟡 MEDIUM

**Issue:** Original target was < 15 MB, actual size is 16.3 MB

**Context:**
- Original 15 MB target was for basic diagnostic core (Phases 0-4)
- Phases 5-12 added significant features:
  - Query language with SQL-like parser
  - Alerting engine with sliding windows
  - 4 exporters (Prometheus, Grafana, Elasticsearch, Console)
  - Attribute-based instrumentation
  - Custom collectors plugin system

**Current Reality:**
- **16.3 MB for full feature set is reasonable**
- Industry comparison: Similar tools are 30-50+ MB
- Only 8.7% over original target

**Options:**

**Option A: Accept 16.3 MB and update documentation ✅ RECOMMENDED**
- Update `CLAUDE.md` size target to < 17 MB
- Update `TESTING.md` thresholds
- Document feature-to-size ratio

**Option B: Aggressive trimming to hit 15 MB**
- Enable `IlcGenerateStackTraceData=false` (lose debug info)
- Make exporters optional at compile time
- Review each dependency for alternatives
- **Not recommended** - sacrifices features for arbitrary target

**Proposed Action:**
```markdown
# CLAUDE.md Update
- Binary size: <17 MB (15 MB for basic diagnostic core, +2 MB for telemetry platform)
```

**Effort:** 30 minutes (documentation update)  
**Priority:** 🟡 MEDIUM (documentation accuracy)

---

### 2.2 Size Optimization Flags Not Always Applied 🟢 LOW

**Issue:** Build scripts don't consistently use all optimization flags

**Current:**
```xml
<InvariantGlobalization>true</InvariantGlobalization>
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>link</TrimMode>
```

**Missing (Optional):**
```xml
<OptimizationPreference>Size</OptimizationPreference>
<IlcOptimizationPreference>Size</IlcOptimizationPreference>
<IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
```

**Impact:**
- Potential 500 KB - 1 MB savings
- Loss of stack traces in crashes (if `IlcGenerateStackTraceData=false`)

**Recommendation:**
- Keep current flags (already optimal)
- Document additional flags in `TESTING.md` for future reference

**Priority:** 🟢 LOW (current size acceptable)

---

## 3. Missing Features & Endpoints

### 3.1 Missing `/api/telemetry/ingest` Endpoint 🔴 HIGH (COMPLETED)

**Issue:** Original design included telemetry ingestion endpoint, not implemented until Phase 12 testing

**Status:** ✅ **RESOLVED** (2026-08-18)

**Implementation:**
- Created `MetricIngestRequest` and `MetricIngestDto` DTOs
- Added `POST /api/metrics/ingest` endpoint
- Wired to `LoomMetrics.RecordCounter/Gauge/Histogram`
- All tests now passing

**Files Modified:**
- `Loom.Web.Contracts\Dtos\MetricIngestRequest.cs` (NEW)
- `Loom.Web.Contracts\JsonContext.cs` (added DTO registration)
- `Loom.Web.Api\Extensions\EndpointExtensions.cs` (added endpoint)

**No further action needed.**

---

### 3.2 Search Endpoint Deferred 🟢 LOW

**Issue:** `DiagnosticSearchRequest`/`DiagnosticSearchResponse` DTOs exist but no search endpoint

**Context:**
- Original design included vector/semantic search
- Replaced by SQL-like query language (Phase 10)
- DTOs remain in codebase but unused

**Options:**
1. **Remove DTOs** - Clean up unused code
2. **Keep DTOs** - Reserve for future full-text search feature
3. **Implement basic search** - Bridge gap between query language and vector search

**Recommendation:** Keep DTOs, mark as future feature in documentation

**Priority:** 🟢 LOW (query language sufficient for now)

---

## 4. Code Quality & Maintainability

### 4.1 LINQ in PrometheusFormatter Cold Path 🟢 LOW (COMPLETED)

**Issue:** `PrometheusFormatter.cs` was claimed to use LINQ in formatting logic

**Status:** ✅ **RESOLVED** - stale by the time this was checked. `PrometheusFormatter.cs`
was rewritten (see § 4.3) between when this entry was filed and now; it contains zero
LINQ. There was nothing to fold this into by the time § 4.3's fix landed.

**No further action needed.**

---

### 4.2 Anonymous Type in Error Response 🟢 LOW (COMPLETED)

**Issue:** `Results.Accepted(null, new { ingested = ... })` caused JsonSerializer error

**Status:** ✅ **RESOLVED** (2026-08-18)

**Fix:** Changed to `Results.Accepted()` (no body)

**Lesson Learned:** Never use anonymous types in Native AOT - they're not source-generatable

**No further action needed.**

---

### 4.3 PrometheusFormatter Emits Invalid Prometheus Output 🟡 MEDIUM (COMPLETED)

**Issue:** `PrometheusFormatter` produced output that violated the Prometheus text
exposition format (0.0.4) in three ways:

1. **Line endings:** `NewLineBytes` was built from `Environment.NewLine`, which is
   CRLF on Windows. The exposition format requires LF-only line endings.
2. **Tags dropped:** `MetricRecord.Tags` were never written as Prometheus labels.
   Every series with distinct tag values collapsed into a single untagged line.
3. **Counters weren't cumulative:** for `MetricType.Counter`, the formatter emitted
   the single most recent recorded value rather than a running total.

**Status:** ✅ **RESOLVED**

**Fix:**
- Line endings hardcoded to `"\n"u8`; the stale `NewLineBytes`/`Environment.NewLine`
  field is gone.
- Records are now read via `MetricBuffer.TryReadRecent` into a pooled
  `MetricRecord[]` (not `Snapshot()`, which discards `Tags`), grouped by a canonical
  tag-set key (tags sorted by key ordinal, so `{a,b}` and `{b,a}` merge into one
  series), and emitted as `name{k="v",...} value` with label names sanitized the
  same way metric names are and label values escaped per spec
  (`\` → `\\`, `"` → `\"`, newline → `\n`). A series with no tags emits no braces.
- Counters now sum every retained record in their label-set group instead of using
  the newest one; gauges keep newest-value semantics. Histogram/summary `_count`,
  `_sum`, and quantile lines are computed per label set, with quantile merged into
  the same brace group as any user labels (`name{env="prod",quantile="0.5"}`).
- `# HELP` now precedes `# TYPE` (both still once per metric name). The class doc
  comment no longer calls this an OpenMetrics formatter — it targets the Prometheus
  text exposition format 0.0.4 specifically, which the `/prometheus` endpoint's
  content type already declared; no trailing `# EOF` is emitted.
- `PrometheusFormatterEquivalenceTests` goldens updated from `\r\n` to `\n` (the
  format requires LF regardless of host OS - these were never meaningfully
  OS-dependent captures), plus new coverage for multi-series grouping, tag-order
  merging, cumulative counters, label escaping, and merged summary+label output.

**Known limitation kept (not fixed here):** counters are still summed from the
*retained* records in a bounded ring buffer (8192 by default). Once a counter's
buffer wraps, older increments are gone and the reported total stops growing even
though the real cumulative count keeps increasing. See DEBT-016 below.

**Cross-reference:** § 4.1 (LINQ in this file) was checked while fixing this and
found to already be stale — the file had been rewritten with zero LINQ before this
fix landed, so there was nothing to fold in.

**No further action needed** (beyond DEBT-016, tracked separately).

---

### 4.4 Prometheus Counters Stop Growing Once Their Buffer Wraps ✅ COMPLETED

**Issue:** `PrometheusFormatter` (§ 4.3) computed a counter's exported total by
summing every `MetricRecord` currently retained in that metric's `MetricBuffer`.
`MetricBuffer` is a bounded ring buffer (8192 records by default) — once a
high-frequency counter's buffer wrapped, the oldest increments were overwritten and
no longer part of the sum, so the exported total stopped growing even though the
true lifetime cumulative count kept increasing, which PromQL's
`rate()`/`increase()` reads as a counter reset.

**Fix:** `InMemoryMetricStore` now maintains a bounded, per-series monotonic
accumulator (`GetCounterTotals()` on `IMetricStore`), independent of ring-buffer
contents:
- Keyed by `MetricSeriesKey.Build(name, sortedTags)` (`Loom.Telemetry`), the single
  canonical (name, tags) → key implementation shared by the store and the
  formatter — no duplicated key logic to drift out of sync.
- Capped at 10,000 tracked series (ctor parameter, default 10,000). Once the cap is
  reached, new series are simply not admitted — existing series keep accumulating,
  nothing is evicted (evicting would reintroduce the same non-monotonicity this
  fix removes). A logged warning fires once when the cap is hit.
- Untagged counters (the common case) key on the metric name itself — no
  allocation. Only tagged counters allocate a key string; `Write()` remains
  allocation-free otherwise.
- `PrometheusFormatter` builds the accumulator lookup once per `Format()` call and
  prefers it for Counter series; a series absent from the accumulator (untracked
  store, or past the cap) falls back to summing the buffer's retained records —
  the original, non-monotonic behavior, now demoted to a fallback rather than the
  only path.
- `LoomMetricsStoreAdapter` and the test double `FakeMetricStore` both implement
  `GetCounterTotals()` as an empty collection (documented as correct — they have
  no accumulator to read from), which exercises the fallback path.

**Verification:** `InMemoryMetricStore` with `bufferCapacity: 16`, 50 counter
increments of 1 — `PrometheusFormatter` reports `50.00`, exceeding the buffer
capacity and confirming the old buffer-summing ceiling is gone. Monotonicity,
tagged series, the cardinality cap, and the fallback path are all covered by new
tests in `InMemoryMetricStoreTests`, `MetricSeriesKeyTests`, and
`PrometheusFormatterTests`. Gauge, histogram, and summary output are unchanged.

---

## 5. Documentation Gaps

### 5.1 Binary Size Target Documentation 🟡 MEDIUM

**Issue:** `CLAUDE.md` states < 15 MB but actual is 16.3 MB

**Files to Update:**
- `CLAUDE.md` - Update target to < 17 MB
- `TESTING.md` - Update thresholds (already done)
- `README.md` - Update specifications (if exists)

**Proposed Change:**
```markdown
## Target Specifications
- Binary size: <17 MB (15 MB core + 2 MB telemetry platform)
  - Basic diagnostic core (Phases 0-4): ~13-14 MB
  - Full telemetry platform (Phases 5-12): ~16-17 MB
```

**Effort:** 30 minutes  
**Priority:** 🟡 MEDIUM (user expectations)

---

### 5.2 Allocation Testing Baseline Documentation 🟢 LOW

**Issue:** No documented baseline for allocation rates

**Proposal:** Add to `TESTING.md`:

```markdown
## Allocation Baselines (Phase 12)

| Operation | Allocation Rate | Notes |
|-----------|----------------|-------|
| Idle | 50-200 B/sec | Baseline |
| Metric Ingestion | 31-61 KB/sec | HTTP overhead |
| Prometheus Export (1st) | ~32 MB | JIT compilation |
| Prometheus Export (2nd+) | ~2.4 MB | Normal operation |
| Query Execution | 5-20 KB/sec | Cold path |
```

**Status:** ✅ **COMPLETED** (added to TESTING.md § 4)

**No further action needed.**

---

### 5.3 Alerting README Documents a Call That Throws 🟢 LOW

**Issue:** `Loom.Telemetry.Alerting/README.md:26` instructs
`services.AddAlertTarget<WebhookAlertTarget>();` as a usage example. Following it
throws at DI resolution time.

**Root Cause:** `AddAlertTarget<T>()` (`ServiceCollectionExtensions.cs`) registers
`T` as a plain DI singleton, requiring `T`'s constructor to be resolvable from the
container. `WebhookAlertTarget(HttpClient httpClient, string webhookUrl)` takes a
`string webhookUrl` parameter that nothing registers — there is no
`AddAlertTarget<WebhookAlertTarget>(string webhookUrl)` overload or factory-based
registration path. The container throws when it tries to construct `T`.

**Fix options:** either add a factory-accepting overload of `AddAlertTarget` (e.g.
`AddAlertTarget<T>(Func<IServiceProvider, T>)`), or correct the README to show
`services.AddSingleton<IAlertTarget>(sp => new WebhookAlertTarget(sp.GetRequiredService<HttpClient>(), "https://...."));`
until such an overload exists.

**Effort:** 15 minutes (doc fix) to 1-2 hours (overload)  
**Priority:** 🟢 LOW (copy-pasting the documented example fails immediately, but
`ConsoleAlertTarget` — the target actually wired up in `Loom.Dashboard` and
`Loom.Web.Api` — works fine)

---

## 6. Future Enhancements

### 6.1 Test Reset Automation 🟡 MEDIUM

**Issue:** Manual test isolation management

**Proposal:** xUnit `IClassFixture<T>` or custom test collection

```csharp
public class LoomTestCollection : ICollectionFixture<LoomTestFixture>
{
    // Shared setup/teardown for all tests
}

public class LoomTestFixture : IDisposable
{
    public LoomTestFixture()
    {
        LoomMetrics.ResetForTesting();
    }
    
    public void Dispose()
    {
        LoomMetrics.ResetForTesting();
    }
}

[Collection("LoomTests")]
public class MyTests
{
    // Automatically gets reset before/after
}
```

**Effort:** 4-6 hours  
**Priority:** 🟡 MEDIUM (test infrastructure improvement)

---

### 6.2 Parallel Test Execution 🟢 LOW (REJECTED)

**Issue:** Tests run sequentially, slow feedback

**Original Proposal:** Enable xUnit parallel execution

```xml
<!-- Loom.Telemetry.Tests.csproj -->
<PropertyGroup>
  <ParallelizeTestCollections>true</ParallelizeTestCollections>
  <MaxParallelThreads>4</MaxParallelThreads>
</PropertyGroup>
```

**Status:** ❌ **REJECTED / superseded** (2026-08-24, see § 9) — this is the opposite of
what shipped. § 1.1's actual fix was
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`, which serializes
the whole assembly rather than unblocking parallelism.

**Reason:** Loom's telemetry surface is deliberately static and process-global (ADR-5)
— `LoomMetrics.Buffers`, `LoomSampling` rules, `LoomCollectors`, the alerting rule
registry. Parallel test *classes* race on those shared buffers regardless of § 6.1's
per-class resets; `AssemblyInfo.cs` documents this in detail. Enabling parallelism
would reintroduce the exact failures § 1.1 fixed.

**Revisiting requires:** removing the global statics (a DI-based buffer/registry
design), not a csproj flag. No such redesign is planned.

---

### 6.3 Benchmark Suite 🟢 LOW

**Issue:** No automated performance benchmarks

**Proposal:** Add BenchmarkDotNet project

```csharp
[MemoryDiagnoser]
public class MetricRecordingBenchmarks
{
    [Benchmark]
    public void RecordCounter_NoTags() =>
        LoomMetrics.RecordCounter("test", 1);
    
    [Benchmark]
    public void RecordCounter_WithTags() =>
        LoomMetrics.RecordCounter("test", 1, new MetricTag("key", "value"));
}
```

**Effort:** 8-12 hours  
**Priority:** 🟢 LOW (nice to have)

---

### 6.4 Deferred Log-Capture Design Questions 🟢 LOW

**Issue:** Three behaviors in the log capture layer (added in `59e0009`) are deliberate
deferrals, not defects. Each depends on work that doesn't exist yet, so deciding now
would be guessing. Recorded here so the triggers aren't lost.

**Context:** Reviewed at the time of `59e0009`. None of these affect correctness of the
current code; all three are stable, documented behavior.

---

**(a) `LoomLogger.BeginScope` returns `null` — scope state is discarded**

Scopes are where correlation IDs and request IDs live, which is exactly the context that
makes log search useful. Capturing them means `LogRecord` gains a structured-properties
field — a schema change, not a bugfix.

*Revisit when:* the RAG corpus design is settled. The corpus determines what shape the
properties field needs, so the schema decision follows it rather than leading it.

---

**(b) `InMemoryLogStore._categories` never evicts**

The ring buffer overwrites records; the category dictionary keeps names for the process
lifetime. `GetCategories()` will therefore list categories whose lines have all aged out.
Not an unbounded-growth risk in practice — categories are logger names, which are
finite — unless a caller generates them dynamically, which nothing does today.

*Revisit when:* the dashboard log view exists. Whether an empty category should appear in
the filter list is a UI question, and the answer determines whether eviction is wanted.

---

**(c) `LoomLogger` is `public` with a public constructor**

The `"Loom."` prefix guard lives in `LoomLoggerProvider.CreateLogger`, so direct
construction bypasses it. Making the class `internal` would close that, but nothing
outside `Loom.Storage` constructs one, and the change risks breaking test construction
for no present benefit.

*Revisit when:* something outside `Loom.Storage` needs to construct a `LoomLogger`.
No trigger today.

---

**Effort:** 1-2 hours (decisions only; implementation cost depends on the outcome)  
**Priority:** 🟢 LOW (blocked on dependencies, not on effort)

---

### 6.5 Alerting Has No Resolution Notifications 🟡 MEDIUM

**Issue:** Alerts can fire, but nothing ever tells a target that a firing condition
stopped being true. Once notified, a target has no signal to distinguish "still
broken" from "fixed" short of the alert simply going quiet.

**Root Cause:**
- `AlertNotification` (`AlertEvaluationHostedService.cs`) is
  `record AlertNotification(AlertRule Rule, MetricAggregate Observed, DateTime FiredAt)`
  — there is no status field (firing vs. resolved), so there is no shape for an "OK"
  notification even if the evaluation loop wanted to send one.
- The evaluation loop only ever calls `notificationChannel.Writer.TryWrite(...)`
  when `rule.Condition(aggregate.Value)` is true; nothing tracks a rule transitioning
  from firing to not-firing, so a resolution is never detected, let alone sent.

**Separate but related bug in the same file:** `AlertEvaluationHostedService.ExecuteAsync`
reads `LoomTelemetryOptionsAlertingExtensions.Rules` once at startup
(`var rules = ...Rules;`) and returns immediately if that snapshot is empty
(`if (rules.Count == 0) { ...; return; }`). Any rule registered after the hosted
service starts is never evaluated — the service has already exited its loop (or
never entered one) before that rule exists.

**Impact:** Operators get paged when something breaks but never get an automatic
"resolved" signal, and rules added post-startup (e.g. via a config reload) silently
do nothing.

**Effort:** 4-8 hours (status field + resolution detection + fixing the
startup-snapshot bug, which likely needs the rule registry to be observable rather
than read once)  
**Priority:** 🟡 MEDIUM (alerting works for the "something is wrong" case that
exists today, but both gaps reduce operational trust in the feature)

---

## 7. Priority Summary

### High Priority (Pre-1.0 Release)

**None open.** § 1.1 (test isolation) and § 3.1 (ingest endpoint), the only two HIGH
items this document ever tracked, are both completed.

1. 🔴 ~~Test isolation fix (§ 1.1)~~ ✅ COMPLETED
2. 🔴 ~~Missing ingest endpoint (§ 3.1)~~ ✅ COMPLETED

**Total High Priority Work:** 0 hours

---

### Medium Priority (Post-1.0)
1. 🟡 Binary size documentation (§ 5.1) - 30 minutes
2. 🟡 Test reset automation (§ 6.1) - 4-6 hours
3. 🟡 Alerting has no resolution notifications (§ 6.5) - 4-8 hours

**Total Medium Priority Work:** ~9-15 hours

---

### Low Priority (Backlog)
1. 🟢 Search endpoint decision (§ 3.2) - 1 hour (decision only)
2. 🟢 Benchmark suite (§ 6.3) - 8-12 hours
3. 🟢 Deferred log-capture design questions (§ 6.4) - 1-2 hours (decisions only, blocked)
4. 🟢 No test coverage for `/api/logs/tail` clamping (§ 1.4) - 1-2 hours
5. 🟢 Alerting README documents a call that throws (§ 5.3) - 15 min - 2 hours

**Total Low Priority Work:** ~11-18 hours (§ 6.4 excluded - blocked on dependencies)

---

## 8. Tracking

| Issue ID | Title | Priority | Effort | Status | Assigned | Target |
|----------|-------|----------|--------|--------|----------|--------|
| ~~DEBT-001~~ | ~~Test isolation fix~~ | ~~🔴 HIGH~~ | ~~2-4h~~ | ✅ Completed | - | § 1.1 |
| ~~DEBT-002~~ | ~~Flaky timer tests~~ | ~~🟡 MEDIUM~~ | ~~4-6h~~ | ✅ Completed | - | § 1.2 |
| DEBT-003 | Binary size docs | 🟡 MEDIUM | 30m | Open | - | Phase 13 |
| DEBT-004 | Test automation | 🟡 MEDIUM | 4-6h | Open | - | Post-1.0 |
| ~~DEBT-005~~ | ~~LINQ optimization~~ | ~~🟢 LOW~~ | ~~1-2h~~ | ✅ Completed (stale) | - | § 4.1 |
| DEBT-006 | Search endpoint | 🟢 LOW | 1h | Open | - | Backlog |
| ~~DEBT-007~~ | ~~Parallel tests~~ | ~~🟢 LOW~~ | ~~30m~~ | ❌ Rejected | - | See § 6.2, § 9 |
| DEBT-008 | Benchmarks | 🟢 LOW | 8-12h | Open | - | Backlog |
| ~~DEBT-009~~ | ~~Ingest endpoint~~ | ~~🔴 HIGH~~ | ~~4h~~ | ✅ Completed | - | Phase 12 |
| ~~DEBT-010~~ | ~~Anonymous type~~ | ~~🟢 LOW~~ | ~~5m~~ | ✅ Completed | - | Phase 12 |
| DEBT-011 | Log-capture deferrals | 🟢 LOW | 1-2h | Deferred | - | See § 6.4 triggers |
| ~~DEBT-012~~ | ~~PrometheusFormatter invalid output~~ | ~~🟡 MEDIUM~~ | ~~3-5h~~ | ✅ Completed | - | § 4.3 |
| DEBT-013 | `/api/logs/tail` test coverage | 🟢 LOW | 1-2h | Open | - | Backlog |
| DEBT-014 | Alerting README broken example | 🟢 LOW | 15m-2h | Open | - | Backlog |
| DEBT-015 | Alerting resolution notifications | 🟡 MEDIUM | 4-8h | Open | - | Backlog |
| DEBT-016 | Prometheus counters stop growing past buffer wrap | 🟡 MEDIUM | 4-6h | Open | - | Backlog |

---

## 9. Decision Log

### 2026-08-18: Binary Size Target Adjustment

**Decision:** Accept 16.3 MB as reasonable for full feature set

**Rationale:**
- Original 15 MB target for basic diagnostic core only
- Full telemetry platform adds significant value (query language, exporters, alerting)
- 16.3 MB is only 8.7% over target
- Industry comparison: competitors are 30-50+ MB
- Further size reduction would require sacrificing features

**Action:** Update documentation to reflect < 17 MB target

---

### 2026-08-18: Test Isolation Strategy

**Decision:** Implement `ResetForTesting()` method for test cleanup

**Rationale:**
- Minimal code change (5 lines)
- Isolated to DEBUG builds only
- Doesn't affect production code
- Standard testing pattern
- Better than introducing dependency injection just for tests

**Alternative Considered:** DI-based buffer factory (rejected - too invasive)

---

### 2026-08-24: Defer Log-Capture Design Questions

**Decision:** Ship `BeginScope` returning `null`, no category eviction, and a `public`
`LoomLogger`. Revisit each when its dependency lands (see § 6.4).

**Rationale:**
- All three depend on work that doesn't exist yet (dashboard log UI, RAG corpus schema)
- Deciding now means guessing at requirements, then likely reworking
- None affects correctness of the shipped code - each is stable, documented behavior
- Recording the revisit triggers costs nothing; a wrong early decision costs rework

**Alternative Considered:** Resolve all three during the `59e0009` follow-up fix commit
(rejected - a bugfix commit carrying schema changes conflates two kinds of risk, and
`LogRecord` gaining a properties field is a schema change)

---

### 2026-08-24: Reject § 6.2 (Parallel Test Execution)

**Decision:** Reject DEBT-007 / § 6.2's proposal to enable xUnit parallel test
execution. Superseded by what actually shipped for § 1.1: assembly-wide
serialization (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`),
the opposite of this proposal.

**Rationale:**
- Loom's telemetry surface is deliberately static and process-global (ADR-5):
  `LoomMetrics.Buffers`, `LoomSampling` rules, `LoomCollectors`, and the alerting
  rule registry are all shared singletons, by design, for the shipped product.
- Parallel test *classes* race on those shared buffers regardless of per-class
  setup/teardown — that race was the actual root cause of § 1.1's flakiness, and
  `Loom.Telemetry.Tests/AssemblyInfo.cs` documents it in detail.
- Enabling parallelism as § 6.2 proposed would reintroduce exactly the failures
  § 1.1 fixed.

**Alternative Considered:** Removing the global statics in favor of an
injectable/scoped telemetry surface, which would make parallel test classes safe.
Not undertaken - out of scope for a backlog reconciliation pass, and a real
architectural change to ADR-5, not a test-infra tweak.

**Action:** § 6.2 marked REJECTED. Revisiting parallel test execution requires
removing the global statics, not a csproj flag.

---

## 10. References

- **Phase 12 Testing Results:** `TESTING.md` § 11
- **AOT Compliance Guidelines:** `CLAUDE.md` § Critical Architectural Constraints
- **Binary Size Specifications:** `CLAUDE.md` § Project Overview

---

**Document Owner:** Project Loom v2 Team  
**Last Review:** 2026-08-24  
**Next Review:** Phase 13 completion or pre-1.0 release
