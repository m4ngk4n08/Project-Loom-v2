# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## The two rules

1. **Verify, don't assert.** Claims about timing, socket binding, allocation, clock
   resolution, binary size, and platform semantics are *measured with tools*, not
   reasoned about. A plausible mechanism is not evidence. Report the numbers. When
   relaying another model's work, re-run the build and tests yourself and state the
   actual totals — never repeat a claimed pass rate.
2. **When the plan is silent or wrong, say so — don't fill the gap silently.**
   `IMPLEMENTATION-METHODOLOGY.md` and `wiggly-noodling-hoare.md` carry design intent;
   the methodology wins on intent, wiggly-noodling fills gaps. But on *facts about this
   codebase* — paths, structure, commands, versions — **this file and the filesystem
   win.** The methodology was written before delivery and has drifted: it still
   references `Loom.Core`, `Loom.Host`, and `Loom.sln`, none of which exist.

## Working style

Terse. Code with minimal explanation, diffs over full files, no preamble, no restating
the question. Don't explain standard C#/.NET behavior. The user types every keystroke by
hand — accuracy matters more than volume. This file and the large docs are cached; don't
re-summarize them, and don't re-read a file already in the conversation.

**ELI5 mode** is opt-in ("explain this", "ELI5 on"; off with "just the code"). In that
mode: why each line exists, why the architectural trade-off, how the pattern satisfies
the AOT constraints. Small chunks, explanation after each block.

---

## Project structure (actual)

Solution file is **`Loom.slnx`** (XML format), NOT `Loom.sln` — `dotnet build/test
Loom.sln` fails with `MSBUILD : error MSB1009`.

```
Loom.slnx                      (14 projects)
├── Loom.Telemetry/            → Core: MetricRecord, LogRecord, ring buffers,
│                                collectors, sampling. NO project references.
├── Loom.Web.Contracts/        → Shared DTOs + LoomJsonSerializerContext.
│                                NO project references (CRITICAL for AOT).
├── Loom.Telemetry.Generators/ → Roslyn source generators (netstandard2.0). No refs.
├── Loom.Security/             → Manual JWT: issuer, validator, PBKDF2 hashing, user
│                                store, login throttle, auth middleware, token endpoints.
├── Loom.Storage/              → IMetricStore/ILogStore, in-memory ring-buffer stores,
│                                ILoggerProvider capture. In-memory only — no mmap
│                                cache, no RAG ingestor.
├── Loom.Telemetry.Query/      → SQL-like query language (tokenizer/parser/executor).
├── Loom.Telemetry.Alerting/   → Alert rules, evaluation, dispatch, targets.
├── Loom.Telemetry.Exporters/  → Console exporter + Prometheus formatter.
├── Loom.Telemetry.Assist/     → Remote LLM "Explain" client over raw HTTP (not the
│                                Anthropic SDK — it is not AOT-clean). Transmits only
│                                message templates and argument NAMES, never values.
│                                NO project references.
├── Loom.Web.RealTime/         → WebSocket handlers (zero-allocation).
├── Loom.AotProbe/             → Minimal console app. **The Native AOT publish target.**
│                                Proves referencing Loom.Telemetry doesn't break a
│                                consumer's AOT publish. Its binary size is NOT a
│                                product metric. Refs: Loom.Telemetry + its generator.
├── Loom.Dashboard/            → dotnet tool `loom-dashboard`. The only web host.
├── Loom.DevTools/             → dotnet tool `loom`. Exe, PackAsTool.
└── Loom.Telemetry.Tests/      → xUnit. The ONLY test project.

Not in the solution:
  Loom.Web.Frontend/           → Angular 21 app (built via ng, not MSBuild)
  examples/SampleMonitoredApp/ → demo app
  Loom.Tests/                  → EMPTY directory, no csproj. Ignore it.
```

**Does not exist** despite older docs referencing them: `Loom.Core` (SIMD engine),
`Loom.Host`, `Loom.Benchmarks` — planned, never built. `Loom.Web.Api` was retired; its
Native AOT proof moved to `Loom.AotProbe` (`BACKLOG.md` § 11.4). There is no separate
host: `Loom.Dashboard` and `Loom.DevTools` are the entry points.

