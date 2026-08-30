<#
.SYNOPSIS
    Verifies that all release version declarations agree.

.DESCRIPTION
    Uses VersionPrefix in src/sdk/Directory.Build.props as the source of truth and checks:

      1. Kkdev92.StackChan.* versions in Directory.Packages.props, which ensure that the
         application restores the intended SDK packages.

      2. The release tag. NuGet.org does not allow replacing a published package version, so
         the tag and package version must agree before publication.

.PARAMETER Tag
    Tag to validate, for example v0.1.0. An empty value skips tag validation.

.EXAMPLE
    ./eng/check-version.ps1
    ./eng/check-version.ps1 -Tag v0.1.0
#>
[CmdletBinding()]
param([string]$Tag = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Authoritative SDK version
$propsPath = Join-Path $root 'src/sdk/Directory.Build.props'
$props = Get-Content -LiteralPath $propsPath -Raw
if ($props -notmatch '<VersionPrefix>([^<]+)</VersionPrefix>') {
    throw "VersionPrefix was not found in $propsPath."
}
$version = $Matches[1].Trim()
Write-Host "version : $version  (src/sdk/Directory.Build.props)"

# Ensure release packages do not retain a prerelease suffix.
if ($props -match '<VersionSuffix>([^<]*)</VersionSuffix>') {
    $suffix = $Matches[1].Trim()
    if ($suffix) {
        throw "VersionSuffix is set to '$suffix'. Remove it before releasing."
    }
}

# SDK versions in Directory.Packages.props
$packagesPath = Join-Path $root 'Directory.Packages.props'
$packages = Get-Content -LiteralPath $packagesPath -Raw
$entries = [regex]::Matches(
    $packages,
    '<PackageVersion\s+Include="(Kkdev92\.StackChan\.[^"]+)"\s+Version="([^"]+)"')

$expected = (Get-ChildItem (Join-Path $root 'src/sdk') -Directory).Count
if ($entries.Count -ne $expected) {
    throw "Found $($entries.Count) Kkdev92.StackChan.* entries for $expected SDK projects."
}

$mismatched = @()
foreach ($entry in $entries) {
    if ($entry.Groups[2].Value -ne $version) {
        $mismatched += "$($entry.Groups[1].Value) = $($entry.Groups[2].Value)"
    }
}
if ($mismatched.Count -gt 0) {
    throw "Versions in Directory.Packages.props do not match ${version}:`n  " + ($mismatched -join "`n  ")
}
Write-Host "packages: all $($entries.Count) entries use $version"

# Release tag
if ($Tag) {
    $expectedTag = "v$version"
    if ($Tag -ne $expectedTag) {
        throw "Tag '$Tag' does not match SDK version $version. Use '$expectedTag'."
    }
    Write-Host "tag     : $Tag"
}
else {
    Write-Host "tag     : not checked"
}

# Verify that CHANGELOG contains a Keep a Changelog release heading.
$changelogPath = Join-Path $root 'CHANGELOG.md'
if (Test-Path -LiteralPath $changelogPath) {
    $changelog = Get-Content -LiteralPath $changelogPath -Raw
    $heading = "(?m)^##\s+\[$([regex]::Escape($version))\]\s+-\s+\d{4}-\d{2}-\d{2}\s*$"
    if ($changelog -notmatch $heading) {
        throw "CHANGELOG.md does not contain a '## [$version] - YYYY-MM-DD' heading."
    }
    Write-Host "changelog: ## [$version]"
}

Write-Host 'ok'
