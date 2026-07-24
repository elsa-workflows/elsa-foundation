<#
.SYNOPSIS
    Runtime diagnostics settings: the effective capture policy that governs per-activity value capture.
.DESCRIPTION
    Foundation-native analog of the JTest "logging" configuration concept. Classic Elsa 3.x configured
    what activity state is persisted per activity via `logPersistenceConfig`; Foundation has no such knob.
    Instead a server-wide **runtime diagnostics settings** policy governs the capture level, and the
    `Test-ValueCapture.ps1` sibling exercises what that policy actually captures.

    This test reads `GET /runtime/workflows/diagnostics/settings` (read-only, no mutation) and asserts the
    contract:
      - `requested` / `effective` / `hostPolicy` / `permissions` sections are present
      - `effective.defaultLevel` is a real capture level (this is why value snapshots get captured)
      - `hostPolicy.snapshotLimits` bounds snapshot size (why payloads return a bounded preview, not raw)

    It does NOT PUT/modify the settings (that is server-global state). Requires the server running from
    source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== Runtime diagnostics settings (logging-config analog) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

$settings = Invoke-Step "GET diagnostics settings" {
    Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/diagnostics/settings" -WebSession $ctx.Session -UseBasicParsing
}

Write-Host ""
Write-Host ("requested.defaultLevel  : {0}" -f $settings.requested.defaultLevel)
Write-Host ("effective.defaultLevel  : {0}" -f $settings.effective.defaultLevel)
Write-Host ("hostPolicy.maximumLevel : {0}" -f $settings.hostPolicy.maximumLevel)
Write-Host ("permissions.canManage   : {0}" -f $settings.permissions.canManage)
Write-Host ("permissions.canEnableFullPayloads : {0}" -f $settings.permissions.canEnableFullPayloads)
if ($settings.hostPolicy.limitationReasons) {
    $settings.hostPolicy.limitationReasons | ForEach-Object { Write-Host ("  hostPolicy limitation: {0}" -f $_) }
}
$lim = $settings.hostPolicy.snapshotLimits
if ($lim) {
    Write-Host ("snapshotLimits          : maxDepth={0} maxStringLength={1} maxTotalBytes={2}" -f $lim.maxDepth, $lim.maxStringLength, $lim.maxTotalBytes)
}

$failures = @()
if (-not $settings.requested)                    { $failures += "missing 'requested' section" }
if (-not $settings.effective)                    { $failures += "missing 'effective' section" }
if (-not $settings.hostPolicy)                   { $failures += "missing 'hostPolicy' section" }
if (-not $settings.permissions)                  { $failures += "missing 'permissions' section" }
if (-not $settings.effective.defaultLevel)       { $failures += "effective.defaultLevel not reported" }
if (-not $settings.hostPolicy.maximumLevel)      { $failures += "hostPolicy.maximumLevel not reported" }
if (-not $settings.hostPolicy.snapshotLimits)    { $failures += "hostPolicy.snapshotLimits not reported" }

Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host ("SUCCESS - diagnostics capture policy reported: effective level '{0}', host max '{1}'" -f $settings.effective.defaultLevel, $settings.hostPolicy.maximumLevel) -ForegroundColor Green
} else {
    Write-Host "FAIL - diagnostics settings contract violations:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host ("  - {0}" -f $_) -ForegroundColor Red }
    exit 1
}
