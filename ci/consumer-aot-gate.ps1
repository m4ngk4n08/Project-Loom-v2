<#
.SYNOPSIS
  BACKLOG.md 11.3 - the packaged-consumer Native AOT gate.

.DESCRIPTION
  Packs LoomDiagnostics.Telemetry, restores it into a throwaway consumer from a folder
  feed (never a ProjectReference), AOT-publishes that consumer, and asserts the result is
  a working native binary with no IL2026/IL3050.

  This is the only check in the repository that can see the failure mode where
  Loom.Telemetry sets OutputItemType="Analyzer" on its own generator: that bakes
  LoomProfileAttribute into Loom.Telemetry.dll, the generator re-emits it in the consumer,
  and the consumer fails CS0436. Every in-repo project passes either way, so no solution
  build, test run, or AOT probe catches it - only a packaged consumer does.

  One implementation, run from both places: the consumer-aot-gate CI job invokes this
  script under pwsh on ubuntu, and it runs unchanged on Windows PowerShell 5.1 locally.
  A bash copy for CI would be a second implementation of the same gate, free to drift.

.PARAMETER Rid
  Runtime identifier to publish. Defaults to the host OS. Native AOT cannot cross-compile
  between operating systems, so linux-x64 must be built on Linux (CI, or WSL).
#>
[CmdletBinding()]
param(
    [string]$Rid
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$consumerDir = Join-Path $PSScriptRoot 'consumer-aot-gate'
$consumerProj = Join-Path $consumerDir 'PackageConsumer.csproj'
$feed = Join-Path $repo 'artifacts/loom-feed'

if (-not $Rid) {
    if ($IsLinux) { $Rid = 'linux-x64' }
    elseif ($IsMacOS) { $Rid = 'osx-x64' }
    else { $Rid = 'win-x64' }   # $IsWindows is undefined on PS 5.1, which is Windows-only
}

function Fail($message) {
    Write-Host "::error::$message"
    throw $message
}

# A unique prerelease version per run. Without it the second run restores the FIRST run's
# package out of ~/.nuget/packages - NuGet caches by id+version, and a rebuilt 1.0.0 with
# different content does not invalidate it. The gate would then pass on stale bits.
$version = '1.0.0-gate' + (Get-Date -Format 'yyyyMMddHHmmss')

Write-Host "== consumer-AOT gate: rid=$Rid version=$version"

# Clean, so a leftover managed assembly from an earlier run cannot be mistaken for either
# a pass or a failure of this one.
foreach ($d in @($feed, (Join-Path $consumerDir 'bin'), (Join-Path $consumerDir 'obj'))) {
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
}
New-Item -ItemType Directory -Force -Path $feed | Out-Null

Write-Host "== pack"
dotnet pack (Join-Path $repo 'Loom.Telemetry/Loom.Telemetry.csproj') -c Release -o $feed -p:PackageVersion=$version
if ($LASTEXITCODE -ne 0) { Fail "dotnet pack failed ($LASTEXITCODE)" }

$nupkg = Join-Path $feed "LoomDiagnostics.Telemetry.$version.nupkg"
if (-not (Test-Path $nupkg)) { Fail "Expected package not produced: $nupkg" }

# Assert the generator is in the package at the path NuGet loads Roslyn components from.
# Without it the package installs cleanly, the consumer compiles, and no wrappers are
# emitted - a silent no-op that produces no telemetry. Checked here rather than inferred
# from the consumer building, because the consumer would still build.
if ($PSVersionTable.PSVersion.Major -lt 6) { Add-Type -AssemblyName System.IO.Compression.FileSystem }
$zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
try {
    $entries = $zip.Entries | ForEach-Object { $_.FullName }
} finally {
    $zip.Dispose()
}
if ($entries -notcontains 'analyzers/dotnet/cs/Loom.Telemetry.Generators.dll') {
    Fail "Generator missing from analyzers/dotnet/cs/ - the package would emit no wrappers. Entries: $($entries -join ', ')"
}
if ($entries -notcontains 'lib/net10.0/Loom.Telemetry.dll') {
    Fail "lib/net10.0/Loom.Telemetry.dll missing from the package. Entries: $($entries -join ', ')"
}
Write-Host "   generator and library present in the package"

Write-Host "== restore + AOT publish the consumer"
# MSBuild writes warnings and errors to stdout, so plain capture is enough; `2>&1` on a
# native exe under PS 5.1 wraps stderr lines in ErrorRecords and breaks $LASTEXITCODE
# handling for no gain here.
$log = dotnet publish $consumerProj -c Release -r $Rid -p:LoomPackageVersion=$version |
    ForEach-Object { Write-Host $_; $_ }
$publishExit = $LASTEXITCODE

# Grep independently of the exit code. TreatWarningsAsErrors should already have failed
# the publish, but the assertion the backlog item actually asks for is "zero IL2026 and
# IL3050", and that must not depend on a property staying set in the csproj.
$trim = $log | Select-String -Pattern 'IL2026|IL3050'
if ($trim) { Fail "Trim/AOT warnings from the packaged consumer:`n$($trim -join "`n")" }
if ($publishExit -ne 0) { Fail "dotnet publish failed ($publishExit)" }

$publishDir = Join-Path $consumerDir "bin/Release/net10.0/$Rid/publish"
$exe = Join-Path $publishDir $(if ($Rid -like 'win-*') { 'PackageConsumer.exe' } else { 'PackageConsumer' })
if (-not (Test-Path $exe)) { Fail "No native binary at $exe" }

# A publish can silently fall back to a managed build and look completely clean. A managed
# assembly sitting beside the binary is the tell.
$dlls = Get-ChildItem $publishDir -Filter *.dll -ErrorAction SilentlyContinue
if ($dlls) { Fail "Managed assemblies beside the native binary - PublishAot did not engage: $($dlls.Name -join ', ')" }

Write-Host "== run"
if (-not ($Rid -like 'win-*')) { & chmod +x $exe }
$out = & $exe
if ($LASTEXITCODE -ne 0) { Fail "Consumer exited $LASTEXITCODE" }
Write-Host "   $out"
if ($out -ne 'package consumer OK') { Fail "Consumer ran but printed '$out'" }

$size = (Get-Item $exe).Length
Write-Host "== consumer-AOT gate PASSED ($Rid, $size bytes)"
