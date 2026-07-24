<#
.SYNOPSIS
    Timer: a recurring START trigger that starts a new workflow instance every interval.
.DESCRIPTION
    Publishes a workflow whose root is [Timer(Interval) -> WriteLine]. Timer is a [TriggerActivity]; publishing
    registers a recurring schedule and the hosted RecurringTriggerPumpTask starts a NEW instance each interval
    (anchored per fire, at most once per due window, swept ~every 10s). No execute call - the pump drives it.
    Asserts that at least one instance is auto-started from the published artifact and completes with the
    downstream WriteLine having run.
    Requires the scheduling features composed in the server (see _SchedulingCommon.ps1 / ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Interval = "PT5S"
)
. "$PSScriptRoot/_SchedulingCommon.ps1"

Write-Host "== Timer (recurring start trigger; Interval=$Interval) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$tm  = Invoke-Step "resolve Timer"     { Get-ActivityVersionId -Ctx $ctx -TypeKey $script:TimerType }

$timer = New-TimerNode -NodeId "timer" -VersionId $tm -Interval $Interval
$tick  = New-ActivityNode -NodeId "tick" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "timer tick") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($timer, $tick))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Timer-$(Get-Random -Max 9999)" -Description "recurring Timer trigger" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
Write-Host ("[publish] timer schedule registered (artifact {0}); waiting for the pump to fire..." -f $pub.artifactId)

$inst = Wait-TriggeredInstance -Ctx $ctx -ArtifactId $pub.artifactId -TimeoutSeconds 75

Write-Host ""
if ($inst -and (Get-NodeRunCount -Instance $inst -NodeId 'tick') -ge 1) {
    Show-WorkflowInstance -Instance $inst
    Write-Host ("SUCCESS - the Timer pump auto-started instance {0}, which completed with the tick step run." -f $inst.instance.workflowExecutionId) -ForegroundColor Green
} else {
    Write-Host "FAIL - no completed instance was auto-started from the Timer within the budget." -ForegroundColor Red
    exit 1
}
