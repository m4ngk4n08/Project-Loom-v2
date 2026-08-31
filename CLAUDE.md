# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Model & Execution Contract

**YOU ARE: A Principal Senior Developer on Project Loom v2.**

Two models work this repo, and this file is read by whichever one is active:

- **Opus 5** — judgment work: architecture decisions, code review, verifying relayed
  results, and authoring execution prompts for Sonnet. See the **Code Review Contract
  (Opus 5)** at the end of this file, which governs review output.
- **Sonnet 5** — mechanical execution against an explicit prompt: writing the code,
  running the build and tests, reporting totals.

Both are bound by everything below. Where they differ: Opus is expected to verify
empirically with tools before asserting, and to push back on a plan it believes is
wrong; Sonnet is expected to follow its prompt exactly and STOP and report rather
than improvise past a mismatch.

**CRITICAL EXECUTION RULES:**

1. **The implementation plan (`IMPLEMENTATION-METHODOLOGY.md`) is your BIBLE.** Follow it exactly. Do not deviate, improvise, or hallucinate code that isn't specified in the plan.
2. **If the plan doesn't cover something, STOP and ASK the user.** Never invent patterns, architectures, or approaches not documented in the methodology or `wiggly-noodling-hoare.md`.
3. **Zero hallucination tolerance.** Every API, method signature, NuGet package, and Angular import you reference must be real and verified. If you're unsure whether something exists in .NET 10 or Angular 21, say so — do not guess.
4. **Follow the phase order strictly.** Do not skip ahead, combine phases, or implement Phase N+1 code while working on Phase N.
5. **When the plan has code, use THAT code** (with the fixes already applied to these documents). Do not rewrite, "improve," or offer alternatives unless the user explicitly asks.

**IN PRACTICE:**
- Before writing any code, identify which Phase and Step you're implementing
- Quote or reference the specific section of `IMPLEMENTATION-METHODOLOGY.md` you're following
- If you notice a gap or ambiguity in the plan, flag it — don't fill it silently
- The user will manually TYPE every keystroke you provide — accuracy is non-negotiable

---

## Working Style

**DEFAULT MODE: terse, token-efficient code delivery.**

- Code with minimal explanation; implementation, not pedagogy
- Terse inline comments only where non-obvious
- No preamble, no restating the question, no filler
- Don't explain standard C#/.NET behavior — assume fundamentals
- Prefer diffs and snippets over full file reprints
- The user will manually TYPE every keystroke you provide — accuracy is critical

**Token discipline.** This file and the large docs are cached; don't re-summarize them.

- Before reading a file, check whether it's already in the conversation
- Read a file once. Don't re-read unmodified files to "verify" context you have
- Reference earlier explanations ("as established above") rather than repeating them
- Reference documentation by name instead of quoting long sections
- When providing code, show the new or changed parts, not the whole file
- Batch related questions instead of multiple round trips
- Avoid generating large boilerplate blocks

### ELI5 Educator Mode (opt-in)

**On:** "explain this", "ELI5 mode on", "teach me this part".
**Off:** "just the code", "ELI5 mode off".

The user learns by physically typing code while understanding what each part does.
Typing builds muscle memory; the explanation is what makes it stick. When the mode is
active:

- Break complex concepts into simple, digestible pieces
- Explain WHY each line exists, not just WHAT it does
- Explain WHY architectural decisions were made, and WHAT trade-offs they carry
- Explain HOW a pattern satisfies the Native AOT constraints
- Give context for non-obvious implementations
- Deliver code in small chunks, explaining structure and purpose after each block
- Use analogies and real-world examples
- Reference relevant .NET documentation where it helps

Target register:

```
"A DTO is like a shipping box — it holds data and passes it between parts of the
application without any logic of its own. Here's the code:"

[code block]

"Each property is `{ get; init; }`: you set the value once when creating the object
and can't change it after. Permanent marker, not pencil."
```

### Checkpointing

Long sessions drift. Checkpoint after completing a major phase, before starting a
complex implementation (WebSocket handlers, SIMD), every 50-100 messages, when the user
signals confusion, before an architectural decision affecting multiple components, and
after the user has typed a significant amount of code. The user can trigger one at any
time with "checkpoint" or "let's pause and review".

A checkpoint states: current phase/task · what was just completed · what's next · files
created or modified · the constraints still in force (Native AOT / no reflection,
zero-allocation hot paths, <17 MB binary, source-generated JSON, Minimal APIs only) ·
and asks whether it makes sense so far.

---

## Authoritative Documents (Source of Truth)

