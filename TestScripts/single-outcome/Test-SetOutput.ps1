<#
.SYNOPSIS
    SetOutput intrinsic: a workflow assigns a named output, asserted on the instance's outputs.
.DESCRIPTION
    Foundation-native equivalent of the JTest single-outcome "SetOutput" concept. Classic Elsa's SetOutput is
    an activity; in Foundation it is a node *intrinsic* (`elsa.intrinsic.set-output@1`, kind "setOutput") that
    assigns a workflow output. Asserts the output surfaces on the workflow instance.

    NOTE: the sibling SetVariable intrinsic (`elsa.intrinsic.set@1`) needs a workflow-scoped variable whose
    referenceKey matches the engine's stable-key derivation; that linkage isn't wired here yet (see ../README.md),
    so this script covers SetOutput only.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl    = "http://localhost:5095",
    [string] $Username   = "admin",
    [string] $Password   = "Password123!",
    [string] $OutputName = "Result",
    [string] $Value      = "hello from SetOutput"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== SetOutput intrinsic ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

$root = New-SetOutputNode -NodeId "set-out" -OutputName $OutputName -Value $Value

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "SetOutput-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "SetOutput intrinsic" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

# Output name is surfaced camelCased under instance.outputs; match case-insensitively and read the value preview.
$outProp = $inst.outputs.PSObject.Properties | Where-Object { $_.Name -ieq $OutputName } | Select-Object -First 1
$actual  = if ($outProp) { $outProp.Value.value.preview } else { $null }

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $actual -eq $Value) {
    Write-Host ("SUCCESS - output '{0}' = '{1}'" -f $OutputName, $actual) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', output '{1}' = '{2}' (expected '{3}')" -f $inst.instance.status, $OutputName, $actual, $Value) -ForegroundColor Red
}
