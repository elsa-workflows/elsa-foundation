<#
.SYNOPSIS
    Republish with a retained v1 Any Set artifact, then execute the corrected Int32 assignment (#3331).
.DESCRIPTION
    Owns an isolated rebuilt Workbench and SQLite database. Imports the frozen historical artifact first,
    then publishes the same authored expression/declarations. Requires a distinct current executable,
    an unchanged retained artifact, an observed numeric variable write, zero incidents, and cleanup.
#>
[CmdletBinding()]
param()
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

$contentRoot = New-ElsaContentRoot -Prefix 'elsa-retained-set-3331'
$baseUrl = Resolve-ElsaServerBaseUrl
$success = $false
try {
    $fixture = Join-Path $PSScriptRoot '../../Essentials/tests/Workflows/Fixtures/legacy-set-v1.json'
    $legacy = Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json -AsHashtable
    $closure = @{
        formatVersion = 1
        rootArtifactId = $legacy.identity.artifactId
        artifacts = @($legacy)
        sourceReferences = @()
        triggerBindings = @()
    }
    $mount = Join-Path $contentRoot 'legacy.json'
    $closure | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $mount
    $options = 'CShells__Shells__default__Features__JsonWorkflowArtifactReconciliation__Options__'
    $environment = @{
        "${options}SourceId" = 'retained-set-3331'
        "${options}FilePath" = $mount
        "${options}TenantId" = 'default'
    }
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot -Environment $environment
    $ctx = Connect-Elsa -BaseUrl $baseUrl
    $retainedBefore = Invoke-RestMethod "$baseUrl/runtime/workflows/executables/$($legacy.identity.artifactId)" -WebSession $ctx.Session
    if ($retainedBefore.artifactHash -ne $legacy.identity.artifactHash) { throw 'The historical artifact was not retained under its v1 identity.' }

    # The old body reproduces the reported poison. Keep that execution as historical evidence;
    # publishing the correction must not rewrite it into a different artifact or clear its incident.
    $oldRun = Invoke-Artifact -Ctx $ctx -ArtifactId $legacy.identity.artifactId
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $oldInstance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $oldRun.workflowExecutionId
    } while ($oldInstance.instance.incidentCount -eq 0 -and (Get-Date) -lt $deadline)
    if ($oldInstance.instance.incidentCount -ne 1 -or $oldInstance.incidents[0].message -notmatch "value type 'Elsa.Any'.*type 'Int32'") {
        throw 'The retained fixture did not reproduce the historical assignment incident.'
    }

    $set = New-SetVariableNode -NodeId 'set-counter' -VariableKey 'counter' -ValueAlias 'Elsa.Any' `
        -Value @{ value = '7'; expressionType = 'JavaScript' }
    $counter = New-VariableDef -Key 'counter' -Alias 'Int32' -Default 0
    $counter.name = 'Counter'
    $definition = Submit-Workflow -Ctx $ctx -Name 'Retained Set artifact upgrade' -RootActivity $set -Variables @($counter)
    $publication = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    if ($publication.artifactId -eq $legacy.identity.artifactId -or $publication.artifactHash -eq $legacy.identity.artifactHash) {
        throw 'The corrected assignment reused the historical Any executable.'
    }
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 30
    Show-WorkflowInstance -Instance $instance
    if ($instance.instance.status -ne 'Completed' -or $instance.instance.incidentCount -ne 0 -or @($instance.incidents).Count -ne 0) {
        throw 'The republished assignment must complete with zero incidents.'
    }
    $evidence = Invoke-RestMethod "$baseUrl/_elsa/execution-evidence/workflows/$($run.workflowExecutionId)?waitMs=5000" -WebSession $ctx.Session
    $writes = @($evidence.records | Where-Object { $_.kind -eq 'VariableSet' -and $_.name -eq 'counter' -and $_.value -eq 7 })
    if ($writes.Count -ne 1 -or $writes[0].value -is [string]) { throw 'Expected one committed numeric counter write of 7.' }
    $retainedAfter = Invoke-RestMethod "$baseUrl/runtime/workflows/executables/$($legacy.identity.artifactId)" -WebSession $ctx.Session
    if ($retainedAfter.artifactHash -ne $retainedBefore.artifactHash -or $retainedAfter.createdAt -ne $retainedBefore.createdAt) {
        throw 'Republishing changed the retained artifact.'
    }
    $oldAfter = Get-WorkflowInstance -Ctx $ctx -ExecutionId $oldRun.workflowExecutionId
    if ($oldAfter.instance.artifactId -ne $legacy.identity.artifactId -or
        $oldAfter.instance.incidentCount -ne 1 -or $oldAfter.incidents[0].incidentId -ne $oldInstance.incidents[0].incidentId) {
        throw 'Republishing changed the historical execution or its incident.'
    }
    $retry = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    if ($retry.artifactId -ne $publication.artifactId -or $retry.publicationId -ne $publication.publicationId) {
        throw 'Retrying the corrected publication was not idempotent.'
    }
    Write-Host "SUCCESS - retained $($legacy.identity.artifactId); published $($publication.artifactId); numeric counter=7; zero incidents." -ForegroundColor Green
    $success = $true
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
if (Test-Path -LiteralPath $contentRoot) { throw "Disposable content root was not removed: $contentRoot" }
Write-Host 'Cleanup verified: owned server stopped and disposable data removed.'
