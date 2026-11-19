<# Qualifies the actual cohort/capacity/key guards using a completed owned-host receipt. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $EvidencePath)
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-BpmnBroadcastRecovery.ps1'), [ref] $tokens, [ref] $errors)
if ($errors.Count -ne 0) { throw 'The actual runner has parser errors.' }
foreach ($name in @('Same-Snapshot', 'Assert-SameArtifactCohort', 'Assert-ActualSameArtifactShed', 'Assert-ActualSameArtifactAccepted')) {
    $functions = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true))
    if ($functions.Count -ne 1) { throw "Expected the actual $name function exactly once." }
    Invoke-Expression $functions[0].Extent.Text
}
$case = Get-Content -Raw $EvidencePath | ConvertFrom-Json -AsHashtable -DateKind String
if (-not $case.passed -or $case.scenario -ne 'real-capacity-shed-then-same-artifact-key-reuse-and-recovery') { throw 'A completed actual same-artifact fixture is required.' }
function Assert-Fixture($c) {
    Assert-SameArtifactCohort $c.initialStopped $c.sameArtifactPublications
    Assert-ActualSameArtifactShed $c.initialStopped $c.actualShed $c.actualShedMarker $c.actualLoadMarker
    Assert-ActualSameArtifactAccepted $c.initialStopped $c.actualShed $c.actualShedMarker $c.sameKeyAccepted $c.sameKeyAcceptedMarker ([DateTimeOffset]::Parse($c.actualLoadReleasedAtUtc)) ([DateTimeOffset]::Parse($c.actualLoadRespondedAtUtc))
}
Assert-SameArtifactCohort $case.initialStopped $case.sameArtifactPublications
Write-Host 'PASS actual two-binding one-key cohort'
Assert-ActualSameArtifactShed $case.initialStopped $case.actualShed $case.actualShedMarker $case.actualLoadMarker
Write-Host 'PASS actual real-capacity refusal'
Assert-ActualSameArtifactAccepted $case.initialStopped $case.actualShed $case.actualShedMarker $case.sameKeyAccepted $case.sameKeyAcceptedMarker ([DateTimeOffset]::Parse($case.actualLoadReleasedAtUtc)) ([DateTimeOffset]::Parse($case.actualLoadRespondedAtUtc))
Write-Host 'PASS actual same-key acceptance after load return'
$mutations = [ordered]@{
    'collapsed publication IDs' = { param($c) $c.sameArtifactPublications[1].publicationId = $c.sameArtifactPublications[0].publicationId }
    'collapsed binding IDs' = { param($c) $c.initialStopped.source.selectedStarts[1].triggerBindingId = $c.initialStopped.source.selectedStarts[0].triggerBindingId }
    'different artifact entries' = { param($c) $c.initialStopped.source.selectedStarts[1].artifactId += ':different' }
    'different original keys' = { param($c) $c.initialStopped.source.selectedStarts[1].startKey += ':different' }
    'collapsed captured source references' = { param($c) $c.initialStopped.source.snapshot.starts[1].sourceCapture.provenance.sourceReferenceId = $c.initialStopped.source.snapshot.starts[0].sourceCapture.provenance.sourceReferenceId }
    'uncaptured source provenance' = { param($c) $c.initialStopped.source.snapshot.starts[0].sourceCapture.version = 0 }
    'changed first attempt key' = { param($c) $c.actualShedMarker.observedStart.idempotencyKey += ':different' }
    'ambient admission exemption on source' = { param($c) $c.actualShedMarker.observedStart.hadAmbientChargeBefore = $true }
    'load without actual ambient charge' = { param($c) $c.actualLoadMarker.hasAmbientCharge = $false }
    'no held capacity' = { param($c) $c.actualShedMarker.observedStart.inFlightBefore = 0 }
    'ordinary deferral without shedding' = { param($c) $c.actualShedMarker.observedStart.isShed = $false }
    'duplicate instead of real refusal' = { param($c) $c.actualShedMarker.observedStart.status = 'Duplicate' }
    'physical target already present at refusal' = { param($c) $c.actualShed.allWorkflows += @{ artifactId = $c.actualShedMarker.observedStart.artifactId; workflowExecutionId = $c.actualShedMarker.observedStart.workflowExecutionId } }
    'different physical source at refusal' = { param($c) $c.actualShed.source.outboxItemId += ':different' }
    'no successor ownership at refusal' = { param($c) $c.actualShed.source.deliveringOwnerId = $c.initialStopped.source.deliveringOwnerId }
    'replacement key at acceptance' = { param($c) $c.sameKeyAcceptedMarker.observedStart.idempotencyKey += ':different' }
    'first binding repeated instead of later binding' = { param($c) $c.sameKeyAcceptedMarker.observedStart.triggerBindingId = $c.actualShedMarker.observedStart.triggerBindingId }
    'capacity still held at acceptance' = { param($c) $c.sameKeyAcceptedMarker.observedStart.inFlightBefore = 1 }
    'duplicate instead of acceptance' = { param($c) $c.sameKeyAcceptedMarker.observedStart.status = 'Duplicate' }
    'source rewritten between refusal and acceptance' = { param($c) $c.sameKeyAccepted.source.rowRevision++ }
    'acceptance before load HTTP response' = { param($c) $c.actualLoadRespondedAtUtc = [DateTimeOffset]::Parse($c.sameKeyAcceptedMarker.capturedAtUtc).AddSeconds(1).ToString('O') }
    'omitted ambient-charge observation' = { param($c) $c.sameKeyAcceptedMarker.observedStart.Remove('hadAmbientChargeBefore') }
    'different public artifact hash' = { param($c) $c.sameArtifactPublications[0].artifactHash = 'different' }
}
foreach ($entry in $mutations.GetEnumerator()) {
    $changed = $case | ConvertTo-Json -Depth 90 | ConvertFrom-Json -AsHashtable -DateKind String
    & $entry.Value $changed
    $rejected = $false
    try { Assert-Fixture $changed } catch { $rejected = $true }
    if (-not $rejected) { throw "The actual guards accepted $($entry.Key)." }
    Write-Host "PASS refuses $($entry.Key)"
}
