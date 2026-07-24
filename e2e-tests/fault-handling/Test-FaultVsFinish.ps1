<#
.SYNOPSIS
    Fault vs Finish: both stop downstream, but Fault ends Faulted (with an incident) and Finish ends Completed.
.DESCRIPTION
    Contrasts the two "stop early" primitives so their different terminal semantics are explicit:
      - Sequence[ WriteLine, Fault,  WriteLine ] -> status Faulted,   downstream skipped, 1 incident.
      - Sequence[ WriteLine, Finish, WriteLine ] -> status Completed, downstream skipped, 0 incidents.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_FaultCommon.ps1"

Write-Host "== Fault vs Finish (terminal-status contrast) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq   = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl    = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$fault = Invoke-Step "resolve Fault"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Fault' }

function Invoke-StopCase {
    param([string] $Kind)  # 'Fault' or 'Finish'
    $tag = Get-Random -Max 999999
    $before = New-ActivityNode -NodeId "before" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "before") )
    $stop = if ($Kind -eq 'Fault') { New-FaultNode -NodeId "stop" -FaultVersionId $fault -Message "stop $tag" } else { New-FinishNode -NodeId "stop" -Outcome "done" }
    $after  = New-ActivityNode -NodeId "after" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "after (skip)") )
    $root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($before, $stop, $after))
    $def = Submit-Workflow -Ctx $ctx -Name "$Kind-$tag" -RootActivity $root
    $pub = Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
    $inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
    [pscustomobject]@{
        Status    = $inst.instance.status
        BeforeRan = (Get-NodeRunCount -Instance $inst -NodeId 'before') -ge 1
        AfterRan  = (Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1
        Incidents = @(Get-InstanceIncidents -Ctx $ctx -ExecutionId $inst.instance.workflowExecutionId).Count
    }
}

$faultR  = Invoke-Step "run Fault case"  { Invoke-StopCase -Kind 'Fault' }
$finishR = Invoke-Step "run Finish case" { Invoke-StopCase -Kind 'Finish' }
Write-Host ("Fault : status={0} before={1} after={2} incidents={3}" -f $faultR.Status, $faultR.BeforeRan, $faultR.AfterRan, $faultR.Incidents)
Write-Host ("Finish: status={0} before={1} after={2} incidents={3}" -f $finishR.Status, $finishR.BeforeRan, $finishR.AfterRan, $finishR.Incidents)

$fail = @()
if ($faultR.Status -ne 'Faulted')  { $fail += "Fault status=$($faultR.Status) (expected Faulted)" }
if (-not $faultR.BeforeRan)         { $fail += "Fault: before did not run" }
if ($faultR.AfterRan)               { $fail += "Fault: after ran (should skip)" }
if ($faultR.Incidents -ne 1)        { $fail += "Fault: incidents=$($faultR.Incidents) (expected 1)" }
if ($finishR.Status -notin @('Completed','Finished')) { $fail += "Finish status=$($finishR.Status) (expected Completed)" }
if (-not $finishR.BeforeRan)        { $fail += "Finish: before did not run" }
if ($finishR.AfterRan)              { $fail += "Finish: after ran (should skip)" }
if ($finishR.Incidents -ne 0)       { $fail += "Finish: incidents=$($finishR.Incidents) (expected 0)" }

Write-Host ""
if ($fail.Count -eq 0) {
    Write-Host "SUCCESS - Fault -> Faulted (1 incident); Finish -> Completed (0 incidents); both skip downstream." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
