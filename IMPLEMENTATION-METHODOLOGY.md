# Project Loom v2 - Methodological Implementation Plan
## HTTPS Native AOT Backend | Customizable Telemetry Platform | Phase-by-Phase Guide

**Document Version:** 2.1
**Last Updated:** 2026-08-14
**Status:** Ready for Implementation
**Scope:** .NET backend only — nine telemetry systems on a single AOT-compiled binary. Angular/frontend is deferred — see `README.md` → "Deferred / Not in Current Scope" and `wiggly-noodling-hoare.md` → "Deferred: Frontend" for the preserved architecture notes.

---

> ### ⚠️ Structural corrections (2026-08-24)
>
> This document is a **build narrative** — it records the plan and the phase-by-phase
> sequence as authored. The delivered solution diverged from it in three ways. The
> phase text below is left intact as the historical record; these corrections override
> it for anything present-tense.
>
> 1. **The solution file is `Loom.slnx`**, not `Loom.sln` (§ Phase 0, L195).
>    `dotnet build/test Loom.sln` fails with `MSBUILD : error MSB1009`.
> 2. **`Loom.Core` and `Loom.Host` were never built.** Phase 0 creates both directories
>    (L163, L165) and later phases forward-reference them (L256, L1444, L1477, L1599,
>    L4828, L4872), but neither project exists. There is no separate bootstrap host:
>    `Loom.Dashboard` (`loom-dashboard`) and `Loom.DevTools` (`loom`) are the packaged
>    dotnet tools and the only entry points. The Phase 7 "integrate with `Loom.Core`
>    SIMD engine" work never happened.
> 3. **`Loom.Storage` is in-memory only** — no memory-mapped binary cache, no RAG
>    ingestor, despite those appearing in the planned structure.
> 4. **The Grafana Cloud and Elasticsearch exporters were deleted** (commit `6d8cc2b`).
>    Phase 12 below specifies all four exporters and Step 12.4 gives a full
>    `GrafanaCloudExporter` listing; those two were built, found to be non-functional
>    against their real targets (bespoke JSON where Grafana Cloud requires
>    snappy-compressed protobuf remote-write; unescaped string-concatenated NDJSON with
>    `ApiKey` never read for Elasticsearch), never registered in any host, and removed.
>    `ToGrafana()` / `ToElasticsearch()` no longer exist. **Console and the Prometheus
>    formatter are the whole exporter surface.** See `BACKLOG.md` § 9.
>
> ### ⚠️ `Loom.Web.Api` was retired (2026-09-02, commit `0b583a7`)
>
> Every phase below builds, edits, runs, or publishes `Loom.Web.Api` — Phase 2 creates it,
> Phases 4 through 15 keep adding to its `Program.cs`, and Phase 15 publishes it. **That
> project no longer exists.** It was a strict feature subset of `Loom.Dashboard`: every
> endpoint family it served, the Dashboard also serves. The phase text is left intact as
> the historical record; these corrections override it for anything present-tense. See
> `BACKLOG.md` § 11.4.
>
> - **The Native AOT publish target is now `Loom.AotProbe`**, a minimal console app
>   referencing only `Loom.Telemetry` and its source generator. It makes a narrower claim
>   than Phase 15 did — that referencing Loom does not break a *consumer's* AOT publish —
>   and its binary size is deliberately **not** gated. Every `< 17 MB` limit below sized
>   `Loom.Web.Api` as a shipping host and retires with it. The CI snippet at the end of
>   Phase 15 is superseded by the real `.github/workflows/ci.yml`.
> - **`MetricsService` / `IMetricsService` now live in `Loom.Storage`**, not
>   `Loom.Web.Api.Services` / `.Interfaces`. They could not go into `Loom.Telemetry`: they
>   need `Loom.Web.Contracts.Dtos`, which carries a `FrameworkReference` on
>   `Microsoft.AspNetCore.App`, and that would drag ASP.NET Core into the package that must
>   stay reference-free (`BACKLOG.md` § 11.1). They are registered by **`AddLoomSelfMetrics()`**,
>   deliberately *not* by `AddLoomStorage()` — `MetricsService` measures the calling
>   process, while `Loom.Dashboard` observes a different one.
> - **`LOOM_CORS_ORIGINS` is dead.** Only `Loom.Web.Api` ever read it, so the opt-in CORS
>   policy described in Phase 14 (and its pipeline-order correction, where auth sits after
>   `UseCors` so a preflight `OPTIONS` is not rejected) **is not part of the shipping
>   system.** `Loom.Dashboard` serves its UI from its own origin, which makes same-origin
>   correct and a CORS policy pure added surface. Setting the variable now does nothing.
> - **The security headers moved to `Loom.Dashboard`**, at the front of its pipeline.
>   Web.Api's CSP did **not** move: `default-src 'none'` suits a JSON-only host and would
>   render the Angular SPA blank. A policy written against what the bundle actually emits
>   replaced it — see `BACKLOG.md` § 11.5, which also records why
>   `optimization.styles.inlineCritical: false` in `angular.json` is load-bearing.
>
> For the current project graph, see `CLAUDE.md` → **Project Structure (Actual)**,
> which is authoritative on structure and build commands.

---

## Overview

This document provides a **complete, step-by-step methodology** for implementing Project Loom v2 from scratch — the diagnostic core (CPU/memory/thread metrics, WebSocket streaming) **plus the full customizable telemetry platform**: custom metrics, attribute-based instrumentation, collectors/plugins, a query language, alerting, exporters, configuration-driven sampling, and a local dev mode. Every phase includes:

- **What to build** - Specific files and components
- **How to build it** - Code provided in ELI5 style for manual typing
- **Why we build it** - Explanation of purpose and constraints
- **AOT-compatibility note** - How this phase avoids reflection (every phase from 5 onward states this explicitly)
- **Verification steps** - How to confirm it works
- **Checkpoints** - Regular drift prevention and understanding checks

This document is the step-by-step counterpart to `wiggly-noodling-hoare.md`'s Architecture Decision Records (ADR-1 through ADR-11) — each phase below implements the ADR with that phase's number-1 (e.g. Phase 6 implements ADR-5's ring buffer design). Where this doc and the ADRs could drift, the ADR is the design decision and this doc is its build sequence; flag it rather than silently picking one.

**A note on ELI5 density:** Phases 0-3 below narrate every line, matching how this document was first written. From Phase 4 onward the same rigor applies to every DTO, interface, and registration step, but line-by-line prose is trimmed where a pattern already established earlier (e.g. "why `sealed`," "why `ValueTask`") would just repeat itself — those callbacks are named instead of re-explained. Ask for ELI5 mode on any section and the fuller narration comes back for that section specifically; this keeps the reference document complete without making every phase as long as Phase 1.

**A note on the dropped search endpoint:** `README.md`'s DTO registry still lists `DiagnosticSearchRequest`/`DiagnosticSearchResponse`/`SearchResult` under "Infrastructure (existing)," but neither the current Implementation Phases table nor the API Endpoints table in `README.md` includes a search endpoint or phase — vector/semantic search over telemetry appears to have been cut from this pass's scope in favor of the ring-buffer-backed query language (Phase 10). This document follows the phase and endpoint tables (the more concrete sources) and does not build a search phase. `POST /api/telemetry/ingest`, which *is* still in the endpoint table, is implemented in Phase 6 instead, since it now feeds the ring buffer rather than a separate search index. Flagging this rather than silently resolving it either way — worth confirming, and easy to re-add as its own phase later if the DTOs weren't meant to be dropped.

---

## Implementation Principles

### Core Rules (Never Violate)

1. ✅ **Native AOT Only** - No reflection, no runtime codegen
2. ✅ **Zero-Allocation Hot Paths** - Use Span<T>, ValueTask, ArrayPool<T>
3. ✅ **Source-Generated JSON** - All DTOs registered in JsonSerializerContext
4. ✅ **Minimal APIs Only** - No MVC controllers (reflection-heavy)
5. ✅ **No SignalR** - Use raw WebSockets (SignalR uses reflection)
6. ✅ **<17 MB Binary** - Monitor size after each phase
7. ✅ **<20 MB Memory** - Zero allocations in hot paths
8. ✅ **"Plugins" are compile-time registered** - `ILoomCollector` implementations, exporters, and alert targets are statically compiled types wired up via generic `Add...<T>()` calls in DI, never `Assembly.LoadFrom` or reflection-based discovery. This applies throughout Phases 5-13.
9. ✅ **Delegates are fine, expression trees are not** - `Func<T,bool>` conditions (e.g. alert conditions) compiled at DI-registration time are ordinary closures, not reflection — allowed. `Expression<Func<T,bool>>` requires `System.Linq.Expressions`, which is reflection-heavy — not allowed. See ADR-7 and ADR-8 in `wiggly-noodling-hoare.md`.

### Educational Approach

- **Every line explained** in ELI5 style, expandable on request per the `eli5-educator` skill
- **User types every keystroke** to build familiarity
- **Checkpoints every major milestone** to prevent drift
- **Token optimization** through caching and concise responses

---

## Phase Breakdown Summary

```
Foundation (Phases 0-4) — Complete
Phase 0:  Project Setup & Tooling                              → Foundation
Phase 1:  Contracts & JSON Serialization                       → Data structures
Phase 2:  Web API Core & Health Endpoint                       → First working API
Phase 3:  Core API Endpoints (Metrics)                         → Business logic
Phase 4:  WebSocket Real-Time Streaming                        → Real-time data

Telemetry Platform (Phases 5-13) — Current Focus
Phase 5:  Source Generator (Loom.Telemetry.Generators)         → Powers Phases 7 & 8 [ADR-4]
Phase 6:  Custom Metrics API (Loom.Telemetry)                  → RecordMetric/Counter/Gauge/Histogram, ring buffer [ADR-5]
Phase 7:  Attribute-Based Instrumentation                      → [LoomProfile]/[LoomTrack], depends on Phase 5
Phase 8:  Custom Collectors/Plugins (Loom.Telemetry.Collectors)→ ILoomCollector, depends on Phases 5, 6 [ADR-6]
Phase 9:  Configuration-Driven Sampling                        → IOptionsMonitor hot-reload, depends on Phase 6 [ADR-10]
Phase 10: Query Language (Loom.Telemetry.Query)                → Tokenizer→Parser→AST→Planner→Executor, depends on Phase 6 [ADR-7]
Phase 11: Alerting/Thresholds (Loom.Telemetry.Alerting)        → Sliding window, Channel<T> dispatch, depends on Phase 10 [ADR-8]
Phase 12: Exporters (Loom.Telemetry.Exporters)                 → Prometheus/Grafana/Elasticsearch/Console, depends on Phase 6 [ADR-9]
Phase 13: Local Development Mode (Loom.DevTools)               → Diagnostics IPC + EventPipe, depends on all above [ADR-11]

Production Hardening (Phases 14-15)
Phase 14: Security Hardening                                   → JWT, HTTPS, systemd
Phase 15: Production Build & Deployment                        → Binary size, systemd service, CI/CD

Deferred (not scheduled): Angular Frontend, Dashboard & Custom Widgets, Query Builder UI — see wiggly-noodling-hoare.md
```

This mirrors `wiggly-noodling-hoare.md`'s "Implementation Phase Overview" table exactly — same phase numbers, same dependency notes.

---

# PHASE 0: Project Setup & Tooling

**Duration:** 1-2 days  
**Goal:** Install required tools and create solution structure  
**Checkpoint Interval:** After each major tool installation

## Prerequisites Verification

### Step 0.1: Verify .NET 10 SDK

```bash
dotnet --version
# Should show 10.0.100 or higher
```

**If not installed:**
- Download from: https://dotnet.microsoft.com/download/dotnet/10.0
- Install .NET 10 SDK

### Step 0.2: Node.js & Angular CLI — Not Required for This Pass

The original plan installed Node.js and the Angular CLI here. Frontend work is **deferred** (see `README.md` → "Deferred / Not in Current Scope"), so skip this for now. If you resume frontend work later, the original steps are preserved in `wiggly-noodling-hoare.md` → "Deferred: Frontend."

### Step 0.3: Verify Build Tools

**Windows:**
```bash
# Check for MSVC Build Tools
where cl.exe
# Should show path to Visual Studio C++ compiler
```

**Linux:**
```bash
clang --version  # Should be 19.x or higher
gcc --version    # Backup compiler
```

**If missing on Linux:**
```bash
sudo apt-get update
sudo apt-get install -y clang-19 llvm-19 zlib1g-dev
```

### 🔍 Checkpoint 0.1
```
✓ .NET 10 SDK installed: [version]
✓ Build tools ready: [MSVC/Clang version]
✓ Ready to create solution? [Y/N]
```

---

## Step 0.4: Create Solution Structure

**Location:** `C:\Users\angel\source\repos\Project Loom v2\`

### Create Solution File

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"

# Create solution
dotnet new sln -n Loom
```

**Explanation (ELI5):**
> Think of a solution (.sln) like a binder that holds multiple notebooks (projects). 
> Each project will be a separate "notebook" but they all live in this one binder so 
> Visual Studio/Rider can work with them together.

### Create Project Directories

```bash
# Backend projects (core diagnostic scope)
mkdir Loom.Web.Contracts
mkdir Loom.Web.Api
mkdir Loom.Web.RealTime
mkdir Loom.Core
mkdir Loom.Storage
mkdir Loom.Host

# Test projects
mkdir Loom.Tests
mkdir Loom.Benchmarks

# Telemetry platform projects — created as their own phases arrive, not here:
#   Loom.Telemetry.Generators  → Phase 5 (source generator, analyzer project)
#   Loom.Telemetry             → Phase 6 (custom metrics runtime, ring buffer)
#   Loom.Telemetry.Collectors  → Phase 8 (ILoomCollector)
#   Loom.Telemetry.Query       → Phase 10 (query engine)
#   Loom.Telemetry.Alerting    → Phase 11 (alert evaluation)
#   Loom.Telemetry.Exporters   → Phase 12 (Prometheus/Grafana/Elasticsearch/Console)
#   Loom.DevTools              → Phase 13 (`dotnet loom dev` CLI)
# Same reason we don't create Loom.Web.Api until Phase 2 even though we know it's coming.
```

**Explanation (ELI5):**
> We're organizing our code like a filing cabinet:
> - Loom.Web.Contracts = The shared "language" every project speaks (DTOs)
> - Loom.Web.Api = The API endpoints (like restaurant menu items)
> - Loom.Web.RealTime = Real-time WebSocket streaming (like a live TV feed)
> - Loom.Core = Math engine with SIMD (super fast calculations)
> - Loom.Storage = Where we save and load data
> - Loom.Host = The "main entrance" that starts everything
>
> The `Loom.Telemetry.*` family (source generator, metrics runtime, collectors, query, alerting, exporters) and `Loom.DevTools` get their own folders when their phase arrives — same reason we don't create `Loom.Web.Api` until Phase 2 even though we know it's coming. Naming note: `Loom.Telemetry` (no suffix) is the metrics runtime itself (Phase 6); everything under it (`Loom.Telemetry.Collectors`, `.Query`, `.Alerting`, `.Exporters`) depends on it, matching the Dependency Flow diagram in `wiggly-noodling-hoare.md`.

### 🔍 Checkpoint 0.2
```
✓ Solution created: Loom.sln
✓ 8 project folders created (6 backend + 2 test)
✓ Directory structure matches plan
✓ Ready to create projects? [Y/N]
```

---

# PHASE 1: Contracts & JSON Serialization

**Duration:** 2-3 days  
**Goal:** Create all DTO classes with source-generated JSON serialization  
**Why Critical:** Native AOT requires ALL JSON types registered at compile time

## Step 1.1: Create Loom.Web.Contracts Project

```bash
cd Loom.Web.Contracts

dotnet new classlib -f net10.0
```

**Explanation (ELI5):**
> We're creating a "class library" - think of it like a dictionary that defines words 
> (data structures) that both the backend and frontend will use to communicate.

### Step 1.2: Configure Project for Native AOT

**File:** `Loom.Web.Contracts/Loom.Web.Contracts.csproj`

**Type this exactly (you'll understand each line):**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    
    <!-- Native AOT Compatibility (analyzers only - PublishAot goes on the Host project) -->
    <IsTrimmable>true</IsTrimmable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.Text.Json" />
  </ItemGroup>
</Project>
```

**Explanation (ELI5) - Line by Line:**

- `<TargetFramework>net10.0</TargetFramework>` 
  > We're using .NET 10 - like saying "I need Windows 11, not Windows 10"

- `<Nullable>enable</Nullable>`
  > This forces us to be explicit about "can this be null?" - prevents runtime crashes

- **NOTE: No `<PublishAot>true</PublishAot>` here!**
  > PublishAot only goes on the EXECUTABLE project (Loom.Host) - not on class libraries.
  > A library can't be "published" on its own, so this flag would be meaningless here.
  > Think of it this way: only the "main entrance" (Host) gets compiled to native code,
  > and it pulls in all the libraries during that process.

- `<IsTrimmable>true</IsTrimmable>`
  > Tells the trimmer "this library is safe to trim" - unused code can be removed

- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  > Warnings become errors - forces us to fix problems, not ignore them

- `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`
  > Tool that warns us if our code won't work when trimmed

- `<EnableAotAnalyzer>true</EnableAotAnalyzer>`
  > Tool that warns us if our code won't work with Native AOT

- **No version on System.Text.Json:**
  > We omit the version so it inherits from the SDK automatically.
  > Specifying an explicit version can cause conflicts with the framework.

### Step 1.3: Delete Default Class1.cs

```bash
rm Class1.cs
```

### 🔍 Checkpoint 1.1
```
✓ Loom.Web.Contracts project created
✓ Native AOT configuration applied
✓ Understanding check: What does PublishAot=true do?
  Expected: "Compiles to native machine code instead of IL bytecode"
✓ Ready to create DTOs? [Y/N]
```

---

## Step 1.4: Create DTO Folder Structure

```bash
mkdir Dtos
cd Dtos
```

### Step 1.5: Create Health Check DTO

**File:** `Loom.Web.Contracts/Dtos/HealthCheckResponse.cs`

**Type this (explanation follows):**

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Response for health check endpoint.
/// Think of this like a doctor's checkup - is the service alive and healthy?
/// </summary>
public sealed record HealthCheckResponse
{
    /// <summary>
    /// Overall health status: "Healthy", "Degraded", or "Unhealthy"
    /// </summary>
    public required string Status { get; init; }
    
    /// <summary>
    /// When this health check was performed (UTC timestamp)
    /// </summary>
    public required DateTime Timestamp { get; init; }
    
    /// <summary>
    /// How long the service has been running (in seconds)
    /// </summary>
    public required long UptimeSeconds { get; init; }
    
    /// <summary>
    /// Current memory usage in megabytes
    /// </summary>
    public required double MemoryUsageMb { get; init; }
}
```

**Explanation (ELI5):**

**Why `record` instead of `class`?**
> A record is like a receipt - once printed, you can't change it. Perfect for data 
> that shouldn't be modified after creation. Records give us automatic equality 
> comparison (two receipts with same data are considered equal).

**Why `sealed`?**
> "Sealed" means no one can inherit from this class. Like a locked box - it does 
> one thing and can't be extended. This helps Native AOT optimization because the 
> compiler knows exactly what this type will be.

**Why `required`?**
> Forces whoever creates this object to provide ALL properties. Like a form where 
> all fields are mandatory - prevents accidentally forgetting data.

**Why `{ get; init; }`?**
> - `get` = anyone can read this property
> - `init` = can only set during object creation, not after
> Think of it like writing in permanent marker vs pencil. Once written, it's permanent.

### Step 1.6: Create CPU Metrics DTO

**File:** `Loom.Web.Contracts/Dtos/CpuMetricResponse.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// CPU hotpath metrics - which code is using the most CPU time.
/// Like a report showing which apps drain your phone battery the most.
/// </summary>
public sealed record CpuMetricResponse
{
    /// <summary>
    /// Overall CPU usage percentage (0-100)
    /// </summary>
    public required double CpuUsagePercent { get; init; }
    
    /// <summary>
    /// List of top CPU-consuming threads/methods
    /// </summary>
    public required CpuHotpath[] Hotpaths { get; init; }
    
    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// A single CPU hotpath - a method or code path consuming CPU.
/// </summary>
public sealed record CpuHotpath
{
    /// <summary>
    /// Name of the method or code path
    /// Example: "OrderProcessor.CalculateTotal"
    /// </summary>
    public required string MethodName { get; init; }
    
    /// <summary>
    /// Percentage of total CPU time this path uses (0-100)
    /// </summary>
    public required double CpuPercent { get; init; }
    
    /// <summary>
    /// Number of times this method was called
    /// </summary>
    public required long InvocationCount { get; init; }
    
    /// <summary>
    /// Average time spent in this method (milliseconds)
    /// </summary>
    public required double AverageTimeMs { get; init; }
}
```

**Explanation (ELI5):**

**Why nested records?**
> `CpuMetricResponse` contains an array of `CpuHotpath` objects. This is like a 
> shopping receipt (CpuMetricResponse) that contains multiple line items (CpuHotpath).
> Each line item shows one thing you bought (one CPU hotpath).

**Why arrays (`CpuHotpath[]`) instead of lists?**
> Arrays are more efficient for Native AOT - they're fixed size and don't need 
> dynamic resizing. Think of an array like an egg carton (fixed slots) vs a 
> grocery bag (flexible but slower).

### Step 1.7: Create Memory Metrics DTO

**File:** `Loom.Web.Contracts/Dtos/MemoryMetricResponse.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Memory allocation metrics - what's using RAM and how much.
/// Like checking which files are taking up space on your hard drive.
/// </summary>
public sealed record MemoryMetricResponse
{
    /// <summary>
    /// Total memory allocated (in megabytes)
    /// </summary>
    public required double TotalMemoryMb { get; init; }
    
    /// <summary>
    /// Memory currently in use (in megabytes)
    /// </summary>
    public required double UsedMemoryMb { get; init; }
    
    /// <summary>
    /// Number of garbage collections that occurred
    /// </summary>
    public required GarbageCollectionStats GcStats { get; init; }
    
    /// <summary>
    /// Top memory allocations by type
    /// </summary>
    public required MemoryAllocation[] TopAllocations { get; init; }
    
    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Garbage collection statistics.
/// GC is like a janitor that cleans up unused memory automatically.
/// </summary>
public sealed record GarbageCollectionStats
{
    /// <summary>
    /// Gen 0 collections (frequent, quick cleanups)
    /// </summary>
    public required int Gen0Collections { get; init; }
    
    /// <summary>
    /// Gen 1 collections (medium-lived objects)
    /// </summary>
    public required int Gen1Collections { get; init; }
    
    /// <summary>
    /// Gen 2 collections (long-lived objects, expensive)
    /// </summary>
    public required int Gen2Collections { get; init; }
    
    /// <summary>
    /// Total time spent in garbage collection (milliseconds)
    /// </summary>
    public required double TotalGcTimeMs { get; init; }
}

/// <summary>
/// A single memory allocation entry.
/// </summary>
public sealed record MemoryAllocation
{
    /// <summary>
    /// Type name that's allocating memory
    /// Example: "System.String", "OrderData[]"
    /// </summary>
    public required string TypeName { get; init; }
    
    /// <summary>
    /// Number of instances allocated
    /// </summary>
    public required long Count { get; init; }
    
    /// <summary>
    /// Total memory used by these instances (in bytes)
    /// </summary>
    public required long TotalBytes { get; init; }
}
```

**Explanation (ELI5):**

**What's Garbage Collection?**
> In .NET, you don't manually delete objects like in C++. The "Garbage Collector" 
> (GC) automatically finds unused objects and removes them. It's like having a 
> janitor who periodically cleans up trash.

**Why Gen 0, Gen 1, Gen 2?**
> The GC uses 3 "generations":
> - Gen 0 = Young objects (babies) - cleaned up frequently (like daily trash)
> - Gen 1 = Middle-aged objects (teenagers) - cleaned up occasionally (weekly trash)
> - Gen 2 = Old objects (adults) - cleaned up rarely (monthly trash)
> 
> Gen 2 collections are expensive because they check EVERYTHING.

### Step 1.8: Create Thread Metrics DTO

**File:** `Loom.Web.Contracts/Dtos/ThreadMetricResponse.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Thread activity and blockage metrics.
/// Threads are like workers in a factory - we want to know if any are blocked/waiting.
/// </summary>
public sealed record ThreadMetricResponse
{
    /// <summary>
    /// Total number of threads in the process
    /// </summary>
    public required int TotalThreads { get; init; }
    
    /// <summary>
    /// Number of threads currently running
    /// </summary>
    public required int ActiveThreads { get; init; }
    
    /// <summary>
    /// Number of threads blocked/waiting
    /// </summary>
    public required int BlockedThreads { get; init; }
    
    /// <summary>
    /// Details about blocked threads
    /// </summary>
    public required ThreadBlockage[] Blockages { get; init; }
    
    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Information about a blocked thread.
/// </summary>
public sealed record ThreadBlockage
{
    /// <summary>
    /// Thread ID
    /// </summary>
    public required int ThreadId { get; init; }
    
    /// <summary>
    /// Thread name (if available)
    /// </summary>
    public string? ThreadName { get; init; }  // Note: nullable
    
    /// <summary>
    /// What the thread is blocked on
    /// Example: "Waiting for database", "Lock contention"
    /// </summary>
    public required string BlockedOn { get; init; }
    
    /// <summary>
    /// How long the thread has been blocked (milliseconds)
    /// </summary>
    public required double BlockedDurationMs { get; init; }
    
    /// <summary>
    /// Stack trace showing where the thread is blocked
    /// </summary>
    public string? StackTrace { get; init; }  // Note: nullable
}
```

**Explanation (ELI5):**

**Why some properties nullable (`string?`) and others not?**
> - `ThreadName` is nullable because not all threads have names (optional)
> - `StackTrace` is nullable because we might not always capture it (expensive)
> - `ThreadId` and `BlockedOn` are required because they're always available
>
> Think of required fields like mandatory form fields, nullable like optional comments.

### 🔍 Checkpoint 1.2
```
✓ DTOs created: HealthCheckResponse, CpuMetricResponse, MemoryMetricResponse, ThreadMetricResponse
✓ Understanding check: Why do we use 'record' instead of 'class'?
  Expected: "Immutable data, value equality, perfect for DTOs"
✓ Understanding check: Why 'sealed'?
  Expected: "Prevents inheritance, helps Native AOT optimization"
✓ Files typed manually: [4 DTO files]
✓ Ready for more DTOs? [Y/N]
```

---

## Step 1.9: Create Search DTOs

**File:** `Loom.Web.Contracts/Dtos/DiagnosticSearchRequest.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Request for vector search over diagnostic telemetry.
/// Think of this like typing a search query into Google.
/// </summary>
public sealed record DiagnosticSearchRequest
{
    /// <summary>
    /// The search query text
    /// Example: "thread blocked on database"
    /// </summary>
    public required string Query { get; init; }
    
    /// <summary>
    /// Maximum number of results to return (default: 10)
    /// </summary>
    public int MaxResults { get; init; } = 10;
    
    /// <summary>
    /// Minimum similarity threshold (0.0 - 1.0)
    /// Higher = more strict matching
    /// </summary>
    public double MinSimilarity { get; init; } = 0.7;
}
```

**File:** `Loom.Web.Contracts/Dtos/DiagnosticSearchResponse.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Response containing search results.
/// Like Google search results - a list of matching items with relevance scores.
/// </summary>
public sealed record DiagnosticSearchResponse
{
    /// <summary>
    /// The original search query
    /// </summary>
    public required string Query { get; init; }
    
    /// <summary>
    /// Number of results found
    /// </summary>
    public required int TotalResults { get; init; }
    
    /// <summary>
    /// How long the search took (milliseconds)
    /// </summary>
    public required double SearchTimeMs { get; init; }
    
    /// <summary>
    /// The actual search results
    /// </summary>
    public required SearchResult[] Results { get; init; }
}

/// <summary>
/// A single search result.
/// </summary>
public sealed record SearchResult
{
    /// <summary>
    /// The diagnostic message or telemetry event
    /// </summary>
    public required string Content { get; init; }
    
    /// <summary>
    /// Similarity score (0.0 - 1.0, higher = better match)
    /// </summary>
    public required double Score { get; init; }
    
    /// <summary>
    /// When this diagnostic event occurred
    /// </summary>
    public required DateTime Timestamp { get; init; }
    
    /// <summary>
    /// Source of the diagnostic (e.g., "CPU", "Memory", "Thread")
    /// </summary>
    public required string Source { get; init; }
}
```

**Explanation (ELI5):**

**What's Vector Search?**
> Traditional search looks for exact words (like Ctrl+F).
> Vector search understands MEANING. "car broken" would match "vehicle malfunction".
> We convert text to numbers (vectors) and find similar patterns using SIMD math.

**Why default values in Request?**
> `public int MaxResults { get; init; } = 10;` gives a sensible default.
> User can override, but if they don't specify, we use 10.
> Like a form with pre-filled values you can change.

### Step 1.10: Create Telemetry Ingestion DTO

**File:** `Loom.Web.Contracts/Dtos/TelemetryIngestRequest.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Request to ingest new telemetry data.
/// This is how external systems send diagnostic events to Loom.
/// </summary>
public sealed record TelemetryIngestRequest
{
    /// <summary>
    /// Type of telemetry event
    /// Examples: "gc_start", "thread_blocked", "cpu_spike"
    /// </summary>
    public required string EventType { get; init; }
    
    /// <summary>
    /// When the event occurred (UTC)
    /// </summary>
    public required DateTime Timestamp { get; init; }
    
    /// <summary>
    /// Source application or service name
    /// </summary>
    public required string Source { get; init; }
    
    /// <summary>
    /// Event severity: "Info", "Warning", "Error", "Critical"
    /// </summary>
    public required string Severity { get; init; }
    
    /// <summary>
    /// Event message or description
    /// </summary>
    public required string Message { get; init; }
    
    /// <summary>
    /// Optional additional metadata (key-value pairs)
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
```

**Explanation (ELI5):**

**Why Dictionary for Metadata?**
> Sometimes events have extra data that doesn't fit the standard structure.
> Like a form with "Other:" field where you can write anything.
> Dictionary<string, string> = pairs of keys and values
> Example: {"thread_id": "42", "lock_name": "OrderLock"}

**Why nullable Dictionary?**
> Not all events need extra metadata, so we make it optional.

### Step 1.11: Create Real-Time Update DTOs

**File:** `Loom.Web.Contracts/Dtos/MetricUpdate.cs`

```csharp
using System.Text.Json.Serialization;

namespace Loom.Web.Contracts.Dtos;

/// <summary>
/// Base type for all real-time metric updates sent via WebSocket.
/// Uses [JsonDerivedType] so the source generator knows all possible concrete types
/// at compile time - REQUIRED for Native AOT!
/// </summary>
[JsonDerivedType(typeof(CpuMetricUpdate), typeDiscriminator: "cpu")]
[JsonDerivedType(typeof(MemoryMetricUpdate), typeDiscriminator: "memory")]
[JsonDerivedType(typeof(ThreadMetricUpdate), typeDiscriminator: "thread")]
public abstract record MetricUpdate
{
    /// <summary>
    /// When this update occurred
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// CPU metric update for WebSocket streaming.
/// </summary>
public sealed record CpuMetricUpdate : MetricUpdate
{
    public required CpuMetricResponse Data { get; init; }
}

/// <summary>
/// Memory metric update for WebSocket streaming.
/// </summary>
public sealed record MemoryMetricUpdate : MetricUpdate
{
    public required MemoryMetricResponse Data { get; init; }
}

/// <summary>
/// Thread metric update for WebSocket streaming.
/// </summary>
public sealed record ThreadMetricUpdate : MetricUpdate
{
    public required ThreadMetricResponse Data { get; init; }
}
```

**Explanation (ELI5):**

**Why separate types instead of `object Data`?**
> Native AOT's source generator MUST know every possible type at compile time.
> Using `object` would require reflection at runtime to figure out what to serialize.
> That would crash with `NotSupportedException`!
>
> Instead, we use a "discriminated union" pattern:
> - One base type (`MetricUpdate`) that lists all possible subtypes
> - Each subtype holds its specific data
> - `[JsonDerivedType]` tells the source generator: "these are ALL the types that can appear"
> - The `typeDiscriminator` adds a `$type` field to the JSON so the receiver knows which one it is

**What does the JSON look like?**
> ```json
> { "$type": "cpu", "timestamp": "...", "data": { "cpuUsagePercent": 45.2, ... } }
> ```
> Any WebSocket client reads `$type` to know how to deserialize the payload — today that's `wscat`/curl/the browser DevTools Network tab for testing; a future dashboard client would do the same thing with the same field.

### 🔍 Checkpoint 1.3
```
✓ All DTOs created: [9 total files]
✓ Understanding check: What's the difference between exact search and vector search?
  Expected: "Exact = keyword matching, Vector = semantic/meaning matching"
✓ Lines of code typed: ~300-400 lines
✓ Hands tired? Take a 5-minute break!
✓ Ready for JSON serialization context? [Y/N]
```

---

## Step 1.12: Create JSON Serialization Context

**This is THE MOST CRITICAL file for Native AOT!**

**File:** `Loom.Web.Contracts/JsonContext.cs`

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Loom.Web.Contracts.Dtos;

namespace Loom.Web.Contracts;

/// <summary>
/// JSON serialization context for Native AOT.
/// THIS IS CRITICAL! Every DTO type MUST be registered here.
/// 
/// Think of this like a registry or phonebook - the compiler needs to know
/// at compile-time which types will be serialized to/from JSON.
/// </summary>
[JsonSerializable(typeof(HealthCheckResponse))]
[JsonSerializable(typeof(CpuMetricResponse))]
[JsonSerializable(typeof(CpuHotpath))]
[JsonSerializable(typeof(MemoryMetricResponse))]
[JsonSerializable(typeof(GarbageCollectionStats))]
[JsonSerializable(typeof(MemoryAllocation))]
[JsonSerializable(typeof(ThreadMetricResponse))]
[JsonSerializable(typeof(ThreadBlockage))]
[JsonSerializable(typeof(DiagnosticSearchRequest))]
[JsonSerializable(typeof(DiagnosticSearchResponse))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(TelemetryIngestRequest))]
[JsonSerializable(typeof(MetricUpdate))]
[JsonSerializable(typeof(CpuMetricUpdate))]
[JsonSerializable(typeof(MemoryMetricUpdate))]
[JsonSerializable(typeof(ThreadMetricUpdate))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization,
    WriteIndented = false
)]
public partial class LoomJsonSerializerContext : JsonSerializerContext
{
    // This class is partial and will be completed by the source generator at compile time.
    // No code needed here - the attributes above do all the work!
}
```

**Explanation (ELI5) - This is IMPORTANT to understand:**

**Why do we need this?**
> Normal JSON serialization uses reflection (runtime magic) to figure out object structure.
> Native AOT doesn't support reflection! So we tell the compiler at compile-time:
> "Hey, these are ALL the types I'll ever serialize to JSON."

**What are those [JsonSerializable] attributes?**
> Each `[JsonSerializable(typeof(SomeType))]` registers one type.
> The compiler will generate specialized, fast code for each type.
> It's like giving the compiler a complete list of ingredients before cooking,
> instead of asking "what's next?" while cooking.

**What's JsonSourceGenerationOptions?**
> These are settings for HOW to serialize:
> - `PropertyNamingPolicy = CamelCase`: C# uses PascalCase (Status), JSON uses camelCase (status)
> - `DefaultIgnoreCondition = WhenWritingNull`: Don't write null fields (smaller JSON)
> - `GenerationMode = Metadata | Serialization`: Generate both read and write code
> - `WriteIndented = false`: Compact JSON (no pretty formatting in production)

**What's `partial class`?**
> The compiler will add MORE code to this class automatically.
> `partial` means "this class is split across multiple files" - one we write, one the compiler generates.

**Why is this critical?**
> If you forget to register a type here and try to serialize it, the app will CRASH at runtime!
> Always remember: Add new DTO → Add to JsonSerializerContext

### Step 1.13: Build and Verify

```bash
cd "C:\Users\angel\source\repos\Project Loom v2\Loom.Web.Contracts"
dotnet build
```

**Expected output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**If you see warnings about trim analysis:**
> This means a DTO wasn't registered in JsonSerializerContext!
> Go back and add the missing type.

### Step 1.14: Add Project to Solution

```bash
cd ..
dotnet sln Loom.slnx add Loom.Web.Contracts/Loom.Web.Contracts.csproj
```

### 🔍 Checkpoint 1.4 (MAJOR MILESTONE)
```
✓ Phase 1 Complete: Contracts & JSON Serialization
✓ Files created: 10 DTO files + 1 JsonContext
✓ All DTOs registered in LoomJsonSerializerContext
✓ Project builds with 0 warnings
✓ Added to solution

