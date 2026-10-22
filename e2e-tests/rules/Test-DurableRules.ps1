<#
.SYNOPSIS
    Run the durable Rules native workflow journey against an isolated rebuilt Workbench host.
.DESCRIPTION
    Starts only the Workbench assembly supplied by -AssemblyPath, composes the package's Rules features over
    fresh SQLite files, and drives real login/catalog/submit/publish/start APIs. Sequence and Flowchart success
    runs expose the typed RunRules result through a normal SetOutput ActivityResult binding so the existing
    workflow-instance API can verify its inline terminal facts. Additional Sequence runs cover budget fault and
    semantic refusal. This does not claim REST publication provenance, package provenance beyond the supplied
    deployment receipt, or parent cancellation while an invocation is waiting.
.EXAMPLE
    pwsh ./e2e-tests/rules/Test-DurableRules.ps1 -AssemblyPath <rebuilt-Elsa.Workbench.dll> `
        -DeploymentReceiptPath <package-deployment-receipt.json> -EvidencePath ./artifacts/rules-e2e.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $AssemblyPath,
    [Parameter(Mandatory)][string] $DeploymentReceiptPath,
    [string] $EvidencePath,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [int] $StartupTimeoutSeconds = 300
)

. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

$assemblyItem = Get-Item -LiteralPath $AssemblyPath -ErrorAction Stop
if ($assemblyItem.PSIsContainer -or $assemblyItem.Extension -ne '.dll') {
    throw '-AssemblyPath must identify a rebuilt Elsa.Workbench.dll file.'
}
$assemblyPath = [IO.Path]::GetFullPath($assemblyItem.FullName)
$receiptItem = Get-Item -LiteralPath $DeploymentReceiptPath -ErrorAction Stop
if ($receiptItem.PSIsContainer) { throw '-DeploymentReceiptPath must identify a deployment receipt file.' }
$receiptPath = [IO.Path]::GetFullPath($receiptItem.FullName)
$hostDepsPath = [IO.Path]::ChangeExtension($assemblyPath, '.deps.json')
if (-not (Test-Path -LiteralPath $hostDepsPath -PathType Leaf)) {
    throw "The active dependency manifest was not found beside the supplied Workbench assembly: $hostDepsPath"
}
$hostRoot = [IO.Path]::GetDirectoryName($assemblyPath)
$baseUrl = Resolve-ElsaServerBaseUrl
$contentRoot = New-ElsaContentRoot -Prefix 'elsa-rules-e2e'
$success = $false
$runEvidence = [System.Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::UtcNow

function Set-RulesFeatureConfiguration {
    param(
        [Parameter(Mandatory)][string] $ContentRoot,
        [Parameter(Mandatory)][string] $HostRoot,
        [Parameter(Mandatory)][string] $HostDepsPath,
        [Parameter(Mandatory)][string] $DeploymentReceiptPath
    )

    $settingsPath = Join-Path $ContentRoot 'shells.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $features = $settings.CShells.Shells.default.Features
    if (-not $features) { throw 'The copied Workbench shell configuration has no default feature map.' }

    $rulesDatabase = Join-Path $ContentRoot 'rules-runtime.db'
    $featureSettings = @{
        RulesRuntime = @{
            HostRoot = $HostRoot
            HostDepsPath = $HostDepsPath
            DeploymentReceiptPath = $DeploymentReceiptPath
            MaxConcurrentAttempts = 1
            PollIntervalMilliseconds = 10
        }
        RulesRuntimeEntityFrameworkCore = @{
            Provider = 'Sqlite'
            ConnectionString = "Data Source=$rulesDatabase;Pooling=False"
            Pooling = $false
            WorkLeaseSeconds = 60
            DeliveryLeaseSeconds = 60
        }
        RulesWorkflowsDesign = @{}
        RulesWorkflowsRuntime = @{
            MaxConcurrentDeliveries = 1
            PollIntervalMilliseconds = 10
        }
    }

    foreach ($featureName in $featureSettings.Keys) {
        $features | Add-Member -MemberType NoteProperty -Name $featureName -Value $featureSettings[$featureName] -Force
    }
    $settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $settingsPath -Encoding utf8
}

function New-RulesActivityNode {
    param(
        [Parameter(Mandatory)][string] $NodeId,
        [Parameter(Mandatory)][string] $VersionId,
        [Parameter(Mandatory)][string] $Program,
        [Parameter(Mandatory)][string[]] $InitialFacts,
        [Parameter(Mandatory)][int] $MaxRounds
    )

    New-ActivityNode -NodeId $NodeId -VersionId $VersionId -Inputs @(
        (New-LiteralInput -ReferenceKey 'Program' -Value $Program),
        (New-LiteralInput -ReferenceKey 'InitialFacts' -Value $InitialFacts),
        (New-LiteralInput -ReferenceKey 'MaxRounds' -Value $MaxRounds)
    )
}

function New-RulesResultCaptureNode {
    param([Parameter(Mandatory)][string] $ProducerScopeId)

    $resultBinding = @{
        value = @{
            producerNodeId = 'rules'
            projectionKey = '$result'
            producerScopeId = $ProducerScopeId
        }
        expressionType = 'ActivityResult'
    }
    New-SetOutputNode -NodeId 'capture-rules-result' -OutputName 'rulesResult' `
        -Value $resultBinding -ValueAlias 'Elsa.Any'
}

