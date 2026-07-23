<#
.SYNOPSIS
    Shared helpers for the variables / value-conversion test scripts.
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds a collection-typed variable definition and a collection Set node
    (the base New-VariableDef / New-SetVariableNode helpers only produce single-valued shapes).
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

# A workflow variable declared with an explicit collection kind ("single" or "collection").
function New-TypedVariableDef {
    param([Parameter(Mandatory)][string] $Key, [string] $Alias = "String", [string] $CollectionKind = "single", $Default = $null)
    @{ referenceKey = $Key; name = $Key; type = @{ alias = $Alias; collectionKind = $CollectionKind }; storageDriverType = $null; default = @{ value = $Default; expressionType = "Literal" } }
}

# A Set intrinsic node that targets a collection-typed variable (valueType.collectionKind = "collection").
function New-SetCollectionNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $VariableKey, [Parameter(Mandatory)] $Value, [string] $Alias = "Int32")
    $binding = if ($Value -is [hashtable]) { $Value } else { @{ value = $Value; expressionType = "Literal" } }
    @{
        nodeId = $NodeId; activityVersionId = "elsa.intrinsic.set@1"; outputs = @()
        intrinsic = @{ kind = "set"; valueType = @{ alias = $Alias; collectionKind = "Array" }; variable = @{ referenceKey = $VariableKey } }
        inputs = @( @{ referenceKey = "value"; value = $binding; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null } )
    }
}

# Read a named output's value off an instance as a string (case-insensitive). String values carry `preview`;
# numbers/objects/arrays carry the raw value under `value` (objects/arrays are returned as compact JSON).
function Get-OutputPreview {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $Name)
    $node = ($Instance.outputs.PSObject.Properties | Where-Object { $_.Name -ieq $Name } | Select-Object -First 1).Value.value
    if ($null -eq $node) { return $null }
    if ($null -ne $node.preview) { return "$($node.preview)" }
    if ($node.PSObject.Properties['value']) {
        $inner = $node.value
        if ($inner -is [System.Array] -or $inner -is [pscustomobject]) { return ($inner | ConvertTo-Json -Compress) }
        return "$inner"
    }
    return ($node | ConvertTo-Json -Compress)
}
