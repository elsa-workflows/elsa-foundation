<#
.SYNOPSIS
    While loop terminated by its own body: a Set of the condition variable ends the loop (issue #977).
.DESCRIPTION
    Foundation-native equivalent of the JTest "While" concept. The loop condition is a Variable-read of a
    WORKFLOW-SCOPE Boolean (see the scoping rule in ../README.md); the loop body is a Set intrinsic that flips it
    to false. With the #977 fix, While opts into per-pass input re-materialization, so the body's committed Set is
    visible to the condition on the next evaluation and the loop exits after exactly one pass — previously it ran
    to DrainCycleLimitExceededException (64 cycles).

    The workflow: Sequence( While(Condition <- Variable KeepGoing){ Set(KeepGoing=false) } ) with KeepGoing
    declared at workflow scope. Asserts the instance completes and the body ran exactly once. Container scope is
    also valid when every reference supplies the declaring Sequence node id (see ../README.md).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== While loop (body Set terminates, #977) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$sequence = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$while    = Invoke-Step "resolve While"    { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.While.Activities.While' }

$flip = New-SetVariableNode -NodeId "flip" -VariableKey "KeepGoing" -Value $false -ValueAlias "Boolean"
$loop = New-ActivityNode -NodeId "loop" -VersionId $while -Inputs @(
    @{
        referenceKey      = "Condition"
        value             = @{ value = "KeepGoing"; expressionType = "Variable" }
        autoEvaluate      = $null
        evaluatorType     = $null
        storageDriverType = $null
        isSensitive       = $null
    }
) -Structure (New-WhileStructure -Body $flip)
$root = New-ActivityNode -NodeId "root" -VersionId $sequence -Structure (
    New-SequenceStructure -Activities @($loop)
)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "While-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "While terminated by body Set" -RootActivity $root -Variables @( (New-VariableDef -Key "KeepGoing" -Alias "Boolean" -Default $true) ) }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
$passes = Get-NodeRunCount -Instance $inst -NodeId 'flip'
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $passes -eq 1) {
    Write-Host ("SUCCESS - loop exited after one pass (body ran {0} time)" -f $passes) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', body ran {1} time(s), expected 1 and Completed" -f $inst.instance.status, $passes) -ForegroundColor Red
    exit 1
}
