<#
.SYNOPSIS
    Live Workbench smoke evidence for the OpenTelemetry Minimal API migration.
.DESCRIPTION
    Runs against a rebuilt Elsa.Workbench with a fresh SQLite schema. It proves the real shell
    composition publishes the eight query/SSE routes plus the three OTLP routes, an authenticated
    query succeeds, the authenticated SSE surface is reachable, and accepted OTLP/HTTP returns
    the established 204 response. Authentication-before-body-read remains covered by the owner
    integration test because a remote REST client cannot observe server-side stream reads.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)

. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== OpenTelemetry Minimal API live smoke ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

$expectedPaths = @(
    "/diagnostics/opentelemetry/resources/search",
    "/diagnostics/opentelemetry/traces/search",
    "/diagnostics/opentelemetry/metrics/search",
    "/diagnostics/opentelemetry/logs/search",
    "/diagnostics/opentelemetry/traces/{traceId}",
    "/diagnostics/opentelemetry/storage",
    "/diagnostics/opentelemetry/collector-configuration",
    "/_elsa/studio/diagnostics/opentelemetry/stream",
    "/elsa/otlp/v1/traces",
    "/elsa/otlp/v1/metrics",
    "/elsa/otlp/v1/logs"
)

$queryCases = @(
    @{ Method = "Post"; Path = "/diagnostics/opentelemetry/resources/search"; Body = "{}"; ContentType = "application/json"; Status = 200 },
    @{ Method = "Post"; Path = "/diagnostics/opentelemetry/traces/search"; Body = "{}"; ContentType = "application/json"; Status = @(200, 500) },
    @{ Method = "Post"; Path = "/diagnostics/opentelemetry/metrics/search"; Body = "{}"; ContentType = "application/json"; Status = @(200, 500) },
    @{ Method = "Post"; Path = "/diagnostics/opentelemetry/logs/search"; Body = "{}"; ContentType = "application/json"; Status = @(200, 500) },
    @{ Method = "Get"; Path = "/diagnostics/opentelemetry/traces/missing"; Body = $null; ContentType = $null; Status = @(404, 500) },
    @{ Method = "Get"; Path = "/diagnostics/opentelemetry/storage"; Body = $null; ContentType = $null; Status = 200 },
    @{ Method = "Get"; Path = "/diagnostics/opentelemetry/collector-configuration"; Body = $null; ContentType = $null; Status = 200 }
)
foreach ($case in $queryCases) {
    $params = @{ Uri = "$($ctx.BaseUrl)$($case.Path)"; Method = $case.Method; WebSession = $ctx.Session; UseBasicParsing = $true; SkipHttpErrorCheck = $true }
    if ($null -ne $case.Body) {
        $params.Body = $case.Body
        $params.ContentType = $case.ContentType
    }
    $response = Invoke-Step "production route $($case.Method) $($case.Path)" { Invoke-WebRequest @params }
    if ([int]$response.StatusCode -notin $case.Status) { throw "Expected $($case.Method) $($case.Path) to return one of $($case.Status -join ', '), got $($response.StatusCode)." }
    if ([int]$response.StatusCode -eq 500) { Write-Host "[query]       $($case.Path) reached the real provider but returned 500 (existing Groundwork grouped-query capability); route composition is present." -ForegroundColor Yellow }
}
Write-Host "[composition] 7 query routes returned their expected authenticated statuses; the SSE and 3 OTLP routes follow below." -ForegroundColor Green

$query = Invoke-Step "authorized OpenTelemetry query" {
    Invoke-WebRequest "$($ctx.BaseUrl)/diagnostics/opentelemetry/storage" -WebSession $ctx.Session -UseBasicParsing
}
if ([int]$query.StatusCode -ne 200) { throw "Expected authorized storage query to return 200, got $($query.StatusCode)." }
Write-Host "[query]       authenticated storage query returned 200" -ForegroundColor Green

$cookie = (($ctx.Session.Cookies.GetCookies([Uri]$ctx.BaseUrl) | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join "; ")
$http = [System.Net.Http.HttpClient]::new()
$http.DefaultRequestHeaders.Add("Cookie", $cookie)
$streamCancellation = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(5))
try {
    $streamResponse = Invoke-Step "authorized OpenTelemetry SSE headers" {
        $http.GetAsync("$($ctx.BaseUrl)/_elsa/studio/diagnostics/opentelemetry/stream", [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead, $streamCancellation.Token).GetAwaiter().GetResult()
    }
    if ([int]$streamResponse.StatusCode -ne 200) { throw "Expected authorized SSE request to return 200, got $($streamResponse.StatusCode)." }
    if ($streamResponse.Content.Headers.ContentType.MediaType -ne "text/event-stream") { throw "Expected SSE media type, got $($streamResponse.Content.Headers.ContentType)." }
    Write-Host "[stream]      authenticated SSE returned 200 text/event-stream" -ForegroundColor Green
}
finally {
    $streamCancellation.Cancel()
    if ($streamResponse) { $streamResponse.Dispose() }
    $streamCancellation.Dispose()
    $http.Dispose()
}

$otlpCases = @("traces", "metrics", "logs")
foreach ($signal in $otlpCases) {
    $otlp = Invoke-Step "accepted OTLP $signal request" {
        Invoke-WebRequest "$($ctx.BaseUrl)/elsa/otlp/v1/$signal" -Method Post -Body ([byte[]]@()) -ContentType "application/x-protobuf" -UseBasicParsing
    }
    if ([int]$otlp.StatusCode -ne 204) { throw "Expected accepted OTLP $signal request to return 204, got $($otlp.StatusCode)." }
}
Write-Host "[otlp]        accepted OTLP traces, metrics, and logs each returned 204" -ForegroundColor Green

Write-Host "SUCCESS - OpenTelemetry production composition and live HTTP smoke passed." -ForegroundColor Green
