<#
.SYNOPSIS
    Captures one bounded closed-loop throughput ramp for the reference HTTP workflow (#2534).
.DESCRIPTION
    Measurement fixture for the owner-approved, scoped ADR 0073 D7 exception recorded on Program #2531. It is
    one-off evidence, not a gate: it enforces no budget and must never be wired into CI. The file is named
    Capture-*.ps1 so the Test-*.ps1 runner does not discover it.

    The script never builds, configures, starts or stops the host or database. The operator starts a Release
    Workbench with the intended persisted-diagnostics setting and passes the label for that setting
    (-PersistedDiagnostics). Use one fresh host and database per series.

    For each client level (default 1,2,4,8,16,32,64), the script:
      - runs the closed loop for WarmupSeconds (results recorded, excluded from throughput);
      - runs it again for WindowSeconds, where each client keeps exactly one request in flight;
      - counts successful requests (HTTP 200 + body `Alice Smith`) completed inside the window;
      - waits for every instance of the run's unique definition to be Completed with zero incidents.
    A step's throughput is its successful completions divided by the window length. Failed requests stay in the
    population; a failed request or unsettled instance invalidates that step's successful-throughput claim and
    stops the series. The series also stops when doubling clients adds less than PlateauThreshold throughput.

    While each window runs, the script samples bottleneck evidence every SampleIntervalSeconds:
      - host 1/5-minute load averages (Linux /proc/loadavg);
      - host process CPU seconds, if -HostProcessId names a local process;
      - PostgreSQL wait events and ungranted lock counts, if -PostgresConnectionString is given (needs psql);
      - dotnet-counters System.Runtime, Microsoft.AspNetCore.Hosting and Npgsql meters, if -HostProcessId is given
        and dotnet-counters is on PATH.
    SQLite busy/locked errors come from the host log. Pass the log path as -HostLogPath so each step records the
    count of matching lines added during that step.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $BaseUrl,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string] $CandidateSha,
    [Parameter(Mandatory)][ValidateSet('Immediate', 'Coalesced')][string] $Cadence,
    [Parameter(Mandatory)][ValidateSet('Http4', 'Http16')][string] $Fixture,
    [Parameter(Mandatory)][ValidateSet('On', 'Off')][string] $PersistedDiagnostics,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $OutputPath,
    [ValidateCount(1, 7)][ValidateRange(1, 64)][int[]] $Levels = @(1, 2, 4, 8, 16, 32, 64),
    [ValidateRange(5, 120)][int] $WarmupSeconds = 15,
    [ValidateRange(10, 300)][int] $WindowSeconds = 60,
    [ValidateRange(0.0, 1.0)][double] $PlateauThreshold = 0.05,
    [ValidateRange(1, 30)][int] $SampleIntervalSeconds = 5,
    [ValidateRange(10, 900)][int] $SettleTimeoutSeconds = 300,
    [int] $HostProcessId = 0,
    [string] $PostgresConnectionString = '',
    [string] $HostLogPath = '',
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../_ElsaCommon.ps1"
$ExpectedText = 'Alice Smith'
$Payload = '{"firstName":"Alice","lastName":"Smith"}'

if (-not [System.IO.Path]::IsPathRooted($OutputPath)) { throw 'OutputPath must be absolute.' }
if (Test-Path -LiteralPath $OutputPath) { throw 'OutputPath already exists; choose a new path so prior captures are preserved.' }
$sortedLevels = @($Levels | Sort-Object -Unique)
if ($sortedLevels.Count -ne $Levels.Count -or (Compare-Object $sortedLevels $Levels -SyncWindow 0)) {
    throw 'Levels must be strictly ascending without duplicates.'
}
$baseUri = [Uri]($BaseUrl.TrimEnd('/') + '/')

