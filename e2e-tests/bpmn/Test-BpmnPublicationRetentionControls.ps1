<# Qualifies the actual republish/collection guard using a completed owned-host receipt. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $EvidencePath)
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-BpmnBroadcastRecovery.ps1'), [ref] $tokens, [ref] $errors)
if ($errors.Count -ne 0) { throw 'The actual runner has parser errors.' }
$functions = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-RepublishedCollectionState' }, $true))
if ($functions.Count -ne 1) { throw 'Expected the actual retention acceptance function exactly once.' }
Invoke-Expression $functions[0].Extent.Text
$case = Get-Content -Raw $EvidencePath | ConvertFrom-Json -AsHashtable -DateKind String
if (-not $case.passed -or $case.scenario -ne 'saved-old-publication-retained-through-real-collection-and-republish') { throw 'A completed actual republish/collection fixture is required.' }
function Assert-Fixture($Fixture, [bool] $AfterCollection) {
    $observation = if ($AfterCollection) { $Fixture.afterCollection } else { $Fixture.beforeCollection }
    Assert-RepublishedCollectionState $observation $Fixture.republication.original $Fixture.republication.replacement $Fixture.collectionControlPublication $AfterCollection
}
Assert-Fixture $case $false
Write-Host 'PASS actual before-collection guard'
Assert-Fixture $case $true
Write-Host 'PASS actual after-collection guard'
$mutations = [ordered]@{
    'missing retained old material' = { param($c) $id = $c.republication.original.artifactId; $c.afterCollection.observedMaterial = @($c.afterCollection.observedMaterial | Where-Object { $_.identity.artifactId -ne $id }) }
    'changed original artifact hash' = { param($c) ($c.afterCollection.observedMaterial | Where-Object { $_.identity.artifactId -eq $c.republication.original.artifactId }).identity.artifactHash = 'changed' }
    'unretired original source' = { param($c) ($c.afterCollection.observedSourceReferences | Where-Object artifactId -eq $c.republication.original.artifactId).deletedAt = $null }
    'active original binding' = { param($c) $c.afterCollection.matchingBindings += @{ artifactId = $c.republication.original.artifactId; isActive = $true } }
    'missing active replacement binding' = { param($c) $id = $c.republication.replacement.artifactId; $c.afterCollection.matchingBindings = @($c.afterCollection.matchingBindings | Where-Object artifactId -ne $id) }
    'existing original execution' = { param($c) $c.afterCollection.allWorkflows += @{ artifactId = $c.republication.original.artifactId; workflowExecutionId = 'other-execution' } }
    'other execution retention root' = { param($c) $c.afterCollection.executionRootArtifactIds += $c.republication.original.artifactId }
    'other dispatch retention root' = { param($c) $c.afterCollection.dispatchRootArtifactIds += $c.republication.original.artifactId }
    'declared executable dependency' = { param($c) $c.afterCollection.observedMaterial[0].dependencies += @{ artifactId = $c.republication.original.artifactId } }
    'wrapper or flattened node template route' = { param($c) $c.afterCollection.observedMaterial[0].templateHashReferences += 'sha256:other-template' }
    'template direct dependency' = { param($c) $c.afterCollection.observedTemplates[0].directDependencies += @{ templateId = 'other-template' } }
    'template closed dependency' = { param($c) $c.afterCollection.observedTemplates[0].closedTemplates += @{ templateId = 'other-template' } }
    'nested template metadata route' = { param($c) $c.afterCollection.observedTemplates[0].templateHashReferences += 'sha256:other-template' }
    'template identity aliases original artifact' = { param($c) $c.afterCollection.observedTemplates[0].templateId = $c.republication.original.artifactId }
    'uncollected control material' = { param($c) $id = $c.collectionControlPublication.artifactId; $c.afterCollection.observedMaterial += @($c.beforeCollection.observedMaterial | Where-Object { $_.identity.artifactId -eq $id }) }
    'uncollected control reference' = { param($c) $id = $c.collectionControlPublication.artifactId; $c.afterCollection.observedSourceReferences += @($c.beforeCollection.observedSourceReferences | Where-Object artifactId -eq $id) }
    'omitted template inventory' = { param($c) $c.afterCollection.Remove('observedTemplates') }
    'omitted flattened metadata projection' = { param($c) $c.afterCollection.observedMaterial[0].Remove('templateHashReferences') }
    'omitted template dependency projection' = { param($c) $c.afterCollection.observedTemplates[0].Remove('closedTemplates') }
}
foreach ($entry in $mutations.GetEnumerator()) {
    $changed = $case | ConvertTo-Json -Depth 90 | ConvertFrom-Json -AsHashtable -DateKind String
    & $entry.Value $changed
    $rejected = $false
    try { Assert-Fixture $changed $true } catch { $rejected = $true }
    if (-not $rejected) { throw "The actual guard accepted $($entry.Key)." }
    Write-Host "PASS refuses $($entry.Key)"
}
