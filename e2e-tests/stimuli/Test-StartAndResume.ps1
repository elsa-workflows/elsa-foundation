<#
.SYNOPSIS
    StartAndResume: a single stimulus both starts a new instance from a start-trigger AND resumes a waiter.
.DESCRIPTION
    For the same event name X, publishes:
      - a START-trigger workflow: [Event(X, CanStartWorkflow=true) -> WriteLine];
      - a WAITER workflow, executed once so it is parked at a mid-flow Event(X, CanStartWorkflow=false).
    One StartAndResume stimulus for X (with an input, required for resume delivery, #1014) must, in a single call,
    report startedCount>=1 (a fresh start-trigger instance) AND resumedCount>=1 (the parked waiter) - and the
    just-started instance must NOT be counted as a resume (the resume set is snapshotted before starts).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_StimuliCommon.ps1"

Write-Host "== StartAndResume (one stimulus starts + resumes) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$ev  = Invoke-Step "resolve Event"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }

$X = "sar-evt-$(Get-Random -Max 999999)"

# 1. the start-trigger workflow
$startNode = New-EventStartNode -NodeId "start" -EventVersionId $ev -EventName $X
$tick = New-ActivityNode -NodeId "tick" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "started via X") )
$startRoot = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($startNode, $tick))
$sDef = Invoke-Step "submit start-trigger"  { Submit-Workflow -Ctx $ctx -Name "SarStart-$(Get-Random -Max 9999)" -Description "start trigger" -RootActivity $startRoot }
$sPub = Invoke-Step "publish start-trigger" { Publish-WorkflowVersion -Ctx $ctx -VersionId $sDef.version.id }

# 2. the waiter workflow, parked mid-flow on X
$w = Invoke-Step "suspend waiter on X" { New-SuspendedWaiter -Ctx $ctx -WriteLineVersion $wl -SequenceVersion $seq -EventVersion $ev -EventName $X -NamePrefix "SarWait" }
$waiterInst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $w.ExecutionId
if (-not (Test-NodeSuspended -Instance $waiterInst)) { Write-Host "FAIL - waiter did not suspend" -ForegroundColor Red; exit 1 }
Start-Sleep -Milliseconds 500

# 3. one StartAndResume stimulus for X
$resp = Invoke-Step "StartAndResume X" { Invoke-Stimulus -Ctx $ctx -EventName $X -Mode "StartAndResume" -InputObject @{ eventName = $X } }
$resumedExecs = @($resp.resumes | ForEach-Object { $_.workflowExecutionId })
Write-Host ("stimulus: startedCount={0} resumedCount={1} resumes=[{2}]" -f $resp.startedCount, $resp.resumedCount, ($resumedExecs -join ','))

# waiter should resume to completion
$waiterDone = $false
for ($i = 0; $i -lt 12 -and -not $waiterDone; $i++) {
    Start-Sleep -Milliseconds 700
    $waiterInst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $w.ExecutionId
    if ((Get-NodeRunCount -Instance $waiterInst -NodeId 'after') -ge 1) { $waiterDone = $true }
}

Write-Host ""
$startedOk  = $resp.startedCount -ge 1
$resumedOk  = $resp.resumedCount -ge 1 -and ($resumedExecs -contains $w.ExecutionId)
$noSelfResume = $resumedExecs -notcontains $sPub.artifactId   # sanity: started instance not in the resume set
if ($startedOk -and $resumedOk -and $waiterDone) {
    Write-Host ("SUCCESS - one StartAndResume started a fresh instance (started={0}) AND resumed the waiter (which completed)." -f $resp.startedCount) -ForegroundColor Green
} else {
    Write-Host ("FAIL - startedCount={0} (>=1?), resumedWaiter={1}, waiterCompleted={2}" -f $resp.startedCount, $resumedOk, $waiterDone) -ForegroundColor Red
    exit 1
}
