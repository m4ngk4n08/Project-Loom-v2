<#
.SYNOPSIS
  Packaged-consumer Native AOT gate for LoomDiagnostics.Dashboard.AspNetCore.

.DESCRIPTION
  Packs LoomDiagnostics.Telemetry and LoomDiagnostics.Dashboard.AspNetCore into a throwaway
  folder feed, asserts the dashboard package's layout, restores it into a consumer that
  never uses a ProjectReference, AOT-publishes that consumer, then runs it against a live
  Loom.TestFixtureApp and asserts real telemetry comes out over HTTP.

  Nothing else in the repository builds the dashboard library as a stranger would: it has
  only ever been compiled from source inside this repo. Sibling of consumer-aot-gate.ps1
  (LoomDiagnostics.Telemetry) - kept separate so a failure names which package broke.

  One implementation, run from both places: the dashboard-consumer-aot-gate CI job invokes
  this under pwsh on ubuntu, and it runs unchanged on Windows PowerShell 5.1 locally.

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
$consumerDir = Join-Path $PSScriptRoot 'dashboard-consumer-aot-gate'
$consumerProj = Join-Path $consumerDir 'DashboardConsumer.csproj'
$feed = Join-Path $repo 'artifacts/loom-dashboard-feed'
$runDir = Join-Path $repo 'artifacts/dashboard-gate-run'

if (-not $Rid) {
    if ($IsLinux) { $Rid = 'linux-x64' }
    elseif ($IsMacOS) { $Rid = 'osx-x64' }
    else { $Rid = 'win-x64' }   # $IsWindows is undefined on PS 5.1, which is Windows-only
}
$isWin = $Rid -like 'win-*'

function Fail($message) {
    Write-Host "::error::$message"
    throw $message
}

# A unique prerelease version per run: NuGet caches by id+version, so a rebuilt package under
# a reused version would restore the previous run's bits and the gate would pass on stale
# content.
$version = '1.0.0-gate' + (Get-Date -Format 'yyyyMMddHHmmss')
Write-Host "== dashboard consumer-AOT gate: rid=$Rid version=$version"

foreach ($d in @($feed, $runDir, (Join-Path $consumerDir 'bin'), (Join-Path $consumerDir 'obj'))) {
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
}
New-Item -ItemType Directory -Force -Path $feed | Out-Null
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

# ---------------------------------------------------------------- 2. pack
Write-Host "== pack"
foreach ($proj in @('Loom.Telemetry/Loom.Telemetry.csproj', 'Loom.Dashboard.AspNetCore/Loom.Dashboard.AspNetCore.csproj')) {
    dotnet pack (Join-Path $repo $proj) -c Release -o $feed -p:PackageVersion=$version
    if ($LASTEXITCODE -ne 0) { Fail "dotnet pack $proj failed ($LASTEXITCODE)" }
}

# ---------------------------------------------------------------- 3. package contents
$nupkg = Join-Path $feed "LoomDiagnostics.Dashboard.AspNetCore.$version.nupkg"
if (-not (Test-Path $nupkg)) { Fail "Expected package not produced: $nupkg" }

