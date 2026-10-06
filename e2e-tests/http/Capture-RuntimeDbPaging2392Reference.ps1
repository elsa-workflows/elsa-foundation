<#
.SYNOPSIS
    Runs the approved, four-node #2392 HTTP reference against an already-running Workbench.
.DESCRIPTION
    This is a scoped fixture, not a host/database harness. It reuses _ElsaCommon.ps1 for authoring and inspection,
    starts the workflow through its synchronous HttpEndpoint, verifies its response and terminal instance state,
    and prints the published intrinsic classification and effective per-run cadence. Run only against the task-owned
    disposable PostgreSQL 16 Workbench described in specs/193-bounded-coalesced-pagination/quickstart.md.

    The SetVariable node is the compiler intrinsic `elsa.intrinsic.set@1` (IntrinsicKind=Set, fusable); it is not the
    historical custom CLR transform. Treat output as a representative, non-equivalent reference.
    ExpectedCadence checks per-run readback only; configure the task-owned host separately. Its default remains
    Coalesced. The workflow definition and published executable inputs are unchanged by this parameter.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string] $HostCandidateSha,
    [string] $BaseUrl = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $TraceParent = "",
    [ValidateSet("Coalesced", "Immediate")][string] $ExpectedCadence = "Coalesced",
    [string] $WorkflowName = "RuntimeDbPaging2392Reference",
    [string] $RoutePath = "runtime-db-paging-2392/transform",
    [switch] $SetupOnly
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

if ([string]::IsNullOrWhiteSpace($TraceParent)) {
    $traceBytes = New-Object byte[] 24
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($traceBytes) } finally { $random.Dispose() }
    $TraceParent = "00-{0}-{1}-01" -f `
        ([BitConverter]::ToString($traceBytes[0..15]).Replace("-", "").ToLowerInvariant()), `
        ([BitConverter]::ToString($traceBytes[16..23]).Replace("-", "").ToLowerInvariant())
}
if ($TraceParent -notmatch '^00-(?!0{32})[0-9a-fA-F]{32}-(?!0{16})[0-9a-fA-F]{16}-[0-9a-fA-F]{2}$') {
    throw "TraceParent must be a valid W3C version 00 traceparent value."
}
$TraceParent = $TraceParent.ToLowerInvariant()

$workflowName = $WorkflowName
$path = $RoutePath
$payload = '{"firstName":"Alice","lastName":"Smith"}'

Write-Host "== #2392 $ExpectedCadence HTTP reference (non-equivalent) == -> $BaseUrl" -ForegroundColor Cyan
Write-Host ("[scriptHead] {0}" -f (git -C (Join-Path $PSScriptRoot '../..') rev-parse HEAD).Trim())
Write-Host ("[hostCandidateInput] {0} (operator supplied; verify against the independent host build record)" -f $HostCandidateSha.ToLowerInvariant())
Write-Host ("[traceparent] {0}" -f $TraceParent)

$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$sequence = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$httpEndpoint = Invoke-Step "resolve HttpEndpoint" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.HttpEndpoint' }
$writeResponse = Invoke-Step "resolve WriteHttpResponse" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.WriteHttpResponse' }

