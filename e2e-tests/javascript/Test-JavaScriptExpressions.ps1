<#
.SYNOPSIS
    JavaScript expression support: pure-ECMAScript features evaluated in a workflow and returned over HTTP.
.DESCRIPTION
    Foundation-native coverage of the JTest javascript suites (array/object/json/modern-JS). Each case is a
    JS expression bound to a Sync HttpEndpoint's WriteHttpResponse.Body; the returned body is asserted.

    IMPORTANT (from the engine): Foundation evaluates binding JS in a DETERMINISTIC CLOSED SANDBOX
    (JintPortableJavaScriptEvaluator). It exposes only `args` (declared expression parameters) and strips
    ambient capabilities - `Date`, `Temporal`, `Intl`, `Math.random`, `crypto`, `performance`, `process`.
    It also has NO classic Elsa host functions (`newGuid`, `getCorrelationId`, `isNullOrWhiteSpace`,
    `parseGuid`, `base64Encode`, `getConfiguration`, ...). So the JTest `date-methods`, `elsa-functions`,
    and `script-handler-functions` suites do NOT reproduce here (that is by design, not a bug); this script
    covers the pure-ES surface (array/object/json/modern syntax) that does.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== JavaScript (pure-ES) expressions ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"         { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$hep = Invoke-Step "resolve HttpEndpoint"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint' }
$whr = Invoke-Step "resolve WriteHttpResponse"{ Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse' }

# Each case: a JS expression that returns a STRING (the HTTP response body), and the expected body.
$cases = @(
    @{ name = "array.map";        js = "JSON.stringify([1,2,3].map(function(x){return x*2;}))";          expect = "[2,4,6]" }
    @{ name = "array.filter";     js = "JSON.stringify([1,2,3,4].filter(function(x){return x%2===0;}))"; expect = "[2,4]" }
    @{ name = "array.reduce";     js = "String([1,2,3,4].reduce(function(a,b){return a+b;},0))";          expect = "10" }
    @{ name = "array.flat";       js = "JSON.stringify([1,[2,[3]]].flat(2))";                             expect = "[1,2,3]" }
    @{ name = "array.includes";   js = "String([1,2,3].includes(2))";                                     expect = "True" }
    @{ name = "string.replaceAll";js = "'a,a,a'.replaceAll('a','b')";                                     expect = "b,b,b" }
    @{ name = "object.keys";      js = "JSON.stringify(Object.keys({a:1,b:2}))";                          expect = '["a","b"]' }
    @{ name = "json.roundtrip";   js = "JSON.stringify(JSON.parse('{\""x\"":5}'))";                       expect = '{"x":5}' }
    @{ name = "optional+nullish"; js = "String(({p:{q:9}})?.p?.q ?? 'none')";                             expect = "9" }
    @{ name = "nullish.default";  js = "(null ?? 'fallback')";                                            expect = "fallback" }
)

$pass = 0
foreach ($c in $cases) {
    $path = "jses-$(Get-Random -Maximum 999999)"
    $httpNode = New-ActivityNode -NodeId "http-in" -VersionId $hep -Inputs @(
        (New-LiteralInput -ReferenceKey "Path" -Value $path),
        (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $true),
        (New-LiteralInput -ReferenceKey "SupportedMethods" -Value @("GET")),
        (New-LiteralInput -ReferenceKey "ResponseMode" -Value "Sync")
    )
    $respNode = New-ActivityNode -NodeId "resp" -VersionId $whr -Inputs @(
        (New-LiteralInput -ReferenceKey "StatusCode" -Value 200),
        @{ referenceKey = "Body"; value = @{ value = $c.js; expressionType = "JavaScript" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null },
        (New-LiteralInput -ReferenceKey "ContentType" -Value "text/plain")
    )
    $root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($httpNode, $respNode))
    $def = Invoke-Step "submit ($($c.name))"  { Submit-Workflow -Ctx $ctx -Name "JsEs-$($c.name)-$(Get-Random -Max 9999)" -RootActivity $root }
    $pub = Invoke-Step "publish ($($c.name))" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
    Start-Sleep -Milliseconds 400
    $body = Invoke-Step "trigger ($($c.name))" { (Invoke-WebRequest "$BaseUrl/workflows/http/$path" -Method GET -WebSession $ctx.Session -UseBasicParsing).Content }
    if ($body -eq $c.expect) { Write-Host ("  OK  {0,-18} -> {1}" -f $c.name, $body) -ForegroundColor Green; $pass++ }
    else { Write-Host ("  FAIL {0,-18} -> got [{1}] expected [{2}]" -f $c.name, $body, $c.expect) -ForegroundColor Red }
}

Write-Host ""
Write-Host ("{0}/{1} JavaScript expression cases passed" -f $pass, $cases.Count) -ForegroundColor $(if ($pass -eq $cases.Count) { "Green" } else { "Yellow" })
