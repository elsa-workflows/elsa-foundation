<#
.SYNOPSIS
    Concurrency correctness of the RuntimeDbPaging2392Reference HTTP workflow under N concurrent clients (#2532).
.DESCRIPTION
    Publishes a fresh copy of the four-node reference (Sequence / synchronous HttpEndpoint / SetVariable /
    WriteHttpResponse) under a unique route, then sends Clients x RequestsPerClient POSTs, with every client
    looping concurrently. It asserts correctness only:
      - every response is HTTP 200 with body `Alice Smith` (any 202, 500, timeout or transport error fails);
      - exactly one durable instance exists per request, each Completed/Finished with zero incidents;
      - every instance carries the effective cadence the run was authored with.
    ADR 0073 D7 treats concurrency and SQLite contention as correctness requirements, not performance gates. This
    script records no elapsed time and enforces no budget. Failed requests stay in the result and are never retried.
    Requires the server from source (see ../README.md). Run once per cadence, e.g. -Cadence Immediate and
    -Cadence Coalesced, and at -Clients 16 and -Clients 32.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [ValidateRange(1, 64)][int] $Clients = 16,
    [ValidateRange(1, 50)][int] $RequestsPerClient = 4,
    [ValidateSet("Coalesced", "Immediate")][string] $Cadence = "Coalesced",
    [ValidateRange(10, 600)][int] $SettleTimeoutSeconds = 120
)
$ErrorActionPreference = "Stop"
$ExpectedText = "Alice Smith"
$Payload = '{"firstName":"Alice","lastName":"Smith"}'
$ExpectedTotal = $Clients * $RequestsPerClient
$suffix = "{0}-{1}" -f $Cadence.ToLowerInvariant(), ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$workflowName = "RuntimeConcurrency2532-$suffix"
$route = "runtime-concurrency-2532/$suffix"

Write-Host "== Concurrency correctness ($Cadence, $Clients clients x $RequestsPerClient) == -> $BaseUrl" -ForegroundColor Cyan

# Reuse the approved reference fixture for authoring/publication so the workflow shape cannot drift from #2392.
$setupArgs = @{
    HostCandidateSha = ("0" * 39) + "1"   # placeholder: this test makes no source-identity claim
    BaseUrl          = $BaseUrl
    Username         = $Username
    Password         = $Password
    ExpectedCadence  = $Cadence
    AuthoredCadence  = $Cadence
    WorkflowName     = $workflowName
    RoutePath        = $route
    SetupOnly        = $true
}
$setup = & "$PSScriptRoot/Capture-RuntimeDbPaging2392Reference.ps1" @setupArgs | Select-Object -Last 1
if ($null -eq $setup -or [string]::IsNullOrWhiteSpace([string]$setup.DefinitionId)) {
    Write-Host "FAIL - reference setup returned no definition id." -ForegroundColor Red
    exit 1
}
$ctx = $setup.Context
$baseUri = [Uri]($BaseUrl.TrimEnd('/') + '/')

