<#
.SYNOPSIS
    Real HTTP/persistence proof for an undefined JavaScript activity input and authorized incident-health paging.
.DESCRIPTION
    Creates two incident-bearing runs and a healthy control on an exclusively owned fresh host. Verifies the
    exact activity/input association after rereading the persisted run, honest failed input evidence, unchanged
    WaitForIntervention lifecycle, and health predicates applied before counting/cursor paging.
    Requires an exclusively owned rebuilt normally composed Workbench with a fresh database. A time window
    scopes the paging assertions to this script; do not run this matrix on a shared or production host.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../persistence-querying/_PersistenceCommon.ps1"

$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
$runLabel = "incident-qa-$([guid]::NewGuid().ToString('N'))"
$from = [DateTimeOffset]::UtcNow.ToString("O")

function Assert-IncidentQA {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "Incident troubleshooting: $Message" }
}

function Start-IncidentQARun {
    param([bool] $FailInput)
    $input = if ($FailInput) {
        @{ referenceKey = 'text'; value = @{ value = 'qaMissingVariable'; expressionType = 'JavaScript' } }
    } else {
        New-LiteralInput -ReferenceKey 'text' -Value 'Healthy incident QA control'
    }
    $writer = New-ActivityNode -NodeId 'writer' -VersionId $wl -Inputs @($input)
    $def = Submit-Workflow -Ctx $ctx -Name "$runLabel-$FailInput" -RootActivity $writer
    $pub = Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
    Assert-IncidentQA (-not [string]::IsNullOrWhiteSpace($run.workflowExecutionId)) 'dispatch returned no execution identity'
    Write-Host ("dispatch: execution={0}, status={1}, reason={2}" -f $run.workflowExecutionId, $run.commandDispatchStatus, $run.reason)
    $expectedDispatch = if ($FailInput) { 'AcceptedButFaulted' } else { 'Accepted' }
    Assert-IncidentQA ($run.commandDispatchStatus -eq $expectedDispatch) "unexpected dispatch outcome: $($run.commandDispatchStatus)"
    $deadline = (Get-Date).AddSeconds(30)
    $instance = $null
    do {
        # Accepted dispatch can precede the first durable instance row. Only that transient 404 is retried;
        # authentication, contract and server failures remain test failures.
        try {
            $instance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
        } catch {
            if ([int]$_.Exception.Response.StatusCode -ne 404) { throw }
        }
        if ($null -ne $instance -and (@($instance.incidents).Count -gt 0 -or $instance.instance.status -eq 'Completed')) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Assert-IncidentQA ($null -ne $instance) 'dispatched instance was not materialized before the polling deadline'
    return $instance
}

$failed = @(Start-IncidentQARun -FailInput $true; Start-IncidentQARun -FailInput $true)
$healthy = Start-IncidentQARun -FailInput $false
Assert-IncidentQA ($healthy.instance.status -eq 'Completed' -and @($healthy.incidents).Count -eq 0) 'healthy control did not complete without incidents'

foreach ($run in $failed) {
    # Fetch again: the assertion concerns stored evidence, not just the dispatch result.
    $stored = Get-WorkflowInstance -Ctx $ctx -ExecutionId $run.instance.workflowExecutionId
    $writer = @($stored.activities | Where-Object executableNodeId -eq 'writer')
    $incident = @($stored.incidents | Where-Object failureType -eq 'SchedulerWorkPoisoned')
    Assert-IncidentQA ($writer.Count -eq 1 -and $incident.Count -eq 1) 'expected one writer occurrence and one poison incident'
    Assert-IncidentQA ($stored.instance.status -eq 'Running' -and $writer[0].status -eq 'Scheduled') 'intervention lifecycle changed'
    Assert-IncidentQA ($incident[0].status -eq 'Blocking' -and $incident[0].resolutionOutcome.actionKind -eq 'WaitForIntervention') 'blocking intervention policy changed'
    Assert-IncidentQA ($incident[0].activityExecutionId -eq $writer[0].activityExecutionId -and $incident[0].executableNodeId -eq 'writer') 'incident lost its exact activity/node association'
    Assert-IncidentQA ($incident[0].metadata.'runtime.inputKey' -eq 'text') 'incident lost the failing input key'
    $detail = Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$($stored.instance.workflowExecutionId)/activity-executions/$($writer[0].activityExecutionId)" -WebSession $ctx.Session
    $failure = @($detail.valueSnapshots | Where-Object { $_.inputKey -eq 'text' -and $_.failure.code -eq 'ExpressionEvaluationFailed' })
    Assert-IncidentQA ($failure.Count -eq 1 -and $failure[0].failure.incidentId -eq $incident[0].incidentId) 'failed evaluation evidence did not reference its incident'
    Assert-IncidentQA ($failure[0].captureState -eq 'captureFailed') 'failed evaluation was presented as a successful or absent capture'
    Assert-IncidentQA ($null -eq $failure[0].payload -and $null -eq $failure[0].snapshot) 'failed evaluation fabricated an input value'
}

foreach ($health in @('active', 'blocking')) {
    $query = @{ from = $from; incidentHealth = $health; take = 1 }
    $first = Get-InstancesPage -Ctx $ctx -Query $query
    Assert-IncidentQA ($first.totalCount -eq 2 -and @($first.items).Count -eq 1 -and $first.hasNext) "$health filter did not count/page the whole matching set"
    $query.cursor = $first.nextCursor
    $second = Get-InstancesPage -Ctx $ctx -Query $query
    Assert-IncidentQA (@($second.items).Count -eq 1 -and -not $second.hasNext -and $second.items[0].workflowExecutionId -ne $first.items[0].workflowExecutionId) "$health cursor duplicated or omitted a matching run"
    foreach ($item in @($first.items) + @($second.items)) {
        Assert-IncidentQA ($item.incidentCount -eq 1 -and $item.activeIncidentCount -eq 1 -and $item.blockingIncidentCount -eq 1) 'historical/current health counts disagree'
    }
}
$none = Get-InstancesPage -Ctx $ctx -Query @{ from = $from; incidentHealth = 'none'; take = 1 }
Assert-IncidentQA ($none.totalCount -eq 1 -and $none.items[0].workflowExecutionId -eq $healthy.instance.workflowExecutionId) 'healthy filter included incident-bearing runs or excluded the control'
Write-Host "SUCCESS - persisted causal input failure, preserved intervention policy and whole-set incident-health paging ($runLabel)" -ForegroundColor Green
