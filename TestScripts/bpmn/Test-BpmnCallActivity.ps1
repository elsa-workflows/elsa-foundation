<#
.SYNOPSIS
    BPMN callActivity against a REAL dispatched child workflow (spec 133) - waited completion + error-boundary routing.
.DESCRIPTION
    The spec-133 unit tests drive the engine's failure-outcome ladder with a probe leaf that emits
    DispatchWorkflow's outcome names; this script closes the operational gap by running a BPMN
    `callActivity` element whose bound child is a REAL `Elsa.DispatchWorkflow` node (WaitForCompletion=true)
    against a live server. Two scenarios:

    A. Happy path: publishes a CHILD workflow (WriteLine), then a BPMN parent
       [startEvent -> callActivity(DispatchWorkflow child, waited) -> task(WriteLine) -> endEvent].
       Asserts the child instance ran to Completed, the callActivity's dispatch node resumed with
       outcome 'Completed', and the parent completed through the normal flow.

    B. Error boundary: publishes a CHILD that faults (Fault activity), then a BPMN parent whose
       callActivity carries an error boundaryEvent
       [startEvent -> callActivity -> end-ok; b-error(attached) -> task(recover) -> end-error].
       A faulted child COMPLETES the DispatchWorkflow node with outcome 'Faulted' (never a fault of the
       parent); the engine's spec-133 D3 translation routes the error boundary. Asserts the parent
       completed via the recover path with NO incidents while the child instance ended Faulted.
       (The child's own incident currently surfaces as SchedulerWorkPoisoned instead of
       ActivityReturnedFault - issue #1031; this script asserts the child's status, not its incident shape.)

    Requires the DispatchWorkflow + Bpmn features composed in the server (default Elsa.Server shells.json
    has 'ActivitiesBpmn' + 'ActivitiesDispatchWorkflowRuntime'/'ActivitiesDispatchWorkflowDesign').
.EXAMPLE
    pwsh ./TestScripts/bpmn/Test-BpmnCallActivity.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

# ---- BPMN authored-structure helpers (elsa.bpmn.structure, camelCase per BpmnAuthoredStructure) ----

function New-BpmnStructure {
    param([Parameter(Mandatory)][array] $Activities, [Parameter(Mandatory)][array] $Elements, [Parameter(Mandatory)][array] $Flows)
    @{ kind = "elsa.bpmn.structure"; schemaVersion = "1.0.0"; payload = @{ activities = $Activities; elements = $Elements; sequenceFlows = $Flows } }
}

# One BPMN flow element. Task-family elements bind an Elsa child via -ChildNodeId; a boundaryEvent
# attaches to its host via -AttachedToRef and carries -EventDefinitions (an error boundary binds no child).
function New-BpmnElement {
    param(
        [Parameter(Mandatory)][string] $Id,
        [Parameter(Mandatory)][string] $Type,
        [string] $ChildNodeId,
        [string] $AttachedToRef,
        [array]  $EventDefinitions
    )
    $e = @{ elementId = $Id; elementType = $Type }
    if ($ChildNodeId)      { $e.childNodeId = $ChildNodeId }
    if ($AttachedToRef)    { $e.attachedToRef = $AttachedToRef }
    if ($EventDefinitions) { $e.eventDefinitions = $EventDefinitions }
    $e
}

function New-BpmnFlow {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Source, [Parameter(Mandatory)][string] $Target)
    @{ flowId = $Id; sourceRef = $Source; targetRef = $Target }
}

# The bound DispatchWorkflow node for a callActivity: waited by convention (spec 133 D2).
function New-DispatchNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $VersionId, [Parameter(Mandatory)][string] $ChildDefinitionId)
    New-ActivityNode -NodeId $NodeId -VersionId $VersionId -Inputs @(
        (New-LiteralInput -ReferenceKey "WorkflowDefinitionId" -Value $ChildDefinitionId),
        (New-LiteralInput -ReferenceKey "WaitForCompletion"    -Value $true)
    )
}

# Find the (single) child instance dispatched for an artifact, then wait for its terminal status.
function Wait-ChildInstance {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $ArtifactId)
    $child = $null
    for ($i = 0; $i -lt 60 -and -not $child; $i++) {
        Start-Sleep -Milliseconds 500
        $list = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances?artifactId=$ArtifactId" -WebSession $Ctx.Session
        $items = if ($list.items) { $list.items } elseif ($list -is [array]) { $list } else { $list }
        $child = $items | Where-Object { $_.artifactId -eq $ArtifactId } | Select-Object -First 1
    }
    if (-not $child) { throw "No dispatched child instance surfaced for artifact $ArtifactId." }
    Wait-WorkflowInstance -Ctx $Ctx -ExecutionId $child.workflowExecutionId
}

