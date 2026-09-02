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

> **Superseded 2026-08-28.** The 16.3 MB figure below is stale and the reasoning is
> wrong. Measured on `d277a58`: **14.737 MB** — under the 17 MB limit by 2.26 MB, and
> under the original 15 MB target. Option B's headline lever
> (`IlcGenerateStackTraceData=false`) was measured at **27 KB**, not megabytes, so
> "aggressive trimming" was never the trade-off this entry assumed. The actual cost was
> `WebApplication.CreateBuilder` rooting IIS, HTTP/3/QUIC, regex route constraints and
> unused config/logging providers — a subsystem-rooting problem, not a trimming
> problem. Fixed in `d277a58` by switching to `CreateSlimBuilder`.

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

### 3.3 `Loom.Web.Api` Cannot Serve HTTPS 🟢 RESOLVED BY DECISION — not by fixing it

**Status:** ✅ **CLOSED 2026-08-28.** Neither option below was taken. A third was:
**Loom does not terminate TLS at all.** It binds loopback in code, `UseHsts()` and
`UseHttpsRedirection()` are deleted, and the SSH tunnel carries the only hop that leaves
the machine. In-process TLS was measured at **+0.946 MB** (15.124 → 16.070 MB) to protect
a hop that never crosses a network, and it would have added certificate provisioning,
permissions under `ProtectSystem=strict`, and renewal as a standing operational chore.

**What made the decision, rather than the measurement alone:** the threat model in
`IMPLEMENTATION-METHODOLOGY.md` § 14.0.4 already asserted that both hosts bind loopback.
That was true of `Loom.Dashboard` (`ListenLocalhost`) and **false** of `Loom.Web.Api`,
which had no explicit bind and accepted whatever `ASPNETCORE_URLS` supplied. Binding
loopback in code costs nothing, cannot be overridden by configuration, and makes the
documented property real — a stronger guarantee than TLS over a hop that stays inside one
machine.

**Option 2 below is retained as the documented path for non-tunnel access**: front the
loopback port with a reverse proxy and let it own the certificate. Option 1 is rejected.

**Still true and worth keeping:** browsers treat `http://localhost` as a secure context,
so nothing in the Angular app depends on TLS being present.

The original analysis follows.

---

### 3.3 (original analysis) `Loom.Web.Api` Cannot Serve HTTPS

**Location:** `Loom.Web.Api/Program.cs` — `WebApplication.CreateSlimBuilder` with no
`UseKestrelHttpsConfiguration()` and no HTTPS listener anywhere.

`CreateSlimBuilder` does not wire HTTPS configuration. Binding an `https://` address
therefore fails at startup, verified in isolation on .NET 10.0.11:

```
CreateSlimBuilder + https://localhost:5443
  -> InvalidOperationException: Call UseKestrelHttpsConfiguration() on IWebHostBuilder
     to automatically enable HTTPS when an https:// address is used.
     Hosting failed to start.
```

A grep of `Loom.Web.Api` for `UseKestrelHttpsConfiguration|ListenAnyIP|UseHttps|5443`
returns only the `UseHttpsRedirection()` call itself.

**Why this matters more than it looks:**

- `CLAUDE.md`'s security architecture specifies HTTPS on 5443 with an HTTP redirect on
  5080. Neither is achievable as configured.
- Phase 14's "HSTS + HTTPS enforcement" was **nominal**. With no HTTPS port,
  `UseHttpsRedirection` logs `Failed to determine the https port for redirect` and passes
  the request straight through — there was no redirect in Production at all. This is also
  why § 4.7 was unobservable for so long: the redirect it described never happened.
- The fix costs binary size, and `CreateSlimBuilder` was adopted in `d277a58` precisely to
  save 3.58 MB.

**Measured cost of the fix** (`builder.WebHost.UseKestrelHttpsConfiguration()`, Release
`win-x64`, publish clean both times):

| Build | Size | Headroom to 17 MB |
|---|---|---|
| `149b57f` as committed | 15.124 MB | 1.876 MB |
| with HTTPS configuration | **16.070 MB** | **0.930 MB** |
| delta | **+0.946 MB** | |

**Verified working end to end** with that one line added: the Production binary bound
`http://localhost:5080` and `https://localhost:5443` together, an HTTP request returned
`307` to the HTTPS address **carrying all four security headers**, and the HTTPS endpoint
returned `401` without a token. That observation is what actually closed § 4.7.

**Two options for Phase 15:**

1. **Enable it** — one line plus certificate provisioning. Costs 969 KB and leaves
   930 KB of headroom, which is workable: `Loom.Web.Api` has no `wwwroot` (the Angular
   bundle lives in `Loom.Dashboard`), and Phase 14D touches Angular, DevTools, and the
   Dashboard rather than this host. Brings in a real dependency on certificate path, file
   permissions for the `loomd` user, and renewal.
2. **Terminate TLS at a reverse proxy**, leaving Loom HTTP-only on loopback. Zero binary
   cost and defensible, since both hosts already sit behind an SSH tunnel. **If this is
   chosen, `UseHsts()` and `UseHttpsRedirection()` must be deleted rather than left in
   place** — they would imply a protection the process does not provide.

**Effort:** 1 hour (option 1, excluding certificate provisioning)
**Priority:** 🔴 HIGH — blocks Phase 15

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

### 4.5 Log Row Declares role="button" While Containing a Button 🟢 LOW

**Issue:** `.log-row` in `logs.component.html` carries `role="button"` and contains the
trace-filter chip, which is a real `<button>`. ARIA forbids interactive descendants inside
a `button` role, so assistive technology may not expose the chip at all.

**Found:** review of `c38d2a5`.

**Fix:** drop `role="button"` from the row, keeping `tabindex="0"` and
`[attr.aria-expanded]` — an element with `aria-expanded` and a tab stop is a valid
disclosure pattern without the invalid nesting. `.group-row` shares the markup and should
change with it, though it has no nested control today.

**Not urgent because:** the behavioural bug this structure caused — activation firing twice
on the keyboard path — was fixed separately in `da99d99`. What remains is a semantics
defect with no functional symptom.

**Effort:** 15-30 minutes
**Priority:** 🟢 LOW

---

### 4.6 parseArguments Runs Twice Per Change-Detection Cycle 🟢 LOW

**Issue:** the expanded-row detail panel calls `rowArguments(row)` twice — once in the
`@if` length guard, once in the `@for`. Each call is a full `JSON.parse` plus
`Object.entries().map()`. Change detection runs on every incoming log line, so that is two
parses per line for as long as any row stays expanded.

