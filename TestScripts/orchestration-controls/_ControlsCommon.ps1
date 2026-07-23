<#
.SYNOPSIS
    Shared helpers for the orchestration-controls test scripts (suspend/resume, stimuli, triggers, terminate).
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds the runtime-control primitives these tests need: the Event stimulus
    hash, stimulus dispatch, mid-flow Event (wait) node authoring, the Finish intrinsic node, and a
    suspend-detection helper.

    Focus of this suite: RUNTIME pause/resume, bookmarks, and triggers/stimuli. It deliberately does not cover
    Cancel or Retry/redrive: external cancel is expected to live in the Alteration API (its own suite later),
    and `dispatches/{id}/redrive` re-drives a DispatchWorkflow parent/child dispatch (child-workflow recovery),
    not a stimulus resume/bookmark.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

# StimulusHash = "sha256:" + lowercase-hex(SHA256(utf8(eventName))) - must match Elsa's EventStimulus.Hash.
function Get-EventStimulusHash {
    param([Parameter(Mandatory)][string] $Name)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hex = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Name)) | ForEach-Object { $_.ToString('x2') }) -join ''
    return "sha256:$hex"
}

# Publish an Event stimulus. Mode: StartOnly | ResumeOnly | StartAndResume (defaults to server default when null).
function Publish-EventStimulus {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $EventName,
        [string] $Mode = "StartAndResume",
        [string] $CorrelationId = $null
    )
    $body = @{ stimulusType = "Event"; stimulusHash = (Get-EventStimulusHash $EventName); mode = $Mode }
    if (-not [string]::IsNullOrEmpty($CorrelationId)) { $body.correlationId = $CorrelationId }
    Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/stimuli" -Method Post -WebSession $Ctx.Session -ContentType 'application/json' -Body ($body | ConvertTo-Json)
}

# A mid-flow Event (wait) node: CanStartWorkflow=false makes it a catch that suspends until the named event.
function New-EventWaitNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $EventVersionId, [Parameter(Mandatory)][string] $EventName, [string] $CorrelationId = $null)
    $inputs = @(
        (New-LiteralInput -ReferenceKey "EventName" -Value $EventName),
        (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $false)
    )
    if (-not [string]::IsNullOrEmpty($CorrelationId)) { $inputs += (New-LiteralInput -ReferenceKey "CorrelationId" -Value $CorrelationId) }
    New-ActivityNode -NodeId $NodeId -VersionId $EventVersionId -Inputs $inputs
}

# The Finish intrinsic node: terminates the whole workflow with a literal outcome (elsa.intrinsic.finish@1).
function New-FinishNode {
    param([Parameter(Mandatory)][string] $NodeId, [string] $Outcome = "done")
    @{
        nodeId            = $NodeId
        activityVersionId = "elsa.intrinsic.finish@1"
        outputs           = @()
        intrinsic         = @{ kind = "finish" }
        inputs            = @( @{ referenceKey = "outcome"; value = @{ value = $Outcome; expressionType = "Literal" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null } )
    }
}

# True when the named activity node reached a Suspended state on the instance.
function Test-ActivitySuspended {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $NodeId)
    [bool]($Instance.activities | Where-Object { $_.executableNodeId -eq $NodeId -and $_.status -eq 'Suspended' })
}
