<#
.SYNOPSIS
    Manually capture the bounded REST-start control for runtime database accounting #2386.
.DESCRIPTION
    This file is deliberately named Capture-*.ps1 so it is not included by Test-*.ps1 suite discovery.
    It tries the selected HTTP-published artifact first, pinned by both artifact ID and sourceReferenceId.
    If that run does not produce the expected terminal referenceText, it retains the direct result and
    provisions an ordinary REST-start companion with an explicit WorkflowRequest -> SetVariable projection,
    the same referenceText SetVariable expression, and SetOutput for instance-detail visibility.

    Source audit basis: at 43b3ef51882105916380ac8e539b49ad7b8a37b6 (the relevant source/helper files are
    unchanged at this worktree's documentation head), WorkflowExecutionStartService passes request.Inputs as
    the input.* channel, separately from workflow variables; RuntimeInputBindingResolver resolves an explicit
    WorkflowRequest memberKey from that channel; HttpEndpoint.ShouldComplete completes a direct invocation
    only when CanStartWorkflow is true and otherwise registers its trigger and suspends; and
    WorkflowInstanceDetailsService exposes only SetOutput-assigned values in outputs.

    The REST companion omits HttpEndpoint admission and WriteHttpResponse transport, and adds exactly one
    input-projection SetVariable plus one SetOutput around the unchanged computation. This is a source/workload
    validity control, not a path-equivalent latency comparison. This script records no latency, EF command,
    SQL statement, transaction, or provider-roundtrip counts. Run it only against the already-owned candidate
    host and its selected published artifact; it does not start, stop, or build a host.

    Authentication uses a fresh _ElsaCommon Connect-Elsa WebSession for each traced API request. The target
    request receives its own explicit W3C traceparent. Each detail read has a fresh session and trace ID separate
    from the start request. No token, password, connection string, settings dump, or request payload is printed.
.EXAMPLE
    pwsh -NoProfile -File ./e2e-tests/http/Capture-RuntimeDbRest2386Control.ps1 `
      -BaseUrl http://127.0.0.1:53780 -HostCandidateSha <40-hex-sha> `
      -HttpArtifactId <artifact-id> -HttpSourceReferenceId <source-reference-id>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseUrl,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string] $HostCandidateSha,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $HttpArtifactId,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $HttpSourceReferenceId,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [ValidateRange(1, 30)][int] $StatusReadTimeoutSeconds = 8
)

. "$PSScriptRoot/../variables/_VarCommon.ps1"

$ErrorActionPreference = 'Stop'
$ExpectedText = 'Alice Smith'
$Content = [ordered]@{ firstName = 'Alice'; lastName = 'Smith' }
$ComputeExpression = "getVariable('content').firstName + ' ' + getVariable('content').lastName"

function New-TraceIdentity {
    $traceId = [Guid]::NewGuid().ToString('N').ToLowerInvariant()
    $spanId = [Guid]::NewGuid().ToString('N').Substring(0, 16).ToLowerInvariant()
    [pscustomobject]@{
        TraceId     = $traceId
        TraceParent = "00-$traceId-$spanId-01"
    }
}

function New-TracedContext {
    param()

    $trace = New-TraceIdentity
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    # Connect-Elsa created a newly authenticated WebSession. Attach this operation's unique parent
    # after login so the one API operation using this context is isolated under its own trace ID.
    $ctx.Session.Headers['traceparent'] = $trace.TraceParent
    [pscustomobject]@{ Context = $ctx; TraceId = $trace.TraceId }
}

function Get-HttpStatusCode {
    param([Parameter(Mandatory)] $ErrorRecord)
    $response = $ErrorRecord.Exception.Response
    if ($null -eq $response) { return 0 }
    try { return [int]$response.StatusCode } catch { return 0 }
}

