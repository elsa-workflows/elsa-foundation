<#
.SYNOPSIS
    Stimulus routing / bookmark matching: which stimuli match a mid-flow wait, and mode/no-match semantics.
.DESCRIPTION
    Tests the trigger/bookmark + stimulus-router matching layer, which is observable via the dispatch counts on
    the stimulus response (`startedCount` / `resumedCount`) independently of the resume *delivery* issue (#1014).

    Against a workflow suspended at a mid-flow `Event(CanStartWorkflow=false)` catch (event name E):
      1. a stimulus with a NON-matching hash            -> started=0, resumed=0 (no-op).
      2. `StartOnly` with E's hash                      -> started=0 (a CanStartWorkflow=false Event is NOT a
                                                           start trigger, so it starts nothing).
      3. `ResumeOnly` with E's hash                     -> resumed>=1 (matches the waiting bookmark).

    (Whether the matched resume then *executes* is #1014; here we assert the routing/matching contract only.)
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ControlsCommon.ps1"

Write-Host "== Stimulus routing / bookmark matching ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$ev  = Invoke-Step "resolve Event"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }
$E = "route-$(Get-Random -Max 999999)"

# Suspend one instance at a mid-flow Event(E) catch.
$before = New-ActivityNode -NodeId "before" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "before") )
$wait   = New-EventWaitNode -NodeId "wait" -EventVersionId $ev -EventName $E
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($before, $wait))
$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Route-$(Get-Random -Max 9999)" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
Start-Sleep -Milliseconds 800
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
if (-not (Test-ActivitySuspended -Instance $inst -NodeId 'wait')) { Write-Host "FAIL - workflow did not suspend at the Event wait" -ForegroundColor Red; exit 1 }
Write-Host "suspended at Event wait; now probing stimulus routing..."

$pass = 0; $total = 3

# 1. non-matching hash -> no-op
$noMatch = Invoke-Step "non-matching stimulus" { Publish-EventStimulus -Ctx $ctx -EventName "nope-$(Get-Random -Max 999999)" -Mode "StartAndResume" }
if ($noMatch.startedCount -eq 0 -and $noMatch.resumedCount -eq 0) { Write-Host ("  OK  non-match -> started=0 resumed=0") -ForegroundColor Green; $pass++ }
else { Write-Host ("  FAIL non-match -> started={0} resumed={1}" -f $noMatch.startedCount, $noMatch.resumedCount) -ForegroundColor Red }

# 2. StartOnly on E -> starts nothing (E's Event is CanStartWorkflow=false, not a start trigger)
$startOnly = Invoke-Step "StartOnly on E" { Publish-EventStimulus -Ctx $ctx -EventName $E -Mode "StartOnly" }
if ($startOnly.startedCount -eq 0) { Write-Host ("  OK  StartOnly -> started=0 (mid-flow Event is not a start trigger)") -ForegroundColor Green; $pass++ }
else { Write-Host ("  FAIL StartOnly -> started={0} (expected 0)" -f $startOnly.startedCount) -ForegroundColor Red }

# 3. ResumeOnly on E -> matches the waiting bookmark
$resumeOnly = Invoke-Step "ResumeOnly on E" { Publish-EventStimulus -Ctx $ctx -EventName $E -Mode "ResumeOnly" }
if ($resumeOnly.resumedCount -ge 1) { Write-Host ("  OK  ResumeOnly -> resumed={0} (matched the waiting bookmark)" -f $resumeOnly.resumedCount) -ForegroundColor Green; $pass++ }
else { Write-Host ("  FAIL ResumeOnly -> resumed={0} (expected >=1)" -f $resumeOnly.resumedCount) -ForegroundColor Red }

Write-Host ""
if ($pass -eq $total) {
    Write-Host ("SUCCESS - {0}/{1} stimulus-routing cases (no-match no-op; StartOnly starts nothing; ResumeOnly matches the bookmark)" -f $pass, $total) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}/{1} stimulus-routing cases passed" -f $pass, $total) -ForegroundColor Red
    exit 1
}
