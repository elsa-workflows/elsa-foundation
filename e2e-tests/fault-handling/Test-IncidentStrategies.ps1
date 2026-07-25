<#
.SYNOPSIS
    Built-in incident strategies produce distinct durable outcomes for the same fault.
.DESCRIPTION
    Publishes the same Sequence[WriteLine, Fault, WriteLine] with Fault/1 and
    ContinueWithIncidents/1. Fault/1 must terminalize the workflow with a Blocking/FaultWorkflow
    incident. ContinueWithIncidents/1 must preserve the faulted activity, leave the workflow
    nonterminal, open the incident, and not run the causally dependent trailing activity.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_FaultCommon.ps1"

function Invoke-IncidentStrategyCase {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $Alias,
        [Parameter(Mandatory)][string] $ExpectedStatus,
        [Parameter(Mandatory)][string] $ExpectedIncidentStatus,
        [Parameter(Mandatory)][string] $ExpectedActionKind,
        [Parameter(Mandatory)][string] $SequenceVersionId,
        [Parameter(Mandatory)][string] $WriteLineVersionId,
        [Parameter(Mandatory)][string] $FaultVersionId
    )

    $tag = Get-Random -Max 999999
    $before = New-ActivityNode -NodeId "before" -VersionId $WriteLineVersionId -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "before $Alias") )
    $boom = New-FaultNode -NodeId "boom" -FaultVersionId $FaultVersionId -Message "strategy $Alias $tag"
    $after = New-ActivityNode -NodeId "after" -VersionId $WriteLineVersionId -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "after $Alias (must not run)") )
    $root = New-ActivityNode -NodeId "root" -VersionId $SequenceVersionId -Structure (New-SequenceStructure -Activities @($before, $boom, $after))
    $strategy = @{ incidentStrategy = @{ alias = $Alias; version = "1" } }

    $definition = Submit-Workflow -Ctx $Ctx -Name "IncidentStrategy-$Alias-$tag" -RootActivity $root -StrategyOptions $strategy
    $publication = Publish-WorkflowVersion -Ctx $Ctx -VersionId $definition.version.id
    $run = Invoke-Artifact -Ctx $Ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId

    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        $instance = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $run.workflowExecutionId
        $incidents = @($instance.incidents)
    } while ($incidents.Count -eq 0 -and (Get-Date) -lt $deadline)

    $incident = $incidents | Select-Object -First 1
    $failures = @()
    if ($instance.instance.status -ne $ExpectedStatus) { $failures += "status=$($instance.instance.status) expected=$ExpectedStatus" }
    if ($incidents.Count -ne 1) { $failures += "incidents=$($incidents.Count) expected=1" }
    if ($incident.status -ne $ExpectedIncidentStatus) { $failures += "incident.status=$($incident.status) expected=$ExpectedIncidentStatus" }
    if ($incident.resolutionOutcome.actionKind -ne $ExpectedActionKind) { $failures += "actionKind=$($incident.resolutionOutcome.actionKind) expected=$ExpectedActionKind" }
    if ($incident.resolutionOutcome.strategy.alias -ne $Alias) { $failures += "strategy.alias=$($incident.resolutionOutcome.strategy.alias) expected=$Alias" }
    if ((Get-NodeRunCount -Instance $instance -NodeId "after") -ne 0) { $failures += "causally dependent trailing activity ran" }

    if ($failures.Count -gt 0) {
        throw "$Alias mismatch: $($failures -join '; ')"
    }
}

Write-Host "== Built-in incident strategies ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$sequence = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
$writeLine = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
$fault = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Fault'

Invoke-IncidentStrategyCase -Ctx $ctx -Alias "Fault" -ExpectedStatus "Faulted" `
    -ExpectedIncidentStatus "Blocking" -ExpectedActionKind "FaultWorkflow" `
    -SequenceVersionId $sequence -WriteLineVersionId $writeLine -FaultVersionId $fault

Invoke-IncidentStrategyCase -Ctx $ctx -Alias "ContinueWithIncidents" -ExpectedStatus "Running" `
    -ExpectedIncidentStatus "Open" -ExpectedActionKind "ContinueWithIncidents" `
    -SequenceVersionId $sequence -WriteLineVersionId $writeLine -FaultVersionId $fault

Write-Host "SUCCESS - Fault/1 and ContinueWithIncidents/1 produced distinct durable outcomes." -ForegroundColor Green
