# Rebuilds Loom.DevTools and reinstalls it as the global `loom` tool.
# Run from the repo root: .\Loom.DevTools\reinstall-loom.ps1
#
# `dotnet tool update` no-ops when the package version hasn't changed (it always
# stays 1.0.0 here), so this uninstalls and reinstalls instead - that's the only
# way to force the global `loom` command to pick up new local code.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $repoRoot "Loom.DevTools\Loom.DevTools.csproj"
$nupkgDir = Join-Path $repoRoot "Loom.DevTools\bin\nupkg"

dotnet pack $csproj -c Release -o $nupkgDir
dotnet tool uninstall -g Loom.DevTools
dotnet tool install -g Loom.DevTools --add-source $nupkgDir

Write-Host "`nloom reinstalled. Verify with: loom" -ForegroundColor Green
