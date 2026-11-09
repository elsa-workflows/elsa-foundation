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
. "$PSScriptRoot/_RulesCommon.ps1"
. "$PSScriptRoot/../durability/_DurabilityCommon.ps1"

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
$nativeDatabasePath = Join-Path $contentRoot 'elsa.db'
$rulesDatabasePath = Join-Path $contentRoot 'rules-runtime.db'
$snapshotTool = (Resolve-Path "$PSScriptRoot/../../tools/rules/native_outcome_snapshot.py").Path
$python = if ($env:OS -eq 'Windows_NT') { 'python' } else { 'python3' }
$snapshotDirectory = if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($EvidencePath))
} else {
    $contentRoot
}
if ([string]::IsNullOrWhiteSpace($snapshotDirectory)) { throw 'Could not resolve the native snapshot output directory.' }
New-Item -Path $snapshotDirectory -ItemType Directory -Force | Out-Null
$success = $false
$runEvidence = [System.Collections.Generic.List[object]]::new()
$nativeOutcomeSnapshotEvidence = [System.Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::UtcNow

function Invoke-PublishedWorkflow {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)] $RootActivity,
        [Parameter(Mandatory)][string] $Name,
        [switch] $StartOnly
    )

    $definition = Submit-Workflow -Ctx $Context -Name $Name -Description 'Durable Rules native runtime acceptance.' -RootActivity $RootActivity
    $publication = Publish-WorkflowVersion -Ctx $Context -VersionId $definition.version.id
    $execution = Invoke-Artifact -Ctx $Context -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    if ($execution.commandDispatchStatus -ne 'Accepted') {
        throw "Workflow start was not accepted: $($execution.commandDispatchStatus)."
    }
    $instance = if ($StartOnly) {
        $null
    } else {
        Wait-WorkflowInstance -Ctx $Context -ExecutionId $execution.workflowExecutionId -TimeoutSeconds 180
    }
    return [pscustomobject]@{
        Definition = $definition
        Publication = $publication
        Execution = $execution
        Instance = $instance
    }
}

function Wait-HeldSiblingFlowchart {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)][string] $ExecutionId,
        [int] $TimeoutSeconds = 180
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $instance = Get-WorkflowInstance -Ctx $Context -ExecutionId $ExecutionId
        $rules = @($instance.activities | Where-Object { $_.executableNodeId -ceq 'rules' })
        $wait = @($instance.activities | Where-Object { $_.executableNodeId -ceq 'wait' })
        $capture = @($instance.activities | Where-Object { $_.executableNodeId -ceq 'capture-rules-result' })
        if ($rules.Count -eq 1 -and $rules[0].status -eq 'Completed' -and
            $wait.Count -eq 1 -and $wait[0].status -eq 'Suspended' -and
            $capture.Count -eq 1 -and $capture[0].status -eq 'Completed' -and
            $instance.instance.status -eq 'Running') {
            return $instance
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    if ($instance) { Show-WorkflowInstance -Instance $instance }
    throw "Flowchart '$ExecutionId' did not reach the bounded state where Rules and result capture are complete while its Event sibling remains suspended."
}

function Assert-HeldSiblingFlowchart {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $Scenario)

    if ($Instance.instance.status -ne 'Running' -or @($Instance.incidents).Count -ne 0) {
        throw "$Scenario parent was not Running without incidents while the Event sibling was held."
    }
    foreach ($nodeId in @('fork', 'rules', 'capture-rules-result', 'wait')) {
        if ((Get-NodeRunCount -Instance $Instance -NodeId $nodeId) -ne 1) {
            throw "$Scenario expected one activity record for '$nodeId'."
        }
    }
    if ((Get-RequiredNodeExecution -Instance $Instance -NodeId 'fork').status -ne 'Completed' -or
        (Get-RequiredNodeExecution -Instance $Instance -NodeId 'rules').status -ne 'Completed' -or
        (Get-RequiredNodeExecution -Instance $Instance -NodeId 'capture-rules-result').status -ne 'Completed' -or
        (Get-RequiredNodeExecution -Instance $Instance -NodeId 'wait').status -ne 'Suspended' -or
        (Get-NodeRunCount -Instance $Instance -NodeId 'join') -ne 0 -or
        (Get-NodeRunCount -Instance $Instance -NodeId 'after-rules') -ne 0) {
        throw "$Scenario did not retain the expected held-sibling state: Rules/capture complete, Event suspended, join/final absent."
    }

    return Get-VerifiedRulesResultSnapshot -Instance $Instance -Scenario $Scenario
}