**Dependency flow** (arrows point to dependencies):
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
Loom.AotProbe            → Loom.Telemetry, Loom.Telemetry.Generators

Loom.Dashboard  → Loom.Security, Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                  Loom.Telemetry.Alerting, Loom.Telemetry.Assist,
                  Loom.Telemetry.Exporters, Loom.Web.Contracts, Loom.Web.RealTime
Loom.DevTools   → Loom.Security, Loom.Storage, Loom.Telemetry, Loom.Telemetry.Query,
                  Loom.Web.Contracts
```

**Stack:** .NET 10 SDK (10.0.100+) · ASP.NET Core Minimal APIs · Kestrel · native
WebSockets (not SignalR) · System.Text.Json source generators · Angular 21.2 standalone
+ RxJS · Node 20+ LTS.

---

## Commands

Primary dev environment is **Windows + PowerShell**. The Bash tool here has no coreutils
(`cat`, `ls` exit 127) — use PowerShell.

```powershell
dotnet build Loom.slnx -c Debug
dotnet test  Loom.slnx -c Debug
dotnet test  Loom.slnx -c Debug --filter FullyQualifiedName~InMemoryLogStoreTests

dotnet watch run --project Loom.Dashboard --no-hot-reload -- <pid>
dotnet run --project Loom.Dashboard -- <pid> [--port <n>]

# Native AOT publish. AOT props live in Loom.AotProbe.csproj — don't re-pass them.
dotnet publish Loom.AotProbe/Loom.AotProbe.csproj -c Release -r win-x64

dotnet-counters monitor --process-id <pid> System.Runtime
```

**Linux builds must happen on Linux.** AOT cannot cross-compile — `-r linux-x64` from
Windows fails with `Cross-OS native compilation is not supported`. Use WSL (Ubuntu 24.04,
SDK 10.0.400 in `~/.dotnet`, `clang` + `zlib1g-dev`) or the `ubuntu-latest` CI job.

```powershell
cd Loom.Web.Frontend
ng serve                            # dev server + proxy, http://localhost:4200
ng build --configuration production
npx ng test                         # vitest, one pass, no watch

