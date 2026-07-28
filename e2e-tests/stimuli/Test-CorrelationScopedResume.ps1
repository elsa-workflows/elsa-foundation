<#
.SYNOPSIS
    Correlation-scoped resume: a correlated stimulus resumes ONLY the waiter with the matching bookmark correlation.
.DESCRIPTION
    Publishes two workflows that both suspend at a mid-flow Event with the SAME event name (same stimulus hash) but
    DIFFERENT CorrelationId inputs (cA vs cB) - so their bookmarks carry runtime.correlationId = cA / cB. A single
    ResumeOnly stimulus for that event with correlationId=cA (and an input, required for delivery, #1014) must:
      - resume exactly ONE instance (resumedCount==1), the cA one, which runs to completion;
      - leave the cB instance still parked at its wait.
    This is the Foundation equivalent of Elsa 3's "signal a correlated workflow". Requires the server running from
    source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_StimuliCommon.ps1"

Write-Host "== Correlation-scoped resume (one event, two correlations) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$ev  = Invoke-Step "resolve Event"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }

$E  = "corr-evt-$(Get-Random -Max 999999)"
$cA = "cust-A-$(Get-Random -Max 9999)"
$cB = "cust-B-$(Get-Random -Max 9999)"

$A = Invoke-Step "suspend waiter A (corr=$cA)" { New-SuspendedWaiter -Ctx $ctx -WriteLineVersion $wl -SequenceVersion $seq -EventVersion $ev -EventName $E -CorrelationId $cA -NamePrefix "CorrA" }
$B = Invoke-Step "suspend waiter B (corr=$cB)" { New-SuspendedWaiter -Ctx $ctx -WriteLineVersion $wl -SequenceVersion $seq -EventVersion $ev -EventName $E -CorrelationId $cB -NamePrefix "CorrB" }

$instA = Get-WorkflowInstance -Ctx $ctx -ExecutionId $A.ExecutionId
$instB = Get-WorkflowInstance -Ctx $ctx -ExecutionId $B.ExecutionId
if (-not (Test-NodeSuspended -Instance $instA) -or -not (Test-NodeSuspended -Instance $instB)) {
    Write-Host "FAIL - both waiters did not suspend at the Event" -ForegroundColor Red; exit 1
}
Write-Host "both A and B suspended at the same event '$E' with distinct correlations."

# Correlated resume for cA only.
$resp = Invoke-Step "ResumeOnly correlationId=$cA" { Invoke-Stimulus -Ctx $ctx -EventName $E -Mode "ResumeOnly" -CorrelationId $cA -InputObject @{ eventName = $E } }
$resumedExecs = @($resp.resumes | ForEach-Object { $_.workflowExecutionId })
Write-Host ("stimulus: resumedCount={0} resumes=[{1}]" -f $resp.resumedCount, ($resumedExecs -join ','))

# Give the resumed instance a moment to complete.
$aDone = $false
for ($i = 0; $i -lt 12 -and -not $aDone; $i++) {
    Start-Sleep -Milliseconds 700
    $instA = Get-WorkflowInstance -Ctx $ctx -ExecutionId $A.ExecutionId
    if ((Get-NodeRunCount -Instance $instA -NodeId 'after') -ge 1) { $aDone = $true }
}
$instB = Get-WorkflowInstance -Ctx $ctx -ExecutionId $B.ExecutionId
$bStillWaiting = Test-NodeSuspended -Instance $instB

Write-Host ""
Write-Host ("A: status={0} after-ran={1} | B: status={2} still-waiting={3}" -f $instA.instance.status, $aDone, $instB.instance.status, $bStillWaiting)
if ($resp.resumedCount -eq 1 -and $resumedExecs -contains $A.ExecutionId -and $aDone -and $bStillWaiting) {
    Write-Host "SUCCESS - the correlated stimulus resumed ONLY waiter A (which completed); B stayed parked." -ForegroundColor Green
} else {
    Write-Host ("FAIL - resumedCount={0}, resumedA={1}, A-done={2}, B-still-waiting={3}" -f $resp.resumedCount, ($resumedExecs -contains $A.ExecutionId), $aDone, $bStillWaiting) -ForegroundColor Red
    exit 1
}