**Found:** review of `c38d2a5`.

**Fix:** a `computed` keyed on `expandedKey`, so parsing happens once per expansion instead
of once per cycle.

**Not urgent because:** only one row expands at a time and argument payloads are a handful
of keys. It becomes a real cost if multiple rows ever expand at once, or if payloads grow.

**Effort:** 30 minutes
**Priority:** 🟢 LOW

---

### 4.7 Security Headers Absent on Production Redirects 🟢 LOW (COMPLETED)

**Status:** ✅ **RESOLVED** in Phase 14C (`149b57f`) — the header middleware moved to the
front of the `Loom.Web.Api` pipeline.

**Verification was only possible after § 3.3 was understood.** With `CreateSlimBuilder`
unable to bind HTTPS, `UseHttpsRedirection` passed requests through and no 307 was ever
emitted, so this defect could not be observed. Adding
`UseKestrelHttpsConfiguration()` temporarily and binding both ports produced the redirect
and confirmed the fix:

```
HTTP/1.1 307 Temporary Redirect
Location: https://localhost:5443/api/metrics/cpu
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Content-Security-Policy: default-src 'none'; frame-ancestors 'none'
```

The original report below is retained for context.


**Location:** `Loom.Web.Api/Program.cs` — the header middleware is registered *after*
`app.UseHttpsRedirection()`.

`UseHttpsRedirection` short-circuits the pipeline: it writes a 307 and returns without
calling `next()`. The header middleware sits downstream of it, so it never runs for a
redirect. Every plain-HTTP request in Production therefore receives a response carrying
no `X-Content-Type-Options`, no `X-Frame-Options`, no CSP, and no `Referrer-Policy`.

**Not urgent because:** the redirect body is empty and the `Location` header is the only
thing a client acts on, and both hosts bind loopback. It matters if a browser is ever
pointed at the HTTP port directly, since the response is then a bare document with no
framing or sniffing protection.

**Fix:** move `app.UseSecurityHeaders()` above the `UseHsts()` / `UseHttpsRedirection()`
block so it wraps the redirect. Already specified as part of Phase 14 Step 14.2 in
`IMPLEMENTATION-METHODOLOGY.md`; filed here so it is tracked if Phase 14 slips.

**Effort:** 5 minutes
**Priority:** 🟢 LOW

---

### 4.8 `loom dashboard` Reports Every Launch Failure as "Package Not Found" 🟢 LOW (COMPLETED)

**Status:** ✅ **RESOLVED 2026-08-29** — item 2 fixed in `LaunchDashboard`; item 1 required
no change, since `loom-dashboard --version` already short-circuits at
`Loom.Dashboard/Program.cs:17-21`, before `AddLoomSecurity()` at line 100.

**Location:** `Loom.DevTools/Commands/DashboardCommand.cs:68-93`

The `loom-dashboard --version` probe is wrapped in a bare `catch` that prints
`Dashboard package not found. / Install with: dotnet tool install -g LoomDiagnostics.Dashboard`
for *any* failure — a non-zero exit, a crash on startup, a missing config file. Only one
of those is actually a missing package.

**Why it matters more after Phase 14:** the dashboard will refuse to start without
`LOOM_JWT_KEY_FILE` and `LOOM_AUTH_USERS_FILE`. If `--version` also loads credentials,
a missing key surfaces as "package not found", sending the operator to reinstall a tool
that is already installed correctly.

**Fix (specified in `IMPLEMENTATION-METHODOLOGY.md` § 14.7.2.2):**

1. `loom-dashboard --version` short-circuits before loading the key or users file. A
   version probe must not require credentials.
2. Narrow the catch to `Win32Exception` for the "not installed" message — measured, that
   is what an absent executable actually raises. Report the child's exit code and stderr
   for everything else.

**Effort:** 30 minutes
**Priority:** 🟢 LOW (raise to MEDIUM if Phase 14 ships before it)

---

### 4.9 `/api/health` Now Requires a Token, Breaking Liveness Probes 🟢 LOW (COMPLETED)

**Status:** ✅ **RESOLVED 2026-08-28** — option 1 taken, the endpoint is marked
`LoomAllowAnonymous`. Once § 3.3 settled on a loopback-only service, an anonymous health
page stopped being an exposure question: only processes on the same machine can reach it,
and it reports status, uptime, and working-set size. Options 2 and 3 both ended in a
long-lived credential living in a systemd unit, which is worse than what it protects.

**Applied on both hosts.** The original entry named only the Dashboard, and the marker
initially landed there alone — `Loom.Web.Api/Extensions/EndpointExtensions.cs` was missed
and its `/api/health` still returned 401, verified by probe. That is the host the decision
was actually written for: `Loom.Web.Api` is the Native AOT publish target and therefore
the one that runs under systemd, where a liveness probe cannot hold a 60-minute JWT.
Both are now marked; both verified 200 without a token.

The original analysis follows.

**Location:** `Loom.Dashboard/Extensions/EndpointExtensions.cs:52`

Phase 14C enforces authentication on every endpoint that is not explicitly marked, and
`/api/health` is not marked. It now returns 401 without a bearer token.

