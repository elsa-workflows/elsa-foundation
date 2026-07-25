<#
.SYNOPSIS
    A reusable activity nested in a workflow Sequence executes in order with its following sibling.
.DESCRIPTION
    Regression coverage for issues #1007 and #1051. The parent Sequence must resolve the reusable
    boundary by its authored id, the reusable's own Sequence root must retain its compiled structure,
    and the following sibling must run after the reusable completes.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Reusable activity nested in a workflow Sequence (issues #1007/#1051) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$t = Get-Random -Max 999999

$ra = Invoke-Step "publish reusable" { Publish-ReusableActivity -Ctx $ctx -DisplayName "NestProbe-$t" -PayloadJson (New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson ("[" + (New-WriteLineNodeJson -NodeId "c" -WriteLineVersionId $wl -Text "nested reusable ran t=$t") + "]")) }

# Parent workflow: Sequence[ <reusable>, WriteLine ]  -- the reusable is ONE child among others.
$child = New-ActivityNode -NodeId "use-reusable" -VersionId $ra.VersionId
$after = New-ActivityNode -NodeId "after" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "after t=$t") )
$root  = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($child, $after))
$def = Invoke-Step "submit parent"  { Submit-Workflow -Ctx $ctx -Name "NestWf-$t" -RootActivity $root }
$pub = Invoke-Step "publish parent" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute parent" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

# Collect the fault message (if any) from the root activity execution.
$wfId = $inst.instance.workflowExecutionId
$faultMessage = ""
foreach ($a in $inst.activities) {
    $d = Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$wfId/activity-executions/$($a.activityExecutionId)" -WebSession $ctx.Session -UseBasicParsing
    foreach ($inc in $d.incidents) { if ($inc.message) { $faultMessage = $inc.message } }
}

Write-Host ""
$completed = $inst.instance.status -in @('Completed','Finished')
$ranReusable = Test-ReusableRan -Instance $inst -ActivityTypeKey $ra.ActivityTypeKey
$ranAfter = (Get-NodeRunCount -Instance $inst -NodeId "after") -ge 1

if ($completed -and $ranReusable -and $ranAfter) {
    Write-Host "SUCCESS - nested reusable and following Sequence sibling both ran." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', reusable-ran={1}, after-ran={2}, message '{3}'" -f $inst.instance.status, $ranReusable, $ranAfter, $faultMessage) -ForegroundColor Red
    exit 1
}