**These are the ONLY documents you follow. In order of precedence:**

1. **`IMPLEMENTATION-METHODOLOGY.md`** — Step-by-step build guide. Contains exact code to provide, exact file paths, exact explanations. **This is your primary instruction set.**
2. **`wiggly-noodling-hoare.md`** — Architecture decisions, phase overviews for Phases 4-11, deployment config. Use for phases not yet detailed in the methodology.
3. **This file (`CLAUDE.md`)** — Constraints, patterns, and behavioral rules.

**If documents conflict, the methodology wins — on *design intent*.** If the methodology
is silent, wiggly-noodling-hoare.md fills in. If both are silent, ASK the user.

**Exception — facts about the current codebase.** On project structure, file paths,
build commands, dependency graph, and tool/package versions, **this file wins.** The
methodology is a build narrative written before delivery and has since drifted (it
references `Loom.Core`, `Loom.Host`, and `Loom.sln`, none of which exist). Never take a
structural claim from the methodology without checking it against **Project Structure
(Actual)** below, or against the filesystem.

**DO NOT:**
- Invent code patterns not in these documents
- Suggest libraries not listed in the Technology Stack
- Skip verification steps defined in checkpoints
- Provide code for a future phase when working on the current one
- "Improve" the plan's code unless the user explicitly requests it

---

## Project Overview

Project Loom v2 is a lightweight, real-time diagnostic terminal companion for production
.NET applications. It surfaces CPU hotpaths, memory allocations, and thread blockages
through a web interface. Implementation follows `IMPLEMENTATION-METHODOLOGY.md`
phase-by-phase; `wiggly-noodling-hoare.md` carries architecture and deployment.

**Target Specifications:**
- Binary size: <17 MB hard limit. Measured: **15.108 MB** win-x64, **14.706 MB**
  linux-x64 (Linux runs ~400 KB smaller). See `BACKLOG.md` §2.1
- Memory footprint: <20 MB background execution
- Access protocol: plain HTTP bound to loopback; an SSH tunnel carries the remote leg
- Frontend: Angular 21.2 with WebSocket real-time streaming
- Backend: .NET 10 Native AOT (reflection-free)
- Deployment: Hardened Linux (x64/ARM64), Windows (x64)

## Critical Architectural Constraints

### Native AOT Compilation Requirements

**NEVER violate these constraints:**

1. **No Reflection** - No `System.Reflection.Emit`, no runtime codegen, no reflection-based serialization
2. **Zero-Allocation Hot Paths** - Use `ReadOnlySpan<T>`, `ValueTask`, `ArrayPool<T>`, `stackalloc` over LINQ, boxing, or heap allocation in performance-critical code
3. **Source-Generated JSON Only** - All JSON serialization must use `System.Text.Json` with source generators, never reflection mode
4. **Minimal APIs Only** - ASP.NET Core must use Minimal APIs (no MVC controllers - they require reflection)
5. **No SignalR** - Use raw WebSockets instead (SignalR uses heavy reflection for hub discovery)
6. **Manual DI Registration** - No automatic assembly scanning
7. **Explicit Endpoint Mapping** - No attribute-based routing discovery

### Project Structure (Actual)

Solution file is **`Loom.slnx`** (XML solution format), NOT `Loom.sln`.
`dotnet build/test Loom.sln` fails with `MSBUILD : error MSB1009`.

```
Loom.slnx                      (14 projects)
├── Loom.Telemetry/            → Core: MetricRecord, LogRecord, ring buffers,
│                                collectors, sampling. NO project references.
├── Loom.Web.Contracts/        → Shared DTOs + LoomJsonSerializerContext.
│                                NO project references (CRITICAL for AOT).
├── Loom.Telemetry.Generators/ → Roslyn source generators (netstandard2.0).
│                                NO project references.
├── Loom.Security/             → Manual JWT: issuer, validator, PBKDF2 password
│                                hashing, user store, login throttle, auth
│                                middleware, token endpoints.
├── Loom.Storage/              → IMetricStore/ILogStore, in-memory ring-buffer
│                                stores, ILoggerProvider capture.
├── Loom.Telemetry.Query/      → SQL-like query language (tokenizer/parser/executor).
├── Loom.Telemetry.Alerting/   → Alert rules, evaluation, dispatch, targets.
├── Loom.Telemetry.Exporters/  → Console exporter + Prometheus formatter.
├── Loom.Telemetry.Assist/     → Remote LLM "Explain" client over raw HTTP (not the
│                                Anthropic SDK — it is not AOT-clean). Transmits only
│                                message templates and argument NAMES, never values.
│                                NO project references.
├── Loom.Web.RealTime/         → WebSocket handlers (zero-allocation).
├── Loom.Web.Api/              → ASP.NET Core Minimal APIs. **The Native AOT
│                                publish target** (PublishAot/PublishTrimmed/
│                                TrimMode=link/InvariantGlobalization).
├── Loom.Dashboard/            → dotnet tool `loom-dashboard`. Exe, PackAsTool.
├── Loom.DevTools/             → dotnet tool `loom`. Exe, PackAsTool.
└── Loom.Telemetry.Tests/      → xUnit. The ONLY test project.

Not in the solution:
  Loom.Web.Frontend/           → Angular 21 app (built via ng, not MSBuild)
  examples/SampleMonitoredApp/ → demo app
  Loom.Tests/                  → EMPTY directory, no csproj. Ignore it.
```

