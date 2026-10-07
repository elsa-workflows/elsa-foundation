<#
.SYNOPSIS
    Multi-activity workflow: a Sequence root running several WriteLine children in order.
.DESCRIPTION
    Demonstrates a composite (container) activity. The root is a Sequence whose children live in
    its Structure payload (kind 'elsa.sequence.structure'). Submits -> publishes -> executes -> observes,
    proving the children run in authored order. Requires the server running from source.
.EXAMPLE
    pwsh ./e2e-tests/Test-SequenceWorkflow.ps1
    pwsh ./e2e-tests/Test-SequenceWorkflow.ps1 -Lines "one","two","three","four"
#>
[CmdletBinding()]
param(
    [string]   $BaseUrl  = "http://localhost:5095",
    [string]   $Username = "admin",
    [string]   $Password = "Password123!",
    [string[]] $Lines    = @("Sequence line 1", "Sequence line 2", "Sequence line 3")
)
. "$PSScriptRoot/_ElsaCommon.ps1"
. "$PSScriptRoot/_SequenceWorkflowVerdict.ps1"

Write-Host "== Sequence of WriteLines ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

$writeLine = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$sequence  = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }

# One WriteLine child per line.
$children = @()
for ($n = 0; $n -lt $Lines.Count; $n++) {
    $children += New-ActivityNode -NodeId "line-$n" -VersionId $writeLine -Inputs @(
        (New-LiteralInput -ReferenceKey "text" -Value $Lines[$n])
    )
}
$root = New-ActivityNode -NodeId "sequence-root" -VersionId $sequence -Structure (New-SequenceStructure -Activities $children)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Sequence-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "sequence of $($Lines.Count) WriteLines" -RootActivity $root }
Write-Host ("[submit]  definition={0} version={1}" -f $def.definition.id, $def.version.id)

$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
Write-Host ("[publish] artifact={0}" -f $pub.artifactId)

$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
Write-Host ("[execute] execution={0} dispatch={1}" -f $run.workflowExecutionId, $run.commandDispatchStatus)

Write-Host "[observe] instance detail:"
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst
Assert-SequenceWorkflowResult -Instance $inst -ExpectedLineCount $Lines.Count

$expected = $Lines.Count + 1   # children + the Sequence root itself
$recordEvidence = @()
foreach ($activity in @($inst.activities)) {
    $nodeId = Get-SequenceWorkflowRequiredProperty -Object $activity -Name 'executableNodeId'
    $activityType = Get-SequenceWorkflowRequiredProperty -Object $activity -Name 'activityType'
    $activityStatus = Get-SequenceWorkflowRequiredProperty -Object $activity -Name 'status'
    $shortType = ($activityType -split '\.')[-1]
    $recordEvidence += "${nodeId}=${shortType}:${activityStatus}"
}
$reportedIncidentCount = Get-SequenceWorkflowRequiredProperty -Object $inst.instance -Name 'incidentCount'
$returnedIncidentCount = @($inst.incidents).Count
Write-Host ("[sequence verdict] instance={0} reportedActivities={1} returnedActivities={2} reportedIncidents={3} returnedIncidents={4} records={5}" -f $inst.instance.status, $inst.instance.activityCount, @($inst.activities).Count, $reportedIncidentCount, $returnedIncidentCount, ($recordEvidence -join ','))
Write-Host ""
Write-Host "SUCCESS - sequence completed with $expected activity records and zero incidents; SERVER console should show, in order:" -ForegroundColor Green
$Lines | ForEach-Object { Write-Host "  $_" }
