<#
.SYNOPSIS
  Builds the four LoomDiagnostics.* packages for a release, into artifacts/release/.

.DESCRIPTION
  Never pushes. Publishing to nuget.org is irreversible and stays a manual step.

  In order, each step failing closed:
    1. The working tree is clean and CI passed on this exact commit. The Linux AOT probe
       and both packaged-consumer gates run only in CI, so a green run is the evidence
       they passed for these bits.
    2. The four packable projects agree on one version (Directory.Build.props).
    3. The Angular UI is rebuilt from scratch (npm ci + production build). dist/ is
       gitignored, so a stale or missing build would otherwise ship silently.
    4. Strict Release build, then the backend and frontend test suites.
    5. Pack, then open LoomDiagnostics.Dashboard and check its Loom.Dashboard.dll embeds
       every file of the UI just built. Angular's output names carry content hashes, so
       this also catches a UI from some other build.

  Runs on Windows PowerShell 5.1 and on pwsh.

.PARAMETER AllowDirty
  Skip the clean-tree check. For trying the script, not for a release.

.PARAMETER SkipCiCheck
  Skip the "CI passed on HEAD" check. For trying the script, not for a release.

.PARAMETER SkipTests
  Skip both test suites. For trying the script, not for a release.
#>
[CmdletBinding()]
param(
    [switch]$AllowDirty,
    [switch]$SkipCiCheck,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
$out = Join-Path $repo 'artifacts/release'
$frontend = Join-Path $repo 'Loom.Web.Frontend'
$dist = Join-Path $frontend 'dist/Loom.Web.Frontend/browser'

$packages = [ordered]@{
    'LoomDiagnostics.Telemetry'            = 'Loom.Telemetry/Loom.Telemetry.csproj'
    'LoomDiagnostics.Dashboard.AspNetCore' = 'Loom.Dashboard.AspNetCore/Loom.Dashboard.AspNetCore.csproj'
    'LoomDiagnostics.Dashboard'            = 'Loom.Dashboard/Loom.Dashboard.csproj'
    'LoomDiagnostics.Cli'                  = 'Loom.DevTools/Loom.DevTools.csproj'
}

function Fail($message) {
    Write-Host "RELEASE FAILED: $message" -ForegroundColor Red
    exit 1   # runs the finally below, so the caller's location is restored
}

function Step($name) { Write-Host "`n== $name" -ForegroundColor Cyan }

# Native exes don't throw on a non-zero exit, even with ErrorActionPreference=Stop.
function Invoke-Checked([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { Fail "$what failed (exit $LASTEXITCODE)" }
}

Push-Location $repo
try {
    # ------------------------------------------------------------------ 1. source state
    Step 'source state'
    $sha = (git rev-parse HEAD).Trim()
    Write-Host "   commit $sha"

    if (-not $AllowDirty) {
        $dirty = git status --porcelain
        if ($dirty) { Fail "working tree is not clean:`n$($dirty -join "`n")" }
    }

    if (-not $SkipCiCheck) {
        $runs = gh run list --commit $sha --workflow ci.yml --json databaseId,status,conclusion --limit 1 | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { Fail 'gh run list failed - is gh installed and authenticated?' }
        if (-not $runs) { Fail "no CI run for $sha - push it and wait for CI" }
        $run = @($runs)[0]
        if ($run.status -ne 'completed') { Fail "CI run $($run.databaseId) is still $($run.status)" }
        if ($run.conclusion -ne 'success') { Fail "CI run $($run.databaseId) concluded '$($run.conclusion)'" }
        Write-Host "   CI run $($run.databaseId) passed"
    }

    # ------------------------------------------------------------------ 2. version
    Step 'version'
    $versions = @{}
    foreach ($id in $packages.Keys) {
        $json = (dotnet msbuild $packages[$id] -getProperty:Version -getProperty:PackageId) -join "`n"
        if ($LASTEXITCODE -ne 0) { Fail "could not read the version of $($packages[$id])" }
        $props = ($json | ConvertFrom-Json).Properties
        if ($props.PackageId -ne $id) { Fail "$($packages[$id]) packs as '$($props.PackageId)', expected '$id'" }
        $versions[$id] = $props.Version
        Write-Host "   $id $($props.Version)"
    }
    $version = @($versions.Values | Select-Object -Unique)
    if ($version.Count -ne 1) { Fail "packages disagree on version: $($versions.Values -join ', ')" }
    $version = $version[0]

    # ------------------------------------------------------------------ 3. frontend
    Step 'frontend (clean production build)'
    $distRoot = Join-Path $frontend 'dist'
    if (Test-Path $distRoot) { Remove-Item -Recurse -Force $distRoot }
    Push-Location $frontend
    try {
        Invoke-Checked 'npm ci' { npm ci }
        Invoke-Checked 'ng build' { npx ng build --configuration production }
    } finally { Pop-Location }
    if (-not (Test-Path (Join-Path $dist 'index.html'))) { Fail "ng build produced no index.html in $dist" }

    # ------------------------------------------------------------------ 4. build + test
    Step 'strict build'
    Invoke-Checked 'strict build' {
        dotnet build Loom.slnx -c Release --no-incremental /p:TreatWarningsAsErrors=true /p:EnableTrimAnalyzer=true
    }

    if (-not $SkipTests) {
        Step 'backend tests'
        Invoke-Checked 'dotnet test' { dotnet test Loom.slnx -c Debug }
        Step 'frontend tests'
        Push-Location $frontend
        try { Invoke-Checked 'ng test' { npx ng test } } finally { Pop-Location }
    }

    # ------------------------------------------------------------------ 5. pack + verify
    Step 'pack'
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    foreach ($id in $packages.Keys) {
        Invoke-Checked "pack $id" { dotnet pack $packages[$id] -c Release -o $out }
    }

    $expected = @($packages.Keys | ForEach-Object { "$_.$version.nupkg" } | Sort-Object)
    $actual = @(Get-ChildItem $out -Filter *.nupkg | ForEach-Object Name | Sort-Object)
    if (Compare-Object $expected $actual) {
        Fail "unexpected package set.`n  expected: $($expected -join ', ')`n  got:      $($actual -join ', ')"
    }

    Step 'verify the dashboard tool embeds this UI'
    if ($PSVersionTable.PSVersion.Major -lt 6) { Add-Type -AssemblyName System.IO.Compression.FileSystem }
    $toolPkg = Join-Path $out "LoomDiagnostics.Dashboard.$version.nupkg"
    $zip = [System.IO.Compression.ZipFile]::OpenRead($toolPkg)
    try {
        $entry = $zip.GetEntry('tools/net10.0/any/Loom.Dashboard.dll')
        if (-not $entry) { Fail "tools/net10.0/any/Loom.Dashboard.dll missing from $toolPkg" }
        $ms = New-Object System.IO.MemoryStream
        $s = $entry.Open()
        try { $s.CopyTo($ms) } finally { $s.Dispose() }
    } finally { $zip.Dispose() }
    # Manifest resource names are stored as UTF-8 in the metadata string heap.
    $dllText = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())

    # Top-level files only: that's everything Angular emits today, and nested paths get
    # mangled into resource names in ways not worth reimplementing here.
    $uiFiles = @(Get-ChildItem $dist -File | ForEach-Object Name)
    $missing = @($uiFiles | Where-Object { -not $dllText.Contains("Loom.Dashboard.wwwroot.$_") })
    if ($missing) { Fail "Loom.Dashboard.dll lacks $($missing.Count) of $($uiFiles.Count) UI files: $($missing -join ', ')" }
    Write-Host "   all $($uiFiles.Count) UI files embedded, index.html included"

    # ------------------------------------------------------------------ summary
    Step "READY: $version @ $sha"
    Get-ChildItem $out -Filter *.nupkg | ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host ("   {0,-55} {1,10:N0} B  sha256 {2}" -f $_.Name, $_.Length, $hash)
    }
    Write-Host "`n   Nothing was pushed. Publishing is a separate, manual step."
}
finally {
    Pop-Location
}
