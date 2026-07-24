<#
.SYNOPSIS
    Delay: a mid-flow activity that durably suspends the instance for a duration, then auto-resumes.
.DESCRIPTION
    Root Sequence is [WriteLine("before") -> Delay(Duration) -> WriteLine("after")]. Executing it runs the
    pre-delay step and suspends at the Delay; the hosted DurableTimerPumpTask fires the bookmark at now+Duration
    (swept ~every 10s) and the instance resumes and completes on its own - no external stimulus. Asserts both
    halves: suspended mid-flow, then resumed-to-completion with the post-delay step run.
    Requires the scheduling features composed in the server (see _SchedulingCommon.ps1 / ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Duration = "00:00:02"
)
. "$PSScriptRoot/_SchedulingCommon.ps1"

Write-Host "== Delay (mid-flow durable suspend/resume; Duration=$Duration) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$dl  = Invoke-Step "resolve Delay"     { Get-ActivityVersionId -Ctx $ctx -TypeKey $script:DelayType }

$before = New-ActivityNode -NodeId "before" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "before delay") )
$delay  = New-DelayNode -NodeId "delay" -VersionId $dl -Duration $Duration
$after  = New-ActivityNode -NodeId "after" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "after delay") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($before, $delay, $after))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Delay-$(Get-Random -Max 9999)" -Description "mid-flow Delay" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$wfId = $run.workflowExecutionId
Start-Sleep -Milliseconds 800
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
Show-WorkflowInstance -Instance $inst

# --- assert the SUSPEND half ---
$beforeRan = (Get-NodeRunCount -Instance $inst -NodeId 'before') -ge 1
$suspended = [bool]($inst.activities | Where-Object { $_.executableNodeId -eq 'delay' -and $_.status -eq 'Suspended' })
$afterRan  = (Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1
if (-not ($beforeRan -and $suspended -and -not $afterRan)) {
    Write-Host ("FAIL (suspend) - before={0} delaySuspended={1} after={2}" -f $beforeRan, $suspended, $afterRan) -ForegroundColor Red
    exit 1
}
Write-Host "suspend OK - parked at the Delay (before ran, after did not)." -ForegroundColor Green

# --- wait for the durable-timer pump to auto-resume (Duration + up to ~10s sweep jitter) ---
$completed = $false
for ($i = 0; $i -lt 20 -and -not $completed; $i++) {
    Start-Sleep -Seconds 2
    $inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
    if ($inst.instance.status -in @('Completed','Finished') -and (Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1) { $completed = $true }
}
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($completed) {
    Write-Host "SUCCESS - Delay suspended the instance, then the durable-timer pump resumed it to completion." -ForegroundColor Green
} else {
    Write-Host ("FAIL (resume) - status '{0}', after ran={1} (pump did not resume within budget)." -f $inst.instance.status, ((Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1)) -ForegroundColor Red
    exit 1
}