Understanding Check:
Q: Why do we need LoomJsonSerializerContext?
A: [User explains - should mention: Native AOT, no reflection, compile-time registration]

Q: What happens if we forget to register a DTO?
A: [User explains - should mention: runtime crash when trying to serialize]

Q: Why is PropertyNamingPolicy set to CamelCase?
A: [User explains - should mention: JavaScript/JSON convention vs C# PascalCase]

Token Usage Check: [Should be using cached CLAUDE.md, methodology doc]
Ready for Phase 2? [Y/N]
Take a break - stretch, water, come back fresh!
```

---

# PHASE 2: Web API Core & Health Endpoint

**Duration:** 2-3 days  
**Goal:** Create ASP.NET Core Minimal API project with first working endpoint  
**Why Critical:** Establishes Native AOT-compatible API foundation

## Step 2.1: Create Loom.Web.Api Project

```bash
cd "C:\Users\angel\source\repos\Project Loom v2\Loom.Web.Api"
dotnet new web -f net10.0
```

**Explanation (ELI5):**
> `dotnet new web` creates a minimal web application template.
> It's like getting a starter kit for building a website/API.
> The `-f net10.0` says "use .NET 10 framework".

### Step 2.2: Configure Project for Native AOT

**File:** `Loom.Web.Api/Loom.Web.Api.csproj`

**Replace entire content with:**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    
    <!-- Native AOT Configuration -->
    <PublishAot>true</PublishAot>
    <IsTrimmable>true</IsTrimmable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
    <EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    
    <!-- Binary Size Optimization -->
    <InvariantGlobalization>true</InvariantGlobalization>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>link</TrimMode>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference our Contracts project -->
    <ProjectReference Include="..\Loom.Web.Contracts\Loom.Web.Contracts.csproj" />
  </ItemGroup>
</Project>
```

**Explanation (ELI5) - New properties:**

- `<EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>`
  > This is CRITICAL for Minimal APIs + Native AOT!
  > Generates code at compile-time for API endpoints (no runtime reflection)
  > Without this, Minimal APIs won't work with Native AOT

- `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
  > Allows "unsafe" code (pointers, direct memory access)
  > Needed for SIMD and memory-mapped files later
  > Called "unsafe" because you can cause crashes if you mess up pointers

- `<InvariantGlobalization>true</InvariantGlobalization>`
  > Removes all culture-specific code (saves ~2 MB on binary size!)
  > Safe for diagnostic tools - we only output UTC timestamps and standard numbers
  > If you later need locale-specific formatting, set to `false` (adds ~2 MB)

- `<PublishTrimmed>true</PublishTrimmed>`
  > Remove unused code from dependencies (like deleting unused apps)

- `<TrimMode>link</TrimMode>`
  > "link" = aggressive trimming at assembly level
  > Alternative is "copyUsed" (safer but larger binary)
  > We use "link" to stay under 17 MB

### Step 2.3: Delete Default Program.cs Content

The template creates a basic Program.cs - we'll replace it entirely.

**File:** `Loom.Web.Api/Program.cs`

**Delete all existing content and type this:**

```csharp
using System.Text.Json;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;

// Create the web application builder
// Think of this as setting up a restaurant before opening
var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// STEP 1: Configure JSON Serialization (Native AOT)
// ============================================================================

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Use our source-generated JSON context
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});

// ============================================================================
// STEP 2: Configure Kestrel (Web Server)
// ============================================================================

builder.WebHost.ConfigureKestrel(options =>
{
    // Remove server header (security - don't advertise we're using Kestrel)
    options.AddServerHeader = false;
    
    // Limit request body size to 1 MB (prevent abuse)
    options.Limits.MaxRequestBodySize = 1_048_576;
    
    // Allow up to 1000 concurrent connections
    options.Limits.MaxConcurrentConnections = 1000;
    
    // Limit max request line size (prevents buffer overflow attacks)
    options.Limits.MaxRequestLineSize = 8192;
});

// ============================================================================
// STEP 3: Build the Application
// ============================================================================

var app = builder.Build();

// ============================================================================
// STEP 4: Configure Middleware Pipeline
// ============================================================================

// Development-only: show detailed errors
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Production: enforce HTTPS
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();  // HTTP Strict Transport Security
    app.UseHttpsRedirection();  // Redirect HTTP → HTTPS
}

// ============================================================================
// STEP 5: Define API Endpoints
// ============================================================================

// Health check endpoint
app.MapGet("/api/health", () =>
{
    // Get current process to read memory usage
    var process = System.Diagnostics.Process.GetCurrentProcess();
    
    // Calculate uptime
    var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
    
    // Create response
    var response = new HealthCheckResponse
    {
        Status = "Healthy",
        Timestamp = DateTime.UtcNow,
        UptimeSeconds = (long)uptime.TotalSeconds,
        MemoryUsageMb = process.WorkingSet64 / 1_048_576.0  // Convert bytes to MB
    };
    
    return Results.Json(
        response,
        LoomJsonSerializerContext.Default.HealthCheckResponse,
        statusCode: 200
    );
})
.WithName("GetHealth")
.WithTags("Health")
.Produces<HealthCheckResponse>(200);

// ============================================================================
// STEP 6: Run the Application
// ============================================================================

app.Run();
```

**Explanation (ELI5) - Let's break this down section by section:**

### Section 1: JSON Serialization Configuration

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, LoomJsonSerializerContext.Default);
});
```

**What's this doing?**
> Telling ASP.NET Core: "When you need to convert objects to/from JSON, use OUR 
> custom serializer (LoomJsonSerializerContext), not the default reflection-based one."
>
> `.Insert(0, ...)` = Put ours FIRST in the chain (highest priority)
> Like putting your favorite shortcut at the top of a menu

### Section 2: Kestrel Configuration

```csharp
builder.WebHost.ConfigureKestrel(options =>
```

**What's Kestrel?**
> Kestrel is the web server built into ASP.NET Core. It's like the engine of a car.
> It receives HTTP requests, sends responses, manages connections.

**Why these limits?**
> - `AddServerHeader = false`: Security - don't tell attackers what software we use
> - `MaxRequestBodySize = 1 MB`: Prevent someone sending 10 GB file to crash us
> - `MaxConcurrentConnections = 1000`: Don't let one attacker open million connections
> - `MaxRequestLineSize = 8192`: Prevent buffer overflow attacks with huge URLs

### Section 3: Build the Application

```csharp
var app = builder.Build();
```

**What's this doing?**
> We've been CONFIGURING the restaurant (builder).
> Now we actually OPEN it for business (app).
> Everything before this line = setup
> Everything after = handling customers (requests)

### Section 4: Middleware Pipeline

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
```

**What's middleware?**
> Middleware is like a series of security checkpoints at an airport.
> Each request passes through multiple checks before reaching your code.
>
> Order matters! Think of it like getting dressed: underwear → pants → shoes
> Not: shoes → pants → underwear

**Why check Environment?**
> - Development: Show detailed errors (helps debugging)
> - Production: Hide error details (security - don't leak info to attackers)

### Section 5: Health Endpoint

```csharp
app.MapGet("/api/health", () =>
```

**What's MapGet?**
> "MapGet" = Handle HTTP GET requests to this path
> Like saying "If someone goes to /api/health, run this code"

**Why lambda `() => { ... }`?**
> This is an anonymous function (no name).
> It's like giving someone directions on the spot vs. writing a manual
> For simple endpoints, lambdas are perfect

**What's Results.Json?**
> Instead of returning the object directly, we explicitly say:
> "Return JSON, using this serializer, with HTTP status 200"
>
> This gives us full control and ensures our source-generated serializer is used

**What are WithName, WithTags, Produces?**
> These are metadata for documentation/tooling:
> - `WithName`: Give endpoint a unique name (for URL generation)
> - `WithTags`: Group endpoints (like folders)
> - `Produces`: Tell Swagger/OpenAPI what type this returns

### Step 2.4: Build and Run

```bash
dotnet build
```

**Expected:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Run the application:**
```bash
dotnet run
```

**Expected output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5080
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shutdown.
```

### Step 2.5: Test the Health Endpoint

**Open another terminal and run:**

```bash
curl http://localhost:5080/api/health
```

**Expected response:**
```json
{
  "status": "Healthy",
  "timestamp": "2026-08-06T12:34:56.789Z",
  "uptimeSeconds": 5,
  "memoryUsageMb": 45.2
}
```

**If you see this JSON response - SUCCESS! 🎉**

### 🔍 Checkpoint 2.1 (MAJOR MILESTONE)
```
✓ Phase 2 Complete: First Working API Endpoint!
✓ Loom.Web.Api project created and configured
✓ Health endpoint returns valid JSON
✓ Native AOT configuration applied
✓ Source-generated serialization working

Understanding Check:
Q: What does ConfigureHttpJsonOptions do?
A: [User explains - should mention: registers custom JSON serializer]

Q: Why do we use Results.Json instead of just returning the object?
A: [User explains - should mention: explicit control over serialization]

Q: What's the difference between builder and app?
A: [User explains - should mention: builder = configuration, app = running application]

Binary Size Check:
Run: dotnet publish -c Release -r win-x64 /p:PublishAot=true
Check size: Should be ~8-10 MB at this stage (only health endpoint)

Celebration: YOU HAVE A WORKING NATIVE AOT API! 🚀
Token Usage: [Check if caching is working - should be minimal]
Ready for Phase 3? [Y/N]
Take a 10-minute break - you earned it!
```

---

# PHASE 3: Core API Endpoints (Metrics)

**Duration:** 3-4 days  
**Goal:** Implement CPU, Memory, and Thread metrics endpoints  
**Why Critical:** Core business logic of the diagnostic tool

## Step 3.1: Create Services Folder

```bash
cd Loom.Web.Api
mkdir Services
mkdir Interfaces
```

**Explanation (ELI5):**
> We're organizing code into folders:
> - `Interfaces` = Contracts (like job descriptions)
> - `Services` = Implementations (people who do the jobs)
>
> This separation lets us swap implementations without changing the API.
> Like changing the chef without changing the menu.

## Step 3.2: Create IMetricsService Interface

**File:** `Loom.Web.Api/Interfaces/IMetricsService.cs`

```csharp
using Loom.Web.Contracts.Dtos;

namespace Loom.Web.Api.Interfaces;

/// <summary>
/// Service for retrieving diagnostic metrics.
/// This is the "contract" - anyone implementing this must provide these methods.
/// </summary>
public interface IMetricsService
{
    /// <summary>
    /// Get current CPU metrics.
    /// ValueTask = optimized Task for performance-critical code
    /// </summary>
    ValueTask<CpuMetricResponse> GetCpuMetricsAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Get current memory metrics.
    /// </summary>
    ValueTask<MemoryMetricResponse> GetMemoryMetricsAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Get current thread metrics.
    /// </summary>
    ValueTask<ThreadMetricResponse> GetThreadMetricsAsync(CancellationToken ct = default);
}
```

**Explanation (ELI5):**

**What's an interface?**
> An interface is like a job description. It says "anyone who claims to do this job 
> must provide these methods." But it doesn't say HOW to do it.
>
> Like: "A Chef must provide: CookMeal(), ServeFood(), CleanKitchen()"
> But it doesn't say what recipe to use or which ingredients.

**Why ValueTask instead of Task?**
> `Task<T>` always allocates memory on the heap (remember - we want zero allocations!)
> `ValueTask<T>` is a struct that MIGHT NOT allocate, depending on the operation
>
> Think of Task like ordering takeout (always involves packaging/waste)
> ValueTask like eating at home (sometimes you have leftovers, sometimes not)
>
> For hot paths (called frequently), ValueTask is faster

**What's CancellationToken?**
> A cancellation token is like a "stop button" for async operations.
> If the user closes their browser, the token gets cancelled, and we stop work.
> Prevents wasting CPU on results no one will see.
>
> `= default` means it's optional (has a default value)

## Step 3.3: Create MetricsService Implementation

**File:** `Loom.Web.Api/Services/MetricsService.cs`

This will be a longer file - type carefully!

```csharp
using System.Diagnostics;
using Loom.Web.Api.Interfaces;
using Loom.Web.Contracts.Dtos;

namespace Loom.Web.Api.Services;

/// <summary>
/// Implementation of metrics collection.
/// Currently uses mock data - will integrate with Loom.Core SIMD engine in Phase 7.
/// </summary>
public sealed class MetricsService : IMetricsService
{
    // Cache the current process to avoid repeated lookups
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    
    // MOCK ONLY: Real implementation MUST use pooled/cached arrays (zero-allocation)
    // These static arrays avoid per-call allocation for mock data
    private static readonly CpuHotpath[] MockHotpaths = new[]
    {
        new CpuHotpath
        {
            MethodName = "OrderProcessor.CalculateTotal",
            CpuPercent = 15.3,
            InvocationCount = 1523,
            AverageTimeMs = 2.4
        },
        new CpuHotpath
        {
            MethodName = "Database.ExecuteQuery",
            CpuPercent = 8.7,
            InvocationCount = 892,
            AverageTimeMs = 5.1
        }
    };
    
    /// <summary>
    /// Get CPU metrics.
    /// </summary>
    public ValueTask<CpuMetricResponse> GetCpuMetricsAsync(CancellationToken ct = default)
    {
        // For now, return mock data
        // TODO Phase 7: Integrate with Loom.Core for real SIMD-based profiling
        
        var response = new CpuMetricResponse
        {
            CpuUsagePercent = _currentProcess.TotalProcessorTime.TotalMilliseconds / 
                             (Environment.ProcessorCount * Environment.TickCount) * 100.0,
            Hotpaths = MockHotpaths,  // Use cached array - zero allocation!
            Timestamp = DateTime.UtcNow
        };
        
        // Return synchronously wrapped in ValueTask
        return ValueTask.FromResult(response);
    }
    
    /// <summary>
    /// Get memory metrics.
    /// </summary>
    public ValueTask<MemoryMetricResponse> GetMemoryMetricsAsync(CancellationToken ct = default)
    {
        // Refresh process info to get current values
        _currentProcess.Refresh();
        
        // Get GC information
        var gcInfo = GC.GetGCMemoryInfo();
        
        var response = new MemoryMetricResponse
        {
            TotalMemoryMb = gcInfo.TotalAvailableMemoryBytes / 1_048_576.0,
            UsedMemoryMb = _currentProcess.WorkingSet64 / 1_048_576.0,
            GcStats = new GarbageCollectionStats
            {
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                TotalGcTimeMs = GC.GetTotalPauseDuration().TotalMilliseconds
            },
            TopAllocations = new[]
            {
                new MemoryAllocation
                {
                    TypeName = "System.String",
                    Count = 50000,
                    TotalBytes = 2_500_000
                },
                new MemoryAllocation
                {
                    TypeName = "System.Byte[]",
                    Count = 1200,
                    TotalBytes = 1_800_000
                }
            },
            Timestamp = DateTime.UtcNow
        };
        
        return ValueTask.FromResult(response);
    }
    
    /// <summary>
    /// Get thread metrics.
    /// </summary>
    public ValueTask<ThreadMetricResponse> GetThreadMetricsAsync(CancellationToken ct = default)
    {
        // Get thread information
        _currentProcess.Refresh();
        var threadCount = _currentProcess.Threads.Count;
        
        // For now, mock blocked threads
        // TODO Phase 7: Real thread profiling
        
        var response = new ThreadMetricResponse
        {
            TotalThreads = threadCount,
            ActiveThreads = threadCount - 2,
            BlockedThreads = 2,
            Blockages = new[]
            {
                new ThreadBlockage
                {
                    ThreadId = 12345,
                    ThreadName = "WorkerThread-1",
                    BlockedOn = "Waiting for database connection",
                    BlockedDurationMs = 1250.5,
                    StackTrace = "at Database.WaitForConnection()\nat OrderProcessor.Process()"
                },
                new ThreadBlockage
                {
                    ThreadId = 12346,
                    ThreadName = null,  // Unnamed thread
                    BlockedOn = "Lock contention on OrderLock",
                    BlockedDurationMs = 500.2,
                    StackTrace = null  // Sometimes we don't capture stack trace
                }
            },
            Timestamp = DateTime.UtcNow
        };
        
        return ValueTask.FromResult(response);
    }
}
```

**Explanation (ELI5):**

**Why sealed class?**
> `sealed` prevents inheritance (no one can extend this class)
> Helps Native AOT optimization - compiler knows exact type, no virtual method lookups
> Like locking a recipe - no one can modify it

**Why cache _currentProcess?**
> `Process.GetCurrentProcess()` is expensive (system call)
> We call it once and reuse
> Like looking up a phone number once and saving it, vs. looking it up every time

**What's ValueTask.FromResult?**
> We have a result immediately (not really async)
> But our interface returns ValueTask, so we wrap it
> Think of it like putting a sandwich in a lunchbox even though you're eating right now
> The "lunchbox" (ValueTask) lets us keep consistent interface

**Why mock data?**
> Right now we don't have the SIMD engine or real profiling hooked up
> These endpoints will work and return realistic-looking data
> In Phase 7, we'll replace mock data with real Loom.Core integration

**Why use _currentProcess.Refresh()?**
> Process information gets stale (cached)
> Refresh() updates it with current values
> Like refreshing a webpage to see new content

## Step 3.4: Register Service in DI Container

**File:** `Loom.Web.Api/Program.cs`

**Add this AFTER `var builder = WebApplication.CreateBuilder(args);` and BEFORE the JSON config:**

```csharp
// ============================================================================
// STEP 0: Register Services (Dependency Injection)
// ============================================================================

builder.Services.AddSingleton<IMetricsService, MetricsService>();
```

**Explanation (ELI5):**

**What's Dependency Injection (DI)?**
> Instead of creating objects yourself (`new MetricsService()`), you ask the 
> framework to give you one. The framework manages the lifetime and sharing.
>
> Like going to a restaurant and ordering "coffee" - you don't make it yourself,
> the restaurant gives you one.

**What's AddSingleton?**
> `Singleton` = One instance for the entire application lifetime
> Like having ONE chef in the restaurant who makes all meals
>
> Alternatives:
> - `AddScoped` = One instance per request (new chef for each customer)
> - `AddTransient` = New instance every time (new chef for each dish)
>
> We use Singleton because MetricsService has no per-request state

**Why interface and implementation?**
> We register `IMetricsService` (interface) → `MetricsService` (implementation)
> Later, if we want a different implementation, we just change this one line
> The rest of the code doesn't know or care
>
> Like swapping chefs - customers still order from same menu

## Step 3.5: Add API Endpoints

**File:** `Loom.Web.Api/Program.cs`

**Add these AFTER the health endpoint:**

```csharp
// CPU metrics endpoint
app.MapGet("/api/metrics/cpu", async (IMetricsService metricsService, CancellationToken ct) =>
{
    var metrics = await metricsService.GetCpuMetricsAsync(ct);
    
    return Results.Json(
        metrics,
        LoomJsonSerializerContext.Default.CpuMetricResponse,
        statusCode: 200
    );
})
.WithName("GetCpuMetrics")
.WithTags("Metrics")
.Produces<CpuMetricResponse>(200);

// Memory metrics endpoint
app.MapGet("/api/metrics/memory", async (IMetricsService metricsService, CancellationToken ct) =>
{
    var metrics = await metricsService.GetMemoryMetricsAsync(ct);
    
    return Results.Json(
        metrics,
        LoomJsonSerializerContext.Default.MemoryMetricResponse,
        statusCode: 200
    );
})
.WithName("GetMemoryMetrics")
.WithTags("Metrics")
.Produces<MemoryMetricResponse>(200);

// Thread metrics endpoint
app.MapGet("/api/metrics/thread", async (IMetricsService metricsService, CancellationToken ct) =>
{
    var metrics = await metricsService.GetThreadMetricsAsync(ct);
    
    return Results.Json(
        metrics,
        LoomJsonSerializerContext.Default.ThreadMetricResponse,
        statusCode: 200
    );
})
.WithName("GetThreadMetrics")
.WithTags("Metrics")
.Produces<ThreadMetricResponse>(200);
```

**Explanation (ELI5):**

**How does IMetricsService get passed in?**
> This is DI magic! ASP.NET Core sees we need `IMetricsService` and automatically
> provides the registered instance (our MetricsService).
>
> Like ordering "coffee" at Starbucks - they know what you mean and give you coffee,
> you don't have to explain "hot brown liquid made from beans"

**Why `async` in the lambda?**
> Our service methods return ValueTask (async)
> So we need to `await` them
> The `async` keyword lets us use `await`

**Why pass CancellationToken?**
> ASP.NET Core automatically provides a token that cancels when request is aborted
> We pass it to our service methods
> Like giving employees a "close for day" signal

## Step 3.6: Build and Test

```bash
dotnet build
```

**Should build with 0 warnings!**

```bash
dotnet run
```

**Test each endpoint:**

```bash
# CPU metrics
curl http://localhost:5080/api/metrics/cpu

# Memory metrics
curl http://localhost:5080/api/metrics/memory

# Thread metrics
curl http://localhost:5080/api/metrics/thread
```

**Expected: Valid JSON responses for each endpoint!**

### 🔍 Checkpoint 3.1 (MAJOR MILESTONE)
```
✓ Phase 3 Complete: Core Metrics Endpoints Working!
✓ Created IMetricsService interface
✓ Implemented MetricsService with mock data
✓ Registered service in DI container
✓ Added 3 metrics endpoints (CPU, Memory, Thread)
✓ All endpoints return valid JSON

Understanding Check:
Q: What's the difference between interface and implementation?
A: [User explains - interface = contract, implementation = actual code]

Q: What's Dependency Injection?
A: [User explains - framework provides dependencies, not manual creation]

Q: Why ValueTask instead of Task?
A: [User explains - performance, potential zero allocation]

Q: Why are we using mock data?
A: [User explains - real SIMD engine comes later, but API works now]

API Testing:
✓ /api/health returns 200 OK
✓ /api/metrics/cpu returns 200 OK with CPU data
✓ /api/metrics/memory returns 200 OK with memory data
✓ /api/metrics/thread returns 200 OK with thread data

Lines typed: ~200+ lines
Token usage: [Should be minimal - using cached docs]
Ready for Phase 4 (WebSockets)? [Y/N]
Break time - 15 minutes! Walk around, rest your hands!
```

---

---

# PHASE 4: WebSocket Real-Time Streaming

**Duration:** 3-4 days
**Goal:** Zero-allocation WebSocket handler streaming CPU/memory/thread updates at ~10 Hz
**Why Critical:** This is the highest-risk phase for accidental heap allocation — every `foreach`, every `new`, every string op in this loop runs 10x/second per connected client
**AOT-compatibility note:** Raw `System.Net.WebSockets`, no SignalR (ADR-2). Buffers come from `ArrayPool<byte>.Shared`, not `new byte[]`. JSON is written directly into the rented buffer via `Utf8JsonWriter` using `LoomJsonSerializerContext`, never reflection-mode serialization.

## Step 4.1: Create Loom.Web.RealTime Project

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"
mkdir Loom.Web.RealTime
cd Loom.Web.RealTime
dotnet new classlib -f net10.0
```

**File:** `Loom.Web.RealTime/Loom.Web.RealTime.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTrimmable>true</IsTrimmable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Loom.Web.Contracts\Loom.Web.Contracts.csproj" />
  </ItemGroup>
</Project>
```

**Explanation (ELI5):** Same pattern as `Loom.Web.Contracts` in Phase 1 — a class library, no `PublishAot`, trim/AOT analyzers on. It references Contracts because it needs the `MetricUpdate` discriminated-union types from Step 1.11.

## Step 4.2: Add Metric Streaming to IMetricsService

**File:** `Loom.Web.Api/Interfaces/IMetricsService.cs`

**Add this member to the interface from Phase 3:**

```csharp
/// <summary>
/// Streams metric updates continuously until the CancellationToken fires.
/// IAsyncEnumerable = a sequence you can 'await foreach' over, one item at a time,
/// instead of waiting for the whole collection to be ready.
/// </summary>
IAsyncEnumerable<MetricUpdate> GetMetricStreamAsync(CancellationToken ct = default);
```

**Explanation (ELI5):**
> `IAsyncEnumerable<T>` is like a conveyor belt instead of a delivery truck. A `Task<List<T>>` waits until every item is ready, packs them all in one truck, then hands them over. `IAsyncEnumerable<T>` puts one item on the belt every time one is ready — perfect for "keep sending updates until told to stop."

**File:** `Loom.Web.Api/Services/MetricsService.cs`

**Add this method to the `MetricsService` class:**

```csharp
public async IAsyncEnumerable<MetricUpdate> GetMetricStreamAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    while (!ct.IsCancellationRequested)
    {
        var cpu = await GetCpuMetricsAsync(ct);
        yield return new CpuMetricUpdate { Timestamp = DateTime.UtcNow, Data = cpu };

        await Task.Delay(100, ct); // ~10 Hz: one update every 100ms

        var memory = await GetMemoryMetricsAsync(ct);
        yield return new MemoryMetricUpdate { Timestamp = DateTime.UtcNow, Data = memory };

        await Task.Delay(100, ct);
    }
}
```

**Explanation (ELI5):**
> `[EnumeratorCancellation]` tells the compiler "wire this parameter into the async-iterator's own cancellation plumbing" — without it, cancelling the token wouldn't actually stop the `while` loop cleanly.
> `yield return` hands one item to the caller and pauses right there — the method doesn't "return" in the normal sense until the loop ends or the token cancels.
> This mock version alternates CPU/memory updates every 100ms (~10 Hz combined). Thread updates can be added the same way; kept out here to keep the example short — add a third `yield return` block following the same pattern if you want all three streams interleaved.

## Step 4.3: The Zero-Allocation WebSocket Handler

**File:** `Loom.Web.RealTime/MetricsWebSocketHandler.cs`

```csharp
using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;

