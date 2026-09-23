# loom-dashboard

A local web dashboard for a running .NET process: live CPU, memory, GC and thread-pool
metrics, captured logs, queries and alerts. Point it at a process ID and it opens in your
browser.

## Install

```
dotnet tool install -g LoomDiagnostics.Dashboard --prerelease
```

`--prerelease` is needed while Loom is in preview.

## First run: credentials

Every dashboard endpoint needs a login, and the dashboard **will not start** without a
signing key and at least one user. There is no auto-generated fallback key. Create them
with the `loom` tool (`LoomDiagnostics.Cli`):

```
dotnet tool install -g LoomDiagnostics.Cli --prerelease
loom auth init              # creates a signing key and a users file, prints the env vars to set
loom auth add-user admin    # prompts for a password
```

`loom auth init` prints the `LOOM_JWT_KEY_FILE` and `LOOM_AUTH_USERS_FILE` values to set.
`loom auth init --persist` sets them permanently instead: in your user environment on
Windows, in your shell profile on Linux and macOS.

## Run

```
loom-dashboard <pid> [--port <n>]
```

It attaches to the process over EventPipe and opens `http://localhost:5209` in your browser.
If 5209 is taken it picks a free port; `--port` or `LOOM_DASHBOARD_PORT` sets one
explicitly, and then a taken port is an error rather than a fallback.

**What you see depends on the target:**

- **Any .NET process:** runtime counters (CPU, working set, GC heap, allocation rate, thread
  pool) and its `ILogger` output.
- **Apps using `LoomDiagnostics.Telemetry`:** also your own counters, gauges and histograms,
  and `[LoomProfile]` method timings.

## Security

- **Loopback only.** It binds `127.0.0.1` in code, so no setting can expose it to the
  network. For remote access use an SSH tunnel (`ssh -L 5209:localhost:5209 host`).
- It serves plain HTTP on that loopback port; the tunnel encrypts the hop that leaves the
  machine.
- Log in with a user from the users file. For a Prometheus scraper,
  `loom auth token --sub prometheus --scope metrics` mints a token that can read
  `/prometheus` and nothing else.

## Optional: LLM "Explain" for log records

Set `LOOM_LLM_API_KEY` to enable an Explain button on log records. Only the log's message
template and argument **names** are sent, never argument values. Without the key, the
feature is off and no LLM request is ever made.

## Requirements

.NET 10 or later. Source, docs and issues: https://github.com/m4ngk4n08/Project-Loom-v2
