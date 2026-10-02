<#
.SYNOPSIS
    Shared helpers for the durability test scripts (checkpoint survival, restart recovery, variable persistence).
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds a mid-flow Event wait node + a ResumeOnly stimulus dispatcher (resume
    delivery needs a non-empty input, #1014).

    Server process lifecycle (start / stop / restart of a server the script owns) lives in ../_ServerLifecycle.ps1.
    This file deliberately has none: it used to stop whatever process listened on a port (#2329).
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

# StimulusHash = "sha256:" + lowercase-hex(SHA256(utf8(eventName))).
function Get-EventHash([string] $Name) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hex = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Name)) | ForEach-Object { $_.ToString('x2') }) -join ''
    return "sha256:$hex"
}

# Resume every waiter on the named event. A non-empty input is REQUIRED for delivery (#1014).
function Invoke-ResumeStimulus {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $EventName, $InputObject = @{ resumed = $true })
    $body = @{ stimulusType = "Event"; stimulusHash = (Get-EventHash $EventName); mode = "ResumeOnly"; input = $InputObject } | ConvertTo-Json
    Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/stimuli" -Method Post -WebSession $Ctx.Session -ContentType 'application/json' -Body $body
}

# A mid-flow Event (wait) node: CanStartWorkflow=false suspends until the named event.
function New-EventWaitNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $EventVersionId, [Parameter(Mandatory)][string] $EventName)
    New-ActivityNode -NodeId $NodeId -VersionId $EventVersionId -Inputs @(
        (New-LiteralInput -ReferenceKey "EventName" -Value $EventName),
        (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $false)
    )
}

# True if the named node reached Suspended.
function Test-NodeSuspended {
    param([Parameter(Mandatory)] $Instance, [string] $NodeId = 'wait')
    [bool]($Instance.activities | Where-Object { $_.executableNodeId -eq $NodeId -and $_.status -eq 'Suspended' })
}
