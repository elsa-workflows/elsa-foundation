<#
.SYNOPSIS
    Manually captures one finite low-logging T02 timing case against an already-owned Workbench.
.DESCRIPTION
    This script is a measurement fixture only. It never builds, configures, starts, stops, or cleans up a
    host/container/database. The owner supplies the loopback BaseUrl, candidate SHA, effective cadence, and
    one bounded case. Every case performs one unmeasured preflight, exactly 25 warm-ups, then exactly 60
    measured requests. Failures remain in the 60-sample set; no retry or replacement sample is made.

    The monotonic interval begins immediately before HttpClient.SendAsync and ends after the complete response
    body is read. Authentication, fixture authoring/publication/export, request JSON/content preparation, warm-ups,
    preflight, instance verification, and all readbacks are outside the measured interval. EF Core logging must be
    Warning with no command/transaction Debug override on the owner-started host. Persisted diagnostics stay enabled
    by default; this script does not change host logging or diagnostics settings.

    The bounded case set is intentionally finite: Http4Sequential, Http16Sequential, Http4Concurrency4, and
    RestCompanionCoalesced (Coalesced only). This file is named Capture-*.ps1 and is not discovered by Test-*.ps1.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $BaseUrl,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string] $CandidateSha,
    [Parameter(Mandatory)][ValidateSet('Immediate', 'Coalesced')][string] $ExpectedCadence,
    [Parameter(Mandatory)][ValidateSet('Http4Sequential', 'Http16Sequential', 'Http4Concurrency4', 'RestCompanionCoalesced')][string] $Case,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $OutputPath,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [bool] $PersistedDiagnosticsEnabled = $true
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../variables/_VarCommon.ps1"
$ExpectedText = 'Alice Smith'
$PayloadJson = '{"firstName":"Alice","lastName":"Smith"}'
$WarmupCount = 25
$MeasuredCount = 60
$InstanceCountExpected = 1 + $WarmupCount + $MeasuredCount

if ($Case -eq 'RestCompanionCoalesced' -and $ExpectedCadence -ne 'Coalesced') {
    throw 'RestCompanionCoalesced is selected only for ExpectedCadence=Coalesced.'
}
if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    throw 'OutputPath must be absolute and point to the owner-selected private capture directory.'
}
if (Test-Path -LiteralPath $OutputPath) {
    throw 'OutputPath already exists; choose a new private capture path so prior samples are preserved.'
}
$BaseUri = $null
if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$BaseUri) -or
    $BaseUri.Scheme -notin @('http', 'https') -or -not $BaseUri.IsLoopback) {
    throw 'BaseUrl must be an absolute loopback HTTP(S) URL for the already-owned host.'
}
$BaseUri = [Uri]($BaseUrl.TrimEnd('/') + '/')

$ExpectedMaxSegment = if ($ExpectedCadence -eq 'Coalesced') { 50 } else { $null }
$ExpectedInspection = if ($ExpectedCadence -eq 'Coalesced') { 'boundary-level' } else { 'activity-level' }
$RepoRoot = (git -C (Join-Path $PSScriptRoot '../..') rev-parse --show-toplevel).Trim()
$FixtureHead = (git -C $RepoRoot rev-parse HEAD).Trim()
$FixtureScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()

function Get-HostSnapshot {
    $loadAverage = $null
    $uptimeCommand = Get-Command uptime -ErrorAction SilentlyContinue
    if ($uptimeCommand) {
        $uptimeOutput = (& $uptimeCommand.Source 2>$null | Out-String)
        $match = [regex]::Match($uptimeOutput, 'load averages?:\s*([0-9]+(?:\.[0-9]+)?)[, ]+\s*([0-9]+(?:\.[0-9]+)?)[, ]+\s*([0-9]+(?:\.[0-9]+)?)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $loadAverage = @(
                [double]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture),
                [double]::Parse($match.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture),
                [double]::Parse($match.Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)
            )
        }
    }
    $architecture = $env:PROCESSOR_ARCHITECTURE
    try { $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() } catch { }
    [pscustomobject]@{
        capturedAtUtc = [DateTime]::UtcNow.ToString('o')
        osDescription = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = $architecture
        logicalProcessorCount = [Environment]::ProcessorCount
        loadAverage1m5m15m = $loadAverage
    }
}

function Get-Sha256Text {
    param([Parameter(Mandatory)][string] $Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function Get-ObjectField {
    param([AllowNull()] $Value, [Parameter(Mandatory)][string] $Name)
    if ($null -eq $Value) { return [pscustomobject]@{ Exists = $false; Value = $null } }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ([string]$key -ieq $Name) { return [pscustomobject]@{ Exists = $true; Value = $Value[$key] } }
        }
    } else {
        $property = $Value.PSObject.Properties | Where-Object { $_.Name -ieq $Name } | Select-Object -First 1
        if ($null -ne $property) { return [pscustomobject]@{ Exists = $true; Value = $property.Value } }
    }
    [pscustomobject]@{ Exists = $false; Value = $null }
}

function Test-ExpectedCadence {
    param([AllowNull()] $Cadence, [AllowNull()] $MaxSegment, [AllowNull()] $Inspection)
    $maxSegmentMatches = if ($ExpectedCadence -eq 'Coalesced') { [string]$MaxSegment -eq '50' } else { $null -eq $MaxSegment }
    return ($Cadence -eq $ExpectedCadence -and $Inspection -eq $ExpectedInspection -and $maxSegmentMatches)
}

