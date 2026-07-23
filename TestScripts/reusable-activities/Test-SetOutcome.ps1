<#
.SYNOPSIS
    Set Outcome + branch-only-that-outcome: a Control intrinsic emits a named outcome; a Flowchart runs only the
    matching branch.
.DESCRIPTION
    Foundation's "Set Outcome" is the `Control` intrinsic (`elsa.intrinsic.control@1`, input `outcome` = a literal
    string) which completes the node with that outcome name without terminating the workflow. A `Flowchart` routes
    to the outbound connection whose source `port` equals the emitted outcome. (Note: `Control` is not in the
    design authoring palette, but authoring the node directly over REST works.)

    The workflow: Flowchart{ decide = Control(outcome=<Outcome>) --port:approved--> approvedBranch,
    --port:rejected--> rejectedBranch }. Runs for both outcomes and asserts ONLY the matching branch executes.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Set Outcome (Control intrinsic) + Flowchart branch routing ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$fc = Invoke-Step "resolve Flowchart" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart' }
$wl = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }

function Invoke-OutcomeCase {
    param([string] $Outcome)
    $t = Get-Random -Max 999999
    $decide = @{
        nodeId = "decide"; activityVersionId = "elsa.intrinsic.control@1"; outputs = @()
        intrinsic = @{ kind = "control" }
        inputs = @( @{ referenceKey = "outcome"; value = @{ value = $Outcome; expressionType = "Literal" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null } )
    }
    $approved = New-ActivityNode -NodeId "approved" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "APPROVED branch t=$t") )
    $rejected = New-ActivityNode -NodeId "rejected" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "REJECTED branch t=$t") )
    $structure = @{ kind = "elsa.flowchart.structure"; schemaVersion = "1.0.0"; payload = @{
        startNodeId = "decide"
        activities  = @($decide, $approved, $rejected)
        connections = @(
            @{ source = @{ nodeId = "decide"; port = "approved" }; target = @{ nodeId = "approved"; port = "Done" } },
            @{ source = @{ nodeId = "decide"; port = "rejected" }; target = @{ nodeId = "rejected"; port = "Done" } }
        )
    } }
    $root = New-ActivityNode -NodeId "root" -VersionId $fc -Structure $structure
    $def = Submit-Workflow -Ctx $ctx -Name "Outcome-$Outcome-$t" -RootActivity $root
    $pub = Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
    Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
}

$pass = 0; $total = 2
foreach ($case in @(@{ Outcome = "approved"; Expected = "approved"; NotExpected = "rejected" }, @{ Outcome = "rejected"; Expected = "rejected"; NotExpected = "approved" })) {
    $inst = Invoke-Step "run outcome '$($case.Outcome)'" { Invoke-OutcomeCase -Outcome $case.Outcome }
    $ranExpected    = (Get-NodeRunCount -Instance $inst -NodeId $case.Expected) -ge 1
    $ranNotExpected = (Get-NodeRunCount -Instance $inst -NodeId $case.NotExpected) -ge 1
    if ($inst.instance.status -in @('Completed','Finished') -and $ranExpected -and -not $ranNotExpected) {
        Write-Host ("  OK  outcome '{0}' -> only '{1}' branch ran" -f $case.Outcome, $case.Expected) -ForegroundColor Green; $pass++
    } else {
        Write-Host ("  FAIL outcome '{0}' -> expected only '{1}' (status {2}, expected-ran={3}, other-ran={4})" -f $case.Outcome, $case.Expected, $inst.instance.status, $ranExpected, $ranNotExpected) -ForegroundColor Red
    }
}

Write-Host ""
if ($pass -eq $total) {
    Write-Host ("SUCCESS - {0}/{1} outcome-routing cases (Set Outcome routes only the matching branch)" -f $pass, $total) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}/{1} outcome-routing cases passed" -f $pass, $total) -ForegroundColor Red
    exit 1
}
