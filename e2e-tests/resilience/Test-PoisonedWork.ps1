<#
.SYNOPSIS
    A scheduler work item that throws during dispatch is poisoned and surfaces a blocking SchedulerWorkPoisoned
    incident over REST (default retry policy = 0 retries -> poisoned on first failure).
.DESCRIPTION
    Binds a WriteLine's `text` input to a JavaScript expression that throws. Evaluating the binding faults the
    invoke work item; with the default NoopRuntimeDomainRetryPolicy (DoNotRetry) it is poisoned immediately and the
    PoisonedSchedulerWorkIncidentObserver records a Critical/Blocking incident (failureType SchedulerWorkPoisoned),
    faulting the workflow. Asserts that incident is observable via GET .../instances/{id} (incidents[]).

    NOTE: the retry COUNT before poisoning is not exercisable here - the reference server composes the default
    zero-retry policy, so a "N retries then incident" assertion is out of reach without a custom retry policy.
    Requires the server from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== Poisoned scheduler work -> blocking incident (JS-throw binding) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }

# WriteLine.text bound to a JS expression that throws at evaluation.
$jsThrow = "(function(){ throw new Error('poison-by-test'); })()"
$boom = New-ActivityNode -NodeId "boom" -VersionId $wl -Inputs @(
    @{ referenceKey = "text"; value = @{ value = $jsThrow; expressionType = "JavaScript" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null }
)
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($boom))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Poison-$(Get-Random -Max 9999)" -Description "poisoned scheduler work" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$incidents = @($inst.incidents)
$poisoned = $incidents | Where-Object { $_.failureType -eq 'SchedulerWorkPoisoned' -or $_.message -match 'poison' }
$blocking = $poisoned | Where-Object { $_.isBlocking -or $_.status -eq 'Blocking' }

Write-Host ""
Write-Host ("incidents: " + (($incidents | ForEach-Object { $_.failureType }) -join ', '))
if ($inst.instance.status -eq 'Faulted' -and $poisoned -and $blocking) {
    Write-Host "SUCCESS - the throwing binding poisoned the work item; a blocking SchedulerWorkPoisoned incident is observable and the workflow Faulted." -ForegroundColor Green
} else {
    Write-Host ("FAIL - status '{0}', poisoned incident present={1}, blocking={2}" -f $inst.instance.status, [bool]$poisoned, [bool]$blocking) -ForegroundColor Red
    exit 1
}
