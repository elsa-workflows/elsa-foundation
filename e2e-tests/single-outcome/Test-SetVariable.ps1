<#
.SYNOPSIS
    SetVariable intrinsic: assign a container-scoped variable, then read it back.
.DESCRIPTION
    Foundation-native equivalent of the JTest single-outcome "SetVariable" concept. Classic Elsa's SetVariable
    is an activity; in Foundation it is a node intrinsic (`elsa.intrinsic.set@1`, kind "set").

    KEY SCOPING RULE: a `Set` intrinsic's target must resolve to a variable that is VISIBLE from the node's
    scope. Since the #972 two-guard validation landed (`IntrinsicVariableTargetValidator` +
    `ExecutableNodeCompiler.ValidateIntrinsicVariableTargets`), the reference's `DeclaringScopeId` must match a
    visible declaring scope. The clean, end-to-end-working shape is a WORKFLOW-SCOPE variable (declared in
    `state.Variables` via `Submit-Workflow -Variables`) referenced with no `declaringScopeId`. Declaring the
    variable only on the enclosing container (the Sequence's `variables`) now FAILS publish validation, and
    forcing a container `declaringScopeId` past the validator faults at runtime (see ../README.md).

    The workflow: Sequence[ Set(Message=<value>) -> SetOutput(Echo = Variable Message) ], then asserts the echoed
    output equals the value, proving the Set wrote the variable and a Variable-expression read it back.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Value    = "hello from a variable"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== SetVariable intrinsic ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$sequence = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }

$setVar  = New-SetVariableNode -NodeId "set-var" -VariableKey "Message" -Value $Value
$readVar = New-SetOutputNode   -NodeId "read-var" -OutputName "Echo" -Value @{ value = "Message"; expressionType = "Variable" }
$root    = New-ActivityNode -NodeId "root" -VersionId $sequence -Structure (
    New-SequenceStructure -Activities @($setVar, $readVar)
)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "SetVar-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "SetVariable intrinsic" -RootActivity $root -Variables @( (New-VariableDef -Key "Message") ) }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$echo = ($inst.outputs.PSObject.Properties | Where-Object { $_.Name -ieq 'Echo' } | Select-Object -First 1).Value.value.preview
Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $echo -eq $Value) {
    Write-Host ("SUCCESS - variable set and read back: '{0}'" -f $echo) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', echoed '{1}' (expected '{2}')" -f $inst.instance.status, $echo, $Value) -ForegroundColor Red
}
