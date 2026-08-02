<#
.SYNOPSIS
    DispatchWorkflow's outcome matrix over REST, driven directly rather than through a BPMN callActivity.
.DESCRIPTION
    Publishes two child workflows — one that completes, one that faults — and a parent per case whose root
    Sequence is [DispatchWorkflow -> WriteLine]. Three cases:

      A. WaitForCompletion=$false        -> 'Dispatched', parent completes without waiting
      B. WaitForCompletion=$true, child completes -> 'Completed'
      C. WaitForCompletion=$true, child faults    -> 'Faulted', and the PARENT still completes

    Case C is the one worth having: a faulted child must surface as an ordinary routable outcome on the parent's
    dispatch port, not as a fault that takes the parent down with it, and not with the child's diagnostics leaking
    into the parent's result.

    Coverage note. Two of the five declared outcomes are NOT reachable over REST today and are deliberately not
    attempted here:
      * 'Cancelled'      needs an instance-cancellation control-plane call; the runtime API exposes no such
                         endpoint (see src/Elsa/Workflows/Runtime/Api/Endpoints).
      * 'DispatchFailed' needs child-start delivery to exhaust its retries, which no REST surface can force.
    Both are covered in-process by tests/Elsa/Activities/Behavioral/Tests (DispatchWorkflowDrive).

    The waited path is also exercised indirectly by bpmn/Test-BpmnCallActivity.ps1; this script removes the BPMN
    engine from between the assertion and the activity. Fire-and-forget alone is covered by ../Test-ChildWorkflow.ps1.

    Why the Sequence wrapper. The dispatch node is deliberately NOT the workflow root: the inspection read model
    does not project the ROOT activity's completion outcome (it is recorded durably as
    `runtime.completionOutcomeNames` on the activity-execution detail, but `outcomeNames` comes back empty), so a
    bare-root parent would report a Completed node that appears to have taken no port. That is a general
    observability gap, not a DispatchWorkflow one — a root Sequence and a root WriteLine lose their outcome the
    same way. The trailing WriteLine also proves the parent actually continued past the wait.

    Requires the DispatchWorkflow feature composed in the server (see ../README.md).
