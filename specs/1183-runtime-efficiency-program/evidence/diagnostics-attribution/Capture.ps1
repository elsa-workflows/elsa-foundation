[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseUrl,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $CandidateSha,
    [Parameter(Mandatory)][string] $ReferenceScript,
    [Parameter(Mandatory)][string] $ControlPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [ValidateSet('Durable','Balanced')][string] $PersistenceMode = 'Balanced'
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Preserve the existing attribution receipt.' }
if (Test-Path -LiteralPath $ControlPath) { throw 'Begin with capture disabled.' }
$nonce = [Guid]::NewGuid().ToString('N')
$setup = & $ReferenceScript -HostCandidateSha $CandidateSha -BaseUrl $BaseUrl -ExpectedPersistenceMode $PersistenceMode -AuthoredPersistenceMode $PersistenceMode `
    -WorkflowName "SqlAttribution-$nonce" -RoutePath "sql-attribution-$nonce" -SetupOnly | Select-Object -Last 1
$url = "$($BaseUrl.TrimEnd('/'))/workflows/http/sql-attribution-$nonce"
$body = '{"firstName":"Alice","lastName":"Smith"}'
function Check-Response($response) {
    if ($response.StatusCode -ne 200 -or $response.Content -cne 'Alice Smith') { throw 'The attribution request failed exact-output validation.' }
}
function Set-Phase([string] $phase) {
    [IO.File]::WriteAllText("$ControlPath.next", $phase, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move("$ControlPath.next", $ControlPath, $true)
}
try {
    1..3 | ForEach-Object { Check-Response (Invoke-WebRequest -Uri $url -Method Post -ContentType 'application/json' -Body $body -WebSession $setup.Context.Session -TimeoutSec 60) }
    Start-Sleep -Seconds 5 # Drain setup/warmup diagnostics before the observed idle control.
    Set-Phase 'before'
    Start-Sleep -Seconds 5
    Remove-Item -LiteralPath $ControlPath
    $traceId = [Guid]::NewGuid().ToString('N')
    $parentId = [Guid]::NewGuid().ToString('N').Substring(0,16)
    Set-Phase 'request'
    Check-Response (Invoke-WebRequest -Uri $url -Method Post -ContentType 'application/json' -Body $body -WebSession $setup.Context.Session -Headers @{ traceparent = "00-$traceId-$parentId-01" } -TimeoutSec 60)
    Start-Sleep -Seconds 5 # Retain asynchronous diagnostic persistence after the response.
    Remove-Item -LiteralPath $ControlPath
    Set-Phase 'after'
    Start-Sleep -Seconds 5
    Remove-Item -LiteralPath $ControlPath
    $id = [Uri]::EscapeDataString([string]$setup.DefinitionId)
    $page = Invoke-RestMethod -Uri "$($BaseUrl.TrimEnd('/'))/runtime/workflows/instances/page?definitionId=$id&take=100" -WebSession $setup.Context.Session -TimeoutSec 60
    $instances = @($page.items)
    if ($page.hasNext -or $instances.Count -ne 4 -or @($instances | Where-Object { $_.status -notin @('Completed','Finished') -or [int]$_.incidentCount -ne 0 }).Count -ne 0) { throw 'Attribution starts did not all settle without incidents.' }
    [ordered]@{ candidateSha = $CandidateSha; definitionId = $setup.DefinitionId; persistenceMode = $PersistenceMode; traceId = $traceId;
        warmupRequests = 3; markedRequests = 1; completedInstances = $instances.Count; timingEligible = $false;
        observation = 'Five-second before/request-tail/after phases. Separate foreground trace IDs from untraced diagnostic persistence and periodic runtime work.' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
} finally {
    Remove-Item -LiteralPath $ControlPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$ControlPath.next" -ErrorAction SilentlyContinue
}