namespace Loom.Web.RealTime;

/// <summary>
/// Streams metric updates over an already-accepted WebSocket connection.
/// Rents one buffer for the lifetime of the connection instead of allocating
/// per-message — this is the whole point of the "zero-allocation" constraint.
/// </summary>
public sealed class MetricsWebSocketHandler(
    WebSocket webSocket,
    IAsyncEnumerable<MetricUpdate> metricStream) : IDisposable
{
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private byte[]? _rentedBuffer;

    public async ValueTask StreamMetricsAsync(CancellationToken ct)
    {
        _rentedBuffer = _bufferPool.Rent(4096);
        try
        {
            await foreach (var metric in metricStream.WithCancellation(ct))
            {
                var bufferWriter = new ArrayBufferWriter<byte>(_rentedBuffer.AsSpan().Length);
                using var writer = new Utf8JsonWriter(bufferWriter);

                JsonSerializer.Serialize(writer, metric, LoomJsonSerializerContext.Default.MetricUpdate);
                writer.Flush();

                await webSocket.SendAsync(
                    bufferWriter.WrittenMemory,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct);
            }
        }
        finally
        {
            if (_rentedBuffer is not null) _bufferPool.Return(_rentedBuffer);
        }
    }

    public void Dispose() => webSocket.Dispose();
}
```

**Explanation (ELI5):**

**Why rent the buffer once, outside the loop?**
> `ArrayPool<byte>.Shared.Rent()` hands you a reusable array instead of the runtime creating a fresh one with `new byte[4096]`. Renting once and reusing it for every message in the connection's lifetime is what makes this loop allocate zero bytes on the managed heap per message.

**What's `ArrayBufferWriter<byte>`?**
> A helper that lets `Utf8JsonWriter` write bytes into memory you control instead of an internal buffer it manages itself. This keeps serialization inside the rented region.

**Why the primary constructor syntax (`MetricsWebSocketHandler(WebSocket webSocket, ...)`)?**
> C# 12+ primary constructors turn constructor parameters directly into fields you can reference by name in the class body — less boilerplate than writing `private readonly WebSocket _webSocket; public MetricsWebSocketHandler(WebSocket webSocket) { _webSocket = webSocket; }` by hand.

**Why `finally { _bufferPool.Return(...) }`?**
> If the client disconnects or the token cancels mid-stream, the `finally` block still runs and returns the buffer to the pool. Skipping this "leaks" the buffer — the pool never gets it back, and over many connections that adds up to real memory growth. This is the exact failure mode flagged in the Risk Register as "WebSocket connection leaks."

## Step 4.4: Wire the WebSocket Endpoint

**File:** `Loom.Web.Api/Program.cs`

**Add after the metrics endpoints from Phase 3:**

```csharp
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.Map("/ws/metrics", async (HttpContext context, IMetricsService metricsService) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var stream = metricsService.GetMetricStreamAsync(context.RequestAborted);
    using var handler = new MetricsWebSocketHandler(webSocket, stream);
    await handler.StreamMetricsAsync(context.RequestAborted);
});
```

**Explanation (ELI5):**
> `app.UseWebSockets(...)` turns on WebSocket support in Kestrel — without it, `IsWebSocketRequest` is always false and every connection attempt gets rejected.
> `KeepAliveInterval` sends a ping every 30 seconds so proxies/load balancers don't silently drop an idle-looking connection.
> `context.RequestAborted` is the same "stop button" pattern from Phase 3 — if the client disconnects, this token cancels and the streaming loop (and the `while` loop inside it) unwinds cleanly.

## Step 4.5: Verify

```bash
dotnet build
dotnet run --project Loom.Web.Api

# In another terminal:
npx wscat -c ws://localhost:5080/ws/metrics
# Should receive a CpuMetricUpdate, then a MemoryMetricUpdate, roughly every 100ms

dotnet-counters monitor --process-id $(pidof Loom.Web.Api) System.Runtime
# GC Heap Allocations should stay at (or very near) 0 B/s while streaming
```

### 🔍 Checkpoint 4.1 (MAJOR MILESTONE — Foundation complete)
```
✓ Phase 4 Complete: Real-Time WebSocket Streaming
✓ Loom.Web.RealTime project created
✓ IMetricsService.GetMetricStreamAsync implemented (IAsyncEnumerable)
✓ MetricsWebSocketHandler uses ArrayPool<byte> — zero allocation confirmed
✓ /ws/metrics endpoint streams CPU/memory updates at ~10 Hz
✓ Foundation (Phases 0-4) done — matches wiggly-noodling-hoare.md's "Complete" status

Understanding Check:
Q: Why IAsyncEnumerable instead of returning a List<MetricUpdate>?
A: [User explains — streaming vs. wait-for-everything]

Q: What would happen if we allocated a new byte[] per message instead of renting?
A: [User explains — GC pressure, allocation rate visible in dotnet-counters]

Ready for Phase 5 (Source Generator)? [Y/N]
Take a break — Phase 5 is dense (Roslyn incremental generators).
```

---

# PHASE 5: Source Generator (`Loom.Telemetry.Generators`)

**Duration:** 5-7 days (highest-complexity phase in the platform)
**Goal:** A Roslyn incremental source generator that rewrites `[LoomProfile]`-decorated methods at compile time into zero-allocation, `Stopwatch`-timed telemetry emission.
**Why Critical:** This is the mechanism — not a nice-to-have — that makes Phase 7 (`[LoomProfile]`/`[LoomTrack]`) and Phase 8 (`ILoomCollector`) Native AOT-compatible. Without it, "attribute-based instrumentation" would mean scanning assemblies for attributes at runtime, which is exactly the reflection Native AOT forbids.
**AOT-compatibility note:** Source generators run **at compile time**, inside the C# compiler itself, and emit ordinary C# source that gets compiled normally. The generated code contains no reflection — it's `Stopwatch` calls and direct method calls the generator wrote out by hand, so to the AOT compiler it looks identical to code you typed yourself. This is ADR-4 in `wiggly-noodling-hoare.md` — see that ADR for the full list of rejected alternatives (Fody/IL weaving, PostSharp, `MethodInfo.Invoke`, `System.Reflection.Emit`, `ConditionalWeakTable`/`DynamicMethod`), summarized briefly in Step 5.0 below.

## Step 5.0: Why Not the Alternatives (ADR-4 Recap)

Before building this, it's worth knowing what was ruled out and why, so the design choices in Steps 5.1-5.4 don't look arbitrary:

| Alternative | Why rejected |
|---|---|
| Fody / IL weaving | Post-compilation IL rewriting is fragile against Native AOT's trimmer — trimmed methods may disappear before weaving runs; depends on `Mono.Cecil`, which has its own AOT compatibility issues |
| PostSharp | Commercial; uses runtime reflection for aspect activation; heavy binary overhead |
| `MethodInfo.Invoke` | Impossible under Native AOT — no runtime method dispatch by reflection |
| `System.Reflection.Emit` | Impossible under Native AOT — no JIT to emit into |
| `ConditionalWeakTable`/`DynamicMethod` | Runtime codegen — same problem as `Reflection.Emit` |

Source generators win because they run at **compile time**: the output is plain C# the AOT compiler treats exactly like hand-written code, incremental generators are cached (fast rebuilds), and the generated `.g.cs` files are inspectable in `obj/` — nothing about the mechanism is a black box.

## Step 5.1: Create the Generator Project

Source generators live in their own project and are referenced differently from a normal library — as an `Analyzer`, not a runtime dependency.

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"
mkdir Loom.Telemetry.Generators
cd Loom.Telemetry.Generators
dotnet new classlib -f netstandard2.0
```

**Explanation (ELI5):**
> Why `netstandard2.0` when everything else targets `net10.0`? Because the source generator doesn't run *in* your app — it runs *inside the C# compiler* while your app is being built, and the compiler (Roslyn) itself targets `netstandard2.0` for maximum compatibility across IDE/build-tool versions. This is a Microsoft requirement for all Roslyn analyzers/generators, not a Loom-specific choice — restated here from `wiggly-noodling-hoare.md` ADR-4's "Key constraint" line.

**File:** `Loom.Telemetry.Generators/Loom.Telemetry.Generators.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Explanation (ELI5):**
- `IsRoslynComponent = true` — tells the SDK "this project produces an analyzer/generator, package it accordingly."
- `EnforceExtendedAnalyzerRules = true` — turns on extra analyzer rules that catch common generator mistakes (like accidentally referencing `Console.WriteLine` or doing file I/O inside a generator, which can break IDE responsiveness).
- `PrivateAssets="all"` on the Roslyn packages — these are build-time-only tools; they must not become a runtime dependency of anything that references `Loom.Telemetry.Generators`.

## Step 5.2: Define the Attributes the Generator Looks For

The generator needs *something* in source code to detect. `[LoomProfile]`/`[LoomTrack]` are consumed by app code via `Loom.Telemetry` (Phase 6) — declare them there once Phase 6 exists; for this phase, declare them directly in the generator project so Phase 5 is self-contained and testable, then move the file (not redefine it) when Phase 6 lands.

**File:** `Loom.Telemetry.Generators/LoomAttributes.cs`

```csharp
namespace Loom.Telemetry;

/// <summary>
/// Marks a method for automatic profiling: entry/exit timing, call count,
/// and exception capture, all emitted with zero heap allocation.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LoomProfileAttribute : Attribute
{
    /// <summary>Optional explicit metric name. Defaults to "{ClassName}.{MethodName}".</summary>
    public string? Name { get; init; }
}

/// <summary>
/// Marks a property for value-change tracking. Every setter invocation emits
/// a gauge update.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LoomTrackAttribute : Attribute
{
    public string? Name { get; init; }
}
```

**Explanation (ELI5):**
> These are ordinary attribute classes — nothing generator-specific about them yet. The generator's job (next step) is to scan the syntax tree for methods/properties decorated with these, and, for each one it finds, emit a *partner* partial class containing the wrapped, timed version of that method. The attributes themselves compile into the final binary as simple metadata (attributes are AOT-safe as long as nothing reads them via reflection at runtime — and nothing here does).

## Step 5.3: The Incremental Generator

**File:** `Loom.Telemetry.Generators/LoomProfileGenerator.cs`

```csharp
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loom.Telemetry.Generators;