$httpNode = New-ActivityNode -NodeId "http-in" -VersionId $httpEndpoint -Inputs @(
    (New-LiteralInput -ReferenceKey "Path" -Value $path),
    (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $true),
    (New-LiteralInput -ReferenceKey "SupportedMethods" -Value @("POST")),
    (New-LiteralInput -ReferenceKey "ResponseMode" -Value "Sync")
) -Outputs @(
    @{ referenceKey = "Request"; value = @{ value = @{ referenceKey = "request" }; expressionType = "Variable" } },
    @{ referenceKey = "RouteData"; value = @{ value = @{ referenceKey = "route" }; expressionType = "Variable" } },
    @{ referenceKey = "ParsedContent"; value = @{ value = @{ referenceKey = "content" }; expressionType = "Variable" } }
)
$setText = New-SetVariableNode -NodeId "set-reference-text" -VariableKey "referenceText" `
    -Value @{ value = "getVariable('content').firstName + ' ' + getVariable('content').lastName"; expressionType = "JavaScript" }
$response = New-ActivityNode -NodeId "write-response" -VersionId $writeResponse -Inputs @(
    (New-LiteralInput -ReferenceKey "StatusCode" -Value 200),
    @{ referenceKey = "Body"; value = @{ value = "getVariable('referenceText')"; expressionType = "JavaScript" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null },
    (New-LiteralInput -ReferenceKey "ContentType" -Value "text/plain")
)
$root = New-ActivityNode -NodeId "root" -VersionId $sequence -Structure (New-SequenceStructure -Activities @($httpNode, $setText, $response))
$variables = @(
    (New-VariableDef -Key "request" -Alias "Object"),
    (New-VariableDef -Key "route" -Alias "Object"),
    (New-VariableDef -Key "content" -Alias "Object"),
    (New-VariableDef -Key "referenceText" -Alias "String")
)

$definition = Invoke-Step "submit reference workflow" {
    Submit-Workflow -Ctx $ctx -Name $workflowName `
        -Description "Task #2392 current-head $ExpectedCadence HTTP reference; non-equivalent to historical custom transform." `
        -RootActivity $root -Variables $variables
}
$publication = Invoke-Step "publish reference workflow" {
    Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
}
Write-Host ("[published]  version={0} artifact={1}" -f $definition.version.id, $publication.artifactId)

# Inspect the exact published executable rather than inferring profile/fusion information from the authoring node.
$export = Invoke-Step "read published executable export (workflow-publishing.read)" {
    Invoke-RestMethod "$BaseUrl/publishing/workflows/$($definition.version.id)/executable-export" -WebSession $ctx.Session
}
function Find-SetIntrinsic($value) {
    if ($value -is [System.Collections.IDictionary]) {
        if ($value.Contains("IntrinsicKind") -and [string]$value["IntrinsicKind"] -eq "Set") { return $value }
        foreach ($child in $value.Values) {
            $found = Find-SetIntrinsic $child
            if ($null -ne $found) { return $found }
        }
    } elseif ($null -ne $value -and $value -isnot [string] -and $null -ne $value.PSObject.Properties["IntrinsicKind"] -and
        [string]$value.PSObject.Properties["IntrinsicKind"].Value -eq "Set") {
        return $value
    } elseif ($null -ne $value -and $value -is [pscustomobject]) {
        foreach ($property in $value.PSObject.Properties) {
            $found = Find-SetIntrinsic $property.Value
            if ($null -ne $found) { return $found }
        }
    } elseif ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
        foreach ($child in $value) {
            $found = Find-SetIntrinsic $child
            if ($null -ne $found) { return $found }
        }
    }
    return $null
}
$publishedSet = Find-SetIntrinsic $export
if ($null -eq $publishedSet) { throw 'Published executable export did not contain intrinsicKind="Set".' }
$contractProperty = $null
if ($publishedSet -is [System.Collections.IDictionary]) {
    $contractKey = @($publishedSet.Keys | Where-Object { [string]$_ -ieq "activityContract" } | Select-Object -First 1)
    if ($contractKey.Count -gt 0) { $contractProperty = [pscustomobject]@{ Exists = $true; Value = $publishedSet[$contractKey[0]] } }
} else {
    $contractValue = @($publishedSet.PSObject.Properties | Where-Object { $_.Name -ieq "activityContract" } | Select-Object -First 1)
    if ($contractValue.Count -gt 0) { $contractProperty = [pscustomobject]@{ Exists = $true; Value = $contractValue[0].Value } }
}
if ($null -ne $contractProperty -and $null -ne $contractProperty.Value) {
    throw "Published Set intrinsic must have a null ActivityContract; the JSON export may omit that null property."
}
Write-Host ("[executable] {0}" -f ($publishedSet | ConvertTo-Json -Compress -Depth 12))
Write-Host '[wire]       intrinsicKind="Set"; activityContract may be omitted or null (JsonPayloadSerializer uses WhenWritingNull; compiler in-memory ActivityContract is separately null).'
Write-Host "[fusion]     WorkflowIntrinsicFusion.IsFusable(Set)=true (source verified at this candidate)."

# The management trigger index refresh is bounded to the same one interval for both setup-only and diagnostic flows.
Start-Sleep -Seconds 1 # Give the published trigger table one refresh interval.

if ($SetupOnly) {
    return [pscustomobject]@{
        DefinitionId       = [string]$definition.definition.id
        VersionId          = [string]$definition.version.id
        ArtifactId         = [string]$publication.artifactId
        SourceReferenceId  = [string]$publication.sourceReferenceId
        WorkflowName       = $workflowName
        RoutePath          = $path
        ExpectedCadence    = $ExpectedCadence
        CandidateSourceSha = $HostCandidateSha.ToLowerInvariant()
        Context            = $ctx
        ExecutableExport   = $export
    }
}

# Content-addressed artifacts can be shared across authored source definitions. Filter on this run's unique source
# definition and reject ambiguity instead of selecting an unrelated older instance.
# PowerShell persists explicit request headers on a reused WebSession. Keep the trigger's traceparent
# on a separate authenticated session so instance inspection cannot join the trigger trace.
$triggerSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$triggerSession.Cookies = $ctx.Session.Cookies
$trigger = Invoke-Step "POST synchronous HTTP trigger" {
    Invoke-WebRequest "$BaseUrl/workflows/http/$path" -Method POST -Body $payload -ContentType "application/json" `
        -Headers @{ traceparent = $TraceParent } -WebSession $triggerSession -UseBasicParsing
}
Write-Host ("[response]   HTTP {0}; body={1}" -f [int]$trigger.StatusCode, "$($trigger.Content)")
if ([int]$trigger.StatusCode -ne 200 -or "$($trigger.Content)" -ne "Alice Smith") {
    throw "Reference workflow did not return HTTP 200 with body 'Alice Smith'."
}

