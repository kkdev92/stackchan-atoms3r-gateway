<#
.SYNOPSIS
    Starts StackChan Gateway.

.DESCRIPTION
    With -Offline, the gateway uses a fixed response and tone instead of Whisper, a language
    model, and Piper. The application consumes SDK packages from local-nuget; run pack-sdk.ps1
    first after changing the SDK.

.PARAMETER Token
    Token used to authenticate the device. It is passed to the application through an
    environment variable.

.PARAMETER AllowUnauthenticatedLan
    Allows LAN connections without a token. Devices on the same network can operate the gateway,
    so use this option only in an isolated test environment.
#>
[CmdletBinding()]
param(
    [switch]$Offline,
    [string]$Urls = 'http://0.0.0.0:8787',
    [string]$Token,
    [switch]$AllowUnauthenticatedLan,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src/app/StackChan.Gateway.App'

$env:Urls = $Urls

if ($Offline) {
    $env:StackChan__Offline__Enabled = 'true'
    Write-Host 'mode    : offline (fixed reply + tone)' -ForegroundColor Yellow
} else {
    $env:StackChan__Offline__Enabled = 'false'
    Write-Host 'mode    : real providers' -ForegroundColor Cyan
}

if ($Token) {
    # Pass the token through the child process environment without writing it to configuration or logs.
    $env:StackChan__Atoms3R__Token = $Token
}

# Set this value on every run so a previous invocation cannot affect the current one.
$env:StackChan__Security__AllowUnauthenticatedLan =
    if ($AllowUnauthenticatedLan) { 'true' } else { 'false' }

if ($AllowUnauthenticatedLan -and -not $Token) {
    Write-Host 'warning : exposing the gateway to the LAN without a token; any device on the network can operate it' `
        -ForegroundColor Red
}

Write-Host "urls    : $Urls"
dotnet run --project $app -c $Configuration
