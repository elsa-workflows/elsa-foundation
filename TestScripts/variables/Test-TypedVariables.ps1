<#
.SYNOPSIS
    Typed variables & collection kinds: Int32 / Boolean / Decimal / Object / collection round-trip, plus defaults.
.DESCRIPTION
    Declares workflow-scope variables of several alias types (and one collection), Sets each, reads each back
    through a SetOutput (`Variable` expression), and asserts the value round-trips. Also asserts that an unset
    variable surfaces its declared default. Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_VarCommon.ps1"

Write-Host "== Typed variables & collection kinds ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }

$vars = @(
    (New-TypedVariableDef -Key "vInt"  -Alias "Int32"   -Default 0),
    (New-TypedVariableDef -Key "vBool" -Alias "Boolean" -Default $false),
    (New-TypedVariableDef -Key "vDec"  -Alias "Decimal" -Default 0),
    (New-TypedVariableDef -Key "vObj"  -Alias "Object"),
    (New-TypedVariableDef -Key "vColl" -Alias "Int32" -CollectionKind "Array"),
    (New-TypedVariableDef -Key "vDef"  -Alias "String"  -Default "kept-default")
)

$activities = @(
    (New-SetVariableNode -NodeId "sInt"  -VariableKey "vInt"  -Value 42     -ValueAlias "Int32"),
    (New-SetVariableNode -NodeId "sBool" -VariableKey "vBool" -Value $true  -ValueAlias "Boolean"),
    (New-SetVariableNode -NodeId "sDec"  -VariableKey "vDec"  -Value 3.14   -ValueAlias "Decimal"),
    (New-SetVariableNode -NodeId "sObj"  -VariableKey "vObj"  -Value @{ value = "({x:7})"; expressionType = "JavaScript" } -ValueAlias "Object"),
    (New-SetCollectionNode -NodeId "sColl" -VariableKey "vColl" -Value @{ value = "[1,2,3]"; expressionType = "JavaScript" } -Alias "Int32"),
    # vDef is deliberately NOT set -> should surface its default
    (New-SetOutputNode -NodeId "oInt"  -OutputName "oInt"  -Value @{ value = "vInt";  expressionType = "Variable" }),
    (New-SetOutputNode -NodeId "oBool" -OutputName "oBool" -Value @{ value = "vBool"; expressionType = "Variable" }),
    (New-SetOutputNode -NodeId "oDec"  -OutputName "oDec"  -Value @{ value = "vDec";  expressionType = "Variable" }),
    (New-SetOutputNode -NodeId "oObj"  -OutputName "oObj"  -Value @{ value = "vObj";  expressionType = "Variable" }),
    (New-SetOutputNode -NodeId "oColl" -OutputName "oColl" -Value @{ value = "vColl"; expressionType = "Variable" }),
    (New-SetOutputNode -NodeId "oDef"  -OutputName "oDef"  -Value @{ value = "vDef";  expressionType = "Variable" })
)
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities $activities)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "TypedVars-$(Get-Random -Max 9999)" -RootActivity $root -Variables $vars }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$checks = @(
    @{ name = "oInt";  got = (Get-OutputPreview -Instance $inst -Name "oInt");  want = "42";   test = { param($v) $v -eq "42" } },
    @{ name = "oBool"; got = (Get-OutputPreview -Instance $inst -Name "oBool"); want = "true"; test = { param($v) "$v" -match '^(true|True)$' } },
    @{ name = "oDec";  got = (Get-OutputPreview -Instance $inst -Name "oDec");  want = "3.14"; test = { param($v) "$v" -match '^3\.14' } },
    @{ name = "oObj";  got = (Get-OutputPreview -Instance $inst -Name "oObj");  want = "x=7";  test = { param($v) "$v" -match '7' } },
    @{ name = "oColl"; got = (Get-OutputPreview -Instance $inst -Name "oColl"); want = "[1,2,3]"; test = { param($v) "$v" -match '1' -and "$v" -match '3' } },
    @{ name = "oDef (default)"; got = (Get-OutputPreview -Instance $inst -Name "oDef"); want = "kept-default"; test = { param($v) $v -eq "kept-default" } }
)
Write-Host ""
$fail = @()
foreach ($c in $checks) {
    if (& $c.test $c.got) { Write-Host ("  OK  {0} -> {1}" -f $c.name, $c.got) -ForegroundColor Green }
    else { Write-Host ("  FAIL {0} -> got [{1}] want ~[{2}]" -f $c.name, $c.got, $c.want) -ForegroundColor Red; $fail += $c.name }
}
Write-Host ""
if ($fail.Count -eq 0 -and $inst.instance.status -in @('Completed','Finished')) {
    Write-Host "SUCCESS - all typed variables (incl. collection) round-tripped and the default surfaced." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', failing: {1}" -f $inst.instance.status, ($fail -join ', ')) -ForegroundColor Red
    exit 1
}
