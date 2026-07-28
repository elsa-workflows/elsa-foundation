<#
.SYNOPSIS
    Workflow inputs/outputs typing: declared typed inputs bound via WorkflowRequest, echoed into typed outputs.
.DESCRIPTION
    Declares typed workflow inputs (a String and an Int32), executes the workflow WITH inputs (the execute body
    carries `inputs`), reads each input through a `WorkflowRequest` expression ({ memberKey: "<input>" }) into a
    SetOutput, and asserts the values surface on the instance outputs with the right shape (string preview vs
    numeric value). Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_VarCommon.ps1"

Write-Host "== Workflow inputs/outputs typing ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }

# Declare typed inputs; echo each into a typed output via a WorkflowRequest binding.
$inputs = @( (New-InputDef -Key "iName" -Alias "String"), (New-InputDef -Key "iCount" -Alias "Int32") )
$activities = @(
    (New-SetOutputNode -NodeId "oName"  -OutputName "oName"  -Value @{ value = @{ memberKey = "iName" };  expressionType = "WorkflowRequest" }),
    (New-SetOutputNode -NodeId "oCount" -OutputName "oCount" -Value @{ value = @{ memberKey = "iCount" }; expressionType = "WorkflowRequest" })
)
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities $activities)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "InOut-$(Get-Random -Max 9999)" -RootActivity $root -Inputs $inputs }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }

# Execute WITH inputs (the execute body carries name -> JSON value).
$body = @{ sourceReferenceId = $pub.sourceReferenceId; inputs = @{ iName = "Ada"; iCount = 7 } } | ConvertTo-Json
$run = Invoke-Step "execute (with inputs)" { Invoke-RestMethod "$BaseUrl/runtime/workflows/executables/$($pub.artifactId)/execute" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$name  = Get-OutputPreview -Instance $inst -Name "oName"
$count = Get-OutputPreview -Instance $inst -Name "oCount"
Write-Host ""
Write-Host ("oName='{0}' oCount='{1}'" -f $name, $count)
$fail = @()
if ($inst.instance.status -notin @('Completed','Finished')) { $fail += "status $($inst.instance.status)" }
if ("$name" -ne "Ada") { $fail += "oName='$name' (expected Ada)" }
if ("$count" -ne "7") { $fail += "oCount='$count' (expected 7)" }

Write-Host ""
if ($fail.Count -eq 0) {
    Write-Host "SUCCESS - typed inputs bound via input.* (WorkflowRequest) surfaced as typed outputs." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
