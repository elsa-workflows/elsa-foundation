<#
    Strict result assertion for the documented Sequence workflow smoke.
    The expected child count is supplied by the caller so the default three-line
    sample and other multi-line callers keep their existing behavior.
#>

function Get-SequenceWorkflowRequiredProperty {
    param([Parameter(Mandatory)][AllowNull()] $Object, [Parameter(Mandatory)][string] $Name)

    if ($null -eq $Object) { throw "Sequence result is missing object '$Name'." }
    if ($Object -is [System.Collections.IDictionary]) {
        if (-not $Object.Contains($Name)) { throw "Sequence result is missing property '$Name'." }
        return ,$Object[$Name]
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Sequence result is missing property '$Name'." }
    return ,$property.Value
}

function Get-SequenceWorkflowCount {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $Name)

    $value = Get-SequenceWorkflowRequiredProperty -Object $Instance -Name $Name
    if ($null -eq $value) { throw "Expected a numeric '$Name'; got null." }
    $type = [Type]::GetTypeCode($value.GetType())
    if ($type -notin @(
            [TypeCode]::SByte, [TypeCode]::Byte, [TypeCode]::Int16, [TypeCode]::UInt16,
            [TypeCode]::Int32, [TypeCode]::UInt32, [TypeCode]::Int64, [TypeCode]::UInt64,
            [TypeCode]::Single, [TypeCode]::Double, [TypeCode]::Decimal)) {
        throw "Expected a numeric '$Name'; got '$value'."
    }
    return [decimal]$value
}

function Assert-SequenceWorkflowResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Instance,
        [Parameter(Mandatory)][int] $ExpectedLineCount
    )
    if ($ExpectedLineCount -lt 0) { throw 'Expected Sequence child count cannot be negative.' }

    $workflow = Get-SequenceWorkflowRequiredProperty -Object $Instance -Name 'instance'
    $status = Get-SequenceWorkflowRequiredProperty -Object $workflow -Name 'status'
    if ($status -isnot [string] -or $status -cne 'Completed') {
        throw "Expected Sequence workflow status 'Completed'; got '$status'."
    }

    $expectedActivityCount = $ExpectedLineCount + 1
    $reportedActivityCount = Get-SequenceWorkflowCount -Instance $workflow -Name 'activityCount'
    if ([decimal]$reportedActivityCount -ne $expectedActivityCount) {
        throw "Expected reported activity count $expectedActivityCount; got '$reportedActivityCount'."
    }

    $reportedIncidentCount = Get-SequenceWorkflowCount -Instance $workflow -Name 'incidentCount'
    if ([decimal]$reportedIncidentCount -ne 0) {
        throw "Expected reported incident count 0; got '$reportedIncidentCount'."
    }

    $activitiesValue = Get-SequenceWorkflowRequiredProperty -Object $Instance -Name 'activities'
    if ($null -eq $activitiesValue -or $activitiesValue -is [string]) {
        throw 'Expected a returned Sequence activity collection; the collection is missing or invalid.'
    }
    $activities = @($activitiesValue)
    if ($activities.Count -ne $expectedActivityCount) {
        throw "Expected exactly $expectedActivityCount returned activities; got $($activities.Count)."
    }
    if ([decimal]$reportedActivityCount -ne $activities.Count) {
        throw "Reported activity count $reportedActivityCount does not match the $($activities.Count) returned activities."
    }

    $activityRows = @()
    foreach ($activity in $activities) {
        $nodeId = Get-SequenceWorkflowRequiredProperty -Object $activity -Name 'executableNodeId'
        if ($nodeId -isnot [string] -or [string]::IsNullOrWhiteSpace($nodeId)) {
            throw "Expected a non-empty activity node identity; got '$nodeId'."
        }
        $duplicate = @($activityRows | Where-Object { [string]::Equals($_.NodeId, $nodeId, [StringComparison]::Ordinal) })
        if ($duplicate.Count -gt 0) { throw "Duplicate returned activity node '$nodeId'." }
        $activityRows += [pscustomobject]@{ NodeId = $nodeId; Activity = $activity }
    }

    $rootRows = @($activityRows | Where-Object { [string]::Equals($_.NodeId, 'sequence-root', [StringComparison]::Ordinal) })
    if ($rootRows.Count -ne 1) { throw "Expected exactly one Sequence root node 'sequence-root'." }
    $root = $rootRows[0].Activity
    $rootType = Get-SequenceWorkflowRequiredProperty -Object $root -Name 'activityType'
    if ($rootType -isnot [string] -or $rootType -cnotmatch '(?:^|\.)Sequence$') {
        throw "Expected sequence-root activity type Sequence; got '$rootType'."
    }
    $rootStatus = Get-SequenceWorkflowRequiredProperty -Object $root -Name 'status'
    if ($rootStatus -isnot [string] -or $rootStatus -cne 'Completed') {
        throw "Expected sequence-root activity status 'Completed'; got '$rootStatus'."
    }

    for ($line = 0; $line -lt $ExpectedLineCount; $line++) {
        $nodeId = "line-$line"
        $childRows = @($activityRows | Where-Object { [string]::Equals($_.NodeId, $nodeId, [StringComparison]::Ordinal) })
        if ($childRows.Count -ne 1) { throw "Expected exactly one WriteLine child node '$nodeId'." }
        $child = $childRows[0].Activity
        $childType = Get-SequenceWorkflowRequiredProperty -Object $child -Name 'activityType'
        if ($childType -isnot [string] -or $childType -cnotmatch '(?:^|\.)WriteLine$') {
            throw "Expected $nodeId activity type WriteLine; got '$childType'."
        }
        $childStatus = Get-SequenceWorkflowRequiredProperty -Object $child -Name 'status'
        if ($childStatus -isnot [string] -or $childStatus -cne 'Completed') {
            throw "Expected $nodeId activity status 'Completed'; got '$childStatus'."
        }
    }

    $incidentsValue = Get-SequenceWorkflowRequiredProperty -Object $Instance -Name 'incidents'
    if ($null -eq $incidentsValue -or $incidentsValue -is [string]) {
        throw 'Expected a returned incident collection; the collection is missing or invalid.'
    }
    $incidents = @($incidentsValue)
    if ($incidents.Count -ne 0) {
        throw "Expected zero returned incidents; got $($incidents.Count)."
    }
}
