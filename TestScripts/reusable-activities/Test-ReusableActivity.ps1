<#
.SYNOPSIS
    Reusable activity (workflow-as-activity) basics: author + publish a reusable activity, consume it as a
    parent workflow's root, execute, and assert the inlined activity ran.
.DESCRIPTION
    Foundation's "workflows used as activities" = a reusable ACTIVITY DEFINITION authored as an
    `elsa.activity-graph` graph with a boundary contract. This test:
      1. authors + publishes a reusable activity whose graph is Sequence[ WriteLine("<marker>") ];
      2. publishes a parent workflow whose ROOT is that reusable activity (bound by exact activityVersionId);
      3. executes the parent and asserts it completes AND an activity of the reusable's type ran inline.

    Root placement is used because the compiler inlines a reusable boundary as the workflow root or inside a
    Flowchart; nesting a reusable boundary directly in a Sequence structure is covered separately
    (Test-ReusableSequenceNesting.ps1). Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Reusable activity (workflow-as-activity) basics ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$token = Get-Random -Max 999999

# 1. author + publish the reusable activity
$manifest = New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson ("[" + (New-WriteLineNodeJson -NodeId "w" -WriteLineVersionId $wl -Text "REUSABLE activity ran token=$token") + "]")
$ra = Invoke-Step "publish reusable activity" { Publish-ReusableActivity -Ctx $ctx -DisplayName "GreetActivity-$token" -PayloadJson $manifest }
Write-Host ("[reusable] version={0} type={1}" -f $ra.VersionId, $ra.ActivityTypeKey)

# 2. parent workflow whose ROOT is the reusable activity
$root = New-ActivityNode -NodeId "use-reusable" -VersionId $ra.VersionId
$def = Invoke-Step "submit parent"  { Submit-Workflow -Ctx $ctx -Name "ParentReusable-$token" -Description "consumes a reusable activity" -RootActivity $root }
$pub = Invoke-Step "publish parent" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute parent" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$ranInline = Test-ReusableRan -Instance $inst -ActivityTypeKey $ra.ActivityTypeKey
Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $ranInline) {
    Write-Host ("SUCCESS - parent completed and the reusable activity ran inline (type '{0}')" -f $ra.ActivityTypeKey) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', reusable-ran={1}" -f $inst.instance.status, $ranInline) -ForegroundColor Red
    exit 1
}