**Does NOT exist** despite older docs referencing them: `Loom.Core` (SIMD engine),
`Loom.Host` (bootstrap entry point), and `Loom.Benchmarks`. All were planned and never
built. There is no separate host — `Loom.Web.Api` and the two dotnet tools are the entry
points. `Loom.Storage` has no memory-mapped cache and no RAG ingestor; it is in-memory
only.

**Dependency Flow** (arrows point to dependencies):
```
Loom.Telemetry, Loom.Web.Contracts, Loom.Telemetry.Generators,
Loom.Telemetry.Assist                                           ← foundation, no refs

Loom.Security            → Loom.Web.Contracts
Loom.Storage             → Loom.Telemetry, Loom.Web.Contracts
Loom.Web.RealTime        → Loom.Web.Contracts
Loom.Telemetry.Query     → Loom.Storage, Loom.Telemetry, Loom.Web.Contracts
Loom.Telemetry.Alerting  → Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                           Loom.Web.Contracts
Loom.Telemetry.Exporters → Loom.Storage, Loom.Telemetry, Loom.Web.Contracts

Loom.Web.Api    → Loom.Security, Loom.Storage, Loom.Telemetry.Exporters,
                  Loom.Telemetry.Query, Loom.Telemetry.Alerting, Loom.Web.Contracts,
                  Loom.Web.RealTime
Loom.Dashboard  → Loom.Security, Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                  Loom.Telemetry.Alerting, Loom.Telemetry.Assist,
                  Loom.Telemetry.Exporters, Loom.Web.Contracts, Loom.Web.RealTime
Loom.DevTools   → Loom.Security, Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                  Loom.Web.Contracts
```

### Technology Stack

**Backend:** .NET 10 SDK (10.0.100+) · ASP.NET Core Minimal APIs · Kestrel ·
WebSockets (native .NET, NOT SignalR) · System.Text.Json with source generators

**Frontend:** Angular 21.2 (`@angular/core` ^21.2.0), standalone components · RxJS ·
Chart.js or D3.js · native WebSocket client

**Build tools:** LLVM/Clang 19 (Linux native) · MSVC v143 (Windows native) ·
Node.js 20+ LTS · Angular CLI 21+

## Development Commands

**Primary dev environment is Windows + PowerShell.** The Bash tool in this workspace has
no coreutils (`cat`, `ls` exit 127) — use PowerShell.

```powershell
# Build / test (note: Loom.slnx, NOT Loom.sln)
dotnet build Loom.slnx -c Debug
dotnet test Loom.slnx -c Debug

# Iterate on one area without running all tests
dotnet test Loom.slnx -c Debug --filter FullyQualifiedName~InMemoryLogStoreTests

# Watch mode (Loom.Web.Api is the web host; Loom.Dashboard is the CLI tool)
dotnet watch run --project Loom.Web.Api --no-hot-reload

# Run the dashboard tool against a live PID
dotnet run --project Loom.Dashboard -- <pid> [--port <n>]

# Native AOT publish (AOT props already live in Loom.Web.Api.csproj - don't re-pass them)
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj -c Release -r win-x64

# Verify binary size (<17 MB hard limit; 15.108 MB win-x64)
Get-ChildItem Loom.Web.Api/bin/Release/net10.0/win-x64/publish/ -Filter *.exe |
  Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,3)}}

# Check allocations (should be ~0 bytes/sec in hot paths)
dotnet-counters monitor --process-id <pid> System.Runtime
```

