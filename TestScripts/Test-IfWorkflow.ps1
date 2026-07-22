<#
.SYNOPSIS
    Branching workflow: an If (decision) composite that runs its Then or Else branch by a boolean condition.
.DESCRIPTION
    Builds an If root with a WriteLine in each branch, publishes and runs it, and asserts the correct branch
    executed. By default it runs BOTH conditions (true and false) to prove each path. The condition is a
    literal here; in a real workflow it would typically be an expression over inputs/variables.
.EXAMPLE
    pwsh ./TestScripts/Test-IfWorkflow.ps1
    pwsh ./TestScripts/Test-IfWorkflow.ps1 -Condition true
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [ValidateSet("both","true","false")][string] $Condition = "both"
)
. "$PSScriptRoot/_ElsaCommon.ps1"

Write-Host "== If / decision workflow ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$ifVer     = Invoke-Step "resolve If"        { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.If.Activities.If' }

$conditions = if ($Condition -eq "both") { @($true, $false) } else { @([bool]::Parse($Condition)) }

foreach ($cond in $conditions) {
    Write-Host ("`n-- Condition = {0} --" -f $cond) -ForegroundColor Cyan
    $then = New-ActivityNode -NodeId "then-branch" -VersionId $writeLine -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "If: THEN branch ran (condition true)") )
    $else = New-ActivityNode -NodeId "else-branch" -VersionId $writeLine -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "If: ELSE branch ran (condition false)") )
    $root = New-ActivityNode -NodeId "if-root" -VersionId $ifVer `
        -Inputs @( (New-LiteralInput -ReferenceKey "Condition" -Value $cond) ) `
        -Structure (New-IfStructure -Then $then -Else $else)

    $def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "If-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "If decision" -RootActivity $root }
    $pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
    $run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
    $inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
    Show-WorkflowInstance -Instance $inst

    $ran = ($inst.activities | Where-Object { $_.executableNodeId -in @('then-branch','else-branch') }).executableNodeId
    $expected = if ($cond) { 'then-branch' } else { 'else-branch' }
    if ($ran -eq $expected) { Write-Host ("  OK - took '{0}' as expected" -f $ran) -ForegroundColor Green }
    else { Write-Host ("  MISMATCH - took '{0}', expected '{1}'" -f $ran, $expected) -ForegroundColor Red }
}
