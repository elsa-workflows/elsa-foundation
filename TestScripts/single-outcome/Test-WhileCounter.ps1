<#
.SYNOPSIS
    While loop driven by a JS-incremented counter: body reads and writes a variable from JavaScript (#984 + #977).
.DESCRIPTION
    Foundation-native equivalent of the JTest "While with a loop counter" concept, and the second While blocker
    we originally deferred. Two runtime capabilities have to line up:

      (#984) a JS expression can read a visible variable via `getVariable('x')` — the visible variable frames are
             projected into the isolated Jint engine as a host-pinned read surface; and
      (#977) `While` re-materializes its inputs per pass, so a value the body commits is visible to both the next
             body pass and the loop condition (instead of a frozen first-pass snapshot).

    The workflow: Sequence( While(Condition <- JS `getVariable('counter') < 3`){ Set(counter = JS
    `getVariable('counter') + 1`) } ) with `counter` (Int32) declared at WORKFLOW scope. Asserts the loop runs the
    body exactly 3 times and completes. Workflow scope is the simplest authoring here; container scope also works
    when every reference supplies the declaring Sequence node id (see ../README.md).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [int]    $Limit    = 3
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== While loop with JS counter (#984 + #977) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$sequence = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$while    = Invoke-Step "resolve While"    { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.While.Activities.While' }

# body: counter = getVariable('counter') + 1  (JS value; reads then writes the workflow-scope Int32)
$inc = New-SetVariableNode -NodeId "inc" -VariableKey "counter" -ValueAlias "Int32" `
    -Value @{ value = "getVariable('counter') + 1"; expressionType = "JavaScript" }
# condition: JS reading the workflow-scope counter (#984)
$loop = New-ActivityNode -NodeId "loop" -VersionId $while -Inputs @(
    @{ referenceKey = "Condition"; value = @{ value = "getVariable('counter') < $Limit"; expressionType = "JavaScript" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null }
) -Structure (New-WhileStructure -Body $inc)
$root = New-ActivityNode -NodeId "root" -VersionId $sequence -Structure (New-SequenceStructure -Activities @($loop))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "WhileCounter-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "While driven by JS counter" -RootActivity $root -Variables @( (New-VariableDef -Key "counter" -Alias "Int32" -Default 0) ) }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
$passes = Get-NodeRunCount -Instance $inst -NodeId 'inc'
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $passes -eq $Limit) {
    Write-Host ("SUCCESS - JS counter drove the loop {0} passes and it exited" -f $passes) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', body ran {1} time(s), expected {2} and Completed" -f $inst.instance.status, $passes, $Limit) -ForegroundColor Red
    exit 1
}