# Packing. Loom.Dashboard embeds the Angular build in wwwroot by wildcard, and
# Loom.Web.Frontend/dist is gitignored — on a fresh clone the wildcard matches
# nothing and the tool ships an EMPTY wwwroot. ALWAYS build the frontend first.
cd Loom.Web.Frontend; ng build; cd ..
dotnet pack Loom.Dashboard -c Release   # -> loom-dashboard
dotnet pack Loom.DevTools  -c Release   # -> loom
```

**CI** (`.github/workflows/ci.yml`, push + PR to `main`): build/test on ubuntu +
windows, Angular tests + prod build, Linux AOT publish of `Loom.AotProbe`. ~4 minutes.

---

## Native AOT constraints

Never violate these:

1. **No reflection** — no `Reflection.Emit`, no runtime codegen, no reflection-based
   serialization
2. **Zero-allocation hot paths** — `ReadOnlySpan<T>`, `ValueTask`, `ArrayPool<T>`,
   `stackalloc`; no LINQ, boxing, or string allocation in WebSocket/API hot paths
3. **Source-generated JSON only** — every DTO registered in `LoomJsonSerializerContext`
   (`Loom.Web.Contracts/JsonContext.cs`). Budget ~28 KB of binary per type.
4. **Minimal APIs only** — no MVC controllers
5. **Raw WebSockets** — no SignalR
6. **Manual DI registration** — no assembly scanning
7. **Explicit endpoint mapping** — no attribute-based route discovery
8. No `Pack = 1` on structs (ARM64 alignment faults); `Unsafe.SizeOf<T>()` not
   `Marshal.SizeOf<T>()`; wrap unsafe pointer ops in `SafeHandle`/`SafeBuffer`

There is **no SIMD in this codebase** — no `Avx2`, no `Vector256`, no `Vector<T>`. The
SIMD engine lived in `Loom.Core`, which was never built.

### The `Map*` trim-warning trap

`IL2026`/`IL3050` on a `Map*` call is fixed by setting
`<EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>` **on the project
holding the call site**. This emits compile-time interceptors replacing reflective
delegate binding.

**Never suppress with `[UnconditionalSuppressMessage]`.** Interceptors apply only within
the compilation holding the call site, so suppression leaves the reflective path live at
runtime: green build, clean publish, endpoint broken once deployed.

**Verify by artefact, not by the warning going quiet.** Build with
`/p:EmitCompilerGeneratedFiles=true --no-incremental` and confirm
`GeneratedRouteBuilderExtensions.g.cs` carries one `InterceptsLocationAttribute` per call
site. (An incremental build skips generation and leaves the directory empty — that is not
evidence of failure.)

### Binary size (historical)

The <17 MB limit and its CI gate sized `Loom.Web.Api`, now retired (`BACKLOG.md` § 11.4).
Measurements: **15.108 MB** win-x64, **14.706 MB** linux-x64. `Loom.AotProbe`'s size is
deliberately not gated. If a shipping AOT host is ever reintroduced, the levers, largest
first: `CreateSlimBuilder` over `CreateBuilder` (18.32 → 14.74 MB — `CreateBuilder`
unconditionally roots IIS integration, HTTP/3+QUIC, the regex route-constraint map, and
the INI/XML/KeyPerFile/user-secrets and EventLog/EventSource/Debug/TraceSource
providers); `InvariantGlobalization=true` (~2 MB); `PublishTrimmed` + `TrimMode=link`;
Brotli the Angular assets. Not worth it: `IlcGenerateStackTraceData=false` (27 KB),
`StripSymbols` on Linux (output is already stripped, symbols split into a `.dbg`).

---

## Security

- **Loopback only.** Kestrel binds `127.0.0.1` in code via `ListenLocalhost`, so no
  environment variable can publish it — verified on Windows and Linux, where
  `ASPNETCORE_URLS=http://0.0.0.0:5080` is overridden and Kestrel logs that it did so.
  Port 5080, `LOOM_HTTP_PORT` to change. Remote access is an SSH tunnel, which already
  encrypts the only leg leaving the machine. In-process TLS measured +0.946 MB to defend
  a hop that never crosses the network (`BACKLOG.md` § 3.3). For non-tunnel access, front
  it with a reverse proxy and let that own the certificate lifecycle.
- **Auth fails closed.** `LOOM_JWT_KEY_FILE` and `LOOM_AUTH_USERS_FILE` (default
  `/var/secrets/loom/`) are both required at startup; a missing key or a zero-user file
  aborts the host with an actionable message. There is no generated-on-the-fly fallback
  in any environment — deliberately. An ephemeral dev key is exactly the convenience that
  reaches production by accident.
- Every endpoint is protected; anonymous is opt-in per endpoint via `LoomAllowAnonymous`.
  `/api/health` is anonymous on both hosts so liveness probes work.
- Scoped tokens: wrong scope returns **403**, not 401. `loom auth token --scope` accepts
  only `metrics` and `full` — a typo must not widen authority.
- CORS: strict whitelist, no wildcards. Run unprivileged (`loomd`), never as root.
- **Never print a key, token, or password** into a report, log, or commit message. Dev
  secrets live outside the repo: `%LOCALAPPDATA%\Loom\dev-secrets\` (Windows),
  `~/.local/share/Loom/dev-secrets/` (Linux).

---

## Traps (verified — do not rediscover)

**`dotnet test --filter` treats a filter matching nothing as success.** A job filtering on
a class that doesn't exist reports green forever while testing nothing. Confirm a filter
returns a non-zero test count before trusting it.

**A publish can silently fall back to a managed build and look clean.** Assert no
`Loom.AotProbe.dll` sits beside the native binary. That is the entire point of the probe.

**AOT publish works on this machine.** `'vswhere.exe' is not recognized` / `MSB3073` is a
PATH problem, not a missing toolchain — no Developer PowerShell needed:
`$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"`