if ($PSVersionTable.PSVersion.Major -lt 6) { Add-Type -AssemblyName System.IO.Compression.FileSystem }
$zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
    $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
    try { $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally {
    $zip.Dispose()
}

$bundled = @('Security', 'Storage', 'Telemetry.Query', 'Telemetry.Alerting', 'Telemetry.Exporters', 'Web.Contracts', 'Web.RealTime') |
    ForEach-Object { "Loom.$_.dll" }
$expectedLib = @('Loom.Dashboard.AspNetCore.dll') + $bundled | ForEach-Object { "lib/net10.0/$_" }
$actualLib = @($entries | Where-Object { $_ -like 'lib/*' })
$missing = @($expectedLib | Where-Object { $actualLib -notcontains $_ })
$extra = @($actualLib | Where-Object { $expectedLib -notcontains $_ })
if ($missing.Count -or $extra.Count) {
    Fail "lib/ layout wrong. Missing: [$($missing -join ', ')] Unexpected: [$($extra -join ', ')]"
}
if ($entries -contains 'lib/net10.0/Loom.Telemetry.dll') {
    Fail "lib/net10.0/Loom.Telemetry.dll must NOT be in the package - it is published on its own"
}
$depMatch = [regex]::Match($nuspec, '<dependency id="LoomDiagnostics\.Telemetry" version="([^"]+)"')
if (-not $depMatch.Success) { Fail "nuspec has no dependency on LoomDiagnostics.Telemetry" }
if ($depMatch.Groups[1].Value -ne $version) {
    Fail "nuspec depends on LoomDiagnostics.Telemetry $($depMatch.Groups[1].Value), expected exactly $version - the pack did not propagate the version override"
}
Write-Host "   lib/ holds the library + 7 bundled DLLs, no Loom.Telemetry.dll; depends on LoomDiagnostics.Telemetry $version"

# ---------------------------------------------------------------- 4. AOT publish
Write-Host "== restore + AOT publish the consumer"
# MSBuild writes warnings and errors to stdout; `2>&1` on a native exe under PS 5.1 wraps
# stderr in ErrorRecords and breaks $LASTEXITCODE handling for no gain here.
$log = dotnet publish $consumerProj -c Release -r $Rid -p:LoomPackageVersion=$version |
    ForEach-Object { Write-Host $_; $_ }
$publishExit = $LASTEXITCODE

# ---------------------------------------------------------------- 5. warnings allow-list
# Measured on win-x64 2026-09-21 (BACKLOG.md decision log): TraceEvent produces these three
# and only these. Matched on code AND method, so a new warning with an allowed code but a
# different method fails. Do not widen this list to make the gate pass.
$allowed = @(
    @{ Code = 'IL3050'; Method = 'Microsoft.Diagnostics.Tracing.TraceEvent.TraceEvent(Int32,Int32,String,Guid,Int32,String,Guid,String)' },
    @{ Code = 'IL2067'; Method = 'Microsoft.Diagnostics.Tracing.Parsers.DynamicTraceEventData.GetDefaultValueByType(Type)' },
    @{ Code = 'IL2057'; Method = 'Microsoft.Diagnostics.Tracing.Parsers.DynamicTraceEventData.PayloadFetch.FromStream(Deserializer)' }
)
$ilLines = @($log | Where-Object { $_ -match '\bIL\d{4}\b' } | ForEach-Object { "$_".Trim() } | Sort-Object -Unique)
$seen = @{}
$unexpected = @()
foreach ($line in $ilLines) {
    $hit = $null
    foreach ($a in $allowed) {
        if ($line.Contains($a.Code) -and $line.Contains($a.Method)) { $hit = $a; break }
    }
    if ($hit) { $seen[$hit.Code] = $true } else { $unexpected += $line }
}
if ($unexpected.Count) { Fail "Trim/AOT warnings outside the accepted list:`n$($unexpected -join "`n")" }
foreach ($a in $allowed) {
    if ($seen[$a.Code]) { Write-Host "   accepted warning: $($a.Code) $($a.Method)" }
    else { Write-Host "   NOTICE: accepted warning $($a.Code) $($a.Method) did not appear - TraceEvent may have changed" }
}
if ($publishExit -ne 0) { Fail "dotnet publish failed ($publishExit)" }

# ---------------------------------------------------------------- 6. no managed fallback
$publishDir = Join-Path $consumerDir "bin/Release/net10.0/$Rid/publish"
$exe = Join-Path $publishDir $(if ($isWin) { 'DashboardConsumer.exe' } else { 'DashboardConsumer' })
if (-not (Test-Path $exe)) { Fail "No native binary at $exe" }

# Dia2Lib.dll and TraceReloggerLib.dll are managed interop assemblies TraceEvent copies
# beside the binary (measured). Anything else managed means PublishAot did not engage.
# Decided by GetAssemblyName, not by file name: native .dll files legitimately appear too.
$tolerated = @('Dia2Lib.dll', 'TraceReloggerLib.dll')
$managed = @()
foreach ($dll in (Get-ChildItem $publishDir -Filter *.dll -File -ErrorAction SilentlyContinue)) {
    if ($tolerated -contains $dll.Name) { continue }
    try {
        [void][System.Reflection.AssemblyName]::GetAssemblyName($dll.FullName)
        $managed += $dll.Name
    } catch {
        # Not a managed assembly - a native DLL, which is fine.
    }
}
if ($managed.Count) { Fail "Managed assemblies beside the native binary - PublishAot did not engage: $($managed -join ', ')" }
if (-not $isWin) { & chmod +x $exe }

# ---------------------------------------------------------------- run harness
$keyFile = Join-Path $runDir 'jwt.key'
$usersFile = Join-Path $runDir 'users'
$missingKeyFile = Join-Path $runDir 'does-not-exist.key'
$noBom = New-Object System.Text.UTF8Encoding($false)

$fixture = $null
$consumer = $null
$savedKeyVar = $env:LOOM_JWT_KEY_FILE
$savedUsersVar = $env:LOOM_AUTH_USERS_FILE
$http = $null

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds, [string]$What) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 250
    }
    Fail "Timed out after ${TimeoutSeconds}s waiting for: $What"
}

function Invoke-Api([string]$Method, [string]$Url, [string]$Token, [string]$Json) {
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($Method), $Url)
    if ($Token) { $req.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") | Out-Null }
    if ($Json) { $req.Content = New-Object System.Net.Http.StringContent($Json, [System.Text.Encoding]::UTF8, 'application/json') }
    $resp = $script:http.SendAsync($req).GetAwaiter().GetResult()
    try {
        $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject]@{ Status = [int]$resp.StatusCode; Headers = $resp.Headers; Body = $body }
    } finally {
        $resp.Dispose(); $req.Dispose()
    }
}

function Start-Consumer([string[]]$ConsumerArgs, [string]$Tag) {
    $out = Join-Path $runDir "$Tag.out.txt"
    $err = Join-Path $runDir "$Tag.err.txt"
    $p = Start-Process -FilePath $exe -ArgumentList $ConsumerArgs -NoNewWindow -PassThru `
        -RedirectStandardOutput $out -RedirectStandardError $err
    $null = $p.Handle   # PS 5.1: without this, ExitCode can read back empty after exit
    return $p
}

function Show-RunLogs {
    foreach ($f in (Get-ChildItem $runDir -Filter *.txt -ErrorAction SilentlyContinue)) {
        Write-Host "---- $($f.Name) (last 40 lines)"
        Get-Content $f.FullName -Tail 40 | ForEach-Object { Write-Host $_ }
    }
}

try {
    # ------------------------------------------------------------ 7. fixture
    Write-Host "== build + start the fixture"
    dotnet build (Join-Path $repo 'Loom.TestFixtureApp/Loom.TestFixtureApp.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { Fail "fixture build failed ($LASTEXITCODE)" }
    $fixtureDll = Join-Path $repo 'Loom.TestFixtureApp/bin/Release/net10.0/Loom.TestFixtureApp.dll'
    if (-not (Test-Path $fixtureDll)) { Fail "Fixture not built at $fixtureDll" }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'dotnet'
    $psi.Arguments = '"' + $fixtureDll + '"'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true    # held open: the fixture exits when stdin closes
    $psi.RedirectStandardOutput = $true
    $fixture = [System.Diagnostics.Process]::Start($psi)
    $readTask = $fixture.StandardOutput.ReadLineAsync()
    if (-not $readTask.Wait(60000)) { Fail "Fixture did not print READY within 60s" }
    if ($readTask.Result -ne 'READY') { Fail "Fixture printed '$($readTask.Result)' instead of READY" }
    $fixturePid = $fixture.Id
    Write-Host "   fixture READY, pid $fixturePid"

    # ------------------------------------------------------------ 8. throwaway credentials
    $keyBytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($keyBytes) } finally { $rng.Dispose() }
    [System.IO.File]::WriteAllText($keyFile, [Convert]::ToBase64String($keyBytes), $noBom)

    $password = [Guid]::NewGuid().ToString('N')
    # The hash comes from the AOT consumer itself, so the bundled Loom.Security runs native.
    # No `| Select-Object -First 1`: it stops the pipeline early and leaves $LASTEXITCODE at -1.
    $hashOut = @(& $exe hash $password)
    if ($LASTEXITCODE -ne 0 -or $hashOut.Count -ne 1 -or -not $hashOut[0]) { Fail "Consumer 'hash' mode failed ($LASTEXITCODE)" }
    $hash = $hashOut[0]
    [System.IO.File]::WriteAllText($usersFile, "gate:$hash`n", $noBom)

    # ------------------------------------------------------------ 9. fail closed first
    Write-Host "== fail-closed: missing signing key"
    $probe = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $probe.Start(); $port = $probe.LocalEndpoint.Port; $probe.Stop()

    $env:LOOM_JWT_KEY_FILE = $missingKeyFile
    $env:LOOM_AUTH_USERS_FILE = $usersFile
    $bad = Start-Consumer @('serve', "$fixturePid", "$port") 'failclosed'
    if (-not $bad.WaitForExit(60000)) {
        try { $bad.Kill() } catch { }
        Fail "Consumer with a missing signing key was still running after 60s - it did not fail closed"
    }
    if ($bad.ExitCode -eq 0) { Fail "Consumer with a missing signing key exited 0" }
    $badOut = Get-Content (Join-Path $runDir 'failclosed.out.txt') -Raw -ErrorAction SilentlyContinue
    if ($badOut -match 'Now listening') { Fail "Consumer with a missing signing key started listening" }
    Write-Host "   refused to start (exit $($bad.ExitCode))"

    # ------------------------------------------------------------ 10. the real run
    Write-Host "== real run on 127.0.0.1:$port, attached to fixture pid $fixturePid"
    $env:LOOM_JWT_KEY_FILE = $keyFile
    $env:LOOM_AUTH_USERS_FILE = $usersFile
    $consumer = Start-Consumer @('serve', "$fixturePid", "$port") 'serve'

    if ($PSVersionTable.PSVersion.Major -lt 6) { Add-Type -AssemblyName System.Net.Http }
    $http = New-Object System.Net.Http.HttpClient
    $http.Timeout = [TimeSpan]::FromSeconds(15)
    $base = "http://127.0.0.1:$port"

    $health = $null
    Wait-Until {
        if ($consumer.HasExited) { Fail "Consumer exited early with code $($consumer.ExitCode)" }
        try { $script:health = Invoke-Api 'GET' "$base/api/health" $null $null; return $true } catch { return $false }
    } 60 '/api/health to answer'
    if ($health.Status -ne 200) { Fail "/api/health returned $($health.Status)" }
    if (-not $health.Headers.Contains('Content-Security-Policy')) { Fail "/api/health has no Content-Security-Policy header" }
    Write-Host "   /api/health 200 with Content-Security-Policy"

    $anon = Invoke-Api 'GET' "$base/api/metrics/cpu" $null $null
    if ($anon.Status -ne 401) { Fail "anonymous /api/metrics/cpu returned $($anon.Status), expected 401" }
    Write-Host "   anonymous /api/metrics/cpu 401"

    $tokenJson = '{"username":"gate","password":"' + $password + '"}'
    $tokenResp = Invoke-Api 'POST' "$base/api/token" $null $tokenJson
    if ($tokenResp.Status -ne 200) { Fail "POST /api/token returned $($tokenResp.Status): $($tokenResp.Body)" }
    $tm = [regex]::Match($tokenResp.Body, '"token"\s*:\s*"([^"]+)"', 'IgnoreCase')
    if (-not $tm.Success) { Fail "POST /api/token 200 carried no token" }
    $token = $tm.Groups[1].Value
    Write-Host "   POST /api/token 200 (token issued)"

    $alerts = Invoke-Api 'GET' "$base/api/alerts" $token $null
    if ($alerts.Status -ne 200) { Fail "authenticated /api/alerts returned $($alerts.Status)" }
    $q = [Uri]::EscapeDataString('SELECT method, COUNT(*) FROM telemetry')
    $query = Invoke-Api 'GET' "$base/api/query?q=$q" $token $null
    if ($query.Status -ne 200) { Fail "authenticated /api/query returned $($query.Status): $($query.Body)" }
    Write-Host "   authenticated /api/alerts 200, /api/query 200"

    # Data flowing through TraceEvent in native code is the reason this gate exists.
    $names = $null
    Wait-Until {
        $r = Invoke-Api 'GET' "$base/api/exporters/metrics/names" $token $null
        $script:names = $r
        return ($r.Status -eq 200 -and $r.Body.Contains('fixture.orders.processed'))
    } 20 "/api/exporters/metrics/names to list fixture.orders.processed (last status $($names.Status))"
    Write-Host "   metric 'fixture.orders.processed' ingested"

    $logs = $null
    Wait-Until {
        $r = Invoke-Api 'GET' "$base/api/logs?count=50" $token $null
        $script:logs = $r
        return ($r.Status -eq 200 -and $r.Body.Contains('fixture processed order 4711'))
    } 20 "/api/logs to contain 'fixture processed order 4711'"
    Write-Host "   log 'fixture processed order 4711' ingested"

    $size = (Get-Item $exe).Length
    $passedLine = "== dashboard consumer-AOT gate PASSED ($Rid, $size bytes)"
}
catch {
    Show-RunLogs
    throw
}
finally {
    # ------------------------------------------------------------ 11. clean up, pass or fail
    if ($http) { $http.Dispose() }
    if ($consumer -and -not $consumer.HasExited) { try { $consumer.Kill() } catch { } }
    if ($fixture) {
        try { $fixture.StandardInput.Close() } catch { }
        if (-not $fixture.WaitForExit(15000)) { try { $fixture.Kill() } catch { } }
    }
    $env:LOOM_JWT_KEY_FILE = $savedKeyVar
    $env:LOOM_AUTH_USERS_FILE = $savedUsersVar
    foreach ($f in @($keyFile, $usersFile)) {
        if (Test-Path $f) { Remove-Item -Force $f }
    }
}

Write-Host $passedLine
