# Project Loom v2

A lightweight, **real-time diagnostic terminal companion** for production .NET applications. Loom gives you live insight into **CPU hotpaths**, **memory allocations**, and **thread blockages** through a modern web interface.

The successor to the original SSH-based design, Loom v2 pivots to an **HTTPS + Angular** architecture for better accessibility, richer visualizations, and easier deployment.

---

## Overview

Loom is a standalone diagnostic service you attach to a production .NET application. It profiles performance-critical behavior and streams the results to a web dashboard in real time:

- **CPU hotpaths** — which methods/paths consume the most CPU
- **Memory allocations** — what's using RAM, top allocation types, GC statistics
- **Thread blockages** — threads that are blocked/waiting and why
- **Diagnostic search** — vector (semantic) search over telemetry

---

## Key Constraint: Native AOT

Everything is built around **.NET 10 Native AOT** (reflection-free) compilation:

| Constraint | Why |
|-----------|-----|
| Binary size **< 15 MB** | |
| Memory footprint **< 20 MB** background | |
| **No reflection** | AOT can't do runtime codegen |
| **Zero-allocation hot paths** | `Span<T>`, `ValueTask`, `ArrayPool<T>` |
| **Source-generated JSON** | All DTOs registered in `LoomJsonSerializerContext` |
| **Minimal APIs only** | No MVC controllers (reflection-heavy) |
| **Raw WebSockets, no SignalR** | SignalR uses reflection at runtime |

---

## Technology Stack

**Backend**
- .NET 10 SDK (10.0.100+)
- ASP.NET Core **Minimal APIs** (Native AOT)
- Kestrel HTTP server
- System.Text.Json with **source generators**
- Raw **WebSockets** (native, not SignalR)
- Manual JWT authentication (no `System.IdentityModel.Tokens.Jwt`)

**Frontend**
- **Angular 19+** with standalone components
- RxJS for reactive data streams
- Chart.js for real-time visualizations
- Native WebSocket client

**Build Tools**
- MSVC v143 (Windows) / LLVM-Clang 19 (Linux) native compilation
- Node.js 20+ LTS & Angular CLI 19+

---

## Project Structure

```
Loom.sln
├── Loom.Web.Api/            → ASP.NET Core Minimal APIs (Native AOT)
├── Loom.Web.Frontend/       → Angular 19+ application
├── Loom.Web.Contracts/      → Shared DTOs + source-generated JSON (MANDATORY for AOT)
├── Loom.Web.RealTime/       → Zero-allocation WebSocket handlers
├── Loom.Core/               → SIMD math engine (AVX2/Neon)
├── Loom.Storage/            → Memory-mapped binary cache + RAG ingestor
├── Loom.Host/               → Bootstrap entry point
├── Loom.Tests/              → Unit & integration tests
└── Loom.Benchmarks/         → BenchmarkDotNet performance benchmarks
```

**Dependency flow:**
```
Loom.Host → Loom.Web.Api → Loom.Web.Contracts (shared DTOs, depended on by all)
                         → Loom.Web.RealTime → Loom.Web.Contracts
                         → Loom.Core ← Loom.Storage
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100+)
- Node.js 20+ LTS & npm 10+
- Angular CLI 19+ (`npm install -g @angular/cli@19`)
- Native compilation tools (MSVC v143 on Windows / Clang 19 on Linux)

### Backend (development, fast iteration)

```bash
dotnet test Loom.sln --configuration Debug
dotnet watch run --project Loom.Host --no-hot-reload
```

The API listens on `http://localhost:5080` (HTTPS: `https://localhost:5443` in production).

### Frontend

```bash
cd Loom.Web.Frontend
npm install
ng service
# Access at http://localhost:4200
```

### API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /api/health` | Health check (status, uptime, memory) |
| `GET /api/metrics/cpu` | CPU hotpath metrics |
| `GET /api/metrics/memory` | Memory allocation & GC stats |
| `GET /api/metrics/thread` | Thread activity & blockage analysis |
| `GET /api/diagnostics/search?q=...` | Semantic (vector) search |
| `POST /api/telemetry/ingest` | Accept incoming telemetry events |
| `WS /ws/metrics` | Real-time metric stream (~10 Hz) |

---

## Native AOT Production Build

```bash
cd Loom.Web.Frontend && ng build --configuration production --output-hashing all && cd ..
mkdir -p Loom.Host/wwwroot
cp -r Loom.Web.Frontend/dist/browser/* Loom.Host/wwwroot/

dotnet publish Loom.Host/Loom.Host.csproj \
  --configuration Release \
  -r linux-x64 \
  /p:PublishAot=true \
  /p:StripSymbols=true

strip --strip-debug \
  Loom.Host/bin/Release/net10.0/linux-x64/publish/Loom.Host

# Verify the binary stays under 15 MB
ls -lh Loom.Host/bin/Release/net10.0/linux-x64/publish/Loom.Host
```

---

## Verification Checklist

Before committing, the project verifies:

```bash
# 1. No trim/AOT warnings
dotnet build Loom.sln -c Release /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true

# 2. Native AOT compiles
dotnet publish Loom.Host -c Release -r linux-x64 /p:PublishAot=true

# 3. Binary size < 15 MB
ls -lh Loom.Host/bin/Release/net10.0/linux-x64/publish/Loom.Host

# 4. IL + AOT tests pass
dotnet test Loom.sln --configuration Debug

# 5. Zero allocations in hot paths
dotnet-counters monitor --process-id $(pidof Loom.Host) System.Runtime
```

---

## Implementation Phases

| Phase | Objective | Status |
|-------|-----------|--------|
| 0 | Project setup & tooling | Will start |
| 1 | Contracts & source-generated JSON | Will start |
| 2 | Web API core & health endpoint | Will start |
| 3 | Core API endpoints (metrics) | |
| 4 | WebSocket real-time streaming | |
| 5 | Angular frontend foundation | |
| 6 | Dashboard & visualizations | |
| 7 | Search & telemetry ingestion | |
| 8 | Production build & optimization | |
| 9 | Security hardening | |
| 10 | Systemd integration & deployment | |
| 11 | CI/CD pipeline (Linux & Windows) | |

---

## Key JSON Serialization Pattern

Every DTO is registered in `Loom.Web.Contracts/JsonContext.cs` with `[JsonSerializable]` attributes. Forgetting to register a type causes a **runtime crash** on serialize — Native AOT requires compile-time type registration:

```csharp
[JsonSerializable(typeof(CpuMetricResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class LoomJsonSerializerContext : JsonSerializerContext { }
```

---

## Security

- HTTPS enforcement + HSTS in production
- Manual JWT authentication (HS256)
- Strict CORS whitelist (no wildcards)
- Security headers (CSP, X-Frame-Options, etc.)
- systemd sandboxing (`ProtectSystem=strict`, `MemoryDenyWriteExecute`, etc.)
- Runs as unprivileged user `loomd` with least privilege
- JWT secret in `/var/secrets/loom/jwt.key` (mode 400)

---

## Documentation

- [`IMPLEMENTATION-METHODOLOGY.md`](./IMPLEMENTATION-METHODOLOGY.md) — authoritative step-by-step build guide
- [`wiggly-noodling-hoare.md`](./wiggly-noodling-hoare.md) — architecture decisions, all phases, deployment config
- [`commands.md`](./commands.md) — development commands
- [`skills.md`](./skills.md) — project skills guide

---

## License

Licensed under the MIT License. See the [LICENSE](./LICENSE) file for details.

<sub>**Note:** This project is currently in early implementation/planning. The repo contains only a `Contracts` project and planning documentation so far.</sub>