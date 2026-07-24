<#
.SYNOPSIS
    Fault propagation: a fault inside a nested container propagates up and faults the whole workflow.
.DESCRIPTION
    Workflow: Sequence[ WriteLine("outer-before"), Sequence[ WriteLine("inner-before"), Fault, WriteLine("inner-after") ], WriteLine("outer-after") ].
    Asserts: status `Faulted`; both `outer-before` and `inner-before` ran; neither `inner-after` nor
    `outer-after` ran (an uncaught fault in the inner container aborts the whole run - there is no catch/continue).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_FaultCommon.ps1"

Write-Host "== Nested fault propagation ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq   = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl    = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$fault = Invoke-Step "resolve Fault"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Fault' }
$tag = Get-Random -Max 999999

$innerBefore = New-ActivityNode -NodeId "inner-before" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "inner-before") )
$boom        = New-FaultNode    -NodeId "boom" -FaultVersionId $fault -Message "nested fault $tag"
$innerAfter  = New-ActivityNode -NodeId "inner-after" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "inner-after (skip)") )
$inner = New-ActivityNode -NodeId "inner" -VersionId $seq -Structure (New-SequenceStructure -Activities @($innerBefore, $boom, $innerAfter))

$outerBefore = New-ActivityNode -NodeId "outer-before" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "outer-before") )
$outerAfter  = New-ActivityNode -NodeId "outer-after" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "outer-after (skip)") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($outerBefore, $inner, $outerAfter))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "NestedFault-$tag" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$fail = @()
if ($inst.instance.status -ne 'Faulted') { $fail += "status=$($inst.instance.status) (expected Faulted)" }
if ((Get-NodeRunCount -Instance $inst -NodeId 'outer-before') -lt 1) { $fail += "outer-before did not run" }
if ((Get-NodeRunCount -Instance $inst -NodeId 'inner-before') -lt 1) { $fail += "inner-before did not run" }
if ((Get-NodeRunCount -Instance $inst -NodeId 'inner-after') -ge 1) { $fail += "inner-after ran (should be skipped)" }
if ((Get-NodeRunCount -Instance $inst -NodeId 'outer-after') -ge 1) { $fail += "outer-after ran (should be skipped)" }

Write-Host ""
if ($fail.Count -eq 0) {
    Write-Host "SUCCESS - fault in the inner container propagated to the workflow; everything after the fault was skipped." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
