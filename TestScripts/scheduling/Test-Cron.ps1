<#
.SYNOPSIS
    Cron: a recurring START trigger that starts a new workflow instance on a cron schedule (UTC).
.DESCRIPTION
    Publishes a workflow whose root is [Cron(Expression) -> WriteLine]. Cron is a [TriggerActivity]; publishing
    registers a recurring schedule whose occurrences are absolute UTC instants (Cronos), and the hosted
    RecurringTriggerPumpTask starts a NEW instance on each occurrence (at most once per due window, swept
    ~every 10s). The default expression is a 6-field (with-seconds) every-15-seconds schedule so the test fires
    within a short budget. Asserts at least one instance is auto-started and completes with the WriteLine run.
    Requires the scheduling features composed in the server (see _SchedulingCommon.ps1 / ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl    = "http://localhost:5095",
    [string] $Username   = "admin",
    [string] $Password   = "Password123!",
    [string] $Expression = "*/15 * * * * *"   # 6-field (seconds): at :00/:15/:30/:45 of every minute, UTC
)
. "$PSScriptRoot/_SchedulingCommon.ps1"

Write-Host "== Cron (recurring start trigger; Expression='$Expression') ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$cr  = Invoke-Step "resolve Cron"      { Get-ActivityVersionId -Ctx $ctx -TypeKey $script:CronType }

$cron = New-CronNode -NodeId "cron" -VersionId $cr -Expression $Expression
$tick = New-ActivityNode -NodeId "tick" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "cron tick") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($cron, $tick))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Cron-$(Get-Random -Max 9999)" -Description "recurring Cron trigger" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
Write-Host ("[publish] cron schedule registered (artifact {0}); waiting for the pump to fire..." -f $pub.artifactId)

$inst = Wait-TriggeredInstance -Ctx $ctx -ArtifactId $pub.artifactId -TimeoutSeconds 90

Write-Host ""
if ($inst -and (Get-NodeRunCount -Instance $inst -NodeId 'tick') -ge 1) {
    Show-WorkflowInstance -Instance $inst
    Write-Host ("SUCCESS - the Cron pump auto-started instance {0}, which completed with the tick step run." -f $inst.instance.workflowExecutionId) -ForegroundColor Green
} else {
    Write-Host "FAIL - no completed instance was auto-started from the Cron within the budget." -ForegroundColor Red
    exit 1
}