$senderSource = @'
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ElsaRuntimeConcurrency2532
{
    public sealed class Result
    {
        public int Client;
        public int Index;
        public int StatusCode;
        public string Body;
        public string ErrorType;
    }

    public static class Sender
    {
        public static Result[] Run(HttpClient client, string relativeUri, string jsonBody, int clients, int requestsPerClient)
        {
            var loops = new Task<List<Result>>[clients];
            for (var c = 0; c < clients; c++)
            {
                var clientIndex = c;
                loops[c] = Task.Run(async () =>
                {
                    var results = new List<Result>(requestsPerClient);
                    for (var i = 0; i < requestsPerClient; i++)
                    {
                        var result = new Result { Client = clientIndex + 1, Index = i + 1 };
                        try
                        {
                            using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                            using (var response = await client.PostAsync(relativeUri, content).ConfigureAwait(false))
                            {
                                result.StatusCode = (int)response.StatusCode;
                                result.Body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            result.ErrorType = ex.GetType().Name;
                        }
                        results.Add(result);
                    }
                    return results;
                });
            }
            Task.WaitAll(loops);
            var all = new List<Result>();
            foreach (var loop in loops) all.AddRange(loop.Result);
            return all.ToArray();
        }
    }
}
'@
if (-not ('ElsaRuntimeConcurrency2532.Sender' -as [type])) {
    Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue
    Add-Type -TypeDefinition $senderSource -Language CSharp
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.CookieContainer = $ctx.Session.Cookies
$handler.UseCookies = $true
$handler.AllowAutoRedirect = $false
$handler.MaxConnectionsPerServer = $Clients
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = $baseUri
$client.Timeout = [TimeSpan]::FromSeconds(60)
try {
    $results = [ElsaRuntimeConcurrency2532.Sender]::Run($client, "workflows/http/$route", $Payload, $Clients, $RequestsPerClient)
} finally {
    $client.Dispose()
}

$badResponses = @($results | Where-Object { $_.StatusCode -ne 200 -or [string]$_.Body -cne $ExpectedText })
$statusHistogram = $results | Group-Object { if ($_.ErrorType) { "error:$($_.ErrorType)" } else { [string]$_.StatusCode } } |
    ForEach-Object { "{0}={1}" -f $_.Name, $_.Count }
Write-Host ("[responses] total={0}; bad={1}; status: {2}" -f $results.Count, $badResponses.Count, ($statusHistogram -join ', '))
foreach ($bad in ($badResponses | Select-Object -First 10)) {
    Write-Host ("  client={0} index={1} status={2} error={3} body={4}" -f $bad.Client, $bad.Index, $bad.StatusCode, $bad.ErrorType, $bad.Body) -ForegroundColor Yellow
}

# Settlement: page through every instance of this run's unique definition until all are terminal or the bound expires.
function Get-DefinitionInstances {
    $all = [System.Collections.Generic.List[object]]::new()
    $cursor = $null
    for ($page = 0; $page -lt 50; $page++) {
        $query = "definitionId=$([Uri]::EscapeDataString([string]$setup.DefinitionId))&take=100"
        if ($cursor) { $query += "&cursor=$([Uri]::EscapeDataString($cursor))" }
        $result = Invoke-RestMethod "$($baseUri)runtime/workflows/instances/page?$query" -WebSession $ctx.Session -TimeoutSec 30
        foreach ($item in @($result.items)) { $all.Add($item) }
        if (-not $result.hasNext) { return $all.ToArray() }
        $cursor = [string]$result.nextCursor
        if ([string]::IsNullOrWhiteSpace($cursor)) { throw "Paged instance list reported hasNext without nextCursor." }
    }
    throw "Paged instance list exceeded the 50-page safety bound."
}

$deadline = [DateTime]::UtcNow.AddSeconds($SettleTimeoutSeconds)
do {
    $instances = @(Get-DefinitionInstances)
    $pending = @($instances | Where-Object { [string]$_.status -notin @('Completed', 'Finished', 'Faulted', 'Cancelled') })
    if ($instances.Count -ge $ExpectedTotal -and $pending.Count -eq 0) { break }
    Start-Sleep -Seconds 2
} while ([DateTime]::UtcNow -lt $deadline)

$ids = @($instances | ForEach-Object { [string]$_.workflowExecutionId } | Where-Object { $_ })
$uniqueIds = @($ids | Select-Object -Unique)
$notCompleted = @($instances | Where-Object { [string]$_.status -notin @('Completed', 'Finished') })
$withIncidents = @($instances | Where-Object { [int]$_.incidentCount -ne 0 })
Write-Host ("[instances] count={0}; unique={1}; notCompleted={2}; withIncidents={3}" -f $instances.Count, $uniqueIds.Count, $notCompleted.Count, $withIncidents.Count)
foreach ($bad in ($notCompleted + $withIncidents | Select-Object -First 10)) {
    Write-Host ("  execution={0} status={1} incidents={2}" -f $bad.workflowExecutionId, $bad.status, $bad.incidentCount) -ForegroundColor Yellow
}

# Effective cadence on a bounded sample of details (the list summary does not carry it).
$cadenceMismatches = [System.Collections.Generic.List[string]]::new()
foreach ($executionId in ($uniqueIds | Select-Object -First 5)) {
    $detail = Invoke-RestMethod "$($baseUri)runtime/workflows/instances/$([Uri]::EscapeDataString($executionId))" -WebSession $ctx.Session -TimeoutSec 30
    $effective = if ($null -ne $detail.checkpointCadence) { $detail.checkpointCadence } else { $detail.instance.checkpointCadence }
    if ("$effective" -ne $Cadence) { $cadenceMismatches.Add("$executionId=$effective") }
}
Write-Host ("[cadence]   sampled={0}; mismatches={1}" -f [Math]::Min(5, $uniqueIds.Count), $cadenceMismatches.Count)

Write-Host ""
$ok = $results.Count -eq $ExpectedTotal -and $badResponses.Count -eq 0 -and
    $instances.Count -eq $ExpectedTotal -and $uniqueIds.Count -eq $ExpectedTotal -and
    $notCompleted.Count -eq 0 -and $withIncidents.Count -eq 0 -and $cadenceMismatches.Count -eq 0
if ($ok) {
    Write-Host ("SUCCESS - {0} concurrent requests ({1} clients, {2}): all HTTP 200 '{3}', {0} distinct Completed instances, zero incidents." -f $ExpectedTotal, $Clients, $Cadence, $ExpectedText) -ForegroundColor Green
} else {
    Write-Host ("FAIL - responses ok={0}/{1}; instances={2} unique={3} notCompleted={4} incidents={5}; cadence mismatches={6} ({7})" -f `
        ($results.Count - $badResponses.Count), $ExpectedTotal, $instances.Count, $uniqueIds.Count, $notCompleted.Count, $withIncidents.Count, $cadenceMismatches.Count, ($cadenceMismatches -join ', ')) -ForegroundColor Red
    exit 1
}
