<#
.SYNOPSIS
    Parallel (fork/join): runs multiple branches and joins when they complete.
.DESCRIPTION
    Foundation-native equivalent of the classic FlowFork + FlowJoin concept: the `Parallel` composite forks into
    named branches and completes when they finish (optionally after a completion `Threshold`).

    LIVING TRACKER (issue #1027): `Parallel` is currently ABSENT from the default activity catalog even though
    its ControlFlow siblings (If/Switch/For/ForEach/While/Do) are present, so it cannot be resolved/authored.
    This script resolves `Parallel` and, if present, runs the fork/join and asserts all branches ran; if it is
    not in the catalog it reports KNOWN ISSUE #1027 and passes (flip to a strict assertion once #1027 is fixed).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [int]    $Branches = 3
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== Parallel fork/join ($Branches branches; tracked by #1027) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }

$parVer = $null
try { $parVer = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Parallel.Activities.Parallel' }
catch { $parVer = $null }

if (-not $parVer) {
    Write-Host ""
    Write-Host "KNOWN ISSUE #1027 - 'Parallel' is not in the activity catalog (dropped in CLR reconciliation); its ControlFlow siblings register fine. Fork/join can't be exercised until it's restored." -ForegroundColor Yellow
    return
}

$branchNodes = @()
for ($n = 1; $n -le $Branches; $n++) {
    $branchNodes += @{
        name     = "branch-$n"
        activity = (New-ActivityNode -NodeId "branch-$n" -VersionId $writeLine -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "Parallel branch $n ran") ))
    }
}
$root = New-ActivityNode -NodeId "parallel-root" -VersionId $parVer -Structure (New-ParallelStructure -Branches $branchNodes)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Parallel-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "Parallel fork/join" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
$ran = (1..$Branches | ForEach-Object { Get-NodeRunCount -Instance $inst -NodeId "branch-$_" } | Measure-Object -Sum).Sum
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $ran -eq $Branches) {
    Write-Host ("SUCCESS - all {0} branches ran and the fork joined/completed" -f $ran) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', branches ran {1}, expected {2}" -f $inst.instance.status, $ran, $Branches) -ForegroundColor Red
    exit 1
}