function Convert-ResponseJson {
    param([AllowNull()][string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try { return ConvertFrom-Json -InputObject $Text -ErrorAction Stop } catch { return $null }
}

function Invoke-TracedJsonRequest {
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST')][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        [AllowNull()][string] $Body
    )

    $traced = New-TracedContext
    $request = @{
        Uri             = "$($BaseUrl.TrimEnd('/'))/$($Path.TrimStart('/'))"
        Method          = $Method
        WebSession      = $traced.Context.Session
        TimeoutSec      = 30
        UseBasicParsing = $true
        ErrorAction     = 'Stop'
    }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json'
        $request.Body = $Body
    }

    try {
        $response = Invoke-WebRequest @request
        [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Data       = Convert-ResponseJson -Text ([string]$response.Content)
            TraceId    = $traced.TraceId
            ErrorType  = $null
        }
    }
    catch {
        # Keep status/error classification, but never echo a response body that might repeat inputs.
        [pscustomobject]@{
            StatusCode = Get-HttpStatusCode -ErrorRecord $_
            Data       = $null
            TraceId    = $traced.TraceId
            ErrorType  = $_.Exception.GetType().Name
        }
    }
}

function Get-HttpEndpointWait {
    param([Parameter(Mandatory)] $Instance)

    foreach ($activity in @($Instance.activities)) {
        $type = [string]$activity.activityType
        $status = [string]$activity.status
        if ($type -match 'HttpEndpoint' -and $status -in @('Suspended', 'Waiting')) {
            return "node=$($activity.executableNodeId),status=$status"
        }
    }
    return $null
}

function Get-InstanceDetailBounded {
    param([Parameter(Mandatory)][string] $ExecutionId)

    $deadline = (Get-Date).AddSeconds($StatusReadTimeoutSeconds)
    $traceIds = @()
    $lastRead = $null
    do {
        if ($traceIds.Count -gt 0) { Start-Sleep -Milliseconds 500 }
        $lastRead = Invoke-TracedJsonRequest -Method GET -Path "runtime/workflows/instances/$ExecutionId" -Body $null
        $traceIds += $lastRead.TraceId
        if ($lastRead.StatusCode -eq 200 -and $null -ne $lastRead.Data) {
            $status = [string]$lastRead.Data.instance.status
            $endpointWait = Get-HttpEndpointWait -Instance $lastRead.Data
            if ($status -in @('Completed', 'Finished', 'Faulted', 'Cancelled') -or $endpointWait) { break }
        }
        elseif ($lastRead.StatusCode -ne 404) {
            break
        }
    } while ((Get-Date) -lt $deadline)

    [pscustomobject]@{
        Detail         = if ($lastRead.StatusCode -eq 200) { $lastRead.Data } else { $null }
        LastStatusCode = $lastRead.StatusCode
        TraceIds       = @($traceIds)
    }
}

function Get-OutputValueSafe {
    param([AllowNull()] $Instance, [Parameter(Mandatory)][string] $Name)
    if ($null -eq $Instance -or $null -eq $Instance.outputs) { return $null }
    Get-OutputPreview -Instance $Instance -Name $Name
}

function Format-ReportedValue {
    param([AllowNull()] $Value)
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return '<unreported>' }
    return [string]$Value
}

