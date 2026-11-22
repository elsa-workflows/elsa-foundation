#requires -Version 7.3
param([Parameter(Mandatory)][string] $EvidenceDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/_Assertions.ps1"
$receipt = Get-Content (Join-Path $EvidenceDirectory 'receipt.json') -Raw | ConvertFrom-Json -AsHashtable
if ($receipt.status -ne 'passed' -or @($receipt.cases).Count -ne 4 -or
    @($receipt.undeclaredRequests | Where-Object status -eq passed).Count -ne 2 -or
    @($receipt.placedReusableCases).Count -ne 2 -or $receipt.placedBodyPublicationRefusal.status -ne 'passed' -or
    $receipt.placedBodyPublicationRefusal.statusCode -ne 400 -or $receipt.placedBodyPublicationRefusal.code -cne 'VF-COER-001') {
    throw 'Controls require a complete real four-original/two-placed runtime receipt and all publication refusals'
}
foreach ($case in $receipt.cases) { Assert-CommittedText $case.runtime.committedInput $case.expected }
foreach ($case in $receipt.placedReusableCases) { Assert-PlacedReusableCase $case }
if (@($receipt.placedReusableCases | Where-Object name -CEQ 'placed-member-single').Count -ne 1 -or
    @($receipt.placedReusableCases | Where-Object name -CEQ 'placed-member-nested').Count -ne 1) { throw 'Both distinct placed proof rows are required' }
$controls = @()
function Expect-Refusal([string] $Name, [scriptblock] $Action, [string] $Message) {
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or $failure -notlike "$Message*") { throw "Oracle control '$Name' did not refuse for its intended reason: $failure" }
    $script:controls += @{name=$Name;status='expected-refusal';message=$failure}
    Write-Host "Expected negative $Name control: $failure"
}
Expect-Refusal 'committed-input' { Assert-CommittedText $receipt.cases[0].runtime.committedInput 'WRONG-SENTINEL' } 'Committed consumer input differs'
$before = Get-Content (Join-Path $EvidenceDirectory 'publication-before.json') -Raw | ConvertFrom-Json -AsHashtable
$after = Get-Content (Join-Path $EvidenceDirectory 'publication-after.json') -Raw | ConvertFrom-Json -AsHashtable
Assert-PublicationUnchanged $before $after
$after.elsa_publication_records.sha256 = 'changed-row-with-same-count'
Expect-Refusal 'original-publication-fingerprint' { Assert-PublicationUnchanged $before $after } 'Refused publication changed'
$placedBefore = Get-Content (Join-Path $EvidenceDirectory 'placed-publication-before.json') -Raw | ConvertFrom-Json -AsHashtable
$placedAfter = Get-Content (Join-Path $EvidenceDirectory 'placed-publication-after.json') -Raw | ConvertFrom-Json -AsHashtable
Assert-PublicationUnchanged $placedBefore $placedAfter
if (@($placedBefore.Keys).Count -ne 4 -or @($placedBefore.Values | Where-Object count -LE 0).Count -ne 0) { throw 'Placed no-write controls require four populated stores' }
$placedAfter.elsa_publication_records.sha256 = 'changed-placed-row-with-same-count'
Expect-Refusal 'placed-publication-fingerprint' { Assert-PublicationUnchanged $placedBefore $placedAfter } 'Refused publication changed'
$nested = @($receipt.placedReusableCases | Where-Object name -CEQ 'placed-member-nested')[0]
function Copy-Nested { $nested | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable }
$swapped = Copy-Nested
foreach ($role in @('producerInput','bodyInput','callerInput')) {
    $value = $swapped.frames[0].runtime[$role]
    $swapped.frames[0].runtime[$role] = $swapped.frames[1].runtime[$role]
    $swapped.frames[1].runtime[$role] = $value
}
Expect-Refusal 'cross-frame-values' { Assert-PlacedReusableCase $swapped } 'Committed consumer input differs'
$wrongProducer = Copy-Nested
$wrongProducer.frames[0].bodyBinding.activityResult.producerExecutableNodeId = $wrongProducer.frames[1].producerNodeId
Expect-Refusal 'cross-frame-producer' { Assert-PlacedReusableCase $wrongProducer } 'Placed member binding lost'
$wrongScope = Copy-Nested
$wrongScope.frames[0].runtime.bodyOwnerId = $wrongScope.frames[1].runtime.boundary.activityExecutionId
Expect-Refusal 'cross-frame-scope' { Assert-PlacedReusableCase $wrongScope } 'Placed runtime occurrence crossed'
$wrongWrapper = Copy-Nested
$wrongWrapper.wrapper.VersionId = 'wrong-published-wrapper-version'
Expect-Refusal 'wrapper-version-identity' { Assert-PlacedReusableCase $wrongWrapper } 'Nested placed wrapper publication identity changed'
$flattened = Copy-Nested
$compiledWrappers = @(Get-PlacedExecutableNodes $flattened.executable.rootActivity | Where-Object activityType -CEQ $flattened.wrapper.ActivityTypeKey)
$flatChildren = @($compiledWrappers | ForEach-Object { foreach ($slot in $_.childSlots) { $slot.activities } })
# Keep all four complete C subtrees/callers and every previous C receipt; remove only the B tree level.
$flattened.executable.rootActivity.childSlots = @(@{name='activities';activities=$flatChildren})
Expect-Refusal 'flattened-wrapper-topology' { Assert-PlacedReusableCase $flattened } 'Nested placed wrapper topology must retain exactly two B boundaries'
$wrongParent = Copy-Nested
$otherB = @($wrongParent.wrappers | Where-Object {$_.boundaryNodeId -cne $wrongParent.frames[0].wrapperNodeId})[0]
$caller = $wrongParent.frames[0].runtime.caller
$caller.provenance.parentActivityExecutionId = $otherB.runtime.activityExecutionId
$wrongParent.runtime.details[$caller.activityExecutionId].provenance.parentActivityExecutionId = $otherB.runtime.activityExecutionId
# Existing C owner/value records remain intact; only the caller's actual B ancestry is wrong.
Expect-Refusal 'wrong-wrapper-parent' { Assert-PlacedReusableCase $wrongParent } 'Nested placed wrapper owns the wrong C boundary or outside caller'
foreach ($invalidMode in @(@{name='deferred-selection-mode';value=[long]1}, @{name='string-known-selection-mode';value='Known'},
        @{name='string-zero-selection-mode';value='0'}, @{name='boolean-false-selection-mode';value=$false})) {
    $wrongMode = Copy-Nested
    $wrongMode.frames[0].bodyBinding.selection.mode = $invalidMode.value
    Expect-Refusal $invalidMode.name { Assert-PlacedReusableCase $wrongMode } 'Placed member selection must use integer Known mode 0'
}
@{status='passed';receiptSha256=(Get-FileHash (Join-Path $EvidenceDirectory 'receipt.json') -Algorithm SHA256).Hash;
    assertionsSha256=(Get-FileHash "$PSScriptRoot/_Assertions.ps1" -Algorithm SHA256).Hash;controls=$controls} |
    ConvertTo-Json -Depth 100 | Set-Content (Join-Path $EvidenceDirectory 'receipt-guard-controls.json')
Write-Host 'Receipt guards: real values, frame ownership and four-store fingerprints pass; thirteen isolated mutations refused.'