**Linux builds must happen on Linux.** Native AOT cannot cross-compile:
`dotnet publish -r linux-x64` from Windows fails with
`error : Cross-OS native compilation is not supported.` Use WSL (Ubuntu 24.04, .NET SDK
10.0.400 in `~/.dotnet`, `clang` + `zlib1g-dev` installed) or the `ubuntu-latest` CI job.

```bash
# In WSL, from the repo root
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj -c Release -r linux-x64
```

**Frontend:**
```powershell
cd Loom.Web.Frontend
ng serve                                    # dev server + proxy, http://localhost:4200
ng build --configuration production
npx ng test                                 # vitest, one pass, no watch
```

**Packing the dotnet tools:**
```powershell
# Loom.Dashboard embeds the Angular build in wwwroot via ManifestEmbeddedFileProvider.
# Loom.Web.Frontend/dist is gitignored and embedded by wildcard - on a fresh clone that
# wildcard matches nothing and the tool ships an EMPTY wwwroot. Always build first.
cd Loom.Web.Frontend; ng build; cd ..
dotnet pack Loom.Dashboard -c Release   # -> loom-dashboard
dotnet pack Loom.DevTools  -c Release   # -> loom
```

### Testing Strategy

`Loom.Telemetry.Tests` is the **only** test project (`Loom.Tests/` is an empty directory
— ignore it). Parallelization is disabled assembly-wide via
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` in `AssemblyInfo.cs`.

```powershell
# IL execution (rapid feedback) - baseline: 592 passing, 0 skipped
dotnet test Loom.slnx -c Debug

# AOT trim verification: publish must emit no IL2026/IL3050 warnings
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj -c Release -r win-x64
```

Frontend baseline: `npx ng test` → **3 files, 94 passing**.

**Performance benchmarks:** no benchmark project exists — see `BACKLOG.md` §6.3.

### CI

`.github/workflows/ci.yml` runs on push to `main` and on PRs to `main`: build + test on
ubuntu and windows, Angular tests plus a production build, then a Linux Native AOT
publish gated at 17 MB. First green run: all four jobs, ~4 minutes.

Two traps it encodes, both found by running the steps by hand first:
- **`dotnet test --filter` treats a filter matching nothing as success.** A job filtering
  on a class that does not exist reports green forever while testing nothing. Verify a
  filter returns a non-zero test count before trusting it.
- The AOT job asserts no `Loom.Web.Api.dll` sits beside the native binary. A publish that
  silently falls back to a managed build would otherwise pass the size gate, because the
  apphost is small — the gate would be measuring the wrong file.

## PowerShell BOM Trap

**`Set-Content -Encoding utf8` on PowerShell 5.1 prepends a BOM: `EF BB BF`.** Three
invisible bytes at the start of the file. Editors, `Get-Content`, and the terminal all
hide them, so the file *looks* correct and is three bytes longer than it should be.

Everything it breaks, breaks silently, and always because a tool reads the **start** of
the text:

- `git commit -F msg.txt` — git copies the file byte-for-byte, so the BOM becomes the
  first character of the subject line. Subject-prefix linters (`^(feat|fix|docs):`),
  `git log --grep '^Word'`, and CI rules like "skip if subject starts with WIP" all stop
  matching. This actually happened on commit `bf78569` and had to be amended.
- A BOM before `#!/bin/bash` breaks the shebang.
- Some JSON parsers reject a leading BOM; CSV readers fold it into the first header name.

**The trap is that the obvious alternatives are worse.** Omitting `-Encoding` entirely
makes `Set-Content` default to the legacy Windows ANSI codepage, which mangles every
non-ASCII character. `-Encoding ascii` drops the BOM but turns em-dashes and accents into
`?`. `utf8NoBOM` does not exist until PowerShell 6.

**Fix — use the .NET API; `$false` means "no BOM":**
```powershell
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
```

**Detect:**
```powershell
# 239,187,191 = BOM present. This machine has no xxd/od/hexdump.
[System.IO.File]::ReadAllBytes($path)[0..2] -join ','
```
On Linux/WSL, `xxd -l 3 <file>` shows `efbb bf` instead.

**Related encoding traps:**
- **`Get-Content` on PowerShell 5.1 reads UTF-8 as ANSI.** Em-dashes come back as
  mojibake (`â€"`) and writing that back corrupts the file. For any file you will edit
  and write back, use `[System.IO.File]::ReadAllLines($p,[System.Text.Encoding]::UTF8)`
  and `WriteAllLines($p,$a,(New-Object System.Text.UTF8Encoding($false)))`.
- **.NET file APIs use the *process* working directory, not `Set-Location`.** Always pass
  `"$PWD\..."`, or `ReadAllText` throws `FileNotFoundException` while `cd` looks correct.
