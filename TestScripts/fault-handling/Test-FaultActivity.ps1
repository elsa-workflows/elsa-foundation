<#
.SYNOPSIS
    Fault activity: faulting an activity ends the workflow Faulted, skips downstream, and records an incident.
.DESCRIPTION
    Uses the dedicated `Fault` activity (`Elsa.Activities.Primitives.Activities.Fault`, input `message`) to fault
    deterministically. The workflow is Sequence[ WriteLine("before"), Fault(message), WriteLine("after") ].
    Asserts: status `Faulted`; the pre-fault step ran; the post-fault step did NOT; exactly one incident is
    recorded (`Blocking` / `WaitForIntervention` — the only resolution today, see issue #1015).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Message  = "deliberate fault"
)
. "$PSScriptRoot/_FaultCommon.ps1"

Write-Host "== Fault activity (fault -> Faulted, downstream skipped, incident recorded) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq   = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl    = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$fault = Invoke-Step "resolve Fault"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Fault' }
$tag = Get-Random -Max 999999

$before = New-ActivityNode -NodeId "before" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "before fault") )
$boom   = New-FaultNode    -NodeId "boom" -FaultVersionId $fault -Message "$Message $tag"
$after  = New-ActivityNode -NodeId "after" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "after fault (should NOT run)") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($before, $boom, $after))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Fault-$tag" -Description "Fault activity" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$beforeRan = (Get-NodeRunCount -Instance $inst -NodeId 'before') -ge 1
$afterRan  = (Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1
$incidents = @(Get-InstanceIncidents -Ctx $ctx -ExecutionId $inst.instance.workflowExecutionId)

$fail = @()
if ($inst.instance.status -ne 'Faulted') { $fail += "status=$($inst.instance.status) (expected Faulted)" }
if (-not $beforeRan) { $fail += "pre-fault step did not run" }
if ($afterRan) { $fail += "post-fault step ran (should have been skipped)" }
if ($incidents.Count -ne 1) { $fail += "incident count=$($incidents.Count) (expected 1)" }

Write-Host ""
if ($fail.Count -eq 0) {
    Write-Host "SUCCESS - Fault ended the workflow Faulted, skipped downstream, recorded 1 incident." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
