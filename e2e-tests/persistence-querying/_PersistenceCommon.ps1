<#
.SYNOPSIS
    Shared helpers for the persistence-querying test scripts (instance/incident query APIs).
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds thin wrappers over the read-only runtime query surface:
    `GET runtime/workflows/instances` (legacy bare array), `GET runtime/workflows/instances/page`
    (paged view with a cursor), and `GET runtime/workflows/instances/{id}/incidents`.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

function ConvertTo-QueryString {
    param([hashtable] $Query)
    if (-not $Query -or $Query.Count -eq 0) { return "" }
    ($Query.GetEnumerator() | Where-Object { $null -ne $_.Value -and $_.Value -ne "" } |
        ForEach-Object { "{0}={1}" -f $_.Key, [uri]::EscapeDataString([string]$_.Value) }) -join '&'
}

# GET runtime/workflows/instances (legacy bare-array contract). Returns an array of summary views.
function Get-InstancesRaw {
    param([Parameter(Mandatory)] $Ctx, [hashtable] $Query = @{})
    $qs = ConvertTo-QueryString $Query
    $url = "$($Ctx.BaseUrl)/runtime/workflows/instances"
    if ($qs) { $url += "?$qs" }
    $r = Invoke-RestMethod $url -WebSession $Ctx.Session -UseBasicParsing
    if ($r -is [array]) { $r } elseif ($null -ne $r.items) { $r.items } else { @($r) }
}

# GET runtime/workflows/instances/page (paged contract). Returns the raw view ({ items, nextCursor|cursor, ... }).
function Get-InstancesPage {
    param([Parameter(Mandatory)] $Ctx, [hashtable] $Query = @{})
    $qs = ConvertTo-QueryString $Query
    $url = "$($Ctx.BaseUrl)/runtime/workflows/instances/page"
    if ($qs) { $url += "?$qs" }
    Invoke-RestMethod $url -WebSession $Ctx.Session -UseBasicParsing
}

# GET runtime/workflows/instances/{id}/incidents. Response is { workflowExists, incidents:[...], count };
# returns the incidents array.
function Get-InstanceIncidents {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $ExecutionId)
    $r = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances/$ExecutionId/incidents" -WebSession $Ctx.Session -UseBasicParsing
    if ($null -ne $r.incidents) { @($r.incidents) }
    elseif ($r -is [array]) { $r }
    else { @($r) }
}

# A SetCorrelationId intrinsic node (elsa.intrinsic.set-correlation-id@1) that stamps the instance correlation id.
function New-SetCorrelationIdNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $Value)
    @{
        nodeId = $NodeId; activityVersionId = "elsa.intrinsic.set-correlation-id@1"; outputs = @()
        intrinsic = @{ kind = "setCorrelationId"; valueType = @{ alias = "String"; collectionKind = "single" } }
        inputs = @( (New-LiteralInput -ReferenceKey "value" -Value $Value) )
    }
}