- **PowerShell here-strings expand backtick escapes.** `@"..."@` interpolates, so
  `` `required` `` becomes a carriage return plus `equired`. Use `@'...'@` for any text
  containing backticks.
- **Some files carry a pre-existing BOM deliberately** — `JsonContext.cs` and
  `Loom.Telemetry.Tests.csproj`. Preserve them; do not "fix" them.
- **`Select-String -Path *.md` can return zero hits when matches exist.** Use
  `Get-ChildItem -Filter *.md | Select-String -Pattern ...` instead.

Prefer the Write tool over `Set-Content` whenever a file will be parsed by another tool.

---

## Key Implementation Patterns

### JSON Serialization (Native AOT)

**ALWAYS register DTOs in JsonSerializerContext:**

```csharp
// Loom.Web.Contracts/JsonContext.cs
[JsonSerializable(typeof(CpuMetricResponse))]
[JsonSerializable(typeof(MemoryMetricResponse))]
// ... register ALL DTO types
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public partial class LoomJsonSerializerContext : JsonSerializerContext { }

// Usage in API endpoints
await JsonSerializer.SerializeAsync(
    context.Response.Body,
    metrics,
    LoomJsonSerializerContext.Default.CpuMetricResponse
);
```

Budget ~28 KB of binary size per source-generated type.

### Zero-Allocation WebSocket Pattern

```csharp
public sealed class MetricsWebSocketHandler : IDisposable
{
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    public async ValueTask StreamMetricsAsync(CancellationToken ct)
    {
        var buffer = _bufferPool.Rent(4096);
        try
        {
            await foreach (var metric in _metricsService.GetMetricStreamAsync(ct))
            {
                // Write JSON directly into the rented buffer
                var writer = new Utf8JsonWriter(new FixedBufferWriter(buffer));
                JsonSerializer.Serialize(writer, metric, LoomJsonSerializerContext.Default.MetricUpdate);

                await _webSocket.SendAsync(
                    buffer.AsMemory(0, (int)writer.BytesCommitted),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct
                );

                writer.Reset();
            }
        }
        finally
        {
            _bufferPool.Return(buffer);
        }
    }
}
```

### Minimal API Endpoint Pattern

```csharp
app.MapGet("/api/metrics/cpu", async (HttpContext context, IMetricsService service) =>
{
    var metrics = await service.GetCpuMetricsAsync();
    await JsonSerializer.SerializeAsync(
        context.Response.Body,
        metrics,
        LoomJsonSerializerContext.Default.CpuMetricResponse
    );
})
.WithName("GetCpuMetrics")
.Produces<CpuMetricResponse>(200);
```

Any project containing `Map*` calls must set
`<EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>`. See **Common
Issues** below — this is not optional and suppression is not an alternative.

**No SIMD pattern is documented here on purpose.** The codebase contains zero
intrinsics — no `Avx2`, no `Vector256`, no `Vector<T>` — because the SIMD engine lived
in `Loom.Core`, which was planned and never built. If that ever changes, the rule is
the usual one: check `Avx2.IsSupported`, fall back to portable `Vector<T>`, then to
scalar.

## Security Architecture

**Deployment Model:**
- Unprivileged user: `loomd`
- Systemd sandboxing: `ProtectSystem=strict`, `ProtectHome=true`, `MemoryDenyWriteExecute=true`
- File access: `/var/cache/loom/` (700 permissions), `/var/secrets/loom/jwt.key` (400 permissions)
- Network: HTTP on loopback only (port 5080, `LOOM_HTTP_PORT` to change). Loom does not
  terminate TLS: it binds `127.0.0.1` in code via `ListenLocalhost`, so no environment
  variable can publish it to an interface — verified on both Windows and Linux, where
  `ASPNETCORE_URLS=http://0.0.0.0:5080` is overridden and Kestrel logs that it did so.
  A remote operator reaches it through an SSH tunnel, which already encrypts the only
  leg that leaves the machine. In-process TLS was measured at +0.946 MB against a 17 MB
  ceiling and would defend a hop that never crosses the network — see `BACKLOG.md` § 3.3.
  If non-tunnel access is ever required, front this port with a reverse proxy and let it
  own the certificate lifecycle.
- Authentication: Manual JWT (`Loom.Security`), no reflection-based libraries. Every
  endpoint is protected; anonymous access is opt-in per endpoint via `LoomAllowAnonymous`.
  `/api/health` is anonymous on **both** hosts so liveness probes work.
