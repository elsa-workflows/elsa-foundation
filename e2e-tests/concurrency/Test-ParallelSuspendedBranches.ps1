<#
.SYNOPSIS
    Parallel with branches that suspend independently: one instance holds multiple concurrent bookmarks; resuming
    one leaves the other parked; the join fires only when all branches complete.
.DESCRIPTION
    A `Parallel` with two branches, each a mid-flow `Event` wait on a DISTINCT event (E1, E2). Executing it parks
    BOTH branches (two concurrent bookmarks under one instance). Resuming E1 only advances branch-1 to Completed
    while branch-2 stays Suspended and the Parallel stays Running; resuming E2 then completes branch-2 and the
    Parallel joins to Completed. Exercises the stateless join re-reading durable branch state + multi-bookmark
    partial resume - behavior the in-process C# harness can't reproduce (no real bookmark/stimulus pipeline).
    Requires the server from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ConcurrencyCommon.ps1"

Write-Host "== Parallel with independently-suspending branches (partial resume) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$par = Invoke-Step "resolve Parallel" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Parallel.Activities.Parallel' }
$ev  = Invoke-Step "resolve Event"    { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }
$E1 = "par-e1-$(Get-Random -Max 999999)"; $E2 = "par-e2-$(Get-Random -Max 999999)"

$branches = @(
    @{ name = "branch-1"; activity = (New-EventWaitNode -NodeId "branch-1" -EventVersionId $ev -EventName $E1) },
    @{ name = "branch-2"; activity = (New-EventWaitNode -NodeId "branch-2" -EventVersionId $ev -EventName $E2) }
)
$root = New-ActivityNode -NodeId "parallel-root" -VersionId $par -Structure (New-ParallelStructure -Branches $branches)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "ParSusp-$(Get-Random -Max 9999)" -Description "parallel suspended branches" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$wfId = $run.workflowExecutionId
Start-Sleep -Milliseconds 900
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
Show-WorkflowInstance -Instance $inst

# both branches parked (two concurrent bookmarks)
$b1s = Test-NodeStatus -Instance $inst -NodeId 'branch-1' -Status 'Suspended'
$b2s = Test-NodeStatus -Instance $inst -NodeId 'branch-2' -Status 'Suspended'
if (-not ($b1s -and $b2s -and $inst.instance.status -eq 'Running')) {
    Write-Host ("FAIL (fork) - both branches did not suspend (b1={0} b2={1} status={2})." -f $b1s, $b2s, $inst.instance.status) -ForegroundColor Red; exit 1
}
Write-Host "both branches suspended (two concurrent bookmarks); Parallel Running."

# resume E1 only -> branch-1 completes, branch-2 still parked, Parallel still Running
Invoke-Step "resume E1" { Invoke-ResumeStimulus -Ctx $ctx -EventName $E1 } | Out-Null
$partial = $false
for ($i = 0; $i -lt 12 -and -not $partial; $i++) {
    Start-Sleep -Milliseconds 700
    $inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
    if (Test-NodeStatus -Instance $inst -NodeId 'branch-1' -Status 'Completed') { $partial = $true }
}
$b2StillParked = Test-NodeStatus -Instance $inst -NodeId 'branch-2' -Status 'Suspended'
$notJoinedYet = $inst.instance.status -eq 'Running'
Write-Host ("after resume E1: branch-1 completed={0}, branch-2 still parked={1}, Parallel Running={2}" -f $partial, $b2StillParked, $notJoinedYet)
if (-not ($partial -and $b2StillParked -and $notJoinedYet)) {
    Write-Host "FAIL (partial resume) - resuming E1 should complete branch-1 only and leave branch-2 parked with the Parallel Running." -ForegroundColor Red; exit 1
}

# resume E2 -> branch-2 completes, Parallel joins -> Completed
Invoke-Step "resume E2" { Invoke-ResumeStimulus -Ctx $ctx -EventName $E2 } | Out-Null
$joined = $false
for ($i = 0; $i -lt 12 -and -not $joined; $i++) {
    Start-Sleep -Milliseconds 700
    $inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
    if ($inst.instance.status -in @('Completed','Finished')) { $joined = $true }
}
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($joined -and (Test-NodeStatus -Instance $inst -NodeId 'branch-1' -Status 'Completed') -and (Test-NodeStatus -Instance $inst -NodeId 'branch-2' -Status 'Completed')) {
    Write-Host "SUCCESS - two branches suspended independently; partial resume advanced only branch-1; resuming branch-2 joined the Parallel to Completed." -ForegroundColor Green
} else {
    Write-Host ("FAIL (join) - status '{0}' after resuming both branches." -f $inst.instance.status) -ForegroundColor Red; exit 1
}
