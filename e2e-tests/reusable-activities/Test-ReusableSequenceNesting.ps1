<#
.SYNOPSIS
    KNOWN ISSUE #1007 tracker: a reusable activity nested in a workflow Sequence faults at runtime.
.DESCRIPTION
    Consuming a reusable activity as a child inside a workflow's `Sequence` structure publishes successfully but
    faults at execution with "Sequence executable node '...' structure references missing child '...'". Reusable
    activities only compose as the workflow ROOT or inside a Flowchart today (issue #1007).

    This is a living tracker: it reproduces the fault and treats the known signature as an expected
    KNOWN-ISSUE pass (so the suite stays green). If the workflow ever completes, it reports FIXED so the test
    can be converted to a strict "completes + reusable ran" assertion. Any *other* failure is a real MISMATCH.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Reusable activity nested in a workflow Sequence (issue #1007) ==  -> $BaseUrl" -ForegroundColor Cyan
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

if ($completed) {
    Write-Host "FIXED - reusable activity now runs nested in a Sequence (was #1007; blocked by #1051). Convert this tracker to a strict assertion." -ForegroundColor Green
} elseif (Test-Structure1051Fault -Instance $inst) {
    # #1007 (references-missing-child) is fixed; the reusable's own Sequence root now faults with Structure=null (#1051).
    Report-Structure1051
} else {
    Write-Host ("MISMATCH - unexpected outcome: status '{0}', message '{1}'" -f $inst.instance.status, $faultMessage) -ForegroundColor Red
    exit 1
}