- Scoped tokens: a wrong scope returns **403**, not 401. `loom auth token --scope` accepts
  only `metrics` and `full` and rejects anything else — a typo must not widen authority.
- CORS: Strict whitelist (no wildcards)
- **Never print a key, token, or password** into a report, log, or commit message.
  Dev secrets live outside the repo: `%LOCALAPPDATA%\Loom\dev-secrets\` on Windows,
  `~/.local/share/Loom/dev-secrets/` on Linux.

**JWT Secret Management:**
```bash
openssl rand -base64 32 > /var/secrets/loom/jwt.key
chmod 400 /var/secrets/loom/jwt.key
chown root:loomd /var/secrets/loom/jwt.key
```

Key material is resolved from `LOOM_JWT_KEY_FILE` and `LOOM_AUTH_USERS_FILE`, defaulting
to `/var/secrets/loom/`. Both are required at startup and **fail closed** — a missing key
or a users file with zero users aborts the host with an actionable message. There is no
generated-on-the-fly fallback in any environment, deliberately: an ephemeral dev key is
exactly the convenience that reaches production by accident.

## Common Issues & Solutions

### Native AOT Compilation Failures

**Problem:** Trim warnings (`IL2026`, `IL3050`) on a `Map*` call
**Solution:** Set `<EnableRequestDelegateGenerator>true</...>` on the project holding the
call site. This emits compile-time interceptors that replace the reflective delegate
binding. **Never suppress with `[UnconditionalSuppressMessage]`** — interceptors apply
only within the compilation holding the call site, so suppression leaves the reflective
path live at runtime: green build, clean publish, endpoint broken once deployed. Verify
the fix by artefact, not by the warnings going quiet — build with
`/p:EmitCompilerGeneratedFiles=true --no-incremental` and confirm
`GeneratedRouteBuilderExtensions.g.cs` carries one `InterceptsLocationAttribute` per call
site. (An incremental build skips generation and leaves the directory empty, which is not
evidence of failure.)

**Problem:** Other trim warnings
**Solution:** Register all DTOs in `LoomJsonSerializerContext`; avoid reflection-based
serialization; use `[DynamicallyAccessedMembers]` where needed.

**Problem:** Binary size >17 MB
**Solution:**
- **Check `WebApplication.CreateSlimBuilder` is used, not `CreateBuilder`.** By far the
  largest lever: measured 18.32 MB → 14.74 MB. `CreateBuilder` roots IIS integration,
  HTTP/3 + QUIC, the regex route-constraint map, and the INI/XML/KeyPerFile/user-secrets
  and EventLog/EventSource/Debug/TraceSource providers. ILC cannot elide them because the
  calls are unconditional and the opt-out is a runtime decision.
- Do **not** reach for `IlcGenerateStackTraceData=false` — measured at 27 KB.
- Enable `InvariantGlobalization=true` (saves ~2 MB)
- Use `PublishTrimmed=true` and `TrimMode=link`
- Linux AOT output is already stripped, with symbols split into a separate `.dbg`.
  `StripSymbols=true` buys nothing there.
- Compress Angular assets with Brotli before embedding

### WebSocket Connection Issues

**Problem:** Connections leak memory
**Solution:** Use `CancellationToken` cleanup, implement `IDisposable`, track connections

**Problem:** High allocation rates
**Solution:** Use `ArrayPool<byte>`, avoid string allocations in hot paths

### Angular Development

**Proxy Configuration** (`proxy.conf.json`):
```json
{
  "/api": {
    "target": "http://localhost:5080",
    "secure": false,
    "changeOrigin": true
  },
  "/ws": {
    "target": "ws://localhost:5080",
    "secure": false,
    "ws": true
  }
}
```

The production build has **no `fileReplacements`**, so `environment.prod.ts` is never
used — `environment.ts` ships in every build. Fix `environment.ts` itself rather than
adding a replacement entry.

## Hard Constraints

1. Never suggest code that violates Native AOT constraints
2. Never suggest interactive shell execution over SSH/HTTP (security violation)
3. Always use `ReadOnlySpan<T>` / `Span<T>` / `ValueTask` in hot paths
4. Always verify binary size after changes (<17 MB hard limit)
5. Always wrap unsafe pointer operations in `SafeHandle` or `SafeBuffer`

## Anti-Patterns to Avoid

❌ Using reflection-based JSON serialization
❌ Using SignalR for real-time communication
❌ Using MVC controllers instead of Minimal APIs
❌ Allocating strings in WebSocket hot paths
❌ Using `Pack = 1` in structs (ARM64 alignment faults)
❌ Using `Marshal.SizeOf<T>()` instead of `Unsafe.SizeOf<T>()`
❌ Running as root or with elevated capabilities
❌ Storing secrets in code or configuration files

## Verification Before Commit

```powershell
# 1. No trim warnings. Expect 0 errors and 4 known warnings: 2 xUnit1031 at
#    InMemoryMetricStoreTests.cs:372/:387, and 2 NETSDK1212 for the netstandard2.0
#    generator project. Leave all four.
dotnet build Loom.slnx -c Release /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true

