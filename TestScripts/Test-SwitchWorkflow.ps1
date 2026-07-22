<#
.SYNOPSIS
    Branching workflow: a Switch composite that runs the branch of the first matching case (or default).
.DESCRIPTION
    Builds a Switch root with cases a/b/c (each a WriteLine) plus a default, publishes and runs it, and
    asserts the branch matching -Value executed (or default when nothing matches). The switch value is a
    literal here; in a real workflow it would typically be an expression over inputs/variables.
.EXAMPLE
    pwsh ./TestScripts/Test-SwitchWorkflow.ps1 -Value b
    pwsh ./TestScripts/Test-SwitchWorkflow.ps1 -Value zzz   # no match -> default
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Value    = "b"
)
. "$PSScriptRoot/_ElsaCommon.ps1"

Write-Host "== Switch workflow (value = '$Value') ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$swVer     = Invoke-Step "resolve Switch"    { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Switch.Activities.Switch' }

function CaseNode($m) { New-ActivityNode -NodeId "case-$m" -VersionId $writeLine -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "Switch: matched case '$m'") ) }
$cases = @(
    @{ match = "a"; activity = (CaseNode "a") },
    @{ match = "b"; activity = (CaseNode "b") },
    @{ match = "c"; activity = (CaseNode "c") }
)
$default = New-ActivityNode -NodeId "case-default" -VersionId $writeLine -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "Switch: no case matched -> default") )
$root = New-ActivityNode -NodeId "switch-root" -VersionId $swVer `
    -Inputs @( (New-LiteralInput -ReferenceKey "Value" -Value $Value) ) `
    -Structure (New-SwitchStructure -Cases $cases -Default $default)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Switch-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "Switch" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$ran = ($inst.activities | Where-Object { $_.executableNodeId -like 'case-*' }).executableNodeId
$expected = if ($Value -in @('a','b','c')) { "case-$Value" } else { 'case-default' }
Write-Host ""
if ($ran -eq $expected) { Write-Host ("SUCCESS - took '{0}' as expected" -f $ran) -ForegroundColor Green }
else { Write-Host ("MISMATCH - took '{0}', expected '{1}'" -f ($ran -join ','), $expected) -ForegroundColor Red }
