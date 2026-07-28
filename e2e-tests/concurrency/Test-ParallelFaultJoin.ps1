<#
.SYNOPSIS
    Parallel join under a branch fault: a faulting branch faults the whole Parallel (default all-branches threshold),
    but a completion Threshold lets the join succeed despite the fault.
.DESCRIPTION
    Two cases against a 3-branch Parallel (branch-1 = Fault, branch-2/3 = WriteLine):
      A. DEFAULT threshold (all): the faulted branch makes the join unsatisfiable, so the Parallel faults
         (fault code `parallel.join.unsatisfied`) rather than hang; the sibling WriteLine branches still ran and
         the instance reaches a terminal Faulted state (not stuck Running).
      B. Threshold = 2: two successful branches meet the threshold, so the Parallel completes `Done` even though
         branch-1 faulted.
    Exercises the stateless fault-join decision (`ApplyJoinDecision`) over the real incident/checkpoint pipeline -
    a wrong decision would hang an instance forever, so this is a high-value correctness check.
    Requires the server from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ConcurrencyCommon.ps1"

Write-Host "== Parallel fault-join + threshold ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$par = Invoke-Step "resolve Parallel"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Parallel.Activities.Parallel' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$flt = Invoke-Step "resolve Fault"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Fault' }

function New-FaultBranches {
    @(
        @{ name = "branch-fault"; activity = (New-FaultNode     -NodeId "branch-fault" -FaultVersionId $flt -Message "branch boom") },
        @{ name = "branch-ok1";   activity = (New-ActivityNode  -NodeId "branch-ok1" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "ok1") )) },
        @{ name = "branch-ok2";   activity = (New-ActivityNode  -NodeId "branch-ok2" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "ok2") )) }
    )
}
function Run-Parallel($structure, $label) {
    $root = New-ActivityNode -NodeId "parallel-root" -VersionId $par -Structure $structure
    $def = Submit-Workflow -Ctx $ctx -Name "ParFault-$label-$(Get-Random -Max 9999)" -Description "parallel fault join $label" -RootActivity $root
    $pub = Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
    Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
}

# --- Case A: default threshold (all) -> a faulted branch faults the Parallel, no hang ---
Write-Host "`n[A] default threshold: one branch faults" -ForegroundColor Cyan
$instA = Invoke-Step "run (default threshold)" { Run-Parallel (New-ParallelStructure -Branches (New-FaultBranches)) "all" }
Show-WorkflowInstance -Instance $instA
$aFaulted   = $instA.instance.status -eq 'Faulted'
$aBranchFlt = Test-NodeStatus -Instance $instA -NodeId 'branch-fault' -Status 'Faulted'
$aSiblings  = (Get-NodeRunCount -Instance $instA -NodeId 'branch-ok1') -ge 1 -and (Get-NodeRunCount -Instance $instA -NodeId 'branch-ok2') -ge 1
$aJoinMsg   = (Get-IncidentMessages -Instance $instA) -join ' | '
Write-Host ("[A] Parallel Faulted={0}, branch-fault Faulted={1}, siblings ran={2}" -f $aFaulted, $aBranchFlt, $aSiblings)
Write-Host ("[A] incidents: {0}" -f $aJoinMsg)

# --- Case B: threshold = 2 -> completes Done despite the fault ---
Write-Host "`n[B] threshold=2: two successes satisfy the join despite the fault" -ForegroundColor Cyan
$instB = Invoke-Step "run (threshold=2)" { Run-Parallel (New-ParallelStructure -Branches (New-FaultBranches) -Threshold 2) "t2" }
Show-WorkflowInstance -Instance $instB
$bCompleted = $instB.instance.status -in @('Completed','Finished')
$bSiblings  = (Get-NodeRunCount -Instance $instB -NodeId 'branch-ok1') -ge 1 -and (Get-NodeRunCount -Instance $instB -NodeId 'branch-ok2') -ge 1
Write-Host ("[B] Parallel Completed={0}, siblings ran={1}" -f $bCompleted, $bSiblings)

Write-Host ""
$okA = $aFaulted -and $aBranchFlt -and $aSiblings -and ($aJoinMsg -match 'parallel\.join\.unsatisfied|branch boom|graph boundary')
$okB = $bCompleted -and $bSiblings
if ($okA -and $okB) {
    Write-Host "SUCCESS - [A] a faulted branch faulted the Parallel (no hang, siblings ran); [B] threshold=2 completed Done despite the fault." -ForegroundColor Green
} else {
    Write-Host ("FAIL - A(ok={0}: faulted={1} branchFault={2} siblings={3}) B(ok={4}: completed={5} siblings={6})" -f $okA, $aFaulted, $aBranchFlt, $aSiblings, $okB, $bCompleted, $bSiblings) -ForegroundColor Red
    exit 1
}
