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
existing per-class resets sufficient. The suite is small and I/O-light, so running
serially has negligible wall-clock cost against a source of non-deterministic
failures.

**Correction (2026-08-24):** an earlier revision of this entry claimed "no
`ResetForTesting()` API was added". That was wrong and contradicted § 9's own decision
log. `LoomMetrics.ResetForTesting()` exists at `Loom.Telemetry/LoomMetrics.cs:155` and
test classes call it — it was option 1 of this entry's original proposal and it did ship.
What it could not do alone was survive xUnit running test *classes* in parallel, since
the resets themselves then raced. The assembly-wide serialization is the piece that was
added on top. No DI-based buffer factory was introduced (that was option 2, still
rejected as too invasive).

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

### 2.1 Binary Size Target Adjustment 🟡 MEDIUM (COMPLETED)

**Status:** ✅ **RESOLVED** — Option A was taken. `CLAUDE.md` now states `<17 MB` in
every place it names the target, and `TESTING.md`'s thresholds match. See also § 5.1,
which tracked the same documentation change from the docs side.

**Issue:** Original target was < 15 MB, actual size is 16.3 MB

**Context:**
- Original 15 MB target was for basic diagnostic core (Phases 0-4)
- Phases 5-12 added significant features:
  - Query language with SQL-like parser
  - Alerting engine with sliding windows
  - Exporters (at the time: Prometheus, Grafana Cloud, Elasticsearch, Console —
    Grafana Cloud and Elasticsearch were deleted in `6d8cc2b` as non-functional dead
    code, so the 16.3 MB figure below predates that removal and is now pessimistic)
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

### 5.1 Binary Size Target Documentation 🟡 MEDIUM (COMPLETED)

**Status:** ✅ **RESOLVED** (2026-08-24) — all three files now agree on `<17 MB`.
`README.md`'s constraints table was the last holdout; it advertised `< 15 MB` long after
`CLAUDE.md` and `TESTING.md` had been corrected.

**Issue:** `CLAUDE.md` states < 15 MB but actual is 16.3 MB

**Files to Update:**
- `CLAUDE.md` - Update target to < 17 MB ✅
- `TESTING.md` - Update thresholds ✅
- `README.md` - Update specifications ✅

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

### 6.1 Test Reset Automation 🟢 LOW (SUPERSEDED)

**Status:** ⚠️ **SUPERSEDED by § 1.1** — downgraded from 🟡 MEDIUM. The problem this
entry exists to solve is already solved by other means: `LoomMetrics.ResetForTesting()`
ships (`Loom.Telemetry/LoomMetrics.cs:155`), test classes call it in their
constructor/`Dispose`, and `[assembly: CollectionBehavior(DisableTestParallelization =
true)]` removes the cross-class race that made those resets insufficient. The suite runs
362/362 with zero skips and no isolation-related flakiness.

What remains is optional tidiness — a shared fixture would centralize the per-class reset
boilerplate — with no correctness benefit. Keep only if that duplication becomes a
maintenance problem.

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

### 6.5 Alerting Has No Resolution Notifications 🟡 MEDIUM (COMPLETED)

**Status:** ✅ **RESOLVED** in `3bd0d8b`. `AlertState` (Firing/Resolved) plus `State` and
`ResolvedAt` on `AlertNotification`; `AlertEvaluationHostedService` now tracks active
alerts separately from the re-notify cooldown and emits exactly one Resolved when a
condition clears, carrying the original firing time. Resolution deliberately ignores both
the cooldown and silencing — an alert an operator already saw open always gets its close.
Console, webhook and email targets render both states; the webhook payload gained a
lowercase `status` and `ResolvedAt`.

Also fixed here: an empty evaluation window used to return a **zero aggregate** rather
than "no data", so `agg => agg.Average < 5` fired spuriously on silence — and once
resolution existed, a `>` condition would have auto-resolved an active alert merely
because data stopped arriving. It now returns null and the tick is skipped, preserving
state. § 6.6 tracks the no-data grace period that follows from that.

**⚠️ The startup-snapshot bug described below was NOT fixed — it is carved out as
§ 6.7 and remains open.**

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

**Status:** Fire/resolve state machine and status field implemented. The
startup-snapshot bug described above is a separate, still-open issue — not touched
by this work.

---

### 6.6 Alerting Has No No-Data Grace Period 🟡 MEDIUM

**Issue:** `AlertEvaluationHostedService.ComputeWindowAggregate` now returns `null`
when a metric's window holds no samples, and the evaluation loop treats `null` as
"skip this rule — no fire, no resolve, state preserved" (see § 6.5). That's correct
as far as it goes, but it means a metric that stops arriving entirely (crashed
collector, network partition, misconfigured emitter) leaves any active alert on it
stuck in whatever state it was in — firing forever with no further notifications, or
never firing at all — for as long as the process runs. There's no timeout that turns
prolonged silence into its own signal.

