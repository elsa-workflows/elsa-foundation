<#
.SYNOPSIS
    For loop: runs a body a fixed number of times, and verifies EndInclusive (inclusive vs exclusive end bound).
.DESCRIPTION
    Foundation-native equivalent of the JTest single-outcome "For loop" case. A `For` composite runs its body
    node from Start to End by Step; EndInclusive controls whether the End value is a final iteration.
    Asserts the body executed the expected number of times (Start=1,End=3: inclusive=3, exclusive=2).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [int]    $Start    = 1,
    [int]    $End      = 3,
    [int]    $Step     = 1
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== For loop (Start=$Start End=$End Step=$Step) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$forVer    = Invoke-Step "resolve For"       { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.For.Activities.For' }

foreach ($inclusive in @($true, $false)) {
    $expected = [int][Math]::Floor(($End - $Start) / $Step) + $(if ($inclusive) { 1 } else { 0 })
    Write-Host ("`n-- EndInclusive = {0} (expect {1} iterations) --" -f $inclusive, $expected) -ForegroundColor Cyan

    $body = New-ActivityNode -NodeId "loop-body" -VersionId $writeLine -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "For iteration") )
    $root = New-ActivityNode -NodeId "for-root" -VersionId $forVer -Inputs @(
        (New-LiteralInput -ReferenceKey "Start"        -Value $Start),
        (New-LiteralInput -ReferenceKey "End"          -Value $End),
        (New-LiteralInput -ReferenceKey "Step"         -Value $Step),
        (New-LiteralInput -ReferenceKey "EndInclusive" -Value $inclusive)
    ) -Structure (New-ForStructure -Body $body)

    $def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "For-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "For loop" -RootActivity $root }
    $pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
    $run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
    $inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
    $runs = Get-NodeRunCount -Instance $inst -NodeId 'loop-body'
    Show-WorkflowInstance -Instance $inst

    if ($inst.instance.status -in @('Completed','Finished') -and $runs -eq $expected) {
        Write-Host ("  OK - body ran {0} times as expected" -f $runs) -ForegroundColor Green
    } else {
        Write-Host ("  MISMATCH - status '{0}', body ran {1}, expected {2}" -f $inst.instance.status, $runs, $expected) -ForegroundColor Red
    }
}
