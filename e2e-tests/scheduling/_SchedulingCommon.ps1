<#
.SYNOPSIS
    Shared helpers for the scheduling test scripts (Delay / Timer / Cron).
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds scheduling-specific node builders + wait helpers.

    Execution models (from src/Elsa/Activities/Scheduling):
      - Delay  : MID-FLOW suspend/resume. Suspends the instance for `Duration`; the hosted DurableTimerPumpTask
                 fires a bookmark at now+Duration (swept ~every 10s) to auto-resume it. No external stimulus.
      - Timer  : START trigger ([TriggerActivity]). The RecurringTriggerPumpTask starts a NEW instance every
                 `Interval`, anchored per fire (fired-at + interval), at most once per due window.
      - Cron   : START trigger. Same pump; occurrences are absolute UTC instants from a Cronos expression.

    Value formats: Delay.Duration = .NET TimeSpan string ("00:00:02"); Timer.Interval = ISO-8601 ("PT5S") or
    TimeSpan; Cron.Expression = Cronos 5-field or 6/7-field-with-seconds, UTC. Timer/Cron inputs MUST be literals.

    REQUIRES the scheduling features composed in the server (shells.json):
      ActivitiesScheduling, WorkflowsRuntimeScheduling, WorkflowsRuntimeRecurringTriggers.
    Without them a Delay faults at construction (IDurableTimerScheduler unresolved) and Timer/Cron never fire.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

$script:DelayType = 'Elsa.Activities.Scheduling.Activities.Delay'
$script:TimerType = 'Elsa.Activities.Scheduling.Activities.Timer'
$script:CronType  = 'Elsa.Activities.Scheduling.Activities.Cron'

# A Delay node that suspends the current instance for the given .NET TimeSpan literal (e.g. "00:00:02").
function New-DelayNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $VersionId, [Parameter(Mandatory)][string] $Duration)
    New-ActivityNode -NodeId $NodeId -VersionId $VersionId -Inputs @( (New-LiteralInput -ReferenceKey "Duration" -Value $Duration) )
}

# A Timer start-trigger node (Interval = ISO-8601 duration or TimeSpan literal).
function New-TimerNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $VersionId, [Parameter(Mandatory)][string] $Interval)
    New-ActivityNode -NodeId $NodeId -VersionId $VersionId -Inputs @( (New-LiteralInput -ReferenceKey "Interval" -Value $Interval) )
}

# A Cron start-trigger node (Expression = Cronos cron expression, UTC).
function New-CronNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $VersionId, [Parameter(Mandatory)][string] $Expression)
    New-ActivityNode -NodeId $NodeId -VersionId $VersionId -Inputs @( (New-LiteralInput -ReferenceKey "Expression" -Value $Expression) )
}

# Instances started from a given published artifact (the recurring pump starts fresh instances there).
function Get-InstancesByArtifact {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $ArtifactId)
    $list = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances?artifactId=$ArtifactId" -WebSession $Ctx.Session
    if ($list.items) { @($list.items) } elseif ($list -is [array]) { @($list) } else { @() }
}

# Poll until at least one instance started from $ArtifactId reaches a terminal Completed/Finished status (or timeout).
# Returns the completed instance-detail object, or $null on timeout. Trigger fires are pump-driven, so this can take
# up to (interval|cron-occurrence + ~10s sweep jitter); default budget is generous.
function Wait-TriggeredInstance {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $ArtifactId, [int] $TimeoutSeconds = 75)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 3
        foreach ($it in (Get-InstancesByArtifact -Ctx $Ctx -ArtifactId $ArtifactId)) {
            $exec = $it.workflowExecutionId
            if (-not $exec) { continue }
            $inst = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $exec
            if ($inst.instance.status -in @('Completed','Finished')) { return $inst }
        }
    } while ((Get-Date) -lt $deadline)
    return $null
}
