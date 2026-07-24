<#
.SYNOPSIS
    Composition: a parent dispatches a child workflow AND passes it an input; the child echoes it.
.DESCRIPTION
    Foundation-native equivalent of the JTest composition "dispatch-workflow handling input" case. The parent's
    DispatchWorkflow activity passes `Inputs = { Foo: <value> }` to the child. The child DECLARES input `Foo`
    (state.inputs) and echoes it into an output via a WorkflowRequest expression. Asserts the child instance's
    output equals the value the parent passed.

    Notes learned here:
    - A dispatched child must DECLARE every input the parent passes (state.inputs) or publish fails fast with
      "Workflow input 'x' is not declared by the target executable" - a nice publish-time contract check.
    - A workflow reads its own input via a `WorkflowRequest` expression: value = { memberKey: "<input>" }.
    - Fire-and-forget dispatch (WaitForCompletion=false); the child runs as an independent instance.
    Requires the server + DispatchWorkflow feature (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Value    = "hello-from-parent"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== Composition: dispatch child with input ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$sequence = Invoke-Step "resolve Sequence"        { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$dispatch = Invoke-Step "resolve DispatchWorkflow"{ Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow' }
$token = Get-Random

# CHILD: declares input 'Foo', echoes it into output 'Echo' via a WorkflowRequest expression.
$childRoot = @{
    nodeId = "echo"; activityVersionId = "elsa.intrinsic.set-output@1"; outputs = @()
    intrinsic = @{ kind = "setOutput"; valueType = @{ alias = "String"; collectionKind = "single" } }
    inputs = @(
        (New-LiteralInput -ReferenceKey "name" -Value "Echo"),
        @{ referenceKey = "value"; value = @{ value = @{ memberKey = "Foo" }; expressionType = "WorkflowRequest" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null }
    )
}
$childDef = Invoke-Step "submit child"  { Submit-Workflow -Ctx $ctx -Name "Child-$token" -Description "echo child" -RootActivity $childRoot -Inputs @( (New-InputDef -Key "Foo") ) }
$childPub = Invoke-Step "publish child" { Publish-WorkflowVersion -Ctx $ctx -VersionId $childDef.version.id }
Write-Host ("[child]  definition={0}" -f $childDef.definition.id)

# PARENT: dispatch the child, passing Inputs = { Foo: <value> }.
$childCorrelationId = "child-corr-$token"
$dispatchNode = New-ActivityNode -NodeId "dispatch-child" -VersionId $dispatch -Inputs @(
    (New-LiteralInput -ReferenceKey "WorkflowDefinitionId" -Value $childDef.definition.id),
    (New-LiteralInput -ReferenceKey "WaitForCompletion"    -Value $false),
    (New-LiteralInput -ReferenceKey "CorrelationId"        -Value $childCorrelationId),
    @{ referenceKey = "Inputs"; value = @{ value = @{ Foo = $Value }; expressionType = "Object" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null }
)
$parentRoot = New-ActivityNode -NodeId "parent-root" -VersionId $sequence -Structure (New-SequenceStructure -Activities @($dispatchNode))
$parentDef = Invoke-Step "submit parent"  { Submit-Workflow -Ctx $ctx -Name "Parent-$token" -Description "dispatching parent" -RootActivity $parentRoot }
$parentPub = Invoke-Step "publish parent" { Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDef.version.id }
$run = Invoke-Step "execute parent" { Invoke-Artifact -Ctx $ctx -ArtifactId $parentPub.artifactId -SourceReferenceId $parentPub.sourceReferenceId }
Write-Host ("[parent] execution={0} dispatch={1}" -f $run.workflowExecutionId, $run.commandDispatchStatus)

# Find the child instance (dispatched async) by its unique correlationId - unambiguous even though the
# content-addressed child artifact is shared across runs. Fire-and-forget child start can be slow (the
# reference JTest suite allows 60s), so poll generously. The list endpoint returns a bare JSON array.
Write-Host "[observe] child instance (correlationId=$childCorrelationId):"
$childInst = $null
for ($i = 0; $i -lt 60 -and -not $childInst; $i++) {
    Start-Sleep -Seconds 1
    $list = Invoke-RestMethod "$BaseUrl/runtime/workflows/instances?correlationId=$childCorrelationId" -WebSession $ctx.Session
    $items = if ($null -ne $list.items) { $list.items } else { $list }
    $childInst = $items | Where-Object { $_.correlationId -eq $childCorrelationId } | Sort-Object createdAt -Descending | Select-Object -First 1
}
if (-not $childInst) { Write-Host "  child instance not found (dispatch may not have started a run)" -ForegroundColor Yellow; exit 1 }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $childInst.workflowExecutionId
$echo = ($inst.outputs.PSObject.Properties | Where-Object { $_.Name -ieq 'Echo' } | Select-Object -First 1).Value.value.preview
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $echo -eq $Value) {
    Write-Host ("SUCCESS - parent passed input to child; child echoed '{0}'" -f $echo) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - child status '{0}', echoed '{1}' (expected '{2}')" -f $inst.instance.status, $echo, $Value) -ForegroundColor Red
}
