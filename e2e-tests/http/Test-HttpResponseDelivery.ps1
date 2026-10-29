<#
.SYNOPSIS
    A resumed HTTP response retains a warning without faulting; an inline response records a server write.
.DESCRIPTION
    Owns a rebuilt Workbench, fresh SQLite databases and a free loopback port. Uses real HTTP ingress and
    the hosted durable timer pump, then restarts its own server to verify exact-invocation evidence survives.
#>
[CmdletBinding()]
param()
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

$contentRoot = New-ElsaContentRoot -Prefix 'elsa-http-delivery'
$baseUrl = Resolve-ElsaServerBaseUrl
$success = $false
try {
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot
    $ctx = Connect-Elsa -BaseUrl $baseUrl
    $sequenceId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $endpointId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint'
    $responseId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse'
    $delayId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Scheduling.Activities.Delay'
    $lateInvocation = $null
    foreach ($ingress in @('published', 'test-run')) {
        foreach ($delayed in @($false, $true)) {
            $path = "$ingress-" + $(if ($delayed) { 'delivery-delayed' } else { 'delivery-inline' })
            $endpoint = New-ActivityNode -NodeId 'endpoint' -VersionId $endpointId -Inputs @(
                (New-LiteralInput -ReferenceKey 'Path' -Value "/$path"),
                @{ referenceKey = 'SupportedMethods'; value = @{ value = '["GET"]'; expressionType = 'Object' } },
                (New-LiteralInput -ReferenceKey 'CanStartWorkflow' -Value 'true'),
                (New-LiteralInput -ReferenceKey 'ResponseMode' -Value 'Sync')
            )
            $write = New-ActivityNode -NodeId 'write' -VersionId $responseId -Inputs @(
                (New-LiteralInput -ReferenceKey 'StatusCode' -Value '200'),
                (New-LiteralInput -ReferenceKey 'Body' -Value 'delivery-proof'),
                (New-LiteralInput -ReferenceKey 'ContentType' -Value 'text/plain')
            )
            $children = @($endpoint)
            if ($delayed) { $children += New-ActivityNode -NodeId 'delay' -VersionId $delayId -Inputs @((New-LiteralInput -ReferenceKey 'Duration' -Value '00:00:02')) }
            $children += $write
            $root = New-ActivityNode -NodeId 'root' -VersionId $sequenceId -Structure (New-SequenceStructure -Activities $children)
            $definition = Submit-Workflow -Ctx $ctx -Name $path -RootActivity $root
            if ($ingress -eq 'published') {
                $published = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
                $requestUrl = "$baseUrl/workflows/http/$path"
            } else {
                $state = @{ variables = @(); inputs = @(); outputs = @(); rootActivity = $root }
                $body = @{ definitionId = $definition.definition.id; snapshotId = "$path-snapshot"; state = $state }
                $options = Invoke-RestMethod "$baseUrl/publishing/workflows/drafts/test-run-options" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 40)
                $option = @($options.options | Where-Object { $_.nodeId -eq 'endpoint' -and $_.isAvailable })[0]
                if (-not $option) { throw 'No HTTP Test Run option was discovered.' }
                $body.start = @{ kind = 'trigger'; optionId = $option.optionId; modeId = 'http-request'; data = @{} }
                $testRun = Invoke-RestMethod "$baseUrl/publishing/workflows/drafts/test-runs" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 40)
                $requestUrl = $baseUrl + $testRun.driverAction.relativeUrl
            }
            $response = Invoke-WebRequest $requestUrl -Method GET -WebSession $ctx.Session
            if ($delayed) {
                if ($response.StatusCode -ne 202) { throw "Delayed request must return 202; got $($response.StatusCode)" }
                $accepted = $response.Content | ConvertFrom-Json
                $executionId = if ($ingress -eq 'published') { $accepted.started[0] } else { $accepted.workflowExecutionId }
            } else {
                if ($response.StatusCode -ne 200 -or $response.Content -ne 'delivery-proof') { throw 'Inline request did not return its authored response.' }
                $artifactId = if ($ingress -eq 'published') { $published.artifactId } else { $testRun.artifactId }
                $executions = Invoke-RestMethod "$baseUrl/runtime/workflows/instances?artifactId=$artifactId" -WebSession $ctx.Session
                $executionId = @($executions)[0].workflowExecutionId
            }
            $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $executionId -TimeoutSeconds 60
            if ($instance.instance.status -ne 'Completed' -or @($instance.incidents).Count -ne 0) { throw 'Delivery evidence changed workflow completion or created an incident.' }
            $writes = @($instance.activities | Where-Object { $_.authoredActivityId -eq 'write' })
            if ($writes.Count -ne 1 -or $writes[0].status -ne 'Completed') { throw 'Expected exactly one completed response invocation.' }
            $inspectionUrl = "$baseUrl/runtime/workflows/instances/$executionId/activity-executions/$($writes[0].activityExecutionId)"
            $inspection = Invoke-RestMethod $inspectionUrl -WebSession $ctx.Session
            $expected = if ($delayed) { 'http.response.undelivered' } else { 'http.response.written' }
            if (@($inspection.observations).Count -ne 1 -or $inspection.observations[0].code -ne $expected) { throw "Expected one retained $expected observation." }
            if ($delayed) { $lateInvocation = @{ Url = $inspectionUrl; Id = $inspection.observations[0].observationId } }
        }
    }
    Restart-OwnedElsaServer
    $ctx = Connect-Elsa -BaseUrl $baseUrl
    $reloaded = Invoke-RestMethod $lateInvocation.Url -WebSession $ctx.Session
    if ($reloaded.status -ne 'Completed' -or @($reloaded.observations).Count -ne 1 -or $reloaded.observations[0].observationId -ne $lateInvocation.Id) { throw 'The exact completed invocation lost its warning after restart.' }
    Write-Host 'SUCCESS - published and Test Run HTTP ingress retain inline write / resumed warning; SQLite restart preserves Completed and the exact warning, with no incidents.' -ForegroundColor Green
    $success = $true
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