function Write-TimingArtifact {
    param([Parameter(Mandatory)][System.Collections.IDictionary] $Artifact)
    $outDirectory = Split-Path -Parent $OutputPath
    if (-not (Test-Path -LiteralPath $outDirectory)) { New-Item -Path $outDirectory -ItemType Directory -Force | Out-Null }
    $json = ConvertTo-Json -InputObject $Artifact -Depth 100
    [System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Add-ExportNodeEvidence {
    param(
        [AllowNull()] $Value,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]] $Nodes,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]] $Seen
    )
    if ($null -eq $Value -or $Value -is [string]) { return }
    # ConvertFrom-Json may materialize dates as DateTime values; their Date properties recurse.
    # Traverse JSON containers only, leaving every scalar outside the node walk.
    if ($Value -isnot [System.Collections.IDictionary] -and $Value -isnot [pscustomobject] -and
        $Value -isnot [System.Collections.IEnumerable]) { return }

    $nodeIdField = Get-ObjectField -Value $Value -Name 'executableNodeId'
    $intrinsicField = Get-ObjectField -Value $Value -Name 'IntrinsicKind'
    $versionField = Get-ObjectField -Value $Value -Name 'activityTypeVersion'
    $activityTypeField = Get-ObjectField -Value $Value -Name 'activityType'
    $contractField = Get-ObjectField -Value $Value -Name 'activityContract'
    if ($intrinsicField.Exists -or $versionField.Exists -or $activityTypeField.Exists -or $contractField.Exists) {
        if (-not $nodeIdField.Exists -or [string]::IsNullOrWhiteSpace([string]$nodeIdField.Value)) {
            throw 'Executable export node is missing its canonical executableNodeId.'
        }
        $nodeId = [string]$nodeIdField.Value
        if ($Seen.Add($nodeId)) {
            $contractState = if (-not $contractField.Exists) { 'omitted' } elseif ($null -eq $contractField.Value) { 'null' } else { 'present' }
            $profileField = if ($null -ne $contractField.Value) { Get-ObjectField -Value $contractField.Value -Name 'sideEffectProfile' } else { [pscustomobject]@{ Exists = $false; Value = $null } }
            $profileState = if (-not $profileField.Exists) { 'omitted' } elseif ($null -eq $profileField.Value) { 'null' } else { [string]$profileField.Value }
            $Nodes.Add([pscustomobject]@{
                nodeId = $nodeId
                activityTypeVersion = if ($versionField.Exists) { [string]$versionField.Value } else { $null }
                activityTypeVersionField = 'activityTypeVersion'
                intrinsicKind = if ($intrinsicField.Exists) { [string]$intrinsicField.Value } else { $null }
                activityType = if ($activityTypeField.Exists) { [string]$activityTypeField.Value } else { $null }
                activityContractWireState = $contractState
                sideEffectProfileWireState = $profileState
            })
        }
    }

    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($child in $Value.Values) { Add-ExportNodeEvidence -Value $child -Nodes $Nodes -Seen $Seen }
    } elseif ($Value -is [System.Collections.IEnumerable]) {
        foreach ($child in $Value) { Add-ExportNodeEvidence -Value $child -Nodes $Nodes -Seen $Seen }
    } else {
        foreach ($property in $Value.PSObject.Properties) {
            Add-ExportNodeEvidence -Value $property.Value -Nodes $Nodes -Seen $Seen
        }
    }
}

function Get-ExecutableExportEvidence {
    param([Parameter(Mandatory)] $Export, [Parameter(Mandatory)][int] $ExpectedSetCount)
    $nodes = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    Add-ExportNodeEvidence -Value $Export -Nodes $nodes -Seen $seen
    $setNodes = @($nodes | Where-Object { $_.intrinsicKind -ieq 'Set' })
    if ($setNodes.Count -ne $ExpectedSetCount) {
        throw "Pinned executable export had $($setNodes.Count) Set intrinsics; expected $ExpectedSetCount."
    }
    $exportJson = ConvertTo-Json -InputObject $Export -Depth 100 -Compress
    [pscustomobject]@{
        normalizedExportSha256 = Get-Sha256Text -Text $exportJson
        exportedNodeCount = $nodes.Count
        setIntrinsicCount = $setNodes.Count
        setIntrinsicNodeIds = @($setNodes | ForEach-Object { $_.nodeId })
        setIntrinsicContractsHaveNoValue = -not [bool](@($setNodes | Where-Object { $_.activityContractWireState -eq 'present' }).Count)
        nodeProfiles = @($nodes | Select-Object nodeId, activityTypeVersion, activityTypeVersionField, intrinsicKind, activityType, activityContractWireState, sideEffectProfileWireState)
        profileQualification = 'Wire profile omission is preserved as omitted; source ActivityContract uses a JsonConstructor default of External and JsonIgnore(WhenWritingDefault). No omitted wire field is rewritten as null or as a recorded External value.'
    }
}

$fusionPath = 'src/essentials/Workflows/Runtime/Core/Models/ExecutableNode.cs'
$fusionSource = (& git -C $RepoRoot show "$($CandidateSha):$fusionPath" 2>$null) -join "`n"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($fusionSource)) {
    throw "Could not read WorkflowIntrinsicFusion from supplied candidate $CandidateSha."
}
$fusionClass = [regex]::Match($fusionSource, 'public static class WorkflowIntrinsicFusion\s*\{(?<body>[\s\S]*?)\n\}', [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
$setFusable = $fusionClass.Success -and [regex]::IsMatch($fusionClass.Groups['body'].Value, 'WorkflowIntrinsicKind\.Set[\s\S]{0,500}?=>\s*true', [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
if (-not $setFusable) { throw "Candidate $CandidateSha does not source-classify WorkflowIntrinsicKind.Set as fusable." }

$samplerSource = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ElsaRuntimeDbTiming2386
{
    public sealed class TimingResponse
    {
        public int Index { get; set; }
        public int ClientIndex { get; set; }
        public int StatusCode { get; set; }
        public string Body { get; set; }
        public double ElapsedMilliseconds { get; set; }
        public string ErrorType { get; set; }
    }

    public static class HttpSampler
    {
        public static async Task<TimingResponse> SendAsync(HttpClient client, string relativeUri, string jsonBody, int index, int clientIndex)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, relativeUri))
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var watch = Stopwatch.StartNew();
                try
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        watch.Stop();
                        return new TimingResponse { Index = index, ClientIndex = clientIndex, StatusCode = (int)response.StatusCode, Body = body, ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds, ErrorType = null };
                    }
                }
                catch (Exception error)
                {
                    watch.Stop();
                    return new TimingResponse { Index = index, ClientIndex = clientIndex, StatusCode = 0, Body = String.Empty, ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds, ErrorType = error.GetType().Name };
                }
            }
        }

        private static async Task<List<TimingResponse>> RunClientAsync(HttpClient client, string relativeUri, string jsonBody, int clientIndex, int requestCount)
        {
            var samples = new List<TimingResponse>();
            for (var offset = 0; offset < requestCount; offset++)
                samples.Add(await SendAsync(client, relativeUri, jsonBody, clientIndex * requestCount + offset + 1, clientIndex + 1).ConfigureAwait(false));
            return samples;
        }

        public static async Task<TimingResponse[]> RunFourClientsAsync(HttpClient[] clients, string relativeUri, string jsonBody, int requestsPerClient)
        {
            if (clients == null || clients.Length != 4) throw new ArgumentException("Exactly four clients are required.", "clients");
            if (requestsPerClient != 15) throw new ArgumentException("Exactly 15 requests per client are required.", "requestsPerClient");
            var tasks = new Task<List<TimingResponse>>[4];
            for (var index = 0; index < 4; index++)
                tasks[index] = RunClientAsync(clients[index], relativeUri, jsonBody, index, requestsPerClient);
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.SelectMany(items => items).OrderBy(item => item.Index).ToArray();
        }
    }
}
'@
if (-not ('ElsaRuntimeDbTiming2386.HttpSampler' -as [type])) {
    Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue
    Add-Type -TypeDefinition $samplerSource -Language CSharp
}

