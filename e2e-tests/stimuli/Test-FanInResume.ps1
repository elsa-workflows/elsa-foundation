<#
.SYNOPSIS
    Fan-in resume: one un-correlated stimulus resumes EVERY waiting instance matching the event.
.DESCRIPTION
    Starts two instances of the same workflow, both parked at a mid-flow Event(EventName) with NO correlation.
    A single ResumeOnly stimulus for that event with NO correlationId (and an input, required for delivery, #1014)
    must resume BOTH waiters (resumedCount>=2) and both run to completion - the fan-in / broadcast semantics.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_StimuliCommon.ps1"

Write-Host "== Fan-in resume (one stimulus resumes many waiters) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$ev  = Invoke-Step "resolve Event"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }

$E = "fanin-evt-$(Get-Random -Max 999999)"
# Two instances of the SAME published artifact, both parked at the same un-correlated event.
$w1 = Invoke-Step "suspend waiter 1" { New-SuspendedWaiter -Ctx $ctx -WriteLineVersion $wl -SequenceVersion $seq -EventVersion $ev -EventName $E -NamePrefix "FanIn" }
$w2Run = Invoke-Step "start waiter 2 (same artifact)" { Invoke-Artifact -Ctx $ctx -ArtifactId $w1.Pub.artifactId -SourceReferenceId $w1.Pub.sourceReferenceId }
Start-Sleep -Milliseconds 800
$exec1 = $w1.ExecutionId; $exec2 = $w2Run.workflowExecutionId

$i1 = Get-WorkflowInstance -Ctx $ctx -ExecutionId $exec1
$i2 = Get-WorkflowInstance -Ctx $ctx -ExecutionId $exec2
if (-not (Test-NodeSuspended -Instance $i1) -or -not (Test-NodeSuspended -Instance $i2)) {
    Write-Host "FAIL - both instances did not suspend at the Event" -ForegroundColor Red; exit 1
}
Write-Host "two instances of the same workflow are parked at event '$E'."

$resp = Invoke-Step "ResumeOnly (no correlationId)" { Invoke-Stimulus -Ctx $ctx -EventName $E -Mode "ResumeOnly" -InputObject @{ eventName = $E } }
Write-Host ("stimulus: resumedCount={0}" -f $resp.resumedCount)

$bothDone = $false
for ($i = 0; $i -lt 15 -and -not $bothDone; $i++) {
    Start-Sleep -Milliseconds 700
    $i1 = Get-WorkflowInstance -Ctx $ctx -ExecutionId $exec1
    $i2 = Get-WorkflowInstance -Ctx $ctx -ExecutionId $exec2
    if ((Get-NodeRunCount -Instance $i1 -NodeId 'after') -ge 1 -and (Get-NodeRunCount -Instance $i2 -NodeId 'after') -ge 1) { $bothDone = $true }
}

Write-Host ""
Write-Host ("instance1={0} instance2={1} bothCompleted={2}" -f $i1.instance.status, $i2.instance.status, $bothDone)
if ($resp.resumedCount -ge 2 -and $bothDone) {
    Write-Host "SUCCESS - one un-correlated stimulus fanned in to both waiters, and both completed." -ForegroundColor Green
} else {
    Write-Host ("FAIL - resumedCount={0} (expected >=2), bothCompleted={1}" -f $resp.resumedCount, $bothDone) -ForegroundColor Red
    exit 1
}
