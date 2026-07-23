<#
.SYNOPSIS
    3-layer reusable-activity hierarchy: leaf C <- mid B <- top workflow A, published bottom-up and executed,
    with all three layers observably running.
.DESCRIPTION
    Exercises "3+ layers deep child/parent hierarchy of workflows used as activities" against Foundation's real,
    WORKING composition: a reusable activity consumes another reusable activity by making it the graph ROOT
    (root-wrapping). The publisher records the dependency (B -> C) and inlines the transitive closure, so
    publishing the top consumer pulls in the whole line and every layer executes.

      C (leaf) : reusable activity, graph = Sequence[ WriteLine("C ran") ].
      B (mid)  : reusable activity whose graph ROOT is C  (references C by exact version).
      A (top)  : workflow whose root is B.

    Published bottom-up (C -> B -> A). Asserts A completes AND both the C-type and B-type activities ran inline.

    NOTE on composition: nesting a reusable reference as one child inside a Sequence (so a layer could call its
    child AND do other work) does NOT wire the child — in a workflow it faults (issue #1007), and inside a
    reusable graph the dependency is silently dropped. Root-wrapping is the mechanism that actually composes
    reusable activities today. Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== 3-layer reusable-activity hierarchy (C <- B <- A, root-wrapping) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$t = Get-Random -Max 999999

# Bottom-up publish: C, then B (root-wraps C), then A (root = B).
$C = Invoke-Step "publish leaf C" { Publish-ReusableActivity -Ctx $ctx -DisplayName "LeafC-$t" -PayloadJson (New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson ("[" + (New-WriteLineNodeJson -NodeId "c" -WriteLineVersionId $wl -Text "C ran t=$t") + "]")) }
Write-Host ("[C] version={0}" -f $C.VersionId)

$B = Invoke-Step "publish mid B (root-wraps C)" { Publish-ReusableActivity -Ctx $ctx -DisplayName "MidB-$t" -PayloadJson (New-RootWrapManifestJson -RootVersionId $C.VersionId) }
Write-Host ("[B] version={0} type={1}" -f $B.VersionId, $B.ActivityTypeKey)

# Verify B actually depends on C (authoritative outbound edge) before running.
$bDeps = Invoke-Step "verify B->C dependency" { Get-OutboundDependencyVersionIds -Ctx $ctx -VersionId $B.VersionId }
$bDependsOnC = $bDeps -contains $C.VersionId

$root = New-ActivityNode -NodeId "use-B" -VersionId $B.VersionId
$def = Invoke-Step "submit top A"  { Submit-Workflow -Ctx $ctx -Name "TopA-$t" -Description "3-deep reusable hierarchy" -RootActivity $root }
$pub = Invoke-Step "publish top A" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute top A" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$ranC = Test-ReusableRan -Instance $inst -ActivityTypeKey $C.ActivityTypeKey
$ranB = Test-ReusableRan -Instance $inst -ActivityTypeKey $B.ActivityTypeKey
Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $bDependsOnC -and $ranC -and $ranB) {
    Write-Host "SUCCESS - 3-deep hierarchy published bottom-up; B depends on C; top completed with both B and C running inline." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', B->C dep={1}, C ran={2}, B ran={3}" -f $inst.instance.status, $bDependsOnC, $ranC, $ranB) -ForegroundColor Red
    exit 1
}
