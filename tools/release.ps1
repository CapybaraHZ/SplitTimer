# Bump the app version, commit and push. GitHub Actions then builds the exe,
# creates the vX.Y.Z tag/release and refreshes the Pages site.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools/release.ps1            # bump patch (1.1.0 -> 1.1.1)
#   powershell -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.2.0

param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root "src/SplitTimer/SplitTimer.csproj"

$raw = Get-Content $csproj -Raw
if ($raw -notmatch '<Version>([\d\.]+)</Version>') {
    throw "cannot find <Version> in $csproj"
}

$current = $Matches[1]
if (-not $Version) {
    $parts = $current.Split('.')
    while ($parts.Count -lt 3) { $parts += "0" }
    $parts[2] = ([int]$parts[2] + 1).ToString()
    $Version = ($parts[0..2] -join '.')
}

Write-Host "version: $current -> $Version"
$raw = $raw -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>"
Set-Content $csproj $raw -Encoding utf8

pushd $root
dotnet build SplitTimer.slnx -c Debug -nr:false | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }

git add -A
git commit -m "release v$Version"
git tag "v$Version"
git push origin main
git push origin "v$Version"
Write-Host "pushed v$Version - Actions is building the release now"
popd
