<#
.SYNOPSIS
    Real-server durable alteration plan coverage for every built-in, plan shape, paging, cancellation, and redaction.
.DESCRIPTION
    Creates two Event waiters, cancels both through one durable CancelWorkflow plan, and observes only public plan/job
    reads until the hosted orchestration pump reaches a terminal state. It then proves successful ModifyVariable,
    Sequence-owned ScheduleActivity, RescheduleActivity, and a retained-identity Migrate smoke path. A second plan
    is cancelled cooperatively to cover the control route. Protected payload/idempotency material is deliberately
    submitted with recognisable values and must never appear in any public response.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5095',
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)
. "$PSScriptRoot/_AlterationCommon.ps1"

Write-Host "== Runtime alteration plans (real server) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$tag = "altplan-$(Get-Random -Maximum 999999)"
$waiterA = Invoke-Step 'create suspended waiter A' { New-AlterationWaiter -Ctx $ctx -NamePrefix "$tag-a" }
$waiterB = Invoke-Step 'create suspended waiter B' { New-AlterationWaiter -Ctx $ctx -NamePrefix "$tag-b" }

# One plan, two targets: public receipt must carry links/identity only, never request payload material.
$secret = "alteration-secret-$tag"
$accepted = Invoke-Step 'submit bulk CancelWorkflow plan' {
    Submit-AlterationPlan -Ctx $ctx -IdempotencyKey "$tag-bulk-$secret" `
        -Target @{ executionIds = @($waiterB.ExecutionId, $waiterA.ExecutionId) } `
        -Alterations @( @{ kind = 'CancelWorkflow'; schemaVersion = 1; payload = @{} } )
}
if ($accepted.Response.Json.status -notin @('CapturingTargets','Queued','Running')) { throw "Unexpected admission status '$($accepted.Response.Json.status)'." }
if (-not $accepted.Response.Json.links.self -or -not $accepted.Response.Headers.Location) { throw 'Accepted receipt must contain self link and Location header.' }
Assert-AlterationResponseIsRedacted -Content $accepted.Response.Content -Secrets @($secret, $accepted.IdempotencyKey)

$terminal = Invoke-Step 'wait for bulk plan terminal state' { Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $accepted.PlanId }
if ($terminal.Json.status -ne 'Completed') { throw "Bulk cancellation plan ended '$($terminal.Json.status)' instead of Completed: $($terminal.Content)" }
if ($terminal.Json.counts.succeeded -ne 2 -or $terminal.Json.counts.failed -ne 0) { throw "Unexpected terminal counts: $($terminal.Json.counts | ConvertTo-Json -Compress)" }
Assert-AlterationResponseIsRedacted -Content $terminal.Content -Secrets @($secret, $accepted.IdempotencyKey)

$jobs = Invoke-Step 'read stable jobs page' { Get-AlterationJobsPage -Ctx $ctx -PlanId $accepted.PlanId -Take 100 }
if ($jobs.Json.count -ne 2 -or $jobs.Json.totalCount -ne 2 -or $jobs.Json.hasNext) { throw "Expected one two-job page: $($jobs.Content)" }
$jobIds = @($jobs.Json.items | ForEach-Object { $_.jobId })
if (($jobIds | Select-Object -Unique).Count -ne 2) { throw 'Bulk plan page contained duplicate job identities.' }
foreach ($job in $jobs.Json.items) {
    if ($job.status -ne 'Succeeded') { throw "Bulk job '$($job.jobId)' ended '$($job.status)'." }
    $jobRead = Invoke-AlterationRequest -Ctx $ctx -Method GET -Path "runtime/workflows/alteration-plans/$($accepted.PlanId)/jobs/$($job.jobId)"
    if ($jobRead.Code -ne 200) { throw "Job read returned HTTP $($jobRead.Code): $($jobRead.Content)" }
    Assert-AlterationResponseIsRedacted -Content $jobRead.Content -Secrets @($secret, $accepted.IdempotencyKey)
}

foreach ($waiter in @($waiterA, $waiterB)) {
    $instance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $waiter.ExecutionId
    if ($instance.instance.status -ne 'Cancelled') { throw "Execution '$($waiter.ExecutionId)' was not cancelled (actual '$($instance.instance.status)')." }
}

