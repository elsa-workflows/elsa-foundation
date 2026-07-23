<#
.SYNOPSIS
    Parent/child workflows: a parent that dispatches a separately-published child workflow (DispatchWorkflow).
.DESCRIPTION
    Publishes a CHILD workflow, then a PARENT whose root Sequence is [DispatchWorkflow(child) -> WriteLine].
    Publishing the parent pins the child (DispatchPinSource resolves WorkflowDefinitionId -> exactly one live
    Published artifact), so the child must be published first and resolve uniquely. Executing the parent
    dispatches the child as an independent execution.

    Requires the DispatchWorkflow feature to be composed in the server (project refs +
    'ActivitiesDispatchWorkflowRuntime'/'ActivitiesDispatchWorkflowDesign' in shells.json).

    Uses WaitForCompletion=$false (fire-and-forget): the parent completes immediately with the child's
    dispatch acknowledged (outcome 'Dispatched') and the child runs independently. The waited path
    (WaitForCompletion=$true, formerly broken as issue #1006, fixed by #982) is covered end-to-end by
    bpmn/Test-BpmnCallActivity.ps1.
.EXAMPLE
    pwsh ./TestScripts/Test-ChildWorkflow.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ElsaCommon.ps1"

Write-Host "== Parent/child workflow dispatch ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Invoke-Step "resolve WriteLine"       { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$sequence  = Invoke-Step "resolve Sequence"        { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$dispatch  = Invoke-Step "resolve DispatchWorkflow"{ Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow' }
$token = Get-Random

# 1. child workflow (unique content so it pins to exactly one published artifact)
$childRoot = New-ActivityNode -NodeId "child-write" -VersionId $writeLine -Inputs @(
    (New-LiteralInput -ReferenceKey "text" -Value "CHILD workflow ran! token=$token")
)
$childDef = Invoke-Step "submit child"  { Submit-Workflow -Ctx $ctx -Name "Child-$token" -Description "child workflow" -RootActivity $childRoot }
$childPub = Invoke-Step "publish child" { Publish-WorkflowVersion -Ctx $ctx -VersionId $childDef.version.id }
Write-Host ("[child]  definition={0} artifact={1}" -f $childDef.definition.id, $childPub.artifactId)

# 2. parent workflow: dispatch the child (fire-and-forget), then write a line
$dispatchNode = New-ActivityNode -NodeId "dispatch-child" -VersionId $dispatch -Inputs @(
    (New-LiteralInput -ReferenceKey "WorkflowDefinitionId" -Value $childDef.definition.id),
    (New-LiteralInput -ReferenceKey "WaitForCompletion"    -Value $false)
)
$afterNode = New-ActivityNode -NodeId "parent-after" -VersionId $writeLine -Inputs @(
    (New-LiteralInput -ReferenceKey "text" -Value "PARENT continued after dispatching child. token=$token")
)
$parentRoot = New-ActivityNode -NodeId "parent-root" -VersionId $sequence -Structure (New-SequenceStructure -Activities @($dispatchNode, $afterNode))
$parentDef = Invoke-Step "submit parent"  { Submit-Workflow -Ctx $ctx -Name "Parent-$token" -Description "parent workflow" -RootActivity $parentRoot }
$parentPub = Invoke-Step "publish parent" { Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDef.version.id }
Write-Host ("[parent] artifact={0}" -f $parentPub.artifactId)

# 3. execute the parent
$run = Invoke-Step "execute parent" { Invoke-Artifact -Ctx $ctx -ArtifactId $parentPub.artifactId -SourceReferenceId $parentPub.sourceReferenceId }
Write-Host ("[parent] execution={0} dispatch={1}" -f $run.workflowExecutionId, $run.commandDispatchStatus)

Write-Host "[observe] parent instance:"
$parentInst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $parentInst

# 4. find the child instance (dispatched asynchronously; poll by artifact)
Write-Host "[observe] child instance:"
$childInst = $null
for ($i = 0; $i -lt 12 -and -not $childInst; $i++) {
    Start-Sleep -Milliseconds 500
    $list = Invoke-RestMethod "$BaseUrl/runtime/workflows/instances?artifactId=$($childPub.artifactId)" -WebSession $ctx.Session
    $items = if ($list.items) { $list.items } elseif ($list -is [array]) { $list } else { $list }
    $childInst = $items | Where-Object { $_.artifactId -eq $childPub.artifactId } | Select-Object -First 1
}
if ($childInst) {
    Show-WorkflowInstance -Instance (Wait-WorkflowInstance -Ctx $ctx -ExecutionId $childInst.workflowExecutionId)
} else {
    Write-Host "  (child instance not surfaced by the list query; its WriteLine still prints to the server console)" -ForegroundColor Yellow
}

$dispatchOutcome = ($parentInst.activities | Where-Object { $_.executableNodeId -eq 'dispatch-child' }).outcomeNames -join ','
Write-Host ""
if ($parentInst.instance.status -in @('Completed','Finished') -and $dispatchOutcome -match 'Dispatched') {
    Write-Host "SUCCESS - parent dispatched the child (outcome '$dispatchOutcome') and completed." -ForegroundColor Green
    Write-Host "SERVER console should show: CHILD workflow ran! token=$token"
} else {
    Write-Host ("FINISHED with parent status '{0}', dispatch outcome '{1}'." -f $parentInst.instance.status, $dispatchOutcome) -ForegroundColor Yellow
}