function New-TimingHttpClient {
    param([Parameter(Mandatory)][System.Net.CookieContainer] $CookieContainer)
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.CookieContainer = $CookieContainer
    $handler.UseCookies = $true
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = $BaseUri
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    return $client
}

function Invoke-TimedRequest {
    param([Parameter(Mandatory)][System.Net.Http.HttpClient] $Client, [Parameter(Mandatory)][string] $RelativeUri, [Parameter(Mandatory)][string] $JsonBody, [Parameter(Mandatory)][int] $Index, [int] $ClientIndex = 1)
    [ElsaRuntimeDbTiming2386.HttpSampler]::SendAsync($Client, $RelativeUri, $JsonBody, $Index, $ClientIndex).GetAwaiter().GetResult()
}

function Convert-HttpResponseRecord {
    param([Parameter(Mandatory)] $Response, [Parameter(Mandatory)][string] $Phase)
    [pscustomobject]@{
        index = [int]$Response.Index
        clientIndex = [int]$Response.ClientIndex
        phase = $Phase
        statusCode = [int]$Response.StatusCode
        elapsedMilliseconds = [double]$Response.ElapsedMilliseconds
        outputMatched = ([int]$Response.StatusCode -eq 200 -and [string]$Response.Body -ceq $ExpectedText)
        errorType = $Response.ErrorType
    }
}

function Convert-RestResponseRecord {
    param([Parameter(Mandatory)] $Response, [Parameter(Mandatory)][string] $Phase)
    $executionId = $null
    if (-not [string]::IsNullOrWhiteSpace([string]$Response.Body)) {
        try {
            $body = ConvertFrom-Json -InputObject ([string]$Response.Body) -ErrorAction Stop
            if ($body.workflowExecutionId) { $executionId = [string]$body.workflowExecutionId }
        } catch { }
    }
    [pscustomobject]@{
        index = [int]$Response.Index
        clientIndex = [int]$Response.ClientIndex
        phase = $Phase
        statusCode = [int]$Response.StatusCode
        elapsedMilliseconds = [double]$Response.ElapsedMilliseconds
        admissionMatched = ([int]$Response.StatusCode -eq 200 -and -not [string]::IsNullOrWhiteSpace($executionId))
        workflowExecutionId = $executionId
        errorType = $Response.ErrorType
    }
}

function Get-EndpointPage {
    param([Parameter(Mandatory)][Microsoft.PowerShell.Commands.WebRequestSession] $Session, [Parameter(Mandatory)][string] $DefinitionId)
    $all = [System.Collections.Generic.List[object]]::new()
    $pageCounts = [System.Collections.Generic.List[int]]::new()
    $cursor = $null
    for ($pageNumber = 0; $pageNumber -lt 20; $pageNumber++) {
        $query = [ordered]@{ definitionId = $DefinitionId; take = '100' }
        if (-not [string]::IsNullOrWhiteSpace($cursor)) { $query.cursor = $cursor }
        $queryString = @($query.GetEnumerator() | ForEach-Object { '{0}={1}' -f [Uri]::EscapeDataString([string]$_.Key), [Uri]::EscapeDataString([string]$_.Value) }) -join '&'
        $page = Invoke-RestMethod "$($BaseUri)runtime/workflows/instances/page?$queryString" -WebSession $Session -TimeoutSec 10
        $pageItems = @($page.items)
        $pageCounts.Add($pageItems.Count)
        foreach ($item in $pageItems) { $all.Add($item) }
        if (-not $page.hasNext) { return [pscustomobject]@{ Items = $all.ToArray(); PageCounts = $pageCounts.ToArray() } }
        if ([string]::IsNullOrWhiteSpace([string]$page.nextCursor)) { throw 'Paged instance list reported HasNext without NextCursor.' }
        $cursor = [string]$page.nextCursor
    }
    throw 'Paged instance list exceeded the 20-page safety bound.'
}

function Get-DetailSetting {
    param([Parameter(Mandatory)] $Detail, [Parameter(Mandatory)][string] $Name)
    $field = Get-ObjectField -Value $Detail -Name $Name
    if ($field.Exists) { return $field.Value }
    $instance = Get-ObjectField -Value $Detail -Name 'instance'
    if ($instance.Exists) {
        $field = Get-ObjectField -Value $instance.Value -Name $Name
        if ($field.Exists) { return $field.Value }
    }
    return $null
}