**`.gitignore:43` ignores `*.md`;** lines 44–50 un-ignore the seven authoritative docs.
`handoff.md` (session state, untracked by design) and every `PROMPT-*.md` are deliberately
NOT among them. Grep will silently miss project docs.

**Env-var prefixes never appear in `pgrep -a` output.** `FOO=bar ./prog` puts `FOO` in the
environment, not `argv`. Prove a process got a variable by reading `/proc/<pid>/environ`.

**Verify the process you are measuring is the one you started.** A `kill %1` in a new
shell finds no job, the relaunch silently fails, and `ss`/`netstat` then reports the *old*
process — indistinguishable from a pass. Check the PID changed.

**A negative probe with invalid input reads exactly like a clean bill of health.** Pull
probe input from the test suite; never invent it.

**A test can be real and untestable at once.** When a check cannot be run, say so — do not
infer the result.

### PowerShell encoding

**`Set-Content -Encoding utf8` on PS 5.1 prepends a BOM (`EF BB BF`).** Three invisible
bytes that editors, `Get-Content`, and the terminal all hide. `git commit -F msg.txt`
copies them into the subject line, breaking subject-prefix linters and `--grep '^Word'` —
this happened on `bf78569` and had to be amended. A BOM also breaks `#!/bin/bash`, and
some JSON/CSV parsers. The obvious alternatives are worse: omitting `-Encoding` uses the
legacy ANSI codepage, `ascii` turns em-dashes into `?`, and `utf8NoBOM` doesn't exist
before PS 6.

```powershell
# Write ($false = no BOM):
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
# Detect (no xxd/od/hexdump on this machine). 239,187,191 = BOM present:
[System.IO.File]::ReadAllBytes($path)[0..2] -join ','
```

- **`Get-Content` on PS 5.1 reads UTF-8 as ANSI** — em-dashes come back as `â€"` and
  writing that back corrupts the file. For read-modify-write use
  `[System.IO.File]::ReadAllLines($p,[System.Text.Encoding]::UTF8)` + `WriteAllLines`.
- **.NET file APIs use the process working directory, not `Set-Location`.** Pass
  `"$PWD\..."` or `ReadAllText` throws while `cd` looks correct.
- **Here-strings expand backtick escapes.** `@"..."@` interpolates, so `` `required` ``
  becomes CR + `equired`. Use `@'...'@`. Here-strings also break `git commit -m` — use
  `git commit -F <file>`, written with the Write tool.
- **`JsonContext.cs` and `Loom.Telemetry.Tests.csproj` carry a BOM deliberately.**
  Preserve them.
- **`Select-String -Path *.md` can return zero hits when matches exist.** Use
  `Get-ChildItem -Filter *.md | Select-String -Pattern ...`.

Prefer the Write tool over `Set-Content` for any file another tool will parse.

---

## Verification before commit

```powershell
# 1. Expect 0 errors and exactly 4 known warnings: 2 xUnit1031 at
#    InMemoryMetricStoreTests.cs:372/:387, 2 NETSDK1212 for the netstandard2.0
#    generator project. Leave all four.
dotnet build Loom.slnx -c Release /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true

# 2 + 3. AOT compiles, and no managed assembly sits beside the native output.
dotnet publish Loom.AotProbe/Loom.AotProbe.csproj -c Release -r win-x64
Get-ChildItem Loom.AotProbe/bin/Release/net10.0/win-x64/publish/ | Select-Object Name, Length

# 4. Baselines: 592 passing / 0 skipped backend; 3 files / 94 passing frontend.
dotnet test Loom.slnx -c Debug
cd Loom.Web.Frontend; npx ng test; cd ..
```

`Loom.Telemetry` and `Loom.AotProbe` set `TreatWarningsAsErrors=true` in their own csproj.
Fix warnings there, never suppress them.

