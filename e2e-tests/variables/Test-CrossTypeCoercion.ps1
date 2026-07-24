<#
.SYNOPSIS
    Cross-type coercion: how Variable/JS/Literal bindings materialise into a target type, and where they fault.
.DESCRIPTION
    Asserts the coercion contract observed on current main:
      - a Literal string "100" bound into an Int32 target coerces to 100;
      - a JavaScript expression bound into an Int32 target coerces (6*7 -> 42);
      - an Object value bound into a String sink FAULTS (the materialization boundary - a String input cannot
        be materialised from an Object; same class as the WriteHttpResponse.Body case).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_VarCommon.ps1"

Write-Host "== Cross-type coercion ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }

function Invoke-Wf($vars, $acts) {
    $root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities $acts)
    $def = Submit-Workflow -Ctx $ctx -Name "Coerce-$(Get-Random -Max 9999)" -RootActivity $root -Variables $vars
    $pub = Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
    Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
}

$fail = @()

# A) Literal string "100" -> Int32 target -> coerces to 100
$a = Invoke-Step "literal string -> Int32" { Invoke-Wf @( (New-TypedVariableDef -Key "n" -Alias "Int32" -Default 0) ) @(
    (New-SetVariableNode -NodeId "s" -VariableKey "n" -Value "100" -ValueAlias "Int32"),
    (New-SetOutputNode -NodeId "o" -OutputName "o" -Value @{ value = "n"; expressionType = "Variable" }) ) }
$av = Get-OutputPreview -Instance $a -Name "o"
if ($a.instance.status -in @('Completed','Finished') -and "$av" -eq "100") { Write-Host "  OK  literal '100' -> Int32 coerced to 100" -ForegroundColor Green } else { $fail += "string->Int32 (got $av)" }

# B) JS expression -> Int32 target -> coerces (6*7 -> 42)
$b = Invoke-Step "JS -> Int32" { Invoke-Wf @( (New-TypedVariableDef -Key "n" -Alias "Int32" -Default 0) ) @(
    (New-SetVariableNode -NodeId "s" -VariableKey "n" -Value @{ value = "6*7"; expressionType = "JavaScript" } -ValueAlias "Int32"),
    (New-SetOutputNode -NodeId "o" -OutputName "o" -Value @{ value = "n"; expressionType = "Variable" }) ) }
$bv = Get-OutputPreview -Instance $b -Name "o"
if ($b.instance.status -in @('Completed','Finished') -and "$bv" -eq "42") { Write-Host "  OK  JS 6*7 -> Int32 coerced to 42" -ForegroundColor Green } else { $fail += "JS->Int32 (got $bv)" }

# C) Object value -> String sink -> FAULTS (materialization boundary)
$c = Invoke-Step "Object -> String sink (expect fault)" { Invoke-Wf @( (New-TypedVariableDef -Key "obj" -Alias "Object") ) @(
    (New-SetVariableNode -NodeId "s" -VariableKey "obj" -Value @{ value = "({a:1})"; expressionType = "JavaScript" } -ValueAlias "Object"),
    (New-ActivityNode -NodeId "wl" -VersionId $wl -Inputs @( @{ referenceKey = "text"; value = @{ value = "obj"; expressionType = "Variable" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null } )) ) }
if ($c.instance.status -eq 'Faulted') { Write-Host "  OK  Object -> String sink faulted (coercion boundary)" -ForegroundColor Green } else { $fail += "Object->String should fault (got $($c.instance.status))" }

Write-Host ""
if ($fail.Count -eq 0) {
    Write-Host "SUCCESS - coercion contract holds: string/JS coerce to Int32; Object->String is a fault." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
