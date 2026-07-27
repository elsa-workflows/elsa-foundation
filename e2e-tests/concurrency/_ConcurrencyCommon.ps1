<#
.SYNOPSIS
    Shared helpers for the concurrency test scripts (Parallel fork/join under suspension and fault).
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds a mid-flow Event wait node, a ResumeOnly stimulus dispatcher (resume
    delivery needs a non-empty input, #1014), a Fault node, and incident inspection - for exercising the Parallel
    composite's non-trivial runtime behaviors (multiple concurrent bookmarks under one instance; the stateless
    fault-join decision) which the in-process C# harness (in-memory store + synchronous drain) cannot reproduce.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

function Get-EventHash([string] $Name) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    "sha256:" + (($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Name)) | ForEach-Object { $_.ToString('x2') }) -join '')
}

# Resume waiters on the named event. A non-empty input is REQUIRED for delivery (#1014).
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

# A Fault node (ends the branch/activity Faulted).
function New-FaultNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $FaultVersionId, [string] $Message = "branch boom")
    New-ActivityNode -NodeId $NodeId -VersionId $FaultVersionId -Inputs @( (New-LiteralInput -ReferenceKey "message" -Value $Message) )
}

# True if the named node reached a given status.
function Test-NodeStatus {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $Status)
    [bool]($Instance.activities | Where-Object { $_.executableNodeId -eq $NodeId -and $_.status -eq $Status })
}

# All incident messages on an instance (aggregated).
function Get-IncidentMessages {
    param([Parameter(Mandatory)] $Instance)
    @($Instance.incidents | ForEach-Object { $_.message })
}
