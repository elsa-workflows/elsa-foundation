<#
.SYNOPSIS
    Studio-shaped Set Variable assignments update an Int32 counter and print 1 through 10 (#3316).
.DESCRIPTION
    Owns a rebuilt Workbench on a free loopback port with disposable SQLite data. All Set nodes keep
    Studio's Elsa.Any authoring fallback. Checks literal initialization, referenced numeric widening, ten JavaScript increments,
    subsequent reads, exact ordered stdout, completion without incidents, and cleanup. Build Workbench first.
#>
[CmdletBinding()]
param()
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

$contentRoot = New-ElsaContentRoot -Prefix 'elsa-set-variable-3316'
$baseUrl = Resolve-ElsaServerBaseUrl
$success = $false
try {
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot
    $ctx = Connect-Elsa -BaseUrl $baseUrl
    $sequence = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $while = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.While.Activities.While'
    $writeLine = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    $marker = "SetVariable3316-$([guid]::NewGuid().ToString('N')):"

    $initialize = New-SetVariableNode -NodeId 'initialize' -VariableKey 'counter' -Value 0 -ValueAlias 'Elsa.Any'
    $copy = New-SetVariableNode -NodeId 'copy' -VariableKey 'copy' -ValueAlias 'Elsa.Any' `
        -Value @{ value = 'counter'; expressionType = 'Variable' }
    $increment = New-SetVariableNode -NodeId 'increment' -VariableKey 'counter' -ValueAlias 'Elsa.Any' `
        -Value @{ value = "getVariable('counter') + 1"; expressionType = 'JavaScript' }
    $print = New-ActivityNode -NodeId 'print' -VersionId $writeLine -Inputs @(
        @{ referenceKey = 'text'; value = @{ value = "'$marker' + getVariable('counter')"; expressionType = 'JavaScript' } }
    )
    $body = New-ActivityNode -NodeId 'body' -VersionId $sequence -Structure (New-SequenceStructure -Activities @($increment, $print))
    $loop = New-ActivityNode -NodeId 'loop' -VersionId $while -Inputs @(
        @{ referenceKey = 'Condition'; value = @{ value = "getVariable('copy') === 0 && getVariable('counter') < 10"; expressionType = 'JavaScript' } }
    ) -Structure (New-WhileStructure -Body $body)
    $root = New-ActivityNode -NodeId 'root' -VersionId $sequence -Structure (New-SequenceStructure -Activities @($initialize, $copy, $loop))

    # A different default makes the literal initialization observable in the loop's first condition.
    $variables = @((New-VariableDef -Key 'counter' -Alias 'Int32' -Default 99), (New-VariableDef -Key 'copy' -Alias 'Int64' -Default 99))
    $analysisBody = @{
        nodeId = 'increment'
        state = @{ variables = $variables; rootActivity = $root; inputs = @(); outputs = @(); workflowActivityOptions = $null; strategyOptions = $null }
    } | ConvertTo-Json -Depth 30
    $analysis = Invoke-RestMethod "$baseUrl/design/workflows/scoped-variables/analyze" -Method Post `
        -WebSession $ctx.Session -ContentType 'application/json' -Body $analysisBody
    $target = @($analysis.visibleVariables | Where-Object { $_.referenceKey -eq 'counter' -and $_.scopeId -eq 'workflow' })
    if ($target.Count -ne 1 -or $target[0].type.alias -ne 'Int32' -or $target[0].type.collectionKind -ne 'Single') {
        throw 'The Studio authoring response must identify the selected variable as a scalar Int32.'
    }
    $definition = Submit-Workflow -Ctx $ctx -Name 'Set Variable typed counter regression' -RootActivity $root -Variables $variables
    $publication = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 30
    Show-WorkflowInstance -Instance $instance

    if ($instance.instance.status -ne 'Completed' -or $instance.instance.incidentCount -ne 0 -or @($instance.incidents).Count -ne 0) {
        throw 'The typed counter workflow must complete with zero incidents.'
    }
    foreach ($node in @('increment', 'print')) {
        if ((Get-NodeRunCount -Instance $instance -NodeId $node) -ne 10) { throw "Expected ten executions of $node." }
        if (@($instance.activities | Where-Object { $_.executableNodeId -eq $node -and $_.status -ne 'Completed' }).Count -ne 0) {
            throw "A $node execution did not complete."
        }
    }
    $actual = @(Get-Content -LiteralPath (Join-Path $contentRoot 'server-1.out.log') | Where-Object { $_.StartsWith($marker) })
    $expected = @(1..10 | ForEach-Object { "$marker$_" })
    if ([string]::Join("`n", $actual) -cne [string]::Join("`n", $expected)) {
        throw "Expected printed values 1 through 10 in order; got: $($actual -join ', ')"
    }
    Write-Host 'SUCCESS - Elsa.Any Set inputs updated the Int32 variable; printed 1..10 in order; zero incidents.' -ForegroundColor Green
    $success = $true
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
if (Test-Path -LiteralPath $contentRoot) { throw "Disposable content root was not removed: $contentRoot" }
Write-Host 'Cleanup verified: owned server stopped and disposable data removed.'
