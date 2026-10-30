<#
.SYNOPSIS
    JSON output capture, variable defaults and shared input conversion through published workflows.
.DESCRIPTION
    Owns a rebuilt Workbench with fresh SQLite databases. The HTTP echo is the reported acceptance
    journey; generic output capture and conversion contracts also have publishing/runtime suite coverage.
#>
[CmdletBinding()]
param()
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

$contentRoot = New-ElsaContentRoot -Prefix 'elsa-json-activity-bindings'
$baseUrl = Resolve-ElsaServerBaseUrl
$success = $false
try {
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot
    $ctx = Connect-Elsa -BaseUrl $baseUrl
    $flowchartVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart'
    $endpointVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint'
    $responseVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse'
    $cases = @(
        @{ Name = 'object'; Capture = $true; Body = '{"Id":9007199254740993,"amount":1234567890.123456789012345}'; Expected = '{"Id":9007199254740993,"amount":1234567890.123456789012345}' },
        @{ Name = 'array'; Capture = $true; Body = '[12345678901234567890,1.0,true,null]'; Expected = '[12345678901234567890,1.0,true,null]' },
        @{ Name = 'boolean'; Capture = $true; Body = 'true'; Expected = 'true' },
        @{ Name = 'text'; Capture = $true; Body = '"hello"'; Expected = 'hello' },
        @{ Name = 'null'; Capture = $true; Body = 'null'; Expected = '' },
        @{ Name = 'default'; Alias = 'System.Text.Json.JsonElement'; Default = '{"Id":9007199254740993}'; Syntax = 'Object'; Expected = '{"Id":9007199254740993}' },
        @{ Name = 'json-array-default'; Alias = 'System.Text.Json.JsonElement'; Kind = 'Array'; Default = '[1,2,{"Id":9007199254740993}]'; Syntax = 'Object'; Expected = '[1,2,{"Id":9007199254740993}]' },
        @{ Name = 'typed-object'; Alias = 'Object'; Default = '{"Name":"Ada"}'; Syntax = 'Object'; Expected = '{"Name":"Ada"}' },
        @{ Name = 'typed-string'; Alias = 'String'; Default = '{text stays text}'; Syntax = 'Literal'; Expected = '{text stays text}' },
        @{ Name = 'typed-array'; Alias = 'Int32'; Kind = 'Array'; Default = '[1,2]'; Syntax = 'Object'; Expected = '[1,2]' }
    )
    foreach ($case in $cases) {
        $workflow = Get-Content -LiteralPath "$PSScriptRoot/fixtures/test-http.json" -Raw | ConvertFrom-Json
        $root = $workflow.state.rootActivity
        $root.activityVersionId = $flowchartVersion
        $endpoint = $root.structure.payload.activities | Where-Object nodeId -eq 'httpendpoint-12550be0'
        $responseNode = $root.structure.payload.activities | Where-Object nodeId -eq 'writehttpresponse-a34979ee'
        $endpoint.activityVersionId = $endpointVersion
        $responseNode.activityVersionId = $responseVersion
        ($endpoint.inputs | Where-Object referenceKey -eq 'Path').value.value = "/json-$($case.Name)"
        $variable = $workflow.state.variables[0]
        $variable.type.alias = if ($case.Alias) { $case.Alias } else { 'System.Text.Json.JsonElement' }
        $variable.type.collectionKind = if ($case.Kind) { $case.Kind } else { 'Single' }
        $variable.storageDriverType = 'elsa.json'
        if (-not $case.Capture) {
            $endpoint.outputs = @()
            $variable.default = @{ value = $case.Default; expressionType = $case.Syntax }
        }
        ($responseNode.inputs | Where-Object referenceKey -eq 'Body').value = @{
            value = @{ referenceKey = $variable.referenceKey; declaringScopeId = 'workflow' }
            expressionType = 'Variable'
        }
        $definition = Submit-Workflow -Ctx $ctx -Name "JSON binding $($case.Name)" -RootActivity $root -Variables $workflow.state.variables
        Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id | Out-Null
        $body = if ($case.Capture) { $case.Body } else { '{}' }
        $response = Invoke-WebRequest "$baseUrl/workflows/http/json-$($case.Name)" -Method POST -Body $body `
            -ContentType 'application/json' -WebSession $ctx.Session -UseBasicParsing
        if ($response.StatusCode -ne 200 -or $response.Content -cne $case.Expected) {
            throw "Case '$($case.Name)': expected HTTP 200 '$($case.Expected)', got HTTP $($response.StatusCode) '$($response.Content)'"
        }
        Write-Host "PASS $($case.Name): HTTP 200, exact body preserved"
    }
    $success = $true
    Write-Host 'SUCCESS - all JSON capture/default/typed-variable bindings passed' -ForegroundColor Green
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
