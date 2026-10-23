<#
.SYNOPSIS
    Requested features missing from the runtime catalog are visible without log access (#1159).
.DESCRIPTION
    Owns a rebuilt Workbench with isolated SQLite data and an unknown feature in its copied shells.json.
    The lenient host becomes ready, while GET /modularity/features distinguishes the requested missing
    feature from a known runtime descriptor. No repository configuration is changed.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [int] $StartupTimeoutSeconds = 300
)
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

$BaseUrl = Resolve-ElsaServerBaseUrl -BaseUrl $BaseUrl
$contentRoot = New-ElsaContentRoot -Prefix 'elsa-unknown-features'
$completed = $false
try {
    $missingId = "MissingFeature1159-$([guid]::NewGuid().ToString('N'))"
    $settingsPath = Join-Path $contentRoot 'shells.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $settings.CShells.Shells.default.Features | Add-Member -NotePropertyName $missingId -NotePropertyValue @{}
    $settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $settingsPath -Encoding utf8

    Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $contentRoot -TimeoutSec $StartupTimeoutSeconds
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $ready = Invoke-WebRequest "$BaseUrl/health/ready" -UseBasicParsing
    $catalog = Invoke-RestMethod "$BaseUrl/modularity/features" -WebSession $ctx.Session
    $missing = @($catalog.features | Where-Object { $_.id -eq $missingId })
    $known = @($catalog.features | Where-Object { $_.id -eq 'Primitives' })

    $checks = @(
        @{ Name = 'lenient host is ready'; Passed = $ready.StatusCode -eq 200 },
        @{ Name = 'unknown feature remains requested'; Passed = $missing.Count -eq 1 -and $missing[0].enabled -eq $true },
        @{ Name = 'unknown feature explicitly lacks a runtime descriptor'; Passed = $missing.Count -eq 1 -and $missing[0].PSObject.Properties.Name -contains 'runtimeAvailable' -and $missing[0].runtimeAvailable -eq $false },
        @{ Name = 'known feature has a runtime descriptor'; Passed = $known.Count -eq 1 -and $known[0].runtimeAvailable -eq $true }
    )
    foreach ($check in $checks) {
        if (-not $check.Passed) { throw "FAILED: $($check.Name)" }
        Write-Host "  OK   $($check.Name)" -ForegroundColor Green
    }
    $completed = $true
    Write-Host "== unknown feature reporting: $($checks.Count)/$($checks.Count) passed ==" -ForegroundColor Green
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $completed)
}