function Format-CadenceReadback {
    param([AllowNull()] $Detail)
    if ($null -eq $Detail) { return 'checkpointCadence=<unreported>; maxSegmentCheckpoints=<unreported>; inspectionGranularity=<unreported>' }
    'checkpointCadence={0}; maxSegmentCheckpoints={1}; inspectionGranularity={2}' -f `
        (Format-ReportedValue $Detail.checkpointCadence), `
        (Format-ReportedValue $Detail.maxSegmentCheckpoints), `
        (Format-ReportedValue $Detail.inspectionGranularity)
}

function Get-RunStatus {
    param([AllowNull()] $Detail)
    if ($null -eq $Detail) { return '<detail-unavailable>' }
    return [string]$Detail.instance.status
}

function Get-DirectFallbackReason {
    param([Parameter(Mandatory)] $Readback, [AllowNull()] $OutputValue)

    $detail = $Readback.Detail
    if ($null -eq $detail) { return "instance detail unavailable (last GET HTTP $($Readback.LastStatusCode))" }
    $endpointWait = Get-HttpEndpointWait -Instance $detail
    if ($endpointWait) { return "waiting at HttpEndpoint ($endpointWait)" }

    $status = [string]$detail.instance.status
    if ($status -eq 'Faulted') { return 'direct HTTP-shaped run faulted; it is not a successful control' }
    if ($status -in @('Completed', 'Finished')) {
        if ($null -eq $OutputValue) { return 'direct run completed without a visible referenceText output' }
        return "direct run completed with referenceText '$OutputValue', not the expected '$ExpectedText'"
    }
    return "direct run remained nonterminal with status '$status' during bounded status reads"
}

function Write-RunReadback {
    param([Parameter(Mandatory)][string] $Label, [Parameter(Mandatory)] $Readback, [AllowNull()] $OutputValue)

    $detail = $Readback.Detail
    $lastTraceId = if ($Readback.TraceIds.Count -gt 0) { $Readback.TraceIds[-1] } else { '<none>' }
    Write-Host ("[{0}-result] detailTraceId={1}; detailTraceIds={2}; detailHttpStatus={3}; executionStatus={4}; referenceText={5}; {6}" -f `
        $Label, $lastTraceId, ($Readback.TraceIds -join ','), $Readback.LastStatusCode, (Get-RunStatus $detail), `
        (Format-ReportedValue $OutputValue), (Format-CadenceReadback $detail))
}

Write-Host '== #2386 REST-start source/workload control ==' -ForegroundColor Cyan
Write-Host ("candidateSourceSha={0}; directArtifactId={1}; directSourceReferenceId={2}" -f `
    $HostCandidateSha, $HttpArtifactId, $HttpSourceReferenceId)

# First ask the selected HTTP-published artifact to start over the ordinary REST execute route.
$directBody = @{
    sourceReferenceId = $HttpSourceReferenceId
    inputs = @{ content = $Content }
} | ConvertTo-Json -Depth 10 -Compress
$directPath = "runtime/workflows/executables/$HttpArtifactId/execute"
$directRequest = Invoke-TracedJsonRequest -Method POST -Path $directPath -Body $directBody
$directExecutionId = if ($directRequest.Data) { [string]$directRequest.Data.workflowExecutionId } else { $null }
Write-Host ("[direct] executeHttpStatus={0}; traceId={1}; executionId={2}; sourceReferenceId={3}" -f `
    $directRequest.StatusCode, $directRequest.TraceId, (Format-ReportedValue $directExecutionId), $HttpSourceReferenceId)

if ($directRequest.StatusCode -lt 200 -or $directRequest.StatusCode -ge 300) {
    throw "Direct REST execute was not accepted (HTTP $($directRequest.StatusCode)); status and trace ID are retained above."
}
if ([string]::IsNullOrWhiteSpace($directExecutionId)) {
    throw "Direct REST execute returned HTTP $($directRequest.StatusCode) without a workflowExecutionId; no instance could be read."
}

$directReadback = Get-InstanceDetailBounded -ExecutionId $directExecutionId
$directOutput = Get-OutputValueSafe -Instance $directReadback.Detail -Name 'referenceText'
Write-RunReadback -Label 'direct' -Readback $directReadback -OutputValue $directOutput

$directTerminal = (Get-RunStatus $directReadback.Detail) -in @('Completed', 'Finished')
if ($directTerminal -and $directOutput -eq $ExpectedText) {
    Write-Host 'SUCCESS - direct REST start reached terminal state and exposed exact Alice Smith output.' -ForegroundColor Green
    return
}

$directReason = Get-DirectFallbackReason -Readback $directReadback -OutputValue $directOutput
Write-Host ("[direct] not accepted as the control: {0}" -f $directReason) -ForegroundColor Yellow

# Build the ordinary REST companion only after preserving the actual direct result above.
$sequenceContext = New-TracedContext
$sequenceVersionId = Get-ActivityVersionId -Ctx $sequenceContext.Context -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
$sequenceLookupTraceId = $sequenceContext.TraceId

$projectContent = New-SetVariableNode -NodeId 'project-content' -VariableKey 'content' `
    -Value @{ value = @{ memberKey = 'content' }; expressionType = 'WorkflowRequest' } -ValueAlias 'Object'
$setReferenceText = New-SetVariableNode -NodeId 'set-referenceText' -VariableKey 'referenceText' `
    -Value @{ value = $ComputeExpression; expressionType = 'JavaScript' }
$exposeReferenceText = New-SetOutputNode -NodeId 'output-referenceText' -OutputName 'referenceText' `
    -Value @{ value = 'referenceText'; expressionType = 'Variable' }
$companionRoot = New-ActivityNode -NodeId 'root' -VersionId $sequenceVersionId -Structure (
    New-SequenceStructure -Activities @($projectContent, $setReferenceText, $exposeReferenceText)
)
$companionName = 'RuntimeDb2386RestControl-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$companionVariables = @(
    (New-VariableDef -Key 'content' -Alias 'Object' -Default $null),
    (New-VariableDef -Key 'referenceText' -Alias 'String' -Default '')
)
$companionInputs = @(New-InputDef -Key 'content' -Alias 'Object')

