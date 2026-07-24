<#
.SYNOPSIS
    HTTP endpoint accepting multiple methods: each supported verb triggers the workflow and gets a sync response.
.DESCRIPTION
    Foundation-native equivalent of the JTest http "multiple-supported-methods" case. One HttpEndpoint declares
    SupportedMethods = [GET, POST, PUT, DELETE] and returns a fixed body synchronously. The script fires each
    verb and asserts a 2xx with the expected body. No request-data capture is involved, so this reproduces cleanly.

    NOTE: the other JTest http cases (query-parameter, route-parameter, request-body, headers) echo request data
    back in the response, which requires capturing an HttpEndpoint output into a workflow-scope variable and then
    reading it in WriteHttpResponse. That read fails at runtime ("Variable '...' in scope 'workflow' ... is
    unavailable") - the same variable-scope inconsistency tracked in issue #972 (output-capture forces
    workflow-scope, but a workflow-scope variable isn't materialized into the container frame a later node reads).
    So those echo cases are currently blocked here; see ../README.md.
    Requires the server running from source.
#>
[CmdletBinding()]
param(
    [string]   $BaseUrl  = "http://localhost:5095",
    [string]   $Username = "admin",
    [string]   $Password = "Password123!",
    [string[]] $Methods  = @("GET", "POST", "PUT", "DELETE")
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== HTTP multiple supported methods ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"          { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$hep = Invoke-Step "resolve HttpEndpoint"      { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint' }
$whr = Invoke-Step "resolve WriteHttpResponse" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse' }

$path = "methods-$(Get-Random -Maximum 999999)"
$httpNode = New-ActivityNode -NodeId "http-in" -VersionId $hep -Inputs @(
    (New-LiteralInput -ReferenceKey "Path" -Value $path),
    (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $true),
    (New-LiteralInput -ReferenceKey "SupportedMethods" -Value $Methods),
    (New-LiteralInput -ReferenceKey "ResponseMode" -Value "Sync")
)
$respNode = New-ActivityNode -NodeId "resp" -VersionId $whr -Inputs @(
    (New-LiteralInput -ReferenceKey "StatusCode" -Value 200),
    (New-LiteralInput -ReferenceKey "Body" -Value "method-ok"),
    (New-LiteralInput -ReferenceKey "ContentType" -Value "text/plain")
)
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($httpNode, $respNode))
$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "HttpMethods-$(Get-Random -Max 9999)" -Description "multi-method endpoint" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
Write-Host ("[publish] endpoint = /workflows/http/$path  (methods: $($Methods -join ', '))")
Start-Sleep -Seconds 1

$pass = 0
foreach ($m in $Methods) {
    try {
        $r = Invoke-WebRequest "$BaseUrl/workflows/http/$path" -Method $m -WebSession $ctx.Session -UseBasicParsing
        $code = [int]$r.StatusCode; $body = "$($r.Content)"
    } catch { $resp = $_.Exception.Response; $code = if ($resp) { [int]$resp.StatusCode } else { 0 }; $body = "" }
    # HEAD returns no body; others should echo "method-ok". Accept any 2xx.
    if ($code -ge 200 -and $code -lt 300 -and ($m -eq "HEAD" -or $body -eq "method-ok")) {
        Write-Host ("  OK  {0,-6} -> HTTP {1}" -f $m, $code) -ForegroundColor Green; $pass++
    } else {
        Write-Host ("  FAIL {0,-6} -> HTTP {1} body=[{2}]" -f $m, $code, $body) -ForegroundColor Red
    }
}
Write-Host ""
Write-Host ("{0}/{1} methods accepted and responded" -f $pass, $Methods.Count) -ForegroundColor $(if ($pass -eq $Methods.Count) { "Green" } else { "Yellow" })
