<#
.SYNOPSIS
    Real-server replay/restart proof for durable alteration plans.
.DESCRIPTION
    Uses an idempotency-key replay to prove one durable plan identity, then restarts Elsa.Server while a second plan
    is eligible for capture/execution. The final public job evidence proves the post-restart worker reconciled the
    acknowledged terminal checkpoint rather than duplicating an observable cancellation. This is the black-box
    acknowledgement/reconciliation proof available to a normal host: fault injection remains covered by the focused
    runtime tests because production HTTP deliberately exposes no acknowledgement-loss switch.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5095',
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [int] $Port = 5095,
    [switch] $RestartServer = $true
)
. "$PSScriptRoot/_AlterationCommon.ps1"
. "$PSScriptRoot/../durability/_DurabilityCommon.ps1"

Write-Host "== Runtime alteration replay + restart (real server) ==  -> $BaseUrl  (restart=$RestartServer)" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$tag = "altreplay-$(Get-Random -Maximum 999999)"
$replayWaiter = Invoke-Step 'create replay waiter' { New-AlterationWaiter -Ctx $ctx -NamePrefix "$tag-replay" }
$key = "$tag-idempotency"
$target = @{ executionIds = @($replayWaiter.ExecutionId) }
$alterations = @( @{ kind = 'CancelWorkflow'; schemaVersion = 1; payload = @{} } )
$first = Invoke-Step 'submit idempotent plan' { Submit-AlterationPlan -Ctx $ctx -Target $target -Alterations $alterations -IdempotencyKey $key }
$second = Invoke-Step 'deliver duplicate idempotent plan' { Submit-AlterationPlan -Ctx $ctx -Target $target -Alterations $alterations -IdempotencyKey $key }
if ($first.PlanId -ne $second.PlanId -or $second.Response.Json.submissionDisposition -ne 'Replayed') {
    throw "Duplicate submission did not replay the durable plan identity: first=$($first.PlanId), second=$($second.PlanId), disposition=$($second.Response.Json.submissionDisposition)"
}
$replayedTerminal = Invoke-Step 'wait for replay plan terminal state' { Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $first.PlanId }
if ($replayedTerminal.Json.counts.targetCount -ne 1 -or $replayedTerminal.Json.counts.succeeded -ne 1) { throw "Replay plan created duplicate work: $($replayedTerminal.Content)" }

# This cohort intentionally spans four default 100-target capture pages. The 301 unavailable identities are safe
# failed jobs; the one real waiter is the observable checkpoint effect. One orchestration sweep captures one page.
# The extra pages make the restart window deterministic despite the five-second recurring sweep cadence. Waiting for
# the first durable page before restarting proves the cursor, remaining capture, and later acknowledgement converge
# across the process boundary rather than merely surviving a pre-capture restart.
$restartWaiter = Invoke-Step 'create restart waiter' { New-AlterationWaiter -Ctx $ctx -NamePrefix "$tag-restart" }
$missingTargets = 1..301 | ForEach-Object { "missing-restart-$tag-$_" }
$restartTargets = @($restartWaiter.ExecutionId) + $missingTargets
$restartPlan = Invoke-Step 'submit restart-continuation plan' {
    Submit-AlterationPlan -Ctx $ctx -IdempotencyKey "$tag-restart" -Target @{ executionIds = $restartTargets } -Alterations $alterations
}

$captureProgress = Invoke-Step 'wait for first durable capture page before restart' {
    Wait-AlterationPlanCaptureProgress -Ctx $ctx -PlanId $restartPlan.PlanId -ExpectedCaptured 100
}
if ($captureProgress.Json.counts.capturedSoFar -ne 100 -or $captureProgress.Json.counts.targetCount -ne 0) {
    throw "Restart plan did not expose exactly one unsealed capture page before restart: $($captureProgress.Content)"
}

if ($RestartServer) {
    Invoke-Step 'restart Elsa.Server during durable plan progression' { Restart-ElsaServer -Port $Port }
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
} else {
    Write-Host '(restart skipped; -RestartServer:$false gives a weaker replay/reconciliation run)' -ForegroundColor Yellow
}

$terminal = Invoke-Step 'wait for post-restart plan terminal state' { Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $restartPlan.PlanId -TimeoutSeconds 180 }
if ($terminal.Json.status -ne 'CompletedWithFailures' -or $terminal.Json.counts.targetCount -ne 302 -or $terminal.Json.counts.succeeded -ne 1 -or $terminal.Json.counts.failed -ne 301) {
    throw "Restarted plan did not reconcile each captured target exactly once: $($terminal.Content)"
}
$page = Invoke-Step 'read first bounded post-restart job page' { Get-AlterationJobsPage -Ctx $ctx -PlanId $restartPlan.PlanId -Take 100 }
if ($page.Json.count -ne 100 -or -not $page.Json.hasNext -or -not $page.Json.nextCursor) { throw "Post-restart first job page is not bounded/continued: $($page.Content)" }
$allJobs = @($page.Json.items)
$cursor = $page.Json.nextCursor
while ($cursor) {
    $continued = Invoke-Step 'read continued post-restart job page' { Get-AlterationJobsPage -Ctx $ctx -PlanId $restartPlan.PlanId -Take 100 -Cursor $cursor }
    $allJobs += @($continued.Json.items)
    $cursor = $continued.Json.nextCursor
}
if ($allJobs.Count -ne 302) { throw "Post-restart job continuation evidence is incomplete: expected 302 jobs, got $($allJobs.Count)." }
if (@($allJobs | Select-Object -ExpandProperty workflowExecutionId | Select-Object -Unique).Count -ne 302) { throw 'Post-restart plan produced duplicate target records.' }
if (@($allJobs | Where-Object status -eq 'Succeeded').Count -ne 1 -or @($allJobs | Where-Object status -eq 'Failed').Count -ne 301) { throw 'Post-restart terminal job evidence did not reconcile to one result per target.' }
$instance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $restartWaiter.ExecutionId
if ($instance.instance.status -ne 'Cancelled') { throw "Restart target '$($restartWaiter.ExecutionId)' was not cancelled exactly once (status '$($instance.instance.status)')." }

Write-Host "SUCCESS - idempotency replay, restart survival, target continuation, duplicate-delivery safety, and terminal checkpoint reconciliation evidence passed." -ForegroundColor Green
