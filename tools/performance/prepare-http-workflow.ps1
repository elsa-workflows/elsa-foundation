<#
.SYNOPSIS
    Publishes the deterministic synchronous HTTP workflow used by the performance harness.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://127.0.0.1:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Path = "performance-hello-world",
    [string] $ResponseBody = "Hello World!"
)

. "$PSScriptRoot/../../e2e-tests/_ElsaCommon.ps1"

$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$sequence = Invoke-Step "resolve Sequence" {
    Get-ActivityVersionId -Ctx $ctx -TypeKey "Elsa.Activities.Sequence.Activities.Sequence"
}
$httpEndpoint = Invoke-Step "resolve HttpEndpoint" {
    Get-ActivityVersionId -Ctx $ctx -TypeKey "Elsa.Activities.Http.Activities.HttpEndpoint"
}
$writeResponse = Invoke-Step "resolve WriteHttpResponse" {
    Get-ActivityVersionId -Ctx $ctx -TypeKey "Elsa.Activities.Http.Activities.WriteHttpResponse"
}

$httpNode = New-ActivityNode -NodeId "http-in" -VersionId $httpEndpoint -Inputs @(
    (New-LiteralInput -ReferenceKey "Path" -Value $Path),
    (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $true),
    (New-LiteralInput -ReferenceKey "SupportedMethods" -Value @("GET")),
    (New-LiteralInput -ReferenceKey "ResponseMode" -Value "Sync")
)
$responseNode = New-ActivityNode -NodeId "response" -VersionId $writeResponse -Inputs @(
    (New-LiteralInput -ReferenceKey "StatusCode" -Value 200),
    (New-LiteralInput -ReferenceKey "Body" -Value $ResponseBody),
    (New-LiteralInput -ReferenceKey "ContentType" -Value "text/plain")
)
$root = New-ActivityNode `
    -NodeId "root" `
    -VersionId $sequence `
    -Structure (New-SequenceStructure -Activities @($httpNode, $responseNode))

$definition = Invoke-Step "submit benchmark workflow" {
    Submit-Workflow `
        -Ctx $ctx `
        -Name "HTTP performance benchmark" `
        -Description "Deterministic synchronous HTTP workflow for hosted and local performance evidence." `
        -RootActivity $root
}
$publication = Invoke-Step "publish benchmark workflow" {
    Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
}

$endpoint = "$BaseUrl/workflows/http/$Path"
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
do {
    try {
        $response = Invoke-WebRequest $endpoint -Method Get -UseBasicParsing -TimeoutSec 10
        if ([int]$response.StatusCode -eq 200 -and "$($response.Content)" -eq $ResponseBody) {
            Write-Host "Prepared $endpoint (artifact $($publication.artifactId))."
            exit 0
        }
    }
    catch {
        # Publishing updates the route table asynchronously; retry until the bounded deadline.
    }
    Start-Sleep -Milliseconds 250
} while ([DateTimeOffset]::UtcNow -lt $deadline)

throw "Published workflow endpoint '$endpoint' did not become ready within 30 seconds."