$submitContext = New-TracedContext
$definition = Submit-Workflow -Ctx $submitContext.Context -Name $companionName `
    -Description 'Bounded #2386 REST-start control; projects input.content to content, applies the reference computation, and exposes referenceText.' `
    -RootActivity $companionRoot -Variables $companionVariables -Inputs $companionInputs
$submitTraceId = $submitContext.TraceId

$publishContext = New-TracedContext
$published = Publish-WorkflowVersion -Ctx $publishContext.Context -VersionId $definition.version.id
$publishTraceId = $publishContext.TraceId
Write-Host ("[companion] artifactId={0}; sourceReferenceId={1}; sequenceLookupTraceId={2}; submitTraceId={3}; publishTraceId={4}" -f `
    $published.artifactId, $published.sourceReferenceId, $sequenceLookupTraceId, $submitTraceId, $publishTraceId)
Write-Host '[companion-shape] omits HttpEndpoint admission and WriteHttpResponse transport; adds SetVariable(input.content -> workflow variable content) and SetOutput(referenceText).'

$companionBody = @{
    sourceReferenceId = $published.sourceReferenceId
    inputs = @{ content = $Content }
} | ConvertTo-Json -Depth 10 -Compress
$companionPath = "runtime/workflows/executables/$($published.artifactId)/execute"
$companionRequest = Invoke-TracedJsonRequest -Method POST -Path $companionPath -Body $companionBody
$companionExecutionId = if ($companionRequest.Data) { [string]$companionRequest.Data.workflowExecutionId } else { $null }
Write-Host ("[companion] executeHttpStatus={0}; traceId={1}; executionId={2}; sourceReferenceId={3}" -f `
    $companionRequest.StatusCode, $companionRequest.TraceId, (Format-ReportedValue $companionExecutionId), $published.sourceReferenceId)

if ($companionRequest.StatusCode -lt 200 -or $companionRequest.StatusCode -ge 300) {
    throw "Companion REST execute was not accepted (HTTP $($companionRequest.StatusCode)); its status and trace ID are retained above."
}
if ([string]::IsNullOrWhiteSpace($companionExecutionId)) {
    throw "Companion REST execute returned HTTP $($companionRequest.StatusCode) without a workflowExecutionId."
}

$companionReadback = Get-InstanceDetailBounded -ExecutionId $companionExecutionId
$companionOutput = Get-OutputValueSafe -Instance $companionReadback.Detail -Name 'referenceText'
Write-RunReadback -Label 'companion' -Readback $companionReadback -OutputValue $companionOutput

$companionTerminal = (Get-RunStatus $companionReadback.Detail) -in @('Completed', 'Finished')
if (-not $companionTerminal -or $companionOutput -ne $ExpectedText) {
    throw "Companion did not verify terminal Alice Smith output (status '$(Get-RunStatus $companionReadback.Detail)', output '$(Format-ReportedValue $companionOutput)')."
}

Write-Host ("SUCCESS - companion is a terminal REST-start control with referenceText='{0}'. Direct outcome remains: {1}." -f `
    $companionOutput, $directReason) -ForegroundColor Green
