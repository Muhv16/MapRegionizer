[CmdletBinding()]
param(
    [switch]$Fast,
    [switch]$Full,
    [switch]$Generation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$selectedModes = [int]$Fast.IsPresent + [int]$Full.IsPresent + [int]$Generation.IsPresent
if ($selectedModes -gt 1) {
    throw 'Specify at most one mode: -Fast, -Full, or -Generation.'
}

$mode = if ($Fast) { 'Fast' } elseif ($Generation) { 'Generation' } else { 'Full' }
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    Write-Host "> dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-home'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:LOCALAPPDATA = Join-Path $repositoryRoot '.localappdata'
    New-Item -ItemType Directory -Force -Path $env:LOCALAPPDATA | Out-Null

    if ($mode -ne 'Generation') {
        Invoke-DotNet -Arguments @('restore')
        Invoke-DotNet -Arguments @('format', '--verify-no-changes')
        Invoke-DotNet -Arguments @('build', '--no-restore')
        Invoke-DotNet -Arguments @('test', '--no-build')
    }

    if ($mode -ne 'Fast') {
        Invoke-DotNet -Arguments @('run', '--project', 'src/MapRegionizer.Cli', '--', 'generate', '--mask', 'samples/mask/small-continent.png', '--out', 'artifacts/verification', '--seed', '42')
        Invoke-DotNet -Arguments @('run', '--project', 'tools/MapRegionizer.Validation', '--', 'artifacts/verification/summary.json')
    }
}
finally {
    Pop-Location
}
