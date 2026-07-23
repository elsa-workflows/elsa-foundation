<#
.SYNOPSIS
    Incident querying: a faulted instance records a blocking incident that is retrievable and counted.
.DESCRIPTION
    Faults a workflow (a WriteLine whose `text` is a JavaScript binding that throws at evaluation), then asserts:
      - the instance status is `Faulted` and its summary `incidentCount` is 1;
      - `GET runtime/workflows/instances/{id}/incidents` returns `{ workflowExists, incidents:[...], count }`
        with one incident that is `Blocking`, `WaitForIntervention`, and carries a `failureType` + `message`.

    (Every fault currently records `WaitForIntervention` — there is no incident-strategy layer to do otherwise;
    see issue #1015. This test asserts the incident *recording/reporting* contract, which is what exists.)
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_PersistenceCommon.ps1"

Write-Host "== Incident query (faulted instance) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

# Fault: a JavaScript text binding that throws at evaluation -> the activity input fails to materialize -> incident.
$boom = New-ActivityNode -NodeId "boom" -VersionId $wl -Inputs @(
    @{ referenceKey = "text"; value = @{ value = "(function(){ throw new Error('boom $tag'); })()"; expressionType = "JavaScript" }; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null }
)
$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Incident-$tag" -Description "faulting workflow" -RootActivity $boom }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$incidents = @(Get-InstanceIncidents -Ctx $ctx -ExecutionId $inst.instance.workflowExecutionId)
$one = $incidents | Select-Object -First 1
Write-Host ""
Write-Host ("instance status={0} summary.incidentCount={1} incidents.count={2}" -f $inst.instance.status, $inst.instance.incidentCount, $incidents.Count)
if ($one) { Write-Host ("incident: severity={0} status={1} resolution={2} failureType={3}" -f $one.severity, $one.status, $one.resolutionAction, $one.failureType) }

$fail = @()
if ($inst.instance.status -ne 'Faulted') { $fail += "instance not Faulted (got $($inst.instance.status))" }
if ([int]$inst.instance.incidentCount -ne 1) { $fail += "summary incidentCount=$($inst.instance.incidentCount) (expected 1)" }
if ($incidents.Count -ne 1) { $fail += "incidents endpoint returned $($incidents.Count) (expected 1)" }
if ($one) {
    if ($one.status -ne 'Blocking') { $fail += "incident status=$($one.status) (expected Blocking)" }
    if ($one.resolutionAction -ne 'WaitForIntervention') { $fail += "resolutionAction=$($one.resolutionAction) (expected WaitForIntervention)" }
    if ([string]::IsNullOrEmpty($one.failureType)) { $fail += "incident missing failureType" }
    if ([string]::IsNullOrEmpty($one.message)) { $fail += "incident missing message" }
} else { $fail += "no incident object returned" }

Write-Host ""
if ($fail.Count -eq 0) {
    Write-Host "SUCCESS - faulted instance recorded a blocking incident, retrievable and counted correctly." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
