<#
.SYNOPSIS
    Suspend & resume via a mid-flow Event wait + stimulus. Asserts both halves strictly.
.DESCRIPTION
    The canonical runtime pause/resume shape: a workflow suspends at a mid-flow `Event` catch
    (`CanStartWorkflow=false`) and resumes when the matching event stimulus is published.

    Verified behaviour on current main (resume delivery fixed by #1038, issue #1014):
      - SUSPEND: the pre-wait step runs, the `Event` node reaches `Suspended`, the post-wait step does not.
        (The workflow-level status stays `Running` while a descendant is parked.)
      - RESUME: `POST runtime/workflows/stimuli` (ResumeOnly) with a non-empty `input` payload delivers and the
        post-wait step runs to completion. NOTE: the `input` is required — an input-less resume matches the
        bookmark (`resumedCount=1`) but silently never delivers (see Publish-EventStimulus / #1014).

    Requires the server running from source (see ../README.md).
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

# --- RESUME: ResumeOnly stimulus carrying an input payload (required for delivery, #1014/#1038) ---
$resp = Invoke-Step "resume stimulus" { Publish-EventStimulus -Ctx $ctx -EventName $E -Mode "ResumeOnly" -InputObject @{ eventName = $E } }
Write-Host ("stimulus: resumedCount={0} (resumes -> {1})" -f $resp.resumedCount, (($resp.resumes | ForEach-Object { $_.status }) -join ','))
$completed = $false
for ($i = 0; $i -lt 15 -and -not $completed; $i++) {
    Start-Sleep -Milliseconds 800
    $inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
    if ((Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1) { $completed = $true }
}
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($resp.resumedCount -ge 1 -and $completed) {
    Write-Host "SUCCESS - suspended at the Event wait, then the stimulus resumed it and the post-wait step ran." -ForegroundColor Green
} else {
    Write-Host ("FAIL (resume) - resumedCount={0}, post-wait ran={1}." -f $resp.resumedCount, $completed) -ForegroundColor Red
    exit 1
}
