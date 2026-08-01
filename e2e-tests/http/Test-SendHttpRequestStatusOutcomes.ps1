<#
.SYNOPSIS
    SendHttpRequest per-status outcome ports: do the ports the compiler pins from ExpectedStatusCodes
    actually materialise on the PUBLISHED node, and do they route correctly?

.DESCRIPTION
    The [ActivityValueOutcomes] attribute existing is not proof the published node has the ports (#1119).
    The in-process drive proves the activity emits an outcome named after the matched status, and the
    contract snapshot guard proves the design facet advertises the mechanism — neither proves a published
    workflow can connect a branch to port "200" and have the runtime take it.

    This authors a Flowchart whose SendHttpRequest node targets the server's own health endpoint (a stable
    200) with ExpectedStatusCodes authored, wires one branch per port plus the unmatched catch-all, and
    asserts the run took exactly the expected branch:

      - ExpectedStatusCodes = [200, 404], response 200  -> the "200" port
      - ExpectedStatusCodes = [404],      response 200  -> the "Unmatched status code" port

    The second case is the one that matters most: it proves the catch-all is a real connectable port and
    not just a string in an attribute.

.EXAMPLE
    pwsh ./e2e-tests/http/Test-SendHttpRequestStatusOutcomes.ps1
#>
[CmdletBinding()]
param([string] $BaseUrl = "http://localhost:5095")

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== SendHttpRequest per-status outcome ports ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl

$send = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.SendHttpRequest'
$wl = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
$fc = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart'

function Invoke-StatusCase {
    param([int[]] $ExpectedStatusCodes, [string[]] $Ports)

    $t = Get-Random
    $sendNode = New-ActivityNode -NodeId "send" -VersionId $send -Inputs @(
        (New-LiteralInput -ReferenceKey "Url" -Value "$BaseUrl/"),
        (New-LiteralInput -ReferenceKey "Method" -Value "GET"),
        (New-LiteralInput -ReferenceKey "ExpectedStatusCodes" -Value $ExpectedStatusCodes)
    )

    # One WriteLine per wired port, named after the port so the assertion reads off the node id.
    $branches = @()
    $connections = @()
    foreach ($port in $Ports) {
        $nodeId = "branch-" + ($port -replace '[^A-Za-z0-9]', '-')
        $branches += New-ActivityNode -NodeId $nodeId -VersionId $wl -Inputs @(
            (New-LiteralInput -ReferenceKey "text" -Value "took port '$port' t=$t")
        )
        $connections += @{ source = @{ nodeId = "send"; port = $port }; target = @{ nodeId = $nodeId; port = "Done" } }
    }

    $structure = @{
        kind          = "elsa.flowchart.structure"
        schemaVersion = "1.0.0"
        payload       = @{
            startNodeId = "send"
            activities  = @(@($sendNode) + $branches)
            connections = $connections
        }
    }
    $root = New-ActivityNode -NodeId "root" -VersionId $fc -Structure $structure
    $def = Submit-Workflow -Ctx $ctx -Name "SendHttpStatusPorts-$t" -RootActivity $root
    $pub = Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
    return Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
}

$cases = @(
    @{
        Label    = 'matched status code'
        Codes    = @(200, 404)
        Ports    = @('200', '404', 'Unmatched status code')
        Expected = 'branch-200'
        Absent   = @('branch-404', 'branch-Unmatched-status-code')
    },
    @{
        Label    = 'unmatched status code'
        Codes    = @(404)
        Ports    = @('404', 'Unmatched status code')
        Expected = 'branch-Unmatched-status-code'
        Absent   = @('branch-404')
    }
)

$pass = 0
foreach ($case in $cases) {
    $instance = Invoke-Step "publish + run: $($case.Label) (ExpectedStatusCodes = $($case.Codes -join ', '))" {
        Invoke-StatusCase -ExpectedStatusCodes $case.Codes -Ports $case.Ports
    }

    Show-WorkflowInstance -Instance $instance

    $tookExpected = (Get-NodeRunCount -Instance $instance -NodeId $case.Expected) -ge 1
    $tookOther = @($case.Absent | Where-Object { (Get-NodeRunCount -Instance $instance -NodeId $_) -ge 1 })

    if ($tookExpected -and $tookOther.Count -eq 0) {
        Write-Host ("  OK  routed to '{0}'" -f $case.Expected) -ForegroundColor Green
        $pass++
    }
    else {
        Write-Host ("  FAIL  expected '{0}' to run alone; alsoRan=[{1}] expectedRan={2}" -f `
                $case.Expected, ($tookOther -join ', '), $tookExpected) -ForegroundColor Red
    }
}

Write-Host ""
if ($pass -eq $cases.Count) {
    Write-Host "SUCCESS - $pass/$($cases.Count): the per-status ports pinned from ExpectedStatusCodes are connectable on the published node and route correctly." -ForegroundColor Green
}
else {
    Write-Host "FAILED - $pass/$($cases.Count) status-outcome cases passed." -ForegroundColor Red
    exit 1
}
