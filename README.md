# Loom

[![CI](https://github.com/m4ngk4n08/Project-Loom-v2/actions/workflows/ci.yml/badge.svg)](https://github.com/m4ngk4n08/Project-Loom-v2/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

**Live telemetry for .NET apps, without the setup.** Add one package, mark the methods you
care about, and watch timings, your own metrics, logs and runtime health, in the terminal or
in a local web dashboard.

- **Compile-time instrumentation.** `[LoomProfile]` is expanded by a source generator. No
  reflection, no startup cost, and it works under Native AOT.
- **Nothing to configure in your app.** No exporter, no collector, no endpoint. The tools
  attach to your running process through .NET's built-in diagnostics channel (EventPipe).
- **Works on any .NET process too.** Even without the package, you get CPU, memory, GC,
  thread pool and `ILogger` output.
- **Local and locked down.** The dashboard listens on `127.0.0.1` only and every endpoint
  needs a login.

> **Preview.** Loom is at `1.0.0-preview.1` and not yet on nuget.org. Until it is, build the
> packages yourself (see [Building from source](#building-from-source)). APIs may still change.

---

## What's in the box

| Package | Install it if you want to… |
|---------|----------------------------|
| [`LoomDiagnostics.Telemetry`](./Loom.Telemetry/PACKAGE.md) | Instrument your app: `[LoomProfile]`, `[LoomTrack]`, counters, gauges, histograms |
| [`LoomDiagnostics.Cli`](./Loom.DevTools/PACKAGE.md) | Use the `loom` command: find processes, watch metrics, query, read logs, manage credentials |
| [`LoomDiagnostics.Dashboard`](./Loom.Dashboard/PACKAGE.md) | Use the `loom-dashboard` command: a web UI for one running process |
| `LoomDiagnostics.Dashboard.AspNetCore` | Host the dashboard's API inside your own ASP.NET Core app (API only, no UI) |

All four target **.NET 10** and are versioned together.

---

## Quick start

### 1. Instrument your app

```bash
dotnet add package LoomDiagnostics.Telemetry --prerelease
```

```csharp
using Loom.Telemetry;

public class OrderService
{
    [LoomProfile]                                   // times every call: "OrderService.PlaceOrder"
    public void PlaceOrder(string region)
    {
        LoomMetrics.RecordCounter("orders.placed", 1, new MetricTag("region", region));
        LoomMetrics.RecordHistogram("orders.value", 129.90);
    }
}
```

That's all. No registration call, no configuration file. Run your app as usual.

### 2. Look at it from the terminal

```bash
dotnet tool install -g LoomDiagnostics.Cli --prerelease

loom dev                 # lists running apps that use Loom, with their PIDs
loom explore <pid>       # every metric and its latest value
loom watch <pid>         # stream metric events live
```

`loom explore` shows your metrics (`orders.placed`, `orders.value`,
`OrderService.PlaceOrder`) next to runtime counters such as `cpu-usage`, `gc-heap-size` and
`threadpool-queue-length`.

### 3. Open the dashboard

The dashboard requires a login, so create credentials once:

```bash
dotnet tool install -g LoomDiagnostics.Dashboard --prerelease

loom auth init --persist     # creates a signing key + users file, sets the env vars for you
loom auth add-user admin     # prompts for a password
```

Open a new terminal so the variables take effect, then:

```bash
loom-dashboard <pid>
```

It opens `http://localhost:5209` in your browser. Log in as `admin`.

> Prefer not to change your environment permanently? Run `loom auth init` without
> `--persist`. It prints the two variables (`LOOM_JWT_KEY_FILE`, `LOOM_AUTH_USERS_FILE`)
> to set in the current shell.

---

## Recording metrics

Everything lives in the `Loom.Telemetry` namespace.

```csharp
LoomMetrics.RecordCounter("orders.placed", 1);                  // something happened N times
LoomMetrics.RecordGauge("queue.depth", queue.Count);            // a current value
LoomMetrics.RecordHistogram("payment.latency_ms", elapsedMs);   // a distribution

// Tags split a metric by dimension
LoomMetrics.RecordCounter("orders.placed", 1,
    new MetricTag("region", "eu"), new MetricTag("channel", "web"));
```

### `[LoomProfile]`: time a method

```csharp
[LoomProfile]                                  // metric name: "ClassName.MethodName"
public Task<Receipt> ChargeAsync(Order o) { … }

[LoomProfile(Name = "Checkout.Charge")]        // or pick the name
public Task<Receipt> ChargeAsync(Order o) { … }
```

Every call records its duration. A call that throws also counts toward
`<name>.errors`.

**Calling through an interface?** Put the attribute on the **interface** method. That is the
usual dependency-injection case: `[LoomProfile]` intercepts calls by what they resolve to at
compile time, and a call through `IOrderService` resolves to the interface, not your class.
If you tag only the class, the compiler warns you (`LOOM0001`).

### `[LoomTrack]`: watch a property

```csharp
public partial class OrderService              // must be partial
{
    [LoomTrack]                                // records a gauge whenever the value changes
    public int OrdersPerMinute { get; set; }
}
```

### What it costs

Measured per call on .NET 10 (bytes allocated on the calling thread, after warm-up):

| Path | Bytes per call |
|------|---------------:|
| `RecordCounter` / `RecordHistogram`, no tags | 0 |
| `[LoomProfile]` method, normal return | 0 |
| `RecordCounter` / `RecordHistogram`, 1 tag | 40 |
| `RecordCounter` / `RecordHistogram`, 2 tags | 56 |
| `RecordGauge`, no tags / 1 tag | 32 / 296 |
| `[LoomProfile]` method that throws | ~552 (the same throw without Loom costs 296) |

The zero rows are enforced by tests, and natively by a Native AOT check in CI. Tags cost
the `params` array, which Loom keeps in its ring buffer.

### More in the package

- `LoomSampling.Configure(...)`: sampling rules by name pattern, duration threshold or rate.
- `LoomCollectors.Register(...)`: plug in your own `ILoomCollector` to publish metrics
  from another system.
- `AddLoomTelemetry()`: an optional `IServiceCollection` registration for hosts that want the
  options object in DI.

---

## The dashboard

`loom-dashboard <pid> [--port <n>]` attaches to one process and serves:

- **Metrics:** live CPU, memory, GC and thread pool, plus your own metrics and method timings.
- **Logs:** everything the target writes through `ILogger`, with search, tail and export.
- **Queries:** a SQL-like language over captured data, e.g.
  `SELECT method, AVG(duration) FROM telemetry`.
- **Alerts:** built in. CPU averaging over 80% for a minute, or working set averaging over
  500 MB for five minutes. Alerts print to the console. Set `LOOM_ALERT_WEBHOOK_URL` to also
  POST them to a webhook.

**Ports.** The default is 5209. If 5209 is taken, it picks a free port. `--port <n>` or
`LOOM_DASHBOARD_PORT` sets one explicitly, and then a taken port is an error.

**Remote servers.** The dashboard only listens on `127.0.0.1`. Reach it over SSH:

```bash
ssh -L 5209:localhost:5209 you@server     # then browse http://localhost:5209
```

**Prometheus.** Mint a token that can only read the scrape endpoint:

```bash
loom auth token --sub prometheus --scope metrics --ttl 90d
```

Then point Prometheus at `http://localhost:5209/prometheus` with that bearer token.

**Explain a log record (optional).** Set `LOOM_LLM_API_KEY` and log records get an Explain
button. Only the message template and argument **names** are sent, never argument values.
Without the key the feature doesn't exist, and no LLM request is ever made.

---

## The `loom` CLI

```text
loom dev [--all]                         find Loom-instrumented processes (--all: every .NET process)
loom dev --dashboard                     launch loom-dashboard
loom watch <pid> [--raw]                 stream metric events
loom explore <pid>                       list every metric and its latest value
loom metrics <pid> [cpu|memory|thread]   formatted metrics; --live for a refreshing view
loom query <pid> "SELECT ..."            run a query against a live process
loom logs <pid> [--count N] [--category X] [--seconds N]
loom search <pid> "<query>" [--max N] [--seconds N]
loom auth init | add-user | hash | token  credentials for loom-dashboard
```

Run `loom` with no arguments for the full usage. `loom` opens no port and needs no token:
it talks to the target process directly, so it can see exactly what your OS user can see.
Don't run it elevated.

---

## Hosting the dashboard in your own app

`LoomDiagnostics.Dashboard.AspNetCore` gives you the same API as `loom-dashboard`, inside
your own ASP.NET Core host. It ships no UI.

```csharp
using Loom.Dashboard.Extensions;

int targetPid = int.Parse(args[0]);             // the process to monitor

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddLoomDashboard(targetPid);    // throws if the key or users file is missing
builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(5209));

var app = builder.Build();
app.UseLoomDashboardSecurityHeaders();           // CSP etc. on Loom's routes only
app.UseWebSockets();
app.UseRouting();
app.UseLoomDashboard();                          // authentication
app.MapLoomDashboard(targetPid);                 // the endpoints
app.Run();
```

It needs the same `LOOM_JWT_KEY_FILE` and `LOOM_AUTH_USERS_FILE` as the tool. If your app
already maps its own catch-all fallback route, call `MapLoomDashboard(targetPid,
mapFallback: false)` to avoid a clash.

CI publishes exactly this host with Native AOT and drives it against a live process. The
publish shows three known trim warnings from Microsoft's `TraceEvent` dependency. None of
them is on the per-event path, and the published host ingests the same data as a normal
build.

---

## Native AOT

`LoomDiagnostics.Telemetry` is `IsAotCompatible`. Every push publishes a consumer app from
the packed `.nupkg` with Native AOT and fails on any `IL2026`/`IL3050` warning. The library
uses no reflection and no runtime code generation.

---

## Security

- **Loopback only.** The dashboard binds `127.0.0.1` in code. No environment variable or
  config setting can expose it to the network. Use an SSH tunnel, or a reverse proxy that
  owns TLS.
- **Every endpoint needs a login.** Only `/api/health`, the login routes and the UI shell
  are anonymous. A token with the wrong scope gets `403`.
- **Fails closed.** A missing signing key or users file stops the dashboard at startup,
  with a message saying what to fix. There is no auto-generated fallback key.
- **Passwords.** PBKDF2-SHA256 with 600,000 iterations. Unknown usernames take the same time
  as wrong passwords, and failed logins are throttled.
- **Tokens.** Sessions refresh up to a 12-hour cap. A refresh re-checks that the user still
  exists. `--scope` accepts only `metrics` or `full`, so a typo can't widen access.
- **Your secrets stay out of the repo.** `loom auth init` writes them under
  `%LOCALAPPDATA%\Loom\dev-secrets\` (Windows) or `~/.local/share/Loom/dev-secrets/` (Linux).
  Never commit them.

<details>
<summary><strong>HTTP API reference</strong></summary>

Every route needs `Authorization: Bearer <token>` unless marked anonymous.

```bash
TOKEN=$(curl -s -X POST http://localhost:5209/api/token \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"..."}' | jq -r .token)

curl -H "Authorization: Bearer $TOKEN" http://localhost:5209/api/metrics/cpu
```

| Route | Notes |
|-------|-------|
| `POST /api/token` | Anonymous. Log in, returns `{"token","expiresIn"}` |
| `POST /api/token/refresh` | Anonymous. Exchange a valid token for a fresh one (12-hour session cap) |
| `GET /api/health` | Anonymous, for liveness probes |
| `GET /api/session` | Attach metadata |
| `GET /api/metrics/cpu` · `/memory` · `/thread` | Runtime metrics |
| `POST /api/metrics/ingest` | Push metric batches |
| `WS /ws/metrics` | Live metric stream (~10 Hz) |
| `GET /api/logs` · `/categories` · `/tail` · `/export` | Captured logs |
| `POST /api/logs/search` | Search captured logs |
| `POST /api/logs/explain` | Only exists when `LOOM_LLM_API_KEY` is set |
| `WS /ws/logs` | Live log stream |
| `GET /api/query?q=…` · `POST /api/query` | Run a query |
| `GET /api/alerts` · `/api/alerts/{name}` | Alert status and history |
| `POST /api/alerts/{name}/test` · `PUT /api/alerts/{name}/silence` | Test or silence an alert |
| `GET /api/exporters/status` · `/metrics/names` · `/metrics/summary` | Exporter and metric summaries |
| `GET /prometheus` | Prometheus text format. Accepts a `metrics`-scoped token |

An unknown `/api/*` path returns `404`. Routes are defined in
`Loom.Dashboard.AspNetCore/Extensions/EndpointExtensions.cs`.

</details>

---

## Building from source

**You need:** the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Node.js 20+
(for the dashboard UI). For Native AOT publishing only: MSVC build tools on Windows, or
`clang` + `zlib1g-dev` on Linux.

```bash
git clone https://github.com/m4ngk4n08/Project-Loom-v2.git
cd Project-Loom-v2

dotnet build Loom.slnx
dotnet test  Loom.slnx                          # 849 tests

cd Loom.Web.Frontend && npm ci && npx ng build && cd ..   # the dashboard UI
dotnet run --project Loom.Dashboard -- <pid>              # run the dashboard from source
```

Without the `ng build` step the dashboard still runs, but API-only, with no UI.

**Release packages:** `./release.ps1` (PowerShell) checks that CI passed on the current
commit, rebuilds the UI from scratch, runs the strict build and both test suites, and writes
all four packages to `artifacts/release/`. It never publishes.

**Native AOT can't cross-compile.** Build `linux-x64` binaries on Linux (WSL, a container,
or CI), not from Windows.

<details>
<summary><strong>Repository layout</strong></summary>

```
Loom.slnx
├── Loom.Telemetry/              the library (packed as LoomDiagnostics.Telemetry)
├── Loom.Telemetry.Generators/   the source generator, shipped inside the library package
├── Loom.Dashboard.AspNetCore/   the dashboard API as a library
├── Loom.Dashboard/              the loom-dashboard tool (embeds the Angular UI)
├── Loom.DevTools/               the loom tool
├── Loom.Security/               JWT, password hashing, login throttle, auth middleware
├── Loom.Storage/                in-memory metric and log stores
├── Loom.Telemetry.Query/        the query language
├── Loom.Telemetry.Alerting/     alert rules and targets
├── Loom.Telemetry.Exporters/    Prometheus and console exporters
├── Loom.Telemetry.Assist/       the optional LLM "Explain" client
├── Loom.Web.Contracts/          shared DTOs and source-generated JSON
├── Loom.Web.RealTime/           WebSocket streaming
├── Loom.AotProbe/               Native AOT check (not a product)
├── Loom.TestFixtureApp/         target process for integration tests
└── Loom.Telemetry.Tests/        all tests

Loom.Web.Frontend/               Angular 21 dashboard UI
examples/SampleMonitoredApp/     a demo app to point the tools at
ci/                              packaged-consumer Native AOT gates
```

</details>

<details>
<summary><strong>What CI checks on every push</strong></summary>

| Job | What it proves |
|-----|----------------|
| Build & test (ubuntu, windows, macOS) | Strict build with trim/AOT analyzers as errors; full test suite |
| Angular tests | UI tests and a production build |
| Native AOT probe (linux-x64) | A consumer of the library AOT-publishes to a real native binary, and untagged recording allocates nothing |
| Packaged consumer AOT gate | The packed `.nupkg` (not project references) works under Native AOT |
| Packaged dashboard consumer AOT gate | The packed dashboard library AOT-publishes and serves a live target: fail-closed startup, login, alerts, queries, ingested data |

</details>

**Project docs:** [`BACKLOG.md`](./BACKLOG.md) (open items, decisions and the measurements
behind them) · [`TESTING.md`](./TESTING.md) · [`SMOKE-TEST.md`](./SMOKE-TEST.md) ·
[`wiggly-noodling-hoare.md`](./wiggly-noodling-hoare.md) (architecture) ·
[`CLAUDE.md`](./CLAUDE.md) (rules for AI-assisted work in this repo)

---

## License

MIT. See [LICENSE](./LICENSE).
