<# Qualifies the actual renewal acceptance guard using a completed owned-host receipt. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $EvidencePath)
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Test-BpmnBroadcastRecovery.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($runner, [ref] $tokens, [ref] $errors)
if ($errors.Count -ne 0) { throw 'The actual runner has parser errors.' }
$functions = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-ActualClaimRenewal' }, $true))
if ($functions.Count -ne 1) { throw 'Expected the actual runner acceptance function exactly once.' }
Invoke-Expression $functions[0].Extent.Text
$case = Get-Content -Raw $EvidencePath | ConvertFrom-Json -AsHashtable -DateKind String
if (-not $case.passed -or $case.scenario -ne 'real-pre-dispatch-claim-renewal-then-crash-and-standard-takeover') { throw 'A completed actual renewal fixture is required.' }
function Assert-Fixture($Fixture) {
    Assert-ActualClaimRenewal $Fixture.initialClaimLive $Fixture.initialClaimMarker $Fixture.renewedClaimLive $Fixture.cut ([DateTimeOffset]::Parse($Fixture.initialClaimReleasedAtUtc))
}
Assert-Fixture $case
Write-Host 'PASS actual owned-host renewal guard'
$mutations = [ordered]@{
    'unrenewed deadline' = { param($c) $c.renewedClaimLive.source.deliveryVisibleAfter = $c.initialClaimLive.source.deliveryVisibleAfter; $c.cut.visibleAfter = $c.initialClaimLive.source.deliveryVisibleAfter }
    'different commit-specific source' = { param($c) $c.renewedClaimLive.source.outboxItemId += ':different-commit' }
    'different claim owner' = { param($c) $c.renewedClaimLive.source.deliveringOwnerId += ':peer' }
    'different claim fence' = { param($c) $c.renewedClaimLive.source.deliveryFencingToken++ }
    'different canonical intent' = { param($c) $c.renewedClaimLive.source.intentFingerprint += ':different' }
    'unchanged physical revision' = { param($c) $c.renewedClaimLive.source.rowRevision = $c.initialClaimLive.source.rowRevision }
    'expired initial claim at release' = { param($c) $c.initialClaimReleasedAtUtc = $c.initialClaimLive.source.deliveryVisibleAfter }
    'expired initial claim at cohort cut' = { param($c) $c.cut.capturedAtUtc = $c.initialClaimLive.source.deliveryVisibleAfter }
}
foreach ($entry in $mutations.GetEnumerator()) {
    $changed = $case | ConvertTo-Json -Depth 90 | ConvertFrom-Json -AsHashtable -DateKind String
    & $entry.Value $changed
    $rejected = $false
    try { Assert-Fixture $changed } catch { $rejected = $true }
    if (-not $rejected) { throw "The actual guard accepted $($entry.Key)." }
    Write-Host "PASS refuses $($entry.Key)"
}
