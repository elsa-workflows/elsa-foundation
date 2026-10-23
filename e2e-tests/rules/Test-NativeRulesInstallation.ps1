<#
.SYNOPSIS
    Preserve a waiting native workflow while adding Rules and its schema to the same SQLite database.
.DESCRIPTION
    Uses a qualified package-backed Workbench. Rules assemblies are present, but their features and schema
    are absent during the first process. Stops only its owned server, installs Rules, compares retained native
    state/schema without writes, then resumes the original Event and checks the original variable and identity.
    This is additive feature/schema installation, not a claim that a prior binary release was upgraded.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $AssemblyPath,
    [Parameter(Mandatory)][string] $DeploymentReceiptPath,
    [Parameter(Mandatory)][string] $EvidencePath,
    [int] $StartupTimeoutSeconds = 300
)
. "$PSScriptRoot/../durability/_DurabilityCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"
. "$PSScriptRoot/_RulesCommon.ps1"

$assemblyPath = (Get-Item -LiteralPath $AssemblyPath -ErrorAction Stop).FullName
$receiptPath = (Get-Item -LiteralPath $DeploymentReceiptPath -ErrorAction Stop).FullName
$evidencePath = [IO.Path]::GetFullPath($EvidencePath)
$evidenceDirectory = [IO.Path]::GetDirectoryName($evidencePath)
New-Item -Path $evidenceDirectory -ItemType Directory -Force | Out-Null
$beforePath = Join-Path $evidenceDirectory 'native-before-rules.json'
$afterPath = Join-Path $evidenceDirectory 'native-after-rules.json'
$snapshotTool = (Resolve-Path "$PSScriptRoot/../../tools/rules/native_upgrade_snapshot.py").Path
$python = if ($IsWindows) { 'python' } else { 'python3' }
$contentRoot = New-ElsaContentRoot -Prefix 'elsa-rules-installation'
$databasePath = Join-Path $contentRoot 'elsa.db'
$baseUrl = Resolve-ElsaServerBaseUrl
$environment = @{ ConnectionStrings__Elsa = "Data Source=$databasePath" }
foreach ($name in [Environment]::GetEnvironmentVariables('Process').Keys) {
    if ($name -match '^(Elsa__Persistence__|CShells__)') { $environment[[string]$name] = $null }
}
$success = $false
$startedAt = [DateTimeOffset]::UtcNow
$processIds = [System.Collections.Generic.List[int]]::new()