# ModifyVariable: a real suspended workflow carries a declared root-frame variable. The replacement is never exposed
# by the plan or job read, while a successful outcome proves it passed captured-revision/type/protection preflight.
# The public instance projection deliberately does not expose root-variable frame values, so this test does not echo
# the replacement value through a workflow output merely to observe it.
$variableSecret = "replacement-secret-$tag"
$variableWaiter = Invoke-Step 'create root-variable waiter' {
    New-AlterationWaiter -Ctx $ctx -NamePrefix "$tag-variable" -RootVariableKey 'operator-note' -RootVariableDefault 'before'
}
$modify = Invoke-Step 'submit ModifyVariable plan' {
    Submit-AlterationPlan -Ctx $ctx -IdempotencyKey "$tag-modify-$variableSecret" -Target @{ executionIds = @($variableWaiter.ExecutionId) } `
        -Alterations @( @{ kind = 'ModifyVariable'; schemaVersion = 1; payload = @{ variableKey = 'operator-note'; value = $variableSecret } } )
}
$modifyTerminal = Invoke-Step 'wait for ModifyVariable terminal state' { Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $modify.PlanId }
$modifyJob = Invoke-Step 'read ModifyVariable durable outcome' { Get-SingleAlterationJob -Ctx $ctx -PlanId $modify.PlanId }
if ($modifyTerminal.Json.status -ne 'Completed' -or $modifyJob.Json.status -ne 'Succeeded' -or @($modifyJob.Json.outcomes | Where-Object status -eq 'Succeeded').Count -ne 1) {
    throw "ModifyVariable did not commit through the durable checkpoint: $($modifyJob.Content)"
}
Assert-AlterationResponseIsRedacted -Content $modifyTerminal.Content -Secrets @($variableSecret, $modify.IdempotencyKey)
Assert-AlterationResponseIsRedacted -Content $modifyJob.Content -Secrets @($variableSecret, $modify.IdempotencyKey)

# ScheduleActivity: Sequence supplies the compiled direct-child capability and policy. The extra child has no live
# execution while its parent waits on the first child, so this is a genuine successful operator-scheduling fixture.
$scheduleWaiter = Invoke-Step 'create Sequence scheduling waiter' { New-AlterationSchedulableWaiter -Ctx $ctx -NamePrefix "$tag-schedule" }
$schedule = Invoke-Step 'submit ScheduleActivity plan' {
    Submit-AlterationPlan -Ctx $ctx -IdempotencyKey "$tag-schedule" -Target @{ executionIds = @($scheduleWaiter.ExecutionId) } `
        -Alterations @( @{ kind = 'ScheduleActivity'; schemaVersion = 1; payload = @{ nodeId = $scheduleWaiter.OperatorChildNodeId; parentActivityExecutionId = $scheduleWaiter.RootActivityExecutionId } } )
}
$scheduleTerminal = Invoke-Step 'wait for ScheduleActivity terminal state' { Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $schedule.PlanId }
$scheduleJob = Invoke-Step 'read ScheduleActivity durable outcome' { Get-SingleAlterationJob -Ctx $ctx -PlanId $schedule.PlanId }
if ($scheduleTerminal.Json.status -ne 'Completed' -or $scheduleJob.Json.status -ne 'Succeeded' -or @($scheduleJob.Json.outcomes | Where-Object status -eq 'Succeeded').Count -ne 1) {
    throw "ScheduleActivity did not commit its operator-owned continuation: $($scheduleJob.Content)"
}
$scheduledChild = Invoke-Step 'observe scheduled operator child completion' {
    Wait-AlterationActivityObservation -Ctx $ctx -ExecutionId $scheduleWaiter.ExecutionId -NodeId $scheduleWaiter.OperatorChildNodeId -ExpectedStatuses @('Completed')
}
if ($scheduledChild.Activities.Count -ne 1 -or $scheduledChild.Activities[0].status -ne 'Completed') {
    throw "ScheduleActivity did not produce one completed operator child: $($scheduledChild.Instance.activities | ConvertTo-Json -Depth 4 -Compress)"
}

