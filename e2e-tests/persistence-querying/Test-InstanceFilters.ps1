<#
.SYNOPSIS
    Instance query filters: artifactId, definitionId, correlationId, and status each return the right subset.
.DESCRIPTION
    Exercises `GET runtime/workflows/instances` filter contracts. Sets up a controlled fixture:
      - Definition A run twice   -> 2 instances sharing artifact A / definition A.
      - Definition B run once     -> 1 instance (distinct artifact / definition).
      - Definition C sets a unique correlation id (SetCorrelationId) -> 1 correlated instance.
    Then asserts:
      - ?artifactId=A     -> exactly the 2 A instances (all carry artifact A).
      - ?definitionId=B   -> the B instance(s), all carrying definition B.
      - ?correlationId=U  -> exactly the 1 correlated instance.
      - ?status=Completed -> every returned instance is Completed, and our fixtures are present.
      - ?status=Faulted   -> our (completed) fixtures are absent.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_PersistenceCommon.ps1"

Write-Host "== Instance query filters (artifactId / definitionId / correlationId / status) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

# --- fixture A: run twice ---
$defA = Invoke-Step "publish A" { Submit-Workflow -Ctx $ctx -Name "FilterA-$tag" -RootActivity (New-ActivityNode -NodeId "a" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "A $tag") )) }
$pubA = Publish-WorkflowVersion -Ctx $ctx -VersionId $defA.version.id
$a1 = Invoke-Artifact -Ctx $ctx -ArtifactId $pubA.artifactId -SourceReferenceId $pubA.sourceReferenceId
$a2 = Invoke-Artifact -Ctx $ctx -ArtifactId $pubA.artifactId -SourceReferenceId $pubA.sourceReferenceId
Wait-WorkflowInstance -Ctx $ctx -ExecutionId $a1.workflowExecutionId | Out-Null
Wait-WorkflowInstance -Ctx $ctx -ExecutionId $a2.workflowExecutionId | Out-Null

# --- fixture B: run once (distinct content -> distinct artifact/definition) ---
$defB = Invoke-Step "publish B" { Submit-Workflow -Ctx $ctx -Name "FilterB-$tag" -RootActivity (New-ActivityNode -NodeId "b" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "B $tag") )) }
$pubB = Publish-WorkflowVersion -Ctx $ctx -VersionId $defB.version.id
$b1 = Invoke-Artifact -Ctx $ctx -ArtifactId $pubB.artifactId -SourceReferenceId $pubB.sourceReferenceId
Wait-WorkflowInstance -Ctx $ctx -ExecutionId $b1.workflowExecutionId | Out-Null

# --- fixture C: unique correlation id ---
$corr = "corr-$tag"
$defC = Invoke-Step "publish C" { Submit-Workflow -Ctx $ctx -Name "FilterC-$tag" -RootActivity (New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @( (New-SetCorrelationIdNode -NodeId "sc" -Value $corr), (New-SetOutputNode -NodeId "done" -OutputName "Done" -Value "ok") ))) }
$pubC = Publish-WorkflowVersion -Ctx $ctx -VersionId $defC.version.id
$c1 = Invoke-Artifact -Ctx $ctx -ArtifactId $pubC.artifactId -SourceReferenceId $pubC.sourceReferenceId
Wait-WorkflowInstance -Ctx $ctx -ExecutionId $c1.workflowExecutionId | Out-Null

$fail = @()

# artifactId filter -> exactly the 2 A instances  (@() forces an array so .Count is reliable)
$byArtifact = @(Get-InstancesRaw -Ctx $ctx -Query @{ artifactId = $pubA.artifactId })
if (($byArtifact.Count -eq 2) -and (@($byArtifact | Where-Object { $_.artifactId -ne $pubA.artifactId }).Count -eq 0)) { Write-Host ("  OK  artifactId -> {0} instances, all artifact A" -f $byArtifact.Count) -ForegroundColor Green } else { $fail += "artifactId (got $($byArtifact.Count), expected 2 all-A)" }

# definitionId filter -> B instance(s), all definition B
$byDef = @(Get-InstancesRaw -Ctx $ctx -Query @{ definitionId = $defB.definition.id })
if (($byDef.Count -ge 1) -and (@($byDef | Where-Object { $_.definitionId -ne $defB.definition.id }).Count -eq 0)) { Write-Host ("  OK  definitionId -> {0} instance(s), all definition B" -f $byDef.Count) -ForegroundColor Green } else { $fail += "definitionId (got $($byDef.Count))" }

# correlationId filter -> exactly the 1 correlated instance
$byCorr = @(Get-InstancesRaw -Ctx $ctx -Query @{ correlationId = $corr })
if (($byCorr.Count -eq 1) -and ($byCorr[0].correlationId -eq $corr)) { Write-Host ("  OK  correlationId -> exactly 1, correlationId '{0}'" -f $corr) -ForegroundColor Green } else { $fail += "correlationId (got $($byCorr.Count))" }

# status=Completed -> all returned Completed, and our A artifact appears
$completed = @(Get-InstancesRaw -Ctx $ctx -Query @{ status = "Completed"; artifactId = $pubA.artifactId })
if (($completed.Count -eq 2) -and (@($completed | Where-Object { $_.status -ne 'Completed' }).Count -eq 0)) { Write-Host ("  OK  status=Completed (scoped to A) -> {0}, all Completed" -f $completed.Count) -ForegroundColor Green } else { $fail += "status=Completed (got $($completed.Count))" }

# status=Faulted scoped to A -> none (A instances completed)
$faulted = @(Get-InstancesRaw -Ctx $ctx -Query @{ status = "Faulted"; artifactId = $pubA.artifactId })
if ($faulted.Count -eq 0) { Write-Host ("  OK  status=Faulted (scoped to A) -> 0 (A completed)") -ForegroundColor Green } else { $fail += "status=Faulted (got $($faulted.Count), expected 0)" }

Write-Host ""
if ($fail.Count -eq 0) {
    Write-Host "SUCCESS - all instance query filters returned the correct subset." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - failing filters: {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
