<#
.SYNOPSIS
    Packages the SDK and refreshes the local development feed.

.DESCRIPTION
    The application consumes SDK packages from artifacts. When a package is rebuilt at the
    same version, this script removes only Kkdev92.StackChan.* from the repository-local package
    cache so restore uses the new content. It does not modify the global package cache.
#>
[CmdletBinding()]
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Remove previous artifacts so packages deleted from the SDK cannot be restored from the feed.
# The extension filter leaves .gitkeep in place.
Get-ChildItem (Join-Path $root 'artifacts') -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.nupkg', '.snupkg' } |
    Remove-Item -Force

dotnet pack (Join-Path $root 'src/sdk/StackChan.Gateway.Sdk.slnx') -c $Configuration `
    -o (Join-Path $root 'artifacts')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem (Join-Path $root '.packages') -Directory -Filter 'kkdev92.stackchan.*' -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force

$packages = Get-ChildItem (Join-Path $root 'artifacts') -Filter '*.nupkg'
$symbols = Get-ChildItem (Join-Path $root 'artifacts') -Filter '*.snupkg'

# Verify that every SDK project produced both a binary package and a symbol package.
$expected = (Get-ChildItem (Join-Path $root 'src/sdk') -Directory).Count

if ($packages.Count -ne $expected) {
    Write-Error "Found $($packages.Count) nupkg files for $expected SDK projects."
    exit 1
}

if ($symbols.Count -ne $expected) {
    Write-Error "Found $($symbols.Count) snupkg files; expected one for each nupkg file."
    exit 1
}

Write-Host "packed  : $($packages.Count) nupkg + $($symbols.Count) snupkg -> artifacts"
Write-Host 'cache   : cleared .packages/kkdev92.stackchan.*'
