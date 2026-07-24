<#
.SYNOPSIS
    Shared helpers for the stimulus-dispatch test scripts (POST runtime/workflows/stimuli).
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds Event node builders + a full-surface stimulus dispatcher.

    The dispatch contract (DispatchStimulus) is: { stimulusType, stimulusHash, input?, correlationId?, mode?,
    idempotencyKey? }. Response: { startedCount, skippedStartCount, resumedCount, starts[], resumes[] } where
    each start is { triggerBindingId, artifactId, status, workflowExecutionId } (workflowExecutionId is null when
    status="SkippedDuplicate") and each resume is { workflowExecutionId, status, reason }.

    Correlation semantics: a ResumeOnly/StartAndResume stimulus with correlationId=X resumes only waiters whose
    bookmark carries runtime.correlationId==X (exact, ordinal); a null correlationId resumes ALL matching-hash
    waiters (fan-in). That bookmark correlation is sourced from the mid-flow Event activity's CorrelationId input
    (NOT the instance identity set by set-correlation-id). Resume delivery requires a non-empty input (#1014).
    Idempotency: a non-null idempotencyKey dedups the START path per artifact (duplicate -> skippedStartCount).
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

# StimulusHash = "sha256:" + lowercase-hex(SHA256(utf8(eventName))) - matches Elsa's EventStimulus.Hash.
function Get-EventHash([string] $Name) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hex = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Name)) | ForEach-Object { $_.ToString('x2') }) -join ''
    return "sha256:$hex"
}

# Dispatch an Event stimulus with the full request surface. Returns the raw response (startedCount/resumedCount/...).
function Invoke-Stimulus {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $EventName,
        [string] $Mode = "StartAndResume",
        $InputObject = $null,
        $CorrelationId = $null,
        $IdempotencyKey = $null
    )
    $body = @{ stimulusType = "Event"; stimulusHash = (Get-EventHash $EventName); mode = $Mode }
    if ($null -ne $InputObject) { $body.input = $InputObject }
    if (-not [string]::IsNullOrEmpty($CorrelationId)) { $body.correlationId = $CorrelationId }
    if (-not [string]::IsNullOrEmpty($IdempotencyKey)) { $body.idempotencyKey = $IdempotencyKey }
    Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/stimuli" -Method Post -WebSession $Ctx.Session -ContentType 'application/json' -Body ($body | ConvertTo-Json)
}

# A mid-flow Event (wait) node: CanStartWorkflow=false suspends until the named event; optional bookmark correlation.
function New-EventWaitNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $EventVersionId, [Parameter(Mandatory)][string] $EventName, $CorrelationId = $null)
    $inputs = @(
        (New-LiteralInput -ReferenceKey "EventName" -Value $EventName),
        (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $false)
    )
    if (-not [string]::IsNullOrEmpty($CorrelationId)) { $inputs += (New-LiteralInput -ReferenceKey "CorrelationId" -Value $CorrelationId) }
    New-ActivityNode -NodeId $NodeId -VersionId $EventVersionId -Inputs $inputs
}

# An Event START-trigger node: CanStartWorkflow=true makes the named event start a new instance.
function New-EventStartNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $EventVersionId, [Parameter(Mandatory)][string] $EventName)
    New-ActivityNode -NodeId $NodeId -VersionId $EventVersionId -Inputs @(
        (New-LiteralInput -ReferenceKey "EventName" -Value $EventName),
        (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $true)
    )
}

# Publish a workflow that suspends at a mid-flow Event(EventName[,CorrelationId]) after a pre-wait WriteLine.
# Returns @{ Def; Pub; ExecutionId } with the instance already parked at the wait.
function New-SuspendedWaiter {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $WriteLineVersion, [Parameter(Mandatory)][string] $SequenceVersion,
          [Parameter(Mandatory)][string] $EventVersion, [Parameter(Mandatory)][string] $EventName, $CorrelationId = $null, [string] $NamePrefix = "Waiter")
    $before = New-ActivityNode -NodeId "before" -VersionId $WriteLineVersion -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "before wait") )
    $wait   = New-EventWaitNode -NodeId "wait" -EventVersionId $EventVersion -EventName $EventName -CorrelationId $CorrelationId
    $after  = New-ActivityNode -NodeId "after" -VersionId $WriteLineVersion -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "after wait") )
    $root = New-ActivityNode -NodeId "root" -VersionId $SequenceVersion -Structure (New-SequenceStructure -Activities @($before, $wait, $after))
    $def = Submit-Workflow -Ctx $Ctx -Name "$NamePrefix-$(Get-Random -Max 999999)" -Description "stimulus waiter" -RootActivity $root
    $pub = Publish-WorkflowVersion -Ctx $Ctx -VersionId $def.version.id
    $run = Invoke-Artifact -Ctx $Ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
    Start-Sleep -Milliseconds 800
    [pscustomobject]@{ Def = $def; Pub = $pub; ExecutionId = $run.workflowExecutionId }
}

# True if the named node on the instance reached Suspended.
function Test-NodeSuspended {
    param([Parameter(Mandatory)] $Instance, [string] $NodeId = 'wait')
    [bool]($Instance.activities | Where-Object { $_.executableNodeId -eq $NodeId -and $_.status -eq 'Suspended' })
}
