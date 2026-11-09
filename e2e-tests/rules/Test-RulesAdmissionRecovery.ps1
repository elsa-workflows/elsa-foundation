<#
.SYNOPSIS
    Prove packaged native Rules admission recovery, cancellation, and invalid-input settlement.
.DESCRIPTION
    Uses the supplied package-backed Workbench deployment and private SQLite databases. It publishes while
    admission is enabled, starts two executions only after restarting with admission disabled, cancels one through
    the public alteration-plan API, then restores admission and proves the other execution resumes exactly once.
    SQLite evidence is captured by native_outcome_snapshot.py through read-only connections.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $AssemblyPath,
    [Parameter(Mandatory)][string] $DeploymentReceiptPath,
    [Parameter(Mandatory)][string] $EvidencePath,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [int] $StartupTimeoutSeconds = 300
)

. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"
. "$PSScriptRoot/_RulesCommon.ps1"
. "$PSScriptRoot/../runtime-alterations/_AlterationCommon.ps1"

$ErrorActionPreference = 'Stop'
$assemblyItem = Get-Item -LiteralPath $AssemblyPath -ErrorAction Stop
if ($assemblyItem.PSIsContainer -or $assemblyItem.Extension -ne '.dll') {
    throw '-AssemblyPath must identify the qualified Elsa.Workbench.dll file.'
}
$assemblyPath = [IO.Path]::GetFullPath($assemblyItem.FullName)
$receiptItem = Get-Item -LiteralPath $DeploymentReceiptPath -ErrorAction Stop
if ($receiptItem.PSIsContainer) { throw '-DeploymentReceiptPath must identify the package deployment receipt.' }
$receiptPath = [IO.Path]::GetFullPath($receiptItem.FullName)
$hostDepsPath = [IO.Path]::ChangeExtension($assemblyPath, '.deps.json')
$hostRuntimeConfigPath = [IO.Path]::ChangeExtension($assemblyPath, '.runtimeconfig.json')
foreach ($requiredFile in @($hostDepsPath, $hostRuntimeConfigPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "A required file beside the supplied Workbench assembly is missing: $requiredFile"
    }
}
$hostRoot = [IO.Path]::GetDirectoryName($assemblyPath)
$evidencePath = [IO.Path]::GetFullPath($EvidencePath)
$evidenceDirectory = [IO.Path]::GetDirectoryName($evidencePath)
New-Item -Path $evidenceDirectory -ItemType Directory -Force | Out-Null
$snapshotTool = (Resolve-Path "$PSScriptRoot/../../tools/rules/native_outcome_snapshot.py").Path
$python = if ($env:OS -eq 'Windows_NT') { 'python' } else { 'python3' }
$baseUrl = Resolve-ElsaServerBaseUrl
$contentRoot = New-ElsaContentRoot -Prefix 'elsa-rules-admission-recovery'
$nativeDatabasePath = Join-Path $contentRoot 'elsa.db'
$rulesDatabasePath = Join-Path $contentRoot 'rules-runtime.db'
$serverEnvironment = @{ ConnectionStrings__Elsa = "Data Source=$nativeDatabasePath;Pooling=False" }
foreach ($name in [Environment]::GetEnvironmentVariables('Process').Keys) {
    if ($name -match '^(Elsa__Persistence__|CShells__|RulesRuntime__|RulesRuntimeEntityFrameworkCore__|RulesWorkflows)') {
        $serverEnvironment[[string]$name] = $null
    }
}
$processIds = [System.Collections.Generic.List[int]]::new()
$snapshotEvidence = [System.Collections.Generic.List[object]]::new()
$stageEvidence = [System.Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::UtcNow
$success = $false
$initialPackageHashes = [ordered]@{}
foreach ($file in @($assemblyPath, $hostDepsPath, $hostRuntimeConfigPath, $receiptPath)) {
    $initialPackageHashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
}
$evidence = [ordered]@{
    passed = $false
    schemaVersion = 1
    startedAt = $startedAt
    scope = 'Actual supplied package-backed Workbench; fresh owned native and Rules SQLite databases; admission recovery, cancellation, and invalid round-limit journeys.'
    workbenchAssemblySha256 = $initialPackageHashes[$assemblyPath]
    workbenchDependenciesSha256 = $initialPackageHashes[$hostDepsPath]
    workbenchRuntimeConfigSha256 = $initialPackageHashes[$hostRuntimeConfigPath]
    deploymentReceiptSha256 = $initialPackageHashes[$receiptPath]
    deploymentReceiptPath = $receiptPath
    databaseFiles = @{ native = $nativeDatabasePath; rules = $rulesDatabasePath }
    configurationTransitions = @()
    processIds = @()
    stages = @()
    snapshots = @()
}

function Set-RulesAdmissionMode {
    param([Parameter(Mandatory)][string] $Root, [Parameter(Mandatory)][bool] $AcceptNewInvocations)
    $settingsPath = Join-Path $Root 'shells.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $feature = $settings.CShells.Shells.default.Features.RulesRuntime
    if (-not $feature) { throw 'RulesRuntime feature settings were not installed into the owned Workbench shell.' }
    $feature | Add-Member -MemberType NoteProperty -Name AcceptNewInvocations -Value $AcceptNewInvocations -Force
    $settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $settingsPath -Encoding utf8
    $check = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).CShells.Shells.default.Features.RulesRuntime.AcceptNewInvocations
    if ([bool]$check -ne $AcceptNewInvocations) { throw 'The owned shell did not retain the requested Rules admission setting.' }
    $evidence.configurationTransitions += [pscustomobject]@{
        acceptNewInvocations = $AcceptNewInvocations
        shellsJsonSha256 = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Start-RulesAdmissionHost {
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot -AssemblyPath $assemblyPath `
        -Environment $serverEnvironment -TimeoutSec $StartupTimeoutSeconds
    $processIds.Add($script:OwnedElsaServer.Process.Id)
    Connect-Elsa -BaseUrl $baseUrl -Username $Username -Password $Password
}

function Get-RulesAdmissionSnapshot {
    param(
        [Parameter(Mandatory)][string] $Phase,
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string[]] $WorkflowIds,
        [switch] $Record
    )
    $arguments = @('--native-db', $nativeDatabasePath, '--rules-db', $rulesDatabasePath,
        '--output', $Path, '--phase', $Phase)
    foreach ($workflowId in $WorkflowIds) { $arguments += @('--workflow-id', $workflowId) }
    $output = & $python $snapshotTool @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "The read-only '$Phase' SQLite snapshot failed: $($output -join ' ')" }
    $snapshot = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($snapshot.schemaVersion -ne 1 -or -not $snapshot.snapshotSha256) {
        throw "The read-only '$Phase' SQLite snapshot did not return its expected schema and digest."
    }
    if ($Record) {
        $snapshotEvidence.Add([pscustomobject]@{
            phase = $Phase
            path = $Path
            fileSha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
            snapshotSha256 = [string]$snapshot.snapshotSha256
        })
    }
    return $snapshot
}

function Wait-RulesAdmissionSnapshot {
    param(
        [Parameter(Mandatory)][string] $Phase,
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string[]] $WorkflowIds,
        [Parameter(Mandatory)][scriptblock] $Predicate,
        [int] $TimeoutSeconds = 45
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $snapshot = Get-RulesAdmissionSnapshot -Phase $Phase -Path $Path -WorkflowIds $WorkflowIds
        if (& $Predicate $snapshot) { return $snapshot }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "The read-only '$Phase' snapshot did not reach its required durable state within $TimeoutSeconds seconds."
}

function Wait-RulesWorkflowHeld {
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)][string] $ExecutionId, [int] $TimeoutSeconds = 60)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $stableReads = 0
    $instance = $null
    do {
        $instance = Get-WorkflowInstance -Ctx $Context -ExecutionId $ExecutionId
        $rules = @($instance.activities | Where-Object { $_.executableNodeId -ceq 'rules' })
        $following = @($instance.activities | Where-Object { $_.executableNodeId -ceq 'after-rules' })
        if ($instance.instance.status -eq 'Running' -and $rules.Count -eq 1 -and
            $rules[0].status -eq 'Suspended' -and $following.Count -eq 0 -and
            @($instance.incidents).Count -eq 0) {
            $stableReads++
            if ($stableReads -ge 2) { return $instance }
        } else {
            $stableReads = 0
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($instance) { Show-WorkflowInstance -Instance $instance }
    throw "Workflow '$ExecutionId' did not remain Running with one suspended Rules activity and no following activity."
}

function Wait-RulesWorkflowStatus {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)][string] $ExecutionId,
        [Parameter(Mandatory)][string[]] $ExpectedStatus,
        [int] $TimeoutSeconds = 120
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $instance = Get-WorkflowInstance -Ctx $Context -ExecutionId $ExecutionId
        if ($instance.instance.status -in $ExpectedStatus) { return $instance }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($instance) { Show-WorkflowInstance -Instance $instance }
    throw "Workflow '$ExecutionId' did not reach expected status '$($ExpectedStatus -join ', ')'; last status '$($instance.instance.status)'."
}

function Get-SingleRulesStartIntent {
    param([Parameter(Mandatory)] $Snapshot, [Parameter(Mandatory)][string] $WorkflowId)
    $matches = @($Snapshot.native.outboxIntents | Where-Object {
        $_.workflowExecutionId -ceq $WorkflowId -and $_.intentKind -ceq 'Elsa.Rules.Start.v1'
    })
    if ($matches.Count -ne 1) { throw "Expected one native Rules Start intent for '$WorkflowId'; found $($matches.Count)." }
    return $matches[0]
}

function Get-SingleRulesRunSummary {
    param([Parameter(Mandatory)] $Snapshot, [Parameter(Mandatory)][string] $RunId)
    $matches = @($Snapshot.rules.runSummaries | Where-Object { $_.runId -ceq $RunId })
    if ($matches.Count -ne 1) { throw "Expected one Rules child run for '$RunId'; found $($matches.Count)." }
    return $matches[0]
}

function ConvertTo-NormalizedRulesImplementationPin {
    param([Parameter(Mandatory)] $Pin)
    $packages = @(
        foreach ($package in @(Get-FieldValue -Object $Pin -Name 'packages')) {
            $packageId = Get-FieldValue -Object $package -Name 'packageId'
            if ($null -eq $packageId) { $packageId = Get-FieldValue -Object $package -Name 'id' }
            $version = Get-FieldValue -Object $package -Name 'nuGetVersion'
            if ($null -eq $version) { $version = Get-FieldValue -Object $package -Name 'version' }
            $contentHash = Get-FieldValue -Object $package -Name 'nuGetContentHash'
            if ($null -eq $contentHash) { $contentHash = Get-FieldValue -Object $package -Name 'contentHash' }
            $sourceAssets = Get-FieldValue -Object $package -Name 'selectedAssets'
            if ($null -eq $sourceAssets) { $sourceAssets = Get-FieldValue -Object $package -Name 'assets' }
            $assets = @(
                foreach ($asset in @($sourceAssets)) {
                    $path = Get-FieldValue -Object $asset -Name 'packageRelativeAssetPath'
                    if ($null -eq $path) { $path = Get-FieldValue -Object $asset -Name 'path' }
                    $assembly = Get-FieldValue -Object $asset -Name 'assemblyFullName'
                    if ($null -eq $assembly) { $assembly = Get-FieldValue -Object $asset -Name 'assembly' }
                    $assetHash = Get-FieldValue -Object $asset -Name 'assetSha256'
                    if ($null -eq $assetHash) { $assetHash = Get-FieldValue -Object $asset -Name 'sha256' }
                    if ([string]::IsNullOrEmpty([string]$path) -or [string]::IsNullOrEmpty([string]$assembly) -or
                        [string]::IsNullOrEmpty([string]$assetHash)) {
                        throw 'A persisted Rules implementation pin contains an incomplete selected asset.'
                    }
                    [pscustomobject][ordered]@{ path = [string]$path; assembly = [string]$assembly; sha256 = [string]$assetHash }
                }
            ) | Sort-Object path, assembly, sha256
            if ([string]::IsNullOrEmpty([string]$packageId) -or [string]::IsNullOrEmpty([string]$version) -or
                [string]::IsNullOrEmpty([string]$contentHash) -or $assets.Count -eq 0) {
                throw 'A persisted Rules implementation pin contains an incomplete package entry.'
            }
            [pscustomobject][ordered]@{
                id = [string]$packageId
                version = [string]$version
                contentHash = [string]$contentHash
                assets = @($assets)
            }
        }
    ) | Sort-Object id, version, contentHash
    $languageId = Get-FieldValue -Object $Pin -Name 'languageId'
    $languageVersion = Get-FieldValue -Object $Pin -Name 'languageVersion'
    $pinSchemaVersion = Get-FieldValue -Object $Pin -Name 'pinSchemaVersion'
    $receiptSchemaVersion = Get-FieldValue -Object $Pin -Name 'receiptSchemaVersion'
    $stateSchemaVersion = Get-FieldValue -Object $Pin -Name 'stateSchemaVersion'
    if ([string]::IsNullOrEmpty([string]$languageId) -or $null -eq $languageVersion -or
        $null -eq $pinSchemaVersion -or $null -eq $receiptSchemaVersion -or $null -eq $stateSchemaVersion -or
        $packages.Count -eq 0) {
        throw 'A persisted Rules implementation pin is incomplete.'
    }
    $normalized = [ordered]@{
        pinSchemaVersion = $pinSchemaVersion
        languageId = [string]$languageId
        languageVersion = $languageVersion
        receiptSchemaVersion = $receiptSchemaVersion
        stateSchemaVersion = $stateSchemaVersion
        packages = @($packages)
    }
    return ConvertTo-Json -InputObject $normalized -Depth 20 -Compress
}

function Assert-NativeRulesActivity {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $ExpectedStatus, [string] $FollowingStatus)
    $rules = Get-RequiredNodeExecution -Instance $Instance -NodeId 'rules'
    if ($rules.status -cne $ExpectedStatus) { throw "RunRules activity ended '$($rules.status)', expected '$ExpectedStatus'." }
    $followingCount = Get-NodeRunCount -Instance $Instance -NodeId 'after-rules'
    if ([string]::IsNullOrEmpty($FollowingStatus)) {
        if ($followingCount -ne 0) { throw "The following activity ran $followingCount time(s) before Rules could continue." }
    } elseif ($followingCount -ne 1 -or (Get-RequiredNodeExecution -Instance $Instance -NodeId 'after-rules').status -cne $FollowingStatus) {
        throw "The following activity was not observed exactly once in status '$FollowingStatus'."
    }
    return $rules
}

function Write-RulesAdmissionEvidence {
    param([Parameter(Mandatory)][bool] $Passed, [string] $ErrorMessage)
    $evidence.passed = $Passed
    if ($ErrorMessage) { $evidence['error'] = $ErrorMessage }
    $evidence['completedAt'] = [DateTimeOffset]::UtcNow
    $evidence.processIds = $processIds.ToArray()
    $evidence.stages = $stageEvidence.ToArray()
    $evidence.snapshots = $snapshotEvidence.ToArray()
    $afterHashes = [ordered]@{}
    foreach ($file in $initialPackageHashes.Keys) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Supplied package file disappeared during the acceptance journey: $file" }
        $afterHashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($afterHashes[$file] -cne $initialPackageHashes[$file]) { throw "Supplied package deployment material changed during the acceptance journey: $file" }
    }
    $evidence['packageFileHashesAfter'] = $afterHashes
    $evidence | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $evidencePath -Encoding utf8
}

try {
    Set-RulesFeatureConfiguration -ContentRoot $contentRoot -HostRoot $hostRoot `
        -HostDepsPath $hostDepsPath -DeploymentReceiptPath $receiptPath -DatabasePath $rulesDatabasePath
    Set-RulesAdmissionMode -Root $contentRoot -AcceptNewInvocations $true
    $ctx = Start-RulesAdmissionHost
    $runRulesVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Rules.Workflows.Runtime.Activities.RunRules'
    $writeLineVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    $sequenceVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $program = '{"formatVersion":1,"languageId":"elsa.rules.finite-positive","languageVersion":1,"vocabulary":["derived","seed"],"rules":[{"id":"derive","premises":["seed"],"conclusion":"derived"}]}'

    $workflowName = "RulesAdmissionRecovery-$([guid]::NewGuid().ToString('N'))"
    $rulesNode = New-RulesActivityNode -NodeId 'rules' -VersionId $runRulesVersion `
        -Program $program -InitialFacts @('seed') -MaxRounds 4
    $followingNode = New-ActivityNode -NodeId 'after-rules' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'The retained Rules result resumed the native Sequence.')
    )
    $captureNode = New-RulesResultCaptureNode -ProducerScopeId 'sequence-root'
    $root = New-ActivityNode -NodeId 'sequence-root' -VersionId $sequenceVersion -Structure (
        New-SequenceStructure -Activities @($rulesNode, $followingNode, $captureNode)
    )
    $definition = Submit-Workflow -Ctx $ctx -Name $workflowName `
        -Description 'Packaged Rules admission recovery acceptance.' -RootActivity $root
    $publication = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    if (-not $publication.artifactId -or -not $publication.sourceReferenceId) {
        throw 'The packaged workflow publication did not return an exact artifact and source-reference identity.'
    }
    $stageEvidence.Add([pscustomobject]@{
        stage = 'published-with-admission-enabled-without-starting'
        acceptNewInvocations = $true
        workflowDefinitionId = $definition.definition.id
        workflowDefinitionVersionId = $definition.version.id
        artifactId = $publication.artifactId
        sourceReferenceId = $publication.sourceReferenceId
        executionsStarted = 0
    })
    Stop-OwnedElsaServer

    Set-RulesAdmissionMode -Root $contentRoot -AcceptNewInvocations $false
    $ctx = Start-RulesAdmissionHost
    $executionA = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    $executionB = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    foreach ($execution in @($executionA, $executionB)) {
        if ($execution.commandDispatchStatus -cne 'Accepted') {
            throw "Native workflow start returned '$($execution.commandDispatchStatus)' instead of Accepted."
        }
    }
    $executionAId = [string]$executionA.workflowExecutionId
    $executionBId = [string]$executionB.workflowExecutionId
    if (-not $executionAId -or -not $executionBId -or $executionAId -ceq $executionBId) {
        throw 'The two starts did not produce distinct native workflow execution identities.'
    }
    $heldA = Wait-RulesWorkflowHeld -Context $ctx -ExecutionId $executionAId
    $heldB = Wait-RulesWorkflowHeld -Context $ctx -ExecutionId $executionBId
    $rulesActivityA = Get-RequiredNodeExecution -Instance $heldA -NodeId 'rules'
    $rulesActivityB = Get-RequiredNodeExecution -Instance $heldB -NodeId 'rules'
    $heldSnapshotPath = Join-Path $evidenceDirectory 'native-admission-held-snapshot.json'
    $heldSettlement = Wait-RulesAdmissionSnapshot -Phase 'admission-disabled-held' -Path $heldSnapshotPath `
        -WorkflowIds @($executionAId, $executionBId) -Predicate {
            param($snapshot)
            $intents = @($snapshot.native.outboxIntents | Where-Object {
                $_.workflowExecutionId -in @($executionAId, $executionBId) -and $_.intentKind -ceq 'Elsa.Rules.Start.v1'
            })
            if ($intents.Count -ne 2) { return $false }
            foreach ($intent in $intents) {
                if ($intent.status -notin @('Pending', 'FailedRetryable') -or $intent.deliveryAttemptCount -lt 1 -or
                    @($snapshot.rules.runSummaries | Where-Object { $_.runId -ceq $intent.request.runId }).Count -ne 0) {
                    return $false
                }
            }
            return $true
        }
    Stop-OwnedElsaServer

    $heldSnapshot = Get-RulesAdmissionSnapshot -Phase 'admission-disabled-held' -Path $heldSnapshotPath `
        -WorkflowIds @($executionAId, $executionBId) -Record
    $startA = Get-SingleRulesStartIntent -Snapshot $heldSnapshot -WorkflowId $executionAId
    $startB = Get-SingleRulesStartIntent -Snapshot $heldSnapshot -WorkflowId $executionBId
    foreach ($intent in @($startA, $startB)) {
        if ($intent.status -notin @('Pending', 'FailedRetryable') -or $intent.request.maxRounds -ne 4) {
            throw "Workflow '$($intent.workflowExecutionId)' did not retain a retryable Rules Start intent after disabled admission."
        }
        if ($intent.request.workflowExecutionId -cne $intent.workflowExecutionId -or
            $intent.request.activityExecutionId -cne $intent.activityExecutionId) {
            throw 'The native outbox identity and frozen Rules request identity did not agree.'
        }
        if ($intent.deliveryAttemptCount -lt 1) {
            throw 'The disabled deployment did not record a physical Start-intent delivery attempt.'
        }
    }
    $runIdA = [string]$startA.request.runId
    $runIdB = [string]$startB.request.runId
    if (-not $runIdA -or -not $runIdB -or $runIdA -ceq $runIdB) { throw 'The two native Rules run identities were not distinct.' }
    if ($null -eq $startA.request.program.implementation -or $null -eq $startB.request.program.implementation) {
        throw 'The frozen published program did not retain its semantic implementation pin.'
    }
    $pinAJson = ConvertTo-Json -InputObject $startA.request.program.implementation -Depth 40 -Compress
    $pinBJson = ConvertTo-Json -InputObject $startB.request.program.implementation -Depth 40 -Compress
    if ($pinAJson -cne $pinBJson) { throw 'Two instances of the same published reference did not retain the same implementation pin.' }
    if (@($heldSnapshot.rules.runSummaries | Where-Object { $_.runId -in @($runIdA, $runIdB) }).Count -ne 0) {
        throw 'A Rules child row was created while both invocations were held before admission.'
    }
    foreach ($held in @(@{ instance = $heldA; activity = $rulesActivityA; id = $executionAId },
                         @{ instance = $heldB; activity = $rulesActivityB; id = $executionBId })) {
        if ($held.activity.status -cne 'Suspended' -or $held.instance.instance.status -cne 'Running' -or
            (Get-NodeRunCount -Instance $held.instance -NodeId 'after-rules') -ne 0 -or
            (Get-NodeRunCount -Instance $held.instance -NodeId 'capture-rules-result') -ne 0) {
            throw "Workflow '$($held.id)' did not retain a suspended Rules activity with no continuation or refusal."
        }
    }
    $stageEvidence.Add([pscustomobject]@{
        stage = 'accepted-native-starts-held-before-rules-admission'
        acceptNewInvocations = $false
        artifactId = $publication.artifactId
        sourceReferenceId = $publication.sourceReferenceId
        workflowExecutionIds = @($executionAId, $executionBId)
        rulesActivityExecutionIds = @($rulesActivityA.activityExecutionId, $rulesActivityB.activityExecutionId)
        runIds = @($runIdA, $runIdB)
        packageImplementationPin = $startA.request.program.implementation
        secondImplementationPin = $startB.request.program.implementation
        startOutboxItems = @($startA, $startB)
        publicStatuses = @($heldA.instance.status, $heldB.instance.status)
        rulesStatuses = @($rulesActivityA.status, $rulesActivityB.status)
        followingCounts = @(0, 0)
        childRowsForEitherRun = 0
    })

    $ctx = Start-RulesAdmissionHost
    $alteration = Submit-AlterationPlan -Ctx $ctx `
        -Target @{ executionIds = @($executionAId) } `
        -Alterations @(@{ kind = 'CancelWorkflow'; schemaVersion = 1; payload = @{} })
    $plan = Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $alteration.PlanId -TimeoutSeconds 90
    if ($plan.Json.status -cne 'Completed') { throw "The CancelWorkflow alteration ended '$($plan.Json.status)'." }
    $cancelledA = Wait-RulesWorkflowStatus -Context $ctx -ExecutionId $executionAId -ExpectedStatus @('Cancelled') -TimeoutSeconds 90
    $cancelledRules = Assert-NativeRulesActivity -Instance $cancelledA -ExpectedStatus 'Cancelled'
    if ((Get-NodeRunCount -Instance $cancelledA -NodeId 'capture-rules-result') -ne 0 -or
        @($cancelledA.incidents).Count -ne 0) {
        throw 'The cancelled workflow exposed a Rules result continuation or an incident.'
    }
    $cancelledSnapshotPath = Join-Path $evidenceDirectory 'native-admission-cancelled-snapshot.json'
    $cancelledSettlement = Wait-RulesAdmissionSnapshot -Phase 'cancelled-before-admission' `
        -Path $cancelledSnapshotPath -WorkflowIds @($executionAId, $executionBId) -Predicate {
            param($snapshot)
            $cancelledRuns = @($snapshot.rules.runSummaries | Where-Object { $_.runId -ceq $runIdA })
            $pendingIntents = @($snapshot.native.outboxIntents | Where-Object {
                $_.workflowExecutionId -ceq $executionBId -and $_.intentKind -ceq 'Elsa.Rules.Start.v1'
            })
            return ($cancelledRuns.Count -eq 1 -and $cancelledRuns[0].status -ceq 'Cancelled' -and
                $cancelledRuns[0].admitted -eq $false -and $pendingIntents.Count -eq 1 -and
                $pendingIntents[0].status -in @('Pending', 'FailedRetryable') -and
                $pendingIntents[0].deliveryAttemptCount -ge 1 -and
                @($snapshot.rules.runSummaries | Where-Object { $_.runId -ceq $runIdB }).Count -eq 0)
        }
    Stop-OwnedElsaServer
    $cancelledSnapshot = Get-RulesAdmissionSnapshot -Phase 'cancelled-before-admission' `
        -Path $cancelledSnapshotPath -WorkflowIds @($executionAId, $executionBId) -Record
    $cancelledRun = Get-SingleRulesRunSummary -Snapshot $cancelledSnapshot -RunId $runIdA
    if ($cancelledRun.status -cne 'Cancelled' -or $cancelledRun.admitted -ne $false) {
        throw 'Parent cancellation did not retain an unadmitted Rules cancellation tombstone.'
    }
    if (@($cancelledSnapshot.rules.runSummaries | Where-Object { $_.runId -ceq $runIdB }).Count -ne 0) {
        throw 'The other accepted invocation was admitted while the host still denied new invocations.'
    }
    $pendingB = Get-SingleRulesStartIntent -Snapshot $cancelledSnapshot -WorkflowId $executionBId
    if ($pendingB.status -notin @('Pending', 'FailedRetryable') -or $pendingB.deliveryAttemptCount -lt 1) {
        throw "The uncancelled Start responsibility was not retained for retry: '$($pendingB.status)'."
    }
    $stageEvidence.Add([pscustomobject]@{
        stage = 'public-cancellation-before-admission'
        acceptNewInvocations = $false
        workflowExecutionId = $executionAId
        activityExecutionId = $cancelledRules.activityExecutionId
        runId = $runIdA
        alterationPlanId = $alteration.PlanId
        alterationPlanStatus = $plan.Json.status
        workflowStatus = $cancelledA.instance.status
        rulesActivityStatus = $cancelledRules.status
        followingCount = 0
        tombstone = $cancelledRun
        uncancelledWorkflowExecutionId = $executionBId
        uncancelledStartOutboxStatus = $pendingB.status
        uncancelledChildRowCount = 0
    })

    Set-RulesAdmissionMode -Root $contentRoot -AcceptNewInvocations $true
    $ctx = Start-RulesAdmissionHost
    $recoveredB = Wait-RulesWorkflowStatus -Context $ctx -ExecutionId $executionBId -ExpectedStatus @('Completed', 'Finished') -TimeoutSeconds 180
    $successfulRules = Assert-NativeRulesActivity -Instance $recoveredB -ExpectedStatus 'Completed' -FollowingStatus 'Completed'
    if ((Get-NodeRunCount -Instance $recoveredB -NodeId 'capture-rules-result') -ne 1 -or
        @($recoveredB.incidents).Count -ne 0) {
        throw 'The recovered workflow did not complete result capture exactly once without incidents.'
    }
    $result = Get-NativeResultSummary -Instance $recoveredB
    if ($result.Kind -cne 'Succeeded' -or $result.CommittedRounds -ne 2 -or
        ($result.ResultFacts -join "`n") -cne "derived`nseed" -or $result.DiagnosticCode) {
        throw 'The recovered native invocation did not expose the expected successful result and committed facts.'
    }
    $cancelledAfterRecovery = Get-WorkflowInstance -Ctx $ctx -ExecutionId $executionAId
    if ($cancelledAfterRecovery.instance.status -cne 'Cancelled' -or
        (Get-RequiredNodeExecution -Instance $cancelledAfterRecovery -NodeId 'rules').status -cne 'Cancelled' -or
        (Get-NodeRunCount -Instance $cancelledAfterRecovery -NodeId 'after-rules') -ne 0) {
        throw 'The previously cancelled workflow changed after admission was restored.'
    }
    $recoveredSnapshotPath = Join-Path $evidenceDirectory 'native-admission-recovered-snapshot.json'
    $recoveredSettlement = Wait-RulesAdmissionSnapshot -Phase 'compatible-admission-restored' `
        -Path $recoveredSnapshotPath -WorkflowIds @($executionAId, $executionBId) -Predicate {
            param($snapshot)
            $successfulRuns = @($snapshot.rules.runSummaries | Where-Object { $_.runId -ceq $runIdB })
            $cancelledRuns = @($snapshot.rules.runSummaries | Where-Object { $_.runId -ceq $runIdA })
            return ($successfulRuns.Count -eq 1 -and $successfulRuns[0].status -ceq 'Succeeded' -and
                $successfulRuns[0].admitted -eq $true -and $successfulRuns[0].deliveryAcknowledged -eq $true -and
                $cancelledRuns.Count -eq 1 -and $cancelledRuns[0].status -ceq 'Cancelled')
        }
    Stop-OwnedElsaServer
    $recoveredSnapshot = Get-RulesAdmissionSnapshot -Phase 'compatible-admission-restored' `
        -Path $recoveredSnapshotPath -WorkflowIds @($executionAId, $executionBId) -Record
    $successfulRun = Get-SingleRulesRunSummary -Snapshot $recoveredSnapshot -RunId $runIdB
    $cancelledRunAfterRecovery = Get-SingleRulesRunSummary -Snapshot $recoveredSnapshot -RunId $runIdA
    if ($successfulRun.status -cne 'Succeeded' -or $successfulRun.admitted -ne $true -or
        $successfulRun.deliveryAcknowledged -ne $true -or $cancelledRunAfterRecovery.status -cne 'Cancelled') {
        throw 'Rules persisted terminal state did not preserve one success and the cancellation tombstone.'
    }
    foreach ($retainedPin in @(
        @{ run = $cancelledRunAfterRecovery; request = $startA.request; expectedRunId = $runIdA },
        @{ run = $successfulRun; request = $startB.request; expectedRunId = $runIdB }
    )) {
        if ($retainedPin.run.runId -cne $retainedPin.expectedRunId -or
            (ConvertTo-NormalizedRulesImplementationPin -Pin $retainedPin.run.implementation) -cne
            (ConvertTo-NormalizedRulesImplementationPin -Pin $retainedPin.request.program.implementation)) {
            throw "Rules run '$($retainedPin.expectedRunId)' did not retain its original Start-request implementation pin."
        }
    }
    if ([string]::IsNullOrEmpty([string]$successfulRun.pinHash) -or
        $successfulRun.pinHash -cne $cancelledRunAfterRecovery.pinHash) {
        throw 'The retained Rules runs did not preserve one immutable implementation-pin hash.'
    }
    $successfulNativeActivity = @($recoveredSnapshot.native.workflowSummaries |
        Where-Object { $_.workflowExecutionId -ceq $executionBId }).activities |
        Where-Object { $_.executableNodeId -ceq 'after-rules' }
    if (@($successfulNativeActivity).Count -ne 1 -or $successfulNativeActivity[0].status -cne 'Completed' -or
        $successfulNativeActivity[0].attemptCountInternal -ne 1) {
        throw 'The resumed native following activity did not have exactly one completed internal activity attempt.'
    }
    $stageEvidence.Add([pscustomobject]@{
        stage = 'compatible-admission-restored'
        acceptNewInvocations = $true
        successfulWorkflowExecutionId = $executionBId
        successfulRulesActivityExecutionId = $successfulRules.activityExecutionId
        successfulRun = $successfulRun
        nativeResult = $result
        followingActivity = $successfulNativeActivity[0]
        cancelledWorkflowExecutionId = $executionAId
        cancelledRunAfterRecovery = $cancelledRunAfterRecovery
        cancelledWorkflowStatusAfterRecovery = $cancelledAfterRecovery.instance.status
    })

    $ctx = Start-RulesAdmissionHost
    $invalidName = "RulesInvalidRoundLimit-$([guid]::NewGuid().ToString('N'))"
    $invalidNode = New-RulesActivityNode -NodeId 'rules' -VersionId $runRulesVersion `
        -Program $program -InitialFacts @('seed') -MaxRounds 0
    $invalidFollowing = New-ActivityNode -NodeId 'after-rules' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value 'This activity must not run after an invalid Rules round limit.')
    )
    $invalidCapture = New-RulesResultCaptureNode -ProducerScopeId 'invalid-root'
    $invalidRoot = New-ActivityNode -NodeId 'invalid-root' -VersionId $sequenceVersion -Structure (
        New-SequenceStructure -Activities @($invalidNode, $invalidFollowing, $invalidCapture)
    )
    $invalidDefinition = Submit-Workflow -Ctx $ctx -Name $invalidName `
        -Description 'Invalid Rules common-envelope acceptance.' -RootActivity $invalidRoot
    $invalidPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $invalidDefinition.version.id
    $invalidExecution = Invoke-Artifact -Ctx $ctx -ArtifactId $invalidPublication.artifactId `
        -SourceReferenceId $invalidPublication.sourceReferenceId
    if ($invalidExecution.commandDispatchStatus -cne 'Accepted') { throw 'Invalid journey native start was not accepted.' }
    $invalidExecutionId = [string]$invalidExecution.workflowExecutionId
    $invalidInstance = Wait-RulesWorkflowStatus -Context $ctx -ExecutionId $invalidExecutionId `
        -ExpectedStatus @('Faulted') -TimeoutSeconds 90
    $invalidRules = Assert-NativeRulesActivity -Instance $invalidInstance -ExpectedStatus 'Faulted'
    if ((Get-NodeRunCount -Instance $invalidInstance -NodeId 'capture-rules-result') -ne 0 -or
        @($invalidInstance.incidents).Count -lt 1) {
        throw 'MaxRounds=0 did not settle through a native Rules fault before result capture.'
    }
    $invalidSnapshotPath = Join-Path $evidenceDirectory 'native-admission-invalid-snapshot.json'
    $invalidSnapshot = $null
    $invalidIntent = $null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    do {
        $invalidSnapshot = Get-RulesAdmissionSnapshot -Phase 'invalid-max-rounds-zero' `
            -Path $invalidSnapshotPath -WorkflowIds @($executionAId, $executionBId, $invalidExecutionId)
        $invalidIntent = Get-SingleRulesStartIntent -Snapshot $invalidSnapshot -WorkflowId $invalidExecutionId
        if ($invalidIntent.status -eq 'Delivered') { break }
        Start-Sleep -Milliseconds 300
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($invalidIntent.status -cne 'Delivered') { throw "The invalid-round-limit Start intent remained '$($invalidIntent.status)' instead of being settled." }
    $invalidRunId = [string]$invalidIntent.request.runId
    if ($invalidIntent.request.maxRounds -ne 0 -or
        @($invalidSnapshot.rules.runSummaries | Where-Object { $_.runId -ceq $invalidRunId }).Count -ne 0) {
        throw 'The invalid MaxRounds=0 invocation created a Rules row or did not retain its exact native request.'
    }
    Stop-OwnedElsaServer
    $invalidSnapshot = Get-RulesAdmissionSnapshot -Phase 'invalid-max-rounds-zero' `
        -Path $invalidSnapshotPath -WorkflowIds @($executionAId, $executionBId, $invalidExecutionId) -Record
    $invalidIntent = Get-SingleRulesStartIntent -Snapshot $invalidSnapshot -WorkflowId $invalidExecutionId
    if ($invalidIntent.status -cne 'Delivered' -or
        @($invalidSnapshot.rules.runSummaries | Where-Object { $_.runId -ceq $invalidRunId }).Count -ne 0) {
        throw 'The final read-only snapshot found a stranded invalid Start intent or an invalid Rules row.'
    }
    $stageEvidence.Add([pscustomobject]@{
        stage = 'invalid-native-round-limit'
        acceptNewInvocations = $true
        workflowExecutionId = $invalidExecutionId
        activityExecutionId = $invalidRules.activityExecutionId
        runId = $invalidRunId
        maxRounds = $invalidIntent.request.maxRounds
        workflowStatus = $invalidInstance.instance.status
        rulesActivityStatus = $invalidRules.status
        followingCount = 0
        incidentCount = @($invalidInstance.incidents).Count
        startOutboxStatus = $invalidIntent.status
        startDeliveryAttemptCount = $invalidIntent.deliveryAttemptCount
        rulesRowCount = 0
    })

    Write-RulesAdmissionEvidence -Passed $true
    $success = $true
} catch {
    try { Write-RulesAdmissionEvidence -Passed $false -ErrorMessage $_.Exception.Message } catch {
        Write-Host "  [evidence] could not write failure evidence: $_" -ForegroundColor Yellow
    }
    throw
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