function Assert-CompletedHeldSiblingFlowchart {
    param(
        [Parameter(Mandatory)] $Instance,
        [Parameter(Mandatory)][string] $Scenario,
        [Parameter(Mandatory)][string] $SnapshotPath
    )

    if ($Instance.instance.status -notin @('Completed', 'Finished') -or @($Instance.incidents).Count -ne 0) {
        throw "$Scenario did not complete without incidents."
    }
    foreach ($nodeId in @('fork', 'rules', 'capture-rules-result', 'wait', 'join', 'after-rules')) {
        if ((Get-NodeRunCount -Instance $Instance -NodeId $nodeId) -ne 1 -or
            (Get-RequiredNodeExecution -Instance $Instance -NodeId $nodeId).status -ne 'Completed') {
            throw "$Scenario expected one completed activity record for '$nodeId'."
        }
    }
    $nativeSnapshot = Get-NativeInternalAttemptSnapshot -WorkflowExecutionId $Instance.instance.workflowExecutionId `
        -Phase $Scenario -Path $SnapshotPath
    $joinAttempts = Assert-NativeInternalAttemptCount -Snapshot $nativeSnapshot -WorkflowExecutionId $Instance.instance.workflowExecutionId `
        -NodeId 'join' -Scenario $Scenario
    $followingAttempts = Assert-NativeInternalAttemptCount -Snapshot $nativeSnapshot -WorkflowExecutionId $Instance.instance.workflowExecutionId `
        -NodeId 'after-rules' -Scenario $Scenario
    $resultSnapshot = Get-VerifiedRulesResultSnapshot -Instance $Instance -Scenario $Scenario
    return [pscustomobject]@{
        SnapshotTypeName = $resultSnapshot.SnapshotTypeName
        PayloadPropertyNames = $resultSnapshot.PayloadPropertyNames
        ResultSummary = $resultSnapshot.ResultSummary
        JoinInternalAttemptCount = $joinAttempts
        FollowingInternalAttemptCount = $followingAttempts
    }
}

function Get-NativeInternalAttemptSnapshot {
    param(
        [Parameter(Mandatory)][string] $WorkflowExecutionId,
        [Parameter(Mandatory)][string] $Phase,
        [Parameter(Mandatory)][string] $Path
    )

    $arguments = @('--native-db', $nativeDatabasePath, '--rules-db', $rulesDatabasePath,
        '--output', $Path, '--phase', $Phase, '--workflow-id', $WorkflowExecutionId)
    $output = & $python $snapshotTool @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "The read-only '$Phase' native outcome snapshot failed: $($output -join ' ')"
    }

    $snapshot = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($snapshot.schemaVersion -ne 1 -or -not $snapshot.snapshotSha256) {
        throw "The read-only '$Phase' native outcome snapshot did not return its expected schema and digest."
    }
    $fileHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $nativeOutcomeSnapshotEvidence.Add([pscustomobject]@{
        phase = $Phase
        path = $Path
        fileSha256 = $fileHash
        snapshotSha256 = [string]$snapshot.snapshotSha256
        workflowExecutionId = $WorkflowExecutionId
    })
    return $snapshot
}

function Assert-NativeInternalAttemptCount {
    param(
        [Parameter(Mandatory)] $Snapshot,
        [Parameter(Mandatory)][string] $WorkflowExecutionId,
        [Parameter(Mandatory)][string] $NodeId,
        [Parameter(Mandatory)][string] $Scenario
    )

    $workflowSummaries = @($Snapshot.native.workflowSummaries | Where-Object {
        $_.workflowExecutionId -ceq $WorkflowExecutionId
    })
    if ($workflowSummaries.Count -ne 1) {
        throw "$Scenario native snapshot did not contain exactly one workflow row for '$WorkflowExecutionId'."
    }
    $activities = @($workflowSummaries[0].activities | Where-Object { $_.executableNodeId -ceq $NodeId })
    if ($activities.Count -ne 1 -or $activities[0].status -cne 'Completed' -or
        $null -eq $activities[0].attemptCountInternal -or [int]$activities[0].attemptCountInternal -ne 1) {
        $status = if ($activities.Count -eq 1) { $activities[0].status } else { '<missing or duplicated>' }
        $attemptCount = if ($activities.Count -eq 1) { $activities[0].attemptCountInternal } else { '<missing or duplicated>' }
        throw "$Scenario native activity '$NodeId' had status '$status' and internal attempt count '$attemptCount'; expected Completed and exactly one persisted attempt."
    }
    return [int]$activities[0].attemptCountInternal
}

function Get-VerifiedRulesResultSnapshot {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $Scenario)

    $summary = Get-NativeResultSummary -Instance $Instance
    $outputs = Get-FieldValue -Object $Instance -Name 'outputs'
    $output = Get-FieldValue -Object $outputs -Name 'rulesResult'
    $snapshot = Get-FieldValue -Object $output -Name 'value'
    $snapshotTypeName = Get-FieldValue -Object $snapshot -Name 'typeName'
    if ($snapshotTypeName -cne 'Elsa.Any') {
        throw "$Scenario Rules output diagnostic snapshot type was '$snapshotTypeName', expected Elsa.Any."
    }

    # SetOutput targets Elsa.Any. The public workflow-output view exposes this snapshot typeName, not its
    # underlying output type descriptor or CLR runtime type, so verify the full JSON shape and contents instead.
    if ((Get-FieldValue -Object $snapshot -Name 'kind') -cne 'object') {
        throw "$Scenario Rules output was not captured as an object snapshot."
    }
    $payload = Convert-DiagnosticSnapshot $snapshot
    $payloadPropertyNames = @($payload.PSObject.Properties.Name | Sort-Object -CaseSensitive)
    $expectedPropertyNames = @('committedRounds', 'diagnostic', 'finalFactsSha256', 'kind', 'resultFacts', 'resultSchemaVersion', 'terminalId')
    if (($payloadPropertyNames -join "`n") -cne ($expectedPropertyNames -join "`n") -or
        [string]::IsNullOrWhiteSpace([string](Get-FieldValue -Object $payload -Name 'terminalId'))) {
        throw "$Scenario inline Rules payload did not match the complete RuleTerminalResult JSON property shape."
    }
    if ($summary.Kind -ne 'Succeeded' -or $summary.CommittedRounds -ne 2 -or
        $summary.ResultSchemaVersion -ne 1 -or $summary.DiagnosticCode -or
        $summary.FinalFactsSha256 -notmatch '^[0-9a-f]{64}$' -or
        ($summary.ResultFacts -join "`n") -cne "derived`nseed") {
        throw "$Scenario native inline result mismatch: kind=$($summary.Kind), rounds=$($summary.CommittedRounds), facts=$($summary.ResultFacts -join ',')."
    }

    $resultSummary = [pscustomobject]@{
        Kind = $summary.Kind
        CommittedRounds = $summary.CommittedRounds
        ResultSchemaVersion = $summary.ResultSchemaVersion
        ResultFacts = @($summary.ResultFacts)
        DiagnosticCode = $summary.DiagnosticCode
        FinalFactsSha256 = $summary.FinalFactsSha256
        IsRedacted = $summary.IsRedacted
    }
    [pscustomobject]@{
        SnapshotTypeName = $snapshotTypeName
        PayloadPropertyNames = $payloadPropertyNames
        ResultSummary = $resultSummary
    }
}

function Assert-SameObservedRulesResultSnapshot {
    param([Parameter(Mandatory)] $Expected, [Parameter(Mandatory)] $Actual, [Parameter(Mandatory)][string] $Scenario)

    $expectedSnapshot = [pscustomobject]@{
        SnapshotTypeName = $Expected.SnapshotTypeName
        PayloadPropertyNames = $Expected.PayloadPropertyNames
        ResultSummary = $Expected.ResultSummary
    }
    $actualSnapshot = [pscustomobject]@{
        SnapshotTypeName = $Actual.SnapshotTypeName
        PayloadPropertyNames = $Actual.PayloadPropertyNames
        ResultSummary = $Actual.ResultSummary
    }
    $expectedJson = ConvertTo-Json -InputObject $expectedSnapshot -Depth 20 -Compress
    $actualJson = ConvertTo-Json -InputObject $actualSnapshot -Depth 20 -Compress
    if ($actualJson -cne $expectedJson) {
        throw "$Scenario observed Rules result snapshot or content changed across the native workflow lifecycle."
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

    return (Get-VerifiedRulesResultSnapshot -Instance $Instance -Scenario $Scenario).ResultSummary
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
    $eventVersion = Invoke-Step 'resolve Event catalog entry' {
        Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event'
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
    # The root Flowchart's local execution scope is distinct from its executable node ID.
    $flowCapture = New-RulesResultCaptureNode -ProducerScopeId 'scope:root'
    $flowchartStructure = @{
        kind = 'elsa.flowchart.structure'
        schemaVersion = '1.0.0'
        payload = @{
            startNodeId = 'rules'
            activities = @($flowRules, $flowCapture, $flowAfter)
            connections = @(
                @{ id = 'rules-to-capture'; source = @{ nodeId = 'rules'; port = 'Done' }; target = @{ nodeId = 'capture-rules-result' } },
                @{ id = 'capture-to-after'; source = @{ nodeId = 'capture-rules-result'; port = 'Done' }; target = @{ nodeId = 'after-rules' } }
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

    $heldFlowchartId = "RulesFlowchartHeldSibling-$([guid]::NewGuid().ToString('N'))"
    $heldEventName = "rules-held-sibling-$([guid]::NewGuid().ToString('N'))"
    $heldFork = New-ActivityNode -NodeId 'fork' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'Start both branches before waiting at the join.')
    )
    $heldRules = New-RulesActivityNode -NodeId 'rules' -VersionId $runRulesVersion `
        -Program $program -InitialFacts @('seed') -MaxRounds 4
    $heldEvent = New-EventWaitNode -NodeId 'wait' -EventVersionId $eventVersion -EventName $heldEventName
    $heldCapture = New-RulesResultCaptureNode -ProducerScopeId 'scope:root'
    $heldJoin = New-ActivityNode -NodeId 'join' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'Both the Rules and Event branches reached the join.')
    )
    $heldAfter = New-ActivityNode -NodeId 'after-rules' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'The held-sibling Flowchart continued after its join.')
    )
    $heldFlowchartStructure = @{
        kind = 'elsa.flowchart.structure'
        schemaVersion = '1.0.0'
        payload = @{
            startNodeId = 'fork'
            activities = @($heldFork, $heldRules, $heldEvent, $heldCapture, $heldJoin, $heldAfter)
            connections = @(
                @{ id = 'fork-to-rules'; source = @{ nodeId = 'fork'; port = 'Done' }; target = @{ nodeId = 'rules' } },
                @{ id = 'fork-to-event'; source = @{ nodeId = 'fork'; port = 'Done' }; target = @{ nodeId = 'wait' } },
                @{ id = 'rules-to-capture'; source = @{ nodeId = 'rules'; port = 'Done' }; target = @{ nodeId = 'capture-rules-result' } },
                @{ id = 'capture-to-join'; source = @{ nodeId = 'capture-rules-result'; port = 'Done' }; target = @{ nodeId = 'join' } },
                @{ id = 'event-to-join'; source = @{ nodeId = 'wait'; port = 'Done' }; target = @{ nodeId = 'join' } },
                @{ id = 'join-to-after'; source = @{ nodeId = 'join'; port = 'Done' }; target = @{ nodeId = 'after-rules' } }
            )
            # Parallel fork on this node; leave the host's configured default join policy in force.
            nodeMetadata = @{ fork = @{ policyKind = 'parallelFork' } }
            connectionMetadata = @{}
        }
    }
    $heldFlowchartRoot = New-ActivityNode -NodeId 'held-flowchart-root' -VersionId $flowchartVersion `
        -Structure $heldFlowchartStructure
    $heldFlowchartRun = Invoke-Step 'submit, publish and start held-sibling Flowchart Rules workflow' {
        Invoke-PublishedWorkflow -Context $ctx -RootActivity $heldFlowchartRoot -Name $heldFlowchartId -StartOnly
    }
    $heldFlowchartRun.Instance = Wait-HeldSiblingFlowchart -Context $ctx `
        -ExecutionId $heldFlowchartRun.Execution.workflowExecutionId -TimeoutSeconds 180
    $heldPendingObservation = Assert-HeldSiblingFlowchart -Instance $heldFlowchartRun.Instance -Scenario 'Held-sibling Flowchart before restart'

    $heldRestartProcessIdBefore = $script:OwnedElsaServer.Process.Id
    Invoke-Step 'restart the owned Workbench while the Flowchart Event sibling is held' { Restart-OwnedElsaServer }
    $heldRestartProcessIdAfter = $script:OwnedElsaServer.Process.Id
    $ctx = Connect-Elsa -BaseUrl $baseUrl -Username $Username -Password $Password
    $heldAfterRestart = Get-WorkflowInstance -Ctx $ctx -ExecutionId $heldFlowchartRun.Execution.workflowExecutionId
    $heldRestartObservation = Assert-HeldSiblingFlowchart -Instance $heldAfterRestart -Scenario 'Held-sibling Flowchart after restart'
    Assert-SameObservedRulesResultSnapshot -Expected $heldPendingObservation -Actual $heldRestartObservation `
        -Scenario 'Held-sibling Flowchart after restart'

    $heldResume = Invoke-Step 'resume the held Flowchart Event sibling' {
        Invoke-ResumeStimulus -Ctx $ctx -EventName $heldEventName
    }
    if ($heldResume.resumedCount -ne 1) {
        throw "The held Flowchart Event did not resume exactly its waiting branch (resumedCount=$($heldResume.resumedCount))."
    }
    $heldCompleted = Wait-WorkflowInstance -Ctx $ctx `
        -ExecutionId $heldFlowchartRun.Execution.workflowExecutionId -TimeoutSeconds 180
    $heldFinalObservation = Assert-CompletedHeldSiblingFlowchart -Instance $heldCompleted `
        -Scenario 'Held-sibling Flowchart after Event resume' `
        -SnapshotPath (Join-Path $snapshotDirectory 'held-flowchart-after-event-resume.snapshot.json')
    Assert-SameObservedRulesResultSnapshot -Expected $heldPendingObservation -Actual $heldFinalObservation `
        -Scenario 'Held-sibling Flowchart after Event resume'

    $heldDuplicateResume = Invoke-Step 'reject duplicate held Flowchart Event stimulus' {
        Invoke-ResumeStimulus -Ctx $ctx -EventName $heldEventName
    }
    if ($heldDuplicateResume.resumedCount -ne 0) {
        throw "A duplicate held Flowchart Event stimulus resumed $($heldDuplicateResume.resumedCount) branch(es), expected zero."
    }
    $heldAfterDuplicate = Get-WorkflowInstance -Ctx $ctx -ExecutionId $heldFlowchartRun.Execution.workflowExecutionId
    $heldDuplicateObservation = Assert-CompletedHeldSiblingFlowchart -Instance $heldAfterDuplicate `
        -Scenario 'Held-sibling Flowchart after duplicate Event stimulus' `
        -SnapshotPath (Join-Path $snapshotDirectory 'held-flowchart-after-duplicate-stimulus.snapshot.json')
    Assert-SameObservedRulesResultSnapshot -Expected $heldPendingObservation -Actual $heldDuplicateObservation `
        -Scenario 'Held-sibling Flowchart after duplicate Event stimulus'

    $runEvidence.Add([pscustomobject]@{
        Scenario = 'FlowchartHeldSibling'
        WorkflowExecutionId = $heldFlowchartRun.Execution.workflowExecutionId
        ArtifactId = $heldFlowchartRun.Publication.artifactId
        EventName = $heldEventName
        BeforeResumeStatus = $heldAfterRestart.instance.status
        BeforeResumeRulesStatus = (Get-RequiredNodeExecution -Instance $heldAfterRestart -NodeId 'rules').status
        BeforeResumeEventStatus = (Get-RequiredNodeExecution -Instance $heldAfterRestart -NodeId 'wait').status
        BeforeResumeJoinCount = Get-NodeRunCount -Instance $heldAfterRestart -NodeId 'join'
        BeforeResumeFollowingCount = Get-NodeRunCount -Instance $heldAfterRestart -NodeId 'after-rules'
        HeldRestartProcessIds = @($heldRestartProcessIdBefore, $heldRestartProcessIdAfter)
        ResumeCount = $heldResume.resumedCount
        DuplicateResumeCount = $heldDuplicateResume.resumedCount
        AfterResumeStatus = $heldAfterDuplicate.instance.status
        JoinCountAfterDuplicate = Get-NodeRunCount -Instance $heldAfterDuplicate -NodeId 'join'
        FollowingCountAfterDuplicate = Get-NodeRunCount -Instance $heldAfterDuplicate -NodeId 'after-rules'
        NativeResultSnapshotTypeName = $heldDuplicateObservation.SnapshotTypeName
        NativeResultPayloadPropertyNames = $heldDuplicateObservation.PayloadPropertyNames
        NativeResultSummary = $heldDuplicateObservation.ResultSummary
        JoinInternalAttemptCount = $heldDuplicateObservation.JoinInternalAttemptCount
        FollowingInternalAttemptCount = $heldDuplicateObservation.FollowingInternalAttemptCount
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
    $persistedHeldFlowchart = Get-WorkflowInstance -Ctx $ctx -ExecutionId $heldFlowchartRun.Execution.workflowExecutionId
    $persistedHeldObservation = Assert-CompletedHeldSiblingFlowchart -Instance $persistedHeldFlowchart `
        -Scenario 'Held-sibling Flowchart after final restart' `
        -SnapshotPath (Join-Path $snapshotDirectory 'held-flowchart-after-final-restart.snapshot.json')
    Assert-SameObservedRulesResultSnapshot -Expected $heldPendingObservation -Actual $persistedHeldObservation `
        -Scenario 'Held-sibling Flowchart after final restart'
    $restartEvidence = [pscustomobject]@{
        PreviousProcessId = $processIdBeforeRestart
        ReconnectedProcessId = $processIdAfterRestart
        WorkbenchAssemblyPath = $assemblyPathAfterRestart
        SequenceStatus = $persistedSequence.instance.status
        SequenceNativeResult = $persistedSequenceResult
        FlowchartStatus = $persistedFlowchart.instance.status
        FlowchartNativeResult = $persistedFlowchartResult
        HeldFlowchartStatus = $persistedHeldFlowchart.instance.status
        HeldFlowchartNativeResultSnapshotTypeName = $persistedHeldObservation.SnapshotTypeName
        HeldFlowchartNativeResultPayloadPropertyNames = $persistedHeldObservation.PayloadPropertyNames
        HeldFlowchartNativeResultSummary = $persistedHeldObservation.ResultSummary
        HeldFlowchartJoinInternalAttemptCount = $persistedHeldObservation.JoinInternalAttemptCount
        HeldFlowchartFollowingInternalAttemptCount = $persistedHeldObservation.FollowingInternalAttemptCount
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
        NativeOutcomeSnapshots = $nativeOutcomeSnapshotEvidence.ToArray()
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
    } else {
        Write-Host "[evidence] native outcome snapshots retained at $snapshotDirectory"
    }
    $success = $true
    Write-Host "SUCCESS - durable Rules REST journey passed $($runEvidence.Count) scenarios on the owned Workbench process." -ForegroundColor Green
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot `
        -KeepContentRoot:((-not $success) -or [string]::IsNullOrWhiteSpace($EvidencePath))
}