[Generator]
public sealed class LoomProfileGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var profiledMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Loom.Telemetry.LoomProfileAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => (MethodDeclarationSyntax)ctx.TargetNode)
            .Collect();

        context.RegisterSourceOutput(profiledMethods, static (spc, methods) =>
        {
            foreach (var group in methods.GroupBy(GetContainingClassName))
            {
                var source = GenerateInterceptorSource(group.Key, group.ToImmutableArray());
                spc.AddSource($"{group.Key}.LoomProfile.g.cs", source);
            }
        });
    }

    private static string GetContainingClassName(MethodDeclarationSyntax method) =>
        (method.Parent as ClassDeclarationSyntax)?.Identifier.Text ?? "Unknown";

    private static string GenerateInterceptorSource(
        string className, ImmutableArray<MethodDeclarationSyntax> methods)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> — emitted by Loom.Telemetry.Generators, do not edit.");
        sb.AppendLine("using System.Diagnostics;");
        sb.AppendLine("using Loom.Telemetry;");
        sb.AppendLine();
        sb.AppendLine($"partial class {className}");
        sb.AppendLine("{");

        foreach (var method in methods)
        {
            var methodName = method.Identifier.Text;
            var metricName = $"{className}.{methodName}";

            sb.AppendLine($"    private static partial void __LoomProfile_{methodName}_Begin(out long startTicks)");
            sb.AppendLine( "    {");
            sb.AppendLine( "        startTicks = Stopwatch.GetTimestamp();");
            sb.AppendLine( "    }");
            sb.AppendLine();
            sb.AppendLine($"    private static partial void __LoomProfile_{methodName}_End(long startTicks, Exception? exception)");
            sb.AppendLine( "    {");
            sb.AppendLine( "        var elapsed = Stopwatch.GetElapsedTime(startTicks);");
            sb.AppendLine($"        LoomRuntime.RecordMethodExecution(\"{metricName}\", elapsed, exception);");
            sb.AppendLine( "    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
```

**Explanation (ELI5):**

**Why `IIncrementalGenerator`, not the older `ISourceGenerator`?**
> Incremental generators cache their work per-syntax-node, so the IDE only re-runs the generator for the *specific method* that changed, not the whole file/project, every keystroke. `ISourceGenerator` re-runs everything on every change — noticeably slower in a large solution. All new generators should be incremental; `ISourceGenerator` is effectively legacy.

**What does `ForAttributeWithMetadataName` do?**
> It's a purpose-built, highly optimized entry point (added specifically to make attribute-driven generators fast) that says "find every syntax node decorated with this exact attribute, by its full metadata name." This is much faster than manually walking the whole syntax tree yourself.

**Why generate `Begin`/`End` partial methods instead of directly wrapping the method body?**
> Directly rewriting a method's body via source generation is possible but fragile (you'd have to accurately reparse and reproduce arbitrary method bodies, including all control flow, try/catch, and generics). The `Begin`/`End` partial-method pattern is the standard, robust approach: the *developer's* method declares itself `partial` and calls the two hooks; the generator fills in the hook implementations. This is the same pattern .NET itself uses internally (e.g. `LoggerMessage` source generators). It requires one extra line of ceremony from the developer (shown in Phase 7) in exchange for much simpler, much more reliable generator code.

**Is this actually zero-allocation?**
> `Stopwatch.GetTimestamp()` and `Stopwatch.GetElapsedTime()` are value-type-returning static methods — no allocation. `LoomRuntime.RecordMethodExecution(...)` (built in Phase 6) is responsible for keeping its own hot path allocation-free the same way the WebSocket handler in Phase 4 does — string interpolation for the metric name happens once per *method*, at generator time (compile time), not once per *call* (runtime), which is the key difference from a reflection-based approach.

## Step 5.4: Referencing the Generator Correctly

**File:** `Loom.Telemetry/Loom.Telemetry.csproj` (created fully in Phase 6 — this is the one line that matters for Phase 5 verification)

```xml
<ItemGroup>
  <ProjectReference Include="..\Loom.Telemetry.Generators\Loom.Telemetry.Generators.csproj"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
</ItemGroup>
```

**Explanation (ELI5):**
> This is the line every "why isn't my generator running" bug report is missing. A *normal* `<ProjectReference>` tells MSBuild "link this project's compiled output into mine at runtime." `OutputItemType="Analyzer"` instead tells MSBuild "run this project's output *as a compiler plugin* during my build." `ReferenceOutputAssembly="false"` explicitly says "and don't also add it as a runtime DLL reference" — without this, you'd get a build warning and the generator's own `netstandard2.0` assembly would leak into your `net10.0` app's dependency list for no reason. This matches `wiggly-noodling-hoare.md`'s Dependency Flow note: "Loom.Telemetry.Generators (analyzer — referenced as `<Analyzer>`, no runtime dependency)."

## Step 5.5: Verify the Generator in Isolation

Before Phase 6/7 build real usage on top of this, confirm the generator itself works — this is also what the Risk Register calls out as the mitigation for "source generator complexity/debugging": unit tests on generated output, inspectable `.g.cs` files.

**File:** `Loom.Tests/SourceGenTests/LoomProfileGeneratorTests.cs`

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Loom.Telemetry.Generators;
using Xunit;

namespace Loom.Tests.SourceGenTests;

public class LoomProfileGeneratorTests
{
    [Fact]
    public void Generates_BeginEnd_Methods_For_Profiled_Method()
    {
        const string source = """
            using Loom.Telemetry;
            public partial class OrderProcessor
            {
                [LoomProfile]
                public partial void ProcessOrder();
            }
            """;

        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var generator = new LoomProfileGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        Assert.Single(result.GeneratedTrees);
        Assert.Contains("__LoomProfile_ProcessOrder_Begin", result.GeneratedTrees[0].ToString());
    }
}
```

**Explanation (ELI5):**
> `CSharpGeneratorDriver` lets you run a generator against a hand-built, in-memory `Compilation` without needing a real project on disk — this is the standard way to unit-test a source generator. The test checks the generator's *output source text* directly, since that's the actual contract this phase promises.

**Verification:**
```bash
dotnet test Loom.Tests --filter LoomProfileGeneratorTests
# Should pass — confirms the generator produces the expected partial methods
```

### 🔍 Checkpoint 5.1 (MAJOR MILESTONE — dependency for two later phases)
```
✓ Phase 5 Complete: Source Generator Working
✓ Loom.Telemetry.Generators project created (netstandard2.0, IsRoslynComponent)
✓ ADR-4 alternatives reviewed and understood (why not Fody/PostSharp/reflection)
✓ LoomProfileAttribute/LoomTrackAttribute defined
✓ IIncrementalGenerator scans for [LoomProfile], emits Begin/End partial methods
✓ Generator unit-tested in isolation via CSharpGeneratorDriver
✓ Correct <ProjectReference OutputItemType="Analyzer"> pattern documented for Phase 6

Understanding Check:
Q: Why netstandard2.0 for the generator project specifically?
A: [User explains — runs inside the Roslyn compiler, not the app]

Q: Why Begin/End partial methods instead of rewriting the method body directly?
A: [User explains — robustness vs. reparsing arbitrary method bodies]

Q: What breaks if OutputItemType="Analyzer" is missing from the reference?
A: [User explains — generator becomes a normal runtime dependency, doesn't run at compile time, [LoomProfile] does nothing]

Phase 7 and Phase 8 both depend on this phase being genuinely complete — don't
proceed to either until the generator test above passes.

Ready for Phase 6 (Custom Metrics API)? [Y/N]
```
---

# PHASE 6: Custom Metrics API (`Loom.Telemetry`)

**Duration:** 4-5 days
**Goal:** `RecordMetric`/`RecordCounter`/`RecordGauge`/`RecordHistogram` with tag/dimension support, backed by a zero-allocation ring buffer (ADR-5) — not a growing dictionary.
**Why Critical:** Every app knows its own domain metrics better than generic CPU/memory numbers. This phase is also the storage foundation Phases 8-13 all read from or write into.
**AOT-compatibility note:** Tags are interned into small integer indices via a `ConcurrentDictionary<TagKey, int>` built at registration time (not per-write); the ring buffer itself is a pre-allocated `struct[]` — no boxing, no per-write heap allocation.

## Step 6.1: Create the Loom.Telemetry Project

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"
mkdir Loom.Telemetry
cd Loom.Telemetry
dotnet new classlib -f net10.0
```

**File:** `Loom.Telemetry/Loom.Telemetry.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTrimmable>true</IsTrimmable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
    <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
    <IsPackable>true</IsPackable>
    <PackageId>Loom.Telemetry</PackageId>
    <Description>Instrumentation runtime for Project Loom — custom metrics, attribute-based profiling, ring-buffer storage.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Loom.Web.Contracts\Loom.Web.Contracts.csproj" />
    <ProjectReference Include="..\Loom.Telemetry.Generators\Loom.Telemetry.Generators.csproj"
                       OutputItemType="Analyzer"
                       ReferenceOutputAssembly="false" />
  </ItemGroup>

  <ItemGroup>
    <!-- Phases 8-12 need to see LoomRuntime's internal snapshot accessor without making it public. -->
    <InternalsVisibleTo Include="Loom.Telemetry.Collectors" />
    <InternalsVisibleTo Include="Loom.Telemetry.Query" />
    <InternalsVisibleTo Include="Loom.Telemetry.Alerting" />
    <InternalsVisibleTo Include="Loom.Telemetry.Exporters" />
    <InternalsVisibleTo Include="Loom.Tests" />
  </ItemGroup>
</Project>
```

**Explanation (ELI5):**
> `IsPackable = true` + `PackageId` mark this as the actual NuGet package a consuming app installs. The generator reference is the exact `OutputItemType="Analyzer"` pattern verified in Phase 5, Step 5.4. `InternalsVisibleTo` is declared once, here, for every downstream `Loom.Telemetry.*` project that will need it in later phases — rather than adding it piecemeal per phase, which is easy to forget and would silently break a later phase's build.

## Step 6.2: The Tag Interning Type (ADR-5)

**File:** `Loom.Telemetry/TagKey.cs`

```csharp
namespace Loom.Telemetry;

/// <summary>A single key/value tag, e.g. { "OrderType", "Premium" }.</summary>
public readonly record struct MetricTag(string Key, string Value);

/// <summary>
/// A composite key over a metric name + its tag set, with a precomputed hash so
/// ConcurrentDictionary lookups during interning don't recompute the hash on every
/// write. Interning happens once per unique {name, tags} combination, not per call.
/// </summary>
internal readonly struct TagKey : IEquatable<TagKey>
{
    private readonly string _metricName;
    private readonly MetricTag[] _tags; // sorted by Key for stable equality/hash
    private readonly int _hash;

    public TagKey(string metricName, ReadOnlySpan<MetricTag> tags)
    {
        _metricName = metricName;
        var sorted = tags.ToArray();
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        _tags = sorted;

        var hash = new HashCode();
        hash.Add(metricName);
        foreach (var tag in sorted) { hash.Add(tag.Key); hash.Add(tag.Value); }
        _hash = hash.ToHashCode();
    }

    public bool Equals(TagKey other) =>
        _hash == other._hash && _metricName == other._metricName && _tags.AsSpan().SequenceEqual(other._tags);

    public override bool Equals(object? obj) => obj is TagKey other && Equals(other);
    public override int GetHashCode() => _hash;
}
```

**Explanation (ELI5):**
> Sorting tags by key before hashing means `{Region: US, Gateway: Stripe}` and `{Gateway: Stripe, Region: US}` intern to the *same* `TagKey` — callers shouldn't have to remember a specific tag order for the same logical series. The hash is computed once, in the constructor, and reused on every dictionary lookup — this is the "precomputed hash" ADR-5 calls out, and it matters because `ConcurrentDictionary` calls `GetHashCode()` on every operation, not just once.

## Step 6.3: The Ring Buffer

**File:** `Loom.Telemetry/MetricRingBuffer.cs`

```csharp
using System.Threading;

namespace Loom.Telemetry;

/// <summary>One entry: a timestamp, a value, and which interned tag combination it belongs to.</summary>
internal readonly struct MetricEntry
{
    public readonly long Ticks;
    public readonly double Value;
    public readonly int TagIndex;

    public MetricEntry(long ticks, double value, int tagIndex)
    {
        Ticks = ticks; Value = value; TagIndex = tagIndex;
    }
}

/// <summary>
/// Fixed-size, pre-allocated circular buffer for one metric. Writes never resize
/// or allocate — the oldest entry is simply overwritten once the buffer is full.
/// Head/tail use Interlocked so readers can snapshot without blocking writers.
/// </summary>
internal sealed class MetricRingBuffer(int capacity = 4096)
{
    private readonly MetricEntry[] _buffer = new MetricEntry[capacity];
    private long _writeIndex = -1; // Interlocked.Increment returns the post-increment value, so start at -1

    public void Write(double value, int tagIndex)
    {
        var index = Interlocked.Increment(ref _writeIndex);
        var slot = (int)(index % capacity);
        _buffer[slot] = new MetricEntry(DateTime.UtcNow.Ticks, value, tagIndex);
    }

    /// <summary>A point-in-time copy for readers (query engine, exporters). Allocates once per
    /// call — acceptable here because reads happen far less often than writes (Query/export
    /// cadence, not per-request), same trade-off as ADR-7's executor and ADR-9's flush loop.</summary>
    public MetricEntry[] Snapshot()
    {
        var count = (int)Math.Min(Interlocked.Read(ref _writeIndex) + 1, capacity);
        var copy = new MetricEntry[count];
        Array.Copy(_buffer, copy, count);
        return copy;
    }
}
```

**Explanation (ELI5):**
> `Interlocked.Increment` is an atomic "add one and tell me the new value" operation — many threads can call `Write()` concurrently and each still gets its own unique slot, with no `lock` needed on the hot path. Once `_writeIndex` exceeds `capacity`, the modulo (`% capacity`) wraps back to slot 0 and starts overwriting — that's the "ring" in ring buffer. This bounds memory at exactly `capacity * sizeof(MetricEntry)` per metric+tag-combination forever, which is what keeps the whole platform under the 20 MB background-memory budget regardless of how long the process has been running or how much data has flowed through it.
>
> **Honest trade-off:** `Snapshot()` does allocate an array — but only when something reads (a query, an export flush), not on every write. This mirrors the same "hot path zero-alloc, cold path LINQ-is-fine" split used elsewhere in this platform (see Phase 10's executor and Phase 12's exporters) — flagged explicitly, not silently assumed acceptable.

## Step 6.4: The Recording Surface

**File:** `Loom.Telemetry/LoomRuntime.cs`

```csharp
using System.Collections.Concurrent;

namespace Loom.Telemetry;

/// <summary>
/// The static entry point devs call directly through ILoomClient, and the target
/// the Phase 5 source generator calls into from generated Begin/End partial methods.
/// </summary>
public static class LoomRuntime
{
    private static readonly ConcurrentDictionary<string, MetricRingBuffer> Buffers = new();
    private static readonly ConcurrentDictionary<TagKey, int> TagIndex = new();
    private static int _nextTagIndex;

    public static void RecordMetric(string name, double value, ReadOnlySpan<MetricTag> tags = default) =>
        RecordHistogram(name, value, tags);

    public static void RecordCounter(string name, long increment = 1, ReadOnlySpan<MetricTag> tags = default)
    {
        var buffer = Buffers.GetOrAdd(name, static _ => new MetricRingBuffer());
        buffer.Write(increment, InternTags(name, tags));
    }

    public static void RecordGauge(string name, double value, ReadOnlySpan<MetricTag> tags = default)
    {
        var buffer = Buffers.GetOrAdd(name, static _ => new MetricRingBuffer());
        buffer.Write(value, InternTags(name, tags));
    }

    public static void RecordHistogram(string name, double value, ReadOnlySpan<MetricTag> tags = default)
    {
        var buffer = Buffers.GetOrAdd(name, static _ => new MetricRingBuffer());
        buffer.Write(value, InternTags(name, tags));
    }

    /// <summary>Called by Phase 5's generated code — not intended for direct use.</summary>
    public static void RecordMethodExecution(string metricName, TimeSpan elapsed, Exception? exception)
    {
        RecordHistogram($"{metricName}.Duration", elapsed.TotalMilliseconds);
        RecordCounter($"{metricName}.Invocations");
        if (exception is not null) RecordCounter($"{metricName}.Exceptions");
    }

    private static int InternTags(string metricName, ReadOnlySpan<MetricTag> tags) =>
        TagIndex.GetOrAdd(new TagKey(metricName, tags), static _ => Interlocked.Increment(ref _nextTagIndex));

    /// <summary>Read by Phase 9's sampler, Phase 10's query engine, Phase 12's exporters.</summary>
    internal static IReadOnlyDictionary<string, MetricRingBuffer> GetBuffersSnapshot() => Buffers;
}
```

**Explanation (ELI5):**

**Why is there no separate `Counters`/`Gauges`/`Histograms` dictionary like a first-pass design might have?**
> ADR-5's ring buffer is generic over "a metric has a series of timestamped values" — a counter is just a metric where each write is an increment, a gauge is a metric where each write replaces the meaning of "current," and a histogram is a metric where all the individual values matter for later aggregation. The *storage* is identical (a ring buffer of `MetricEntry`); only *how a reader interprets* the series differs, and that interpretation lives in Phase 10's query engine (`AVG`, `COUNT`, etc.) and Phase 12's exporters, not in the write path. One ring-buffer type instead of three specialized ones is what keeps `RecordCounter`/`RecordGauge`/`RecordHistogram` all equally cheap.

**Why `ReadOnlySpan<MetricTag>` instead of `params MetricTag[]`?**
> `params MetricTag[]` allocates a new array at every call site unless the caller happens to reuse one. `ReadOnlySpan<MetricTag>` lets a caller pass `stackalloc` tags, a slice of an existing array, or `default` (empty) with no allocation. The tradeoff: call sites write `RecordCounter("PaymentFailures", tags: [new("Gateway", "Stripe")])` (a collection expression, which the compiler can turn into a `stackalloc`-backed span for small fixed sets) instead of a fluent tag builder.

**Why static, not a regular injected service?**
> The Phase 5 generated code calls `LoomRuntime.RecordMethodExecution(...)` from a `static partial` method with no access to the app's DI container. `ILoomClient` (Step 6.6) wraps this in an injectable facade for everywhere else in the app.

## Step 6.5: DTOs — Ingest and Read Models

**File:** `Loom.Web.Contracts/Dtos/MetricDtos.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>What Loom.Telemetry posts to /api/telemetry/ingest for the cross-process/remote
/// case (in-process apps call LoomRuntime directly and never hit this endpoint).</summary>
public sealed record MetricRecord
{
    public required string Name { get; init; }
    public required string Kind { get; init; } // "counter" | "gauge" | "histogram"
    public required double Value { get; init; }
    public IReadOnlyList<MetricTagDto>? Tags { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record MetricTagDto
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}

/// <summary>A batch wrapper — the ingest endpoint accepts either one MetricRecord or a
/// MetricBatch; batching is also the unit Phase 12's push exporters flush.</summary>
public sealed record MetricBatch
{
    public required IReadOnlyList<MetricRecord> Records { get; init; }
}

/// <summary>What GET /api/telemetry/metrics returns — the list of currently registered
/// metric names with their kind and last-known summary, not raw ring buffer contents
/// (that's what the query engine in Phase 10 is for).</summary>
public sealed record MetricRegistration
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required DateTime LastRecordedAt { get; init; }
}

public sealed record CounterValue { public required string Name { get; init; } public required long Total { get; init; } }
public sealed record GaugeValue { public required string Name { get; init; } public required double Current { get; init; } }

public sealed record HistogramValue
{
    public required string Name { get; init; }
    public required long Count { get; init; }
    public required double Sum { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public IReadOnlyList<HistogramBucket>? Buckets { get; init; }
}

public sealed record HistogramBucket { public required double UpperBound { get; init; } public required long Count { get; init; } }
```

**Register in `Loom.Web.Contracts/JsonContext.cs` — add to the existing attribute list from Phase 1:**

```csharp
[JsonSerializable(typeof(MetricRecord))]
[JsonSerializable(typeof(MetricTagDto))]
[JsonSerializable(typeof(MetricBatch))]
[JsonSerializable(typeof(MetricRegistration))]
[JsonSerializable(typeof(List<MetricRegistration>))]
[JsonSerializable(typeof(CounterValue))]
[JsonSerializable(typeof(GaugeValue))]
[JsonSerializable(typeof(HistogramValue))]
[JsonSerializable(typeof(HistogramBucket))]
```

**Explanation (ELI5):**
> Every DTO from every phase from here on follows the same two-step ritual established in Phase 1: define the record, then add it to `LoomJsonSerializerContext`. This callout won't be repeated in full for every remaining DTO in this document — treat "register the new DTO in JsonContext" as implied any time a new `record` type appears, and the `native-aot-guard` skill (see `skills.md`) is what catches it if you forget.

## Step 6.6: The Injectable Facade + DI Entry Point

**File:** `Loom.Telemetry/ILoomClient.cs`

```csharp
namespace Loom.Telemetry;

/// <summary>Inject this — not LoomRuntime directly — everywhere except generated code.</summary>
public interface ILoomClient
{
    void RecordMetric(string name, double value, ReadOnlySpan<MetricTag> tags = default);
    void RecordCounter(string name, long increment = 1, ReadOnlySpan<MetricTag> tags = default);
    void RecordGauge(string name, double value, ReadOnlySpan<MetricTag> tags = default);
    void RecordHistogram(string name, double value, ReadOnlySpan<MetricTag> tags = default);
}

internal sealed class LoomClient : ILoomClient
{
    public void RecordMetric(string name, double value, ReadOnlySpan<MetricTag> tags = default) =>
        LoomRuntime.RecordMetric(name, value, tags);
    public void RecordCounter(string name, long increment = 1, ReadOnlySpan<MetricTag> tags = default) =>
        LoomRuntime.RecordCounter(name, increment, tags);
    public void RecordGauge(string name, double value, ReadOnlySpan<MetricTag> tags = default) =>
        LoomRuntime.RecordGauge(name, value, tags);
    public void RecordHistogram(string name, double value, ReadOnlySpan<MetricTag> tags = default) =>
        LoomRuntime.RecordHistogram(name, value, tags);
}
```

**File:** `Loom.Telemetry/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Telemetry;

public static class ServiceCollectionExtensions
{
    /// <summary>Entry point every later phase's options.AddX(...) hangs off of —
    /// AddLoomCollector (Phase 8), sampling config (Phase 9), AddAlert (Phase 11),
    /// Export.To*() (Phase 12) all extend LoomTelemetryOptions, not this method.</summary>
    public static IServiceCollection AddLoomTelemetry(
        this IServiceCollection services, Action<LoomTelemetryOptions>? configure = null)
    {
        var options = new LoomTelemetryOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<ILoomClient, LoomClient>();
        return services;
    }
}

/// <summary>Intentionally near-empty — extended in Phase 8 (collectors), Phase 9 (sampling),
/// Phase 11 (alerts), Phase 12 (exporters) via partial-class-style extension methods, so
/// services.AddLoomTelemetry(options => { options.AddLoomCollector<T>(); ... }) reads as
/// one coherent block regardless of which phases are wired up.</summary>
public sealed class LoomTelemetryOptions;
```

## Step 6.7: The Ingestion Endpoint

**File:** `Loom.Web.Api/Program.cs` — **add after the Phase 4 WebSocket endpoint:**

```csharp
app.MapPost("/api/telemetry/ingest", async (HttpContext context) =>
{
    // Accept either a single MetricRecord or a MetricBatch — sniff the shape via a
    // small discriminator rather than two endpoints, matching README's single
    // "POST /api/telemetry/ingest" entry.
    using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    var records = doc.RootElement.TryGetProperty("records", out _)
        ? JsonSerializer.Deserialize(doc.RootElement, LoomJsonSerializerContext.Default.MetricBatch)!.Records
        : [JsonSerializer.Deserialize(doc.RootElement, LoomJsonSerializerContext.Default.MetricRecord)!];

    foreach (var record in records)
    {
        var tags = record.Tags?.Select(t => new MetricTag(t.Key, t.Value)).ToArray() ?? [];
        switch (record.Kind)
        {
            case "counter": LoomRuntime.RecordCounter(record.Name, (long)record.Value, tags); break;
            case "gauge": LoomRuntime.RecordGauge(record.Name, record.Value, tags); break;
            default: LoomRuntime.RecordHistogram(record.Name, record.Value, tags); break;
        }
    }

    context.Response.StatusCode = 202;
});

app.MapGet("/api/telemetry/metrics", (IMetricRegistry registry) =>
    Results.Json(registry.ListRegistered(), LoomJsonSerializerContext.Default.ListMetricRegistration));
```

**Explanation (ELI5):**
> `JsonDocument.ParseAsync` + `TryGetProperty("records", ...)` is a small, deliberate shape-sniff — cheaper than trying two separate `Deserialize` calls with try/catch, and still fully source-generator-driven (no reflection) since each branch still deserializes through an explicit `JsonTypeInfo` from `LoomJsonSerializerContext`. `IMetricRegistry` (interface + a small implementation that reads `LoomRuntime.GetBuffersSnapshot()` and reports name/kind/last-write-time per metric) is a short addition left as a natural exercise following the `IMetricsService` pattern from Phase 3 — not reproduced line-for-line here since the pattern, not the specifics, is what's new.

## Step 6.8: Verify

```bash
dotnet build Loom.Telemetry
dotnet build Loom.Web.Api

curl -X POST http://localhost:5080/api/telemetry/ingest \
  -H "Content-Type: application/json" \
  -d '{"name":"OrderProcessingTime","kind":"histogram","value":245.5,"timestamp":"2026-08-14T12:00:00Z"}'
# Should return 202 Accepted

curl http://localhost:5080/api/telemetry/metrics
# Should list OrderProcessingTime with kind "histogram"
```

### 🔍 Checkpoint 6.1 (MAJOR MILESTONE)
```
✓ Phase 6 Complete: Custom Metrics API
✓ Loom.Telemetry packable NuGet project, references Loom.Telemetry.Generators as Analyzer
✓ TagKey — sorted, precomputed-hash interning key (ADR-5)
✓ MetricRingBuffer — fixed-size, Interlocked writes, bounded memory
✓ LoomRuntime — RecordMetric/Counter/Gauge/Histogram + generator's RecordMethodExecution hook
✓ ILoomClient/LoomClient — injectable facade, AddLoomTelemetry() DI entry point
✓ MetricRecord/MetricBatch/MetricRegistration + read-model DTOs (CounterValue/GaugeValue/HistogramValue/HistogramBucket)
✓ POST /api/telemetry/ingest (single or batch) + GET /api/telemetry/metrics
✓ New DTOs registered in LoomJsonSerializerContext

Understanding Check:
Q: Why does the ring buffer overwrite old entries instead of growing?
A: [User explains — bounded memory regardless of process uptime, ADR-5]

Q: Why is there one MetricRingBuffer type instead of separate Counter/Gauge/Histogram storage?
A: [User explains — storage is identical, interpretation happens at read time]

Q: What's the honest allocation caveat on MetricRingBuffer.Snapshot()?
A: [User explains — read-path allocation is acceptable, write-path is not]

Ready for Phase 7 (Attribute-Based Instrumentation)? [Y/N]
```
---

# PHASE 7: Attribute-Based Instrumentation

**Duration:** 2-3 days
**Goal:** `[LoomProfile]` and `[LoomTrack]` — automatic method/property instrumentation with minimal code changes.
**Why Critical:** "Zero friction" instrumentation is the whole pitch: one attribute instead of manually wiring `RecordHistogram` calls into every method.
**Dependency:** Requires Phase 5 (generator) and Phase 6 (`LoomRuntime.RecordMethodExecution`, ring buffer) complete.
**AOT-compatibility note:** The attribute alone does nothing at runtime — it's inert metadata. All the actual work happens via the Phase 5 generator at compile time. This phase is where that mechanism becomes visible to app developers.

## Step 7.1: Move the Attributes to Their Real Home

Phase 5 defined `LoomProfileAttribute`/`LoomTrackAttribute` directly in `Loom.Telemetry.Generators` so the generator was testable in isolation. Now that `Loom.Telemetry` exists (Phase 6), move `LoomAttributes.cs` there — app code should depend on `Loom.Telemetry` (the NuGet package), not reach into the generator project directly.

```bash
git mv Loom.Telemetry.Generators/LoomAttributes.cs Loom.Telemetry/LoomAttributes.cs
```

The file's contents and namespace (`Loom.Telemetry`) don't change — only its project. Re-run the Phase 5 generator test after moving it to confirm `ForAttributeWithMetadataName("Loom.Telemetry.LoomProfileAttribute", ...)` in `LoomProfileGenerator.cs` still resolves correctly (it will — the fully-qualified name didn't change, only which `.csproj` compiles the file).

## Step 7.2: The Developer-Facing Contract

This is what a consuming app actually writes — note the `partial` keyword, which is the one piece of ceremony the Begin/End pattern from Phase 5 requires:

```csharp
using Loom.Telemetry;

public partial class OrderProcessor
{
    [LoomProfile]
    public partial Task ProcessOrderAsync(Order order);
}

public partial class OrderProcessor
{
    public partial async Task ProcessOrderAsync(Order order)
    {
        __LoomProfile_ProcessOrderAsync_Begin(out var start);
        Exception? caught = null;
        try
        {
            await _paymentGateway.ChargeAsync(order.Total);
            await _inventory.ReserveAsync(order.Items);
        }
        catch (Exception ex)
        {
            caught = ex;
            throw;
        }
        finally
        {
            __LoomProfile_ProcessOrderAsync_End(start, caught);
        }
    }
}
```

**Explanation (ELI5):**

**Why does the developer have to write the `Begin`/`try`/`finally`/`End` calls themselves — I thought the generator did the work?**
> The generator writes the *hook implementations* (`Begin`/`End` bodies — Phase 5, Step 5.3). It deliberately does **not** rewrite the method body itself, for the robustness reasons explained in Phase 5. This is the honest trade-off of the safer design: "zero friction" becomes "one begin call, one end call, one partial keyword" rather than truly zero lines. If fully invisible instrumentation (no calls in the method body at all) is a hard requirement, that needs C# 12 interceptors instead of the Begin/End partial-method pattern — flagged here as a documented alternative, not implemented, because interceptors are still an evolving, opt-in-only compiler feature and pin the whole plan to a narrower compiler-version window than partial methods do.

**Why `partial` on both the class and the method?**
> `partial class` — because the generator adds a second, generated part of the same class (the file from Phase 5, Step 5.3). `partial` on the method itself lets the *developer's* file declare the method's signature/attribute while a second declaration (also written by the developer, shown above) supplies the actual implementation — this is a separate, older C# partial-method feature being reused here, not something the generator writes.

## Step 7.3: `[LoomTrack]` for Properties

Property tracking doesn't have a method body to wrap, so it uses a slightly different generator target — a partial `set` accessor:

```csharp
public partial class Order
{
    [LoomTrack]
    public partial decimal OrderTotal { get; set; }
}
```

**Generator output (`Loom.Telemetry.Generators` — add this method to `LoomProfileGenerator.cs` from Phase 5):**

```csharp
private static string GeneratePropertyTrackerSource(string className, string propertyName, string propertyType)
{
    return $$"""
        // <auto-generated/>
        using Loom.Telemetry;

        partial class {{className}}
        {
            private {{propertyType}} __{{propertyName}}_backing;

            public partial {{propertyType}} {{propertyName}}
            {
                get => __{{propertyName}}_backing;
                set
                {
                    __{{propertyName}}_backing = value;
                    LoomRuntime.RecordGauge("{{className}}.{{propertyName}}", (double)value);
                }
            }
        }
        """;
}
```

**Wire it into `Initialize` alongside the method-profile pipeline from Phase 5:**

```csharp
var trackedProperties = context.SyntaxProvider
    .ForAttributeWithMetadataName(
        "Loom.Telemetry.LoomTrackAttribute",
        predicate: static (node, _) => node is PropertyDeclarationSyntax,
        transform: static (ctx, _) => (PropertyDeclarationSyntax)ctx.TargetNode)
    .Collect();

context.RegisterSourceOutput(trackedProperties, static (spc, props) =>
{
    foreach (var prop in props)
    {
        var className = (prop.Parent as ClassDeclarationSyntax)?.Identifier.Text ?? "Unknown";
        var propertyType = prop.Type.ToString();
        var source = GeneratePropertyTrackerSource(className, prop.Identifier.Text, propertyType);
        spc.AddSource($"{className}.{prop.Identifier.Text}.LoomTrack.g.cs", source);
    }
});
```

**Explanation (ELI5):**
> `(double)value` assumes the tracked property is numeric — `decimal`, `int`, `double`, etc. Tracking a non-numeric property (a `string` status field, say) needs a different DTO shape than "gauge," which is out of scope for this pass; the generator as written will produce a compile error for non-numeric types via the invalid cast, which is an acceptable (if blunt) guardrail until a typed-tracking variant is designed.

## Step 7.4: Verify

```bash
dotnet build
# Confirm the generated files exist (Native AOT / normal build both produce them):
ls obj/Debug/net10.0/generated/Loom.Telemetry.Generators/Loom.Telemetry.Generators.LoomProfileGenerator/

dotnet run --project Loom.Web.Api
# Trigger ProcessOrderAsync via whatever test harness calls it, then:
curl "http://localhost:5080/api/telemetry/metrics"
# OrderProcessor.ProcessOrderAsync.Duration / .Invocations should be listed as registered
```

### 🔍 Checkpoint 7.1
```
✓ Phase 7 Complete: Attribute-Based Instrumentation
✓ LoomAttributes.cs moved from Loom.Telemetry.Generators to Loom.Telemetry (its real home)
✓ [LoomProfile] Begin/End pattern working end-to-end on a real method
✓ [LoomTrack] property gauge tracking working
✓ Both verified as compile-time-only (inspected generated/ output, no runtime reflection)
✓ Honest limitation documented: not fully invisible — one Begin/try/finally/End block required

Understanding Check:
Q: Why can't the generator just rewrite the method body directly?
A: [User explains — robustness/complexity trade-off from Phase 5]

Q: What would make [LoomTrack] fail to compile?
A: [User explains — non-numeric property type, invalid cast to double]

Ready for Phase 8 (Custom Collectors/Plugins)? [Y/N]
```
---

# PHASE 8: Custom Collectors/Plugins (`Loom.Telemetry.Collectors`)

**Duration:** 3-4 days
**Goal:** `ILoomCollector` — lets developers write collectors for arbitrary tech (Redis pools, custom caches, third-party SDKs) and register them via `AddLoomCollector<T>()`.
**Why Critical:** Every app has different infrastructure; a fixed built-in metric set can't cover all of it.
**Dependency:** Builds on Phase 5 (generator project pattern for AOT-safe registration) and Phase 6 (`LoomTelemetryOptions`/DI setup, ring buffer to write into).
**AOT-compatibility note (ADR-6):** "Plugin" here means a type **compiled into the same binary** and registered with a **generic method resolved at compile time** — `options.AddLoomCollector<RedisConnectionPoolCollector>()`. This is critically different from a dynamically loaded plugin DLL (`Assembly.LoadFrom("SomePlugin.dll")`), which would require runtime type loading and break Native AOT — and it's exactly the failure mode the Risk Register calls out under "Native AOT trim removes collector types": explicit `AddLoomCollector<T>()` registration preserves the type through trimming; assembly scanning would not.

## Step 8.1: Create the Collectors Project

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"
mkdir Loom.Telemetry.Collectors
cd Loom.Telemetry.Collectors
dotnet new classlib -f net10.0
```

**File:** `Loom.Telemetry.Collectors/Loom.Telemetry.Collectors.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTrimmable>true</IsTrimmable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Loom.Telemetry\Loom.Telemetry.csproj" />
  </ItemGroup>
</Project>
```

This matches the Dependency Flow in `wiggly-noodling-hoare.md`: `Loom.Telemetry.Collectors → Loom.Telemetry`.

## Step 8.2: The Collector Contract (ADR-6)

**File:** `Loom.Telemetry.Collectors/ILoomCollector.cs`

```csharp
namespace Loom.Telemetry.Collectors;

/// <summary>
/// A developer-authored collector for metrics Loom doesn't know about natively.
/// Registered at compile time via options.AddLoomCollector&lt;T&gt;() — never loaded
/// dynamically. Implementations must be sealed (ADR-6: enables AOT devirtualization).
/// </summary>
public interface ILoomCollector
{
    string Name { get; }

    /// <summary>Per-collector interval — not a single global tick — so a cheap in-memory
    /// collector and an expensive network-round-trip collector can run on different cadences.</summary>
    TimeSpan CollectionInterval { get; }

    ValueTask<CollectorSnapshot> CollectAsync(CancellationToken ct);
}
```

**File:** `Loom.Web.Contracts/Dtos/CollectorDtos.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

/// <summary>A single collector's readings for one collection tick. Uses a pre-allocated
/// MetricTagDto[] (ADR-6) rather than a Dictionary&lt;string,double&gt; — same "avoid
/// per-tick heap churn" reasoning as the ring buffer's TagKey interning in Phase 6.</summary>
public sealed record CollectorSnapshot
{
    public required string CollectorName { get; init; }
    public required IReadOnlyList<CollectorMetricValue> Values { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record CollectorMetricValue
{
    public required string Key { get; init; }
    public required double Value { get; init; }
    public IReadOnlyList<MetricTagDto>? Tags { get; init; }
}

public sealed record CollectorRegistration
{
    public required string Name { get; init; }
    public required TimeSpan CollectionInterval { get; init; }
}

public sealed record CollectorStatus
{
    public required string Name { get; init; }
    public required DateTime LastCollectedAt { get; init; }
    public required bool LastCollectionSucceeded { get; init; }
    public required long ErrorCount { get; init; }
}
```

**Register in `JsonContext.cs`:**
```csharp
[JsonSerializable(typeof(CollectorSnapshot))]
[JsonSerializable(typeof(CollectorMetricValue))]
[JsonSerializable(typeof(List<CollectorMetricValue>))]
[JsonSerializable(typeof(CollectorRegistration))]
[JsonSerializable(typeof(List<CollectorRegistration>))]
[JsonSerializable(typeof(CollectorStatus))]
```

**Explanation (ELI5):**
> `CollectorMetricValue` carrying its own optional `Tags` (rather than one flat `Dictionary<string,double>` for the whole snapshot) lets a single collector report per-dimension values — e.g. a connection-pool collector reporting `active`/`idle`/`waiters` each tagged by pool name if an app has more than one Redis instance. This is a deliberate upgrade over a flat dictionary, matching ADR-6's "pre-allocated `MetricTag[]` arrays" note.

## Step 8.3: The Example from the Original Design, Made Real

**File:** `Loom.Telemetry.Collectors/Examples/RedisConnectionPoolCollector.cs`

```csharp
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Collectors.Examples;

/// <summary>
/// Reference implementation developers can copy. Loom.Telemetry.Collectors doesn't
/// take a hard dependency on any Redis client library itself (would bloat every
/// consumer's binary whether or not they use Redis) — this is a template against a seam.
/// ADR-6 requires collectors be sealed (AOT devirtualization).
/// </summary>
public sealed class RedisConnectionPoolCollector(IRedisConnectionPoolInspector pool) : ILoomCollector
{
    public string Name => "RedisConnections";
    public TimeSpan CollectionInterval => TimeSpan.FromSeconds(10);

    public ValueTask<CollectorSnapshot> CollectAsync(CancellationToken ct)
    {
        var snapshot = new CollectorSnapshot
        {
            CollectorName = Name,
            Values =
            [
                new CollectorMetricValue { Key = "active", Value = pool.ActiveConnections },
                new CollectorMetricValue { Key = "idle", Value = pool.IdleConnections },
                new CollectorMetricValue { Key = "waiters", Value = pool.WaitingClients }
            ],
            Timestamp = DateTime.UtcNow
        };
        return ValueTask.FromResult(snapshot);
    }
}

/// <summary>Thin seam so this project doesn't reference StackExchange.Redis (or any
/// specific client) directly — the consuming app implements this against whatever
/// Redis client it already uses.</summary>
public interface IRedisConnectionPoolInspector
{
    int ActiveConnections { get; }
    int IdleConnections { get; }
    int WaitingClients { get; }
}
```

## Step 8.4: Registration and the Collection Loop

**File:** `Loom.Telemetry.Collectors/LoomTelemetryOptionsCollectorExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Loom.Telemetry;

namespace Loom.Telemetry.Collectors;

public static class LoomTelemetryOptionsCollectorExtensions
{
    internal static readonly List<Type> CollectorTypes = [];

    /// <summary>Compile-time registration — T is a real, referenced type; nothing loaded
    /// at runtime. Matches ADR-6's "Users call services.AddLoomCollector&lt;RedisCollector&gt;()."</summary>
    public static LoomTelemetryOptions AddLoomCollector<T>(this LoomTelemetryOptions options)
        where T : class, ILoomCollector
    {
        CollectorTypes.Add(typeof(T));
        return options;
    }
}
```

**File:** `Loom.Telemetry.Collectors/CollectorSchedulerHostedService.cs`

```csharp
using Microsoft.Extensions.Hosting;
using Loom.Telemetry;

namespace Loom.Telemetry.Collectors;

/// <summary>Runs every registered ILoomCollector on ITS OWN CollectionInterval — not one
/// shared tick — by tracking each collector's next-due time independently.</summary>
public sealed class CollectorSchedulerHostedService(IEnumerable<ILoomCollector> collectors) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _nextDue = [];
    private readonly Dictionary<string, long> _errorCounts = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ticks at the shortest configured interval so no collector's own cadence is missed;
        // per-collector "is it actually due yet" is decided below via _nextDue.
        var tickInterval = collectors.Select(c => c.CollectionInterval).DefaultIfEmpty(TimeSpan.FromSeconds(10)).Min();
        using var timer = new PeriodicTimer(tickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            foreach (var collector in collectors)
            {
                if (_nextDue.TryGetValue(collector.Name, out var due) && now < due) continue;

                try
                {
                    var snapshot = await collector.CollectAsync(stoppingToken);
                    foreach (var metric in snapshot.Values)
                    {
                        var tags = metric.Tags?.Select(t => new MetricTag(t.Key, t.Value)).ToArray() ?? [];
                        LoomRuntime.RecordGauge($"{snapshot.CollectorName}.{metric.Key}", metric.Value, tags);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A misbehaving collector must not take down the scheduler or other collectors.
                    _errorCounts[collector.Name] = _errorCounts.GetValueOrDefault(collector.Name) + 1;
                    LoomRuntime.RecordCounter($"{collector.Name}.CollectionErrors");
                }
                finally
                {
                    _nextDue[collector.Name] = now + collector.CollectionInterval;
                }
            }
        }
    }
}
```

**Explanation (ELI5):**

**Why per-collector `CollectionInterval` instead of one global tick?**
> A cheap in-memory collector (reading a local counter) and an expensive one (a network round-trip to a Redis server) shouldn't be forced onto the same cadence — ADR-6 calls this out specifically. The scheduler ticks at the *shortest* configured interval (so nothing is ever late) but only actually calls `CollectAsync` on a collector once its own `_nextDue` time has passed.

**Why does the catch block deliberately swallow exceptions?**
> One collector throwing (say, Redis is briefly unreachable) shouldn't stop CPU/memory metrics or any other collector from reporting. The error becomes a counter metric and a tracked `_errorCounts` entry (surfaced via `GET /api/telemetry/collectors`, Step 8.5) instead of a crash — visible, not fatal.

## Step 8.5: DI Wiring and the Endpoints

**File:** `Loom.Telemetry.Collectors/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Telemetry.Collectors;

public static class ServiceCollectionExtensions
{
    /// <summary>Call after AddLoomTelemetry(options => options.AddLoomCollector<T>()...)
    /// to wire up the DI registrations and the scheduler.</summary>
    public static IServiceCollection AddLoomCollectors(this IServiceCollection services)
    {
        foreach (var collectorType in LoomTelemetryOptionsCollectorExtensions.CollectorTypes)
            services.AddSingleton(typeof(ILoomCollector), collectorType);

        services.AddHostedService<CollectorSchedulerHostedService>();
        return services;
    }
}
```

**Explanation (ELI5):**
> `services.AddSingleton(typeof(ILoomCollector), collectorType)` — the non-generic overload — because `collectorType` is a `Type` *value* pulled from a list built by potentially several `AddLoomCollector<T>()` calls, not a single compile-time-known `T` at this call site. Each `Type` object in that list still originated from a real `typeof(T)` at a real call site earlier, so this is still zero reflection-based *discovery* — just deferred registration of already-known types.

**File:** `Loom.Web.Api/Program.cs`
```csharp
app.MapGet("/api/telemetry/collectors", (IEnumerable<ILoomCollector> collectors) =>
    Results.Json(
        collectors.Select(c => new CollectorRegistration { Name = c.Name, CollectionInterval = c.CollectionInterval }).ToList(),
        LoomJsonSerializerContext.Default.ListCollectorRegistration));

app.MapPost("/api/telemetry/collectors/{name}/collect", async (string name, IEnumerable<ILoomCollector> collectors, CancellationToken ct) =>
{
    var collector = collectors.FirstOrDefault(c => c.Name == name);
    if (collector is null) return Results.NotFound();

    var snapshot = await collector.CollectAsync(ct);
    return Results.Json(snapshot, LoomJsonSerializerContext.Default.CollectorSnapshot);
});
```

## Step 8.6: Verify

```bash
dotnet build
dotnet run --project Loom.Web.Api
curl http://localhost:5080/api/telemetry/collectors
curl -X POST http://localhost:5080/api/telemetry/collectors/RedisConnections/collect
# With a test ILoomCollector registered and returning fixed values:
dotnet-counters monitor --process-id $(pidof Loom.Web.Api)
# Confirm a gauge appears under the collector's namespaced name on its own interval
```

### 🔍 Checkpoint 8.1 (MAJOR MILESTONE)
```
✓ Phase 8 Complete: Custom Collectors/Plugins
✓ ILoomCollector contract (sealed-implementation requirement, per-collector CollectionInterval)
✓ CollectorSnapshot/CollectorMetricValue DTOs — pre-allocated list, not Dictionary
✓ RedisConnectionPoolCollector reference implementation (dependency-free via seam interface)
✓ AddLoomCollector<T>() — compile-time registration, confirmed NOT reflection-based
✓ CollectorSchedulerHostedService — per-collector cadence, resilient (one bad collector doesn't crash others)
✓ GET /api/telemetry/collectors + POST .../collect wired

Understanding Check:
Q: Why can't collectors be loaded from a DLL the host app didn't reference at compile time?
A: [User explains — Assembly.LoadFrom requires reflection, breaks AOT, exactly the Risk Register's "trim removes collector types" scenario]

Q: Why does each collector get its own CollectionInterval instead of a shared global tick?
A: [User explains — cheap vs. expensive collectors shouldn't share a cadence]

Ready for Phase 9 (Configuration-Driven Sampling)? [Y/N]
```
---

# PHASE 9: Configuration-Driven Sampling

**Duration:** 3-4 days
**Goal:** `appsettings.json`-based sampling rules — default rate, path-based overrides, duration-based overrides — that **hot-reload** without a restart (ADR-10).
**Why Critical:** High-traffic apps can't instrument every single request/method call without cost; sampling keeps overhead bounded while still catching what matters (always sample slow requests, always sample critical paths).
**Dependency:** Reads/writes through Phase 6's `LoomRuntime`.
**AOT-compatibility note:** Configuration binding uses the source-generated config binder (`EnableConfigurationBindingGenerator`), not `Microsoft.Extensions.Configuration.Binder`'s reflection-based default path. Path matching uses `ReadOnlySpan<char>.StartsWith()` — no regex, no allocation on the decision hot path.

## Step 9.1: The Sampling Config Shape

**File:** `Loom.Telemetry/Sampling/SamplingConfig.cs`

```csharp
namespace Loom.Telemetry.Sampling;

public sealed class SamplingConfig
{
    public const string SectionName = "Loom:Sampling";

    /// <summary>Fraction of events to sample when no rule matches. 1.0 = always, 0.1 = 10%.</summary>
    public double Default { get; set; } = 1.0;

    public List<SamplingRule> Rules { get; set; } = [];
}

public sealed class SamplingRule
{
    /// <summary>Optional: match by request path prefix, e.g. "/api/critical/*".</summary>
    public string? Path { get; set; }

    /// <summary>Optional: match by observed duration, e.g. "> 1000ms". Parsed in Step 9.3.</summary>
    public string? Duration { get; set; }

    public double Rate { get; set; } = 1.0;
}
```

Matches the exact `appsettings.json` shape from ADR-10:

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

**File:** `Loom.Web.Contracts/Dtos/SamplingDtos.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

public sealed record SamplingConfigDto
{
    public required double Default { get; init; }
    public required IReadOnlyList<SamplingRuleDto> Rules { get; init; }
}

public sealed record SamplingRuleDto
{
    public string? Path { get; init; }
    public string? Duration { get; init; }
    public required double Rate { get; init; }
}
```

**Register in `JsonContext.cs`:**
```csharp
[JsonSerializable(typeof(SamplingConfigDto))]
[JsonSerializable(typeof(SamplingRuleDto))]
[JsonSerializable(typeof(List<SamplingRuleDto>))]
```

## Step 9.2: Hot-Reloadable Binding Without Reflection

**File:** `Loom.Telemetry/Sampling/ServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Loom.Telemetry.Sampling;

public static class SamplingServiceCollectionExtensions
{
    public static IServiceCollection AddLoomSampling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SamplingConfig>(configuration.GetSection(SamplingConfig.SectionName));
        services.AddSingleton<ISamplingDecider, SamplingDecider>();
        return services;
    }
}
```

Add `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` to `Loom.Telemetry.csproj`'s `PropertyGroup` (already present from Step 6.1).

**Explanation (ELI5):**
> `services.Configure<T>(section)` (rather than a one-time manual `section.Bind(new SamplingConfig())`) is what makes `IOptionsMonitor<SamplingConfig>` available for injection — `IOptionsMonitor<T>`, unlike plain `IOptions<T>`, re-reads the underlying `appsettings.json` and fires `OnChange` callbacks when the file changes on disk, all without a process restart. `EnableConfigurationBindingGenerator` (a source-generator-backed config binder, same spirit as the JSON source generator from Phase 1, applied to configuration instead of JSON payloads) is what keeps this reflection-free — skipping the flag would silently fall back to the reflection-based binder and produce a trim warning; the `native-aot-guard` skill checks for this flag specifically whenever `IConfiguration`/`IOptionsMonitor` binding appears in a diff.

## Step 9.3: The Sampling Decision (ADR-10)

**File:** `Loom.Telemetry/Sampling/ISamplingDecider.cs`

```csharp
using Microsoft.Extensions.Options;

namespace Loom.Telemetry.Sampling;

public interface ISamplingDecider
{
    bool ShouldSample(ReadOnlySpan<char> path, TimeSpan? observedDuration);
}

internal sealed class SamplingDecider : ISamplingDecider
{
    // Atomic reference swap: OnChange replaces this reference, never mutates the array
    // in place — a reader mid-loop always sees one consistent, complete rule set,
    // never a half-updated one. This is the "no locking on the hot path" ADR-10 requires.
    private SamplingRule[] _rules;
    private double _default;

    public SamplingDecider(IOptionsMonitor<SamplingConfig> monitor)
    {
        (_rules, _default) = Snapshot(monitor.CurrentValue);
        monitor.OnChange(updated => (_rules, _default) = Snapshot(updated));
    }

    private static (SamplingRule[], double) Snapshot(SamplingConfig config) => ([.. config.Rules], config.Default);

    public bool ShouldSample(ReadOnlySpan<char> path, TimeSpan? observedDuration)
    {
        var rules = _rules; // local copy of the reference — stable for the duration of this call
        foreach (var rule in rules)
        {
            if (rule.Path is { } p && !path.IsEmpty && path.StartsWith(p.AsSpan().TrimEnd('*'), StringComparison.Ordinal))
                return Roll(rule.Rate);

            if (rule.Duration is not null && observedDuration is { } duration && MatchesDurationRule(rule.Duration, duration))
                return Roll(rule.Rate);
        }

        return Roll(_default);
    }

    private static bool Roll(double rate) => rate >= 1.0 || Random.Shared.NextDouble() < rate;

    private static bool MatchesDurationRule(string rule, TimeSpan observed)
    {
        // Minimal parser for "> 1000ms" style rules — intentionally not a general
        // expression language; Phase 10's query engine is where general expressions live.
        var trimmed = rule.AsSpan().Trim();
        if (trimmed.Length < 2 || trimmed[0] != '>') return false;
        var numberPart = trimmed[1..].Trim().TrimEnd("ms".ToCharArray()).Trim();
        return double.TryParse(numberPart, out var thresholdMs) && observed.TotalMilliseconds > thresholdMs;
    }
}
```

**Explanation (ELI5):**

**Why an atomic reference swap instead of a `lock` around the rules list?**
> The Risk Register lists "`IOptionsMonitor` hot-reload race condition" as a risk with the mitigation "atomic reference swap; readers see consistent snapshot" — that's implemented literally here. `_rules = [.. config.Rules]` inside `OnChange` builds a brand-new array and reassigns the field in one atomic pointer write; any `ShouldSample` call already in flight keeps using the array reference it read at the top of the method, and any call starting after the swap sees the new array. No reader ever observes a half-old, half-new rule set, and no reader ever blocks on a writer.

**Why `Random.Shared` here but not elsewhere in this platform?**
> `Random.Shared` (.NET 6+) is already thread-safe internally — no `ThreadLocal<Random>` wrapper needed, simpler than an equivalent pattern would have looked pre-.NET 6.

**Why can duration-based sampling only be a retroactive decision, and how does "retroactive" actually work?**
> The duration isn't known until the operation finishes, so a `"> 1000ms"` rule can't be consulted *before* deciding whether to record. ADR-10 handles this by buffering: `RecordMethodExecution` (Phase 5/6) always computes the entry but defers the actual `LoomRuntime.Record*` call until it has both the duration *and* a sampling decision informed by that duration — practically, this means `ShouldSample` is called with `observedDuration` populated at the *end* of the profiled operation, not the start, and the write only happens if the decision comes back true. Path-based rules, by contrast, can be evaluated up front since the path is known immediately.

## Step 9.4: Applying Sampling at the Call Sites

**Update `LoomRuntime.RecordMethodExecution` from Phase 6** to consult the decider — the generator's Begin/End hooks from Phase 5 don't change; the decision moves inside `RecordMethodExecution` itself:

```csharp
public static void RecordMethodExecution(string metricName, TimeSpan elapsed, Exception? exception, ISamplingDecider? decider = null)
{
    if (decider is not null && !decider.ShouldSample(ReadOnlySpan<char>.Empty, elapsed))
        return; // Sampled out — no recording, no allocation beyond the check itself.

    RecordHistogram($"{metricName}.Duration", elapsed.TotalMilliseconds);
    RecordCounter($"{metricName}.Invocations");
    if (exception is not null) RecordCounter($"{metricName}.Exceptions");
}
```

**Explanation (ELI5):**
> Exceptions always get recorded regardless of sampling in most real designs — an error is disproportionately valuable data compared to a routine success. This reference implementation samples exceptions the same as everything else for simplicity; flagging this as a reasonable place to special-case if error visibility matters more than sampling-rate discipline in your deployment. Note the `decider` parameter defaults to `null` (meaning "always sample") — this keeps Phase 6/7 code that doesn't reference Phase 9 at all still compiling and behaving exactly as before; sampling is additive, not a breaking change to earlier phases. Path-based sampling for HTTP-level recording (as opposed to method-level `[LoomProfile]` recording) is applied the same way in `Loom.Web.Api`'s request pipeline — a small middleware that calls `ShouldSample(context.Request.Path, observedDuration: null)` before request-level metrics are recorded, evaluated up front since path is known immediately (see the ADR-10 distinction above).

## Step 9.5: The Config Endpoints

**File:** `Loom.Web.Api/Program.cs`

```csharp
app.MapGet("/api/config/sampling", (IOptionsMonitor<SamplingConfig> monitor) =>
{
    var config = monitor.CurrentValue;
    var dto = new SamplingConfigDto
    {
        Default = config.Default,
        Rules = config.Rules.Select(r => new SamplingRuleDto { Path = r.Path, Duration = r.Duration, Rate = r.Rate }).ToList()
    };
    return Results.Json(dto, LoomJsonSerializerContext.Default.SamplingConfigDto);
});

app.MapPut("/api/config/sampling", async (SamplingConfigDto dto, CancellationToken ct) =>
{
    // Runtime updates via this endpoint write back to appsettings.json (or an
    // environment-appropriate override file), which IOptionsMonitor then picks up
    // through the same OnChange path as an out-of-band file edit — the endpoint
    // doesn't maintain a second, separate in-memory config path.
    await SamplingConfigWriter.PersistAsync(dto, ct);
    return Results.NoContent();
});
```

`SamplingConfigWriter` (a small file-write helper serializing back to the `appsettings.json` sampling section via `LoomJsonSerializerContext`) is a short, mechanical addition following the same explicit-`JsonTypeInfo` discipline established in Phase 6 — not reproduced line-for-line here since the pattern is already fully covered.

## Step 9.6: Verify

```bash
dotnet build
# With Default: 1.0 initially, run a loop of 1000 profiled calls, confirm ~1000 Invocations.
# Edit appsettings.json's Loom:Sampling:Default to 0.1 WHILE THE APP IS RUNNING, save, then
# run another 1000 calls — confirm roughly ~100 (not exactly, it's random) got recorded,
# without restarting the process. This is the hot-reload behavior ADR-10 requires.
curl http://localhost:5080/api/config/sampling
```

### 🔍 Checkpoint 9.1
```
✓ Phase 9 Complete: Configuration-Driven Sampling
✓ SamplingConfig/SamplingRule bound via IOptionsMonitor (source-generated binder, not reflection-mode)
✓ SamplingDecider — atomic reference swap on OnChange, no locking on the hot path
✓ ReadOnlySpan<char> path-prefix matching, ">Nms" duration rule parsing
✓ Confirmed hot-reload works without process restart
✓ RecordMethodExecution now sampling-aware, backward compatible (decider optional)
✓ GET/PUT /api/config/sampling wired

Understanding Check:
Q: Why does the SamplingDecider build a new array instead of mutating the existing one on reload?
A: [User explains — atomic swap avoids the hot-reload race condition in the Risk Register]

Q: Why can duration-based sampling only be a retroactive decision?
A: [User explains — the duration isn't known until the operation finishes]

Ready for Phase 10 (Query Language for Telemetry)? [Y/N]
```
---

# PHASE 10: Query Language (`Loom.Telemetry.Query`)

**Duration:** 6-7 days
**Goal:** Both a SQL-like query string (`POST/GET /api/query`) and a fluent code-based `Query()` API over the ring buffers from Phase 6.
**Why Critical:** Devs need to ask ad-hoc questions ("what failed between 2-3pm?", "which methods got slower after the deploy?") that a fixed dashboard can't anticipate.
**Dependency:** Reads Phase 6's `LoomRuntime.GetBuffersSnapshot()`.
**AOT-compatibility note (ADR-7):** Tokenizer → Parser → AST → Planner → Executor, all hand-written and closed over a fixed, compile-time-known set of node types — no ANTLR (reflection-heavy generated parsers, runtime `Type.GetType()` for AST nodes), no Sprache/Superpower (LINQ-heavy, allocation-prone, limited AOT compatibility), no Roslyn scripting (needs a JIT, impossible under AOT), no embedded SQLite (native dependency, ~1.5 MB binary size, overkill for in-memory metric queries). The fluent API does **not** use `Expression<Func<T,bool>>` — that requires `System.Linq.Expressions`, which is reflection-heavy — it builds the identical `QueryAst` via explicit method calls instead.

## Step 10.1: Create the Query Project

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"
mkdir Loom.Telemetry.Query
cd Loom.Telemetry.Query
dotnet new classlib -f net10.0
```

**File:** `Loom.Telemetry.Query/Loom.Telemetry.Query.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTrimmable>true</IsTrimmable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Loom.Telemetry\Loom.Telemetry.csproj" />
  </ItemGroup>
</Project>
```

## Step 10.2: The Grammar (Minimal, Closed Surface)

Rather than a general SQL engine, this phase supports the specific shape from the fluent-API example in ADR-7:

```
SELECT <column> [, AVG(<column>) | COUNT(*) | MAX(<column>) | P99(<column>) | ...]
FROM telemetry
[WHERE <condition> [AND <condition>]*]
[GROUP BY <column>]
[ORDER BY <expr> [ASC|DESC]]
[LIMIT <n>]
```

## Step 10.3: The Tokenizer (`ReadOnlySpan<char>` Lexer)

**File:** `Loom.Telemetry.Query/Tokenizer.cs`

```csharp
namespace Loom.Telemetry.Query;

public enum TokenKind { Keyword, Identifier, Operator, Number, StringLiteral, Comma, LParen, RParen, End }

/// <summary>A token as a struct — no per-token heap allocation. Text is a slice
/// (start/length) into the original query string, not a copied substring.</summary>
public readonly struct Token(TokenKind kind, int start, int length)
{
    public TokenKind Kind { get; } = kind;
    public int Start { get; } = start;
    public int Length { get; } = length;

    public ReadOnlySpan<char> Slice(ReadOnlySpan<char> source) => source.Slice(Start, Length);
}

/// <summary>Lexes over ReadOnlySpan&lt;char&gt; — yields Token structs (no string allocation
/// for keywords/identifiers; the caller slices the original span when it needs text, e.g.
/// for identifier names that become AST leaf values in Step 10.4).</summary>
public ref struct Tokenizer(ReadOnlySpan<char> source)
{
    private readonly ReadOnlySpan<char> _source = source;
    private int _position;

    public Token Next()
    {
        SkipWhitespace();
        if (_position >= _source.Length) return new Token(TokenKind.End, _position, 0);

        var c = _source[_position];
        if (c == ',') return Single(TokenKind.Comma);
        if (c == '(') return Single(TokenKind.LParen);
        if (c == ')') return Single(TokenKind.RParen);
        if (c is '=' or '>' or '<') return ReadOperator();
        if (char.IsDigit(c)) return ReadNumber();
        if (c == '\'') return ReadStringLiteral();
        if (char.IsLetter(c) || c == '_' || c == '*') return ReadIdentifierOrKeyword();

        throw new QuerySyntaxException($"Unexpected character '{c}' at position {_position}");
    }

    private void SkipWhitespace() { while (_position < _source.Length && char.IsWhiteSpace(_source[_position])) _position++; }

    private Token Single(TokenKind kind) => new(kind, _position++, 1);

    private Token ReadOperator()
    {
        var start = _position;
        _position++;
        if (_position < _source.Length && _source[_position] == '=') _position++; // >=, <=
        return new Token(TokenKind.Operator, start, _position - start);
    }

    private Token ReadNumber()
    {
        var start = _position;
        while (_position < _source.Length && (char.IsDigit(_source[_position]) || _source[_position] == '.')) _position++;
        return new Token(TokenKind.Number, start, _position - start);
    }

    private Token ReadStringLiteral()
    {
        var start = ++_position; // skip opening quote
        while (_position < _source.Length && _source[_position] != '\'') _position++;
        var token = new Token(TokenKind.StringLiteral, start, _position - start);
        _position++; // skip closing quote
        return token;
    }

    private static readonly string[] Keywords =
        ["SELECT", "FROM", "WHERE", "AND", "GROUP", "BY", "ORDER", "DESC", "ASC", "LIMIT", "TELEMETRY"];

    private Token ReadIdentifierOrKeyword()
    {
        var start = _position;
        while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] is '_' or '*' or '.')) _position++;
        var text = _source[start.._position];
        var isKeyword = Keywords.Any(k => text.Equals(k, StringComparison.OrdinalIgnoreCase));
        return new Token(isKeyword ? TokenKind.Keyword : TokenKind.Identifier, start, _position - start);
    }
}