function Get-HttpVerification {
    param(
        [Parameter(Mandatory)] $Setup,
        [Parameter(Mandatory)][Microsoft.PowerShell.Commands.WebRequestSession] $Session,
        [Parameter(Mandatory)][ValidateRange(1, 1000)][int] $ExpectedInstanceCount
    )
    $pageResult = Get-EndpointPage -Session $Session -DefinitionId $Setup.DefinitionId
    $items = @($pageResult.Items)
    $ids = @($items | ForEach-Object { [string]$_.workflowExecutionId } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $uniqueIds = @($ids | Select-Object -Unique)
    $badSummary = @($items | Where-Object { [string]$_.status -notin @('Completed', 'Finished') -or [int]$_.incidentCount -ne 0 })
    $readbackErrors = [System.Collections.Generic.List[string]]::new()
    $executionReadbacks = [System.Collections.Generic.List[object]]::new()
    $readbackWatch = [Diagnostics.Stopwatch]::StartNew()
    $readbackBudget = [TimeSpan]::FromMinutes(5)
    foreach ($executionId in $uniqueIds) {
        if ($readbackWatch.Elapsed -ge $readbackBudget) {
            $readbackErrors.Add('ReadbackBudgetExceeded')
            break
        }
        try {
            $escapedId = [Uri]::EscapeDataString([string]$executionId)
            $detail = Invoke-RestMethod "$($BaseUri)runtime/workflows/instances/$escapedId" -WebSession $Session -TimeoutSec 10
            $cadence = Get-DetailSetting -Detail $detail -Name 'checkpointCadence'
            $maxSegment = Get-DetailSetting -Detail $detail -Name 'maxSegmentCheckpoints'
            $inspection = Get-DetailSetting -Detail $detail -Name 'inspectionGranularity'
            $status = [string](Get-DetailSetting -Detail $detail -Name 'status')
            $incidentCount = Get-DetailSetting -Detail $detail -Name 'incidentCount'
            if ($null -eq $incidentCount) { $incidentCount = @($detail.incidents).Count }
            $executionReadbacks.Add([pscustomobject]@{
                workflowExecutionId = [string]$executionId
                status = $status
                incidentCount = [int]$incidentCount
                checkpointCadence = $cadence
                maxSegmentCheckpoints = $maxSegment
                inspectionGranularity = $inspection
                matchesExpectedCadence = Test-ExpectedCadence -Cadence $cadence -MaxSegment $maxSegment -Inspection $inspection
            })
        } catch { $readbackErrors.Add($_.Exception.GetType().Name) }
    }
    $badDetail = @($executionReadbacks | Where-Object {
        $_.status -notin @('Completed', 'Finished') -or $_.incidentCount -ne 0 -or -not $_.matchesExpectedCadence
    })
    $allExecutionIdsPresentAndUnique = ($items.Count -eq $ExpectedInstanceCount -and $ids.Count -eq $ExpectedInstanceCount -and $uniqueIds.Count -eq $ExpectedInstanceCount)
    $allSummariesTerminalAndIncidentFree = ($badSummary.Count -eq 0 -and $items.Count -eq $ExpectedInstanceCount)
    $allDetailsTerminalAndIncidentFree = ($executionReadbacks.Count -eq $ExpectedInstanceCount -and $badDetail.Count -eq 0)
    $effectiveSettingsMatched = ($executionReadbacks.Count -eq $ExpectedInstanceCount -and @($executionReadbacks | Where-Object { -not $_.matchesExpectedCadence }).Count -eq 0)
    [pscustomobject]@{
        endpoint = 'GET /runtime/workflows/instances/page?definitionId=...&take=100'
        expectedInstanceCount = $ExpectedInstanceCount
        instanceCount = $items.Count
        nonemptyExecutionIdCount = $ids.Count
        uniqueExecutionIdCount = $uniqueIds.Count
        duplicateExecutionIds = ($ids.Count -ne $uniqueIds.Count)
        allExecutionIdsPresentAndUnique = $allExecutionIdsPresentAndUnique
        allSummariesTerminalAndIncidentFree = $allSummariesTerminalAndIncidentFree
        nonterminalOrIncidentSummaryCount = $badSummary.Count
        pageAndPerPageCounts = @($pageResult.PageCounts)
        readbackCount = $executionReadbacks.Count
        allDetailsTerminalAndIncidentFree = $allDetailsTerminalAndIncidentFree
        executionReadbacks = $executionReadbacks.ToArray()
        readbackErrorTypes = $readbackErrors.ToArray()
        readbackBudgetSeconds = [int]$readbackBudget.TotalSeconds
        effectiveSettingsMatched = $effectiveSettingsMatched
        allHttpRunsVerified = ($allExecutionIdsPresentAndUnique -and $allSummariesTerminalAndIncidentFree -and $allDetailsTerminalAndIncidentFree -and $effectiveSettingsMatched)
    }
}

function Get-RestVerification {
    param(
        [Parameter(Mandatory)][array] $Records,
        [Parameter(Mandatory)][Microsoft.PowerShell.Commands.WebRequestSession] $Session,
        [Parameter(Mandatory)][ValidateRange(1, 1000)][int] $ExpectedInstanceCount
    )
    # This helper intentionally accepts IDs from start responses, then reads every instance after timing ends.
    $ids = @($Records | ForEach-Object { $_.workflowExecutionId } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    $uniqueIds = @($ids | Select-Object -Unique)
    $runDetails = [System.Collections.Generic.List[object]]::new()
    $errors = [System.Collections.Generic.List[string]]::new()
    $readbackWatch = [Diagnostics.Stopwatch]::StartNew()
    $readbackBudget = [TimeSpan]::FromMinutes(5)
    foreach ($executionId in $uniqueIds) {
        $detail = $null
        do {
            if ($readbackWatch.Elapsed -ge $readbackBudget) {
                $errors.Add('ReadbackBudgetExceeded')
                break
            }
            try {
                $escapedId = [Uri]::EscapeDataString([string]$executionId)
                $detail = Invoke-RestMethod "$($BaseUri)runtime/workflows/instances/$escapedId" -WebSession $Session -TimeoutSec 10
            }
            catch { $errors.Add($_.Exception.GetType().Name); break }
            $status = [string](Get-DetailSetting -Detail $detail -Name 'status')
            if ($status -in @('Completed', 'Finished', 'Faulted', 'Cancelled') -or $readbackWatch.Elapsed -ge $readbackBudget) { break }
            Start-Sleep -Milliseconds 250
        } while ($true)
        $status = if ($null -ne $detail) { [string](Get-DetailSetting -Detail $detail -Name 'status') } else { $null }
        $output = $null
        if ($null -ne $detail) {
            try { $output = Get-OutputPreview -Instance $detail -Name 'referenceText' } catch { }
        }
        $incidentCount = if ($null -ne $detail) {
            $count = Get-DetailSetting -Detail $detail -Name 'incidentCount'
            if ($null -ne $count) { [int]$count } else { @($detail.incidents).Count }
        } else { -1 }
        $cadence = if ($null -ne $detail) { Get-DetailSetting -Detail $detail -Name 'checkpointCadence' } else { $null }
        $maxSegment = if ($null -ne $detail) { Get-DetailSetting -Detail $detail -Name 'maxSegmentCheckpoints' } else { $null }
        $inspection = if ($null -ne $detail) { Get-DetailSetting -Detail $detail -Name 'inspectionGranularity' } else { $null }
        $runDetails.Add([pscustomobject]@{
            workflowExecutionId = [string]$executionId
            status = $status
            incidentCount = $incidentCount
            outputMatched = ([string]$output -ceq $ExpectedText)
            checkpointCadence = $cadence
            maxSegmentCheckpoints = $maxSegment
            inspectionGranularity = $inspection
            matchesExpectedCadence = Test-ExpectedCadence -Cadence $cadence -MaxSegment $maxSegment -Inspection $inspection
            readbackAvailable = ($null -ne $detail)
        })
    }
    $bad = @($runDetails | Where-Object {
        -not $_.readbackAvailable -or $_.status -notin @('Completed', 'Finished') -or $_.incidentCount -ne 0 -or -not $_.outputMatched -or
        -not $_.matchesExpectedCadence
    })
    if ($readbackWatch.Elapsed -ge $readbackBudget -and $runDetails.Count -lt $uniqueIds.Count -and 'ReadbackBudgetExceeded' -notin $errors) { $errors.Add('ReadbackBudgetExceeded') }
    [pscustomobject]@{
        expectedInstanceCount = $ExpectedInstanceCount
        admissionInstanceIdCount = $ids.Count
        uniqueExecutionIdCount = $uniqueIds.Count
        duplicateExecutionIds = ($ids.Count -ne $uniqueIds.Count)
        terminalCompletedAliceSmithIncidentFreeCount = @($runDetails | Where-Object { $_.status -in @('Completed','Finished') -and $_.incidentCount -eq 0 -and $_.outputMatched }).Count
        readbackCount = @($runDetails | Where-Object { $_.readbackAvailable }).Count
        admittedIdsWithoutReadback = @($runDetails | Where-Object { -not $_.readbackAvailable }).Count
        readbackErrorTypes = $errors.ToArray()
        readbackBudgetSeconds = [int]$readbackBudget.TotalSeconds
        allRunsVerified = ($bad.Count -eq 0 -and $runDetails.Count -eq $ExpectedInstanceCount -and @($runDetails | Where-Object { $_.readbackAvailable }).Count -eq $ExpectedInstanceCount -and $ids.Count -eq $ExpectedInstanceCount -and $uniqueIds.Count -eq $ExpectedInstanceCount)
        effectiveSettingsMatched = ($bad.Count -eq 0)
        runReadbacks = $runDetails.ToArray()
    }
}

function Get-TimingStatistics {
    param([Parameter(Mandatory)][array] $Samples)
    $values = @($Samples | ForEach-Object { [double]$_.elapsedMilliseconds } | Sort-Object)
    if ($values.Count -eq 0) { return $null }
    $mean = ($values | Measure-Object -Average).Average
    $squared = 0.0
    foreach ($value in $values) { $squared += [Math]::Pow(($value - $mean), 2) }
    $median = if ($values.Count % 2 -eq 0) { ($values[($values.Count / 2) - 1] + $values[$values.Count / 2]) / 2.0 } else { $values[[Math]::Floor($values.Count / 2)] }
    $p95Index = [Math]::Max(0, [Math]::Ceiling(0.95 * $values.Count) - 1)
    [pscustomobject]@{
        population = 'All measured attempts, including HTTP failures; no sample replacement or retry.'
        count = $values.Count
        medianMilliseconds = [double]$median
        p95NearestRankMilliseconds = [double]$values[$p95Index]
        minMilliseconds = [double]$values[0]
        maxMilliseconds = [double]$values[-1]
        meanMilliseconds = [double]$mean
        sampleStandardDeviationMilliseconds = if ($values.Count -gt 1) { [Math]::Sqrt($squared / ($values.Count - 1)) } else { 0.0 }
    }
}

function New-HttpFixtureSetup {
    param([Parameter(Mandatory)][string] $FixtureCase)
    $nonce = [Guid]::NewGuid().ToString('N').Substring(0, 10)
    $workflowName = "RuntimeDbTiming2386-$FixtureCase-$ExpectedCadence-$nonce"
    $route = "runtime-db-timing-2386/$($FixtureCase.ToLowerInvariant())-$nonce/transform"
    . "$PSScriptRoot/Capture-RuntimeDbPaging2392Reference.ps1" `
        -HostCandidateSha $CandidateSha -BaseUrl $BaseUrl -Username $Username -Password $Password `
        -ExpectedCadence $ExpectedCadence -WorkflowName $workflowName -RoutePath $route -SetupOnly
}

function New-SixteenNodeFixtureSetup {
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $sequence = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $httpEndpoint = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint'
    $writeResponse = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse'
    $nonce = [Guid]::NewGuid().ToString('N').Substring(0, 10)
    $workflowName = "RuntimeDbTiming2386-Http16-$ExpectedCadence-$nonce"
    $route = "runtime-db-timing-2386/http16-$nonce/transform"
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
    $sets = @()
    for ($index = 1; $index -le 13; $index++) {
        $sets += New-SetVariableNode -NodeId ("set-reference-text-{0:D2}" -f $index) -VariableKey 'referenceText' -Value $expression
    }
    $response = New-ActivityNode -NodeId 'write-response' -VersionId $writeResponse -Inputs @(
        (New-LiteralInput -ReferenceKey 'StatusCode' -Value 200),
        @{ referenceKey = 'Body'; value = @{ value = "getVariable('referenceText')"; expressionType = 'JavaScript' }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null },
        (New-LiteralInput -ReferenceKey 'ContentType' -Value 'text/plain')
    )
    $children = @($httpNode) + $sets + @($response)
    if ($children.Count -ne 15) { throw "16-node fixture must have exactly 15 child nodes; got $($children.Count)." }
    $root = New-ActivityNode -NodeId 'root' -VersionId $sequence -Structure (New-SequenceStructure -Activities $children)
    $variables = @(
        (New-VariableDef -Key 'request' -Alias 'Object'),
        (New-VariableDef -Key 'route' -Alias 'Object'),
        (New-VariableDef -Key 'content' -Alias 'Object'),
        (New-VariableDef -Key 'referenceText' -Alias 'String')
    )
    $definition = Submit-Workflow -Ctx $ctx -Name $workflowName -Description 'Bounded T02 sixteen-node HTTP reference: thirteen sequential Set intrinsics write the same referenceText expression.' -RootActivity $root -Variables $variables
    $published = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    $export = Invoke-RestMethod "$BaseUrl/publishing/workflows/$($definition.version.id)/executable-export" -WebSession $ctx.Session -TimeoutSec 30
    Start-Sleep -Seconds 1 # Give the published trigger table one refresh interval.
    [pscustomobject]@{
        DefinitionId = [string]$definition.definition.id
        VersionId = [string]$definition.version.id
        ArtifactId = [string]$published.artifactId
        SourceReferenceId = [string]$published.sourceReferenceId
        WorkflowName = $workflowName
        RoutePath = $route
        ExpectedCadence = $ExpectedCadence
        CandidateSourceSha = $CandidateSha.ToLowerInvariant()
        Context = $ctx
        ExecutableExport = $export
        AuthoredNodeCount = 16
        AuthoredSetCount = 13
    }
}

function New-RestCompanionFixtureSetup {
    . "$PSScriptRoot/Capture-RuntimeDbRest2386Control.ps1" `
        -BaseUrl $BaseUrl -HostCandidateSha $CandidateSha -Username $Username -Password $Password `
        -CompanionOnly -SetupOnly
}

$setup = $null
$profileEvidence = $null
$httpClient = $null
$httpClients = @()
$preflightRecord = $null
$warmupRecords = [System.Collections.Generic.List[object]]::new()
$measuredRecords = [System.Collections.Generic.List[object]]::new()
$batchStart = $null
$batchEnd = $null
$loadBefore = $null
$loadAfter = $null
$terminalVerification = $null
$fixtureKind = if ($Case -eq 'RestCompanionCoalesced') { 'rest' } else { 'http' }

if ($Case -eq 'Http4Sequential' -or $Case -eq 'Http4Concurrency4') {
    $setup = New-HttpFixtureSetup -FixtureCase $Case
    $profileEvidence = Get-ExecutableExportEvidence -Export $setup.ExecutableExport -ExpectedSetCount 1
} elseif ($Case -eq 'Http16Sequential') {
    $setup = New-SixteenNodeFixtureSetup
    $profileEvidence = Get-ExecutableExportEvidence -Export $setup.ExecutableExport -ExpectedSetCount 13
    if ($setup.AuthoredNodeCount -ne 16 -or $setup.AuthoredSetCount -ne 13) { throw '16-node authoring shape did not meet the exact node count.' }
} else {
    $setup = New-RestCompanionFixtureSetup
    $profileEvidence = Get-ExecutableExportEvidence -Export $setup.ExecutableExport -ExpectedSetCount 2
}
if ($null -eq $setup -or [string]::IsNullOrWhiteSpace([string]$setup.DefinitionId) -or [string]::IsNullOrWhiteSpace([string]$setup.ArtifactId)) {
    throw 'Fixture setup did not return the definition/artifact identity needed for the bounded capture.'
}
$exportExpectedSetCount = switch ($Case) { 'Http4Sequential' { 1 }; 'Http4Concurrency4' { 1 }; 'Http16Sequential' { 13 }; 'RestCompanionCoalesced' { 2 } }
$exportExpectedNodeCount = if ($Case -eq 'Http16Sequential') { 16 } else { 4 }
if ($profileEvidence.setIntrinsicCount -ne $exportExpectedSetCount) { throw 'Pinned executable export Set count did not match the selected fixture.' }
if ($profileEvidence.exportedNodeCount -ne $exportExpectedNodeCount) { throw "Pinned executable export had $($profileEvidence.exportedNodeCount) actual nodes; expected $exportExpectedNodeCount." }
if (-not $profileEvidence.setIntrinsicContractsHaveNoValue) { throw 'One or more Set intrinsic nodes unexpectedly carried an ActivityContract.' }

$context = $setup.Context
if ($null -eq $context -or $null -eq $context.Session -or $context.Session.Cookies -isnot [System.Net.CookieContainer]) {
    throw 'Fixture helper did not return an authenticated WebSession with a CookieContainer.'
}
$cookieContainer = $context.Session.Cookies
if ($fixtureKind -eq 'http') {
    $relativeUri = "workflows/http/$($setup.RoutePath)"
    $requestJson = $PayloadJson
} else {
    $relativeUri = "runtime/workflows/executables/$($setup.ArtifactId)/execute"
    $requestJson = (@{ sourceReferenceId = $setup.SourceReferenceId; inputs = @{ content = @{ firstName = 'Alice'; lastName = 'Smith' } } } | ConvertTo-Json -Depth 10 -Compress)
}

try {
    if ($Case -eq 'Http4Concurrency4') {
        for ($index = 0; $index -lt 4; $index++) { $httpClients += New-TimingHttpClient -CookieContainer $cookieContainer }
    } else {
        $httpClient = New-TimingHttpClient -CookieContainer $cookieContainer
    }

    $preflightClient = if ($httpClient) { $httpClient } else { $httpClients[0] }
    $preflightResponse = Invoke-TimedRequest -Client $preflightClient -RelativeUri $relativeUri -JsonBody $requestJson -Index 0 -ClientIndex 1
    if ($fixtureKind -eq 'http') {
        $preflightRecord = Convert-HttpResponseRecord -Response $preflightResponse -Phase 'preflight'
    } else {
        $preflightRecord = Convert-RestResponseRecord -Response $preflightResponse -Phase 'preflight'
    }

    try {
        if ($fixtureKind -eq 'http') {
            $preflightVerification = Get-HttpVerification -Setup $setup -Session $context.Session -ExpectedInstanceCount 1
            $preflightReadbackPassed = [bool]$preflightVerification.allHttpRunsVerified
        } else {
            $preflightVerification = Get-RestVerification -Records @($preflightRecord) -Session $context.Session -ExpectedInstanceCount 1
            $preflightReadbackPassed = [bool]$preflightVerification.allRunsVerified
        }
    } catch {
        $preflightVerification = [pscustomobject]@{
            verificationErrorType = $_.Exception.GetType().Name
            expectedInstanceCount = 1
            effectiveSettingsMatched = $false
            allHttpRunsVerified = $false
            allRunsVerified = $false
        }
        $preflightReadbackPassed = $false
    }
    $preflightResponsePassed = if ($fixtureKind -eq 'http') { [bool]$preflightRecord.outputMatched } else { [bool]$preflightRecord.admissionMatched }
    if (-not $preflightResponsePassed -or -not $preflightReadbackPassed) {
        $preflightFailure = [ordered]@{
            schemaVersion = 1
            captureKind = 'manual-low-logging-timing-fixture'
            case = $Case
            source = [ordered]@{ candidateSha = $CandidateSha.ToLowerInvariant(); fixtureWorktreeHead = $FixtureHead; timingScriptSha256 = $FixtureScriptSha256 }
            fixture = [ordered]@{
                kind = $fixtureKind
                workflowName = $setup.WorkflowName
                definitionId = $setup.DefinitionId
                versionId = $setup.VersionId
                artifactId = $setup.ArtifactId
                sourceReferenceId = $setup.SourceReferenceId
                routeOrEndpoint = $relativeUri
                executableExport = $profileEvidence
            }
            protocol = [ordered]@{
                setupPreflightRequests = 1
                warmupsRequired = $WarmupCount
                measuredSamplesRequired = $MeasuredCount
                warmupsPerformed = 0
                measuredSamples = 0
                measurementStarted = $false
                retriesOrReplacementSamples = 0
                failedSamplesRetained = $true
            }
            preflight = $preflightRecord
            preflightVerification = $preflightVerification
            summary = [ordered]@{ preflightResponsePassed = $preflightResponsePassed; preflightReadbackPassed = $preflightReadbackPassed; failurePoint = 'preflight' }
        }
        Write-TimingArtifact -Artifact $preflightFailure
        throw "Preflight failed; no warm-up or measured requests were issued, and a sanitized artifact was saved to '$OutputPath'."
    }

    for ($index = 1; $index -le $WarmupCount; $index++) {
        $warmupClientIndex = if ($httpClients.Count -eq 4) { ($index - 1) % 4 } else { 0 }
        $warmupClient = if ($httpClient) { $httpClient } else { $httpClients[$warmupClientIndex] }
        $warmupResponse = Invoke-TimedRequest -Client $warmupClient -RelativeUri $relativeUri -JsonBody $requestJson -Index $index -ClientIndex ($warmupClientIndex + 1)
        if ($fixtureKind -eq 'http') {
            $warmupRecords.Add((Convert-HttpResponseRecord -Response $warmupResponse -Phase 'warmup'))
        } else {
            $record = Convert-RestResponseRecord -Response $warmupResponse -Phase 'warmup'
            $warmupRecords.Add($record)
        }
    }

    $loadBefore = Get-HostSnapshot
    $batchStart = [DateTime]::UtcNow.ToString('o')
    if ($Case -eq 'Http4Concurrency4') {
        $responses = [ElsaRuntimeDbTiming2386.HttpSampler]::RunFourClientsAsync($httpClients, $relativeUri, $requestJson, 15).GetAwaiter().GetResult()
        foreach ($response in $responses) { $measuredRecords.Add((Convert-HttpResponseRecord -Response $response -Phase 'measured')) }
    } else {
        for ($index = 1; $index -le $MeasuredCount; $index++) {
            $response = Invoke-TimedRequest -Client $httpClient -RelativeUri $relativeUri -JsonBody $requestJson -Index $index
            if ($fixtureKind -eq 'http') {
                $measuredRecords.Add((Convert-HttpResponseRecord -Response $response -Phase 'measured'))
            } else {
                $record = Convert-RestResponseRecord -Response $response -Phase 'measured'
                $measuredRecords.Add($record)
            }
        }
    }
    $batchEnd = [DateTime]::UtcNow.ToString('o')
    $loadAfter = Get-HostSnapshot
} finally {
    foreach ($client in $httpClients) { if ($null -ne $client) { $client.Dispose() } }
    if ($null -ne $httpClient) { $httpClient.Dispose() }
}

if ($measuredRecords.Count -ne $MeasuredCount) {
    throw "Capture produced $($measuredRecords.Count) measured records; the fixed protocol requires exactly $MeasuredCount."
}
try {
    if ($fixtureKind -eq 'http') {
        $terminalVerification = Get-HttpVerification -Setup $setup -Session $context.Session -ExpectedInstanceCount $InstanceCountExpected
    } else {
        $terminalVerification = Get-RestVerification -Records (@($preflightRecord) + $warmupRecords.ToArray() + $measuredRecords.ToArray()) -Session $context.Session -ExpectedInstanceCount $InstanceCountExpected
    }
} catch {
    $terminalVerification = [pscustomobject]@{
        verificationErrorType = $_.Exception.GetType().Name
        effectiveSettingsMatched = $false
        allSummariesTerminalAndIncidentFree = $false
        allHttpRunsVerified = $false
        allRunsVerified = $false
        executionReadbacks = @()
        runReadbacks = @()
    }
}

$successCount = if ($fixtureKind -eq 'http') {
    @($measuredRecords | Where-Object { $_.statusCode -eq 200 -and $_.outputMatched }).Count
} else {
    @($measuredRecords | Where-Object { $_.statusCode -eq 200 -and $_.admissionMatched }).Count
}
$failureCount = $MeasuredCount - $successCount
$timingCaseName = switch ($Case) {
    'Http4Sequential' { "4-node HTTP sequential $ExpectedCadence" }
    'Http16Sequential' { "16-node HTTP sequential $ExpectedCadence" }
    'Http4Concurrency4' { "4-node HTTP concurrency 4 $ExpectedCadence; 60 total" }
    'RestCompanionCoalesced' { 'ordinary REST companion execute admission Coalesced' }
}
$cadenceReadbacks = if ($fixtureKind -eq 'http') { @($terminalVerification.executionReadbacks) } else { @($terminalVerification.runReadbacks) }
$settingsMatch = [bool]$terminalVerification.effectiveSettingsMatched
$bodyMatchesAll = if ($fixtureKind -eq 'http') {
    $preflightRecord.outputMatched -and @($warmupRecords | Where-Object { -not $_.outputMatched }).Count -eq 0 -and @($measuredRecords | Where-Object { -not $_.outputMatched }).Count -eq 0
} else {
    $preflightRecord.admissionMatched -and @($warmupRecords | Where-Object { -not $_.admissionMatched }).Count -eq 0 -and @($measuredRecords | Where-Object { -not $_.admissionMatched }).Count -eq 0
}
$warmupClientDistribution = if ($Case -eq 'Http4Concurrency4') {
    @(1..4 | ForEach-Object {
        $clientNumber = $_
        [pscustomobject]@{ clientIndex = $clientNumber; warmupCount = @($warmupRecords | Where-Object { $_.clientIndex -eq $clientNumber }).Count }
    })
} else {
    @([pscustomobject]@{ clientIndex = 1; warmupCount = $warmupRecords.Count })
}

$result = [ordered]@{
    schemaVersion = 1
    captureKind = 'manual-low-logging-timing-fixture'
    case = $Case
    caseDescription = $timingCaseName
    source = [ordered]@{
        candidateSha = $CandidateSha.ToLowerInvariant()
        fixtureWorktreeHead = $FixtureHead
        timingScriptSha256 = $FixtureScriptSha256
    }
    fixture = [ordered]@{
        kind = $fixtureKind
        workflowName = $setup.WorkflowName
        definitionId = $setup.DefinitionId
        versionId = $setup.VersionId
        artifactId = $setup.ArtifactId
        sourceReferenceId = $setup.SourceReferenceId
        routeOrEndpoint = $relativeUri
        expectedHttpResponse = if ($fixtureKind -eq 'http') { 'HTTP 200 text body Alice Smith' } else { 'HTTP 200 execute admission with workflowExecutionId; terminal/output readback is separate' }
        authoredNodeCount = if ($setup.AuthoredNodeCount) { $setup.AuthoredNodeCount } elseif ($fixtureKind -eq 'http') { 4 } else { 4 }
        authoredSetCount = if ($setup.AuthoredSetCount) { $setup.AuthoredSetCount } elseif ($fixtureKind -eq 'http') { 1 } else { 2 }
        executableExport = $profileEvidence
        workflowIntrinsicFusion = [ordered]@{
            sourceSha = $CandidateSha.ToLowerInvariant()
            sourcePath = $fusionPath
            isFusableSet = $setFusable
            meaning = 'Source classifies Set as fusable; this does not remove it from the exported/authored node count.'
        }
    }
    settings = [ordered]@{
        expectedCadence = $ExpectedCadence
        expectedMaxSegmentCheckpoints = $ExpectedMaxSegment
        expectedInspectionGranularity = $ExpectedInspection
        effectiveSettingsReadback = $cadenceReadbacks
        effectiveSettingsMatched = $settingsMatch
        expectedEfCoreLogLevel = 'Warning'
        hostLoggingLevelVerifiedByScript = $false
        persistedDiagnosticsEnabled = $PersistedDiagnosticsEnabled
        persistedDiagnosticsChangedByScript = $false
    }
    protocol = [ordered]@{
        timingBoundary = 'Monotonic Stopwatch starts before HttpClient.SendAsync and stops after complete response body read.'
        excludes = @('authentication', 'fixture setup/publication/export', 'request JSON and StringContent preparation', 'preflight', 'warm-ups', 'readbacks', 'terminal settlement')
        setupPreflightRequests = 1
        warmups = $WarmupCount
        measuredSamples = $MeasuredCount
        concurrentClients = if ($Case -eq 'Http4Concurrency4') { 4 } else { 1 }
        warmupDistribution = $warmupClientDistribution
        warmupDistributionPolicy = if ($Case -eq 'Http4Concurrency4') { 'round-robin across four clients' } else { 'single client' }
        measuredRequestsPerConcurrentClient = if ($Case -eq 'Http4Concurrency4') { 15 } else { 60 }
        expectedTotalInstancesIncludingPreflight = $InstanceCountExpected
        retriesOrReplacementSamples = 0
        failedSamplesRetained = $true
        timer = 'System.Diagnostics.Stopwatch; monotonic per request; full body buffered and read.'
        lowLoggingScope = 'Owner-started host must use Microsoft.EntityFrameworkCore=Warning and no command/transaction Debug override; this script does not configure or verify the setting.'
        diagnosticsNote = 'Persisted diagnostics remain enabled/unchanged by this harness unless the owner explicitly records otherwise; no diagnostics setting endpoint is written.'
    }
    environment = [ordered]@{
        startBeforeMeasuredBatch = $loadBefore
        endAfterMeasuredBatch = $loadAfter
    }
    timestampsUtc = [ordered]@{ measuredBatchStart = $batchStart; measuredBatchEnd = $batchEnd }
    preflight = $preflightRecord
    warmups = $warmupRecords.ToArray()
    measured = $measuredRecords.ToArray()
    measuredStatistics = Get-TimingStatistics -Samples $measuredRecords.ToArray()
    summary = [ordered]@{
        measuredSamples = $measuredRecords.Count
        successfulMeasuredResponses = $successCount
        failedMeasuredResponses = $failureCount
        everySetupWarmupAndMeasuredResponseMatched = $bodyMatchesAll
        terminalAndIncidentVerification = $terminalVerification
    }
}

Write-TimingArtifact -Artifact $result
Write-Host ("[timing-result] case={0}; cadence={1}; samples={2}; successes={3}; failures={4}; p95-nearest-rank-ms={5}; output={6}" -f `
    $timingCaseName, $ExpectedCadence, $measuredRecords.Count, $successCount, $failureCount, $result.measuredStatistics.p95NearestRankMilliseconds, $OutputPath)

$verificationPassed = if ($fixtureKind -eq 'http') { [bool]$terminalVerification.allHttpRunsVerified } else { [bool]$terminalVerification.allRunsVerified }
if (-not $bodyMatchesAll -or -not $settingsMatch -or -not $verificationPassed) {
    throw "Capture and verification completed but acceptance checks failed; all measured samples were retained at '$OutputPath'."
}