function Get-FieldValue {
    param([AllowNull()] $Object, [Parameter(Mandatory)][string] $Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary] -and $Object.Contains($Name)) { return $Object[$Name] }
    $property = $Object.PSObject.Properties[$Name]
    if ($property) { return $property.Value }
    return $null
}

function Convert-DiagnosticSnapshot {
    param([AllowNull()] $Snapshot)
    if ($null -eq $Snapshot) { return $null }

    $snapshotKind = Get-FieldValue -Object $Snapshot -Name 'kind'
    switch ($snapshotKind) {
        'object' {
            if ((Get-FieldValue -Object $Snapshot -Name 'truncated') -eq $true) {
                throw 'The native Rules result diagnostic snapshot was truncated.'
            }
            $properties = @(Get-FieldValue -Object $Snapshot -Name 'properties')
            $values = [ordered]@{}
            foreach ($property in $properties) {
                $name = [string](Get-FieldValue -Object $property -Name 'name')
                if ([string]::IsNullOrWhiteSpace($name) -or $name -eq '...') {
                    throw 'The native Rules result diagnostic snapshot omitted an object property.'
                }
                $values[$name] = Convert-DiagnosticSnapshot (Get-FieldValue -Object $property -Name 'value')
            }
            return [pscustomobject]$values
        }
        'array' {
            if ((Get-FieldValue -Object $Snapshot -Name 'truncated') -eq $true) {
                throw 'The native Rules result diagnostic snapshot truncated its fact array.'
            }
            $items = [System.Collections.Generic.List[object]]::new()
            foreach ($item in @(Get-FieldValue -Object $Snapshot -Name 'items')) {
                $items.Add((Convert-DiagnosticSnapshot $item))
            }
            $itemCount = Get-FieldValue -Object $Snapshot -Name 'itemCount'
            if ($null -ne $itemCount -and [int]$itemCount -ne $items.Count) {
                throw 'The native Rules result diagnostic snapshot did not retain every array item.'
            }
            return ,$items.ToArray()
        }
        'string' {
            if ((Get-FieldValue -Object $Snapshot -Name 'truncated') -eq $true) {
                throw 'A string in the native Rules result diagnostic snapshot was truncated.'
            }
            return Get-FieldValue -Object $Snapshot -Name 'preview'
        }
        { $_ -in @('number', 'scalar') } { return Get-FieldValue -Object $Snapshot -Name 'value' }
        'null' { return $null }
        default { return $Snapshot }
    }
}

