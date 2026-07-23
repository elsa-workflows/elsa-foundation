<#
.SYNOPSIS
    Per-activity value capture: a WriteLine's input is captured as a diagnostic value snapshot and its
    evidence payload is retrievable via the value-evidence endpoint.
.DESCRIPTION
    Foundation-native analog of the JTest "logging" concept. Classic Elsa 3.x had a per-activity
    `logPersistenceConfig` (Include/Exclude what state gets persisted); Foundation has NO such knob.
    Instead the runtime captures per-activity **value snapshots** (capture mode `DiagnosticSnapshot`)
    that are enumerated on the activity-execution detail and whose evidence payload is fetched separately.
    In `DiagnosticSnapshot` mode that payload is a bounded preview, not the raw authored value.

    This test drives that observability path end to end:
      1. run a Sequence[WriteLine("<marker>")]
      2. read the WriteLine activity-execution: assert it captured >= 1 value snapshot
      3. locate the ActivityInput snapshot for the `Text` input
      4. GET its evidence payload via .../value-evidence/{evidenceId}/payload and assert the preview == marker

    Read-only: it does not change any server-wide diagnostics settings. See ../README.md for why the
    classic `logPersistenceConfig` and the storage-driver dichotomy do not map to Foundation.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== Per-activity value capture (logging analog) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

$marker = "captured-$(Get-Random -Max 999999)"
$wl  = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
$seq = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'

# A bare root activity does not sit at a capture boundary; nesting the WriteLine in a Sequence is what
# produces a diagnostic snapshot for its input. (Boundary-level capture, learned by probing.)
$child = New-ActivityNode -NodeId "writer" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value $marker) )
$root  = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($child))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "ValueCapture-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "per-activity value capture" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$wfId = $inst.instance.workflowExecutionId
$ae   = $inst.activities | Where-Object { $_.executableNodeId -eq 'writer' } | Select-Object -First 1

$ok = $true
if (-not $ae) { Write-Host "FAIL - WriteLine activity execution not found on instance" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host ("WriteLine activityExecutionId = {0}  valueSnapshotCount = {1}" -f $ae.activityExecutionId, $ae.valueSnapshotCount)
if (($ae.valueSnapshotCount | ForEach-Object { [int]$_ }) -lt 1) {
    Write-Host "FAIL - expected at least one captured value snapshot on the WriteLine" -ForegroundColor Red
    $ok = $false
}

# Activity-execution detail enumerates the captured value snapshots (evidence ids + capture metadata).
$detail = Invoke-Step "activity-execution detail" {
    Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$wfId/activity-executions/$($ae.activityExecutionId)" -WebSession $ctx.Session -UseBasicParsing
}
Write-Host "captured snapshots:"
$detail.valueSnapshots | ForEach-Object {
    Write-Host ("  - name={0} subject={1} captureMode={2} captureState={3} evidence={4}" -f $_.name, $_.subject, $_.captureMode, $_.captureState, $_.evidenceId)
}

# The Text input snapshot is the contract this test exercises; an unrelated snapshot must not satisfy it.
$snap = $detail.valueSnapshots | Where-Object { $_.name -ieq 'Text' -and $_.subject -ieq 'ActivityInput' } | Select-Object -First 1
if (-not $snap) {
    $available = @($detail.valueSnapshots | ForEach-Object { "$($_.subject)/$($_.name)" }) -join ', '
    Write-Host "FAIL - Text/ActivityInput snapshot not found; available snapshots: $available" -ForegroundColor Red
    exit 1
}
if ($snap.captureMode -notin @('DiagnosticSnapshot', 'Payload')) {
    Write-Host "FAIL - Text/ActivityInput capture mode '$($snap.captureMode)' does not include value evidence" -ForegroundColor Red
    exit 1
}

# Evidence payload is fetched separately (not inlined in the detail), keyed by evidence id.
$payload = Invoke-Step "value-evidence payload" {
    Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$wfId/activity-executions/$($ae.activityExecutionId)/value-evidence/$($snap.evidenceId)/payload" -WebSession $ctx.Session -UseBasicParsing
}
$preview = $payload.payload.preview
Write-Host ("payload: kind={0} typeName={1} preview='{2}'" -f $payload.payload.kind, $payload.payload.typeName, $preview)

Write-Host ""
if ($ok -and $inst.instance.status -in @('Completed','Finished') -and $preview -eq $marker) {
    Write-Host ("SUCCESS - WriteLine 'Text' input captured and retrievable: '{0}'" -f $preview) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', captured preview '{1}' (expected '{2}')" -f $inst.instance.status, $preview, $marker) -ForegroundColor Red
    exit 1
}
