<#
.SYNOPSIS
    Shared helpers for the fault-handling test scripts.
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds the `Fault` activity node (the deterministic way to fault a
    workflow) and an incidents fetch. Foundation has no incident-strategy layer, so every fault currently
    parks as a `Blocking` / `WaitForIntervention` incident and the workflow ends `Faulted` (see issue #1015).
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

# A Fault activity node (Elsa.Activities.Primitives.Activities.Fault) - faults the workflow with a message.
function New-FaultNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $FaultVersionId, [string] $Message = "faulted by test")
    New-ActivityNode -NodeId $NodeId -VersionId $FaultVersionId -Inputs @( (New-LiteralInput -ReferenceKey "message" -Value $Message) )
}

# The Finish intrinsic node: ends the workflow COMPLETED (contrast with Fault, which ends it Faulted).
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

# GET runtime/workflows/instances/{id}/incidents -> { workflowExists, incidents:[...], count }; returns the array.
function Get-InstanceIncidents {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $ExecutionId)
    $r = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances/$ExecutionId/incidents" -WebSession $Ctx.Session -UseBasicParsing
    if ($null -ne $r.incidents) { @($r.incidents) } elseif ($r -is [array]) { $r } else { @($r) }
}