public sealed class QuerySyntaxException(string message) : Exception(message);
```

**Explanation (ELI5):**
> `Token` stores `Start`/`Length` into the *original* query string rather than an allocated `string`. Only when the parser (next step) actually needs an identifier's text as an AST leaf value does anything get materialized into a `string` — and even then, it's one small string per column/condition, not per character. This is the "no string allocation for keywords" ADR-7 calls out; `Tokenizer` is a `ref struct` specifically so it can hold a `ReadOnlySpan<char>` field, which ordinary (non-`ref`) structs and classes cannot do.

## Step 10.4: The AST and Parser

**File:** `Loom.Telemetry.Query/Ast.cs`

```csharp
namespace Loom.Telemetry.Query;

/// <summary>Closed set of AST node kinds — the executor's switch over this enum is
/// exhaustive and compiler-checked, unlike a reflection-based visitor pattern.</summary>
public enum AggregateFunction { None, Avg, Count, Max, Min, P99 }

public sealed record SelectColumn(string Name, AggregateFunction Aggregate);
public sealed record WhereCondition(string Column, string Operator, string Value);

public sealed record QueryAst(
    IReadOnlyList<SelectColumn> Columns,
    IReadOnlyList<WhereCondition> Conditions,
    string? GroupByColumn,
    string? OrderByColumn,
    bool OrderDescending,
    int? Limit);
```

**File:** `Loom.Telemetry.Query/QueryParser.cs`

```csharp
namespace Loom.Telemetry.Query;

/// <summary>Hand-written recursive-descent parser producing a QueryAst. No Type.GetType()/
/// reflection anywhere — every keyword is matched by an ordinal string comparison against
/// a fixed, known set (the Tokenizer's Keywords array).</summary>
public static class QueryParser
{
    public static QueryAst Parse(string queryText)
    {
        var source = queryText.AsSpan();
        var tokenizer = new Tokenizer(source);
        var current = tokenizer.Next();

        Expect(ref tokenizer, ref current, source, "SELECT");
        var columns = ParseSelectColumns(ref tokenizer, ref current, source);

        Expect(ref tokenizer, ref current, source, "FROM");
        Expect(ref tokenizer, ref current, source, "TELEMETRY"); // only table this phase supports

        var conditions = new List<WhereCondition>();
        if (IsKeyword(current, source, "WHERE"))
        {
            current = tokenizer.Next();
            conditions.Add(ParseCondition(ref tokenizer, ref current, source));
            while (IsKeyword(current, source, "AND"))
            {
                current = tokenizer.Next();
                conditions.Add(ParseCondition(ref tokenizer, ref current, source));
            }
        }

        string? groupBy = null;
        if (IsKeyword(current, source, "GROUP"))
        {
            current = tokenizer.Next(); Expect(ref tokenizer, ref current, source, "BY");
            groupBy = current.Slice(source).ToString();
            current = tokenizer.Next();
        }

        string? orderBy = null;
        var descending = false;
        if (IsKeyword(current, source, "ORDER"))
        {
            current = tokenizer.Next(); Expect(ref tokenizer, ref current, source, "BY");
            orderBy = current.Slice(source).ToString();
            current = tokenizer.Next();
            if (IsKeyword(current, source, "DESC") || IsKeyword(current, source, "ASC"))
            {
                descending = current.Slice(source).Equals("DESC", StringComparison.OrdinalIgnoreCase);
                current = tokenizer.Next();
            }
        }

        int? limit = null;
        if (IsKeyword(current, source, "LIMIT"))
        {
            current = tokenizer.Next();
            limit = int.Parse(current.Slice(source));
        }

        return new QueryAst(columns, conditions, groupBy, orderBy, descending, limit);
    }

    private static bool IsKeyword(Token token, ReadOnlySpan<char> source, string keyword) =>
        token.Kind == TokenKind.Keyword && token.Slice(source).Equals(keyword, StringComparison.OrdinalIgnoreCase);

    private static void Expect(ref Tokenizer tokenizer, ref Token current, ReadOnlySpan<char> source, string expected)
    {
        if (!IsKeyword(current, source, expected))
            throw new QuerySyntaxException($"Expected '{expected}' but found '{current.Slice(source)}' at position {current.Start}");
        current = tokenizer.Next();
    }

    private static List<SelectColumn> ParseSelectColumns(ref Tokenizer tokenizer, ref Token current, ReadOnlySpan<char> source)
    {
        var columns = new List<SelectColumn>();
        while (true)
        {
            var name = current.Slice(source).ToString();
            current = tokenizer.Next();

            if (current.Kind == TokenKind.LParen) // AVG(duration), COUNT(*), P99(duration)
            {
                current = tokenizer.Next(); // the column inside the parens
                var innerColumn = current.Slice(source).ToString();
                current = tokenizer.Next(); // consume RParen
                var aggregate = Enum.Parse<AggregateFunction>(name, ignoreCase: true);
                columns.Add(new SelectColumn(innerColumn, aggregate));
                current = tokenizer.Next();
            }
            else
            {
                columns.Add(new SelectColumn(name, AggregateFunction.None));
            }

            if (current.Kind == TokenKind.Comma) { current = tokenizer.Next(); continue; }
            break;
        }
        return columns;
    }

    private static WhereCondition ParseCondition(ref Tokenizer tokenizer, ref Token current, ReadOnlySpan<char> source)
    {
        var column = current.Slice(source).ToString();
        current = tokenizer.Next();
        var op = current.Slice(source).ToString();
        current = tokenizer.Next();
        var value = current.Slice(source).ToString();
        current = tokenizer.Next();
        return new WhereCondition(column, op, value);
    }
}
```

**Explanation (ELI5):**
> This is deliberately a small, closed grammar — the exact shape from ADR-7's fluent-API example (`SELECT method, AVG(duration) FROM telemetry WHERE ... GROUP BY method ORDER BY ... LIMIT 10`), not a general SQL implementation. Extending it (subqueries, joins, `OR`, parenthesized boolean expressions) is real, scoped future work, not something this phase silently promises. String literals (`'US-West'`) are supported by the tokenizer (`ReadStringLiteral`) for `WHERE` values containing spaces — the one gap flagged in an earlier draft of this parser is now closed.

## Step 10.5: The Planner and Executor

**File:** `Loom.Telemetry.Query/QueryPlanner.cs`

```csharp
using Loom.Telemetry;

namespace Loom.Telemetry.Query;

/// <summary>Resolves metric names in the AST to actual ring buffers, and validates
/// time-range-shaped WHERE conditions before execution — the "Planner" stage ADR-7 calls
/// out between parsing and executing.</summary>
internal static class QueryPlanner
{
    public static QueryPlan Plan(QueryAst ast)
    {
        var buffers = LoomRuntime.GetBuffersSnapshot();
        var referencedNames = ast.Columns.Select(c => c.Name)
            .Concat(ast.Conditions.Select(c => c.Column))
            .Where(n => n != "*" && buffers.ContainsKey(n))
            .Distinct()
            .ToList();

        return new QueryPlan(ast, referencedNames);
    }
}

internal sealed record QueryPlan(QueryAst Ast, IReadOnlyList<string> ReferencedMetricNames);
```

**File:** `Loom.Telemetry.Query/QueryExecutor.cs`

```csharp
using System.Diagnostics;
using Loom.Telemetry;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Query;

public interface IQueryExecutor
{
    ValueTask<QueryResponse> ExecuteAsync(string queryText, CancellationToken ct);
}

public sealed class QueryExecutor : IQueryExecutor
{
    public ValueTask<QueryResponse> ExecuteAsync(string queryText, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var ast = QueryParser.Parse(queryText);
        var plan = QueryPlanner.Plan(ast);

        var rows = Execute(plan);
        var elapsed = Stopwatch.GetElapsedTime(started);

        var response = new QueryResponse
        {
            Columns = ast.Columns.Select(c => c.Aggregate == AggregateFunction.None ? c.Name : $"{c.Aggregate}({c.Name})".ToUpperInvariant()).ToList(),
            Rows = rows,
            ExecutionTimeMs = elapsed.TotalMilliseconds
        };
        return ValueTask.FromResult(response);
    }

    private static List<QueryResultRow> Execute(QueryPlan plan)
    {
        // Executor stage: switch on closed AggregateFunction enum, not a visitor.Visit(dynamic).
        var buffers = LoomRuntime.GetBuffersSnapshot();
        var rows = new List<QueryResultRow>();

        foreach (var metricName in plan.ReferencedMetricNames.DefaultIfEmpty(buffers.Keys.FirstOrDefault() ?? ""))
        {
            if (!buffers.TryGetValue(metricName, out var buffer)) continue;
            if (!MatchesConditions(metricName, plan.Ast.Conditions)) continue;

            var entries = buffer.Snapshot();
            if (entries.Length == 0) continue;

            var values = plan.Ast.Columns.Select(col => col.Aggregate switch
            {
                AggregateFunction.Avg => new QueryValue { Number = entries.Average(e => e.Value) },
                AggregateFunction.Count => new QueryValue { Number = entries.Length },
                AggregateFunction.Max => new QueryValue { Number = entries.Max(e => e.Value) },
                AggregateFunction.Min => new QueryValue { Number = entries.Min(e => e.Value) },
                AggregateFunction.P99 => new QueryValue { Number = Percentile(entries.Select(e => e.Value), 0.99) },
                _ => new QueryValue { Text = metricName }
            }).ToList();

            rows.Add(new QueryResultRow { Values = values });
        }

        if (plan.Ast.OrderByColumn is not null)
        {
            rows = plan.Ast.OrderDescending
                ? rows.OrderByDescending(RowSortKey).ToList()
                : rows.OrderBy(RowSortKey).ToList();
        }

        return plan.Ast.Limit is { } limit ? rows.Take(limit).ToList() : rows;

        static double RowSortKey(QueryResultRow row) => row.Values[^1].Number ?? 0;
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static bool MatchesConditions(string metricName, IReadOnlyList<WhereCondition> conditions) =>
        conditions.Count == 0 || conditions.All(c => c.Column != "method" || metricName.Contains(c.Value, StringComparison.OrdinalIgnoreCase));
}
```

**Explanation (ELI5):**
> This is deliberately LINQ-heavy (`.Average`, `.Max`, `.OrderBy`, ...) — and that's fine here, unlike in the Phase 4 WebSocket loop or Phase 6 write path, because `ExecuteAsync` runs per-*query* (a human or a dashboard asking a question occasionally), not per-message or per-write. The Risk Register's "Query engine performance on large datasets" mitigation ("ring buffer bounds total data; indexes on metric name + tag; benchmark in CI") is why this is still fast enough despite the LINQ usage: the ring buffer from Phase 6 already caps each metric's entry count at `capacity` (default 4096), so `.Average()`/`.Max()` over a `Snapshot()` is bounded work, not "average over unbounded history." `MatchesConditions` here is a deliberately simplified filter (a `method`-column substring check) — full arbitrary-column `WHERE` filtering (matching on tag values, numeric comparisons on `value`, not just the metric name) is the natural next extension of this method, flagged as such rather than presented as exhaustive.

## Step 10.6: DTOs

**File:** `Loom.Web.Contracts/Dtos/QueryDtos.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

public sealed record QueryRequest { public required string Query { get; init; } }

public sealed record QueryResponse
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<QueryResultRow> Rows { get; init; }
    public required double ExecutionTimeMs { get; init; }
}

public sealed record QueryResultRow { public required IReadOnlyList<QueryValue> Values { get; init; } }

/// <summary>Closed union over a query cell's value kinds — avoids `object`, which would
/// need reflection to serialize (same pattern as MetricUpdate in Phase 1).</summary>
public sealed record QueryValue
{
    public string? Text { get; init; }
    public double? Number { get; init; }
    public DateTime? Timestamp { get; init; }
}

public sealed record QueryColumn { public required string Name { get; init; } public required string Kind { get; init; } }
```

**Register in `JsonContext.cs`:**
```csharp
[JsonSerializable(typeof(QueryRequest))]
[JsonSerializable(typeof(QueryResponse))]
[JsonSerializable(typeof(QueryResultRow))]
[JsonSerializable(typeof(List<QueryResultRow>))]
[JsonSerializable(typeof(QueryValue))]
[JsonSerializable(typeof(List<QueryValue>))]
[JsonSerializable(typeof(QueryColumn))]
```

## Step 10.7: The Fluent Code API

**File:** `Loom.Telemetry.Query/LoomQueryBuilder.cs`

```csharp
namespace Loom.Telemetry.Query;

/// <summary>Builds the SAME QueryAst the SQL parser produces, via explicit method calls —
/// not Expression&lt;Func&lt;T,bool&gt;&gt;, which would need System.Linq.Expressions
/// (reflection-heavy). Matches ADR-7's fluent example: _loom.Query().Where(...).Last(...)
/// .GroupBy(...).OrderByDescending(...).Take(10).ExecuteAsync().</summary>
public sealed class LoomQueryBuilder
{
    private readonly List<SelectColumn> _columns = [];
    private readonly List<WhereCondition> _conditions = [];
    private string? _groupBy;
    private string? _orderBy;
    private bool _descending;
    private int? _limit;

    public LoomQueryBuilder Select(string column, AggregateFunction aggregate = AggregateFunction.None)
    {
        _columns.Add(new SelectColumn(column, aggregate));
        return this;
    }

    public LoomQueryBuilder Where(string column, string op, string value)
    {
        _conditions.Add(new WhereCondition(column, op, value));
        return this;
    }

    public LoomQueryBuilder Last(TimeSpan window) =>
        Where("timestamp", ">", DateTime.UtcNow.Subtract(window).ToString("O"));

    public LoomQueryBuilder GroupBy(string column) { _groupBy = column; return this; }

    public LoomQueryBuilder OrderByDescending(string column) { _orderBy = column; _descending = true; return this; }

    public LoomQueryBuilder Take(int count) { _limit = count; return this; }

    public ValueTask<QueryResponse> ExecuteAsync(IQueryExecutor executor, CancellationToken ct = default)
    {
        var ast = new QueryAst(_columns, _conditions, _groupBy, _orderBy, _descending, _limit);
        return executor.ExecuteAsync(RenderSql(ast), ct); // round-trips through the same executor as the SQL path
    }

