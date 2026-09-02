# Loom.Telemetry

Method-level telemetry for .NET that costs nothing at startup and survives Native AOT.

Annotate a method with `[LoomProfile]` and a Roslyn source generator emits the timing
wrapper at compile time. There is no reflection, no `Reflection.Emit`, no runtime code
generation, and nothing to scan when your process starts — the instrumentation is ordinary
C# by the time the compiler sees it.

## Install

```
dotnet add package LoomDiagnostics.Telemetry
```

The package ID is `LoomDiagnostics.Telemetry`; the namespace you write against is
`Loom.Telemetry`. They differ because the `Loom.` prefix on nuget.org belongs to unrelated,
long-established packages.

That is the whole wiring. The generator ships inside this package at
`analyzers/dotnet/cs/`, so a single `PackageReference` delivers both the attributes and the
code that acts on them.

## Use

```csharp
using Loom.Telemetry;

public partial class OrderService
{
    [LoomProfile(Name = "Order.Submit")]
    public void Submit(Order order)
    {
        // your code, unchanged
    }
}
```

**The class must be `partial`.** The generator emits the recording helpers into a second
declaration of your class, so a non-partial class fails to compile with `CS0260`. It cannot
be `sealed` and non-partial. `Name` is optional and defaults to `ClassName.MethodName`.

To collect what the wrappers record, register the runtime in any
`IServiceCollection`-based host:

```csharp
builder.Services.AddLoomTelemetry(options => { });
```

The `options` callback is currently required and `LoomTelemetryOptions` has no settings
yet, so an empty lambda is the correct call today.

`[LoomTrack]` does the same for a property, recording a metric whenever its value changes.

## What is in this package

The attributes, the source generator, the recording runtime (ring buffers, collectors,
sampling), and nothing else. `Loom.Telemetry` deliberately carries **zero** references to
other Loom assemblies — its only dependency is
`Microsoft.Extensions.DependencyInjection.Abstractions`. That is what keeps ASP.NET Core out
of your console worker and keeps the dashboard, the exporters, and the optional LLM "explain"
client out of your dependency graph unless you ask for them by name.

## Native AOT

This package is `IsAotCompatible` and is verified against a Native AOT publish in CI. If
you publish with `PublishAot`, referencing Loom should not produce an `IL2026` or `IL3050`
of its own.

## Requirements

.NET 10 or later.