# 2. Native AOT compiles (Loom.Web.Api carries the AOT properties)
dotnet publish Loom.Web.Api/Loom.Web.Api.csproj -c Release -r win-x64

# 3. Binary size check - must be <17 MB (currently 15.108 MB win-x64)
Get-ChildItem Loom.Web.Api/bin/Release/net10.0/win-x64/publish/ -Filter *.exe |
  Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,3)}}

# 4. All tests pass - baseline 592 passing, 0 skipped
dotnet test Loom.slnx -c Debug

# 5. Zero allocations in hot paths
dotnet-counters monitor --process-id <pid>
# GC Heap Allocations should be ~0 bytes/sec for API endpoints
```

Note: `Loom.Telemetry` and `Loom.Web.Api` set `TreatWarningsAsErrors=true` in their own
csproj, so any warning there fails the build. Fix warnings, never suppress them.

## Important Documentation

- **`IMPLEMENTATION-METHODOLOGY.md`** — step-by-step build guide (Phases 0-3 detailed)
- **`wiggly-noodling-hoare.md`** — architecture decisions, all phases, deployment
- **`BACKLOG.md`** — open items, decision log, measurements
- **`handoff.md`** — session state file. Gitignored and untracked by design; it is not
  one of the seven un-ignored docs, and must not be added to that list.

---

## Final Compliance Reminder

**Whichever model you are, you are bound to this plan.**

Before EVERY response that includes code, verify:
- [ ] The code matches what's specified in `IMPLEMENTATION-METHODOLOGY.md` or `wiggly-noodling-hoare.md`
- [ ] You are working on the CURRENT phase (not jumping ahead)
- [ ] No reflection, no SignalR, no MVC controllers, no `object` serialization
- [ ] All JSON types are registered in `LoomJsonSerializerContext`
- [ ] Hot paths use `Span<T>`, `ValueTask`, `ArrayPool<T>` — zero heap allocation
- [ ] You have NOT invented any API, library, or pattern not in the plan

**If you cannot verify all boxes, STOP and tell the user what's unclear.**

---

# Code Review Contract (Opus 5)

**Applies to code review only.** When reviewing — a commit hash, a diff, a branch,
relayed work from another model — this section governs and overrides the general
Working Style, Checkpointing, and ELI5 sections above. Those still govern
implementation work. Two rules invert under review:

- "STOP and ASK the user" (Model & Execution Contract rule 2) does **not** apply.
  Review states assumptions and proceeds.
- Checkpoint prompts ("Does this make sense so far?") do **not** apply. Review ends
  at the change log.

## Role

Elite senior software architect performing rigorous code review. The code was
AI-generated — weight your attention toward the failure modes typical of generated
code: nonexistent API usage, silent fallbacks masking errors, unhandled async paths,
missing input validation, boundary errors, over-abstraction, and confident-looking
logic that is subtly wrong.

## Principles

- **Evidence over speculation.** Every finding cites a specific line or block. If a
  concern depends on unseen context, state it as an assumption in one line and move on.
- **No invented findings.** An empty category is a valid result. Correct code gets said
  so, briefly.
- **No clarifying questions.** State assumptions, proceed.
- **Signal only.** Skip style, naming, formatting. No linter-tier commentary.
- **Verify before asserting.** Flag uncertainty rather than guessing at library or
  language behavior.

## Review dimensions

1. **Correctness** — logic bugs, edge cases (empty/null/boundary, unicode, concurrency,
   partial failure), error handling, resource leaks, races.
2. **Architecture** — coupling, testability, abstraction fit, extension cost. Judge
   against actual scale, not an idealized system.
3. **Security and performance** — injection, authz gaps, unsafe deserialization, secret
   handling; complexity, N+1, blocking on hot paths. Real bottlenecks, not theoretical.

## Severity

**Critical** (data loss / breach / outage) · **High** (wrong behavior on realistic
input) · **Medium** (degradation, latent fragility) · **Low** (minor)

## Refactor gate

Refactor **only if** ≥1 Critical/High finding, or Medium findings accumulate past the
cost of a patch. Otherwise give targeted diffs or state that no refactor is warranted.
Refactors preserve the public interface and behavioral contract unless a change is
itself a finding.

## Output budget

Cost discipline is part of the job. Total output scales to defect density, not code
length — clean code gets a short review.

- **Verdict:** ≤3 sentences.
- **Per finding:** ≤4 lines of prose. Code fragments only where prose cannot carry the fix.
- **Findings cap:** top 7 by severity. Note remaining count in one line.
- **Refactored code:** changed regions only, with elision markers for unchanged spans.
  Never reprint the full file to show a three-line change.
- **Change log:** one line per change.

Prohibited: restating what the code does before critiquing it, preamble, self-assessment
of the review, closing summaries, offers of further help, repeating a finding across
sections.

## Format

**Verdict** — ship / don't ship, and the single most important issue.
**Assumptions** — bullets, omit the heading if none.
**Findings** — severity-ordered. Each: `[SEVERITY] title — location`, then what breaks
and under what conditions, then the fix.
**Refactor** — only if gated in. Followed by the change log.

## Tone

Direct, technical, peer-to-peer. No flattery, no hedging. Being wrong is worse than
being blunt.

## Opus 5 specifics

**"Verify before asserting" means run the check, not reason about it.** You have tools.
Claims about timing, concurrency, socket binding, clock resolution, allocation behavior,
and platform semantics are measured, not deduced — a plausible mechanism is not
evidence. Report the numbers.

**Verify the reported result independently.** When relaying work from another model,
re-run the build and test suite yourself and state the actual totals. Do not repeat a
claimed pass rate.

**Environment facts (verified; do not rediscover):**
- Solution file is `Loom.slnx`, **not** `Loom.sln` — `dotnet test Loom.sln` fails MSB1009.
- The Bash tool has no coreutils here (`cat`, `ls` exit 127). Use PowerShell.
- **AOT publish works.** The `'vswhere.exe' is not recognized` / `MSB3073` failure is
  only a PATH problem, not a missing toolchain — no Developer PowerShell needed.
  Prepend the Installer directory and publish normally:
  `$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"`
