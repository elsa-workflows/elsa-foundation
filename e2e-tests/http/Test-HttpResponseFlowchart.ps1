<#
.SYNOPSIS
    Import and run the HTTP demo Flowchart, returning its authored response in the same request.
.DESCRIPTION
    Regression for an exported HttpEndpoint -> SetCorrelationId -> WriteHttpResponse workflow that returned
    202 because ResponseMode was omitted, and could not publish with unused required outputs left unbound.
    The corrected fixture explicitly selects Sync and captures only ParsedContent. Activity version IDs
    are resolved from this server's catalog because the export's content-addressed IDs belong to its source
    server. All inputs, output captures, variables and Flowchart connections are otherwise used as exported.

    Owns a rebuilt Workbench on a free loopback port with fresh SQLite databases; build Workbench first.
    Pass the original export as -WorkflowPath to reproduce the failing response assertion.
#>
[CmdletBinding()]
param(
    [string] $WorkflowPath = "$PSScriptRoot/fixtures/test-http.json"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

$workflow = Get-Content -LiteralPath $WorkflowPath -Raw | ConvertFrom-Json
$contentRoot = New-ElsaContentRoot -Prefix 'elsa-http-response-flowchart'
$baseUrl = Resolve-ElsaServerBaseUrl
$success = $false
try {
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot
    $ctx = Connect-Elsa -BaseUrl $baseUrl

    $root = $workflow.state.rootActivity
    $root.activityVersionId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart'
    $activities = $root.structure.payload.activities
    $endpoint = $activities | Where-Object nodeId -eq 'httpendpoint-12550be0'
    $responseNode = $activities | Where-Object nodeId -eq 'writehttpresponse-a34979ee'
    $endpoint.activityVersionId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint'
    $responseNode.activityVersionId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse'

    $definition = Invoke-Step 'submit exported Flowchart' {
        Submit-Workflow -Ctx $ctx -Name 'HTTP response Flowchart regression' -RootActivity $root -Variables $workflow.state.variables
    }
    Invoke-Step 'publish exported Flowchart' {
        Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    } | Out-Null

    $response = Invoke-Step 'POST /demo' {
        Invoke-WebRequest "$baseUrl/workflows/http/demo" -Method POST -Body '{"hello":"world"}' `
            -ContentType 'application/json' -WebSession $ctx.Session -UseBasicParsing
    }
    if ($response.StatusCode -ne 200) {
        throw "Expected HTTP 200, got $($response.StatusCode): $($response.Content)"
    }
    if ($response.Content -ne '{"hey" : "John"}') {
        throw "Expected the authored response body, got: $($response.Content)"
    }
    if ($response.Headers['Content-Type'] -notlike 'application/json*') {
        throw "Expected application/json, got: $($response.Headers['Content-Type'])"
    }

    Write-Host 'SUCCESS - exported Flowchart returned HTTP 200 and its authored JSON body in the same exchange' -ForegroundColor Green
    $success = $true
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
