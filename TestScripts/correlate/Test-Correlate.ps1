<#
.SYNOPSIS
    Correlation: a workflow sets its own correlation id (SetCorrelationId intrinsic), asserted on the instance.
.DESCRIPTION
    Foundation-native equivalent of the JTest "correlate-guid" case. Classic Elsa's Correlate is an activity;
    in Foundation it is the `SetCorrelationId` node intrinsic (`elsa.intrinsic.set-correlation-id@1`). The
    workflow sets a correlation id, and the test asserts the instance carries it and is findable by it via
    `GET /runtime/workflows/instances?correlationId=...`.

    NOTE: the JTest version uses `newGuid()` for the id; Foundation's deterministic JS sandbox has no `newGuid()`
    (see the javascript findings), so this uses a caller-supplied literal id instead - which is what actually
    matters for the correlation behavior.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl       = "http://localhost:5095",
    [string] $Username      = "admin",
    [string] $Password      = "Password123!",
    [string] $CorrelationId = "corr-$(Get-Random -Maximum 999999)"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== Correlate (SetCorrelationId) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }

$setCorr = @{
    nodeId = "set-corr"; activityVersionId = "elsa.intrinsic.set-correlation-id@1"; outputs = @()
    intrinsic = @{ kind = "setCorrelationId"; valueType = @{ alias = "String"; collectionKind = "single" } }
    inputs = @( (New-LiteralInput -ReferenceKey "value" -Value $CorrelationId) )
}
$done = New-SetOutputNode -NodeId "done" -OutputName "Done" -Value "ok"
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($setCorr, $done))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Correlate-$(Get-Random -Max 9999)" -Description "SetCorrelationId" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$found = @(Invoke-RestMethod "$BaseUrl/runtime/workflows/instances?correlationId=$CorrelationId" -WebSession $ctx.Session | Where-Object { $_.correlationId -eq $CorrelationId })
Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $inst.instance.correlationId -eq $CorrelationId -and $found.Count -ge 1) {
    Write-Host ("SUCCESS - instance carries correlationId '{0}' and is findable by it" -f $CorrelationId) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', correlationId '{1}' (expected '{2}'), found={3}" -f $inst.instance.status, $inst.instance.correlationId, $CorrelationId, $found.Count) -ForegroundColor Red
}