Test parallelization is disabled assembly-wide via
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` in `AssemblyInfo.cs`.
No benchmark project exists (`BACKLOG.md` § 6.3).

## Commit rules

- **Never add `Co-Authored-By` or generated-by trailers** — no commit, no PR body, ever.
- Commits are pre-authorized. **Every push needs fresh explicit authorization, per push.**
  One approval never generalizes to the next.
- Stage only files actually touched. The tree may carry unrelated WIP, and `.gitignore`
  must never be staged.

## Frontend notes

`proxy.conf.js` targets `http://localhost:${LOOM_DASHBOARD_PORT || 5209}` for `/api`,
`/ws` (with `ws: true`), and `/prometheus`.

The production build has **no `fileReplacements`**, so `environment.prod.ts` is never
used — `environment.ts` ships in every build. Fix `environment.ts` itself rather than
adding a replacement entry.

---

## Code review

Elite senior architect, peer-to-peer, direct. Being wrong is worse than being blunt. The
code under review was AI-generated — weight attention toward the failure modes typical of
generated code: nonexistent API usage, silent fallbacks masking errors, unhandled async
paths, missing input validation, boundary errors, over-abstraction, and confident-looking
logic that is subtly wrong.

Under review, two general rules invert: **do not stop to ask clarifying questions** (state
assumptions, proceed) and **do not checkpoint** (end at the change log).

- **Evidence over speculation.** Every finding cites a line or block. Concerns depending
  on unseen context get one line as an assumption.
- **No invented findings.** An empty category is a valid result; correct code gets said so
  briefly. Skip style, naming, formatting.
- **Dimensions:** correctness (edge cases, error handling, resource leaks, races) ·
  architecture (coupling, testability, abstraction fit, judged against actual scale) ·
  security and performance (authz gaps, secret handling, blocking hot paths — real
  bottlenecks, not theoretical).
- **Severity:** Critical (data loss / breach / outage) · High (wrong behavior on
  realistic input) · Medium (degradation, latent fragility) · Low.
- **Refactor gate:** only if ≥1 Critical/High, or Medium findings accumulate past the cost
  of a patch. Otherwise give targeted diffs or say no refactor is warranted. Preserve the
  public interface unless changing it is itself a finding.
- **Output budget** — scales to defect density, not code length. Verdict ≤3 sentences ·
  ≤4 lines per finding · top 7 findings, remaining count in one line · changed regions
  only, never a full file reprint · one line per change in the log. No preamble, no
  restating what the code does, no closing summary, no offers of further help.
- **Format:** Verdict (ship / don't ship + the single most important issue) · Assumptions
  (omit if none) · Findings, severity-ordered, each `[SEVERITY] title — location` then
  what breaks and when, then the fix · Refactor, only if gated in.

**Authoring prompts for Sonnet:** scope its reads explicitly ("read exactly these, then
stop"), give filtered test commands for iteration with one full run at the end, and
instruct it to STOP and report on any line-reference mismatch rather than guessing.
**Sonnet stopping is a signal, not a failure** — read a STOP as evidence the prompt was
wrong before treating it as a blocker. A find-string prompt cannot catch a second call
site it does not mention, which is why a runtime probe follows every mechanical edit.

**Hand every Sonnet task a branch, never `main`.** Before writing the prompt, create and
check out `sonnet/<short-task-name>`; name that branch in the prompt's first line. This is
mechanical, not advisory: on 2026-09-02 two consecutive tasks committed and pushed after
being told in the prompt not to, once landing a startup regression on `main` directly. A
"do not push" instruction is worth writing, but it is not a control — being on a branch is,
because a push then goes somewhere harmless. Review the branch, then merge it yourself.

**Probe the failure path, not just the happy one.** The same 2026-09-02 regression turned a
missing signing key from an actionable message + exit 1 into an unhandled exception + exit
255. A strict build, 600 passing tests, and a live runtime probe of the *working* path were
all green throughout. Only running with a deliberately invalid `LOOM_JWT_KEY_FILE` found
it. Loom's security design is built on failing closed, so a check that never exercises a
refusal is not checking the part that matters.

## Docs

`IMPLEMENTATION-METHODOLOGY.md` (build guide, Phases 0–3) · `wiggly-noodling-hoare.md`
(architecture, all phases, deployment) · `BACKLOG.md` (open items, decision log,
measurements) · `handoff.md` (session state; gitignored and untracked by design — must
not be added to the un-ignored list).
