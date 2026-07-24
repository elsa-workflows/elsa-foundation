<#
.SYNOPSIS
    ForEach loop: runs a body once per item in a collection.
.DESCRIPTION
    Foundation-native equivalent of the JTest single-outcome "ForEach" case. A `ForEach` composite iterates
    its `Collection` input, running the body node per item. Asserts the body executed once per item.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string]   $BaseUrl  = "http://localhost:5095",
    [string]   $Username = "admin",
    [string]   $Password = "Password123!",
    [string[]] $Items    = @("item1", "item2", "item3")
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== ForEach loop ($($Items.Count) items) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$feVer     = Invoke-Step "resolve ForEach"   { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.ForEach.Activities.ForEach' }

$body = New-ActivityNode -NodeId "fe-body" -VersionId $writeLine -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "ForEach item") )
$root = New-ActivityNode -NodeId "foreach-root" -VersionId $feVer `
    -Inputs @( (New-LiteralInput -ReferenceKey "Collection" -Value $Items) ) `
    -Structure (New-ForEachStructure -Body $body)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "ForEach-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "ForEach loop" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
$runs = Get-NodeRunCount -Instance $inst -NodeId 'fe-body'
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $runs -eq $Items.Count) {
    Write-Host ("SUCCESS - body ran {0} times, one per item" -f $runs) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', body ran {1}, expected {2}" -f $inst.instance.status, $runs, $Items.Count) -ForegroundColor Red
}
