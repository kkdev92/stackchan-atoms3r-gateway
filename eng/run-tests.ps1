<#
.SYNOPSIS
    Runs the SDK and application test suites.

.DESCRIPTION
    Tests use the Microsoft Testing Platform selected in global.json. Application restore
    requires SDK packages in local-nuget. After changing the SDK, run pack-sdk.ps1 first or use
    build-all.ps1, which includes packaging.
#>
[CmdletBinding()]
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

dotnet test (Join-Path $root 'src/sdk/StackChan.Gateway.Sdk.slnx') -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test (Join-Path $root 'src/app/StackChan.Gateway.App.slnx') -c $Configuration
exit $LASTEXITCODE
