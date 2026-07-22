<#
.SYNOPSIS
    HTTP-triggered workflow: an HttpEndpoint start-trigger that runs a WriteLine when its URL is hit.
.DESCRIPTION
    Builds a Sequence root of [HttpEndpoint -> WriteLine], publishes it (which registers the HTTP route),
    then triggers it with a real GET request. Finally locates the resulting instance and prints it.

    Inbound workflow HTTP endpoints are served under the base path '/workflows/http', so an endpoint
    authored with Path 'hello' is reachable at {BaseUrl}/workflows/http/hello.

    Only Path and CanStartWorkflow are required (SupportedMethods is authored here just to honor -Method;
    it defaults to ["GET"]). The other endpoint options (Policy, RequestTimeout, RequestSizeLimit) are
    left unauthored on purpose - this also regression-tests the HttpEndpoint preflight fix, which used to
    500 on unauthored nullable options (they compile to an omitted literal that was mis-read as non-literal).
.EXAMPLE
    pwsh ./TestScripts/Test-HttpWorkflow.ps1
    pwsh ./TestScripts/Test-HttpWorkflow.ps1 -Method POST
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [ValidateSet("GET","POST","PUT","DELETE")][string] $Method = "GET",
    [string] $Message  = "HTTP-triggered workflow ran!"
)
. "$PSScriptRoot/_ElsaCommon.ps1"

Write-Host "== HTTP-triggered workflow ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

$writeLine = Invoke-Step "resolve WriteLine"   { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$sequence  = Invoke-Step "resolve Sequence"    { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$httpEp    = Invoke-Step "resolve HttpEndpoint"{ Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint' }

$path = "hello-$(Get-Random -Maximum 999999)"

# HttpEndpoint start trigger. Path + CanStartWorkflow are required; SupportedMethods is a JSON array
# (authored so -Method is honored). Other options are intentionally left unauthored (they default).
$httpNode = New-ActivityNode -NodeId "http-in" -VersionId $httpEp -Inputs @(
    (New-LiteralInput -ReferenceKey "Path"             -Value $path),
    (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $true),
    (New-LiteralInput -ReferenceKey "SupportedMethods" -Value @($Method))
)
$writeNode = New-ActivityNode -NodeId "after-http" -VersionId $writeLine -Inputs @(
    (New-LiteralInput -ReferenceKey "text" -Value $Message)
)
$root = New-ActivityNode -NodeId "sequence-root" -VersionId $sequence -Structure (New-SequenceStructure -Activities @($httpNode, $writeNode))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "HttpTrigger-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "HTTP-triggered workflow" -RootActivity $root }
Write-Host ("[submit]  definition={0} version={1}" -f $def.definition.id, $def.version.id)

$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
Write-Host ("[publish] artifact={0}" -f $pub.artifactId)

$triggerUrl = "$BaseUrl/workflows/http/$path"
Write-Host ("[trigger] {0} {1}" -f $Method, $triggerUrl)
Start-Sleep -Seconds 1   # let the route table pick up the new trigger
# Async HttpEndpoint replies 202 with body { started: [executionId...], resumed: [...] }.
# We read the started id straight from that body, avoiding the instances-LIST endpoint, which is
# broken in this build (GET /runtime/workflows/instances 500s: its per-instance incident Count query
# 'list-by-workflow-execution' does not declare a Count operation). GetInstance (by id) works fine.
$trigger = Invoke-Step "trigger via HTTP" {
    $resp = Invoke-WebRequest $triggerUrl -Method $Method -WebSession $ctx.Session -UseBasicParsing
    [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = "$($resp.Content)" }
}
Write-Host ("[trigger] HTTP {0}; body={1}" -f $trigger.Status, $trigger.Body)

$execId = $null
try { $execId = ($trigger.Body | ConvertFrom-Json).started | Select-Object -First 1 } catch {}
if (-not $execId) {
    Write-Host "Trigger returned no 'started' execution id; the HTTP trigger may not have started a run." -ForegroundColor Yellow
    exit 1
}
Write-Host ("[observe] triggered execution={0}" -f $execId)
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $execId
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished')) {
    Write-Host "SUCCESS - HTTP request triggered the workflow. SERVER console should show:" -ForegroundColor Green
    Write-Host "  $Message"
} else {
    Write-Host ("FINISHED with status '{0}' (see incidents above)." -f $inst.instance.status) -ForegroundColor Yellow
}