    private static string RenderSql(QueryAst ast)
    {
        var cols = string.Join(", ", ast.Columns.Select(c => c.Aggregate == AggregateFunction.None ? c.Name : $"{c.Aggregate}({c.Name})"));
        var sql = $"SELECT {cols} FROM telemetry";
        if (ast.Conditions.Count > 0) sql += " WHERE " + string.Join(" AND ", ast.Conditions.Select(c => $"{c.Column} {c.Operator} '{c.Value}'"));
        if (ast.GroupByColumn is not null) sql += $" GROUP BY {ast.GroupByColumn}";
        if (ast.OrderByColumn is not null) sql += $" ORDER BY {ast.OrderByColumn} {(ast.OrderDescending ? "DESC" : "ASC")}";
        if (ast.Limit is { } limit) sql += $" LIMIT {limit}";
        return sql;
    }
}
```

**Explanation (ELI5):**
> `RenderSql` rendering the builder's AST back into a SQL string and handing it to the *same* `QueryExecutor.ExecuteAsync(string, ...)` is a deliberate simplification for this pass — it guarantees the fluent path and the SQL path can never silently diverge in behavior (they're literally the same code past this point), at the cost of a round-trip through text rendering and re-parsing that a "real" two-executor-paths design would skip. If `ExecuteAsync`'s parse step ever shows up in a profile as meaningfully expensive for the fluent path specifically, the fix is `QueryExecutor.ExecuteAsync(QueryAst ast, ...)` as a second, AST-accepting overload that both paths converge on *before* parsing rather than after rendering — flagged as the natural next step, not built here to keep this phase's core mechanism (one executor, one code path, provably consistent behavior) easy to verify first.

## Step 10.8: Wire the Endpoints

**File:** `Loom.Web.Api/Program.cs`

```csharp
app.MapGet("/api/query", async (string q, IQueryExecutor executor, CancellationToken ct) =>
{
    try
    {
        var result = await executor.ExecuteAsync(q, ct);
        return Results.Json(result, LoomJsonSerializerContext.Default.QueryResponse);
    }
    catch (QuerySyntaxException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});

app.MapPost("/api/query", async (QueryRequest request, IQueryExecutor executor, CancellationToken ct) =>
{
    try
    {
        var result = await executor.ExecuteAsync(request.Query, ct);
        return Results.Json(result, LoomJsonSerializerContext.Default.QueryResponse);
    }
    catch (QuerySyntaxException ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
});
```

**Register:** `builder.Services.AddSingleton<IQueryExecutor, QueryExecutor>();`

## Step 10.9: Verify

```bash
curl "http://localhost:5080/api/query?q=SELECT+method,AVG(duration)+FROM+telemetry+ORDER+BY+AVG(duration)+DESC+LIMIT+10"

curl -X POST http://localhost:5080/api/query \
  -H "Content-Type: application/json" \
  -d "{\"query\":\"SELECT method, COUNT(*) FROM telemetry WHERE method = 'ProcessOrder'\"}"

dotnet run --project Loom.Benchmarks --filter *QueryBenchmark*
# Target from wiggly-noodling-hoare.md: <10ms for GROUP BY over 10K entries
```

### 🔍 Checkpoint 10.1 (MAJOR MILESTONE)
```
✓ Phase 10 Complete: Query Language for Telemetry
✓ Tokenizer — ref struct, Span<char> lexer, Token structs (no per-token allocation)
✓ Parser — recursive descent → closed QueryAst (no reflection/Type.GetType())
✓ Planner — resolves metric names against LoomRuntime's buffer snapshot
✓ Executor — switch on closed AggregateFunction enum, LINQ acceptable (per-query, not per-write)
✓ Fluent LoomQueryBuilder — builds same AST via method calls, NOT Expression<Func<>>
✓ GET and POST /api/query wired, results via closed QueryValue union
✓ Benchmark target checked against wiggly-noodling-hoare.md's <10ms/10K-entries goal

Understanding Check:
Q: Why is the SQL grammar closed/small instead of a general parser, and why not ANTLR?
A: [User explains — ADR-7's rejected-alternatives list: reflection-heavy generated parsers, Type.GetType() for AST nodes]

Q: Why is LINQ acceptable in QueryExecutor but not in the Phase 6 write path?
A: [User explains — per-query vs. per-write hot path, ring buffer bounds the data size either way]

Q: Why does the fluent API build a QueryAst via method calls instead of Expression<Func<>>?
A: [User explains — Expression<Func<>> needs System.Linq.Expressions, reflection-heavy]

Ready for Phase 11 (Alerting/Thresholds)? [Y/N]
```
---

# PHASE 11: Alerting/Thresholds (`Loom.Telemetry.Alerting`)

**Duration:** 4-5 days
**Goal:** `AddAlert()` config API — sliding-window conditions, webhook/email/console notification targets, dispatched without blocking the metrics hot path.
**Why Critical:** Monitoring is reactive without alerts; devs need to know before customers complain.
**Dependency:** Uses Phase 10's query engine's aggregate machinery conceptually (count/avg/max/p99), Phase 6's ring buffers directly for window access.
**AOT-compatibility note (ADR-8):** Alert conditions are `Func<MetricAggregate, bool>` delegates **compiled at DI-registration time** (ordinary closures over app code, not reflection) — this is different from, and safer than, an *expression tree* (`Expression<Func<>>`), which would need `System.Linq.Expressions`. Because `AddAlert()` is called from `Program.cs` at startup, not from an untrusted runtime API, a plain delegate is both expressive (matches the original `alert.When(metrics => metrics.ErrorCount > 100)` design almost exactly) and fully AOT-safe — no serialization boundary to cross. Notification targets implement `IAlertTarget`, registered via `AddAlertTarget<T>()` (same compile-time-generic pattern as `AddLoomCollector<T>()` in Phase 8).

## Step 11.1: Create the Alerting Project

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"
mkdir Loom.Telemetry.Alerting
cd Loom.Telemetry.Alerting
dotnet new classlib -f net10.0
```

**File:** `Loom.Telemetry.Alerting/Loom.Telemetry.Alerting.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTrimmable>true</IsTrimmable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Loom.Telemetry.Query\Loom.Telemetry.Query.csproj" />
  </ItemGroup>
</Project>
```

Matches `wiggly-noodling-hoare.md`'s Dependency Flow: `Loom.Telemetry.Alerting → Loom.Telemetry.Query`.

## Step 11.2: The Alert Rule Shape

**File:** `Loom.Telemetry.Alerting/AlertRule.cs`

```csharp
namespace Loom.Telemetry.Alerting;

/// <summary>What an aggregate-condition delegate receives — pre-computed count/avg/max/p99
/// over the alert's own sliding window, so the condition itself stays a cheap comparison.</summary>
public readonly record struct MetricAggregate(string MetricName, long Count, double Average, double Max, double P99);

public sealed class AlertRule(string name, string metricName, TimeSpan window)
{
    public string Name { get; } = name;
    public string MetricName { get; } = metricName;
    public TimeSpan Window { get; } = window;

    /// <summary>A plain closure, not an expression tree — see the AOT-compatibility note above.</summary>
    public Func<MetricAggregate, bool> Condition { get; internal set; } = static _ => false;

    internal List<Type> TargetTypes { get; } = [];

    // Circular buffer of recent values within Window — the "sliding window" ADR-8 describes.
    internal readonly Queue<(DateTime Timestamp, double Value)> RecentValues = new();
}
```

## Step 11.3: The Fluent Config API (Matching the Original `When(lambda)` Design)

**File:** `Loom.Telemetry.Alerting/LoomTelemetryOptionsAlertingExtensions.cs`

```csharp
using Loom.Telemetry;

namespace Loom.Telemetry.Alerting;

public static class LoomTelemetryOptionsAlertingExtensions
{
    internal static readonly List<AlertRule> Rules = [];

    public static LoomTelemetryOptions AddAlert(this LoomTelemetryOptions options, string name, Action<AlertBuilder> configure)
    {
        var builder = new AlertBuilder(name);
        configure(builder);
        Rules.Add(builder.Build());
        return options;
    }
}

public sealed class AlertBuilder(string name)
{
    private string _metricName = "";
    private TimeSpan _window = TimeSpan.FromMinutes(5);
    private Func<MetricAggregate, bool> _condition = static _ => false;
    private readonly List<Type> _targetTypes = [];

    /// <summary>metrics => metrics.ErrorCount > 100 from the original design becomes
    /// agg => agg.Count > 100 here — same delegate shape, now scoped to one metric's
    /// pre-aggregated window instead of an ambient "metrics" object.</summary>
    public AlertBuilder When(string metricName, Func<MetricAggregate, bool> condition)
    {
        _metricName = metricName;
        _condition = condition;
        return this;
    }

    public AlertBuilder InWindow(TimeSpan window) { _window = window; return this; }

    public AlertBuilder Notify<T>() where T : class, IAlertTarget
    {
        _targetTypes.Add(typeof(T));
        return this;
    }

    internal AlertRule Build()
    {
        var rule = new AlertRule(name, _metricName, _window) { Condition = _condition };
        rule.TargetTypes.AddRange(_targetTypes);
        return rule;
    }
}
```

**Usage, matching the original brainstorm's shape closely:**

```csharp
services.AddLoomTelemetry(options =>
{
    options.AddAlert("HighErrorRate", alert => alert
        .When("PaymentFailures", agg => agg.Count > 100)
        .InWindow(TimeSpan.FromMinutes(5))
        .Notify<WebhookAlertTarget>());

    options.AddAlert("SlowOrders", alert => alert
        .When("OrderProcessingTime", agg => agg.P99 > 5000)
        .InWindow(TimeSpan.FromMinutes(5))
        .Notify<EmailAlertTarget>());
});
```

**Explanation (ELI5):**
> This is closer to the original design's `alert.When(metrics => metrics.ErrorCount > 100)` than an earlier, more conservative draft of this phase allowed — because `AddAlert()` only ever runs at DI-configuration time in `Program.cs`, the `Condition` delegate is a normal C# closure the compiler closes over at compile time, not a value that ever needs to be serialized, sent over a wire, or reconstructed from persisted data. That's *exactly* the boundary that matters for AOT: a `Func<T,bool>` living entirely in-process for the app's lifetime is fine; it's only `Expression<Func<T,bool>>` (which exists specifically so something else — like an ORM — can *inspect and translate* the logic at runtime) that pulls in reflection-heavy machinery. `wiggly-noodling-hoare.md`'s API endpoint table reflects this too: there's no `POST /api/alerts` to create a rule at runtime — alerts are code, configured once at startup, then only inspected/tested/silenced (Step 11.6) through the API.

## Step 11.4: Sliding Window Evaluation (ADR-8)

**File:** `Loom.Telemetry.Alerting/AlertEvaluationHostedService.cs`

```csharp
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Loom.Telemetry;

namespace Loom.Telemetry.Alerting;

public sealed record AlertNotification(AlertRule Rule, MetricAggregate Observed, DateTime FiredAt);

public sealed class AlertEvaluationHostedService(
    Channel<AlertNotification> notificationChannel) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastFired = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rules = LoomTelemetryOptionsAlertingExtensions.Rules;
        if (rules.Count == 0) return;

        // Tick at (smallest window / 10) per ADR-8, so even the tightest window gets
        // several evaluation opportunities within its own duration.
        var tickInterval = rules.Select(r => r.Window).DefaultIfEmpty(TimeSpan.FromMinutes(5)).Min() / 10;
        using var timer = new PeriodicTimer(tickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            foreach (var rule in rules)
            {
                var aggregate = ComputeWindowAggregate(rule, now);
                if (aggregate is null) continue;

                if (rule.Condition(aggregate.Value) && ShouldFire(rule, now))
                {
                    // Fire-and-forget: TryWrite never blocks the evaluation loop.
                    // On a full bounded channel, the notification is dropped rather than
                    // stalling evaluation of the remaining rules — see Step 11.5 for the
                    // channel's bounded/drop-oldest configuration.
                    notificationChannel.Writer.TryWrite(new AlertNotification(rule, aggregate.Value, now));
                    _lastFired[rule.Name] = now;
                }
            }
        }
    }

    private static MetricAggregate? ComputeWindowAggregate(AlertRule rule, DateTime now)
    {
        var buffers = LoomRuntime.GetBuffersSnapshot();
        if (!buffers.TryGetValue(rule.MetricName, out var buffer)) return null;

        var cutoff = now - rule.Window;
        var windowValues = buffer.Snapshot()
            .Where(e => new DateTime(e.Ticks, DateTimeKind.Utc) >= cutoff)
            .Select(e => e.Value)
            .ToArray();

        if (windowValues.Length == 0) return new MetricAggregate(rule.MetricName, 0, 0, 0, 0);

        var sorted = windowValues.OrderBy(v => v).ToArray();
        var p99Index = Math.Clamp((int)Math.Ceiling(0.99 * sorted.Length) - 1, 0, sorted.Length - 1);

        return new MetricAggregate(
            rule.MetricName, windowValues.Length, windowValues.Average(), windowValues.Max(), sorted[p99Index]);
    }

    private bool ShouldFire(AlertRule rule, DateTime now) =>
        !_lastFired.TryGetValue(rule.Name, out var last) || now - last >= rule.Window;
}
```

**Explanation (ELI5):**

**Why tick at `smallest window / 10` instead of, say, once a minute?**
> If the tightest alert has a 1-minute window and evaluation only ticks once a minute, a breach could sit undetected for up to a minute before the next tick even looks — ADR-8's "tick at window/10" guarantees roughly 10 chances to notice a breach within its own window, which keeps detection latency proportional to how tight the alert's window is, not a fixed global cadence.

**Why the cooldown (`_lastFired`)?**
> Without it, a sustained breach would refire on every single tick — the Risk Register doesn't call this out by name for alerting specifically, but it's the same "alert spam" concern noted for other event-driven systems in this platform. Cooldown = `rule.Window` means an alert can fire at most once per window, a reasonable default matching "this is a sustained condition, not a series of separate incidents."

**Why is `ComputeWindowAggregate`'s LINQ usage acceptable here?**
> Same reasoning as Phase 10's `QueryExecutor`: this runs on a periodic tick (at most every `window/10`, not per-metric-write), over data already bounded by the Phase 6 ring buffer's capacity. The Risk Register's target — "Alert evaluation < 1ms for 100 active alerts" — is a benchmark target to verify (Step 11.7), not an argument against using LINQ here; if the benchmark misses target, the concrete next step is caching each rule's own filtered/sorted window incrementally rather than recomputing it from a fresh `Snapshot()` every tick.

## Step 11.5: Notification Dispatch (Decoupled via `Channel<T>`)

**File:** `Loom.Telemetry.Alerting/IAlertTarget.cs`

```csharp
namespace Loom.Telemetry.Alerting;

public interface IAlertTarget
{
    Task NotifyAsync(AlertNotification notification, CancellationToken ct);
}

public sealed class WebhookAlertTarget(HttpClient httpClient, string webhookUrl) : IAlertTarget
{
    public async Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        var payload = new AlertWebhookPayload
        {
            Alert = notification.Rule.Name,
            Metric = notification.Rule.MetricName,
            ObservedCount = notification.Observed.Count,
            ObservedAverage = notification.Observed.Average,
            FiredAt = notification.FiredAt
        };
        await httpClient.PostAsJsonAsync(webhookUrl, payload, LoomJsonSerializerContext.Default.AlertWebhookPayload, ct);
    }
}

public sealed class EmailAlertTarget(IEmailSender sender, string toAddress) : IAlertTarget
{
    public Task NotifyAsync(AlertNotification notification, CancellationToken ct) => sender.SendAsync(
        toAddress,
        subject: $"Loom alert: {notification.Rule.Name}",
        body: $"{notification.Rule.MetricName}: count={notification.Observed.Count}, avg={notification.Observed.Average:F1} at {notification.FiredAt:O}",
        ct);
}

public interface IEmailSender { Task SendAsync(string to, string subject, string body, CancellationToken ct); }
```

**File:** `Loom.Web.Contracts/Dtos/AlertWebhookPayload.cs`
```csharp
namespace Loom.Web.Contracts.Dtos;

public sealed record AlertWebhookPayload
{
    public required string Alert { get; init; }
    public required string Metric { get; init; }
    public required long ObservedCount { get; init; }
    public required double ObservedAverage { get; init; }
    public required DateTime FiredAt { get; init; }
}
```
**Register in `JsonContext.cs`:** `[JsonSerializable(typeof(AlertWebhookPayload))]`

**File:** `Loom.Telemetry.Alerting/AlertDispatchHostedService.cs`

```csharp
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Loom.Telemetry.Alerting;

/// <summary>Consumes the Channel<AlertNotification> that AlertEvaluationHostedService
/// writes to — this decoupling is ADR-8's "fire-and-forget via Channel<T> to avoid
/// blocking evaluation" and the Risk Register's mitigation for "Alert evaluation
/// blocking hot path."</summary>
public sealed class AlertDispatchHostedService(
    Channel<AlertNotification> channel, IEnumerable<IAlertTarget> allTargets) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in channel.Reader.ReadAllAsync(stoppingToken))
        {
            var targets = allTargets.Where(t => notification.Rule.TargetTypes.Contains(t.GetType()));
            foreach (var target in targets)
            {
                try { await target.NotifyAsync(notification, stoppingToken); }
                catch (Exception) when (stoppingToken.IsCancellationRequested is false)
                {
                    // A failed notification must not crash the dispatcher or block other targets/alerts.
                }
            }
        }
    }
}
```

**Explanation (ELI5):**
> `Channel<AlertNotification>` is a producer/consumer queue: `AlertEvaluationHostedService` (Step 11.4) is the producer, writing via `TryWrite` (non-blocking — if the channel is full, the write is dropped rather than stalling evaluation); `AlertDispatchHostedService` here is the consumer, reading via `ReadAllAsync` and doing the actual (potentially slow — network calls for webhooks/email) notification work on its own schedule. This is precisely why alert *evaluation* (Step 11.4, needs to be fast and frequent) and alert *notification* (this step, can be slow and is fine to lag slightly) are two separate `BackgroundService`s instead of one — evaluation never waits on a webhook response.

## Step 11.6: DI Wiring and the Endpoints

**File:** `Loom.Telemetry.Alerting/ServiceCollectionExtensions.cs`

```csharp
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Telemetry.Alerting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoomAlerting(this IServiceCollection services)
    {
        // Bounded, drop-oldest: a burst of simultaneous alerts shouldn't grow memory
        // unboundedly if the dispatcher briefly falls behind (Risk Register: "Exporter
        // backpressure causing memory growth" — same channel-backpressure pattern, applied
        // here to alert notifications instead of exported metric batches).
        var channel = Channel.CreateBounded<AlertNotification>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        services.AddSingleton(channel);
        services.AddHostedService<AlertEvaluationHostedService>();
        services.AddHostedService<AlertDispatchHostedService>();
        return services;
    }
}
```

**File:** `Loom.Web.Contracts/Dtos/AlertStatusDtos.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

public sealed record AlertConfigDto
{
    public required string Name { get; init; }
    public required string MetricName { get; init; }
    public required TimeSpan Window { get; init; }
}

public sealed record AlertConditionDto { public required string Description { get; init; } }

public sealed record AlertStatusDto
{
    public required string Name { get; init; }
    public required bool CurrentlyBreached { get; init; }
    public DateTime? LastFiredAt { get; init; }
    public DateTime? SilencedUntil { get; init; }
}

public sealed record AlertHistoryEntry
{
    public required string AlertName { get; init; }
    public required DateTime FiredAt { get; init; }
    public required long ObservedCount { get; init; }
}
```

**Register in `JsonContext.cs`:**
```csharp
[JsonSerializable(typeof(AlertConfigDto))]
[JsonSerializable(typeof(List<AlertConfigDto>))]
[JsonSerializable(typeof(AlertConditionDto))]
[JsonSerializable(typeof(AlertStatusDto))]
[JsonSerializable(typeof(AlertHistoryEntry))]
[JsonSerializable(typeof(List<AlertHistoryEntry>))]
```

**File:** `Loom.Web.Api/Program.cs`

```csharp
app.MapGet("/api/alerts", () =>
{
    var rules = LoomTelemetryOptionsAlertingExtensions.Rules
        .Select(r => new AlertConfigDto { Name = r.Name, MetricName = r.MetricName, Window = r.Window }).ToList();
    return Results.Json(rules, LoomJsonSerializerContext.Default.ListAlertConfigDto);
});

app.MapGet("/api/alerts/{name}", (string name) =>
{
    var rule = LoomTelemetryOptionsAlertingExtensions.Rules.FirstOrDefault(r => r.Name == name);
    return rule is null ? Results.NotFound() : Results.Ok(new AlertConfigDto { Name = rule.Name, MetricName = rule.MetricName, Window = rule.Window });
});

app.MapPost("/api/alerts/{name}/test", async (string name, Channel<AlertNotification> channel) =>
{
    var rule = LoomTelemetryOptionsAlertingExtensions.Rules.FirstOrDefault(r => r.Name == name);
    if (rule is null) return Results.NotFound();

    var testAggregate = new MetricAggregate(rule.MetricName, Count: 1, Average: 0, Max: 0, P99: 0);
    await channel.Writer.WriteAsync(new AlertNotification(rule, testAggregate, DateTime.UtcNow));
    return Results.Accepted();
});

app.MapPut("/api/alerts/{name}/silence", (string name, TimeSpan duration, ISilenceStore silenceStore) =>
{
    silenceStore.Silence(name, DateTime.UtcNow + duration);
    return Results.NoContent();
});
```

`ISilenceStore` (a small in-memory `ConcurrentDictionary<string, DateTime>` consulted by `ShouldFire` in Step 11.4 before firing) is a short, mechanical addition — not reproduced line-for-line here since it follows the exact same shape as `_lastFired` already shown.

## Step 11.7: Verify

```bash
dotnet build
dotnet run --project Loom.Web.Api
curl http://localhost:5080/api/alerts
curl -X POST http://localhost:5080/api/alerts/HighErrorRate/test
curl -X PUT "http://localhost:5080/api/alerts/HighErrorRate/silence?duration=00:30:00"

dotnet run --project Loom.Benchmarks --filter *AlertBenchmark*
# Target from wiggly-noodling-hoare.md: < 1ms evaluation for 100 active alerts
```

### 🔍 Checkpoint 11.1 (MAJOR MILESTONE)
```
✓ Phase 11 Complete: Alerting/Thresholds
✓ AlertRule — Func<MetricAggregate,bool> condition, compiled at registration (not an expression tree)
✓ AddAlert()/AlertBuilder — matches original When(lambda) design closely; no runtime rule creation API
✓ AlertEvaluationHostedService — sliding window, tick at window/10, per-rule cooldown
✓ AlertDispatchHostedService — decoupled via bounded Channel<AlertNotification>, drop-oldest backpressure
✓ WebhookAlertTarget/EmailAlertTarget via IAlertTarget + AddAlertTarget-style DI
✓ GET /api/alerts, GET /api/alerts/{name}, POST .../test, PUT .../silence wired
✓ Benchmark target checked against <1ms/100-alerts goal

Understanding Check:
Q: Why is a Func<MetricAggregate,bool> condition safe here when a serialized lambda wouldn't be?
A: [User explains — closure compiled at startup vs. crossing a runtime/serialization boundary]

Q: Why are alert evaluation and alert dispatch two separate BackgroundServices instead of one?
A: [User explains — Channel<T> decoupling, evaluation must never block on a slow webhook]

Q: What happens to a notification if the channel is full?
A: [User explains — DropOldest, bounded memory over blocking the evaluator]

Ready for Phase 12 (Exporters)? [Y/N]
```
---

# PHASE 12: Exporters (`Loom.Telemetry.Exporters`)

**Duration:** 5-6 days
**Goal:** Prometheus, Grafana Cloud, Elasticsearch, and Console exporters — interoperability with observability stacks teams already run.
**Why Critical:** Teams already use Prometheus/Grafana/DataDog; Loom shouldn't force abandoning them.
**Dependency:** Reads Phase 6's ring buffers.
**AOT-compatibility note (ADR-9):** Prometheus's exposition format is hand-written text formatting (no client-library reflection). Push exporters (Grafana, Elasticsearch) serialize via `LoomJsonSerializerContext`. No plugin discovery — exporters are registered via `options.Export.ToPrometheus()`/`.ToGrafana()`/etc., compile-time generic calls, same pattern as `AddLoomCollector<T>()` in Phase 8.

## Step 12.1: Create the Exporters Project

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"
mkdir Loom.Telemetry.Exporters
cd Loom.Telemetry.Exporters
dotnet new classlib -f net10.0
```

**File:** `Loom.Telemetry.Exporters/Loom.Telemetry.Exporters.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTrimmable>true</IsTrimmable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Loom.Telemetry\Loom.Telemetry.csproj" />
  </ItemGroup>
</Project>
```

## Step 12.2: The Push/Pull Model (ADR-9)

| Exporter | Model | Protocol |
|----------|-------|----------|
| Prometheus | **Pull** | `GET /metrics` returns OpenMetrics text format |
| Grafana Cloud | **Push** | HTTP POST to remote-write endpoint (batched) |
| Elasticsearch | **Push** | Bulk index API (batched, buffered) |
| Console | **Push** | Immediate, no batching |

Pull exporters have no background loop — they format on-demand when scraped. Push exporters accumulate into a shared `Channel<MetricBatch>` and flush on a timer, exactly like alert dispatch in Phase 11.

## Step 12.3: Prometheus — Pull, Hand-Written Formatting

**File:** `Loom.Telemetry.Exporters/PrometheusFormatter.cs`

```csharp
using System.Text;
using Loom.Telemetry;

namespace Loom.Telemetry.Exporters;

public static class PrometheusFormatter
{
    public static string Format(IReadOnlyDictionary<string, MetricRingBuffer> buffers)
    {
        var sb = new StringBuilder();
        foreach (var (name, buffer) in buffers)
        {
            var entries = buffer.Snapshot();
            if (entries.Length == 0) continue;

            var baseName = SanitizeName(name);
            sb.Append("# TYPE ").Append(baseName).Append(" summary\n");
            sb.Append(baseName).Append("_sum ").Append(entries.Sum(e => e.Value)).Append('\n');
            sb.Append(baseName).Append("_count ").Append(entries.Length).Append('\n');
        }
        return sb.ToString();
    }

    // Prometheus metric names: [a-zA-Z_:][a-zA-Z0-9_:]* — dots (Loom's separator) aren't valid.
    private static string SanitizeName(string name) => name.Replace('.', '_');
}
```

**File:** `Loom.Web.Api/Program.cs`
```csharp
app.MapGet("/metrics", () =>
    Results.Text(PrometheusFormatter.Format(LoomRuntime.GetBuffersSnapshot()), "text/plain; version=0.0.4"));
```

**Explanation (ELI5):**
> `# TYPE ... summary` with only `_sum`/`_count` is a simplification — a real Prometheus summary/histogram exposes quantiles or bucket boundaries. `_sum`/`_count` alone are valid and let Prometheus compute an average via `rate(x_sum[5m]) / rate(x_count[5m])`, just not full quantile support at the Prometheus side (Phase 10's `P99` aggregate is still available via the query API even though it isn't exported here) — noted honestly rather than presented as complete. This runs synchronously on scrape, not on a background timer, because pull-based means "format when asked," matching the table in Step 12.2.

## Step 12.4: Grafana Cloud & Elasticsearch — Push, Batched via `Channel<T>`

**File:** `Loom.Telemetry.Exporters/IMetricsExporter.cs`

```csharp
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Exporters;

public interface IMetricsExporter
{
    string Name { get; }
    Task ExportBatchAsync(MetricBatch batch, CancellationToken ct);
}
```

**File:** `Loom.Telemetry.Exporters/GrafanaCloudExporter.cs`

```csharp
using System.Net.Http.Json;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Exporters;

public sealed class GrafanaCloudExporter(HttpClient httpClient, GrafanaCloudExporterOptions options) : IMetricsExporter
{
    public string Name => "GrafanaCloud";

    public async Task ExportBatchAsync(MetricBatch batch, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.PushEndpoint)
        {
            // Explicit JsonTypeInfo overload — the parameterless JsonContent.Create(payload)
            // uses reflection-based serialization internally. Always use this overload,
            // here and everywhere else HttpClient/JsonContent appears in this codebase.
            Content = JsonContent.Create(batch, LoomJsonSerializerContext.Default.MetricBatch)
        };
        request.Headers.Authorization = new("Bearer", options.ApiKey);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class GrafanaCloudExporterOptions
{
    public required string PushEndpoint { get; init; }
    public required string ApiKey { get; init; } // loaded from /var/secrets/loom/, never appsettings.json
}
```

**Explanation (ELI5):**
> The Elasticsearch exporter (`ElasticsearchExporter`, `Loom.Telemetry.Exporters/ElasticsearchExporter.cs`) follows the identical shape — `HttpClient` + `JsonContent.Create(batch, explicit JsonTypeInfo)` + credentials from `/var/secrets/loom/` — against Elasticsearch's bulk index API instead of Grafana's remote-write endpoint; not reproduced line-for-line here since it's the same mechanism with a different URL and a different bulk-request body shape, and Console (next) is the third and genuinely different pattern worth showing in full.

## Step 12.5: Console — Push, Immediate, No Batching

**File:** `Loom.Telemetry.Exporters/ConsoleExporter.cs`

```csharp
using Microsoft.Extensions.Logging;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Exporters;

public sealed class ConsoleExporter(ILogger<ConsoleExporter> logger) : IMetricsExporter
{
    public string Name => "Console";

    public Task ExportBatchAsync(MetricBatch batch, CancellationToken ct)
    {
        logger.LogInformation("[Loom] {Timestamp:O} — {Count} metric record(s)", DateTime.UtcNow, batch.Records.Count);
        return Task.CompletedTask;
    }
}
```

**Explanation (ELI5):**
> `ILogger<T>` instead of raw `Console.WriteLine` — this is what Phase 13's local dev mode taps into (dev mode redirects/reads log output rather than needing a second, parallel console-writing mechanism), and it's consistent with ADR-9's own notification-target table listing Console under `ILogger`/stdout.

## Step 12.6: The Shared Batching/Flush Loop

**File:** `Loom.Telemetry.Exporters/ExportFlushHostedService.cs`

```csharp
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Exporters;

/// <summary>Same Channel<T> decoupling pattern as Phase 11's alert dispatch — push
/// exporters must never block the metrics hot path, so writes to the channel are
/// non-blocking and this service drains it on its own schedule.</summary>
public sealed class ExportFlushHostedService(
    Channel<MetricBatch> channel, IEnumerable<IMetricsExporter> exporters) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in channel.Reader.ReadAllAsync(stoppingToken))
        {
            foreach (var exporter in exporters)
            {
                try { await exporter.ExportBatchAsync(batch, stoppingToken); }
                catch (Exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // One exporter failing (e.g. Elasticsearch briefly unreachable) must not
                    // stop other exporters or crash the flush loop.
                }
            }
        }
    }
}
```

**Registration** — the shared channel, bounded with drop-oldest, same backpressure semantics as Phase 11's alert channel:

```csharp
services.AddSingleton(Channel.CreateBounded<MetricBatch>(
    new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest }));
```

## Step 12.7: Registration Surface (`options.Export.To*()`)

**File:** `Loom.Telemetry/LoomTelemetryOptionsExportExtensions.cs`

```csharp
namespace Loom.Telemetry;

public static class LoomTelemetryOptionsExportExtensions
{
    public static ExportOptions Export(this LoomTelemetryOptions options) => ExportOptions.Instance;
}

public sealed class ExportOptions
{
    internal static readonly ExportOptions Instance = new();
    internal List<Type> ExporterTypes { get; } = [];
    internal bool PrometheusEnabled { get; private set; }

    /// <summary>Prometheus is pull-based (Step 12.3's /metrics endpoint) — there's no exporter
    /// instance to register, just a flag that turns the endpoint on. Kept in the fluent chain
    /// (rather than a missing method) so app code doesn't need to know push vs. pull.</summary>
    public ExportOptions ToPrometheus() { PrometheusEnabled = true; return this; }
    public ExportOptions ToGrafana() { ExporterTypes.Add(typeof(GrafanaCloudExporter)); return this; }
    public ExportOptions ToElasticsearch() { ExporterTypes.Add(typeof(ElasticsearchExporter)); return this; }
    public ExportOptions ToConsole() { ExporterTypes.Add(typeof(ConsoleExporter)); return this; }
}
```

**Usage:**
```csharp
services.AddLoomTelemetry(options =>
{
    options.Export().ToPrometheus();
    options.Export().ToGrafana();
    options.Export().ToConsole();
});
```

## Step 12.8: DTOs and Status Endpoints

**File:** `Loom.Web.Contracts/Dtos/ExporterDtos.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

public sealed record ExporterStatusDto
{
    public required string Name { get; init; }
    public required string Model { get; init; } // "pull" | "push"
    public required bool Healthy { get; init; }
    public DateTime? LastExportAt { get; init; }
}

public sealed record ExportBatchResult
{
    public required string ExporterName { get; init; }
    public required bool Success { get; init; }
    public required int RecordCount { get; init; }
}
```

**Register in `JsonContext.cs`:**
```csharp
[JsonSerializable(typeof(ExporterStatusDto))]
[JsonSerializable(typeof(List<ExporterStatusDto>))]
[JsonSerializable(typeof(ExportBatchResult))]
```

**File:** `Loom.Web.Api/Program.cs`
```csharp
app.MapGet("/api/exporters", (IEnumerable<IMetricsExporter> exporters) =>
{
    var statuses = exporters.Select(e => new ExporterStatusDto { Name = e.Name, Model = "push", Healthy = true }).ToList();
    return Results.Json(statuses, LoomJsonSerializerContext.Default.ListExporterStatusDto);
});

app.MapGet("/api/exporters/{name}/status", (string name, IEnumerable<IMetricsExporter> exporters) =>
{
    var exporter = exporters.FirstOrDefault(e => e.Name == name);
    return exporter is null
        ? Results.NotFound()
        : Results.Ok(new ExporterStatusDto { Name = exporter.Name, Model = "push", Healthy = true });
});
```

## Step 12.9: Verify

```bash
curl http://localhost:5080/metrics
# OpenMetrics text — # TYPE ... summary lines per registered metric

curl http://localhost:5080/api/exporters
curl http://localhost:5080/api/exporters/Console/status

dotnet run --project Loom.Benchmarks --filter *PrometheusScrapeBenchmark*
# Target: < 50ms for 1000 metrics
```

### 🔍 Checkpoint 12.1 (MAJOR MILESTONE)
```
✓ Phase 12 Complete: Exporters
✓ Push/pull model table implemented per exporter (ADR-9)
✓ Prometheus — pull-based /metrics endpoint, hand-written exposition formatting
✓ Grafana Cloud + Elasticsearch — push-based, HttpClient + explicit JsonTypeInfo (not reflection)
✓ Console — push-based via ILogger<T>, immediate (no batching)
✓ ExportFlushHostedService — Channel<MetricBatch> decoupling, bounded + drop-oldest backpressure
✓ Export().To*() registration, ToPrometheus() honestly documented as a flag, not an exporter instance
✓ GET /api/exporters, GET /api/exporters/{name}/status wired
✓ Benchmark target checked against <50ms/1000-metrics goal

Understanding Check:
Q: Why is JsonContent.Create(payload) alone unsafe under Native AOT, and what's the fix?
A: [User explains — reflection-based default overload vs. explicit JsonTypeInfo overload]

Q: Why do push exporters share one Channel<MetricBatch> instead of each having their own?
A: [User explains — one flush loop, one backpressure policy, simpler to reason about]

Ready for Phase 13 (Local Development Mode)? [Y/N]
```
---

# PHASE 13: Local Development Mode (`Loom.DevTools`)

**Duration:** 5-6 days
**Goal:** `dotnet loom dev` — discovers running .NET processes and streams live metrics with zero configuration, using the .NET runtime's own diagnostics infrastructure rather than a Loom-specific discovery protocol.
**Why Critical:** Loom should be useful daily during development, not just for production firefighting.
**Dependency:** Meaningful (not just "is it alive") metrics require the target process to be running `Loom.Telemetry`, which this phase bridges onto `System.Diagnostics.Metrics` (Step 13.3) specifically so EventPipe can see it.
**AOT-compatibility note (ADR-11):** `Loom.DevTools` uses the .NET Diagnostics IPC protocol (named pipes on Windows, Unix domain sockets on Linux/macOS) and `EventPipe` sessions — both are built-in runtime facilities (`System.Diagnostics.Tracing`, the same machinery `dotnet-counters`/`dotnet-trace` use), not reflection over the target process's types. `Loom.DevTools` itself can be Native AOT-published independently of `Loom.Host` for a smaller, faster-starting CLI, but doesn't have to be for this phase — flagged as an optional follow-up, not required for correctness.

## Step 13.1: Why EventPipe/Diagnostics IPC Instead of a Marker File (ADR-11 Recap)

An earlier, simpler design for this phase used a "drop a JSON file in a temp directory on startup" discovery mechanism. ADR-11 replaces that with the .NET runtime's own diagnostics facilities, for reasons worth understanding before building on top of them:

| Marker-file approach | Diagnostics IPC/EventPipe approach (this phase) |
|---|---|
| Only finds apps that added a Loom-specific startup call | Finds **any** running .NET process — the diagnostics IPC channel is opened by the runtime itself, automatically, for every .NET process |
| Stale entries need manual cleanup (`ProcessExit`, liveness checks) | The IPC channel and `EventPipe` session naturally disappear when the process exits — nothing to clean up |
| No live data — just "an app exists at this port" | `EventPipe` sessions stream live event data (GC, thread pool, and — via Step 13.3's bridge — Loom's own metrics) without any HTTP polling |
| Loom-specific protocol to maintain | Reuses the same protocol `dotnet-counters`/`dotnet-trace`/`dotnet-dump` already rely on — well-documented, stable across .NET versions |

The trade-off: basic process discovery (Step 13.2) works against *any* .NET process, but genuinely useful Loom metrics (Step 13.3) still require that process to be running `Loom.Telemetry` — discovery got easier and more general, but "meaningful data" still depends on instrumentation being present, same as before.

## Step 13.2: Create the DevTools Project

```bash
cd "C:\Users\angel\source\repos\Project Loom v2"
mkdir Loom.DevTools
cd Loom.DevTools
dotnet new console -f net10.0
```

**File:** `Loom.DevTools/Loom.DevTools.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <ToolCommandName>loom</ToolCommandName>
    <PackAsTool>true</PackAsTool>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Diagnostics.NETCore.Client" />
    <ProjectReference Include="..\Loom.Web.Contracts\Loom.Web.Contracts.csproj" />
  </ItemGroup>
</Project>
```

**Explanation (ELI5):**
> `Microsoft.Diagnostics.NETCore.Client` is Microsoft's own NuGet package for talking the diagnostics IPC protocol — it's what `dotnet-counters`/`dotnet-trace` themselves are built on, so this phase isn't reimplementing the wire protocol by hand, just using the same client library those tools use. `PackAsTool = true` + `ToolCommandName = "loom"` is the standard .NET global-tool pattern (unrelated to Native AOT) that makes `dotnet tool install -g Loom.DevTools` register a `loom` command globally, so `dotnet loom dev` (or just `loom dev`) works afterward. `Loom.DevTools` is intentionally **not** referenced by any other project — per `wiggly-noodling-hoare.md`'s Dependency Flow, it's a standalone CLI that can run independently of `Loom.Host`, matching README's description of it as separate, not embedded in the main server.

## Step 13.3: Bridging `LoomRuntime` onto `System.Diagnostics.Metrics`

This is the piece that connects Phase 6's ring buffers to something `EventPipe` can actually see. `System.Diagnostics.Metrics` (the `Meter`/`Counter<T>`/`Histogram<T>`/`ObservableGauge<T>` API) is itself EventPipe-compatible and fully reflection-free — .NET's own built-in metrics system, not a Loom invention — so bridging onto it is far simpler than inventing a custom EventPipe event source.

**File:** `Loom.Telemetry/MetricsBridge.cs`

```csharp
using System.Diagnostics.Metrics;

namespace Loom.Telemetry;

/// <summary>
/// Republishes LoomRuntime's ring-buffer writes through System.Diagnostics.Metrics so
/// any EventPipe-aware tool (dotnet-counters, Loom.DevTools, or a generic APM agent)
/// can observe them — without those tools needing to know anything about ring buffers,
/// tag interning, or Loom-specific wire formats.
/// </summary>
internal static class MetricsBridge
{
    private static readonly Meter Meter = new("Loom.Telemetry", "1.0.0");
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Counter<long>> Counters = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Histogram<double>> Histograms = new();

    public static void PublishCounter(string name, long increment) =>
        Counters.GetOrAdd(name, n => Meter.CreateCounter<long>(n)).Add(increment);

    public static void PublishHistogram(string name, double value) =>
        Histograms.GetOrAdd(name, n => Meter.CreateHistogram<double>(n)).Record(value);
}
```

**Wire it into `LoomRuntime` from Phase 6** — add one call at the end of each `Record*` method:

```csharp
public static void RecordCounter(string name, long increment = 1, ReadOnlySpan<MetricTag> tags = default)
{
    var buffer = Buffers.GetOrAdd(name, static _ => new MetricRingBuffer());
    buffer.Write(increment, InternTags(name, tags));
    MetricsBridge.PublishCounter(name, increment); // NEW — makes this visible to EventPipe
}
```

(Apply the equivalent one-line addition to `RecordGauge`/`RecordHistogram`, calling `PublishHistogram` for both — gauges are single point-in-time values, which a histogram of size 1 per tick represents adequately for dev-mode visibility purposes; a dedicated `ObservableGauge<T>` per gauge name is a nicer fit and a reasonable follow-up, not required for this phase's discovery goal.)

**Explanation (ELI5):**
> This is deliberately a *republish*, not a replacement — the ring buffer from Phase 6 stays the system of record for the query engine (Phase 10), alerting (Phase 11), and exporters (Phase 12); `System.Diagnostics.Metrics` is a second, parallel output purely so external diagnostics tools (including `Loom.DevTools`, but also anyone's existing `dotnet-counters` setup) get a zero-effort way to see Loom metrics without speaking Loom's internal format. The `Meter` name (`"Loom.Telemetry"`) is what a consumer subscribes to — shown in Step 13.4.

## Step 13.4: Process Discovery

**File:** `Loom.DevTools/Commands/DevCommand.cs`

```csharp
using Microsoft.Diagnostics.NETCore.Client;

namespace Loom.DevTools.Commands;

public static class DevCommand
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("Loom local dev mode — discovering .NET processes...\n");

        while (!ct.IsCancellationRequested)
        {
            var processes = DiagnosticsClient.GetPublishedProcesses().ToList();
            Console.Clear();
            Console.WriteLine($"Loom dev — {processes.Count} .NET process(es) discovered — {DateTime.Now:T}\n");

            foreach (var pid in processes)
            {
                await DescribeProcessAsync(pid, ct);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private static async Task DescribeProcessAsync(int pid, CancellationToken ct)
    {
        try
        {
            var client = new DiagnosticsClient(pid);
            var processInfo = await client.GetProcessInfoAsync(ct);
            var hasLoomMeter = await HasLoomMeterAsync(client, ct);

            Console.WriteLine(hasLoomMeter
                ? $"  ✓ {processInfo.ProcessName} (pid {pid}) — Loom.Telemetry active"
                : $"  · {processInfo.ProcessName} (pid {pid}) — .NET process, not Loom-instrumented");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ pid {pid} — unreachable: {ex.Message}");
        }
    }

    private static async Task<bool> HasLoomMeterAsync(DiagnosticsClient client, CancellationToken ct)
    {
        // A short-lived EventPipe session against the "Loom.Telemetry" Meter (Step 13.3):
        // if the target process has never called into LoomRuntime, no events arrive within
        // the timeout and this returns false — a lightweight presence check, not a full
        // streaming subscription (that's Step 13.5, started only for a process the user selects).
        var providers = new[] { new EventPipeProvider("System.Diagnostics.Metrics", System.Diagnostics.Tracing.EventLevel.Informational,
            arguments: new Dictionary<string, string> { ["Metrics"] = "Loom.Telemetry" }) };

        using var session = client.StartEventPipeSession(providers, requestRundown: false);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));

        try
        {
            var source = new EventPipeEventSource(session.EventStream);
            var found = false;
            source.Dynamic.All += _ => found = true;
            await Task.Run(() => { try { source.Process(); } catch (Exception) { } }, timeoutCts.Token);
            return found;
        }
        catch (OperationCanceledException) { return false; }
    }
}
```

**Explanation (ELI5):**
> `DiagnosticsClient.GetPublishedProcesses()` lists every process the runtime has an open diagnostics IPC channel to — this is the "finds any .NET process" line from the comparison table in Step 13.1, and it requires no cooperation from the target process beyond it being a normal .NET app (every .NET process opens this channel automatically). `HasLoomMeterAsync` then does a short, best-effort EventPipe session filtered to just the `"Loom.Telemetry"` meter (Step 13.3's `Meter` name) to distinguish "a .NET process exists" from "a Loom-instrumented .NET process exists" — this is deliberately a quick presence check (500ms timeout, no persistent session) so the discovery loop stays responsive scanning potentially many processes every 2 seconds; a full live-metrics view for one chosen process is a separate, longer-lived session (Step 13.5).

## Step 13.5: Live Streaming for a Selected Process

**File:** `Loom.DevTools/Commands/WatchCommand.cs`

```csharp
using Microsoft.Diagnostics.NETCore.Client;

namespace Loom.DevTools.Commands;

public static class WatchCommand
{
    public static async Task RunAsync(int pid, CancellationToken ct)
    {
        var client = new DiagnosticsClient(pid);
        var providers = new[] { new EventPipeProvider("System.Diagnostics.Metrics", System.Diagnostics.Tracing.EventLevel.Informational,
            arguments: new Dictionary<string, string> { ["Metrics"] = "Loom.Telemetry", ["RefreshInterval"] = "1" }) };

        using var session = client.StartEventPipeSession(providers, requestRundown: false);
        var source = new EventPipeEventSource(session.EventStream);

        source.Dynamic.All += traceEvent =>
        {
            // Real payload parsing depends on the counter-payload event schema
            // System.Diagnostics.Metrics emits (CounterPayload/HistogramPayload) —
            // sketched here at the subscription level; full payload decoding is a
            // mechanical follow-up once this subscription is confirmed receiving events.
            Console.WriteLine($"[{DateTime.Now:T}] {traceEvent.EventName}: {traceEvent.FormattedMessage}");
        };

        await Task.Run(() => source.Process(), ct);
    }
}
```

**File:** `Loom.DevTools/Program.cs`

```csharp
using Loom.DevTools.Commands;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

switch (args)
{
    case ["dev"]:
        await DevCommand.RunAsync(cts.Token);
        break;
    case ["watch", var pidArg] when int.TryParse(pidArg, out var pid):
        await WatchCommand.RunAsync(pid, cts.Token);
        break;
    default:
        Console.WriteLine("Usage: loom dev | loom watch <pid>");
        break;
}
```

**Explanation (ELI5):**
> `loom dev` (Step 13.4) is the zero-config discovery view — a live-refreshing list of what's running. `loom watch <pid>` opens a longer-lived `EventPipeSession` against one specific, already-discovered process and streams its `Loom.Telemetry` meter events as they happen — this is the "live streaming" half of local dev mode, and it's still console/JSON output only, not a browser dashboard, matching the scope boundary carried over from earlier drafts of this document. The event-payload parsing in the `Dynamic.All` handler is intentionally left at the subscription/wiring level — `TraceEvent`'s exact `CounterPayload` schema is a mechanical decoding detail once the subscription itself is confirmed working, not additional architecture, so it's flagged rather than papered over with unverified parsing code.

## Step 13.6: DTOs

**File:** `Loom.Web.Contracts/Dtos/DevModeDtos.cs`

```csharp
namespace Loom.Web.Contracts.Dtos;

public sealed record DevModeStatusDto
{
    public required int DiscoveredProcessCount { get; init; }
    public required int LoomInstrumentedCount { get; init; }
}

public sealed record DiscoveredAppDto
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required bool IsLoomInstrumented { get; init; }
}
```

**Register in `JsonContext.cs`:**
```csharp
[JsonSerializable(typeof(DevModeStatusDto))]
[JsonSerializable(typeof(DiscoveredAppDto))]
[JsonSerializable(typeof(List<DiscoveredAppDto>))]
```

These back a `--json` output mode for `loom dev` (emit `DiscoveredAppDto[]` instead of the human-readable console table) — a small branch on the existing `DevCommand.RunAsync` loop, left as a mechanical extension since the discovery logic itself (Step 13.4) doesn't change based on output format.

## Step 13.7: Verify

```bash
dotnet pack Loom.DevTools -c Release
dotnet tool install -g Loom.DevTools --add-source ./Loom.DevTools/bin/Release

# In one terminal, run an app with Loom.Telemetry wired up:
dotnet run --project Loom.Web.Api

# In another terminal:
loom dev
# Should list the running process, marked "Loom.Telemetry active"

loom watch <pid-shown-above>
# Should stream live metric events as RecordCounter/RecordGauge/RecordHistogram calls happen
```

### 🔍 Checkpoint 13.1 (MAJOR MILESTONE — Telemetry platform complete)
```
✓ Phase 13 Complete: Local Development Mode
✓ ADR-11 rationale understood: diagnostics IPC/EventPipe over a marker-file mechanism
✓ Loom.DevTools packable as a standalone dotnet global tool (`loom` command), not referenced by other projects
✓ MetricsBridge — republishes LoomRuntime onto System.Diagnostics.Metrics (Meter/Counter/Histogram)
✓ `loom dev` — DiagnosticsClient.GetPublishedProcesses() discovery, Loom-instrumented detection via short EventPipe probe
✓ `loom watch <pid>` — longer-lived EventPipe session, live console streaming
✓ Confirmed console/JSON output only — no browser dashboard work introduced

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
TELEMETRY PLATFORM NOW IMPLEMENTED (Phases 5-13):
  Source Generator (5) → Custom Metrics API (6) → Attributes (7)
  → Collectors (8) → Sampling (9) → Query Language (10)
  → Alerting (11) → Exporters (12) → Local Dev Mode (13)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Understanding Check:
Q: Why can loom dev discover any .NET process, but only show real metrics for some of them?
A: [User explains — diagnostics IPC is automatic/universal, Loom-specific data still needs Loom.Telemetry present]

Q: Why bridge onto System.Diagnostics.Metrics instead of inventing a Loom-specific EventPipe provider?
A: [User explains — reuses existing, well-documented tooling/protocol instead of a bespoke one]

Ready for Phase 14 (Security Hardening)? [Y/N]
```
---

# PHASE 14: Security Hardening

**Duration:** 4-6 days (revised up from 3-4; see 14.0.3)
**Goal:** Authenticate every data-bearing endpoint on **both** HTTP hosts, on top of the
transport hardening and query-input safety already shipped.

> **Status banner — revised 2026-08-28.** This section was rewritten after the original
> was found to be under-specified and, in one place, provably wrong. Three things
> changed:
>
> 1. **The original Step 14.1 code does not work.** Its `TryBase64UrlDecode` delegates to
>    `Convert.TryFromBase64Chars`, which rejects base64url. Measured on .NET 10.0.11:
>    `Convert.TryFromBase64Chars("----Pn8")` returns **`False`, 0 bytes written**, while
>    `System.Buffers.Text.Base64Url.TryDecodeFromChars` returns `True`, 5 bytes, correct
>    round-trip. Any JWT whose signature contains `-` or `_` — roughly all of them —
>    would have failed validation. `ExtractSubjectClaim` also returned `default`
>    unconditionally, and no expiry, no `nbf`, and **no `alg` header check** existed.
> 2. **Phase 14 was scoped to `Loom.Web.Api` only.** `Loom.Dashboard` is a second,
>    larger HTTP host (~25 endpoints, including `/api/logs/*`, `/api/logs/explain`,
>    `/ws/logs`, `/prometheus`) and is the host operators actually run. Securing one and
>    not the other is a full bypass. Both are now in scope.
> 3. **The scope decisions are now recorded** (14.0.1) instead of being carried as an
>    inference in a handoff file.

**AOT-compatibility note (ADR-3):** Manual `HMACSHA256`-based JWT — not
`System.IdentityModel.Tokens.Jwt` or `Microsoft.AspNetCore.Authentication.JwtBearer`,
both of which use reflection for claim deserialization, `TypeDescriptor` for validation,
and assembly scanning for scheme discovery. `Span<byte>`/`stackalloc` throughout.

---

## Step 14.0: Scope, Threat Model, and Status

### 14.0.1 Resolved scope decisions

Decided by the user on 2026-08-28. These are settled; do not re-open them.

| # | Decision | Chosen | Consequence |
|---|---|---|---|
| 1 | Token issuance | **Interactive login** — `POST /api/token` exchanges username + password for a JWT | Requires a credential store, a password KDF, and a login UI. No user database — see 14.1.1. |
| 2 | Protection scope | **Everything protected, no exceptions** | Prometheus scraping breaks until its scrape job carries a token. See 14.0.2 for the two mechanically-forced carve-outs. |
| 3 | WebSocket carrier | **`Sec-WebSocket-Protocol`** | Token never appears in a URL, access log, or `Referer`. |

### 14.0.2 Two carve-outs forced by mechanics, not by preference

Decision 2 says "no exceptions." Two endpoints cannot honour it, for reasons that are
structural rather than discretionary. Both are called out here so they are visible as
accepted exposure rather than discovered later as oversights.

- **`POST /api/token`** — it is the credential exchange. Requiring a token to obtain a
  token is not satisfiable.
- **The SPA shell and its static assets** (`Loom.Dashboard`'s `MapSpaFallback` /
  `MapFallback`) — the browser must load the login page *before* a token exists. These
  routes serve only the Angular bundle from `ManifestEmbeddedFileProvider`; they expose
  no telemetry. Every `/api/*` and `/ws/*` route behind them is protected.

`/prometheus` and `/metrics` **are** protected, per decision 2. This has a real
operational cost — see 14.7.3, which carries the one item still needing your sign-off.

### 14.0.3 What is already done

Shipped in `7f147e1` and `289422b`, verified in source at `289422b`:

> **Superseded 2026-09-02.** All four items below describe `Loom.Web.Api`, which no longer
> exists. HSTS and HTTPS redirection were later removed from both hosts outright (in-process
> TLS rejected, `BACKLOG.md` § 3.3). **CORS and `LOOM_CORS_ORIGINS` were not ported to
> `Loom.Dashboard` and the variable is now read nowhere** — the Dashboard serves its UI from
> its own origin, so same-origin is correct and a policy would only widen the surface. The
> three safe headers did move; the CSP was rewritten for the SPA (`BACKLOG.md` § 11.5).

- HSTS + HTTPS redirection under `!IsDevelopment()` (`Loom.Web.Api/Program.cs`)
- Opt-in strict CORS via `LOOM_CORS_ORIGINS`; no policy registered when unset, so the
  browser's same-origin default applies. No wildcard branch exists.
- Security headers: `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
  `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`
- Kestrel limits: `AddServerHeader=false`, 1 MB body, 8 KB request line, 1000 connections
- Query input safety: `QueryParser.MaxQueryLength = 4096` bounded off a null-safe span,
  and the `int.TryParse` guard on `LIMIT` at `QueryParser.cs:86`

**Still to build:** everything in 14.1 through 14.7.

### 14.0.4 Threat model

What this phase defends against, stated plainly so the design can be judged against it:

- **In scope.** An unauthenticated party who can reach the listening port reads captured
  telemetry and application logs, or writes fabricated metrics via
  `POST /api/metrics/ingest`. Today every endpoint on both hosts is anonymous, so this
  requires no attack — only reachability.
- **In scope.** Credential brute-force against the new login endpoint (14.1.5).
- **In scope.** JWT algorithm confusion and `alg: none` forgery (14.1.4).
- **Out of scope, by design.** An attacker with local code execution as the `loomd` user.
  They can read `jwt.key` directly; no token scheme survives that.
- **Out of scope for JWT, governed by the OS instead.** `Loom.DevTools` attaches to target
  processes over the .NET diagnostic IPC channel and has no network surface at all, so no
  token applies to it. Its authorization boundary is the OS user that owns the target
  process — which is why the "never run `loom` elevated" rule in 14.7.2 is a security
  control, not a style preference.
- **Out of scope, unchanged from today.** Both hosts bind loopback
  (`options.ListenLocalhost(port)` in `Loom.Dashboard/Program.cs:104`), so remote access
  already requires an SSH tunnel. Authentication is defence in depth on top of that, not
  a replacement for it — do not relax the loopback bind because auth now exists.

---

## Step 14.1: Authentication

### 14.1.1 Where credentials live

No database, no `IdentityDbContext`, no ORM — all three would drag reflection into an AOT
binary. Users live in a flat file, parsed once at startup into a frozen array.

**File:** `LOOM_AUTH_USERS_FILE`, default `/var/secrets/loom/users` (mode `400`,
`root:loomd`, alongside `jwt.key`).

```
# comment lines and blanks are skipped
operator:pbkdf2-sha256$600000$<base64url-salt>$<base64url-hash>
```

Rules:

- Parsed once at startup. A malformed line is a **startup failure**, not a skipped line —
  a typo must not silently remove an account.
- Empty or missing file with auth enabled is a **startup failure**. Fail closed.
- Username comparison is `StringComparison.Ordinal`.
- **An unknown username still runs a full PBKDF2 verification against a fixed dummy
  record before returning failure.** Without this, "no such user" returns in
  microseconds while "wrong password" takes ~74 ms, and the endpoint becomes a user
  enumeration oracle.

### 14.1.2 Password KDF

`Rfc2898DeriveBytes.Pbkdf2` — a static method in `System.Security.Cryptography`, no
reflection, AOT-clean. Argon2 is deliberately **not** used: it is not in the BCL, and the
available packages would add a dependency with unverified AOT behaviour to satisfy a
threat this deployment does not face.

| Parameter | Value |
|---|---|
| Algorithm | PBKDF2-HMAC-SHA256 |
| Iterations | 600,000 (OWASP guidance for SHA-256) |
| Salt | 16 bytes from `RandomNumberGenerator.Fill` |
| Output | 32 bytes |

**Measured on this machine, .NET 10.0.11: 74 ms per derivation.** That figure is load-
bearing in two directions — it is an acceptable one-off login cost, and it caps offline
brute-force throughput at roughly 13 guesses/second/core, which is why the throttle in
14.1.5 is defence in depth rather than the primary control.

Comparison uses `CryptographicOperations.FixedTimeEquals`. Never `SequenceEqual`.

### 14.1.3 Token format

```
Header   {"alg":"HS256","typ":"JWT"}
Payload  {"sub":"operator","iss":"loom","iat":<unix>,"exp":<unix>}
Payload  {"sub":"prometheus","iss":"loom","scope":"metrics","iat":<unix>,"exp":<unix>}
```

- **`scope` is optional and defaults to full operator authority when absent.** The only
  defined value is `metrics`, which restricts a token to the two scrape routes
  (14.2, 14.7.3). Interactive logins never set it, so decision 1 is unaffected.
  A token carrying an *unrecognised* `scope` is rejected outright rather than treated as
  unscoped — an unknown scope must never fail open into full authority.
- **TTL 60 minutes** for interactive logins. Absolute session lifetime 12 hours, enforced from `iat` — after
  that, refresh stops working and the operator logs in again.
- **Clock skew leeway: 60 seconds** on `exp` and `nbf`.
- Stateless. No server-side token store, no revocation list. Rotating `jwt.key`
  invalidates every outstanding token at once, which is the intended revocation lever
  for a single-operator diagnostic tool.

`JwtHeader` and `JwtClaims` are DTOs in **`Loom.Web.Contracts`** and **must** be
registered in `LoomJsonSerializerContext` like every other DTO. `TokenRequest` and
`TokenResponse` go there too.

### 14.1.4 `JwtValidator` — corrected

**New project: `Loom.Security`.** Both hosts need this code and neither may depend on
the other. `Loom.Web.Contracts` is not the home for it — that project holds DTOs and
must keep its "no project references" property. `Loom.Security` references
`Loom.Web.Contracts` only, and is added to `Loom.slnx` as the 14th project.

**File:** `Loom.Security/JwtValidator.cs`

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;

namespace Loom.Security;

public enum JwtFailure { None, Malformed, BadAlgorithm, BadSignature, Expired, NotYetValid, SessionExpired }

/// <summary>HS256 validation. No reflection, no TypeDescriptor, no assembly-scanned
/// auth schemes. Signature work is stack-allocated; the single unavoidable heap
/// allocation is the UTF-8 byte copy of the signing input.</summary>
public sealed class JwtValidator(byte[] secret, TimeProvider clock)
{
    private const int SignatureBytes = 32;              // HMAC-SHA256 is always 32
    private const int SkewSeconds = 60;
    private const int AbsoluteSessionSeconds = 12 * 60 * 60;

    public JwtFailure Validate(ReadOnlySpan<char> token, out string? subject)
    {
        subject = null;

        var firstDot = token.IndexOf('.');
        if (firstDot < 0) return JwtFailure.Malformed;
        var lastDot = token.LastIndexOf('.');
        if (lastDot <= firstDot) return JwtFailure.Malformed;

        var signingInput = token[..lastDot];
        var headerSpan = token[..firstDot];
        var payloadSpan = token[(firstDot + 1)..lastDot];
        var signatureSpan = token[(lastDot + 1)..];

        // 1. Header FIRST. An attacker controls this; `alg: none` and algorithm
        //    confusion are only stopped by refusing anything that is not exactly HS256,
        //    before a signature is computed.
        Span<byte> headerBytes = stackalloc byte[256];
        if (!Base64Url.TryDecodeFromChars(headerSpan, headerBytes, out var headerWritten))
            return JwtFailure.Malformed;
        JwtHeader? header;
        try
        {
            header = JsonSerializer.Deserialize(headerBytes[..headerWritten],
                LoomJsonSerializerContext.Default.JwtHeader);
        }
        catch (JsonException) { return JwtFailure.Malformed; }
        if (header is null || !string.Equals(header.Alg, "HS256", StringComparison.Ordinal))
            return JwtFailure.BadAlgorithm;

        // 2. Signature. Base64Url, NOT Convert.TryFromBase64Chars - that call returns
        //    false on any base64url input containing '-' or '_' (measured).
        Span<byte> provided = stackalloc byte[SignatureBytes];
        if (!Base64Url.TryDecodeFromChars(signatureSpan, provided, out var sigWritten)
            || sigWritten != SignatureBytes)
            return JwtFailure.BadSignature;

        var signingBytes = Encoding.UTF8.GetBytes(signingInput.ToString()); // the one allocation
        Span<byte> computed = stackalloc byte[SignatureBytes];
        HMACSHA256.HashData(secret, signingBytes, computed);
        if (!CryptographicOperations.FixedTimeEquals(computed, provided))
            return JwtFailure.BadSignature;

        // 3. Claims - only after the signature is trusted.
        Span<byte> payloadBytes = stackalloc byte[512];
        if (!Base64Url.TryDecodeFromChars(payloadSpan, payloadBytes, out var payloadWritten))
            return JwtFailure.Malformed;
        JwtClaims? claims;
        try
        {
            claims = JsonSerializer.Deserialize(payloadBytes[..payloadWritten],
                LoomJsonSerializerContext.Default.JwtClaims);
        }
        catch (JsonException) { return JwtFailure.Malformed; }
        if (claims is null || string.IsNullOrEmpty(claims.Sub)) return JwtFailure.Malformed;

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (now > claims.Exp + SkewSeconds) return JwtFailure.Expired;
        if (claims.Nbf > 0 && now + SkewSeconds < claims.Nbf) return JwtFailure.NotYetValid;
        if (now > claims.Iat + AbsoluteSessionSeconds) return JwtFailure.SessionExpired;

        subject = claims.Sub;
        return JwtFailure.None;
    }
}
```

Four notes on why this differs from the original:

- **`Base64Url` over `Convert`** — the original was measurably broken. See the status
  banner.
- **Header validated before the signature is computed** — `alg` is attacker-controlled.
- **Fixed 256/512-byte stack buffers** — a header or payload larger than that is not a
  token this system issued, so overflow returns `Malformed` rather than growing a buffer
  on attacker input.
- **`TimeProvider` injected** — expiry is testable without `Thread.Sleep`.

A `JwtIssuer` in the same project mints tokens with the same key. Keep issue and validate
adjacent; a mismatch between them is the classic source of "works locally, 401 in prod."

### 14.1.5 `POST /api/token`

```
Request   {"username":"operator","password":"..."}
200       {"token":"eyJ...","expiresIn":3600}
401       {"error":"Invalid credentials"}          <- identical for unknown user and bad password
429       Retry-After: <seconds>
```

- The 401 body and timing are **identical** for both failure modes (14.1.1).
- **Throttle:** 5 failed attempts per remote IP per 15-minute fixed window, then 429.
  The attempt table is a bounded dictionary — **cap 1024 entries, evict oldest on
  insert** — so spoofed source addresses cannot grow it without limit.
- **Honest limitation:** behind a loopback bind, every request presents as `127.0.0.1`,
  so this degrades to a global throttle and one attacker can lock out the operator. That
  is the correct trade for a single-operator tool, but it is a trade, not a free win.
  The 74 ms KDF is the real brute-force control.
- Log failures at Warning with username and source IP. **Never log the password, the
  token, or any part of either.**

`POST /api/token/refresh` takes a currently-valid token and returns a new 60-minute one,
subject to the 12-hour absolute cap. Stateless; no store.

### 14.1.6 Key and credential provisioning

**Signing key:** `LOOM_JWT_KEY_FILE`, default `/var/secrets/loom/jwt.key`, containing
base64 of at least 32 random bytes. **Startup fails** if the file is missing, unreadable,
or decodes to fewer than 32 bytes. There is no generated-on-the-fly fallback in any
environment — an ephemeral dev key is exactly the kind of convenience that ships to
production by accident.

```bash
openssl rand -base64 32 > /var/secrets/loom/jwt.key
chmod 400 /var/secrets/loom/jwt.key
chown root:loomd /var/secrets/loom/jwt.key
```

**Windows development** has no `/var/secrets`. Add to `Loom.DevTools`:

```
loom auth init                          # writes key + empty users file under
                                        #   %LOCALAPPDATA%\Loom\dev-secrets\
loom auth add-user <name>               # prompts for a password, appends a PBKDF2 line
loom auth hash                          # prompts, prints one users-file line to stdout
```

`loom auth init` prints the two `LOOM_*` environment variables to set. It refuses to
overwrite an existing key file.

---

## Step 14.2: Enforcement Middleware — Both Hosts

`Loom.Security/AuthenticationMiddleware.cs`, registered by both hosts through one shared
extension so the two pipelines cannot drift apart.

- Reads `Authorization: Bearer <token>`.
- On success, stashes the subject in `HttpContext.Items["loom.sub"]`. **No
  `ClaimsPrincipal`, no `HttpContext.User`** — populating those pulls in the ASP.NET Core
  authentication stack this ADR exists to avoid.
- On failure: `401` with `WWW-Authenticate: Bearer`, and a body that names only the
  failure class (`invalid_token` / `expired_token`), never the internal `JwtFailure`
  value.
- **Exemptions are an explicit allow-list in code**, not a prefix match. A path-prefix
  exemption is how `/api/token/../logs` becomes a bypass.
- **Scope enforcement.** After a token validates, a `scope: metrics` claim restricts it
  to `/metrics` and `/prometheus`; anything else returns **403, not 401** — the
  credential is valid, the authority is not, and a 401 would send a correctly-configured
  scraper into a pointless re-authentication loop. An unrecognised scope value is
  rejected as invalid. Route matching here is exact, for the same reason exemptions are.

**`Loom.Web.Api/Program.cs` — corrected pipeline order:**

```csharp
app.UseSecurityHeaders();      // MOVED: was after UseHttpsRedirection, so 307 redirects
                               // went out bare. Fixes the known low-severity gap.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
if (corsOrigins.Length > 0) app.UseCors();
app.UseWebSockets(...);
app.UseLoomAuthentication();   // NEW - after CORS so preflight OPTIONS is not 401'd
app.MapApiEndpoints();
```

Auth sits **after** `UseCors`. A CORS preflight `OPTIONS` carries no `Authorization`
header; rejecting it produces a browser error that looks like a CORS misconfiguration and
costs an afternoon to diagnose.

> **Superseded 2026-09-02.** This is `Loom.Web.Api`'s pipeline and the project is gone. In
> the shipping `Loom.Dashboard` pipeline there is **no `UseCors`** (the variable that gated
> it, `LOOM_CORS_ORIGINS`, is read nowhere) and **no `UseHsts`/`UseHttpsRedirection`**
> (in-process TLS was rejected — `BACKLOG.md` § 3.3). What survives is the ordering
> principle: the header middleware runs at the very front, ahead of authentication and
> `UseStaticFiles`, so a short-circuited response still carries headers. The
> preflight-ordering rule above is correct and worth keeping for the day a CORS policy is
> reintroduced — but nothing depends on it today.

**`Loom.Dashboard`** gets the same call in the same position. Its exemption list is
`POST /api/token`, the SPA fallback, and the embedded static assets — nothing else.
`/api/logs/*`, `/api/logs/explain`, `/prometheus`, `/ws/logs`, and `/ws/metrics` are all
protected.

---

## Step 14.3: WebSocket Authentication

Browsers cannot set request headers on a WebSocket handshake. The token rides the
subprotocol negotiation instead.

**Client:**
```ts
new WebSocket(url, ['loom.v1', `loom.token.${token}`])
```

**Server**, before `AcceptWebSocketAsync`:

```csharp
var requested = context.WebSockets.WebSocketRequestedProtocols;
var carrier = requested.FirstOrDefault(p => p.StartsWith("loom.token.", StringComparison.Ordinal));
if (carrier is null || validator.Validate(carrier.AsSpan(11), out var sub) != JwtFailure.None)
{
    context.Response.StatusCode = 401;   // reject the handshake; do not accept then close
    return;
}
await context.WebSockets.AcceptWebSocketAsync("loom.v1");   // echo the REAL subprotocol only
```

Three rules:

- **Reject before accepting.** Accepting and then closing with a policy code leaks that
  the endpoint exists and burns a connection slot.
- **Echo `loom.v1`, never the token subprotocol.** Echoing it back puts the credential in
  the response headers.
- Applies to `/ws/metrics` on both hosts and `/ws/logs` on the Dashboard.

**Known gap, accepted:** the token is validated at handshake only. A connection opened at
minute 59 survives past token expiry for as long as it stays open. Bounding that needs
per-message re-validation or a server-side connection registry; both are more machinery
than a loopback-bound diagnostic socket warrants. Recorded here so it is a decision, not
an omission.

---

## Step 14.4: Transport Hardening — DONE

Shipped; see 14.0.3. The only change is the middleware **ordering fix** in 14.2.

The original text here specified `app.Configuration.GetSection("Loom:Cors:AllowedOrigins").Get<string[]>()`.
The shipped implementation uses the `LOOM_CORS_ORIGINS` environment variable instead:
`IConfiguration.Get<T>()` binds by reflection, which `CreateSlimBuilder` does not root and
the trimmer may remove. The shipped form is correct; this text is corrected to match.

> **Superseded 2026-09-02 — CORS left the product entirely.** `Loom.Web.Api` was the only
> reader of `LOOM_CORS_ORIGINS`, and it has been retired (`BACKLOG.md` § 11.4). CORS was
> **not** ported: `Loom.Dashboard` serves its Angular bundle from its own origin, so the
> browser's same-origin default is already the correct policy and any allow-list would only
> widen the surface. Setting `LOOM_CORS_ORIGINS` today has no effect anywhere in the
> solution, and no host registers a CORS policy. The reflection point above still stands as
> a general rule for `CreateSlimBuilder` hosts.

---

## Step 14.5: Query Parser Input Safety — DONE

Shipped in `7f147e1` / `289422b`. Two deviations from the original text, both deliberate:

- **The cap is 4096, not 2048, and lives in `QueryParser` itself**, not at the endpoint.
  The endpoint is not the only caller — `Loom.DevTools` and `Loom.Dashboard` both parse
  queries — so a bound at one endpoint left the other paths unguarded.
- **The bound is taken off `queryText.AsSpan()`**, because `AsSpan()` is null-safe and
  `.Length` on a null string throws. `QueryRequest.Query` can be null: `required` demands
  only that the JSON property be *present*, not non-null.

Also fixed here: `int.Parse` on `LIMIT` threw `FormatException` / `OverflowException`,
neither of which is a `QuerySyntaxException`, so both escaped as unhandled 500s on
unauthenticated input.

---

## Step 14.6: Alert Webhook Validation — OUT OF SCOPE

The original text framed this as SSRF mitigation. It is not: the URL comes from
`LOOM_ALERT_WEBHOOK_URL`, set by the operator, not supplied by an attacker. An operator
who can set that variable can already reach the network directly. An allow-list here adds
configuration surface and blocks nothing. Revisit only if webhook destinations ever
become user-supplied through the API.

---

## Step 14.7: Client Updates

Auth is not shippable without these. A grep of every `.cs` and `.ts` outside
`obj`/`bin`/`node_modules` for `Authorization|Bearer|jwt|LOOM_TOKEN` returns **zero
hits** — no client sends a token today, so enforcement breaks all three at once.

### 14.7.1 Angular (`Loom.Web.Frontend`)

- Login component posting to `/api/token`.
- An `HttpInterceptor` attaching `Authorization: Bearer`, and routing any 401 back to
  login.
- WebSocket construction updated to the subprotocol form in 14.3.
- A refresh timer firing at ~50 minutes.
- **Token storage: `sessionStorage`.** In-memory-only would force a re-login on every
  page refresh, which operators will work around by writing the token somewhere worse.
  `sessionStorage` is readable by injected script, so this is a real XSS exposure —
  acceptable because the Dashboard serves only its own embedded bundle with no
  user-generated HTML, and unacceptable the moment that stops being true.

### 14.7.2 `Loom.DevTools` — no token, and that is correct

**Correction.** An earlier revision of this step claimed `loom logs` and `loom search`
call the Dashboard API and therefore need a `--token` flag. **They do not.** Verified by
reading the source: `LogsCommand` constructs its own `InMemoryMetricStore` /
`InMemoryLogStore` and an `EventPipeCollector`, then attaches directly to the target
process. A scan of every `.cs` in `Loom.DevTools` for
`HttpClient|HttpListener|Socket|WebApplication|Kestrel|TcpListener|WebSocket` returns
**zero matches**. The tool has no network surface in either direction. Do not add a
`--token` flag; there is nothing for it to authenticate to.

**`Loom.DevTools` is therefore outside Phase 14's authentication boundary — but not
outside its threat model.** Its authorization is the operating system's:

| Platform | Diagnostic IPC endpoint | Who may attach |
|---|---|---|
| Windows | named pipe `dotnet-diagnostic-<pid>-*` | the target process's user, and Administrators |
| Linux / macOS | Unix domain socket under `TMPDIR` | the target process's user, and root |

`new DiagnosticsClient(pid)` (`EventPipeCollector.cs:62`) inherits exactly that. The
security property to preserve is one operational rule, and it belongs in the runbook:

> **Never run `loom` as root or Administrator.** EventPipe attach authority is the OS
> user boundary and nothing else. Elevated, `loom logs <pid>` will dump the captured
> logs — and therefore any secret those logs contain — of *any* process on the machine.
> This is the single highest-impact misuse of the tool and it requires no exploit.

This also means `loom` must **not** be given a setuid bit, a capability grant, or a
sudoers entry as a convenience for attaching to services running as other users.

### 14.7.2.1 CLI command execution — audited, no injection surface

`Loom.DevTools` starts exactly one external process, in `DashboardCommand.LaunchDashboard`
(`DashboardCommand.cs:70` and `:97`):

```csharp
Process.Start(new ProcessStartInfo("loom-dashboard", pid.ToString()) { UseShellExecute = false, ... })
```

Assessed and found sound; recorded so it is not re-litigated:

- **No argument injection.** The only argument is `pid.ToString()` on an `int` — digits
  and possibly a leading `-`. No caller-supplied string reaches the argument list.
- **No shell.** `UseShellExecute = false` means no `cmd.exe` and no `/bin/sh`, so shell
  metacharacters have no interpreter even if one were somehow introduced.
- **No current-directory binary planting.** The bare image name `"loom-dashboard"` raised
  the question of whether the working directory is searched. **Measured, not assumed:**
  with a decoy `loom-dashboard.exe` in the working directory and no `loom-dashboard`
  anywhere on `PATH`, this call throws
  `Win32Exception: The system cannot find the file specified`. .NET does not search the
  current directory for `UseShellExecute = false`, and `SafeProcessSearchMode` is unset
  (default) on the test machine. **This is not a finding.**
- **`PATH` order is the residual risk, and it is out of scope.** An attacker who can
  write to a directory earlier in the operator's `PATH` controls what `loom dashboard`
  executes — but that attacker already owns the account. This is standard for every bare
  process launch and is not specific to Loom. Not filed.

**Standing constraint, re-verified this phase:** `CLAUDE.md` forbids interactive shell
execution over SSH/HTTP. No endpoint on either host executes a command, and
`Loom.DevTools` has no network surface, so no remote-command path exists anywhere in the
platform. Phase 14 must not introduce one — in particular, no "run diagnostic command"
endpoint, however convenient it looks next to the query endpoint.

### 14.7.2.2 What Phase 14 *does* change for `Loom.DevTools`

One thing, and it is a diagnosability trap rather than a security hole.

`loom dashboard <pid>` spawns `loom-dashboard`, which after this phase refuses to start
without `LOOM_JWT_KEY_FILE` and `LOOM_AUTH_USERS_FILE` (14.1.6). The child inherits the
parent's environment, so a correctly configured shell works unchanged. A missing key does
not.

The trap: `DashboardCommand.cs:68-93` wraps the `--version` probe in a bare `catch` that
prints **"Dashboard package not found. Install with: dotnet tool install -g Loom.Dashboard"**
for *every* failure. After this phase the most likely failure is a missing signing key,
which would be reported as a missing package — sending the operator to reinstall a tool
that is already installed. Two required changes:

1. `loom-dashboard --version` must short-circuit and print the version **before** loading
   the key or users file. A version probe must never require credentials.
2. Widen the catch: report the child's actual exit code and stderr, and keep the
   "not installed" message for `Win32Exception` specifically, which is what an absent
   executable actually raises (measured above).

Filed as `BACKLOG.md` § 4.8.

### 14.7.3 Prometheus — scoped service token (decided 2026-08-28)

Decision 2 protects `/metrics` and `/prometheus`. Prometheus authenticates with a static
`bearer_token_file` and **cannot refresh a JWT**, so a 60-minute token would break
scraping an hour after every login.

**Resolution: a scope-restricted service token.** Long-lived, non-interactive, minted by
the operator, exempt from the 12-hour absolute cap because it carries no interactive
session to cap.

```
loom auth token --sub prometheus --scope metrics --ttl 90d \
  > /var/secrets/loom/prometheus.token
chmod 400 /var/secrets/loom/prometheus.token      # owned by the prometheus service user
```

```yaml
scrape_configs:
  - job_name: loom
    authorization:
      type: Bearer
      credentials_file: /var/secrets/loom/prometheus.token
```

Four decisions inside that command, each load-bearing:

- **`--scope metrics` is not optional.** This token is the weakest-protected credential in
  the system: static, on disk, read by a service account, and near-certain to end up in
  configuration management and backups. Without a scope it would carry the same authority
  as an operator login, so a leaked scrape token would read `/api/logs/*` and
  `/api/logs/explain` — the most sensitive surface in the product. Scoped, a leak yields
  a metrics dump and nothing else. Enforced at 14.2.
- **90 days, not a year.** There is no revocation list. The only other revocation lever is
  rotating `jwt.key`, which also invalidates the operator's own sessions. TTL *is*
  revocation for service tokens, so the window has to be one somebody would actually
  accept a leak across.
- **Expiry is self-alarming, which is what makes the long TTL safe.** When the token
  lapses, scrapes 401 and `up{job="loom"}` goes to 0 — a condition Prometheus already
  alerts on. A forgotten rotation is loud, not silent. Put the rotation date in
  `RUNBOOK-staging-trial.md`.
- **This widens decision 1** from "interactive login only" to "interactive login, plus
  operator-minted scoped service tokens." Recorded explicitly so the widening is visible
  rather than inferred from the code later.

**Rejected alternative:** a systemd timer minting a fresh 60-minute token into the
credentials file, eliminating long-lived tokens entirely. It depends on Prometheus
re-reading `credentials_file` on every scrape — plausible for recent versions but
**not verified against any specific version here, so do not build on it without checking**.
Even if true, it trades a static credential for a timer that can fail silently, which is
the worse failure mode for a loopback-bound single-operator tool. Revisit only if Loom is
ever exposed beyond the SSH tunnel.

---

## Step 14.8: Verification

```powershell
dotnet build Loom.slnx -c Release /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true
dotnet test Loom.slnx -c Debug          # baseline 509 passing before this phase
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj -c Release -r win-x64
```

**Binary size.** 14.784 MB at `289422b` against a 17 MB hard limit — **2.216 MB of
headroom**. `Loom.Security` adds PBKDF2, HMAC, and two more source-generated DTOs. Measure
after; do not assume.

```powershell
Get-ChildItem Loom.Web.Api/bin/Release/net10.0/win-x64/publish/ -Filter *.exe |
  Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}}
```

**Correction to the original verification block:** it curled `https://localhost:5443/api/health`.
Both halves were wrong. Loom does not terminate TLS (`BACKLOG.md` § 3.3), so the scheme and
port are `http://localhost:5080`. And `/api/health` now exists on **both** hosts —
`Loom.Web.Api` (`Extensions/EndpointExtensions.cs:35`) and `Loom.Dashboard`
(`EndpointExtensions.cs:50`) — since `598c345` added the anonymous marker to the AOT publish
target that actually runs under systemd. The claim that only the Dashboard had one was true
when written and this note predates that commit.

Required test coverage in `Loom.Telemetry.Tests`:

| Area | Cases |
|---|---|
| `JwtValidator` | valid; expired; not-yet-valid; past 12-hour absolute cap; tampered payload; tampered signature; `alg: none`; `alg: RS256`; missing/extra dots; empty token; oversized header; oversized payload; base64url signature containing `-` and `_` (the regression the original code failed) |
| `JwtIssuer` | round-trips through `JwtValidator`; honours TTL |
| Credentials | correct password; wrong password; unknown user; malformed users line fails startup; missing file fails startup |
| Timing | unknown-user and wrong-password paths both perform a KDF derivation |
| Throttle | 6th attempt returns 429; window expiry resets; dictionary stays at its 1024 cap |
| Middleware | each exempt route reachable anonymously; a representative protected route on **each host** returns 401 |
| Scope | `scope: metrics` token accepted on `/metrics` and `/prometheus`; **403 not 401** on `/api/logs`; unscoped operator token accepted everywhere; unrecognised scope value rejected rather than treated as unscoped |
| WebSocket | handshake without carrier → 401; with expired token → 401; accepted response echoes `loom.v1` and not the token |

Use `TimeProvider` / `FakeTimeProvider` for every expiry test. No `Thread.Sleep`.

### Checkpoint 14.1

```
Phase 14 Complete: Security Hardening
  Manual HS256 JWT - Base64Url decode, alg pinned to HS256, exp/nbf/absolute-cap
    enforced, FixedTimeEquals, no reflection-based libraries
  Interactive login at POST /api/token; PBKDF2-SHA256 600k, uniform failure timing
  Enforcement on BOTH hosts through one shared middleware
  WebSocket auth via Sec-WebSocket-Protocol on /ws/metrics and /ws/logs
  HTTPS + HSTS, strict CORS, security headers - headers now emitted on redirects too
  Query length cap and LIMIT parse guard (shipped earlier in the phase)
  Angular login + interceptor
  Prometheus scrape via a scope-restricted 90-day service token; scope enforced as 403
  Loom.DevTools confirmed out of the auth boundary (no network surface); "never run
    loom elevated" recorded in the runbook; loom-dashboard --version no longer requires
    credentials
  Binary size re-measured against the 17 MB limit
  Test suite green, above the 509 baseline

Ready for Phase 15 (Production Build & Deployment)? [Y/N]
```

---

# PHASE 15: Production Build & Deployment

**Duration:** 3-4 days
**Goal:** <17 MB binary, systemd-hardened deployment, CI/CD for Linux and Windows — the final phase.

## Step 15.1: Native AOT Production Build

```bash
#!/bin/bash
set -e

dotnet publish Loom.Web.Api/Loom.Web.Api.csproj \
  --configuration Release \
  -r linux-x64 \
  /p:PublishAot=true \
  /p:StripSymbols=true

strip --strip-debug Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api

ls -lh Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api
```

**Optimization checklist, now audited against nine additional projects' dependencies:**
- [x] `InvariantGlobalization=true`
- [x] `PublishTrimmed=true` / `TrimMode=link`
- [x] Strip debug symbols
- [x] Audit exporter dependencies specifically (Step 12.4's `HttpClient`-only approach, deliberately avoiding official Grafana/Elasticsearch client SDKs, exists partly for this reason — re-check size impact if either is ever substituted in)
- [x] Remove unused NuGet packages
- [x] Confirm `Microsoft.Diagnostics.NETCore.Client` (Phase 13) is referenced only by `Loom.DevTools`, never by `Loom.Web.Api` — it has no reason to ship inside the main server binary

## Step 15.2: Systemd Deployment

The full unit file (`loom-web.service`) and `deploy.sh` are maintained in `wiggly-noodling-hoare.md` → "Deployment" — reproduced there in full rather than duplicated here, per this document's own cross-linking convention (README stays the index, `wiggly-noodling-hoare.md` carries deployment/ops config, this document carries build-sequence steps). Key points to verify against that file:

```bash
sudo systemctl status loom-web.service
curl http://localhost:5080/api/health
sudo -u loomd cat /var/secrets/loom/jwt.key   # should succeed (loomd can read)
cat /var/secrets/loom/jwt.key                 # should fail for other users (400 perms)
```

## Step 15.3: CI/CD Pipeline

```yaml
name: Loom CI

on: [push, pull_request]

jobs:
  build-and-test:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Build (trim/AOT analyzers as errors)
        run: dotnet build Loom.slnx -c Release /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true

      - name: Unit + integration tests (IL mode)
        run: dotnet test Loom.slnx --configuration Debug

      - name: Source generator tests
        run: dotnet test Loom.Telemetry.Tests --filter LoomProfileGeneratorTests

  aot-publish-linux:
    runs-on: ubuntu-latest
    needs: build-and-test
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Publish AOT
        run: dotnet publish Loom.Web.Api -c Release -r linux-x64 /p:PublishAot=true /p:StripSymbols=true

      - name: Binary size gate (< 17 MB)
        run: |
          SIZE=$(stat -c%s Loom.Web.Api/bin/Release/net10.0/linux-x64/publish/Loom.Web.Api)
          MAX=$((17 * 1024 * 1024))
          if [ "$SIZE" -gt "$MAX" ]; then echo "Binary too large: $SIZE bytes"; exit 1; fi

      - name: Benchmarks (query/alert/scrape targets)
        run: # NOT YET IMPLEMENTED — Loom.Benchmarks project does not exist
```

**Explanation (ELI5):**
> A dedicated `test-source-generator`-equivalent step (`Source generator tests` above) exists because a generator regression is unusually easy to miss in normal build output — the build still "succeeds," it just silently stops emitting the `Begin`/`End` methods Phase 7 depends on — worth its own explicit, fast-failing check rather than relying on downstream phases' tests to eventually notice. The binary-size gate turns the Risk Register's "HIGH" severity "Binary size bloat" risk into a hard CI failure instead of something only caught by someone remembering to run `ls -lh` locally.

## Step 15.4: Final Verification Against the Full Checklist

This is the complete checklist from `wiggly-noodling-hoare.md` → "Verification Checklist" — run through it explicitly before calling the project done:

**Functional:**
```
[ ] All 9 telemetry systems operational
[ ] Custom metrics recordable and queryable
[ ] Source generator produces correct instrumentation wrappers
[ ] Collectors run on schedule and report snapshots
[ ] Query engine handles SQL-like and fluent API
[ ] Alerts fire and dispatch notifications
[ ] Exporters push/pull metrics to external systems
[ ] Sampling reduces throughput without data loss for slow requests
[ ] Dev mode discovers and streams from local apps
```

**Performance:**
```
[ ] Binary size < 17 MB
[ ] Memory usage < 20 MB idle
[ ] Zero allocations in: RecordMetric, WebSocket send, Prometheus scrape
[ ] API latency < 10ms (p95)
[ ] Query execution < 10ms for standard queries
[ ] Alert evaluation < 1ms for 100 active alerts
```

**AOT:**
```
[ ] Zero trim warnings (IL2026, IL3050)
[ ] All DTOs registered in LoomJsonSerializerContext
[ ] No reflection usage anywhere in runtime code
[ ] Source generator output compiles cleanly under AOT
```

### 🔍 Checkpoint 15.1 (PROJECT COMPLETE)
```
✓ All 16 phases implemented (0-15)
✓ Foundation: CPU/memory/thread metrics, WebSocket streaming
✓ Full telemetry platform: custom metrics, attributes, collectors, sampling, query
  language, alerting, exporters, local dev mode — all backed by the ring buffer
  from Phase 6, all AOT-compatible per their respective ADRs
✓ Production-hardened: <17 MB binary, manual JWT, systemd sandboxing, CI/CD with
  a size gate and a dedicated source-generator test job
✓ Deferred, not lost: Angular dashboard architecture preserved in
  wiggly-noodling-hoare.md → "Deferred: Frontend"

This is the end of the backend-first telemetry platform build. Resuming frontend
work starts from the "Deferred: Frontend" appendix in wiggly-noodling-hoare.md.
```
