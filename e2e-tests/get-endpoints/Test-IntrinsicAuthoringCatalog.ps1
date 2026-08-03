<#
.SYNOPSIS
    The authoring catalog offers the engine intrinsics that replace first-class Elsa 3 activities, and a node
    authored straight from a catalog descriptor publishes and runs.

.DESCRIPTION
    #1113: the engine implements nine intrinsic kinds; the authoring catalog used to publish two. Finish,
    SetCorrelationId and SetInstanceName each replace an Elsa 3 activity (Finish/Complete, Correlate, SetName)
    and worked when posted directly - they were simply not DISCOVERABLE, so a designer had no way to offer them.

    This asserts the discoverability half over the real API (the catalog lists them, with the intrinsic kind and
    authoring template a client needs to place one), then closes the loop: it takes the SetCorrelationId
    descriptor's own template, authors a node from it, publishes, runs, and checks the correlation id actually
    landed on the instance. Authoring from the descriptor rather than from a hand-written literal is the point -
    it proves the published descriptor is sufficient to place a working node.

    Merge, Reduce, Control and Return are deliberately NOT offered (Merge/Reduce execute identically to Set
    today; Control/Return are compiler seams). That exclusion is asserted too, so re-adding one is a deliberate
    change rather than a drift.

.EXAMPLE
    pwsh ./e2e-tests/get-endpoints/Test-IntrinsicAuthoringCatalog.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://localhost:5095",
    [string] $CorrelationId = "intrinsic-catalog-$(Get-Random -Maximum 999999)"
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== Intrinsic authoring catalog (#1113) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl

$catalog = Invoke-Step "GET design/activities/catalog" {
    Invoke-RestMethod "$($ctx.BaseUrl)/design/activities/catalog" -WebSession $ctx.Session
}
$activities = if ($catalog.activities) { $catalog.activities } else { $catalog }
$intrinsics = @($activities | Where-Object { $_.intrinsic })

Write-Host ("[catalog] {0} activities, {1} intrinsic descriptors" -f @($activities).Count, $intrinsics.Count)
foreach ($i in ($intrinsics | Sort-Object activityTypeKey)) {
    Write-Host ("  - {0,-28} kind={1,-18} versionId={2}" -f $i.activityTypeKey, $i.intrinsic.kind, $i.activityVersionId)
}

$fail = 0

# --- offered: the five author-facing kinds ---
foreach ($expected in @(
        @{ Key = 'Elsa.SetVariable'; Kind = 'Set'; VersionId = 'elsa.intrinsic.set@1' },
        @{ Key = 'Elsa.SetOutput'; Kind = 'SetOutput'; VersionId = 'elsa.intrinsic.set-output@1' },
        @{ Key = 'Elsa.SetCorrelationId'; Kind = 'SetCorrelationId'; VersionId = 'elsa.intrinsic.set-correlation-id@1' },
        @{ Key = 'Elsa.SetInstanceName'; Kind = 'SetInstanceName'; VersionId = 'elsa.intrinsic.set-instance-name@1' },
        @{ Key = 'Elsa.Finish'; Kind = 'Finish'; VersionId = 'elsa.intrinsic.finish@1' })) {

    $hit = $intrinsics | Where-Object { $_.activityTypeKey -eq $expected.Key } | Select-Object -First 1
    if (-not $hit) {
        Write-Host ("  FAIL  '{0}' is not offered by the catalog" -f $expected.Key) -ForegroundColor Red
        $fail++
        continue
    }
    if ($hit.intrinsic.kind -ne $expected.Kind -or $hit.activityVersionId -ne $expected.VersionId) {
        Write-Host ("  FAIL  '{0}' has kind='{1}' versionId='{2}', expected '{3}'/'{4}'" -f `
                $expected.Key, $hit.intrinsic.kind, $hit.activityVersionId, $expected.Kind, $expected.VersionId) -ForegroundColor Red
        $fail++
        continue
    }
    Write-Host ("  OK    offered: {0}" -f $expected.Key) -ForegroundColor Green
}

# --- withheld: the four engine-internal kinds ---
foreach ($withheld in @('Merge', 'Reduce', 'Control', 'Return')) {
    if ($intrinsics | Where-Object { $_.intrinsic.kind -eq $withheld }) {
        Write-Host ("  FAIL  '{0}' is offered but is meant to stay engine-internal" -f $withheld) -ForegroundColor Red
        $fail++
    }
    else {
        Write-Host ("  OK    withheld: {0}" -f $withheld) -ForegroundColor Green
    }
}

# --- author a node from the SetCorrelationId descriptor and run it ---
$descriptor = $intrinsics | Where-Object { $_.activityTypeKey -eq 'Elsa.SetCorrelationId' } | Select-Object -First 1
if ($descriptor) {
    $instance = Invoke-Step "author + publish + run a node built from the SetCorrelationId descriptor" {
        $seq = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'

        # Everything below comes from the descriptor, not from a hand-written literal: the version id, the
        # intrinsic kind, and the key of the input that carries the value.
        $valueKey = $descriptor.intrinsic.valueInputKey
        $node = @{
            nodeId            = "set-correlation"
            activityVersionId = $descriptor.activityVersionId
            outputs           = @()
            intrinsic         = @{
                kind      = "setCorrelationId"
                valueType = @{ alias = "String"; collectionKind = "single" }
            }
            inputs            = @( (New-LiteralInput -ReferenceKey $valueKey -Value $CorrelationId) )
        }
        $done = New-SetOutputNode -NodeId "done" -OutputName "Done" -Value "ok"
        $root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($node, $done))

        $def = Submit-Workflow -Ctx $ctx -Name "IntrinsicCatalog-$(Get-Random -Maximum 9999)" -RootActivity $root
        $pub = Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id
        $run = Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
        Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
    }

    Show-WorkflowInstance -Instance $instance

    $actual = $instance.instance.correlationId
    if ($instance.instance.status -eq 'Completed' -and $actual -eq $CorrelationId) {
        Write-Host ("  OK    a node authored from the descriptor ran and set correlationId '{0}'" -f $actual) -ForegroundColor Green
    }
    else {
        Write-Host ("  FAIL  status='{0}' correlationId='{1}', expected Completed / '{2}'" -f `
                $instance.instance.status, $actual, $CorrelationId) -ForegroundColor Red
        $fail++
    }
}

Write-Host ""
if ($fail -eq 0) {
    Write-Host "SUCCESS - the catalog offers the five author-facing intrinsics, withholds the four engine-internal ones, and a descriptor-authored node publishes and runs." -ForegroundColor Green
}
else {
    Write-Host "FAILED - $fail intrinsic-catalog assertion(s) failed." -ForegroundColor Red
    exit 1
}
