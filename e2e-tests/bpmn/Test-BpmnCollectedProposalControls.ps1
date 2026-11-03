<# Evidence-acceptance controls for collection winning before actual cohort acceptance. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $EvidencePath)
$ErrorActionPreference = 'Stop'
$t=$null;$e=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-BpmnBroadcastRecovery.ps1'),[ref]$t,[ref]$e)
if($e.Count){throw 'The actual recovery runner has parser errors.'}
foreach($name in @('Same-Snapshot','Assert-CollectedProposalState','Assert-CollectedProposalRefusal')){
    $f=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true))
    if($f.Count -ne 1){throw "Expected one actual $name guard."}
    Invoke-Expression $f[0].Extent.Text
}
$case=Get-Content -Raw $EvidencePath | ConvertFrom-Json -AsHashtable -DateKind String
if(-not $case.passed -or $case.scenario -ne 'real-collection-before-cohort-acceptance-refuses-then-reselects-after-restart'){throw 'A completed actual collected-proposal fixture is required.'}
function Assert-Fixture($c){
    Assert-CollectedProposalState $c.beforeCollection $c.cut $c.collectedPublication $false
    Assert-CollectedProposalState $c.afterCollection $c.cut $c.collectedPublication $true
    Assert-CollectedProposalRefusal $c.afterCollection $c.afterProposalRefusal $c.cut $c.collectedProposalRefusal $c.collectedPublication
}
Assert-Fixture $case
Write-Host 'PASS actual prepared proposal, real collection and actual refusal guards'
$mutations=[ordered]@{
    'omitted collected material inventory'={param($c)$null=$c.afterCollection.Remove('observedMaterial')}
    'omitted collected reference inventory'={param($c)$null=$c.afterCollection.Remove('observedSourceReferences')}
    'omitted workflow inventory'={param($c)$null=$c.afterCollection.Remove('allWorkflows')}
    'omitted execution roots'={param($c)$null=$c.afterCollection.Remove('executionRootArtifactIds')}
    'omitted dispatch roots'={param($c)$null=$c.afterCollection.Remove('dispatchRootArtifactIds')}
    'already accepted before collection'={param($c)$c.beforeCollection.source.snapshot=$c.cut.proposal}
    'different physical source'={param($c)$c.beforeCollection.source.outboxItemId+=':different'}
    'different prepared source'={param($c)$c.cut.proposal.sourceOutboxItemId+=':different'}
    'different prepared fingerprint'={param($c)$c.cut.proposal.intentFingerprint+=':different'}
    'foreign prepared scope'={param($c)$c.cut.proposal.scope.persistenceScope.value='foreign'}
    'wrong source owner'={param($c)$c.beforeCollection.source.deliveringOwnerId='foreign'}
    'wrong source fence'={param($c)$c.beforeCollection.source.deliveryFencingToken++}
    'expired rather than collected proposal'={param($c)$c.beforeCollection.source.deliveryVisibleAfter='2000-01-01T00:00:00Z'}
    'missing source capture'={param($c)$c.cut.proposal.starts[0].sourceCapture.version=0}
    'different captured publication'={param($c)$c.cut.proposal.starts[0].sourceCapture.provenance.publicationId='foreign'}
    'different captured material hash'={param($c)$c.cut.proposal.starts[0].executable.artifactHash='foreign'}
    'different captured reference'={param($c)$c.cut.proposal.starts[0].sourceCapture.provenance.sourceReferenceId='foreign'}
    'reference not retired'={param($c)@($c.beforeCollection.observedSourceReferences | Where-Object sourceReferenceId -eq $c.collectedPublication.sourceReferenceId)[0].deletedAt=$null}
    'material not actually collected'={param($c)$c.afterCollection.observedMaterial+=@($c.beforeCollection.observedMaterial | Where-Object {$_.identity.artifactId -eq $c.collectedPublication.artifactId})}
    'reference not actually collected'={param($c)$c.afterCollection.observedSourceReferences+=@($c.beforeCollection.observedSourceReferences | Where-Object sourceReferenceId -eq $c.collectedPublication.sourceReferenceId)}
    'target already physically started'={param($c)$c.beforeCollection.allWorkflows+=@{artifactId=$c.collectedPublication.artifactId;workflowExecutionId='foreign'}}
    'target retained by an execution root'={param($c)$c.beforeCollection.executionRootArtifactIds+=$c.collectedPublication.artifactId}
    'target retained by a dispatch root'={param($c)$c.beforeCollection.dispatchRootArtifactIds+=$c.collectedPublication.artifactId}
    'never entered actual provider'={param($c)$c.collectedProposalRefusal.attemptedProviderInstall=$false}
    'refusal caused by cancellation'={param($c)$c.collectedProposalRefusal.cancellationRequested=$true}
    'stale claim instead of unavailable material'={param($c)$c.collectedProposalRefusal.innerFailureType='RuntimePostCommitOutboxStaleClaimException'}
    'wrong boundary classification'={param($c)$c.collectedProposalRefusal.isUnavailable=$true}
    'wrong missing artifact diagnostic'={param($c)$c.collectedProposalRefusal.dependencyGraphDiagnostic=$c.collectedProposalRefusal.dependencyGraphDiagnostic.Replace($c.collectedPublication.artifactId,'foreign')}
    'wrong graph failure kind'={param($c)$c.collectedProposalRefusal.dependencyGraphFailure='HashMismatch'}
    'wrong dependency path'={param($c)$c.collectedProposalRefusal.dependencyGraphDiagnostic+=' -> foreign'}
    'wrong refused claim identity'={param($c)$c.collectedProposalRefusal.fencingToken++}
    'claim expired before provider refusal'={param($c)$c.collectedProposalRefusal.observedAtUtc='2100-01-01T00:00:00Z'}
    'provider refusal rewrote source'={param($c)$c.afterProposalRefusal.source.rowRevision++}
    'provider refusal created a target'={param($c)$c.afterProposalRefusal.allWorkflows+=@{artifactId=$c.collectedPublication.artifactId;workflowExecutionId='foreign'}}
}
foreach($entry in $mutations.GetEnumerator()){
    $changed=$case | ConvertTo-Json -Depth 90 | ConvertFrom-Json -AsHashtable -DateKind String
    & $entry.Value $changed
    $rejected=$false
    try{Assert-Fixture $changed}catch{$rejected=$true}
    if(-not $rejected){throw "The actual guards accepted $($entry.Key)."}
    Write-Host "PASS refuses $($entry.Key)"
}