# ---------------------------------------------------------------- fixture setup (shape pinned to #2392 / T02)
$nonce = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$workflowName = "RuntimeThroughput2534-$Fixture-$Cadence-$nonce"
$route = "runtime-throughput-2534/$($Fixture.ToLowerInvariant())-$nonce/transform"
if ($Fixture -eq 'Http4') {
    $setup = & "$PSScriptRoot/Capture-RuntimeDbPaging2392Reference.ps1" -HostCandidateSha $CandidateSha -BaseUrl $BaseUrl `
        -Username $Username -Password $Password -ExpectedCadence $Cadence -AuthoredCadence $Cadence `
        -WorkflowName $workflowName -RoutePath $route -SetupOnly | Select-Object -Last 1
    $ctx = $setup.Context
    $definitionId = [string]$setup.DefinitionId
    $versionId = [string]$setup.VersionId
} else {
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $sequence = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $httpEndpoint = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint'
    $writeResponse = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse'
    $httpNode = New-ActivityNode -NodeId 'http-in' -VersionId $httpEndpoint -Inputs @(
        (New-LiteralInput -ReferenceKey 'Path' -Value $route),
        (New-LiteralInput -ReferenceKey 'CanStartWorkflow' -Value $true),
        (New-LiteralInput -ReferenceKey 'SupportedMethods' -Value @('POST')),
        (New-LiteralInput -ReferenceKey 'ResponseMode' -Value 'Sync')
    ) -Outputs @(
        @{ referenceKey = 'Request'; value = @{ value = @{ referenceKey = 'request' }; expressionType = 'Variable' } },
        @{ referenceKey = 'RouteData'; value = @{ value = @{ referenceKey = 'route' }; expressionType = 'Variable' } },
        @{ referenceKey = 'ParsedContent'; value = @{ value = @{ referenceKey = 'content' }; expressionType = 'Variable' } }
    )
    $expression = @{ value = "getVariable('content').firstName + ' ' + getVariable('content').lastName"; expressionType = 'JavaScript' }
    $sets = @(1..13 | ForEach-Object { New-SetVariableNode -NodeId ("set-reference-text-{0:D2}" -f $_) -VariableKey 'referenceText' -Value $expression })
    $response = New-ActivityNode -NodeId 'write-response' -VersionId $writeResponse -Inputs @(
        (New-LiteralInput -ReferenceKey 'StatusCode' -Value 200),
        @{ referenceKey = 'Body'; value = @{ value = "getVariable('referenceText')"; expressionType = 'JavaScript' }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null },
        (New-LiteralInput -ReferenceKey 'ContentType' -Value 'text/plain')
    )
    $root = New-ActivityNode -NodeId 'root' -VersionId $sequence -Structure (New-SequenceStructure -Activities (@($httpNode) + $sets + @($response)))
    $variables = @(
        (New-VariableDef -Key 'request' -Alias 'Object'),
        (New-VariableDef -Key 'route' -Alias 'Object'),
        (New-VariableDef -Key 'content' -Alias 'Object'),
        (New-VariableDef -Key 'referenceText' -Alias 'String')
    )
    $definition = Submit-Workflow -Ctx $ctx -Name $workflowName -Description '#2534 sixteen-node throughput reference (T02 shape).' `
        -RootActivity $root -Variables $variables -StrategyOptions @{ checkpointCadence = @{ mode = $Cadence } }
    $null = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    $definitionId = [string]$definition.definition.id
    $versionId = [string]$definition.version.id
    Start-Sleep -Seconds 1 # Give the published trigger table one refresh interval.
}

# ---------------------------------------------------------------- closed-loop driver
$driverSource = @'
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ElsaRuntimeThroughput2534
{
    public sealed class Attempt
    {
        public int Client;
        public double StartOffsetMs;
        public double EndOffsetMs;
        public int StatusCode;
        public bool OutputMatched;
        public string ErrorType;
    }

    public static class Driver
    {
        // Runs `clients` closed loops for `seconds`; each loop keeps one request in flight. Requests started before the
        // deadline are allowed to finish and are recorded with their offsets so the caller can apply the window boundary.
        public static Task<Attempt[]> RunAsync(HttpClient client, string relativeUri, string jsonBody, string expected, int clients, int seconds)
        {
            return Task.Run(async () =>
            {
                var attempts = new ConcurrentBag<Attempt>();
                var clock = Stopwatch.StartNew();
                var deadline = TimeSpan.FromSeconds(seconds);
                var loops = new Task[clients];
                for (var c = 0; c < clients; c++)
                {
                    var clientIndex = c + 1;
                    loops[c] = Task.Run(async () =>
                    {
                        while (clock.Elapsed < deadline)
                        {
                            var attempt = new Attempt { Client = clientIndex, StartOffsetMs = clock.Elapsed.TotalMilliseconds };
                            try
                            {
                                using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                                using (var response = await client.PostAsync(relativeUri, content).ConfigureAwait(false))
                                {
                                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    attempt.StatusCode = (int)response.StatusCode;
                                    attempt.OutputMatched = attempt.StatusCode == 200 && string.Equals(body, expected, StringComparison.Ordinal);
                                }
                            }
                            catch (Exception ex)
                            {
                                attempt.ErrorType = ex.GetType().Name;
                            }
                            attempt.EndOffsetMs = clock.Elapsed.TotalMilliseconds;
                            attempts.Add(attempt);
                        }
                    });
                }
                await Task.WhenAll(loops).ConfigureAwait(false);
                return attempts.ToArray();
            });
        }
    }
}
'@
if (-not ('ElsaRuntimeThroughput2534.Driver' -as [type])) {
    Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue
    Add-Type -TypeDefinition $driverSource -Language CSharp
}

function New-DriverClient([int] $Clients) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.CookieContainer = $ctx.Session.Cookies
    $handler.UseCookies = $true
    $handler.AllowAutoRedirect = $false
    $handler.MaxConnectionsPerServer = [Math]::Max(1, $Clients)
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = $baseUri
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    return $client
}

# ---------------------------------------------------------------- samplers
$psql = Get-Command psql -ErrorAction SilentlyContinue
$dotnetCounters = Get-Command dotnet-counters -ErrorAction SilentlyContinue

function Get-LoadAverage {
    if (Test-Path '/proc/loadavg') {
        $parts = (Get-Content '/proc/loadavg' -Raw).Split(' ')
        return [pscustomobject]@{ oneMinute = [double]$parts[0]; fiveMinute = [double]$parts[1] }
    }
    return $null
}

function Get-HostCpuSeconds {
    if ($HostProcessId -le 0) { return $null }
    try { return (Get-Process -Id $HostProcessId -ErrorAction Stop).TotalProcessorTime.TotalSeconds } catch { return $null }
}

function Get-PostgresSample {
    if ([string]::IsNullOrWhiteSpace($PostgresConnectionString) -or $null -eq $psql) { return $null }
    $waitSql = "select coalesce(wait_event_type,'CPU/none') || ':' || coalesce(wait_event,'-') || ':' || coalesce(state,'-') as k, count(*) from pg_stat_activity where datname = current_database() and pid <> pg_backend_pid() group by 1 order by 2 desc"
    $lockSql = "select count(*) from pg_locks where not granted"
    try {
        $waits = @(& $psql.Source $PostgresConnectionString -At -F '|' -c $waitSql 2>$null | Where-Object { $_ } | ForEach-Object {
            $kv = $_.Split('|'); [pscustomobject]@{ key = $kv[0]; count = [int]$kv[1] }
        })
        $ungranted = [int](& $psql.Source $PostgresConnectionString -At -c $lockSql 2>$null | Select-Object -First 1)
        return [pscustomobject]@{ waits = $waits; ungrantedLocks = $ungranted }
    } catch { return [pscustomobject]@{ error = $_.Exception.GetType().Name } }
}

function Get-HostLogBusyCount {
    if ([string]::IsNullOrWhiteSpace($HostLogPath) -or -not (Test-Path -LiteralPath $HostLogPath)) { return $null }
    return @(Select-String -LiteralPath $HostLogPath -Pattern 'SQLITE_BUSY|SQLITE_LOCKED|database is locked|DbContext.*concurrent|second operation was started' -SimpleMatch:$false).Count
}

function Get-DefinitionInstances {
    $all = [System.Collections.Generic.List[object]]::new()
    $cursor = $null
    for ($page = 0; $page -lt 1000; $page++) {
        $query = "definitionId=$([Uri]::EscapeDataString($definitionId))&take=100"
        if ($cursor) { $query += "&cursor=$([Uri]::EscapeDataString($cursor))" }
        $result = Invoke-RestMethod "$($baseUri)runtime/workflows/instances/page?$query" -WebSession $ctx.Session -TimeoutSec 60
        foreach ($item in @($result.items)) { $all.Add($item) }
        if (-not $result.hasNext) { return $all.ToArray() }
        $cursor = [string]$result.nextCursor
        if ([string]::IsNullOrWhiteSpace($cursor)) { throw 'Paged instance list reported hasNext without nextCursor.' }
    }
    throw 'Paged instance list exceeded the 1000-page safety bound.'
}

function Wait-Settlement([int] $ExpectedCount) {
    $deadline = [DateTime]::UtcNow.AddSeconds($SettleTimeoutSeconds)
    do {
        $instances = @(Get-DefinitionInstances)
        $pending = @($instances | Where-Object { [string]$_.status -notin @('Completed', 'Finished', 'Faulted', 'Cancelled') })
        if ($instances.Count -ge $ExpectedCount -and $pending.Count -eq 0) { break }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    [pscustomobject]@{
        expectedInstanceCount = $ExpectedCount
        instanceCount = $instances.Count
        notCompletedCount = @($instances | Where-Object { [string]$_.status -notin @('Completed', 'Finished') }).Count
        incidentInstanceCount = @($instances | Where-Object { [int]$_.incidentCount -ne 0 }).Count
        settled = ($instances.Count -eq $ExpectedCount -and $pending.Count -eq 0)
    }
}

function Get-Percentile([double[]] $Sorted, [double] $Fraction) {
    if ($Sorted.Count -eq 0) { return $null }
    return $Sorted[[Math]::Max(0, [Math]::Ceiling($Fraction * $Sorted.Count) - 1)]
}

# ---------------------------------------------------------------- ramp
$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) { New-Item -ItemType Directory -Path $outputDirectory | Out-Null }
$steps = [System.Collections.Generic.List[object]]::new()
$requestsSent = 0
$previousThroughput = $null
$stopReason = 'AllLevelsCompleted'

Write-Host "== Throughput ramp #2534: $Fixture / $Cadence / diagnostics $PersistedDiagnostics == -> $BaseUrl" -ForegroundColor Cyan
foreach ($level in $Levels) {
    $client = New-DriverClient -Clients $level
    try {
        $warmup = [ElsaRuntimeThroughput2534.Driver]::RunAsync($client, "workflows/http/$route", $Payload, $ExpectedText, $level, $WarmupSeconds).GetAwaiter().GetResult()

        $counterFile = $null
        $counterProcess = $null
        if ($HostProcessId -gt 0 -and $null -ne $dotnetCounters) {
            $counterFile = Join-Path $outputDirectory ("{0}.counters.c{1}.json" -f [IO.Path]::GetFileNameWithoutExtension($OutputPath), $level)
            $duration = [TimeSpan]::FromSeconds($WindowSeconds).ToString('hh\:mm\:ss')
            $counterProcess = Start-Process -FilePath $dotnetCounters.Source -PassThru -NoNewWindow -RedirectStandardOutput ([IO.Path]::GetTempFileName()) -ArgumentList @(
                'collect', '--process-id', $HostProcessId, '--format', 'json', '--output', $counterFile, '--duration', $duration,
                '--counters', 'System.Runtime,Microsoft.AspNetCore.Hosting,Npgsql')
        }
        $busyBefore = Get-HostLogBusyCount
        $cpuBefore = Get-HostCpuSeconds
        $samples = [System.Collections.Generic.List[object]]::new()
        $measured = [ElsaRuntimeThroughput2534.Driver]::RunAsync($client, "workflows/http/$route", $Payload, $ExpectedText, $level, $WindowSeconds)
        $sampleClock = [Diagnostics.Stopwatch]::StartNew()
        while (-not $measured.IsCompleted) {
            $samples.Add([pscustomobject]@{
                offsetSeconds = [Math]::Round($sampleClock.Elapsed.TotalSeconds, 1)
                load = Get-LoadAverage
                hostCpuSeconds = Get-HostCpuSeconds
                postgres = Get-PostgresSample
            })
            $null = $measured.Wait([TimeSpan]::FromSeconds($SampleIntervalSeconds))
        }
        $attempts = $measured.GetAwaiter().GetResult()
        $cpuAfter = Get-HostCpuSeconds
        $busyAfter = Get-HostLogBusyCount
        if ($null -ne $counterProcess) { $null = $counterProcess.WaitForExit(15000) }
    } finally {
        $client.Dispose()
    }

    $requestsSent += $warmup.Count + $attempts.Count
    $windowMs = $WindowSeconds * 1000.0
    $inWindow = @($attempts | Where-Object { $_.EndOffsetMs -le $windowMs })
    $succeeded = @($inWindow | Where-Object { $_.OutputMatched })
    $failedAll = @(@($warmup) + @($attempts) | Where-Object { -not $_.OutputMatched })
    $latencies = [double[]]@($succeeded | ForEach-Object { $_.EndOffsetMs - $_.StartOffsetMs } | Sort-Object)
    $throughput = $succeeded.Count / $WindowSeconds
    $settlement = Wait-Settlement -ExpectedCount $requestsSent
    $statusHistogram = @(@($warmup) + @($attempts) | Group-Object { if ($_.ErrorType) { "error:$($_.ErrorType)" } else { [string]$_.StatusCode } } |
        ForEach-Object { [pscustomobject]@{ status = $_.Name; count = $_.Count } })
    $hostCpuUtilization = if ($null -ne $cpuBefore -and $null -ne $cpuAfter) {
        [Math]::Round(($cpuAfter - $cpuBefore) / ($sampleClock.Elapsed.TotalSeconds * [Environment]::ProcessorCount), 3)
    } else { $null }
    $gain = if ($null -ne $previousThroughput -and $previousThroughput -gt 0) { ($throughput - $previousThroughput) / $previousThroughput } else { $null }

    $step = [pscustomobject]@{
        clients = $level
        warmupAttempts = $warmup.Count
        measuredAttempts = $attempts.Count
        completedInWindow = $inWindow.Count
        succeededInWindow = $succeeded.Count
        failedAttemptsIncludingWarmup = $failedAll.Count
        statusHistogram = $statusHistogram
        successfulExecutionsPerSecond = [Math]::Round($throughput, 3)
        gainVersusPreviousLevel = if ($null -ne $gain) { [Math]::Round($gain, 4) } else { $null }
        exploratoryLatencyMilliseconds = [pscustomobject]@{
            p50 = Get-Percentile $latencies 0.50
            p95 = Get-Percentile $latencies 0.95
            note = 'Closed-loop latency of successful in-window requests; exploratory, not an objective.'
        }
        hostCpuUtilizationOfAllCores = $hostCpuUtilization
        sqliteBusyOrConcurrencyLogLinesAdded = if ($null -ne $busyBefore -and $null -ne $busyAfter) { $busyAfter - $busyBefore } else { $null }
        dotnetCountersFile = if ($counterFile) { Split-Path -Leaf $counterFile } else { $null }
        samples = $samples.ToArray()
        settlement = $settlement
        validForSuccessfulThroughput = ($failedAll.Count -eq 0 -and $settlement.settled -and $settlement.notCompletedCount -eq 0 -and $settlement.incidentInstanceCount -eq 0)
    }
    $steps.Add($step)
    Write-Host ("[c={0,2}] {1,8:N2} exec/s  gain={2}  ok={3} failed={4}  p95={5}ms  cpu={6}  settled={7}" -f `
        $level, $throughput, $(if ($null -ne $gain) { '{0:P1}' -f $gain } else { 'n/a' }), $succeeded.Count, $failedAll.Count,
        $(if ($latencies.Count) { '{0:N0}' -f (Get-Percentile $latencies 0.95) } else { 'n/a' }), $hostCpuUtilization, $settlement.settled)

    if (-not $step.validForSuccessfulThroughput) { $stopReason = "InvalidStepAtClients$level"; break }
    if ($null -ne $gain -and $gain -lt $PlateauThreshold) { $stopReason = "PlateauAtClients$level"; break }
    $previousThroughput = $throughput
}