# RescheduleActivity: the Event child is suspended and therefore eligible. The successful alteration must leave its
# durable outcome before the successor continuation is delivered by the normal scheduler lane.
$rescheduleWaiter = Invoke-Step 'create reschedule waiter' { New-AlterationWaiter -Ctx $ctx -NamePrefix "$tag-reschedule" }
$reschedule = Invoke-Step 'submit RescheduleActivity plan' {
    Submit-AlterationPlan -Ctx $ctx -IdempotencyKey "$tag-reschedule" -Target @{ executionIds = @($rescheduleWaiter.ExecutionId) } `
        -Alterations @( @{ kind = 'RescheduleActivity'; schemaVersion = 1; payload = @{ sourceActivityExecutionId = $rescheduleWaiter.WaitActivityExecutionId } } )
}
$rescheduleTerminal = Invoke-Step 'wait for RescheduleActivity terminal state' { Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $reschedule.PlanId }
$rescheduleJob = Invoke-Step 'read RescheduleActivity durable outcome' { Get-SingleAlterationJob -Ctx $ctx -PlanId $reschedule.PlanId }
if ($rescheduleTerminal.Json.status -ne 'Completed' -or $rescheduleJob.Json.status -ne 'Succeeded' -or @($rescheduleJob.Json.outcomes | Where-Object status -eq 'Succeeded').Count -ne 1) {
    throw "RescheduleActivity did not record supersession/checkpoint evidence: $($rescheduleJob.Content)"
}
$rescheduleObservation = Invoke-Step 'observe RescheduleActivity supersession lineage' {
    Wait-AlterationRescheduleObservation -Ctx $ctx -ExecutionId $rescheduleWaiter.ExecutionId -NodeId 'wait' -SourceActivityExecutionId $rescheduleWaiter.WaitActivityExecutionId
}
if ($rescheduleObservation.Successor.status -notin @('Scheduled','Running','Suspended','Completed')) {
    throw "RescheduleActivity successor '$($rescheduleObservation.Successor.activityExecutionId)' reached unexpected status '$($rescheduleObservation.Successor.status)'."
}

# Migrate smoke path: the public authoring API currently has no clone-to-new-version operation, so this fixture uses
# the same retained compatible artifact identity. It exercises strict five-field decoding, retained-reference lookup,
# suspended/quiescent proof, compatibility, and root checkpoint staging, but does not claim to prove a cross-artifact
# repin without inventing a private test hook.
$migrateWaiter = Invoke-Step 'create migratable suspended waiter' { New-AlterationWaiter -Ctx $ctx -NamePrefix "$tag-migrate" }
$targetIdentity = Invoke-Step 'read retained source artifact identity' { Get-ExecutableIdentity -Ctx $ctx -Published $migrateWaiter.Published }
$migrate = Invoke-Step 'submit Migrate plan' {
    Submit-AlterationPlan -Ctx $ctx -IdempotencyKey "$tag-migrate" -Target @{ executionIds = @($migrateWaiter.ExecutionId) } `
        -Alterations @( @{ kind = 'Migrate'; schemaVersion = 1; payload = @{ targetArtifact = $targetIdentity } } )
}
$migrateTerminal = Invoke-Step 'wait for Migrate terminal state' { Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $migrate.PlanId }
$migrateJob = Invoke-Step 'read Migrate durable outcome' { Get-SingleAlterationJob -Ctx $ctx -PlanId $migrate.PlanId }
if ($migrateTerminal.Json.status -ne 'Completed' -or $migrateJob.Json.status -ne 'Succeeded' -or @($migrateJob.Json.outcomes | Where-Object status -eq 'Succeeded').Count -ne 1) {
    throw "Migrate did not record compatible suspended-execution checkpoint evidence: $($migrateJob.Content)"
}
Assert-AlterationResponseIsRedacted -Content $migrateTerminal.Content -Secrets @($targetIdentity.artifactHash, $migrate.IdempotencyKey)
Assert-AlterationResponseIsRedacted -Content $migrateJob.Content -Secrets @($targetIdentity.artifactHash, $migrate.IdempotencyKey)

# A cancellation requested immediately after admission is cooperative. A quick hosted pump is permitted to finish
# first, so the contract is 202 while active or 200 as a terminal no-op; either response must still be redacted.
$waiterC = Invoke-Step 'create cancellation-control waiter' { New-AlterationWaiter -Ctx $ctx -NamePrefix "$tag-control" }
$control = Invoke-Step 'submit control plan' {
    Submit-AlterationPlan -Ctx $ctx -IdempotencyKey "$tag-control-$secret" -Target @{ executionIds = @($waiterC.ExecutionId) } `
        -Alterations @( @{ kind = 'CancelWorkflow'; schemaVersion = 1; payload = @{} } )
}
$cancel = Invoke-Step 'request cooperative plan cancellation' {
    Invoke-AlterationRequest -Ctx $ctx -Method POST -Path "runtime/workflows/alteration-plans/$($control.PlanId)/cancel"
}
if ($cancel.Code -notin @(200,202)) { throw "Plan cancellation returned HTTP $($cancel.Code): $($cancel.Content)" }
Assert-AlterationResponseIsRedacted -Content $cancel.Content -Secrets @($secret, $control.IdempotencyKey)
$controlTerminal = Invoke-Step 'wait for cancelled-control plan terminal state' { Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $control.PlanId }
if ($controlTerminal.Json.status -notin @('Cancelled','Completed')) { throw "Control plan terminal status '$($controlTerminal.Json.status)' is not contract-valid." }

Write-Host "SUCCESS - CancelWorkflow, ModifyVariable, ScheduleActivity, RescheduleActivity, and the retained-identity Migrate smoke path; stable paging, cooperative cancellation, visible schedule/reschedule effects, and redaction passed." -ForegroundColor Green
