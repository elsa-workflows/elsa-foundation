<#
    Strict result assertion for the documented one-activity WriteLine workflow smoke.
    Kept separate so its pass/fail contract can be exercised without a server.
#>

function Get-WorkflowFlowRequiredProperty {
    param([Parameter(Mandatory)][AllowNull()] $Object, [Parameter(Mandatory)][string] $Name)

    if ($null -eq $Object) { throw "Workflow result is missing object '$Name'." }
    if ($Object -is [System.Collections.IDictionary]) {
        if (-not $Object.Contains($Name)) { throw "Workflow result is missing property '$Name'." }
        return ,$Object[$Name]
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Workflow result is missing property '$Name'." }
    return ,$property.Value
}

function Assert-WorkflowFlowResult {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $Instance)

    $workflow = Get-WorkflowFlowRequiredProperty -Object $Instance -Name 'instance'
    $status = Get-WorkflowFlowRequiredProperty -Object $workflow -Name 'status'
    if ($status -isnot [string] -or $status -cne 'Completed') {
        throw "Expected workflow status 'Completed'; got '$status'."
    }

    $reportedIncidentCount = Get-WorkflowFlowRequiredProperty -Object $workflow -Name 'incidentCount'
    $numericType = [Type]::GetTypeCode($reportedIncidentCount.GetType())
    if ($reportedIncidentCount -is [bool] -or $numericType -notin @(
            [TypeCode]::SByte, [TypeCode]::Byte, [TypeCode]::Int16, [TypeCode]::UInt16,
            [TypeCode]::Int32, [TypeCode]::UInt32, [TypeCode]::Int64, [TypeCode]::UInt64,
            [TypeCode]::Single, [TypeCode]::Double, [TypeCode]::Decimal)) {
        throw "Expected a numeric reported incident count; got '$reportedIncidentCount'."
    }
    if ([decimal]$reportedIncidentCount -ne 0) {
        throw "Expected reported incident count 0; got '$reportedIncidentCount'."
    }

    $activitiesValue = Get-WorkflowFlowRequiredProperty -Object $Instance -Name 'activities'
    if ($null -eq $activitiesValue -or $activitiesValue -is [string]) {
        throw 'Expected exactly one returned WriteLine activity; the activity collection is missing or invalid.'
    }
    $activities = @($activitiesValue)
    if ($activities.Count -ne 1) {
        throw "Expected exactly one returned activity; got $($activities.Count)."
    }

    $activity = $activities[0]
    $nodeId = Get-WorkflowFlowRequiredProperty -Object $activity -Name 'executableNodeId'
    if ($nodeId -isnot [string] -or $nodeId -cne 'write-root') {
        throw "Expected activity node 'write-root'; got '$nodeId'."
    }
    $activityType = Get-WorkflowFlowRequiredProperty -Object $activity -Name 'activityType'
    if ($activityType -isnot [string] -or $activityType -cnotmatch '(?:^|\.)WriteLine$') {
        throw "Expected a WriteLine activity; got '$activityType'."
    }
    $activityStatus = Get-WorkflowFlowRequiredProperty -Object $activity -Name 'status'
    if ($activityStatus -isnot [string] -or $activityStatus -cne 'Completed') {
        throw "Expected write-root activity status 'Completed'; got '$activityStatus'."
    }

    $incidentsValue = Get-WorkflowFlowRequiredProperty -Object $Instance -Name 'incidents'
    if ($null -eq $incidentsValue -or $incidentsValue -is [string]) {
        throw 'Expected an incident collection; the returned incident collection is missing or invalid.'
    }
    $incidents = @($incidentsValue)
    if ($incidents.Count -ne 0) {
        throw "Expected zero returned incidents; got $($incidents.Count)."
    }
}