$validSteps = @($steps | Where-Object { $_.validForSuccessfulThroughput })
$peak = $validSteps | Sort-Object successfulExecutionsPerSecond -Descending | Select-Object -First 1
$artifact = [ordered]@{
    schema = 'elsa.runtime-throughput-ramp/1'
    issue = 'https://github.com/elsa-workflows/elsa-foundation/issues/2534'
    authority = 'Scoped ADR 0073 D7 exception, Program #2531 (owner decision 2026-10-09). One-off evidence; not a gate.'
    capturedAtUtc = [DateTime]::UtcNow.ToString('o')
    scriptHead = (git -C (Join-Path $PSScriptRoot '../..') rev-parse HEAD 2>$null)
    candidateSha = $CandidateSha.ToLowerInvariant()
    baseUrl = $BaseUrl
    fixture = $Fixture
    cadence = $Cadence
    persistedDiagnosticsLabel = $PersistedDiagnostics
    definitionId = $definitionId
    versionId = $versionId
    route = $route
    clientMachine = [ordered]@{ processorCount = [Environment]::ProcessorCount; os = [Environment]::OSVersion.VersionString; pwsh = $PSVersionTable.PSVersion.ToString() }
    protocol = [ordered]@{
        levels = $Levels; warmupSeconds = $WarmupSeconds; windowSeconds = $WindowSeconds; plateauThreshold = $PlateauThreshold
        sampleIntervalSeconds = $SampleIntervalSeconds; settleTimeoutSeconds = $SettleTimeoutSeconds
        throughputDefinition = 'Successful (HTTP 200 + exact body) requests completed inside the measured window / window seconds.'
        population = 'All attempts retained; no retry or replacement. Any failure or unsettled instance invalidates the step and stops the series.'
    }
    samplersAvailable = [ordered]@{ hostProcess = ($HostProcessId -gt 0); postgres = (-not [string]::IsNullOrWhiteSpace($PostgresConnectionString) -and $null -ne $psql); dotnetCounters = ($HostProcessId -gt 0 -and $null -ne $dotnetCounters); hostLog = (-not [string]::IsNullOrWhiteSpace($HostLogPath)) }
    stopReason = $stopReason
    peakValidStep = if ($peak) { [ordered]@{ clients = $peak.clients; successfulExecutionsPerSecond = $peak.successfulExecutionsPerSecond } } else { $null }
    steps = $steps.ToArray()
}
$artifact | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host ("[artifact] {0}; stop={1}" -f $OutputPath, $stopReason)
if ($stopReason -like 'InvalidStep*') { exit 1 }
