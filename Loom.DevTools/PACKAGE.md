# loom

The command-line companion to Loom. It attaches to a running .NET process over EventPipe to
show its metrics and logs in the terminal, runs Loom queries against it, and manages the
credentials `loom-dashboard` needs.

## Install

```
dotnet tool install -g LoomDiagnostics.Cli --prerelease
```

`--prerelease` is needed while Loom is in preview.

## Commands

```
loom dev [--all]                         find Loom-instrumented processes (--all: every .NET process)
loom dev --dashboard                     launch loom-dashboard (needs LoomDiagnostics.Dashboard)
loom watch <pid> [--raw]                 stream metric events
loom explore <pid>                       list every metric and its latest value
loom metrics <pid> [cpu|memory|thread]   formatted metrics; --live for a refreshing view
loom query <pid> "SELECT ..."            run a LoomQL query
loom logs <pid> [--count N] [--category X] [--seconds N]
loom search <pid> "<query>" [--max N] [--seconds N]    search captured logs
loom auth init | add-user | hash | token  credentials for loom-dashboard
```

Run `loom` with no arguments for the full usage.

Runtime metrics and `ILogger` output work for any .NET process. Your own metrics and
`[LoomProfile]` timings appear for apps that use `LoomDiagnostics.Telemetry`.

## Security

`loom` has **no network surface**: it talks to the target process directly through the
.NET diagnostics channel, so there is no token or port involved. Its boundary is the OS
user that owns the target process. Don't run it elevated.

`loom auth token --scope` accepts only `metrics` and `full`; anything else is rejected
rather than widened to full authority.

## Requirements

.NET 10 or later. Source, docs and issues: https://github.com/m4ngk4n08/Project-Loom-v2
