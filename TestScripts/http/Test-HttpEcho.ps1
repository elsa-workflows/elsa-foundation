<#
.SYNOPSIS
    HttpEndpoint request-data echo: capture request data into workflow variables and echo it in a sync response.
.DESCRIPTION
    Foundation-native equivalent of the JTest "http" echo cases (request-body / route-parameter). These were the
    cases we originally deferred under issue #972: capturing an HttpEndpoint output forced a workflow-scope
    variable, and reading a workflow-scope variable in a later node faulted at runtime. Both halves are fixed on
    current main (workflow-scope reads work; a JS binding can read a variable via `getVariable`, #984), so the
    capture -> read -> respond path now works end to end.

    Authoring notes learned here:
      - `HttpEndpoint` exposes `Request` + `RouteData` (both REQUIRED to bind) and `ParsedContent` (optional).
        Output capture must target a WORKFLOW-SCOPE `Variable` (RuntimeOutputCaptureCompiler), so all three
        variables are declared at workflow scope.
      - `WriteHttpResponse.Body` materializes as `System.String`; a captured Object/JsonElement variable does not
        implicitly convert. Reading it through a JS binding (`JSON.stringify(getVariable('x'))` / member access)
        yields a string, which both echoes the value and satisfies the Body type.

    Two cases:
      body  : POST a JSON body -> capture ParsedContent -> echo it back as JSON.
      route : GET /<base>/{name} -> capture RouteData -> echo "hello <name>".
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== HttpEndpoint request-data echo (#972 / #984) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"          { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$hep = Invoke-Step "resolve HttpEndpoint"      { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint' }
$whr = Invoke-Step "resolve WriteHttpResponse" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse' }

# Builds an echo workflow: HttpEndpoint (capturing all outputs to workflow vars) -> WriteHttpResponse (JS body).
function New-EchoWorkflow {
    param($Path, [string[]] $Methods, $BodyJs, $ContentType)
    $httpNode = New-ActivityNode -NodeId "http-in" -VersionId $hep -Inputs @(
        (New-LiteralInput -ReferenceKey "Path" -Value $Path),
        (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $true),
        (New-LiteralInput -ReferenceKey "SupportedMethods" -Value $Methods),
        (New-LiteralInput -ReferenceKey "ResponseMode" -Value "Sync")
    )
    # Request + RouteData are required outputs; ParsedContent is optional. Capture all into workflow-scope vars.
    $httpNode.outputs = @(
        @{ referenceKey = "Request";       value = @{ value = @{ referenceKey = "req" };     expressionType = "Variable" } },
        @{ referenceKey = "RouteData";     value = @{ value = @{ referenceKey = "route" };   expressionType = "Variable" } },
        @{ referenceKey = "ParsedContent"; value = @{ value = @{ referenceKey = "content" }; expressionType = "Variable" } }
    )
    $respNode = New-ActivityNode -NodeId "resp" -VersionId $whr -Inputs @(
        (New-LiteralInput -ReferenceKey "StatusCode" -Value 200),
        @{ referenceKey = "Body"; value = @{ value = $BodyJs; expressionType = "JavaScript" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null },
        (New-LiteralInput -ReferenceKey "ContentType" -Value $ContentType)
    )
    New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($httpNode, $respNode))
}

$wfVars = @(
    (New-VariableDef -Key "req" -Alias "Object"),
    (New-VariableDef -Key "route" -Alias "Object"),
    (New-VariableDef -Key "content" -Alias "Object")
)

$pass = 0; $total = 2

# ---- Case 1: request-body echo ----
$bodyPath = "echo-body-$(Get-Random -Max 999999)"
$rootBody = New-EchoWorkflow -Path $bodyPath -Methods @("POST") -BodyJs "JSON.stringify(getVariable('content'))" -ContentType "application/json"
$defB = Invoke-Step "submit body"  { Submit-Workflow -Ctx $ctx -Name "EchoBody-$(Get-Random -Max 9999)" -RootActivity $rootBody -Variables $wfVars }
Invoke-Step "publish body" { Publish-WorkflowVersion -Ctx $ctx -VersionId $defB.version.id } | Out-Null
Start-Sleep -Milliseconds 500
$rBody = Invoke-Step "POST body" { Invoke-WebRequest "$BaseUrl/workflows/http/$bodyPath" -Method POST -Body '{"msg":"hello-echo"}' -ContentType "application/json" -WebSession $ctx.Session -UseBasicParsing }
if ($rBody.Content -eq '{"msg":"hello-echo"}') { Write-Host ("  OK  body  -> {0}" -f $rBody.Content) -ForegroundColor Green; $pass++ }
else { Write-Host ("  FAIL body  -> got [{0}]" -f $rBody.Content) -ForegroundColor Red }

# ---- Case 2: route-parameter echo ----
$routeBase = "echo-route-$(Get-Random -Max 999999)"
$rootRoute = New-EchoWorkflow -Path "$routeBase/{name}" -Methods @("GET") -BodyJs "'hello ' + getVariable('route').name" -ContentType "text/plain"
$defR = Invoke-Step "submit route"  { Submit-Workflow -Ctx $ctx -Name "EchoRoute-$(Get-Random -Max 9999)" -RootActivity $rootRoute -Variables $wfVars }
Invoke-Step "publish route" { Publish-WorkflowVersion -Ctx $ctx -VersionId $defR.version.id } | Out-Null
Start-Sleep -Milliseconds 500
$rRoute = Invoke-Step "GET route" { Invoke-WebRequest "$BaseUrl/workflows/http/$routeBase/world" -Method GET -WebSession $ctx.Session -UseBasicParsing }
if ($rRoute.Content -eq 'hello world') { Write-Host ("  OK  route -> {0}" -f $rRoute.Content) -ForegroundColor Green; $pass++ }
else { Write-Host ("  FAIL route -> got [{0}]" -f $rRoute.Content) -ForegroundColor Red }

Write-Host ""
if ($pass -eq $total) {
    Write-Host ("SUCCESS - {0}/{1} echo cases (request-body, route-parameter)" -f $pass, $total) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}/{1} echo cases passed" -f $pass, $total) -ForegroundColor Red
    exit 1
}
