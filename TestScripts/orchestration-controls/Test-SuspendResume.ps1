<#
.SYNOPSIS
    Suspend & resume via a mid-flow Event wait + stimulus. Suspend works; resume delivery is tracked against
    issue #1014.
.DESCRIPTION
    The canonical runtime pause/resume shape: a workflow suspends at a mid-flow `Event` catch
    (`CanStartWorkflow=false`) and should resume when the matching event stimulus is published.

    Verified behaviour on current main:
      - SUSPEND works: the pre-wait step runs, the `Event` node reaches `Suspended`, the post-wait step does not
        run. (Note: the workflow-level status stays `Running` rather than `Suspended` — see #1014.)
      - RESUME is acknowledged but does NOT deliver: `POST runtime/workflows/stimuli` returns `resumedCount=1`
        targeting the instance, yet the instance never progresses (the post-wait step never runs). Tracked as
        issue #1014.

    This is a living tracker: it asserts the suspend half strictly, then attempts the resume and reports either
    KNOWN ISSUE #1014 (resume did not deliver) or FIXED (resume completed the workflow — convert to a strict
    assertion). Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ControlsCommon.ps1"

Write-Host "== Suspend & resume (mid-flow Event wait + stimulus; resume tracked by #1014) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$ev  = Invoke-Step "resolve Event"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }
$E = "wait-$(Get-Random -Max 999999)"

$before = New-ActivityNode -NodeId "before" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "before wait") )
$wait   = New-EventWaitNode -NodeId "wait" -EventVersionId $ev -EventName $E
$after  = New-ActivityNode -NodeId "after" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "after wait") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($before, $wait, $after))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "SuspendResume-$(Get-Random -Max 9999)" -Description "mid-flow Event wait" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$wfId = $run.workflowExecutionId
Start-Sleep -Milliseconds 800
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
Show-WorkflowInstance -Instance $inst

# --- assert the SUSPEND half strictly ---
$beforeRan = (Get-NodeRunCount -Instance $inst -NodeId 'before') -ge 1
$suspended = Test-ActivitySuspended -Instance $inst -NodeId 'wait'
$afterRan  = (Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1
if (-not ($beforeRan -and $suspended -and -not $afterRan)) {
    Write-Host ("FAIL (suspend) - before={0} waitSuspended={1} after={2}" -f $beforeRan, $suspended, $afterRan) -ForegroundColor Red
    exit 1
}
Write-Host "suspend OK - parked at the Event wait (before ran, after did not)." -ForegroundColor Green

# --- attempt RESUME (tracked by #1014) ---
$resp = Invoke-Step "resume stimulus" { Publish-EventStimulus -Ctx $ctx -EventName $E -Mode "ResumeOnly" }
Write-Host ("stimulus: resumedCount={0} (resumes -> {1})" -f $resp.resumedCount, (($resp.resumes | ForEach-Object { $_.status }) -join ','))
$completed = $false
for ($i = 0; $i -lt 15 -and -not $completed; $i++) {
    Start-Sleep -Milliseconds 800
    $inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
    if ((Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1) { $completed = $true }
}

Write-Host ""
if ($completed) {
    Write-Host "FIXED (#1014) - resume delivered and the workflow completed. Convert this tracker to a strict suspend+resume assertion." -ForegroundColor Green
} elseif ($resp.resumedCount -ge 1) {
    Write-Host "KNOWN ISSUE #1014 - suspend works and the stimulus matched (resumedCount>=1), but the resume did not deliver (post-wait step never ran)." -ForegroundColor Yellow
} else {
    Write-Host ("MISMATCH - resume stimulus matched nothing (resumedCount={0})." -f $resp.resumedCount) -ForegroundColor Red
    exit 1
}