This is **correct per the Phase 14 scope decision** ("everything protected, no
exceptions") and is not a defect. It is filed because Phase 15 will run into it: a
systemd `ExecStartPost` check, a container `HEALTHCHECK`, or any external monitor cannot
hold a 60-minute JWT, so the probe fails permanently and the unit looks unhealthy while
the process is fine.

**What the endpoint exposes:** `Status`, `Timestamp`, `UptimeSeconds`, `MemoryUsageMb`.
No telemetry, no logs, no configuration.

**Three options, needing a decision rather than a fix:**

1. Mark it `LoomAllowAnonymous`. Simplest, and the payload is close to inert — though
   uptime and working-set size are a small unauthenticated information leak.
2. Give probes a scope-restricted service token, reusing the § 14.7.3 mechanism with a
   new `health` scope. Consistent with the Prometheus decision, but adds a scope value
   and a credential to provision.
3. Leave it protected and let the probe authenticate. Realistically means embedding a
   long-lived token in a systemd unit, which is option 2 with worse ergonomics.

**Effort:** 15 minutes once decided
**Priority:** 🟢 LOW — no impact until Phase 15 deployment

---

### 4.10 Setup Instructions Are Windows-Only on the Platform That Ships 🟢 LOW

**Issue:** Two operator-facing messages hardcode Windows syntax and print unchanged on
Linux. Both were hit while smoke-testing the Linux AOT binary in WSL on 2026-08-31.

1. **`AuthCommand.Init` prints PowerShell.** `Loom.DevTools/Commands/AuthCommand.cs:43-44`
   emits `$env:LOOM_JWT_KEY_FILE = "..."` regardless of platform. On Linux the correct
   form is `export LOOM_JWT_KEY_FILE=...`. Anyone pasting what `loom auth init` tells
   them to paste gets a shell error.
2. **The startup failure message says "Windows dev setup".** The fail-closed path for a
   missing signing key prints `Windows dev setup:  loom auth init` on Linux.

**Why it matters more than it looks:** Phase 15 deploys to Linux under systemd. These
are the two messages an operator sees at first-run and at first-failure, and both
describe a platform they are not on. Neither breaks anything — `auth init` still writes
correct files, and the startup failure is still correctly diagnosed and actionable.

**Fix:** branch on `OperatingSystem.IsWindows()` and emit `export VAR=value` otherwise.
`PersistEnvironmentVariables` already does exactly this check (`AuthCommand.cs:64`), so
the pattern is established in the same file.

**Effort:** 20 minutes, plus a test — the printed text is not currently covered.
**Priority:** 🟢 LOW — cosmetic, but on the first-run path for the shipping platform

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

*Update (2026-08-25, after `1ae0b7f`):* the dashboard no longer captures logs through
`LoomLogger` at all — `EventPipeBridge` reads the target process's
`Microsoft-Extensions-Logging` EventSource instead. Verified against a live
`Host.CreateApplicationBuilder` target, the `MessageJson` payload already carries two
fields this item assumed were unavailable:

- `ArgumentsJson` — the structured properties, e.g.
  `{"N":"1","{OriginalFormat}":"probe info line {N}"}`. Message template and named
  arguments both, with no scope plumbing required.
- `ActivityTraceId` / `ActivitySpanId` — empty under a plain console host, populated
  under ASP.NET Core. A distributed-trace correlation key that arrives for free.

`IngestLogMessage` currently reads neither. The schema question is unchanged —
`LogRecord` still needs a structured-properties field, and that remains a schema
change rather than a bugfix. What changed is the *source*: this no longer waits on
`BeginScope`, so the deferral now blocks only on `LogRecord`'s shape.

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

**None open.** § 3.3 was closed by decision rather than by code — Loom does not terminate
TLS. See its entry and the 2026-08-28 decision-log note.

1. 🔴 ~~`Loom.Web.Api` cannot serve HTTPS (§ 3.3)~~ ✅ RESOLVED BY DECISION
2. 🔴 ~~Test isolation fix (§ 1.1)~~ ✅ COMPLETED
3. 🔴 ~~Missing ingest endpoint (§ 3.1)~~ ✅ COMPLETED

**Total High Priority Work:** ~1 hour of code; the real cost is deciding whether Loom
terminates TLS itself or hands that to a reverse proxy.

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

10. 🟢 Log row `role="button"` ARIA nesting (§ 4.5) - 15-30 min
11. 🟢 `parseArguments` runs twice per change-detection cycle (§ 4.6) - 30 min
12. 🟢 ~~Security headers absent on production redirects (§ 4.7)~~ ✅ COMPLETED in Phase 14C
13. 🟢 `loom dashboard` reports every launch failure as "package not found" (§ 4.8) - 30 min
14. 🟢 ~~`/api/health` now requires a token (§ 4.9)~~ ✅ COMPLETED — marked anonymous

**Total Low Priority Work:** ~18-29 hours (§ 6.4 excluded - blocked on dependencies)

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
| DEBT-020 | Log row role="button" ARIA nesting | 🟢 LOW | 15-30m | Open | - | § 4.5 |
| DEBT-021 | parseArguments double-parse per CD cycle | 🟢 LOW | 30m | Open | - | § 4.6 |
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

### 2026-08-27: File Two Frontend Log-View Lows Rather Than Fix Them

Both surfaced while reviewing `c38d2a5` (expandable log rows). Neither is reachable as a
user-visible defect: § 4.5 is an ARIA semantics violation whose only behavioural symptom —
row activation firing twice on the keyboard path — was already fixed in `da99d99`, and
§ 4.6 is two small `JSON.parse` calls per change-detection cycle while a single row is
expanded.

Filed rather than fixed because the log view had taken nine consecutive commits and further
polish there was worth less than moving on. Both are cheap enough to fold into whatever
next touches that component, and neither blocks anything.

---

### 2026-08-28: Binary Size Resolved by Builder Choice, Not Trimming

**Decision:** Switch `Loom.Web.Api` to `WebApplication.CreateSlimBuilder`; keep the
17 MB limit as stated rather than re-baselining it a second time.

**Rationale:**
- The gate had not been run for several commits because `'vswhere.exe' is not
  recognized` was recorded as a missing toolchain. It is a PATH problem. Once run,
  the binary measured 18.32 MB — over the hard limit by 1.32 MB.
- An ILC map-file breakdown attributed the excess to framework subsystems Loom never
  calls: `System.Text.RegularExpressions` 223 KB (route-constraint map),
  `Microsoft.AspNetCore.Server.IIS` 114 KB, `System.Net.Quic` + Kestrel QUIC transport
  140 KB, and 317 KB of `System.Security.Cryptography`.
- `CreateSlimBuilder` removed 8,812 methods and 4,990 types. Six per-method and
  per-type tables in the binary scale off those counts, which is why a one-line change
  moved 3.58 MB rather than the ~1 MB the named assemblies account for.
- Loom's own code is 286 KB of the binary. No amount of optimizing Loom code could have
  closed a 1.32 MB gap; only the root set mattered.

**Action:** `d277a58`. Docs reconciled in the follow-up commit. § 2.1 marked superseded.

**Unresolved:** the 317 KB `System.Security.Cryptography` reduction has not been traced
to a specific root. TLS still functions in the slim build (verified by running it).

---

## 10. References

- **Phase 12 Testing Results:** `TESTING.md` § 11
- **AOT Compliance Guidelines:** `CLAUDE.md` § Critical Architectural Constraints
- **Binary Size Specifications:** `CLAUDE.md` § Project Overview

---

### 2026-08-28: Phase 14 Specified in Full Before Any Auth Code Is Written

**Decision:** Resolve the three open Phase 14 scope questions, rewrite the phase in
`IMPLEMENTATION-METHODOLOGY.md` end-to-end, and write no authentication code until that
document is settled.

**Scope answers (user, 2026-08-28):** interactive login at `POST /api/token`; every
endpoint protected with no discretionary exceptions; WebSocket tokens carried in
`Sec-WebSocket-Protocol`.

**Rationale:**

- The methodology's Step 14.1 sample was not merely abbreviated, it was **wrong**. Its
  `TryBase64UrlDecode` called `Convert.TryFromBase64Chars`, which rejects base64url.
  Measured on .NET 10.0.11: that call returns `False` on `"----Pn8"` where
  `System.Buffers.Text.Base64Url.TryDecodeFromChars` returns `True` with a correct
  round-trip. Implementing the phase as written would have produced a validator that
  rejects nearly every real token — and would have looked like a key-mismatch bug.
  The stub also had no `alg` check, so `alg: none` forgery was unguarded.
- **Phase 14 was scoped to the wrong host set.** It named `Loom.Web.Api` only.
  `Loom.Dashboard` is a second HTTP host with roughly 25 endpoints, including
  `/api/logs/*`, `/api/logs/explain`, `/ws/logs`, and `/prometheus` — the most sensitive
  surface in the product — and it is the host operators actually run. Shipping auth on
  one host would have produced a complete bypass while reading as "Phase 14 complete."
- **No client sends a token today.** A grep across every `.cs` and `.ts` outside
  `obj`/`bin`/`node_modules` for `Authorization|Bearer|jwt|LOOM_TOKEN` returns zero hits.
  Enforcement breaks the Angular app, `Loom.Dashboard`, and `Loom.DevTools`
  simultaneously, so client work is part of the phase, not follow-up.
- Writing the spec first converted four decisions that would otherwise have been invented
  mid-implementation — credential storage, KDF choice, token lifetime, WebSocket carrier —
  into recorded ones.

**Resolved same day — Prometheus scrape authentication (methodology § 14.7.3).** A
**scope-restricted** 90-day service token, `--scope metrics`, enforced as 403 on any
other route. Three reasons the scope is the load-bearing part: this is the
weakest-protected credential in the system (static file, service account, config
management, backups); unscoped it would carry full operator authority over
`/api/logs/explain` and the rest; and expiry is self-alarming via `up{job="loom"} == 0`,
which is what makes a 90-day TTL acceptable when the only other revocation lever is
rotating `jwt.key` and logging the operator out. Rejected: a systemd timer refreshing a
short-lived token, which depends on unverified Prometheus `credentials_file` re-read
behaviour and trades a static credential for a timer that fails silently. This widens
decision 1 to "interactive login **plus** operator-minted scoped service tokens."

**`Loom.DevTools` audited and found outside the authentication boundary.** It has no
network surface — a scan of every `.cs` in the project for
`HttpClient|HttpListener|Socket|WebApplication|Kestrel|TcpListener|WebSocket` returns
zero matches. `loom logs` attaches directly via `DiagnosticsClient(pid)`. An earlier
revision of § 14.7.2 wrongly claimed it was an API client needing a `--token` flag; that
is corrected. Its real boundary is the OS user owning the target process, making
**"never run `loom` elevated"** a security control rather than a style note.

**CLI process execution audited, no finding.** The single `Process.Start` in
`DashboardCommand` passes an `int` PID with `UseShellExecute = false`, so there is no
shell and no injection. The bare image name `"loom-dashboard"` raised a
current-directory binary-planting question, which was **measured rather than assumed**:
with a decoy in the working directory and nothing on `PATH`, the call throws
`Win32Exception: The system cannot find the file specified`. .NET does not search the CWD
for `UseShellExecute = false` (`SafeProcessSearchMode` unset). Not filed. The residual
`PATH`-order risk requires write access to a `PATH` directory, which is already account
compromise, and is not Loom-specific.

**Also filed from that audit:** § 4.8, `loom dashboard` reporting every launch failure as
"package not found" — a diagnosability trap that Phase 14 makes materially more likely by
adding a startup credential requirement.

**Deliberately rejected:** the original Step 14.4 alert-webhook allowlist. The URL is
operator-set via `LOOM_ALERT_WEBHOOK_URL`, not attacker-supplied, so it is not an SSRF
surface. An allowlist there adds configuration and blocks nothing.

**Also filed:** § 4.7, the security headers missing from Production redirects, found while
reading the middleware order.

---

### 2026-08-28: HTTPS Was Never Actually Enabled; Measuring It Before Phase 15 Commits

**Decision:** File the HTTPS gap as § 3.3 HIGH rather than fixing it inside Phase 14, and
measure the cost of the fix now so Phase 15 chooses its TLS termination point with a
number in hand.

**Rationale:**

- The gap surfaced from a Sonnet deviation report that under-called its own finding. It
  read as "could not verify the 307 redirect locally"; the actual cause is that
  `CreateSlimBuilder` cannot bind an `https://` address at all, so **`Loom.Web.Api` has
  never been able to serve HTTPS**. Phase 14's "HSTS + HTTPS enforcement" line was
  nominal, and § 4.7's missing-headers-on-redirect defect described a redirect that never
  fired. Reproduced in isolation before being believed.
- Measuring beat estimating. `UseKestrelHttpsConfiguration()` costs **+0.946 MB**
  (15.124 → 16.070 MB), leaving 0.930 MB of headroom. That is affordable, but only
  because `Loom.Web.Api` is nearly feature-complete — it has no `wwwroot`, and 14D touches
  other projects. Guessing "a few hundred KB" would have been wrong by a factor of three.
- Fixing it inside Phase 14 was rejected. It is a deployment-shape decision (does Loom
  terminate TLS, or does a reverse proxy?) with certificate provisioning, file
  permissions, and renewal attached. That belongs to Phase 15 with the rest of the
  deployment work, not bolted onto an authentication phase.
- The temporary fix was kept long enough to verify § 4.7 end to end and then reverted, so
  the finding is recorded with evidence rather than with an argument.

**Consequence worth stating plainly:** if Phase 15 chooses a reverse proxy instead,
`UseHsts()` and `UseHttpsRedirection()` must be **removed** from `Program.cs`. Left in
place with no HTTPS listener they are inert, and inert security code reads as protection
that exists.

**Also filed:** § 4.9, `/api/health` becoming authenticated under 14C's blanket
enforcement. Correct per the phase's scope decision, but it breaks liveness probes and
needs a deliberate answer before deployment rather than a discovery during it.

---

## 11. Packaging & Distribution

### 11.1 Loom Ships as NuGet Packages, Not Only as a Host 🟡 MEDIUM (PLANNED)

**Current state.** Only `Loom.Dashboard` and `Loom.DevTools` are packable, both via
`PackAsTool`. None of the nine libraries carries a `PackageId`, description, license, or
repository URL. The consumer-facing story today is "run the host" or "run the CLI"; there
is no "reference the library" story despite the library surface already existing.

**Target state.** Three tiers, plus a metapackage:

Package IDs settled 2026-09-02 — see § 11.7. They are `LoomDiagnostics.*`, which differs
from the assembly names and namespaces (`Loom.*`), and that is deliberate.

| Package ID | Contains | Consumer writes |
|---|---|---|
| `LoomDiagnostics.Telemetry` | attribute + generator + recording | `[LoomProfile]`, `AddLoomTelemetry()` |
| `LoomDiagnostics.Dashboard.AspNetCore` | mountable dashboard | `AddLoomDashboard()` / `MapLoomDashboard()` |
| `LoomDiagnostics` (metapackage) | no code; depends on the others | one-line "everything" install |
| `LoomDiagnostics.Dashboard` | CLI tool | command stays `loom-dashboard` |
| `LoomDiagnostics.Cli` | CLI tool | command stays `loom` |

**Work items:**

1. ~~Package metadata (`PackageId`, `Description`, `PackageLicenseExpression`,
   `RepositoryUrl`, README) across the libraries intended for publication.~~
   **DONE 2026-09-02.** `LoomDiagnostics.Dashboard` and `LoomDiagnostics.Cli` gained
   `Description`, `PackageTags`, `PackageLicenseExpression`, `RepositoryUrl` and
   `RepositoryType`; `LoomDiagnostics.Telemetry` already had them. Neither tool gets a
   `PackageReadmeFile`: `Loom.Telemetry` can only have one because `.gitignore` carries a
   specific `!Loom.Telemetry/PACKAGE.md` un-ignore against the blanket `*.md` rule, and
   `.gitignore` is never staged in this repository.

   **The defect this closed.** `dotnet pack Loom.slnx -c Release` emitted **twelve**
   packages, nine of them strays — `Loom.Security`, `Loom.Storage`,
   `Loom.Telemetry.Alerting`, `Loom.Telemetry.Assist`, `Loom.Telemetry.Exporters`,
   `Loom.Telemetry.Generators`, `Loom.Telemetry.Query`, `Loom.Web.Contracts`,
   `Loom.Web.RealTime`. None set `IsPackable`, so MSBuild defaulted them to packable and
   named each after its assembly, **under the `Loom.` prefix § 11.7 rejected as
   unreservable**. `Loom.Telemetry.Generators` was the worst case: § 11.6 ships it inside
   `LoomDiagnostics.Telemetry`, so a standalone package is a second, divergent copy. The
   risk was never the names — it was a folder feed produced by a solution-wide pack
   carrying nine packages nobody intended to publish. All ten (plus
   `examples/SampleMonitoredApp`) now set `IsPackable=false`; the pack emits exactly three
   packages and one symbol package.

   **Deliberately not done: the eight supporting libraries were not given IDs.** Their
   public names are decided with item 3 below, because
   `LoomDiagnostics.Dashboard.AspNetCore` is what determines which of them are real
   published dependencies rather than internal detail — `Loom.Web.Contracts` in
   particular, whose `<FrameworkReference Microsoft.AspNetCore.App>` (`:16`) makes naming
   it a public-surface decision, not a rename. Nothing is claimed until `dotnet nuget
   push`, so this stays free.

   **Verified**, and the assertion that carried the weight: `PackAsTool` packs project
   references as assemblies under `tools/net10.0/any/` with **zero** package dependencies,
   and making those projects unpackable had to not change that — a package depending on
   something that will never be published would be a silent break. Baseline captured
   before the change and re-checked after: `LoomDiagnostics.Cli` 6 assemblies,
   `LoomDiagnostics.Dashboard` 10, both with no dependencies, identical. Plus the § 11.3
   consumer-AOT gate still passing, which is the only check that proves
   `Loom.Telemetry.Generators` is still packed *inside* the Telemetry package.

   **Known gap, not fixed:** all three packages carry `<authors>` defaulted to the
   assembly name (`Loom.DevTools`, `Loom.Dashboard`, `Loom.Telemetry`) because none sets
   `<Authors>`. nuget.org renders that as the package author. Fix before the first public
   push.
2. ~~Ship `Loom.Telemetry.Generators` **inside** `Loom.Telemetry.nupkg` at
   `analyzers/dotnet/cs/`.~~ **DONE 2026-09-02 — see § 11.6.** A single
   `PackageReference` now delivers both the attributes and the generator; no consumer
   types the hand-written `OutputItemType="Analyzer"` line. The generator's
   `netstandard2.0` target — visible only as the ignorable `NETSDK1212` warning — was the
   prerequisite that made this work.
3. Extract `Loom.Web.Api`'s wiring behind `AddLoomDashboard()` / `MapLoomDashboard()` and
   reduce `Program.cs` to a thin host over them. **This is a refactor, not a repack:**
   `Program.cs` currently owns `CreateSlimBuilder`, the Kestrel loopback bind, the
   security bootstrap, and a `return 1` on misconfiguration. A library cannot terminate
   its host's process.
4. ~~Add a consumer-AOT CI gate (see § 11.3).~~ **DONE 2026-09-02 — see § 11.3.** The
   `consumer-aot-gate` job packs `Loom.Telemetry`, restores it into a throwaway consumer
   from a folder feed, AOT-publishes, and runs the result.

### 11.2 A Single All-In-One Package Is Rejected 🟢 LOW (DECIDED)

Considered and rejected in favour of § 11.1's split. NuGet dependencies are transitive
and non-optional, so one package forces all of Loom into every consumer. Three concrete
costs, each verified:

- **ASP.NET Core enters non-web apps.** `Loom.Web.Contracts.csproj:16` declares
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, and Storage, Query,
  Alerting, and Exporters all depend on Web.Contracts. A console worker referencing Loom
  to time one method would acquire a web framework it never calls.
- **`IL2104` becomes the consumer's problem.** `Microsoft.Diagnostics.Tracing.TraceEvent`
  (`Loom.Dashboard.csproj:21`, `Loom.DevTools.csproj:12`) emits `IL2104` when trimmed.
  Confined to the tools today; a merged package puts it in every consumer's build log.
- **`Loom.Telemetry.Assist` is an adoption blocker in a default install.**
  `AnthropicExplainClient.cs:31` sends `x-api-key` to an external LLM endpoint. It is
  opt-in and inert without `LOOM_LLM_API_KEY`, but dependency review assesses capability,
  not configuration. An outbound AI client in the transitive graph of a telemetry library
  is a plausible rejection at any organisation with a security gate. **Assist must not be
  in the default install path**, including via the `Loom` metapackage.

A fourth cost is versioning: one package means one version line, so a dashboard-only fix
forces a release on consumers who use only the attribute.

**Invariant this establishes:** `Loom.Telemetry` must retain **zero** project references
to other Loom assemblies. It currently references only
`Microsoft.Extensions.DependencyInjection.Abstractions`. This is what makes the tier
split viable and what quarantines both TraceEvent and Assist away from the package most
consumers will install. Treat a new reference here as a breaking change.

### 11.3 Consumer-AOT Gate 🟢 LOW (COMPLETED 2026-09-02)

The 17 MB gate measures Loom's own standalone binary. Under a package-first distribution
that binary is not what consumers download, so the gate protects an artefact of declining
relevance. It should remain, but it is no longer the most valuable check.

The check that matters instead: **a consumer's AOT publish must not break because Loom is
referenced.** `Loom.Telemetry` already sets `IsAotCompatible` and `EnableTrimAnalyzer`,
but nothing proves the packaged form is clean. Gate: a minimal consumer app referencing
the **packed `.nupkg`** (not a `ProjectReference`) publishes with `PublishAot` and emits
zero `IL2026`/`IL3050`. Referencing the project instead of the package would not exercise
the `analyzers/dotnet/cs/` path from § 11.1 item 2 and could pass while the real package
is broken.

**Automated 2026-09-02.** `ci/consumer-aot-gate.ps1`, run by the `consumer-aot-gate` job
in `.github/workflows/ci.yml`. The throwaway consumer lives at `ci/consumer-aot-gate/`
and is deliberately **not** in `Loom.slnx` — it must only ever build against a packed
`.nupkg`. The script packs `Loom.Telemetry`, restores it from `artifacts/loom-feed` via a
`nuget.config` whose `<clear />` stops any parent or machine feed from satisfying the
reference, AOT-publishes, and then asserts, in order:

1. `analyzers/dotnet/cs/Loom.Telemetry.Generators.dll` and `lib/net10.0/Loom.Telemetry.dll`
   are both inside the `.nupkg`. A package missing the analyzer path installs cleanly,
   compiles, and emits no wrappers — silent no-op telemetry.
2. Zero `IL2026`/`IL3050` in the publish log. Asserted by grep as well as by
   `TreatWarningsAsErrors`, so the check does not depend on a csproj property staying set.
3. No managed `.dll` beside the native binary — a publish can fall back to a managed build
   and look completely clean.
4. The binary runs and prints its expected line.

Two things that were measured rather than assumed:

- **Each run packs a unique `1.0.0-gate<timestamp>` version.** NuGet caches by id+version
  in the global packages folder, so a rebuilt `1.0.0` with different content is *not*
  re-extracted — the second run onward would silently test the first run's bits.
- **Negative control passed.** Injecting `OutputItemType="Analyzer"` onto
  `Loom.Telemetry`'s reference to its own generator fails this gate with `CS0436` on both
  attributes, while `dotnet build Loom.slnx` stays green. That is the failure mode nothing
  else in the repository can see, and it is now caught.

One script for both places: CI runs it under `pwsh` on `ubuntu-latest`, and it runs
unchanged on Windows PowerShell 5.1 locally (`./ci/consumer-aot-gate.ps1`). A separate
bash copy for CI would be a second implementation of the same gate, free to drift from the
one developers actually run. Verified passing on **win-x64** (2,106,880 B) and on
**linux-x64** in WSL (2,218,808 B).

### 11.4 `Loom.Web.Api` Retired 🟢 LOW (COMPLETED)

`Loom.Web.Api` is deleted. It was a strict feature subset of `Loom.Dashboard` — every
endpoint family it served, the Dashboard also serves — and its only unique roles were
carrying the repo's sole `PublishAot` project and its sole security-header middleware.
Both were ported before deletion:

- **AOT proof** moved to `Loom.AotProbe`, a minimal console app referencing only
  `Loom.Telemetry` and its source generator. It proves referencing `Loom.Telemetry`
  does not break a consumer's Native AOT publish. Its binary size is **not** a product
  metric and is not gated — the 17 MB figures in § 2.1 measured `Loom.Web.Api` as a
  shipping host, which no longer exists. CI's `aot-publish-linux` job now publishes and
  asserts nativeness against `Loom.AotProbe`; the binary-size step was removed rather
  than repointed. § 11.3's consumer-AOT gate (against a packed `.nupkg`) remains open —
  `Loom.AotProbe` uses a `ProjectReference`, not the packaged form, so it does not
  supersede that item.
- **Security headers** (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`)
  moved to the front of `Loom.Dashboard`'s pipeline, before authentication and
  `UseStaticFiles`, matching the placement fix in § 4.7. `Loom.Web.Api`'s CSP was **not**
  carried over — `default-src 'none'` suits a JSON-only endpoint and would have rendered
  the Angular SPA blank. A policy written for the SPA replaced it; see § 11.5.
- **CSP authored 2026-09-02** — see § 11.5. Closed.
- **`MetricsService` moved to `Loom.Storage`**, not `Loom.Telemetry`: it needs
  `Loom.Web.Contracts.Dtos`, and `Loom.Web.Contracts.csproj:16` carries a
  `FrameworkReference` on `Microsoft.AspNetCore.App`, which would drag ASP.NET Core into
  the package § 11.1 requires to stay reference-free. `Loom.Storage` already depends on
  both and is `IsAotCompatible`, so the move costs zero new dependencies. Its 4 tests moved
  with it; the suite is still 592.

  **It is registered by a separate `AddLoomSelfMetrics()`, not by `AddLoomStorage()`**
  (fixed 2026-09-02; the first pass had bundled it). `MetricsService` measures *the calling
  process*, and `Loom.Dashboard` calls `AddLoomStorage()` while observing a **different**
  process — the target PID. Bundling them meant any future Dashboard endpoint injecting
  `IMetricsService` would silently report the dashboard's own CPU and memory as the
  monitored process's: wrong data that reads as plausible data, which is worse than an
  error. Nothing injects it today, so this closed a latent trap rather than a live bug. The
  interface carries the same warning in its XML doc. **Nothing in the repo currently calls
  `AddLoomSelfMetrics()`** — with `Loom.Web.Api` gone the type has no production consumer,
  only tests. It is kept because it is the natural surface for a future in-process
  self-monitoring package; delete it if that never materializes.
- **CORS** was not ported. `Loom.Web.Api` read `LOOM_CORS_ORIGINS` and conditionally
  enabled it; `Loom.Dashboard` serves its UI from its own origin, so same-origin is
  correct and a CORS policy would only widen the surface. `LOOM_CORS_ORIGINS` is no
  longer read anywhere; it is still mentioned in `IMPLEMENTATION-METHODOLOGY.md` and in
  `Loom.Web.Api`'s own (deleted) `Program.cs` history.

---

### 11.5 `Loom.Dashboard` Content-Security-Policy 🟢 LOW (COMPLETED 2026-09-02)

Shipped in `Loom.Dashboard/Program.cs`, in the same front-of-pipeline middleware as the
other three headers:

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';
img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none';
base-uri 'self'; form-action 'none'; frame-ancestors 'none'
```

Written against what the production bundle actually emits, not against a template. Two
findings drove it:

- **`script-src 'self'` required a build change.** Angular's default production build runs
  Beasties critical-CSS inlining, which emits both an inline `<style>` block *and* an
  `onload="this.media='all'"` attribute on the stylesheet link. That attribute is an inline
  event handler, so the policy would have needed `script-src 'unsafe-inline'` — which
  defeats the point of having a CSP. `angular.json`'s production configuration now sets
  `optimization.styles.inlineCritical: false`. Verified in
  `dist/Loom.Web.Frontend/browser/index.html`: no inline script, style, or handler remains.
  **Re-enabling `inlineCritical` silently breaks this policy.**
- **`style-src` keeps `'unsafe-inline'`.** Angular injects component styles as `<style>`
  elements at runtime. Removing it requires a per-request nonce (`ngCspNonce`), which means
  generating `index.html` per request instead of serving it as a static embedded resource.
  Not worth it on a loopback host; revisit if a reverse proxy is ever added.

No external origins appear anywhere: `src/**/*.scss` contains no `url()`, no `@import`, and
no web-font reference, so every fetch is same-origin.

**Verified 2026-09-02** against a Release `Loom.Dashboard` on port 5231: all four headers
present on `/api/health` (200, anonymous), and the SPA loads and renders fully styled at
`/` — Angular boots and routes to `/login` — with **zero CSP violations** in the browser
console.

**Not verified:** `connect-src 'self'` covering `ws://127.0.0.1:5231/ws`. CSP3 specifies
that `'self'` matches same-origin WebSocket schemes, but confirming it needs an
authenticated session, which the verification pass did not open. If the live charts ever
fail to stream while REST calls succeed, this is the first thing to check — the fix is to
append `ws://localhost:* wss://localhost:*` to `connect-src`.

### 11.6 `Loom.Telemetry` Packs the Generator 🟢 LOW (COMPLETED 2026-09-02)

`Loom.Telemetry` is now packable and carries `Loom.Telemetry.Generators.dll` at
`analyzers/dotnet/cs/`. A consumer writes one `PackageReference` and gets the attributes
and the generator together — the hand-written
`OutputItemType="Analyzer" ReferenceOutputAssembly="false"` line is no longer part of the
consumer story. Package contents, verified by reading the `.nupkg`:

```
lib/net10.0/Loom.Telemetry.dll
analyzers/dotnet/cs/Loom.Telemetry.Generators.dll
README.md                       (packed from Loom.Telemetry/PACKAGE.md)
```

The only declared dependency is `Microsoft.Extensions.DependencyInjection.Abstractions`.
`Loom.Telemetry.Generators` does **not** appear as one: it is an analyzer, and a consumer
must never resolve it as a runtime dependency.

**The generator is attached to `Loom.Telemetry` for build ordering only — deliberately
without `OutputItemType="Analyzer"`.** The first packed build did set it, and the resulting
package was broken in a way that only a real consumer reveals: running the generator over
`Loom.Telemetry`'s own compilation baked `LoomProfileAttribute` and `LoomTrackAttribute`
into `Loom.Telemetry.dll`, the generator then re-emitted them in the consumer, and the
consumer failed with **`CS0436`** — the emitted type conflicting with the imported one. The
attributes must exist *only* as generator post-initialization output. Evidence the fix
holds: `Loom.Telemetry.dll` shrank 44,032 → 43,520 bytes when the analyzer wiring was
removed.

**§ 11.3's gate was run manually and passes.** A throwaway console app in a scratch
directory, one `PackageReference` to the packed `Loom.Telemetry` 1.0.0 resolved from a
**local folder feed** (a directory of `.nupkg` files named in a `nuget.config` — no feed
server, no credentials), `PublishAot` + `PublishTrimmed` + `TrimMode=link` +
`TreatWarningsAsErrors`:

- publishes clean, **zero `IL2026`/`IL3050`**, native `PackageConsumer.exe` with no managed
  assembly beside it, and running it prints its expected line;
- building with `/p:EmitCompilerGeneratedFiles=true --no-incremental` emits
  `LoomAttributes.g.cs`, `OrderService_LoomProfile_g_cs.cs`, and
  `OrderService_LoomTrack_g_cs.cs` — **proof by artefact that the packaged generator ran**,
  not an inference from a clean build.

Note that compiling at all is itself strong evidence: `[LoomProfile]` exists *only* as
generator output, so a package that failed to deliver the generator could not produce a
consumer that compiles.

**Two consumer-facing facts the gate surfaced, both now correct in `PACKAGE.md`:**

- **An instrumented class must be `partial`.** The generator emits its helpers into a
  second declaration of the class, so a plain `sealed class` fails with `CS0260`. The
  README's first draft showed exactly that and would have shipped a non-compiling example.
- **`AddLoomTelemetry` requires a `configure` callback** and there is no parameterless
  overload, while `LoomTelemetryOptions` currently has no settings — so the only correct
  call is `AddLoomTelemetry(options => { })`. This is poor ergonomics for the package's
  headline API and § 11.1's own target state writes it as `AddLoomTelemetry()`. **Adding
  the overload is a public API change and is left open** rather than made in passing;
  `configure` is also dereferenced without a null check.

**Package ID settled 2026-09-02: `LoomDiagnostics.Telemetry`** — see § 11.7.

`examples/SampleMonitoredApp`, `Loom.AotProbe`, and `Loom.Telemetry.Tests` keep their
hand-wired analyzer `ProjectReference`. That is correct and not an oversight — they consume
Loom by project reference, and the `analyzers/dotnet/cs/` path exists only for package
consumers.

### 11.7 Public Package IDs Are `LoomDiagnostics.*` 🟢 LOW (DECIDED 2026-09-02)

**Decision:** publish under the `LoomDiagnostics.` prefix. Assembly names, namespaces, the
`[LoomProfile]` attribute, and the product name all stay `Loom.*` — **package ID and
namespace do not have to match, and here they deliberately do not.**

| Package ID | Project | Command |
|---|---|---|
| `LoomDiagnostics.Telemetry` | `Loom.Telemetry` | — |
| `LoomDiagnostics.Dashboard` | `Loom.Dashboard` | `loom-dashboard` |
| `LoomDiagnostics.Cli` | `Loom.DevTools` | `loom` |
| `LoomDiagnostics` | metapackage, not yet built | — |
| `LoomDiagnostics.Dashboard.AspNetCore` | mountable library, § 11.1 item 3 | — |

**Why not `Loom.*`.** Verified on nuget.org 2026-09-02 by registration-index probe and
search API:

- **`Loom` is taken** — owner `qlaq2435`, v0.0.2, 1,427 downloads, an unrelated binary
  serialization library. The metapackage could never have been called `Loom`.
- **`Loom.Telemetry` itself was free**, so this was a choice, not a forced move.
- **The `Loom.` prefix is not reservable.** Four owners are active under it, and `gyuwon`
  alone publishes 12+ actively-versioned packages there — `Loom.Messaging.*`,
  `Loom.EventSourcing.*`, `Loom.Json*`, `Loom.DataAnnotations` — totalling roughly 350k
  downloads. NuGet will not reserve a prefix an established publisher already occupies. No
  reservation means no verified checkmark, permanently.
- The concrete harm is misattribution in both directions: a reader who finds
  `Loom.Telemetry` beside `Loom.Messaging` and `Loom.EventSourcing` reasonably concludes
  they are one project. **Package IDs cannot be renamed after publication and versions
  cannot be deleted or re-pushed**, so that association would have been permanent.

**Rejected alternatives**, all checked the same day: `Weft.` (occupied by
`StrangeDaysTech`), `Shuttle.` (occupied by `EbenRoux`, 40+ packages, several with 250k+
downloads), `Warp.` (prefix clean but collides with a well-known terminal product and
abandons the Loom identity), `Loomweave.`/`Loomlet.` (clean but weak — one is redundant,
the other reads as a toy). `LoomDiagnostics` and its whole prefix are clean and reservable.

**Cost of the rename, measured rather than estimated.** The change was applied, packed,
consumed, AOT-published, run, and reverted before being adopted. It is **one
`<PackageId>` line per packable project** — no project set one previously, all three
defaulted to their assembly name. The packed `.nupkg` changes its file name only: the
assembly inside stays `lib/net10.0/Loom.Telemetry.dll` and the analyzer stays
`analyzers/dotnet/cs/Loom.Telemetry.Generators.dll`. A consumer changes its
`PackageReference` line and nothing else — `using Loom.Telemetry;` and `[LoomProfile]` are
untouched, and the AOT publish stayed clean with zero `IL2026`/`IL3050`.

**`ToolCommandName` is independent of `PackageId`.** Users still type `loom` and
`loom-dashboard`; only the install command changes.

**Prefix reservation is not automatic** — it must be requested from NuGet after first
publish. Until it is granted there is no checkmark; the point of this decision is that the
request can succeed at all.

---

### 2026-09-01: Loom Distributes as NuGet Packages; Split Over Monolith; Private Before Public

**Decision:** Reorient Loom's primary distribution from "a host you run" to "a package you
reference." Ship the tier split in § 11.1 rather than a single all-in-one package. Publish
to a private feed first and to nuget.org only after the HIM integration has exercised the
install path.

**Rationale:**

- The in-process surface already exists and is demonstrated end to end:
  `examples/SampleMonitoredApp` is an ordinary `Host.CreateApplicationBuilder` app that
  gets instrumented purely by `[LoomProfile]` and the source generator, with no Loom host
  in the process. The gap is packaging, not capability.
- A single package's cost is blast radius and trust, not megabytes — see § 11.2 for the
  three verified costs. The metapackage recovers the one-line install without forcing the
  transitive graph on consumers who do not want it.
- Public publication is irreversible in two ways that matter: a pushed version can be
  unlisted but never deleted or re-pushed, and the package ID is claimed permanently. The
  API surface of v1 therefore becomes a compatibility promise at the moment of first push.
- HIM is Loom's first real consumer and the correct place to discover rough edges in
  naming and install ergonomics, while those are still free to change. A private feed —
  initially a local directory of `.nupkg` files, requiring no feed server or credentials —
  reproduces the consumer install mechanic exactly.

**Consequences:**

- Supersedes the recommendation in `handoff.md` § 5 that HIM integration shape **(b)**
  (out-of-process EventPipe attach) precede **(a)** (library inside `him-ai`). Under a
  package-first Loom, **(a) is the rehearsal**: HIM stops being an integration target and
  becomes the first consumer of the package. (b) remains available and unaffected.
- Reduces the urgency of Phase 15 Step 15.2 (systemd units, `loomd` user, secrets
  provisioning). That work provisions the standalone host, whose role shrinks under
  package-first distribution. It stays deferred behind HIM; the HIM rehearsal should now
  also inform whether 15.2 is still needed in its planned form.
- Adds § 11.3 as a release gate alongside the 17 MB check.

**Open, needs the user:**

1. Whether the standalone `Loom.Web.Api` host remains a shipping artefact or becomes
   dev-only scaffolding once the dashboard is mountable. Decides whether the 17 MB gate
   stays a release gate or demotes to an internal smoke check.
2. Package ID naming pass before any public push, since IDs are permanent.

**Action:** None yet — no code or project files changed. Recorded ahead of implementation.

---

**Document Owner:** Project Loom v2 Team  
**Last Review:** 2026-08-24  
**Next Review:** Phase 13 completion or pre-1.0 release