function Start-InstallationHost {
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot -AssemblyPath $assemblyPath `
        -Environment $environment -TimeoutSec $StartupTimeoutSeconds
    $processIds.Add($script:OwnedElsaServer.Process.Id)
    Connect-Elsa -BaseUrl $baseUrl
}

function Assert-WaitingNativeInstance {
    param([Parameter(Mandatory)] $Instance)
    if ((Get-RequiredNodeExecution -Instance $Instance -NodeId 'wait').status -ne 'Suspended' -or
        (Get-RequiredNodeExecution -Instance $Instance -NodeId 'set').status -ne 'Completed' -or
        (Get-NodeRunCount -Instance $Instance -NodeId 'echo') -ne 0 -or @($Instance.incidents).Count -ne 0) {
        throw 'Expected one completed Set, one suspended Event, no continuation and no incidents.'
    }
}

try {
    $settings = Get-Content -LiteralPath (Join-Path $contentRoot 'shells.json') -Raw | ConvertFrom-Json
    if (@($settings.CShells.Shells.default.Features.PSObject.Properties.Name | Where-Object { $_ -like 'Rules*' }).Count) {
        throw 'The baseline shell already enables Rules; it cannot prove additive installation.'
    }
    $ctx = Start-InstallationHost
    $sequence = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $event = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event'
    $eventName = "rules-install-$([guid]::NewGuid().ToString('N'))"
    $token = "retained-$([guid]::NewGuid().ToString('N'))"
    $root = New-ActivityNode -NodeId 'root' -VersionId $sequence -Structure (New-SequenceStructure -Activities @(
        (New-SetVariableNode -NodeId 'set' -VariableKey 'Msg' -Value $token),
        (New-EventWaitNode -NodeId 'wait' -EventVersionId $event -EventName $eventName),
        (New-SetOutputNode -NodeId 'echo' -OutputName 'Echo' -Value @{ value = 'Msg'; expressionType = 'Variable' })
    ))
    $definition = Submit-Workflow -Ctx $ctx -Name $eventName -RootActivity $root -Variables @((New-VariableDef -Key 'Msg'))
    $publication = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    $execution = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    if ($execution.commandDispatchStatus -ne 'Accepted') { throw 'The baseline start was not accepted.' }
    $executionId = $execution.workflowExecutionId
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        $before = Get-WorkflowInstance -Ctx $ctx -ExecutionId $executionId
        if (Test-NodeSuspended -Instance $before) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    Assert-WaitingNativeInstance -Instance $before
    Stop-OwnedElsaServer
    & $python $snapshotTool $databasePath $beforePath
    if ($LASTEXITCODE -ne 0) { throw 'The pre-install native snapshot failed.' }

    # Add Rules to the existing database, rather than hiding coexistence behind a new Rules-only file.
    Set-RulesFeatureConfiguration -ContentRoot $contentRoot -HostRoot ([IO.Path]::GetDirectoryName($assemblyPath)) `
        -HostDepsPath ([IO.Path]::ChangeExtension($assemblyPath, '.deps.json')) `
        -DeploymentReceiptPath $receiptPath -DatabasePath $databasePath
    $ctx = Start-InstallationHost
    $after = Get-WorkflowInstance -Ctx $ctx -ExecutionId $executionId
    Assert-WaitingNativeInstance -Instance $after
    Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Rules.Workflows.Runtime.Activities.RunRules' | Out-Null
    Stop-OwnedElsaServer
    & $python $snapshotTool $databasePath $afterPath --before $beforePath
    if ($LASTEXITCODE -ne 0) { throw 'The additive installation changed retained native state or schema.' }

    $ctx = Start-InstallationHost
    $response = Invoke-ResumeStimulus -Ctx $ctx -EventName $eventName
    if ($response.resumedCount -ne 1) { throw "Expected exactly one resumed original workflow; got $($response.resumedCount)." }
    $completed = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $executionId -TimeoutSeconds 90
    if ($completed.instance.status -notin @('Completed', 'Finished') -or @($completed.incidents).Count -ne 0 -or
        (Get-NodeRunCount -Instance $completed -NodeId 'set') -ne 1 -or
        (Get-NodeRunCount -Instance $completed -NodeId 'echo') -ne 1 -or
        (Get-RequiredNodeExecution -Instance $completed -NodeId 'echo').status -ne 'Completed' -or
        $completed.outputs.Echo.value.preview -cne $token) {
        throw 'The original native workflow did not complete once with its retained variable.'
    }
    # A second stimulus must not create another continuation or a new workflow.
    $duplicate = Invoke-ResumeStimulus -Ctx $ctx -EventName $eventName
    if ($duplicate.resumedCount -ne 0) { throw 'The completed native wait matched a duplicate stimulus.' }
    $final = Get-WorkflowInstance -Ctx $ctx -ExecutionId $executionId
    if ((Get-NodeRunCount -Instance $final -NodeId 'echo') -ne 1) { throw 'Duplicate continuation after installation.' }
    $snapshot = Get-Content -LiteralPath $afterPath -Raw | ConvertFrom-Json
    $evidence = [ordered]@{
        passed = $true
        schemaVersion = 1
        startedAt = $startedAt
        completedAt = [DateTimeOffset]::UtcNow
        scope = 'Additive Rules feature/schema installation in one SQLite database; same package-backed host binaries.'
        workflowExecutionId = $executionId
        artifactId = $publication.artifactId
        sourceReferenceId = $publication.sourceReferenceId
        processIds = $processIds.ToArray()
        workbenchAssemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        deploymentReceiptSha256 = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash.ToLowerInvariant()
        beforeSnapshot = $beforePath
        beforeSnapshotSha256 = (Get-FileHash -LiteralPath $beforePath -Algorithm SHA256).Hash.ToLowerInvariant()
        afterSnapshot = $afterPath
        afterSnapshotSha256 = (Get-FileHash -LiteralPath $afterPath -Algorithm SHA256).Hash.ToLowerInvariant()
        preservedNativeStateAndSchema = $snapshot.preservedNativeStateAndSchema
        nativeStateSha256 = $snapshot.stateSha256
        addedSchemaObjects = $snapshot.addedSchemaObjects
        finalStatus = $completed.instance.status
        retainedVariable = $token
        followingActivityCount = Get-NodeRunCount -Instance $final -NodeId 'echo'
        resumedCount = $response.resumedCount
        duplicateResumedCount = $duplicate.resumedCount
    }
    $evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $evidencePath -Encoding utf8
    $success = $true
    Write-Host 'SUCCESS - retained native execution survived additive Rules installation and completed once.'
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
