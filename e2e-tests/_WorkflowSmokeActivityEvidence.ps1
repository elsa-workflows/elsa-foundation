function Get-DeferredWorkflowSmokeActivities {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)] $Instance,
        [Parameter(Mandatory)][string] $ExecutionId,
        [Parameter(Mandatory)] $Publication,
        [Parameter(Mandatory)][int] $ExpectedCount
    )

    if ($Instance.activitiesDeferred -isnot [bool] -or -not $Instance.activitiesDeferred) {
        return
    }
    $run = $Instance.instance
    if ($run.workflowExecutionId -cne $ExecutionId -or [string]::IsNullOrWhiteSpace($Publication.artifactId) -or
        $run.artifactId -cne $Publication.artifactId -or [string]::IsNullOrWhiteSpace($Publication.sourceReferenceId) -or
        $run.sourceReferenceId -cne $Publication.sourceReferenceId) {
        throw 'The deferred instance response belongs to a different workflow execution or publication.'
    }

    $id = [Uri]::EscapeDataString($ExecutionId)
    $path = "$($Ctx.BaseUrl)/runtime/workflows/instances/$id/activity-executions?limit=50"
    $activities = [object[]]::new($ExpectedCount)
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $cursors = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $cursor = $null
    do {
        $uri = if ($null -eq $cursor) { $path } else { "$path&cursor=$([Uri]::EscapeDataString($cursor))" }
        $page = Invoke-RestMethod $uri -WebSession $Ctx.Session
        $validCount = ($page.totalCount -is [int] -or $page.totalCount -is [long]) -and $page.totalCount -eq $ExpectedCount
        if (-not $validCount -or $page.items -isnot [array] -or $page.items.Count -eq 0) {
            throw "Expected a deferred activity page for $ExpectedCount executions; got total '$($page.totalCount)'."
        }
        $countBeforePage = $ids.Count
        foreach ($entry in $page.items) {
            $row = $entry.execution
            $position = $entry.position
            if (($position -isnot [int] -and $position -isnot [long]) -or $position -lt 1 -or $position -gt $ExpectedCount -or
                $row.workflowExecutionId -cne $ExecutionId -or $row.activityExecutionId -isnot [string] -or
                [string]::IsNullOrWhiteSpace($row.activityExecutionId)) {
                throw 'Deferred activity results contain a missing, duplicated or foreign execution.'
            }
            $index = $position - 1
            if ($null -ne $activities[$index]) {
                if ($activities[$index].activityExecutionId -cne $row.activityExecutionId) {
                    throw 'Deferred activity pages disagree on the execution at one position.'
                }
                continue
            }
            if (-not $ids.Add($row.activityExecutionId)) {
                throw 'Deferred activity pages repeat one execution at different positions.'
            }
            $activities[$index] = $row
        }
        # The browse API opens at the tail. Its clamped first page can overlap the next page in execution order.
        $cursor = $page.previousCursor
        if ($null -ne $cursor -and ($cursor -isnot [string] -or [string]::IsNullOrWhiteSpace($cursor) -or
                -not $cursors.Add($cursor) -or $ids.Count -eq $countBeforePage -or $ids.Count -ge $ExpectedCount)) {
            throw 'The deferred activity page returned an invalid continuation.'
        }
    } while ($null -ne $cursor)

    if ($ids.Count -ne $ExpectedCount) {
        throw "Expected exactly $ExpectedCount deferred activity executions; got $($ids.Count)."
    }
    return ,$activities
}