function Get-NativeResultSummary {
    param([Parameter(Mandatory)] $Instance)

    $outputProperty = @($Instance.outputs.PSObject.Properties | Where-Object { $_.Name -ceq 'rulesResult' })
    if ($outputProperty.Count -ne 1) { throw 'The public workflow-instance response did not contain exactly one rulesResult output.' }
    $output = $outputProperty[0].Value
    if ((Get-FieldValue -Object $output -Name 'isRedacted') -ne $false) {
        throw 'The public workflow-instance API did not expose the native Rules result inline.'
    }

    $payload = Convert-DiagnosticSnapshot (Get-FieldValue -Object $output -Name 'value')
    $kindValue = Get-FieldValue -Object $payload -Name 'kind'
    $kindText = [string]$kindValue
    $kind = switch ($kindText) {
        { $_ -in @('Succeeded', '0') } { 'Succeeded'; break }
        { $_ -in @('Faulted', '1') } { 'Faulted'; break }
        { $_ -in @('Cancelled', '2') } { 'Cancelled'; break }
        { $_ -in @('Refused', '3') } { 'Refused'; break }
        default { throw "The public Rules result had an unknown terminal kind '$kindText'." }
    }
    $facts = @(Get-FieldValue -Object $payload -Name 'resultFacts')
    $atoms = @($facts | ForEach-Object { [string](Get-FieldValue -Object $_ -Name 'atom') } | Sort-Object -CaseSensitive)
    if ($atoms | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
        throw 'The public Rules result contained an empty fact atom.'
    }

    $diagnostic = Get-FieldValue -Object $payload -Name 'diagnostic'
    [pscustomobject]@{
        Kind = $kind
        CommittedRounds = [int](Get-FieldValue -Object $payload -Name 'committedRounds')
        ResultSchemaVersion = [int](Get-FieldValue -Object $payload -Name 'resultSchemaVersion')
        ResultFacts = $atoms
        DiagnosticCode = Get-FieldValue -Object $diagnostic -Name 'code'
        FinalFactsSha256 = [string](Get-FieldValue -Object $payload -Name 'finalFactsSha256')
        IsRedacted = $false
        OutputType = Get-FieldValue -Object $output -Name 'type'
    }
}

function Get-RequiredNodeExecution {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $NodeId)
    $matches = @($Instance.activities | Where-Object { $_.executableNodeId -ceq $NodeId })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one activity execution for '$NodeId'; found $($matches.Count)."
    }
    return $matches[0]
}

function Invoke-PublishedWorkflow {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)] $RootActivity,
        [Parameter(Mandatory)][string] $Name
    )

    $definition = Submit-Workflow -Ctx $Context -Name $Name -Description 'Durable Rules native runtime acceptance.' -RootActivity $RootActivity
    $publication = Publish-WorkflowVersion -Ctx $Context -VersionId $definition.version.id
    $execution = Invoke-Artifact -Ctx $Context -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    if ($execution.commandDispatchStatus -ne 'Accepted') {
        throw "Workflow start was not accepted: $($execution.commandDispatchStatus)."
    }
    $instance = Wait-WorkflowInstance -Ctx $Context -ExecutionId $execution.workflowExecutionId -TimeoutSeconds 180
    return [pscustomobject]@{
        Definition = $definition
        Publication = $publication
        Execution = $execution
        Instance = $instance
    }
}

function Assert-SuccessfulRulesResult {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $Scenario)

    if ($Instance.instance.status -notin @('Completed', 'Finished')) {
        throw "$Scenario workflow ended '$($Instance.instance.status)', expected Completed."
    }
    $rules = Get-RequiredNodeExecution -Instance $Instance -NodeId 'rules'
    $after = Get-RequiredNodeExecution -Instance $Instance -NodeId 'after-rules'
    $capture = Get-RequiredNodeExecution -Instance $Instance -NodeId 'capture-rules-result'
    if ($rules.status -ne 'Completed' -or $after.status -ne 'Completed' -or $capture.status -ne 'Completed') {
        throw "$Scenario did not complete RunRules, its following WriteLine, and the result capture activity."
    }
    if ((Get-NodeRunCount -Instance $Instance -NodeId 'after-rules') -ne 1) {
        throw "$Scenario following WriteLine did not execute exactly once."
    }
    if (@($Instance.incidents).Count -ne 0) { throw "$Scenario completed with $(@($Instance.incidents).Count) incident(s)." }

    $result = Get-NativeResultSummary -Instance $Instance
    if ($result.Kind -ne 'Succeeded' -or $result.CommittedRounds -ne 2 -or $result.ResultSchemaVersion -ne 1 -or
        $result.DiagnosticCode -or $result.FinalFactsSha256 -notmatch '^[0-9a-f]{64}$' -or
        ($result.ResultFacts -join "`n") -cne "derived`nseed") {
        throw "$Scenario native inline result mismatch: kind=$($result.Kind), rounds=$($result.CommittedRounds), facts=$($result.ResultFacts -join ',')."
    }
    return $result
}