$list = Invoke-Step "find instance by source definition" {
    Invoke-RestMethod "$BaseUrl/runtime/workflows/instances?definitionId=$($definition.definition.id)" -WebSession $ctx.Session
}
$instances = if ($list.items) { @($list.items) } elseif ($list -is [array]) { @($list) } else { @() }
$executionIds = @($instances | ForEach-Object { $_.workflowExecutionId } | Where-Object { $_ })
if ($executionIds.Count -ne 1) {
    throw "Expected exactly one instance for source definition '$($definition.definition.id)', got $($executionIds.Count)."
}
$executionId = $executionIds[0]
Write-Host ("[instance]   workflowExecutionId={0}" -f $executionId)

$detail = Invoke-Step "read per-run instance detail (workflow-runtime.read)" {
    Wait-WorkflowInstance -Ctx $ctx -ExecutionId $executionId -TimeoutSeconds 30
}
if ($detail.instance.status -notin @("Completed", "Finished")) {
    Show-WorkflowInstance -Instance $detail
    throw "Reference workflow reached nonterminal or unsuccessful status '$($detail.instance.status)'."
}
function Get-DetailValue($detailValue, [string] $name) {
    $value = $detailValue.$name
    if ($null -eq $value) { $value = $detailValue.instance.$name }
    return $value
}
$cadence = Get-DetailValue $detail "checkpointCadence"
$maxSegment = Get-DetailValue $detail "maxSegmentCheckpoints"
$inspection = Get-DetailValue $detail "inspectionGranularity"
Write-Host ("[terminal]   status={0}" -f $detail.instance.status)
Write-Host ("[effective]  checkpointCadence={0}; maxSegmentCheckpoints={1}; inspectionGranularity={2}" -f $cadence, $maxSegment, $inspection)
$expectedMaxSegment = if ($ExpectedCadence -eq "Coalesced") { 50 } else { $null }
$expectedInspection = if ($ExpectedCadence -eq "Coalesced") { "boundary-level" } else { "activity-level" }
$expectedMaxSegmentText = if ($null -eq $expectedMaxSegment) { "<null>" } else { [string]$expectedMaxSegment }
if ("$cadence" -ne $ExpectedCadence -or $maxSegment -ne $expectedMaxSegment -or "$inspection" -ne $expectedInspection) {
    throw "Expected effective checkpointCadence=$ExpectedCadence, maxSegmentCheckpoints=$expectedMaxSegmentText, and inspectionGranularity=$expectedInspection; got cadence='$cadence', maxSegmentCheckpoints='$maxSegment', inspectionGranularity='$inspection'."
}
