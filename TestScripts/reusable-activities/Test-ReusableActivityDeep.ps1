<#
.SYNOPSIS
    3-layer reusable-activity hierarchy: leaf C <- mid B <- top workflow A, published bottom-up and executed.
.DESCRIPTION
    Exercises "3+ layers deep child/parent hierarchy of workflows used as activities" against Foundation's real
    mechanism: reusable ACTIVITY graphs that reference other reusable activities by exact `activityVersionId`.
    The publisher inlines the transitive closure, so publishing the top consumer pulls in the whole line.

      C (leaf)  : reusable activity, graph = Sequence[ WriteLine("C ran") ].
      B (mid)   : reusable activity, graph = Sequence[ <ref to C>, WriteLine("B ran") ]  (references C by version).
      A (top)   : workflow whose ROOT is B.

    Published bottom-up (C -> B -> A). Asserts A executes to completion (the whole inlined chain resolved and
    ran) and B is observably inlined. NOTE: deeply-inlined leaf executions (C, the WriteLines) are collapsed and
    are not individually surfaced in the instance's shallow activity view or the descendants endpoint, so the
    assertion is completion + B-inlined; completion is meaningful because a broken child binding faults instead
    of completing (see Test-ReusableSequenceNesting.ps1). Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== 3-layer reusable-activity hierarchy (C <- B <- A) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$t = Get-Random -Max 999999

# Bottom-up publish: C, then B (references C), then A (root = B).
$mC = New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson ("[" + (New-WriteLineNodeJson -NodeId "c" -WriteLineVersionId $wl -Text "C ran t=$t") + "]")
$C  = Invoke-Step "publish leaf C" { Publish-ReusableActivity -Ctx $ctx -DisplayName "LeafC-$t" -PayloadJson $mC }
Write-Host ("[C] version={0}" -f $C.VersionId)

$actsB = "[" + (New-ReusableRefNodeJson -NodeId "callC" -VersionId $C.VersionId) + "," + (New-WriteLineNodeJson -NodeId "b" -WriteLineVersionId $wl -Text "B ran t=$t") + "]"
$B = Invoke-Step "publish mid B" { Publish-ReusableActivity -Ctx $ctx -DisplayName "MidB-$t" -PayloadJson (New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson $actsB) }
Write-Host ("[B] version={0} type={1}" -f $B.VersionId, $B.ActivityTypeKey)

$root = New-ActivityNode -NodeId "use-B" -VersionId $B.VersionId
$def = Invoke-Step "submit top A"  { Submit-Workflow -Ctx $ctx -Name "TopA-$t" -Description "3-deep reusable hierarchy" -RootActivity $root }
$pub = Invoke-Step "publish top A" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute top A" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$bInlined = Test-ReusableRan -Instance $inst -ActivityTypeKey $B.ActivityTypeKey
Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $bInlined) {
    Write-Host "SUCCESS - 3-deep hierarchy published bottom-up and the top executed to completion (B inlined; C transitively inlined)." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', B-inlined={1}" -f $inst.instance.status, $bInlined) -ForegroundColor Red
    exit 1
}