try {
    Set-RulesFeatureConfiguration -ContentRoot $contentRoot -HostRoot $hostRoot `
        -HostDepsPath $hostDepsPath -DeploymentReceiptPath $receiptPath
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot -AssemblyPath $assemblyPath `
        -TimeoutSec $StartupTimeoutSeconds

    $ctx = Connect-Elsa -BaseUrl $baseUrl -Username $Username -Password $Password
    $runRulesVersion = Invoke-Step 'resolve RunRules catalog entry' {
        Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Rules.Workflows.Runtime.Activities.RunRules'
    }
    $writeLineVersion = Invoke-Step 'resolve WriteLine catalog entry' {
        Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    }
    $sequenceVersion = Invoke-Step 'resolve Sequence catalog entry' {
        Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    }
    $flowchartVersion = Invoke-Step 'resolve Flowchart catalog entry' {
        Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart'
    }

    $program = '{"formatVersion":1,"languageId":"elsa.rules.finite-positive","languageVersion":1,"vocabulary":["derived","seed"],"rules":[{"id":"derive","premises":["seed"],"conclusion":"derived"}]}'
    $sequenceId = "RulesSequence-$([guid]::NewGuid().ToString('N'))"
    $sequenceRules = New-RulesActivityNode -NodeId 'rules' -VersionId $runRulesVersion -Program $program -InitialFacts @('seed') -MaxRounds 4
    $sequenceAfter = New-ActivityNode -NodeId 'after-rules' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'Sequence continued after durable Rules completion.')
    )
    $sequenceCapture = New-RulesResultCaptureNode -ProducerScopeId 'sequence-root'
    $sequenceRoot = New-ActivityNode -NodeId 'sequence-root' -VersionId $sequenceVersion -Structure (
        New-SequenceStructure -Activities @($sequenceRules, $sequenceAfter, $sequenceCapture)
    )
    $sequenceRun = Invoke-Step 'submit, publish and start Sequence Rules workflow' {
        Invoke-PublishedWorkflow -Context $ctx -RootActivity $sequenceRoot -Name $sequenceId
    }
    $sequenceResult = Assert-SuccessfulRulesResult -Instance $sequenceRun.Instance -Scenario 'Sequence'
    $runEvidence.Add([pscustomobject]@{
        Scenario = 'SequenceSucceeded'
        WorkflowExecutionId = $sequenceRun.Execution.workflowExecutionId
        ArtifactId = $sequenceRun.Publication.artifactId
        Status = $sequenceRun.Instance.instance.status
        RulesActivityStatus = (Get-RequiredNodeExecution -Instance $sequenceRun.Instance -NodeId 'rules').status
        FollowingActivityStatus = (Get-RequiredNodeExecution -Instance $sequenceRun.Instance -NodeId 'after-rules').status
        NativeResult = $sequenceResult
    })

    $flowchartId = "RulesFlowchart-$([guid]::NewGuid().ToString('N'))"
    $flowRules = New-RulesActivityNode -NodeId 'rules' -VersionId $runRulesVersion -Program $program -InitialFacts @('seed') -MaxRounds 4
    $flowAfter = New-ActivityNode -NodeId 'after-rules' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'Flowchart continued after durable Rules completion.')
    )
    $flowCapture = New-RulesResultCaptureNode -ProducerScopeId 'flowchart-root'
    $flowchartStructure = @{
        kind = 'elsa.flowchart.structure'
        schemaVersion = '1.0.0'
        payload = @{
            startNodeId = 'rules'
            activities = @($flowRules, $flowAfter, $flowCapture)
            connections = @(
                @{ id = 'rules-to-after'; source = @{ nodeId = 'rules'; port = 'Done' }; target = @{ nodeId = 'after-rules' } },
                @{ id = 'after-to-capture'; source = @{ nodeId = 'after-rules'; port = 'Done' }; target = @{ nodeId = 'capture-rules-result' } }
            )
            nodeMetadata = @{}
            connectionMetadata = @{}
        }
    }
    $flowchartRoot = New-ActivityNode -NodeId 'flowchart-root' -VersionId $flowchartVersion -Structure $flowchartStructure
    $flowchartRun = Invoke-Step 'submit, publish and start Flowchart Rules workflow' {
        Invoke-PublishedWorkflow -Context $ctx -RootActivity $flowchartRoot -Name $flowchartId
    }
    $flowchartResult = Assert-SuccessfulRulesResult -Instance $flowchartRun.Instance -Scenario 'Flowchart'
    $runEvidence.Add([pscustomobject]@{
        Scenario = 'FlowchartSucceeded'
        WorkflowExecutionId = $flowchartRun.Execution.workflowExecutionId
        ArtifactId = $flowchartRun.Publication.artifactId
        Status = $flowchartRun.Instance.instance.status
        RulesActivityStatus = (Get-RequiredNodeExecution -Instance $flowchartRun.Instance -NodeId 'rules').status
        FollowingActivityStatus = (Get-RequiredNodeExecution -Instance $flowchartRun.Instance -NodeId 'after-rules').status
        NativeResult = $flowchartResult
    })

    $budgetId = "RulesBudgetFault-$([guid]::NewGuid().ToString('N'))"
    $budgetRules = New-RulesActivityNode -NodeId 'rules' -VersionId $runRulesVersion -Program $program -InitialFacts @('seed') -MaxRounds 1
    $budgetAfter = New-ActivityNode -NodeId 'after-rules' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'This line must not run after a Rules budget fault.')
    )
    $budgetCapture = New-RulesResultCaptureNode -ProducerScopeId 'budget-root'
    $budgetRoot = New-ActivityNode -NodeId 'budget-root' -VersionId $sequenceVersion -Structure (
        New-SequenceStructure -Activities @($budgetRules, $budgetAfter, $budgetCapture)
    )
    $budgetRun = Invoke-Step 'submit, publish and start round-budget fault workflow' {
        Invoke-PublishedWorkflow -Context $ctx -RootActivity $budgetRoot -Name $budgetId
    }
    if ($budgetRun.Instance.instance.status -ne 'Faulted') {
        throw "The one-round Rules budget workflow ended '$($budgetRun.Instance.instance.status)', expected Faulted."
    }
    $budgetRuleExecution = Get-RequiredNodeExecution -Instance $budgetRun.Instance -NodeId 'rules'
    if ($budgetRuleExecution.status -ne 'Faulted' -or
        (Get-NodeRunCount -Instance $budgetRun.Instance -NodeId 'after-rules') -ne 0 -or
        (Get-NodeRunCount -Instance $budgetRun.Instance -NodeId 'capture-rules-result') -ne 0 -or
        [int]$budgetRuleExecution.incidentCount -lt 1) {
        throw 'The one-round Rules budget fault did not fault RunRules with a blocking incident before its following activities.'
    }
    $runEvidence.Add([pscustomobject]@{
        Scenario = 'RoundBudgetFault'
        WorkflowExecutionId = $budgetRun.Execution.workflowExecutionId
        ArtifactId = $budgetRun.Publication.artifactId
        Status = $budgetRun.Instance.instance.status
        RulesActivityStatus = $budgetRuleExecution.status
        RulesIncidentCount = $budgetRuleExecution.incidentCount
        FollowingActivityCount = Get-NodeRunCount -Instance $budgetRun.Instance -NodeId 'after-rules'
        ExactRulesDiagnosticAvailableFromRest = $false
    })

    $refusalId = "RulesSemanticRefusal-$([guid]::NewGuid().ToString('N'))"
    $refusalRules = New-RulesActivityNode -NodeId 'rules' -VersionId $runRulesVersion -Program $program `
        -InitialFacts @('outside-vocabulary') -MaxRounds 4
    $refusalAfter = New-ActivityNode -NodeId 'after-rules' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'Sequence continued after a retained Rules refusal.')
    )
    $refusalCapture = New-RulesResultCaptureNode -ProducerScopeId 'refusal-root'
    $refusalRoot = New-ActivityNode -NodeId 'refusal-root' -VersionId $sequenceVersion -Structure (
        New-SequenceStructure -Activities @($refusalRules, $refusalAfter, $refusalCapture)
    )
    $refusalRun = Invoke-Step 'submit, publish and start semantic-refusal workflow' {
        Invoke-PublishedWorkflow -Context $ctx -RootActivity $refusalRoot -Name $refusalId
    }
    $refusalInstance = $refusalRun.Instance
    if ($refusalInstance.instance.status -notin @('Completed', 'Finished') -or
        (Get-RequiredNodeExecution -Instance $refusalInstance -NodeId 'rules').status -ne 'Completed' -or
        (Get-NodeRunCount -Instance $refusalInstance -NodeId 'after-rules') -ne 1) {
        throw 'The out-of-vocabulary Rules refusal did not complete as a retained result and continue the Sequence.'
    }
    $refusalResult = Get-NativeResultSummary -Instance $refusalInstance
    if ($refusalResult.Kind -ne 'Refused' -or $refusalResult.CommittedRounds -ne 0 -or
        $refusalResult.DiagnosticCode -ne 'rules.invalid-finite-model' -or $refusalResult.ResultFacts.Count -ne 0) {
        throw "The public native refusal result did not match the expected bounded refusal: kind=$($refusalResult.Kind), code=$($refusalResult.DiagnosticCode)."
    }
    $runEvidence.Add([pscustomobject]@{
        Scenario = 'SemanticRefusal'
        WorkflowExecutionId = $refusalRun.Execution.workflowExecutionId
        ArtifactId = $refusalRun.Publication.artifactId
        Status = $refusalInstance.instance.status
        RulesActivityStatus = (Get-RequiredNodeExecution -Instance $refusalInstance -NodeId 'rules').status
        FollowingActivityStatus = (Get-RequiredNodeExecution -Instance $refusalInstance -NodeId 'after-rules').status
        NativeResult = $refusalResult
    })

    # Restart only after every journey has reached a terminal native/workflow state. This checks
    # ordinary persisted REST reads after reconnect; it is not a mid-flight Rules recovery test.
    $processIdBeforeRestart = $script:OwnedElsaServer.Process.Id
    $assemblyPathBeforeRestart = [IO.Path]::GetFullPath($script:OwnedElsaServer.AssemblyPath)
    Invoke-Step 'restart the owned Workbench after completed Rules journeys' { Restart-OwnedElsaServer }
    $assemblyPathAfterRestart = [IO.Path]::GetFullPath($script:OwnedElsaServer.AssemblyPath)
    if ($assemblyPathAfterRestart -cne $assemblyPath -or $assemblyPathBeforeRestart -cne $assemblyPath) {
        throw 'The owned server restart did not retain the explicitly supplied Workbench assembly path.'
    }
    $processIdAfterRestart = $script:OwnedElsaServer.Process.Id
    $ctx = Connect-Elsa -BaseUrl $baseUrl -Username $Username -Password $Password
    $persistedSequence = Get-WorkflowInstance -Ctx $ctx -ExecutionId $sequenceRun.Execution.workflowExecutionId
    $persistedSequenceResult = Assert-SuccessfulRulesResult -Instance $persistedSequence -Scenario 'Sequence after restart'
    $persistedFlowchart = Get-WorkflowInstance -Ctx $ctx -ExecutionId $flowchartRun.Execution.workflowExecutionId
    $persistedFlowchartResult = Assert-SuccessfulRulesResult -Instance $persistedFlowchart -Scenario 'Flowchart after restart'
    $restartEvidence = [pscustomobject]@{
        PreviousProcessId = $processIdBeforeRestart
        ReconnectedProcessId = $processIdAfterRestart
        WorkbenchAssemblyPath = $assemblyPathAfterRestart
        SequenceStatus = $persistedSequence.instance.status
        SequenceNativeResult = $persistedSequenceResult
        FlowchartStatus = $persistedFlowchart.instance.status
        FlowchartNativeResult = $persistedFlowchartResult
    }

    $evidence = [pscustomobject]@{
        passed = $true
        SchemaVersion = 1
        StartedAt = $startedAt
        CompletedAt = [DateTimeOffset]::UtcNow
        WorkbenchAssembly = [IO.Path]::GetFileName($assemblyPath)
        WorkbenchAssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        DeploymentReceiptFile = [IO.Path]::GetFileName($receiptPath)
        DeploymentReceiptSha256 = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash.ToLowerInvariant()
        RestartVerification = $restartEvidence
        Runs = $runEvidence.ToArray()
    }
    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $resolvedEvidencePath = [IO.Path]::GetFullPath($EvidencePath)
        $evidenceDirectory = [IO.Path]::GetDirectoryName($resolvedEvidencePath)
        if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
            New-Item -Path $evidenceDirectory -ItemType Directory -Force | Out-Null
        }
        $evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $resolvedEvidencePath -Encoding utf8
        Write-Host "[evidence] wrote $resolvedEvidencePath"
    }
    $success = $true
    Write-Host "SUCCESS - durable Rules REST journey passed $($runEvidence.Count) scenarios on the owned Workbench process." -ForegroundColor Green
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