.EXAMPLE
    pwsh ./e2e-tests/composition/Test-DispatchWorkflowOutcomes.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== DispatchWorkflow outcomes over REST ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx       = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Invoke-Step "resolve WriteLine"        { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$fault     = Invoke-Step "resolve Fault"            { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Fault' }
$sequence  = Invoke-Step "resolve Sequence"         { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$dispatch  = Invoke-Step "resolve DispatchWorkflow" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow' }
$token     = Get-Random
$failures  = @()

# Outcome names reported by one node's execution in an instance detail.
function Get-NodeOutcomes {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $NodeId)
    (@($Instance.activities | Where-Object { $_.executableNodeId -eq $NodeId }).outcomeNames) -join ','
}

# Find the (single) child instance dispatched for an artifact, then wait for its terminal status.
function Wait-ChildInstance {
    param([Parameter(Mandatory)][string] $ArtifactId)
    $child = $null
    for ($i = 0; $i -lt 60 -and -not $child; $i++) {
        Start-Sleep -Milliseconds 500
        $list  = Invoke-RestMethod "$BaseUrl/runtime/workflows/instances?artifactId=$ArtifactId" -WebSession $ctx.Session
        $items = if ($list.items) { $list.items } elseif ($list -is [array]) { $list } else { $list }
        $child = $items | Where-Object { $_.artifactId -eq $ArtifactId } | Select-Object -First 1
    }
    if (-not $child) { throw "No dispatched child instance surfaced for artifact $ArtifactId." }
    Wait-WorkflowInstance -Ctx $ctx -ExecutionId $child.workflowExecutionId
}

# Publish a child workflow whose single node either writes a line or faults.
function Publish-Child {
    param([Parameter(Mandatory)][string] $Kind)
    $root = if ($Kind -eq 'fault') {
        New-ActivityNode -NodeId "child-fault" -VersionId $fault -Inputs @(
            (New-LiteralInput -ReferenceKey "message" -Value "child faulted deliberately. token=$token")
        )
    } else {
        New-ActivityNode -NodeId "child-write" -VersionId $writeLine -Inputs @(
            (New-LiteralInput -ReferenceKey "text" -Value "child ($Kind) ran. token=$token")
        )
    }
    $def = Invoke-Step "submit child ($Kind)"  { Submit-Workflow -Ctx $ctx -Name "DispatchChild-$Kind-$token" -Description "dispatch target" -RootActivity $root }
    $pub = Invoke-Step "publish child ($Kind)" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
    [pscustomobject]@{ DefinitionId = $def.definition.id; ArtifactId = $pub.artifactId }
}

# Publish a parent whose ROOT is a bare DispatchWorkflow node, execute it, and wait for its terminal status.
function Invoke-DispatchCase {
    param(
        [Parameter(Mandatory)][string] $Case,
        [Parameter(Mandatory)][string] $ChildDefinitionId,
        [Parameter(Mandatory)][bool]   $Wait
    )
    $node = New-ActivityNode -NodeId "node-dispatch" -VersionId $dispatch -Inputs @(
        (New-LiteralInput -ReferenceKey "WorkflowDefinitionId" -Value $ChildDefinitionId),
        (New-LiteralInput -ReferenceKey "WaitForCompletion"    -Value $Wait)
    )
    $after = New-ActivityNode -NodeId "node-after" -VersionId $writeLine -Inputs @(
        (New-LiteralInput -ReferenceKey "text" -Value "parent continued past the dispatch ($Case). token=$token")
    )
    $root = New-ActivityNode -NodeId "node-root" -VersionId $sequence -Structure (New-SequenceStructure -Activities @($node, $after))
    $def = Invoke-Step "submit parent ($Case)"  { Submit-Workflow -Ctx $ctx -Name "DispatchParent-$Case-$token" -Description "dispatch parent" -RootActivity $root }
    $pub = Invoke-Step "publish parent ($Case)" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
    $run = Invoke-Step "execute parent ($Case)" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
    Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 30
}

$completingChild = Publish-Child -Kind 'complete'
$faultingChild   = Publish-Child -Kind 'fault'

# --- A. fire-and-forget: the parent does not wait, so the port taken is 'Dispatched' ---
Write-Host "`n[A] fire-and-forget" -ForegroundColor Cyan
$a = Invoke-DispatchCase -Case 'dispatched' -ChildDefinitionId $completingChild.DefinitionId -Wait $false
Show-WorkflowInstance -Instance $a
$outcomeA = Get-NodeOutcomes -Instance $a -NodeId 'node-dispatch'
if ($a.instance.status -notin @('Completed','Finished')) { $failures += "A: parent status '$($a.instance.status)' (expected Completed)" }
if ($outcomeA -notmatch 'Dispatched')                    { $failures += "A: dispatch outcome '$outcomeA' (expected Dispatched)" }

# --- B. waited, child completes ---
Write-Host "`n[B] waited, child completes" -ForegroundColor Cyan
$b = Invoke-DispatchCase -Case 'completed' -ChildDefinitionId $completingChild.DefinitionId -Wait $true
Show-WorkflowInstance -Instance $b
$outcomeB = Get-NodeOutcomes -Instance $b -NodeId 'node-dispatch'
if ($b.instance.status -notin @('Completed','Finished'))     { $failures += "B: parent status '$($b.instance.status)' (expected Completed)" }
if ($outcomeB -notmatch 'Completed')                         { $failures += "B: dispatch outcome '$outcomeB' (expected Completed)" }
if ((Get-NodeRunCount -Instance $b -NodeId 'node-after') -ne 1) { $failures += "B: the step after the dispatch did not run (the parent never resumed past the wait)" }

# --- C. waited, child faults: an ordinary routable outcome, and the parent survives ---
Write-Host "`n[C] waited, child faults" -ForegroundColor Cyan
$c = Invoke-DispatchCase -Case 'faulted' -ChildDefinitionId $faultingChild.DefinitionId -Wait $true
Show-WorkflowInstance -Instance $c
$outcomeC = Get-NodeOutcomes -Instance $c -NodeId 'node-dispatch'
if ($c.instance.status -notin @('Completed','Finished'))     { $failures += "C: parent status '$($c.instance.status)' (expected Completed — a faulted child routes, it does not fault the parent)" }
if ($outcomeC -notmatch 'Faulted')                           { $failures += "C: dispatch outcome '$outcomeC' (expected Faulted)" }
if ((Get-NodeRunCount -Instance $c -NodeId 'node-after') -ne 1) { $failures += "C: the step after the dispatch did not run (a faulted child must still route onward)" }
# The child's fault belongs to the child. A parent that inherits its incidents has leaked them across the boundary.
if ($c.incidents.Count -ne 0)                            { $failures += "C: parent recorded $($c.incidents.Count) incident(s) (expected none — the child's fault is the child's)" }

Write-Host "`n[observe] faulted child instance:" -ForegroundColor Cyan
$childC = Wait-ChildInstance -ArtifactId $faultingChild.ArtifactId
Show-WorkflowInstance -Instance $childC
if ($childC.instance.status -ne 'Faulted') { $failures += "C: child status '$($childC.instance.status)' (expected Faulted)" }

Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host "SUCCESS - DispatchWorkflow routed Dispatched / Completed / Faulted over REST; a faulted child left the parent Completed and incident-free." -ForegroundColor Green
    Write-Host "NOTE    - Cancelled and DispatchFailed are not reachable over REST (no cancellation endpoint, no way to force delivery exhaustion); they are driven in-process instead." -ForegroundColor DarkGray
    exit 0
}

Write-Host "FAILED:" -ForegroundColor Red
$failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
