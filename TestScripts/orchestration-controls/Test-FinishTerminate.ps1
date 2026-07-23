<#
.SYNOPSIS
    Terminate from within: the Finish intrinsic ends the workflow early; downstream steps do not run.
.DESCRIPTION
    Foundation's in-workflow terminate control is the `Finish` intrinsic (`elsa.intrinsic.finish@1`, input
    `outcome`), which completes the whole workflow immediately. (There is no external "cancel this instance"
    REST endpoint; operator cancel is expected to live in the Alteration API. This covers the in-workflow
    terminate.)

    The workflow: Sequence[ WriteLine("before finish"), Finish(outcome), WriteLine("after finish") ]. Asserts the
    workflow completes, the pre-Finish step ran, and the post-Finish step did NOT run.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Outcome  = "early-exit"
)
. "$PSScriptRoot/_ControlsCommon.ps1"

Write-Host "== Terminate from within (Finish intrinsic) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }

$before = New-ActivityNode -NodeId "before" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "before finish") )
$finish = New-FinishNode -NodeId "finish" -Outcome $Outcome
$after  = New-ActivityNode -NodeId "after" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "after finish (should NOT run)") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($before, $finish, $after))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Finish-$(Get-Random -Max 9999)" -Description "Finish terminate" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$beforeRan = (Get-NodeRunCount -Instance $inst -NodeId 'before') -ge 1
$afterRan  = (Get-NodeRunCount -Instance $inst -NodeId 'after') -ge 1
Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $beforeRan -and -not $afterRan) {
    Write-Host "SUCCESS - Finish terminated the workflow early: pre-Finish ran, post-Finish did not." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', before={1}, after={2} (expected Completed, before ran, after NOT)" -f $inst.instance.status, $beforeRan, $afterRan) -ForegroundColor Red
    exit 1
}