# Outcome names reported by one node's execution in an instance detail.
function Get-NodeOutcomes {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $NodeId)
    (@($Instance.activities | Where-Object { $_.executableNodeId -eq $NodeId }).outcomeNames) -join ','
}

Write-Host "== BPMN callActivity -> real dispatched child (spec 133) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine   = Invoke-Step "resolve WriteLine"        { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$faultAct    = Invoke-Step "resolve Fault"            { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Fault' }
$dispatch    = Invoke-Step "resolve DispatchWorkflow" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow' }
$bpmnProcess = Invoke-Step "resolve BpmnProcess"      { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Bpmn.Activities.BpmnProcess' }
$token = Get-Random
$failures = @()

# ============================================================================================
# Scenario A - happy path: waited callActivity completes through the child's real completion
# ============================================================================================
Write-Host ""
Write-Host "-- Scenario A: waited callActivity, child completes -> parent routes normal flow --" -ForegroundColor Cyan

$childRoot = New-ActivityNode -NodeId "child-write" -VersionId $writeLine -Inputs @(
    (New-LiteralInput -ReferenceKey "text" -Value "CHILD (callActivity) ran! token=$token")
)
$childDef = Invoke-Step "submit child A"  { Submit-Workflow -Ctx $ctx -Name "BpmnCallChild-$token" -Description "child called by BPMN callActivity" -RootActivity $childRoot }
$childPub = Invoke-Step "publish child A" { Publish-WorkflowVersion -Ctx $ctx -VersionId $childDef.version.id }
Write-Host ("[child A]  definition={0} artifact={1}" -f $childDef.definition.id, $childPub.artifactId)

$parentRoot = New-ActivityNode -NodeId "bpmn-root" -VersionId $bpmnProcess -Structure (New-BpmnStructure `
    -Activities @(
        (New-DispatchNode -NodeId "node-call" -VersionId $dispatch -ChildDefinitionId $childDef.definition.id),
        (New-ActivityNode -NodeId "node-after" -VersionId $writeLine -Inputs @(
            (New-LiteralInput -ReferenceKey "text" -Value "PARENT resumed after real child completed. token=$token")))
    ) `
    -Elements @(
        (New-BpmnElement -Id "start" -Type "startEvent"),
        (New-BpmnElement -Id "call"  -Type "callActivity" -ChildNodeId "node-call"),
        (New-BpmnElement -Id "after" -Type "task"         -ChildNodeId "node-after"),
        (New-BpmnElement -Id "end"   -Type "endEvent")
    ) `
    -Flows @(
        (New-BpmnFlow -Id "flow-1" -Source "start" -Target "call"),
        (New-BpmnFlow -Id "flow-2" -Source "call"  -Target "after"),
        (New-BpmnFlow -Id "flow-3" -Source "after" -Target "end")
    ))
$parentDef = Invoke-Step "submit parent A"  { Submit-Workflow -Ctx $ctx -Name "BpmnCallParent-$token" -Description "BPMN process with a waited callActivity" -RootActivity $parentRoot }
$parentPub = Invoke-Step "publish parent A" { Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDef.version.id }
Write-Host ("[parent A] artifact={0}" -f $parentPub.artifactId)

$run = Invoke-Step "execute parent A" { Invoke-Artifact -Ctx $ctx -ArtifactId $parentPub.artifactId -SourceReferenceId $parentPub.sourceReferenceId }
Write-Host ("[parent A] execution={0}" -f $run.workflowExecutionId)

$parentInst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 120
Write-Host "[observe] parent A instance:"
Show-WorkflowInstance -Instance $parentInst

Write-Host "[observe] child A instance:"
$childInst = Invoke-Step "await child A instance" { Wait-ChildInstance -Ctx $ctx -ArtifactId $childPub.artifactId }
Show-WorkflowInstance -Instance $childInst

$callOutcome = Get-NodeOutcomes -Instance $parentInst -NodeId "node-call"
if ($childInst.instance.status -ne 'Completed')            { $failures += "A: child instance status '$($childInst.instance.status)' (expected Completed)" }
if ($callOutcome -notmatch 'Completed')                    { $failures += "A: dispatch node outcome '$callOutcome' (expected Completed)" }
if ((Get-NodeRunCount -Instance $parentInst -NodeId "node-after") -lt 1) { $failures += "A: post-call task 'node-after' never ran" }
if ($parentInst.instance.status -ne 'Completed')           { $failures += "A: parent status '$($parentInst.instance.status)' (expected Completed)" }
if ($failures.Count -eq 0) { Write-Host "Scenario A OK - parent waited on the real child and completed via 'Completed'." -ForegroundColor Green }

# ============================================================================================
# Scenario B - faulting child: the 'Faulted' outcome routes the callActivity's error boundary
# ============================================================================================
Write-Host ""
Write-Host "-- Scenario B: child faults -> parent's error boundary routes (no parent incident) --" -ForegroundColor Cyan

$faultRoot = New-ActivityNode -NodeId "child-fault" -VersionId $faultAct -Inputs @(
    (New-LiteralInput -ReferenceKey "message" -Value "CHILD faulted deliberately. token=$token")
)
$faultDef = Invoke-Step "submit child B"  { Submit-Workflow -Ctx $ctx -Name "BpmnCallFaultChild-$token" -Description "child that faults" -RootActivity $faultRoot }
$faultPub = Invoke-Step "publish child B" { Publish-WorkflowVersion -Ctx $ctx -VersionId $faultDef.version.id }
Write-Host ("[child B]  definition={0} artifact={1}" -f $faultDef.definition.id, $faultPub.artifactId)

$parentRootB = New-ActivityNode -NodeId "bpmn-root" -VersionId $bpmnProcess -Structure (New-BpmnStructure `
    -Activities @(
        (New-DispatchNode -NodeId "node-call" -VersionId $dispatch -ChildDefinitionId $faultDef.definition.id),
        (New-ActivityNode -NodeId "node-recover" -VersionId $writeLine -Inputs @(
            (New-LiteralInput -ReferenceKey "text" -Value "PARENT error boundary routed the faulted child. token=$token")))
    ) `
    -Elements @(
        (New-BpmnElement -Id "start"   -Type "startEvent"),
        (New-BpmnElement -Id "call"    -Type "callActivity" -ChildNodeId "node-call"),
        (New-BpmnElement -Id "b-error" -Type "boundaryEvent" -AttachedToRef "call" -EventDefinitions @(@{ type = "error" })),
        (New-BpmnElement -Id "recover" -Type "task" -ChildNodeId "node-recover"),
        (New-BpmnElement -Id "end-ok"    -Type "endEvent"),
        (New-BpmnElement -Id "end-error" -Type "endEvent")
    ) `
    -Flows @(
        (New-BpmnFlow -Id "flow-1" -Source "start"   -Target "call"),
        (New-BpmnFlow -Id "flow-2" -Source "call"    -Target "end-ok"),
        (New-BpmnFlow -Id "flow-3" -Source "b-error" -Target "recover"),
        (New-BpmnFlow -Id "flow-4" -Source "recover" -Target "end-error")
    ))
$parentDefB = Invoke-Step "submit parent B"  { Submit-Workflow -Ctx $ctx -Name "BpmnCallFaultParent-$token" -Description "BPMN callActivity with an error boundary" -RootActivity $parentRootB }
$parentPubB = Invoke-Step "publish parent B" { Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDefB.version.id }
Write-Host ("[parent B] artifact={0}" -f $parentPubB.artifactId)

$runB = Invoke-Step "execute parent B" { Invoke-Artifact -Ctx $ctx -ArtifactId $parentPubB.artifactId -SourceReferenceId $parentPubB.sourceReferenceId }
Write-Host ("[parent B] execution={0}" -f $runB.workflowExecutionId)

$parentInstB = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $runB.workflowExecutionId -TimeoutSeconds 120
Write-Host "[observe] parent B instance:"
Show-WorkflowInstance -Instance $parentInstB

Write-Host "[observe] child B instance:"
$childInstB = Invoke-Step "await child B instance" { Wait-ChildInstance -Ctx $ctx -ArtifactId $faultPub.artifactId }
Show-WorkflowInstance -Instance $childInstB

$callOutcomeB = Get-NodeOutcomes -Instance $parentInstB -NodeId "node-call"
if ($childInstB.instance.status -ne 'Faulted')              { $failures += "B: child instance status '$($childInstB.instance.status)' (expected Faulted)" }
if ($callOutcomeB -notmatch 'Faulted')                      { $failures += "B: dispatch node outcome '$callOutcomeB' (expected Faulted)" }
if ((Get-NodeRunCount -Instance $parentInstB -NodeId "node-recover") -lt 1) { $failures += "B: boundary path task 'node-recover' never ran" }
if ($parentInstB.instance.status -ne 'Completed')           { $failures += "B: parent status '$($parentInstB.instance.status)' (expected Completed via the boundary path)" }
if ($parentInstB.instance.incidentCount -gt 0)              { $failures += "B: parent surfaced $($parentInstB.instance.incidentCount) incident(s) (expected none - outcome translation, no seam B)" }
if ($failures.Count -eq 0 -or -not ($failures -match '^B:')) { Write-Host "Scenario B OK - faulted child routed the error boundary; parent completed incident-free." -ForegroundColor Green }

# ============================================================================================
Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host "SUCCESS - BPMN callActivity ran a REAL dispatched child end-to-end: waited completion routed the normal flow; a faulted child routed the error boundary." -ForegroundColor Green
} else {
    Write-Host "FAILED assertions:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