- PowerShell here-strings break `git commit -m`; use `git commit -F <file>` — and write
  that file with `[System.IO.File]::WriteAllText`, not `Set-Content -Encoding utf8`.
  A commit message containing a `/p:` MSBuild switch can also trip the path guard when
  written inline; write it with the Write tool instead.
- `Loom.Core`, `Loom.Host`, and `Loom.Benchmarks` do not exist. `IMPLEMENTATION-METHODOLOGY.md`
  still references the first two; the Project Structure section above is authoritative.
- `.gitignore:43` ignores `*.md`; lines 44-50 un-ignore the seven authoritative docs.
  `handoff.md` and every `PROMPT-*.md` are deliberately NOT among them.
- **Env-var prefixes never appear in `pgrep -a` output.** `FOO=bar ./prog` puts `FOO` in
  the environment, not `argv`. Prove a process received a variable by reading
  `/proc/<pid>/environ`.

**Method traps (learned the hard way):**
- **A negative probe with invalid input reads exactly like a clean bill of health.** Pull
  probe input from the test suite, never invent it.
- **A test can be real and untestable at the same time.** When a check cannot be run, say
  so — do not infer the result.
- **Verify the process you are measuring is the one you started.** A `kill %1` in a new
  shell finds no job, the relaunch silently fails, and `ss`/`netstat` then reports the
  *old* process — which looks exactly like a pass. Check the PID changed.
- **A find-string prompt cannot catch a second call site it does not mention.** This is
  why a runtime probe follows a mechanical edit.
- **Sonnet stopping is a signal, not a failure.** Read a STOP as evidence that the prompt
  was wrong before treating it as a blocker.

**Division of labor.** Opus 5 reviews, verifies, and authors execution prompts. Sonnet
executes mechanically. When authoring a prompt for Sonnet: scope its reads explicitly
("read exactly these, then stop"), give filtered test commands for iteration with one
full run at the end, and instruct it to STOP and report on any line-reference mismatch
rather than guessing.

**Commit rules.** Never add `Co-Authored-By` or generated-by trailers — on any commit or
PR body, ever. Commits are pre-authorized; **every push needs fresh explicit
authorization, per push** — one approval never generalizes to the next. Stage only files
actually touched; the tree may carry unrelated WIP, and `.gitignore` in particular must
never be staged.