**Root Cause:** No-data and "condition false" are correctly no longer conflated, but
no-data has no expiry. The loop has no concept of "how long has this rule had no
data" to compare against a grace period.

**Impact:** An operator with an active alert on a metric whose source dies gets no
further updates — the alert just stops updating silently, indistinguishable from
"still broken, cooldown hasn't elapsed yet" without checking timestamps.

**Fix sketch (not implemented):** Track last-seen-data time per rule. After a
configurable grace period with no data, either auto-resolve the active alert (with a
distinct reason so it's not confused with condition-cleared) or fire a separate
"no data" alert. Needs a design decision on which, and whether it's per-rule or
global.

**Effort:** 2-4 hours  
**Priority:** 🟡 MEDIUM (silent gap, not a wrong answer — but "the alert went quiet"
is exactly the operational-trust problem § 6.5 exists to fix)

---

### 6.7 Alert Rules Registered After Startup Are Never Evaluated 🟡 MEDIUM

**Issue:** Carved out of § 6.5, which fixed resolution notifications but deliberately
left this untouched.

`AlertEvaluationHostedService.ExecuteAsync` reads the rule registry **once**:

```csharp
var rules = LoomTelemetryOptionsAlertingExtensions.Rules;
if (rules.Count == 0) { logger?.LogInformation("...no rules registered."); return; }
```

Two consequences. If the registry is empty when the hosted service starts, the method
**returns** — the service never enters its timer loop, and no rule registered later is
ever evaluated, for the life of the process. Even when it is non-empty, the tick interval
is computed once from that snapshot (`rules.Select(r => r.Window).Min() / 10`), so a rule
added afterwards with a shorter window would be evaluated too slowly even if the loop did
see it.

**Root Cause:** `LoomTelemetryOptionsAlertingExtensions.Rules` is a plain
`public static readonly List<AlertRule>` — process-global, not thread-safe, and not
observable. Nothing can notify the hosted service that it changed.

**Impact:** Registration order becomes load-bearing and silently so. Any config-reload or
runtime rule-management feature would appear to work — the rule lands in the list — while
never firing.

**Fix sketch (not implemented):** Re-read the registry each tick rather than snapshotting
it, and drop the empty-list early return so the loop idles instead of exiting.
Recomputing the tick interval per pass covers the window case. A fuller fix makes the
registry observable and thread-safe, which is also a prerequisite for any alert CRUD API.

**Effort:** 2-4 hours (re-read per tick), more if the registry is made properly
observable

**Priority:** 🟡 MEDIUM (silently does nothing; the failure mode is invisible)

---

### 6.8 § 6.6 and § 6.7 Are Now Live Concerns, Not Latent Ones 🟡 MEDIUM

**Issue:** Both § 6.6 (no-data grace period) and § 6.7 (rules registered after startup
never evaluated) were previously unreachable in practice — `AddAlert` was implemented
and tested, but nothing called it outside of tests, so `LoomTelemetryOptionsAlertingExtensions.Rules`
was empty at runtime in both hosts and `AlertEvaluationHostedService` exited its loop
immediately. Neither gap could actually bite anyone.

That changed with the webhook-alerting activation work: `Loom.Dashboard` and
`Loom.Web.Api` now both call `AddAlert` at startup (`HighCpuUsage`/`HighMemoryUsage` in
the Dashboard; `HighIngestErrorRate`/`HighIngestLatency` in Web.Api) and register a real
`WebhookAlertTarget` alongside `ConsoleAlertTarget`. Rules are registered before the app
is built, so § 6.7's empty-registry-at-startup failure mode does not currently trigger —
but the registry is no longer empty, meaning any *future* runtime rule-registration path
(a config reload, an alert-management API) would hit exactly the silent-no-op described
in § 6.7. And with `AlertEvaluationHostedService` now actually ticking against live
metrics (`cpu-usage`, `working-set` in the Dashboard), a source that goes quiet — a
crashed `EventPipeBridge` connection, a target process that exits — will leave any active
alert stuck exactly as § 6.6 describes, for real, in production, not just in a test.

**No fix attempted here** — this entry only updates the status framing. Both § 6.6 and
§ 6.7 remain open with their existing fix sketches.

**Effort:** N/A (tracking entry only)
**Priority:** 🟡 MEDIUM (unchanged from § 6.6/§ 6.7 — the risk was already rated, only its
reachability changed)

---

### 6.9 Statistical Anomaly Detection on Metrics 🟢 LOW (PLANNED)

**Issue:** Alert rules are threshold-based only — `agg.Average > 0.8`, `agg.P99 > 500`.
Every threshold is a number somebody guessed, and a guess that is wrong in either
direction is useless: too tight and it pages on normal load, too loose and it never
fires. There is no way to express "tell me when this is abnormal for *this* process"
rather than "tell me when this crosses a constant I picked."

**Proposed:** Statistical baselining over the metric ring buffer, feeding the alerting
path that already exists. Compute a rolling baseline (EWMA, or mean plus standard
deviation over a trailing window) per metric series, and let a rule trigger on deviation
from it — e.g. "working set is 4σ above its last-hour baseline" — instead of, or
alongside, a fixed threshold.

**Why it fits Loom specifically:**
- It is arithmetic, not machine learning. No model, no embeddings, no new dependency,
  no measurable impact on the <17 MB binary budget — the same reasoning that made BM25
  the right call for log search over an embedding model (§ see Bm25LogSearch).
- The data is already in memory. `IMetricStore.GetBuffers()` and `MetricBuffer.ReadSince`
  provide the trailing window with no new storage.
- It plugs into `AlertRule`/`AlertEvaluationHostedService` rather than replacing them —
  a deviation predicate is just another way to produce the boolean a rule already needs.
- It is a genuine differentiator. `dotnet-monitor`'s collection rules are threshold- and
  event-triggered; nothing in the .NET diagnostics tooling space does edge-side
  statistical baselining.

**Open questions (decide before implementing, do not guess):**
1. Which estimator — EWMA is cheap and adapts, but a fixed trailing window with mean/σ is
   easier to explain and to test deterministically. Favor whichever is easier to write a
   failing test for.
2. Cold-start behavior. A baseline computed from three samples will fire constantly. A
   minimum-sample threshold is required, and it interacts with § 6.6 (no-data grace
   period) — both are "the rule should stay quiet until it actually knows something."
3. Whether deviation replaces or composes with thresholds. Composition (`Average > 0.8
   AND 3σ above baseline`) is more useful and no harder to express in the existing
   builder.
4. Seasonality is explicitly **out of scope**. A tool whose store dies with the process
   cannot observe a daily cycle. Do not build toward it.

**Depends on:** § 6.3 (benchmark suite) is *not* a blocker, but this is a hot-path-adjacent
computation over the metric buffers and should be measured, not assumed cheap.

**Effort:** 6-10 hours (including the design decisions above)
**Priority:** 🟢 LOW — a differentiator, not a defect. Nothing is broken without it, and
the two open MEDIUM alerting gaps (§ 6.6, § 6.7) should land first since this builds on
the same evaluation loop they affect.

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
1. 🟡 Alerting no-data grace period (§ 6.6) - 2-4 hours
2. 🟡 Alert rules registered after startup never evaluated (§ 6.7) - 2-4 hours

**Total Medium Priority Work:** ~4-8 hours

Both are in the same file (`AlertEvaluationHostedService`) and are natural companions:
§ 6.6 needs per-rule last-seen-data tracking, § 6.7 needs the rule list re-read each
tick. Doing them together is cheaper than either alone.

Closed since the last revision: § 5.1 (binary size docs — all three files now agree on
<17 MB), § 6.1 (superseded by § 1.1's fix; downgraded to LOW), § 6.5 (resolution
notifications shipped in `3bd0d8b`), § 2.1 (Option A taken), § 4.3 and § 4.4
(Prometheus output and counter monotonicity).

---

### Low Priority (Backlog)
1. 🟢 Search endpoint decision (§ 3.2) - 1 hour (decision only)
2. 🟢 Benchmark suite (§ 6.3) - 8-12 hours
3. 🟢 Deferred log-capture design questions (§ 6.4) - 1-2 hours (decisions only, blocked)
4. 🟢 No test coverage for `/api/logs/tail` clamping (§ 1.4) - 1-2 hours
5. 🟢 Alerting README documents a call that throws (§ 5.3) - 15 min - 2 hours
6. 🟢 Size optimization flags (§ 2.2) - documentation only
7. 🟢 Allocation testing baseline documentation (§ 5.2)
8. 🟢 Test reset automation (§ 6.1) - superseded, optional tidiness only
9. 🟢 Statistical anomaly detection on metrics (§ 6.9) - 6-10 hours (planned enhancement)

**Total Low Priority Work:** ~17-28 hours (§ 6.4 excluded - blocked on dependencies)

**§ 6.3 (benchmark suite) is worth more than its LOW rating suggests.** Two deferred
optimizations are explicitly waiting on measurement it would provide: the zero-allocation
struct key for the counter accumulator (§ 4.4's note) and any decision about whether
tagged-counter write allocation matters at all. Without benchmarks those stay guesses.

---

## 8. Tracking

| Issue ID | Title | Priority | Effort | Status | Assigned | Target |
|----------|-------|----------|--------|--------|----------|--------|
| ~~DEBT-001~~ | ~~Test isolation fix~~ | ~~🔴 HIGH~~ | ~~2-4h~~ | ✅ Completed | - | § 1.1 |
| ~~DEBT-002~~ | ~~Flaky timer tests~~ | ~~🟡 MEDIUM~~ | ~~4-6h~~ | ✅ Completed | - | § 1.2 |
| ~~DEBT-003~~ | ~~Binary size docs~~ | ~~🟡 MEDIUM~~ | ~~30m~~ | ✅ Completed | - | § 2.1, § 5.1 |
| DEBT-004 | Test automation | 🟢 LOW | 4-6h | ⚠️ Superseded | - | § 6.1 |
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
| ~~DEBT-015~~ | ~~Alerting resolution notifications~~ | ~~🟡 MEDIUM~~ | ~~4-8h~~ | ✅ Completed | - | § 6.5 (`3bd0d8b`) |
| ~~DEBT-016~~ | ~~Prometheus counters stop growing past buffer wrap~~ | ~~🟡 MEDIUM~~ | ~~4-6h~~ | ✅ Completed | - | § 4.4 (`25a1fae`) |
| DEBT-017 | Alerting no-data grace period | 🟡 MEDIUM | 2-4h | Open | - | § 6.6 |
| DEBT-018 | Alert rules added after startup never evaluated | 🟡 MEDIUM | 2-4h | Open | - | § 6.7 |
| DEBT-019 | § 6.6/§ 6.7 now reachable at runtime (status update only) | 🟡 MEDIUM | N/A | Open | - | § 6.8 |

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

### 2026-08-24: Backlog Reconciliation After the Cleanup Branch

**Decision:** Close § 2.1, § 5.1 and § 6.5; downgrade § 6.1 to LOW/superseded; split the
startup-snapshot bug out of § 6.5 into § 6.7; correct § 1.1's `ResetForTesting()` claim.

**Rationale:**
- § 6.5 was marked open on `main` while `3bd0d8b`, which implements it, was already
  merged — the document was reporting shipped work as outstanding.
- § 1.1 asserted "no `ResetForTesting()` API was added". It exists at
  `LoomMetrics.cs:155`, and § 9's own 2026-08-18 entry records the decision to build it.
  The document contradicted itself; the correction is recorded in § 1.1 rather than by
  quietly rewriting the original claim.
- § 6.5 bundled two unrelated defects. Only resolution notifications were fixed, so
  closing the whole entry would have buried a live bug. Carving out § 6.7 keeps it
  visible.
- § 2.1 and § 5.1 tracked the same documentation change from two angles and were both
  effectively done; `README.md`'s `< 15 MB` was the last stale copy.

**Pattern worth noting:** five entries in this document were found stale during this
branch — work happened incidentally and nothing closed the entry. Closing the entry in
the commit that does the work avoids re-deriving state later.

---

### 2026-08-25: File Anomaly Detection as § 6.9; Auth Deferred to Phase 14

**Decision:** Record statistical anomaly detection as a planned LOW enhancement (§ 6.9)
rather than starting it, and keep endpoint authentication out of this document — it is
tracked as Phase 14 work, not backlog.

**Rationale:**
- Anomaly detection came out of a positioning discussion, not a defect report. It is a
  differentiator against `dotnet-monitor`, whose collection rules are threshold- and
  event-triggered with no edge-side baselining. Left unwritten it would have survived
  only in conversation.
- It is filed LOW deliberately. Nothing is broken without it, and it builds on
  `AlertEvaluationHostedService` — the same loop § 6.6 and § 6.7 still have open MEDIUM
  defects in. Building a new capability on a loop with two known gaps would compound
  them.
- Its design questions are recorded as *open* rather than resolved. Estimator choice and
  cold-start behavior are real decisions with test-design consequences, and guessing them
  now would bake in an answer nobody evaluated.
- Authentication is deliberately not a backlog entry. Every endpoint is currently
  unauthenticated, which is a release gate rather than a tracked improvement, and the
  repository is now public — the mitigation in the meantime is that hosts bind via
  `ListenLocalhost`, so access is over an SSH tunnel rather than an exposed port.

---

## 10. References

- **Phase 12 Testing Results:** `TESTING.md` § 11
- **AOT Compliance Guidelines:** `CLAUDE.md` § Critical Architectural Constraints
- **Binary Size Specifications:** `CLAUDE.md` § Project Overview

---

**Document Owner:** Project Loom v2 Team  
**Last Review:** 2026-08-24  
**Next Review:** Phase 13 completion or pre-1.0 release
