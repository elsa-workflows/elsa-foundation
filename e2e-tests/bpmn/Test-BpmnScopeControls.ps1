<# Qualifies actual eligibility and exact private callback evidence using a completed owned-host receipt. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $EvidencePath)
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-BpmnBroadcastRecovery.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'The actual runner has parser errors.' }
foreach ($name in @('Same-Snapshot', 'Assert-ScopedEligibility', 'Assert-PrivateScopeWaits', 'Assert-PrivateDeliveryFacts')) {
    $functions = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true))
    if ($functions.Count -ne 1) { throw "Expected the actual $name function exactly once." }
    Invoke-Expression $functions[0].Extent.Text
}
$case = Get-Content -Raw $EvidencePath | ConvertFrom-Json -AsHashtable -DateKind String
if (-not $case.passed -or $case.scopeProfile.kind -notin @('local-scope', 'correlation-scope')) { throw 'A completed actual scoped host fixture is required.' }
function Assert-Fixture($c) {
    $before = $c.scopeEligibilityBeforePrivateDelivery
    Assert-ScopedEligibility $before $c.scopeProfile $c.cut
    $private = @($before.workflows | Where-Object workflowExecutionId -eq $c.privateScopeTestRun.workflowExecutionId)[0]
    Assert-PrivateScopeWaits $c.privateScopeWaits $private
    $ids = @()
    foreach ($delivery in $c.privateScopeDeliveries) {
        $ids += $delivery.waitId
        Assert-PrivateDeliveryFacts $before $delivery.observation $c.privateScopeTestRun.workflowExecutionId $ids $delivery.deliveryId
    }
    if ($ids.Count -ne 2) { throw 'Both actual deliveries are required.' }
}
Assert-Fixture $case
Write-Host 'PASS actual scoped eligibility, live wait API and both exact private callbacks'
$mutations = [ordered]@{
    'wrong local intent' = { param($c) $c.scopeEligibilityBeforePrivateDelivery.source.intentPayload.isLocalEvent = -not $c.scopeEligibilityBeforePrivateDelivery.source.intentPayload.isLocalEvent }
    'wrong correlation intent' = { param($c) $c.scopeEligibilityBeforePrivateDelivery.source.intentPayload.correlationId = 'foreign' }
    'missing selected callback' = { param($c) $c.scopeEligibilityBeforePrivateDelivery.source.snapshot.resumes = @($c.scopeEligibilityBeforePrivateDelivery.source.snapshot.resumes[0]) }
    'duplicate selected callback' = { param($c) $c.scopeEligibilityBeforePrivateDelivery.source.snapshot.resumes[1].bookmarkId = $c.scopeEligibilityBeforePrivateDelivery.source.snapshot.resumes[0].bookmarkId }
    'selected wrong execution' = { param($c) $c.scopeEligibilityBeforePrivateDelivery.source.snapshot.resumes[0].workflowExecutionId = 'foreign' }
    'inactive published native start' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.matchingBindings | Where-Object artifactId -eq $c.scopeProfile.nativeStartPublication.artifactId)[0].isActive = $false }
    'start lacks distinct correlation' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.matchingBindings | Where-Object artifactId -eq $c.scopeProfile.nativeStartPublication.artifactId)[0].correlationScope = $c.scopeProfile.correlationA }
    'isolated listener has published run kind' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[0].workflowExecutionId)[0].runKind = 2 }
    'isolated listener has foreign scope' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[0].workflowExecutionId)[0].testScope.scopeId = 'foreign' }
    'isolated listener is closed' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[0].workflowExecutionId)[0].testScopeState = 'Closed' }
    'isolated listener expired' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[0].workflowExecutionId)[0].testScope.expiresAt = '2000-01-01T00:00:00Z' }
    'isolated listener is foreign partition' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[0].workflowExecutionId)[0].partition = 'foreign' }
    'isolated listener already consumed' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[0].workflowExecutionId)[0].testRunDeliveryReceipts.foreign = @{} }
    'isolated listener hash does not match' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[0].workflowExecutionId)[0].bookmarks[0].stimulusHash = 'foreign' }
    'isolated listener is not suspended' = { param($c) @($c.scopeEligibilityBeforePrivateDelivery.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[0].workflowExecutionId)[0].bookmarks[0].activityStatus = 'Completed' }
    'private wait API unavailable' = { param($c) $c.privateScopeWaits.waits[0].isAvailable = $false }
    'private wait API duplicates sibling' = { param($c) $c.privateScopeWaits.waits[1].waitId = $c.privateScopeWaits.waits[0].waitId }
    'private wait API foreign identity' = { param($c) $c.privateScopeWaits.waits[0].waitId = 'foreign' }
    'private wait API wrong module' = { param($c) $c.privateScopeWaits.waits[0].driverId = 'foreign' }
    'private callback changed source' = { param($c) $c.privateScopeDeliveries[0].observation.source.rowRevision++ }
    'private callback missing receipt' = { param($c) @($c.privateScopeDeliveries[0].observation.workflows | Where-Object workflowExecutionId -eq $c.privateScopeTestRun.workflowExecutionId)[0].testRunDeliveryReceipts.Clear() }
    'private callback consumed sibling instead' = { param($c) $c.privateScopeDeliveries[0].waitId = $c.privateScopeDeliveries[1].waitId }
    'private callback changed sibling generation' = { param($c) @($c.privateScopeDeliveries[0].observation.workflows | Where-Object workflowExecutionId -eq $c.privateScopeTestRun.workflowExecutionId)[0].bookmarks[0].generation = 'changed' }
    'private callback lacks completed activity' = { param($c) $a=@($c.privateScopeDeliveries[0].observation.workflows | Where-Object workflowExecutionId -eq $c.privateScopeTestRun.workflowExecutionId)[0]; @($a.testActivities | Where-Object status -eq 'Completed')[0].status = 'Suspended' }
    'private callback reinvoked activity' = { param($c) $a=@($c.privateScopeDeliveries[0].observation.workflows | Where-Object workflowExecutionId -eq $c.privateScopeTestRun.workflowExecutionId)[0]; @($a.testActivities | Where-Object status -eq 'Completed')[0].attemptCount++ }
    'private callback changed another scope' = { param($c) @($c.privateScopeDeliveries[0].observation.workflows | Where-Object workflowExecutionId -eq $c.scopeProfile.testRuns[1].workflowExecutionId)[0].status = 'Completed' }
}
foreach ($entry in $mutations.GetEnumerator()) {
    $changed = $case | ConvertTo-Json -Depth 90 | ConvertFrom-Json -AsHashtable -DateKind String
    & $entry.Value $changed
    $rejected = $false
    try { Assert-Fixture $changed } catch { $rejected = $true }
    if (-not $rejected) { throw "The actual guards accepted $($entry.Key)." }
    Write-Host "PASS refuses $($entry.Key)"
}
