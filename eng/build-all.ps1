<#
.SYNOPSIS
    Runs SDK tests, packages the SDK, and then runs application tests.

.DESCRIPTION
    The application consumes SDK packages from local-nuget instead of project references.
    This script refreshes those packages before testing the application.
#>
[CmdletBinding()]
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

dotnet test (Join-Path $root 'src/sdk/StackChan.Gateway.Sdk.slnx') -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot 'pack-sdk.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test (Join-Path $root 'src/app/StackChan.Gateway.App.slnx') -c $Configuration
exit $LASTEXITCODE
