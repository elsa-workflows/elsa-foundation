<#
.SYNOPSIS
    Crashes an owned source-built Workbench after cohort commit or a completed partial callback; recovers with the standard build.
.DESCRIPTION
    Requires separately built probe/standard assemblies. Uses normal authenticated APIs, the real provider/router/
    dispatchers, and checked coherent SQLite observations. Only processes created here are stopped.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ProbeAssembly,
    [Parameter(Mandatory)][string] $NormalAssembly,
    [Parameter(Mandatory)][string] $ReaderAssembly,
    [Parameter(Mandatory)][string] $EvidencePath,
    [string] $Python = 'python3',
    [Parameter(Mandatory)][string] $SourceRevision,
    [ValidateSet('after-cohort', 'after-resume', 'before-selection', 'empty-cohort', 'competing-proposal', 'competing-empty-proposal', 'competing-empty-winner', 'after-start', 'after-fanout', 'after-source-commit', 'renewed-claim', 'republished-cohort', 'same-artifact-shed')][string] $Phase = 'after-cohort'
)
$ErrorActionPreference = 'Stop'
$sameArtifactShed = $Phase -eq 'same-artifact-shed'
$republishedCohort = $Phase -eq 'republished-cohort'
$renewedClaim = $Phase -eq 'renewed-claim'
$committedAck = $Phase -eq 'after-source-commit'
$completeFanout = $Phase -in @('after-fanout', 'after-source-commit')
$partialStart = $Phase -eq 'after-start'
$emptyProposal = $Phase -eq 'competing-empty-proposal'
$emptyWinner = $Phase -eq 'competing-empty-winner'
$competing = $Phase -in @('competing-proposal', 'competing-empty-proposal', 'competing-empty-winner')
$unmatchedInitial = $Phase -eq 'empty-cohort' -or $emptyProposal
$selectsAfterRestart = $Phase -in @('before-selection', 'competing-proposal', 'competing-empty-proposal')
if ($competing -and $IsWindows) { throw 'This owned process-suspension race requires Unix signals.' }
. "$PSScriptRoot/../_ServerLifecycle.ps1"
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../runtime-alterations/_AlterationCommon.ps1"
# Verify assembly metadata and the full adjacent runtime closure before launching either build.
function Read-BuildManifest([string] $Assembly) {
    $text = & dotnet $ReaderAssembly --inspect-host $Assembly
    if ($LASTEXITCODE -ne 0) { throw 'The host build manifest could not be inspected.' }
    return $text | ConvertFrom-Json -AsHashtable -DateKind String
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourceCommit = (& git -C $repository rev-parse HEAD).Trim()
if (-not $SourceRevision.StartsWith($sourceCommit, [StringComparison]::Ordinal)) { throw 'The candidate revision must name the current full source commit, with an optional explicit worktree suffix.' }
function Read-SourceManifest {
    $files = & git -C $repository ls-files --cached --others --exclude-standard -- Essentials/src/Activities/Bpmn Essentials/src/Activities/Primitives Essentials/src/Workflows/Runtime Essentials/apps/Elsa.Workbench e2e-tests/bpmn tools/bpmn/DeliveryRecoveryRead e2e-tests/_ElsaCommon.ps1 e2e-tests/_ServerLifecycle.ps1 e2e-tests/runtime-alterations/_AlterationCommon.ps1
    if ($LASTEXITCODE -ne 0) { throw 'The source manifest could not be enumerated.' }
    return @($files | Sort-Object -Unique | ForEach-Object {
        $path = Join-Path $repository $_
        @{ name = $_; sha256 = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() } else { 'deleted' } }
    })
}
$sourceManifest = Read-SourceManifest
$probeManifest = Read-BuildManifest $ProbeAssembly
$normalManifest = Read-BuildManifest $NormalAssembly
if (-not $probeManifest.containsRecoveryProbe -or $normalManifest.containsRecoveryProbe) { throw 'The supplied binaries do not separate the fault probe from the standard host.' }
$readerHash = (Get-FileHash $ReaderAssembly).Hash.ToLowerInvariant()
$readerManifest = Read-BuildManifest $ReaderAssembly
$root = New-ElsaContentRoot -Prefix 'elsa-bpmn-source-recovery'
$base = Resolve-ElsaServerBaseUrl
$control = Join-Path $root 'recovery-control'; New-Item -ItemType Directory $control | Out-Null
$caseId = $Phase + '-' + [guid]::NewGuid().ToString('N')
$eventName = 'bpmn-source-recovery-' + [guid]::NewGuid().ToString('N')
$caseDirectory = Join-Path $control $caseId
$markerPath = Join-Path $caseDirectory 'reached.json'
$settingsPath = Join-Path $root 'shells.json'
$settings = Get-Content -Raw $settingsPath | ConvertFrom-Json -AsHashtable
$settings.CShells.Shells.default.Features.BpmnRecoveryProbe = @{ ControlDirectory = $control }
$settings.CShells.Shells.default.Features.WorkflowsRuntimeResumption = @{ SweepIntervalSeconds = 1 }
if ($republishedCohort) {
    $settings.CShells.Shells.default.Features.WorkflowsRuntimeReferenceGarbageCollection = @{
        SweepIntervalMinutes = 0.02; MaxBackoffIntervalMinutes = 0.1; ArtifactCreationGracePeriodMinutes = 0
    }
}
$settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
@{ caseId = $caseId; phase = if ($competing) { 'before-install' } elseif ($Phase -eq 'empty-cohort' -or $sameArtifactShed) { 'after-cohort' } else { $Phase }; eventName = $eventName } | ConvertTo-Json | Set-Content (Join-Path $control 'plan.json')
$receipt = @{ sourceRevision = $SourceRevision; scenario = switch ($Phase) { 'after-resume' { 'partial-callback-and-rearm-before-source-ack' } 'after-start' { 'one-of-two-starts-parked-before-remaining-start-and-resumes' } 'after-fanout' { 'all-recipient-effects-completed-before-source-ack-write' } 'after-source-commit' { 'committed-source-completion-before-provider-return-loss' } 'renewed-claim' { 'real-pre-dispatch-claim-renewal-then-crash-and-standard-takeover' } 'republished-cohort' { 'saved-old-publication-retained-through-real-collection-and-republish' } 'same-artifact-shed' { 'real-capacity-shed-then-same-artifact-key-reuse-and-recovery' } 'before-selection' { 'delayed-first-selection-and-pre-start-wait-exclusion' } 'empty-cohort' { 'empty-saved-cohort-excludes-later-matches' } 'competing-proposal' { 'expired-old-prepared-proposal-cannot-replace-successor-winner' } 'competing-empty-proposal' { 'expired-old-empty-proposal-cannot-replace-populated-successor-winner' } 'competing-empty-winner' { 'expired-old-populated-proposal-cannot-replace-empty-successor-winner' } default { 'after-cohort-before-dispatch' } }; passed = $false;
    probeAssemblySha256 = (Get-FileHash $ProbeAssembly).Hash.ToLowerInvariant(); normalAssemblySha256 = (Get-FileHash $NormalAssembly).Hash.ToLowerInvariant();
    sourceCommit = $sourceCommit; sourceFiles = $sourceManifest; probeBuild = $probeManifest; normalBuild = $normalManifest; readerSha256 = $readerHash; readerBuild = $readerManifest;
    cut = $null; stopped = $null; successorClaim = $null; recovered = $null; observations = @(); failures = @() }
$client = $null; $pending = $null; $watch = $null; $oldBroker = $null; $oldBrokerStop = $null
$recoveryRoot = $root; $recoveryBase = $base; $sharedEnvironment = @{}
if ($competing) {
    $suffix = [guid]::NewGuid().ToString('N')
    $oldNodeId = "bpmn-old-$suffix"; $peerNodeId = "bpmn-peer-$suffix"
    $sharedEnvironment = @{
        'ConnectionStrings__Elsa' = "Data Source=$(Join-Path $root 'elsa.db')"
        'Elsa__Cluster__Membership__EntityFrameworkCore__Enabled' = 'true'
        'Elsa__Cluster__Membership__EntityFrameworkCore__Provider' = 'Sqlite'
        'Elsa__Cluster__Membership__EntityFrameworkCore__ConnectionString' = "Data Source=$(Join-Path $root 'membership.db')"
        'Elsa__Cluster__Membership__HeartbeatInterval' = '00:00:01'
        'Elsa__Cluster__Membership__ExpiryPeriod' = '00:00:04'
        'Elsa__Cluster__Membership__SkewAllowance' = '00:00:00'
    }
    $settings.CShells.Shells.default.Features.FileSystemDistributedLocking = @{ LocksFolderPath = (Join-Path $root 'shared-locks') }
    $settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
    $receipt.oldNodeId = $oldNodeId; $receipt.peerNodeId = $peerNodeId
}
function Import-Process([string] $Xml) {
    $import = Invoke-RestMethod "$($ctx.BaseUrl)/interchange/bpmn/import" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (@{ xml = $Xml } | ConvertTo-Json -Depth 30)
    if (-not $import.analysis.hostInventoryVerified -or -not $import.analysis.canExecute) { throw 'The actual catalog could not execute the imported fixture.' }
    $definition = Submit-Workflow -Ctx $ctx -Name "BpmnSourceRecovery-$([guid]::NewGuid().ToString('N'))" -RootActivity $import.processNode
    return Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
}
function Wait-Catches([string] $ExecutionId, [int] $Count) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        $instance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $ExecutionId
        # Node IDs are authored fixture identities; root status is independent.
        $catches = @($instance.activities | Where-Object { $_.status -eq 'Suspended' -and $_.executableNodeId -like 'node-wait*' })
        if ($catches.Count -eq $Count) { return $instance }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Expected exact waits were not durably suspended before the cut.'
}
function Observe([string] $Name, [string[]] $ExtraIds, [string] $Marker = $markerPath) {
    $path = Join-Path $caseDirectory "$Name.json"
    if ($sameArtifactShed -and $receipt.loadWorkflowId) { $ExtraIds = @($ExtraIds) + @($receipt.loadWorkflowId) }
    $arguments = @("$PSScriptRoot/inspect_broadcast_recovery.py", '--database', (Join-Path $root 'elsa.db'), '--reader', $ReaderAssembly, '--marker', $Marker, '--output', $path)
    foreach ($id in $ExtraIds) { $arguments += @('--extra-workflow', $id) }
    & $Python @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "The checked $Name observation failed." }
    return Get-Content -Raw $path | ConvertFrom-Json -AsHashtable -DateKind String
}
function Observe-Publisher([string] $Name, [string] $PublisherId) {
    $path = Join-Path $caseDirectory "$Name.json"
    & $Python "$PSScriptRoot/inspect_broadcast_recovery.py" --database (Join-Path $root 'elsa.db') --reader $ReaderAssembly --marker $markerPath --output $path --publisher $PublisherId | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The checked control-publisher observation failed.' }
    return Get-Content -Raw $path | ConvertFrom-Json -AsHashtable -DateKind String
}
function Observe-Membership([string] $Name) {
    $path = Join-Path $caseDirectory "$Name.json"
    & $Python "$PSScriptRoot/inspect_broadcast_recovery.py" --membership --database (Join-Path $root 'membership.db') --reader $ReaderAssembly --marker $markerPath --output $path | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The actual membership observation failed.' }
    return Get-Content -Raw $path | ConvertFrom-Json -AsHashtable -DateKind String
}
function Same-Snapshot($Left, $Right) {
    if ($null -eq $Left -or $null -eq $Right) { return ($null -eq $Left -and $null -eq $Right) }
    $leftDocument = [System.Text.Json.JsonDocument]::Parse(($Left | ConvertTo-Json -Depth 60 -Compress))
    $rightDocument = [System.Text.Json.JsonDocument]::Parse(($Right | ConvertTo-Json -Depth 60 -Compress))
    try { return [System.Text.Json.JsonElement]::DeepEquals($leftDocument.RootElement, $rightDocument.RootElement) }
    finally { $leftDocument.Dispose(); $rightDocument.Dispose() }
}
function Assert-SameArtifactCohort($Observation, $Publications) {
    $entries = @($Observation.source.selectedStarts)
    if ($Publications.Count -ne 2 -or @($Publications.artifactId | Sort-Object -Unique).Count -ne 1 -or
        @($Publications.publicationId | Sort-Object -Unique).Count -ne 2 -or @($Publications.sourceReferenceId | Sort-Object -Unique).Count -ne 2 -or
        $entries.Count -ne 2 -or @($entries.artifactId | Sort-Object -Unique).Count -ne 1 -or
        @($entries.workflowExecutionId | Sort-Object -Unique).Count -ne 1 -or @($entries.startKey | Sort-Object -Unique).Count -ne 1 -or
        @($entries.triggerBindingId | Sort-Object -Unique).Count -ne 2 -or $Observation.source.snapshot.starts.Count -ne 2) {
        throw 'The saved publications do not retain two binding entries and one artifact-scoped key/execution.'
    }
    $capturedIds = @()
    foreach ($entry in $Observation.source.snapshot.starts) {
        $captured = $entry.sourceCapture.provenance
        $published = @($Publications | Where-Object sourceReferenceId -eq $captured.sourceReferenceId)
        if ($entry.sourceCapture.version -ne 1 -or $published.Count -ne 1 -or $captured.publicationId -ne $published[0].publicationId -or
            $entry.binding.activationId -ne $captured.publicationId -or $entry.binding.slotId -ne $captured.slotId -or
            $entry.executable.artifactId -ne $published[0].artifactId -or $entry.executable.artifactHash -ne $published[0].artifactHash -or
            $captured.definitionVersionId -ne $published[0].definitionVersionId) { throw 'A saved alias binding does not retain its exact public publication provenance.' }
        $capturedIds += $captured.sourceReferenceId
    }
    if (@($capturedIds | Sort-Object -Unique).Count -ne 2) { throw 'The two alias bindings collapsed onto one publication source.' }
}
function Assert-ActualSameArtifactShed($Initial, $Shed, $Marker, $Load) {
    $attempt = $Marker.observedStart; $entries = @($Initial.source.selectedStarts)
    foreach ($key in @('idempotencyKey', 'workflowExecutionId', 'artifactId', 'triggerBindingId', 'status', 'isShed', 'inFlightBefore', 'hadAmbientChargeBefore', 'limit', 'controller')) {
        if (-not $attempt.ContainsKey($key)) { throw "The actual shed observation omits $key." }
    }
    if (-not $Load.hasAmbientCharge -or $Load.inFlightDispatches -lt 1 -or $Load.limit -ne 1 -or
        -not $attempt.isShed -or $attempt.status -ne 'Deferred' -or $attempt.hadAmbientChargeBefore -or $attempt.inFlightBefore -lt 1 -or $attempt.limit -ne 1 -or $attempt.controller -ne $Load.controller -or
        $attempt.idempotencyKey -ne $entries[0].startKey -or $attempt.workflowExecutionId -ne $entries[0].workflowExecutionId -or $attempt.artifactId -ne $entries[0].artifactId -or $attempt.triggerBindingId -ne $entries[0].triggerBindingId -or
        $Shed.source.outboxItemId -ne $Initial.source.outboxItemId -or $Marker.sourceOutboxItemId -ne $Initial.source.outboxItemId -or
        $Initial.source.deliveryFencingToken -ne 1 -or $Shed.source.deliveryFencingToken -ne 2 -or $Marker.fencingToken -ne 2 -or
        $Shed.source.status -ne 'Delivering' -or $Shed.source.deliveringOwnerId -eq $Initial.source.deliveringOwnerId -or $Shed.source.deliveringOwnerId -ne $Marker.ownerId -or
        $Shed.source.intentFingerprint -ne $Initial.source.intentFingerprint -or @($Shed.allWorkflows | Where-Object artifactId -eq $entries[0].artifactId).Count -ne 0 -or
        @($Shed.workflows | Where-Object { $_.workflowExecutionId -eq $attempt.workflowExecutionId -and $_.exists }).Count -ne 0 -or
        -not (Same-Snapshot $Shed.source.snapshot $Initial.source.snapshot)) { throw 'The real first binding was not shed under held capacity without a target or key consumption.' }
}
function Assert-ActualSameArtifactAccepted($Initial, $Shed, $ShedMarker, $Accepted, $Marker, [DateTimeOffset] $LoadReleasedAt, [DateTimeOffset] $LoadRespondedAt) {
    $first = $ShedMarker.observedStart; $next = $Marker.observedStart; $entries = @($Initial.source.selectedStarts)
    foreach ($key in @('idempotencyKey', 'workflowExecutionId', 'artifactId', 'triggerBindingId', 'status', 'isShed', 'inFlightBefore', 'hadAmbientChargeBefore', 'limit', 'controller')) {
        if (-not $next.ContainsKey($key)) { throw "The actual accepted-start observation omits $key." }
    }
    if ($next.isShed -or $next.status -ne 'Accepted' -or $next.hadAmbientChargeBefore -or $next.inFlightBefore -ne 0 -or $next.limit -ne 1 -or $next.controller -ne $first.controller -or
        $next.idempotencyKey -ne $first.idempotencyKey -or $next.workflowExecutionId -ne $first.workflowExecutionId -or $next.artifactId -ne $first.artifactId -or $next.triggerBindingId -ne $entries[1].triggerBindingId -or
        $Accepted.source.outboxItemId -ne $Initial.source.outboxItemId -or $Marker.sourceOutboxItemId -ne $Initial.source.outboxItemId -or
        $Marker.fencingToken -ne 2 -or $Marker.ownerId -ne $Shed.source.deliveringOwnerId -or -not (Same-Snapshot $Accepted.source $Shed.source) -or
        @($Accepted.allWorkflows | Where-Object artifactId -eq $next.artifactId).Count -ne 1 -or
        [DateTimeOffset]::Parse($ShedMarker.capturedAtUtc) -gt $LoadReleasedAt -or $LoadReleasedAt -gt $LoadRespondedAt -or $LoadRespondedAt -gt [DateTimeOffset]::Parse($Marker.capturedAtUtc)) {
        throw 'The later binding did not reuse the same unconsumed key after the real load command returned.'
    }
}
function Assert-RepublishedCollectionState($Observation, $Original, $Replacement, $Control, [bool] $AfterCollection) {
    foreach ($required in @('observedMaterial', 'observedTemplates', 'observedSourceReferences', 'matchingBindings', 'allWorkflows', 'executionRootArtifactIds', 'dispatchRootArtifactIds')) {
        if (-not $Observation.ContainsKey($required)) { throw "The checked retention inventory omits $required." }
    }
    if (@($Observation.observedMaterial | Where-Object { -not $_.ContainsKey('dependencies') -or -not $_.ContainsKey('templateHashReferences') }).Count -ne 0 -or
        @($Observation.observedTemplates | Where-Object { -not $_.ContainsKey('directDependencies') -or -not $_.ContainsKey('closedTemplates') -or -not $_.ContainsKey('templateHashReferences') }).Count -ne 0) {
        throw 'The checked retention inventory omits an indirect dependency projection.'
    }
    $oldMaterial = @($Observation.observedMaterial | Where-Object { $_.identity.artifactId -eq $Original.artifactId })
    $newMaterial = @($Observation.observedMaterial | Where-Object { $_.identity.artifactId -eq $Replacement.artifactId })
    $controlMaterial = @($Observation.observedMaterial | Where-Object { $_.identity.artifactId -eq $Control.artifactId })
    $oldRefs = @($Observation.observedSourceReferences | Where-Object artifactId -eq $Original.artifactId)
    $newRefs = @($Observation.observedSourceReferences | Where-Object artifactId -eq $Replacement.artifactId)
    $controlRefs = @($Observation.observedSourceReferences | Where-Object artifactId -eq $Control.artifactId)
    if ($oldMaterial.Count -ne 1 -or $newMaterial.Count -ne 1 -or $oldMaterial[0].identity.artifactHash -ne $Original.artifactHash -or
        $oldRefs.Count -ne 1 -or $oldRefs[0].sourceReferenceId -ne $Original.sourceReferenceId -or -not $oldRefs[0].deletedAt -or
        $newRefs.Count -ne 1 -or $newRefs[0].sourceReferenceId -ne $Replacement.sourceReferenceId -or $newRefs[0].deletedAt -or
        @($Observation.matchingBindings | Where-Object { $_.artifactId -eq $Original.artifactId -and $_.isActive }).Count -ne 0 -or
        @($Observation.matchingBindings | Where-Object { $_.artifactId -eq $Replacement.artifactId -and $_.isActive }).Count -ne 1 -or
        @($Observation.allWorkflows | Where-Object { $_.artifactId -in @($Original.artifactId, $Replacement.artifactId, $Control.artifactId) }).Count -ne 0 -or
        $Original.artifactId -in $Observation.executionRootArtifactIds -or $Original.artifactId -in $Observation.dispatchRootArtifactIds -or
        @($Observation.observedTemplates | Where-Object { $_.templateId -eq $Original.artifactId -or @($_.directDependencies).Count -ne 0 -or
            @($_.closedTemplates).Count -ne 0 -or @($_.templateHashReferences).Count -ne 0 }).Count -ne 0 -or
        @($Observation.observedMaterial | Where-Object { @($_.dependencies).Count -ne 0 -or @($_.templateHashReferences).Count -ne 0 }).Count -ne 0) {
        throw 'The old publication is not an unstarted saved-cohort root with retired-only references and a distinct active replacement.'
    }
    if ($AfterCollection) {
        if ($controlMaterial.Count -ne 0 -or $controlRefs.Count -ne 0) { throw 'Actual collection left the unrooted control material or reference behind.' }
    } elseif ($controlMaterial.Count -ne 1 -or $controlRefs.Count -ne 1 -or -not $controlRefs[0].deletedAt) {
        throw 'The unrooted control was not physically present and retired before actual collection.'
    }
}
function Assert-ActualClaimRenewal($Initial, $InitialMarker, $Renewed, $Marker, [DateTimeOffset] $ReleasedAt) {
    $initialDeadline = [DateTimeOffset]::Parse($Initial.source.deliveryVisibleAfter)
    $cutAt = [DateTimeOffset]::Parse($Marker.capturedAtUtc)
    if ($Initial.source.outboxItemId -ne $InitialMarker.sourceOutboxItemId -or
        $Renewed.source.outboxItemId -ne $Initial.source.outboxItemId -or $Marker.sourceOutboxItemId -ne $Initial.source.outboxItemId -or
        $InitialMarker.scope -ne $Marker.scope -or $Renewed.source.workflowExecutionId -ne $Initial.source.workflowExecutionId -or
        $Renewed.source.deliveringOwnerId -ne $Initial.source.deliveringOwnerId -or $Marker.ownerId -ne $Initial.source.deliveringOwnerId -or
        $Renewed.source.deliveryFencingToken -ne $Initial.source.deliveryFencingToken -or $Marker.fencingToken -ne $Initial.source.deliveryFencingToken -or
        $Renewed.source.intentFingerprint -ne $Initial.source.intentFingerprint -or
        $Renewed.source.rowRevision -le $Initial.source.rowRevision -or
        [DateTimeOffset]::Parse($Renewed.source.deliveryVisibleAfter) -lt $initialDeadline.AddSeconds(2) -or
        $Renewed.source.deliveryVisibleAfter -ne $Marker.visibleAfter -or $Renewed.source.status -ne 'Delivering' -or
        $ReleasedAt -ge $initialDeadline -or $cutAt -ge $initialDeadline -or $cutAt -lt $ReleasedAt) {
        throw 'The actual processor did not renew the same unexpired physical claim before selecting recipients.'
    }
}
try {
    if ($competing) {
        Assert-OwnableAddress -BaseUrl $base
        $oldEnvironment = @{} + $sharedEnvironment; $oldEnvironment.Elsa__Cluster__Membership__HostId = $oldNodeId
        $environmentFile = Join-Path $control 'probe-environment.json'; $oldEnvironment | ConvertTo-Json | Set-Content $environmentFile
        $oldBrokerStop = Join-Path $control 'stop-owned-probe'
        $manifest = Join-Path $control 'owned-probe.json'
        $start = [Diagnostics.ProcessStartInfo]::new(); $start.FileName = $Python; $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        foreach ($argument in @("$PSScriptRoot/run_owned_broadcast_probe.py", '--assembly', $ProbeAssembly, '--content-root', $root,
            '--base-url', $base, '--environment-file', $environmentFile, '--manifest', $manifest, '--stop-file', $oldBrokerStop)) { $start.ArgumentList.Add($argument) }
        $oldBroker = [Diagnostics.Process]::Start($start); $oldBrokerOut = $oldBroker.StandardOutput.ReadToEndAsync(); $oldBrokerError = $oldBroker.StandardError.ReadToEndAsync()
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(300)
        while (-not (Test-Path $manifest) -or -not (Test-ElsaServerReady -BaseUrl $base)) {
            if ($oldBroker.HasExited) { throw "Owned probe controller exited: $($oldBrokerError.GetAwaiter().GetResult())" }
            if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'The owned probe did not become ready.' }
            Start-Sleep -Milliseconds 200
        }
        $owned = Get-Content -Raw $manifest | ConvertFrom-Json -AsHashtable
        $probeProcess = [Diagnostics.Process]::GetProcessById($owned.processId)
        $registeredStart = [long]$owned.startEpochSeconds
        $receipt.probeControllerProcessId = $oldBroker.Id
        if ($probeProcess.Id -notin @(Get-PortListenerPid -Port ([Uri]$base).Port)) { throw 'The ready endpoint does not belong to the exact controller-owned process.' }
    } else {
        Start-OwnedElsaServer -BaseUrl $base -ContentRoot $root -AssemblyPath $ProbeAssembly
        $probeProcess = $script:OwnedElsaServer.Process
    }
    $ctx = Connect-Elsa -BaseUrl $base
    $waitXml = @"
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:source-recovery"><signal id="signal" name="$eventName"/>
<process id="waiter" isExecutable="true"><startEvent id="start"/><parallelGateway id="fork"/><parallelGateway id="join"/><endEvent id="end"/>
<intermediateCatchEvent id="wait-a"><signalEventDefinition signalRef="signal"/></intermediateCatchEvent><intermediateCatchEvent id="wait-b"><signalEventDefinition signalRef="signal"/></intermediateCatchEvent>
<sequenceFlow id="begin" sourceRef="start" targetRef="fork"/><sequenceFlow id="a" sourceRef="fork" targetRef="wait-a"/><sequenceFlow id="b" sourceRef="fork" targetRef="wait-b"/>
<sequenceFlow id="a-join" sourceRef="wait-a" targetRef="join"/><sequenceFlow id="b-join" sourceRef="wait-b" targetRef="join"/><sequenceFlow id="finish" sourceRef="join" targetRef="end"/></process></definitions>
"@
    if ($Phase -eq 'after-resume') {
        # Both branches rearm the same authored listener after one callback. Whichever recipient sorts first
        # can therefore prove real consume/rearm exclusion while the other original recipient is still waiting.
        $waitXml = $waitXml.Replace('<parallelGateway id="join"/>', '').Replace('<endEvent id="end"/>', '')
        $waitXml = $waitXml.Replace('sourceRef="wait-a" targetRef="join"', 'sourceRef="wait-a" targetRef="wait-a"')
        $waitXml = $waitXml.Replace('sourceRef="wait-b" targetRef="join"', 'sourceRef="wait-b" targetRef="wait-b"')
        $waitXml = $waitXml.Replace('<sequenceFlow id="finish" sourceRef="join" targetRef="end"/>', '')
    }
    $initialWaitXml = if ($unmatchedInitial) { $waitXml.Replace($eventName, "unmatched-$eventName") } else { $waitXml }
    $waitPublication = Import-Process $initialWaitXml
    $waitRun = Invoke-Artifact -Ctx $ctx -ArtifactId $waitPublication.artifactId -SourceReferenceId $waitPublication.sourceReferenceId
    $null = Wait-Catches $waitRun.workflowExecutionId 2
    # Published before the snapshot; this start must survive a process restart with its original artifact-scoped key.
    $parkName = if ($selectsAfterRestart) { $eventName } else { "park-$eventName" }
    $startXml = @"
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:source-recovery"><signal id="signal" name="$eventName"/><signal id="park" name="$parkName"/>
<process id="selected-start" isExecutable="true"><startEvent id="receive"><signalEventDefinition signalRef="signal"/></startEvent><intermediateCatchEvent id="park"><signalEventDefinition signalRef="park"/></intermediateCatchEvent><endEvent id="end"/>
<sequenceFlow id="first" sourceRef="receive" targetRef="park"/><sequenceFlow id="last" sourceRef="park" targetRef="end"/></process></definitions>
"@
    if ($selectsAfterRestart) {
        $startXml = $startXml.Replace("<signal id=`"park`" name=`"$parkName`"/>", '').Replace('signalRef="park"', 'signalRef="signal"')
    }
    $initialStartXml = if ($unmatchedInitial) { $startXml.Replace($eventName, "unmatched-$eventName") } else { $startXml }
    $selectedPublication = Import-Process $initialStartXml
    $secondInitialPublication = $null
    if ($partialStart) { $secondInitialPublication = Import-Process ($startXml.Replace('id="selected-start"', 'id="second-start"')) }
    if ($republishedCohort) {
        $collectionControl = Import-Process ($startXml.Replace('id="selected-start"', 'id="collection-control"').Replace($eventName, "unmatched-control-$eventName"))
        $receipt.collectionControlPublication = $collectionControl
    }
    if ($sameArtifactShed) {
        $alias = Invoke-RestMethod "$base/publishing/workflows/$($selectedPublication.versionId)/publish" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (@{ action = 'SideBySide'; slotName = 'same-artifact-alias' } | ConvertTo-Json)
        if ($alias.artifactId -ne $selectedPublication.artifactId -or $alias.sourceReferenceId -eq $selectedPublication.sourceReferenceId -or $alias.publicationId -eq $selectedPublication.publicationId) { throw 'The public alias did not produce distinct eligible bindings of the exact same artifact.' }
        $loadXml = $waitXml.Replace('id="waiter"', 'id="actual-load"').Replace($eventName, "load-$eventName")
        $loadPublication = Import-Process $loadXml
        $receipt.sameArtifactPublications = @($selectedPublication, $alias); $receipt.loadPublication = $loadPublication
    }
    $publisherXml = @"
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:source-recovery"><signal id="signal" name="$eventName"/>
<process id="publisher" isExecutable="true"><startEvent id="start"/><intermediateThrowEvent id="send"><signalEventDefinition signalRef="signal"/></intermediateThrowEvent><endEvent id="end"/>
<sequenceFlow id="begin" sourceRef="start" targetRef="send"/><sequenceFlow id="finish" sourceRef="send" targetRef="end"/></process></definitions>
"@
    $publisher = Import-Process $publisherXml
    $handler = [System.Net.Http.HttpClientHandler]::new(); $handler.CookieContainer = $ctx.Session.Cookies
    $client = [System.Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromMinutes(5)
    $content = [System.Net.Http.StringContent]::new((@{ sourceReferenceId = $publisher.sourceReferenceId } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json')
    $pending = $client.PostAsync("$base/runtime/workflows/executables/$($publisher.artifactId)/execute", $content)
    if ($renewedClaim) {
        $initialMarkerPath = Join-Path $caseDirectory 'initial-claim.json'
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(40)
        while (-not (Test-Path $initialMarkerPath)) {
            if ($probeProcess.HasExited) { throw 'The owned probe exited before its actual claim save.' }
            if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'The initial claim cut was not reached.' }
            Start-Sleep -Milliseconds 100
        }
        $initialMarker = Get-Content -Raw $initialMarkerPath | ConvertFrom-Json -AsHashtable -DateKind String
        $initial = Observe 'initial-claim-live' @($waitRun.workflowExecutionId) $initialMarkerPath
        if ($initialMarker.providerEvent -ne 'EF.SaveChangesCompleted' -or $initial.source.status -ne 'Delivering' -or
            $initial.source.deliveryFencingToken -ne 1 -or $initial.source.deliveringOwnerId -ne $initialMarker.ownerId -or
            $initial.source.deliveryVisibleAfter -ne $initialMarker.visibleAfter -or $null -ne $initial.source.snapshot) {
            throw 'The independent checked reader did not prove the actual first claim before renewal.'
        }
        $receipt.initialClaimMarker = $initialMarker; $receipt.initialClaimLive = $initial
        # A short real delay makes renewal distinguishable without changing the clock or waiting for lease loss.
        Start-Sleep -Seconds 2
        $beforeRelease = Observe 'initial-claim-before-release' @($waitRun.workflowExecutionId) $initialMarkerPath
        if (-not (Same-Snapshot $initial.source $beforeRelease.source)) { throw 'The held claim changed before the processor could renew it.' }
        $receipt.initialClaimBeforeRelease = $beforeRelease
        $receipt.initialClaimReleasedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        New-Item -ItemType File (Join-Path $caseDirectory 'release-initial-claim') | Out-Null
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(40)
    $publisherResponseRead = $false
    while (-not (Test-Path $markerPath)) {
        if ($pending.IsCompleted -and -not $publisherResponseRead) {
            $response = $pending.GetAwaiter().GetResult(); $publisherResponseRead = $true
            if (-not $response.IsSuccessStatusCode) { throw "Publisher admission failed: HTTP $([int]$response.StatusCode)." }
            $receipt.publisherAdmission = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json -AsHashtable -DateKind String
        }
        if ($probeProcess.HasExited) { throw 'The owned probe host exited before its cut.' }
        if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'The cohort cut was not reached within the fixture bound.' }
        Start-Sleep -Milliseconds 100
    }
    $marker = Get-Content -Raw $markerPath | ConvertFrom-Json -AsHashtable -DateKind String
    $receipt.cut = $marker
    if ($renewedClaim) {
        $renewed = Observe 'renewed-claim-live' @($waitRun.workflowExecutionId)
        Assert-ActualClaimRenewal $initial $initialMarker $renewed $marker ([DateTimeOffset]::Parse($receipt.initialClaimReleasedAtUtc))
        $receipt.renewedClaimLive = $renewed
    }
    if ($committedAck) {
        $liveCommitted = Observe 'committed-source-live' @($waitRun.workflowExecutionId)
        if ($marker.providerEvent -ne 'EF.TransactionCommitted' -or $liveCommitted.source.status -ne 'Delivered' -or
            $liveCommitted.source.deliveryFencingToken -ne $marker.fencingToken -or $null -ne $liveCommitted.source.deliveringOwnerId -or
            -not $liveCommitted.source.deliveredAt) { throw 'The independent live observation did not prove a committed source completion at the diagnostic cut.' }
        $receipt.committedSourceLive = $liveCommitted
    }
    if ($selectsAfterRestart -or $emptyWinner) {
        if ($null -ne $marker.snapshot) { throw 'Selection already occurred before the delayed-delivery cut.' }
        if ($competing) {
            $proposalStarts = if ($emptyProposal) { 0 } else { 1 }; $proposalResumes = if ($emptyProposal) { 0 } else { 2 }
            if ($null -eq $marker.proposal -or $marker.proposal.starts.Count -ne $proposalStarts -or $marker.proposal.resumes.Count -ne $proposalResumes) { throw 'The original owner did not prepare its complete distinct proposal.' }
        }
    } elseif ($Phase -eq 'empty-cohort') {
        if ($null -eq $marker.snapshot -or $marker.snapshot.starts.Count -ne 0 -or $marker.snapshot.resumes.Count -ne 0) { throw 'The empty recipient decision was not durably committed.' }
    } elseif ($marker.snapshot.starts.Count -ne $(if ($partialStart -or $sameArtifactShed) { 2 } else { 1 }) -or $marker.snapshot.resumes.Count -ne 2) { throw 'The actual persisted cohort is incomplete at the cut.' }
    if ($completeFanout) {
        $completed = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $waitRun.workflowExecutionId -TimeoutSeconds 30
        if ($completed.instance.status -ne 'Completed' -or @($completed.incidents).Count -ne 0) { throw 'Full fan-out acceptance did not establish completed recipient callbacks/join.' }
        foreach ($start in $marker.snapshot.starts) {
            $selected = Observe 'fanout-starts' @($waitRun.workflowExecutionId)
            $keyed = @($selected.source.selectedStarts | Where-Object artifactId -eq $start.executable.artifactId)
            if ($keyed.Count -ne 1) { throw 'The full fan-out source did not identify one exact saved keyed target.' }
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
            do {
                $api = Get-WorkflowInstance -Ctx $ctx -ExecutionId $keyed[0].workflowExecutionId
                $park = @($api.activities | Where-Object { $_.executableNodeId -eq 'node-park' -and $_.status -eq 'Suspended' })
                if ($park.Count -eq 1) { break }
                Start-Sleep -Milliseconds 100
            } while ([DateTimeOffset]::UtcNow -lt $deadline)
            if ($park.Count -ne 1 -or @($api.incidents).Count -ne 0) { throw 'A full fan-out saved start did not actually park before source acknowledgment loss.' }
        }
    }
    if ($partialStart) {
        $acceptedSource = Observe 'accepted-start' @($waitRun.workflowExecutionId)
        $acceptedStart = @($acceptedSource.source.selectedStarts | Where-Object workflowExecutionId -eq $marker.acceptedExecution)
        if ($acceptedStart.Count -ne 1) { throw 'The accepted start marker is not one exact saved keyed recipient.' }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        do {
            $acceptedApi = Get-WorkflowInstance -Ctx $ctx -ExecutionId $marker.acceptedExecution
            $parked = @($acceptedApi.activities | Where-Object { $_.executableNodeId -eq 'node-park' -and $_.status -eq 'Suspended' })
            if ($parked.Count -eq 1) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($parked.Count -ne 1 -or $acceptedApi.activityNextContinuationToken -or @($acceptedApi.incidents).Count -ne 0) { throw 'Start acceptance did not establish an actual parked child and complete observation.' }
        $receipt.acceptedStartApiBeforeCrash = $acceptedApi
    }
    if ($Phase -eq 'after-resume') {
        $accepted = Observe 'accepted' @($waitRun.workflowExecutionId)
        $recipient = @($accepted.source.selectedResumes | Where-Object { $_.bookmarkId -eq $marker.acceptedBookmark -and $_.workflowExecutionId -eq $marker.acceptedExecution })
        if ($recipient.Count -ne 1) { throw 'The accepted marker does not identify one saved exact recipient.' }
        $recipient = $recipient[0]
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        do {
            $partialInstance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $recipient.workflowExecutionId
            $completed = @($partialInstance.activities | Where-Object { $_.activityExecutionId -eq $recipient.activityExecutionId -and $_.status -eq 'Completed' })
            $rearmed = @($partialInstance.activities | Where-Object { $_.executableNodeId -eq $recipient.executableNodeId -and $_.status -eq 'Suspended' -and $_.activityExecutionId -ne $recipient.activityExecutionId })
            if ($completed.Count -eq 1 -and $rearmed.Count -eq 1) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($completed.Count -ne 1 -or $rearmed.Count -ne 1 -or @($partialInstance.incidents).Count -ne 0) { throw 'Mailbox acceptance did not establish an actual completed callback and rearmed listener.' }
        $partial = Observe 'partial-before-crash' @($waitRun.workflowExecutionId)
        $partialWorkflow = @($partial.workflows | Where-Object workflowExecutionId -eq $recipient.workflowExecutionId)[0]
        $newWait = @($partialWorkflow.bookmarks | Where-Object executableNodeId -eq $recipient.executableNodeId)
        if ($newWait.Count -ne 1 -or $newWait[0].bookmarkId -eq $recipient.bookmarkId -or $newWait[0].generation -eq $recipient.generation -or
            $newWait[0].activityStatus -ne 'Suspended' -or $partialWorkflow.bookmarks.Count -ne 2) { throw 'The accepted callback did not create one distinct physical wait for the same listener.' }
        $receipt.partialBeforeCrash = $partial; $receipt.rearmedWait = $newWait[0]
        $receipt.acceptedRecipient = $recipient; $receipt.partialApi = $partialInstance
    }
    if ($emptyWinner) {
        $plan = Submit-AlterationPlan -Ctx $ctx -Target @{ executionIds = @($waitRun.workflowExecutionId) } `
            -Alterations @(@{ kind = 'CancelWorkflow'; schemaVersion = 1; payload = @{} }) -IdempotencyKey "cancel-$caseId"
        $terminal = Wait-AlterationPlanTerminal -Ctx $ctx -PlanId $plan.PlanId -TimeoutSeconds 45
        if ($terminal.Json.status -ne 'Completed' -or $terminal.Json.counts.succeeded -ne 1 -or $terminal.Json.counts.targetCount -ne 1) { throw 'The original listener was not canceled through the actual public alteration service.' }
        $cancelled = Get-WorkflowInstance -Ctx $ctx -ExecutionId $waitRun.workflowExecutionId
        if ($cancelled.instance.status -ne 'Cancelled' -or @($cancelled.incidents).Count -ne 0) { throw 'The original listener did not reach its actual canceled checkpoint.' }
        foreach ($recipient in $marker.proposal.resumes) {
            $activity = @($cancelled.activities | Where-Object activityExecutionId -eq $recipient.activityExecutionId)
            if ($activity.Count -ne 1 -or $activity[0].status -ne 'Cancelled') { throw 'A prepared exact original catch was not canceled.' }
        }
        $definitionId = [Uri]::EscapeDataString($marker.proposal.starts[0].binding.definitionId)
        $unpublished = Invoke-RestMethod "$base/publishing/workflows/$definitionId/slots/default" -Method Delete -WebSession $ctx.Session
        $receipt.withdrawal = @{ plan = $terminal.Json; cancelledInstance = $cancelled; publicationSlot = $unpublished }
    }
    if ($republishedCohort) {
        $replacementXml = $startXml.Replace('id="selected-start"', 'id="replacement-start"').Replace('<intermediateCatchEvent id="park"', '<intermediateCatchEvent id="replacement-park"').Replace('targetRef="park"', 'targetRef="replacement-park"').Replace('sourceRef="park"', 'sourceRef="replacement-park"')
        $replacementImport = Invoke-RestMethod "$base/interchange/bpmn/import" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (@{ xml = $replacementXml } | ConvertTo-Json -Depth 30)
        if (-not $replacementImport.analysis.hostInventoryVerified -or -not $replacementImport.analysis.canExecute) { throw 'The actual host cannot execute the replacement definition.' }
        $replacementVersion = Invoke-RestMethod "$base/design/workflows/versions/ingest" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (@{
            operationKey = "republish-$caseId"; definitionId = $selectedPublication.definitionId;
            state = @{ variables = @(); inputs = @(); outputs = @(); strategyOptions = $null; rootActivity = $replacementImport.processNode }
        } | ConvertTo-Json -Depth 40)
        $replacement = Invoke-RestMethod "$base/publishing/workflows/$($replacementVersion.id)/publish" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (@{
            action = 'Replace'; slotName = 'default'; expectedPublicationId = $selectedPublication.publicationId
        } | ConvertTo-Json)
        if ($replacement.definitionId -ne $selectedPublication.definitionId -or $replacement.artifactId -eq $selectedPublication.artifactId -or
            $replacement.publicationId -eq $selectedPublication.publicationId -or $replacement.sourceReferenceId -eq $selectedPublication.sourceReferenceId) { throw 'The public replacement did not create a distinct artifact/publication in the original definition slot.' }
        $controlDefinitionId = [Uri]::EscapeDataString($collectionControl.definitionId)
        $controlSlot = Invoke-RestMethod "$base/publishing/workflows/$controlDefinitionId/slots/default" -Method Delete -WebSession $ctx.Session
        $receipt.republication = @{ original = $selectedPublication; replacement = $replacement; replacementVersionId = $replacementVersion.id; controlUnpublishedSlot = $controlSlot }
    }
    # These recipients are created after publisher commit. They join only if selection has not yet happened.
    $lateWaitXml = $waitXml.Replace('id="waiter"', 'id="late-waiter"')
    $lateStartXml = $startXml.Replace('id="selected-start"', 'id="late-start"')
    if ($emptyWinner) { $lateWaitXml = $lateWaitXml.Replace($eventName, "unmatched-later-$eventName"); $lateStartXml = $lateStartXml.Replace($eventName, "unmatched-later-$eventName") }
    $latePublication = Import-Process $lateWaitXml
    $lateRun = Invoke-Artifact -Ctx $ctx -ArtifactId $latePublication.artifactId -SourceReferenceId $latePublication.sourceReferenceId
    $null = Wait-Catches $lateRun.workflowExecutionId 2
    $lateStart = Import-Process $lateStartXml
    if ($republishedCohort) {
        $beforeCollection = Observe 'republished-before-collection' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        Assert-RepublishedCollectionState $beforeCollection $selectedPublication $replacement $collectionControl $false
        $receipt.beforeCollection = $beforeCollection
        $receipt.collectionReleasedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        New-Item -ItemType File (Join-Path $caseDirectory 'run-collection') | Out-Null
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        do {
            $collected = Observe 'republished-after-collection' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
            $completedSweeps = @(Get-ChildItem $caseDirectory -Filter 'collection-*.json' | ForEach-Object { Get-Content -Raw $_.FullName | ConvertFrom-Json -AsHashtable -DateKind String })
            $usefulSweeps = @($completedSweeps | Where-Object { $_.result.deletedArtifactCount -gt 0 -and $_.result.deletedReferenceCount -gt 0 -and
                $_.supportsStimulusDeliveryRetention -and $_.artifactCreationGracePeriodSeconds -eq 0 -and $_.scope -eq $marker.scope -and
                [DateTimeOffset]::Parse($_.startedAtUtc) -ge [DateTimeOffset]::Parse($receipt.collectionReleasedAtUtc) })
            $controlStillExists = @($collected.observedMaterial | Where-Object { $_.identity.artifactId -eq $collectionControl.artifactId }).Count -ne 0
            if (-not $controlStillExists -and $usefulSweeps.Count -gt 0) { break }
            if ($probeProcess.HasExited) { throw 'The owned probe exited before its actual reference collector ran.' }
            Start-Sleep -Milliseconds 200
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($controlStillExists -or $usefulSweeps.Count -eq 0) { throw 'A real zero-grace selected collector did not delete the unrelated unrooted control.' }
        Assert-RepublishedCollectionState $collected $selectedPublication $replacement $collectionControl $true
        if (-not (Same-Snapshot $collected.source $beforeCollection.source)) { throw 'Collection changed the owed source or its saved cohort before dispatch.' }
        $receipt.afterCollection = $collected; $receipt.actualCollectionSweeps = $usefulSweeps
        $receipt.originalRetiredApiAdmission = Invoke-WebRequest "$base/runtime/workflows/executables/$($selectedPublication.artifactId)/execute" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (@{ sourceReferenceId = $selectedPublication.sourceReferenceId } | ConvertTo-Json) -SkipHttpErrorCheck
        if ([int]$receipt.originalRetiredApiAdmission.StatusCode -ne 409) { throw 'Ordinary public admission unexpectedly authorized the retired original source.' }
        # Store only the bounded public response, never cookies/headers/session objects in evidence.
        $receipt.originalRetiredApiAdmission = @{ statusCode = [int]$receipt.originalRetiredApiAdmission.StatusCode; body = $receipt.originalRetiredApiAdmission.Content }
    }
    if ($completeFanout) {
        $fanout = Observe 'fanout-before-crash' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        $receipt.completedFanoutBeforeCrash = $fanout
        if ($committedAck -and ($marker.providerEvent -ne 'EF.TransactionCommitted' -or $fanout.source.status -ne 'Delivered' -or $null -ne $fanout.source.deliveringOwnerId -or -not $fanout.source.deliveredAt)) { throw 'The independent live observation did not prove the source completion actually committed before the provider returned.' }
        $recipientIds = @(@($fanout.source.selectedResumes | ForEach-Object workflowExecutionId) + @($fanout.source.selectedStarts | ForEach-Object workflowExecutionId) | Sort-Object -Unique)
        if ($recipientIds.Count -ne 2 -or $waitRun.workflowExecutionId -notin $recipientIds) { throw 'The full fan-out observation does not cover both exact recipient workflows.' }
        $receipt.completedRecipientApiBeforeCrash = @()
        foreach ($identity in $recipientIds) {
            $api = Get-WorkflowInstance -Ctx $ctx -ExecutionId $identity
            if ($api.activityNextContinuationToken -or @($api.incidents).Count -ne 0) { throw 'The full fan-out recipient API observation is incomplete or faulted.' }
            $receipt.completedRecipientApiBeforeCrash += @{ workflowExecutionId = $identity; instance = $api.instance; activities = $api.activities }
        }
    }
    if ($competing) {
        if ($probeProcess.HasExited) { throw 'The owned first process exited before suspension.' }
        $pausePath = Join-Path $caseDirectory 'paused.json'
        & $Python "$PSScriptRoot/pause_owned_broadcast_host.py" --pid $probeProcess.Id --expected-start $registeredStart --assembly $ProbeAssembly --content-root $root --database (Join-Path $root 'elsa.db') --database (Join-Path $root 'membership.db') --output $pausePath
        if ($LASTEXITCODE -ne 0) { throw 'The owned process could not be paused outside SQLite writes.' }
        $receipt.paused = Get-Content -Raw $pausePath | ConvertFrom-Json -AsHashtable -DateKind String
        $recoveryRoot = New-ElsaContentRoot -Prefix 'elsa-bpmn-competing-peer'
        $recoveryBase = Resolve-ElsaServerBaseUrl
    } else {
        Stop-OwnedElsaServer
        if (-not $probeProcess.HasExited) { throw 'The owned probe process remains live after termination.' }
    }
    $stopped = Observe 'stopped' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
    $receipt.stopped = $stopped
    if ($competing) {
        $receipt.membershipBeforePeer = Observe-Membership 'membership-before-peer'
        if (@($receipt.membershipBeforePeer.members | Where-Object hostId -eq $oldNodeId).Count -ne 1) { throw 'The old host did not publish its actual unique membership identity.' }
    }
    if ($committedAck) {
        if ($stopped.source.status -ne 'Delivered' -or $stopped.source.deliveryFencingToken -ne $marker.fencingToken -or
            -not (Same-Snapshot $stopped.source $receipt.completedFanoutBeforeCrash.source)) { throw 'The committed source changed between its live observation and the owned crash.' }
    } elseif ($stopped.source.status -ne 'Delivering' -or $stopped.source.deliveryFencingToken -ne $marker.fencingToken -or $stopped.source.deliveringOwnerId -ne $marker.ownerId) { throw 'The stopped source lost its actual claim facts.' }
    if (-not (Same-Snapshot $stopped.source.snapshot $marker.snapshot)) { throw 'The physical source differs from the captured winner.' }
    if ($emptyWinner) {
        $original = @($stopped.workflows | Where-Object workflowExecutionId -eq $waitRun.workflowExecutionId)[0]
        $later = @($stopped.workflows | Where-Object workflowExecutionId -eq $lateRun.workflowExecutionId)[0]
        $identity = $stopped.source.intentPayload
        if ($null -ne $stopped.source.snapshot -or $original.status -ne 'Cancelled' -or $original.bookmarks.Count -ne 0 -or
            $stopped.matchingBindings.Count -ne 0 -or $later.bookmarks.Count -ne 2 -or
            @($later.bookmarks | Where-Object { $_.stimulusType -eq $identity.stimulusType -and $_.stimulusHash -eq $identity.stimulusHash }).Count -ne 0) { throw 'The checked physical state does not establish withdrawal before the empty winning selection.' }
    }
    if ($Phase -eq 'empty-cohort') {
        $identity = $stopped.source.intentPayload
        $prior = @($stopped.workflows | Where-Object workflowExecutionId -eq $waitRun.workflowExecutionId)[0]
        $later = @($stopped.workflows | Where-Object workflowExecutionId -eq $lateRun.workflowExecutionId)[0]
        if ($prior.bookmarks.Count -ne 2 -or @($prior.bookmarks | Where-Object { $_.stimulusType -eq $identity.stimulusType -and $_.stimulusHash -eq $identity.stimulusHash }).Count -ne 0) { throw 'The initial waits do not establish an unmatched selection.' }
        if ($later.bookmarks.Count -ne 2 -or @($later.bookmarks | Where-Object { $_.stimulusType -ne $identity.stimulusType -or $_.stimulusHash -ne $identity.stimulusHash -or [DateTimeOffset]::Parse($_.createdAt) -le [DateTimeOffset]::Parse($marker.capturedAtUtc) }).Count -ne 0) { throw 'The later waits are not real matching recipients created after the empty decision.' }
        $binding = @($stopped.matchingBindings | Where-Object artifactId -eq $lateStart.artifactId)
        if ($binding.Count -ne 1 -or -not $binding[0].isActive -or [DateTimeOffset]::Parse($binding[0].createdAt) -le [DateTimeOffset]::Parse($marker.capturedAtUtc)) { throw 'The later binding is not actually active/matching after the empty decision.' }
    }
    if ($selectsAfterRestart) {
        if ($null -ne $stopped.source.snapshot) { throw 'The stopped source already selected recipients.' }
        foreach ($executionId in @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)) {
            $wf = @($stopped.workflows | Where-Object workflowExecutionId -eq $executionId)[0]
            if ($wf.bookmarks.Count -ne 2 -or @($wf.bookmarks | Where-Object activityStatus -ne 'Suspended').Count -ne 0) { throw 'A waiting recipient changed before first selection.' }
        }
        $later = @($stopped.workflows | Where-Object workflowExecutionId -eq $lateRun.workflowExecutionId)[0]
        if (@($later.bookmarks | Where-Object { [DateTimeOffset]::Parse($_.createdAt) -le [DateTimeOffset]::Parse($stopped.source.recordedAt) }).Count -ne 0) { throw 'The delayed listeners were not created after the source commit.' }
        foreach ($artifactId in @($selectedPublication.artifactId, $lateStart.artifactId)) {
            if (@($stopped.allWorkflows | Where-Object artifactId -eq $artifactId).Count -ne 0) { throw 'A signal start ran before initial recipient selection.' }
        }
    }
    foreach ($recipient in $stopped.source.selectedResumes) {
        $wf = @($stopped.workflows | Where-Object workflowExecutionId -eq $recipient.workflowExecutionId)[0]
        $actual = @($wf.bookmarks | Where-Object bookmarkId -eq $recipient.bookmarkId)
        $activity = @($wf.selectedActivities | Where-Object activityExecutionId -eq $recipient.activityExecutionId)
        if ($completeFanout) {
            if ($actual.Count -ne 0 -or $activity.Count -ne 1 -or $activity[0].status -ne 'Completed' -or $wf.status -ne 'Completed') { throw 'A supposedly completed full fan-out recipient still owes its callback before the crash.' }
        } elseif ($Phase -eq 'after-resume' -and $recipient.bookmarkId -eq $marker.acceptedBookmark) {
            if ($actual.Count -ne 0 -or $activity.Count -ne 1 -or $activity[0].status -ne 'Completed') { throw 'The accepted callback was not durably completed before crash.' }
            $rearmed = @($wf.bookmarks | Where-Object bookmarkId -eq $receipt.rearmedWait.bookmarkId)
            if ($rearmed.Count -ne 1 -or -not (Same-Snapshot $rearmed[0] $receipt.rearmedWait)) { throw 'The completed callback rearm changed before crash.' }
        } elseif ($actual.Count -ne 1 -or $actual[0].generation -ne $recipient.generation -or $activity.Count -ne 1 -or $activity[0].status -ne 'Suspended') { throw 'An undispatched selected wait changed before crash.' }
    }
    foreach ($start in $stopped.source.selectedStarts) {
        $target = @($stopped.workflows | Where-Object workflowExecutionId -eq $start.workflowExecutionId)
        if ($target.Count -ne 1) { throw 'The exact selected keyed target observation is unavailable.' }
        if ($Phase -in @('after-cohort', 'renewed-claim', 'republished-cohort', 'same-artifact-shed') -or ($partialStart -and $start.workflowExecutionId -ne $marker.acceptedExecution)) {
            if ($target[0].exists -or @($stopped.allWorkflows | Where-Object artifactId -eq $start.artifactId).Count -ne 0) { throw 'A selected start ran before its dispatch cut.' }
        } else {
            $park = @($target[0].bookmarks | Where-Object { $_.executableNodeId -eq 'node-park' -and $_.activityStatus -eq 'Suspended' })
            if (-not $target[0].exists -or $park.Count -ne 1 -or $target[0].bookmarks.Count -ne 1) { throw 'Partial delivery did not durably start and park the original keyed target.' }
            $receipt.startedParkBeforeCrash = $park[0]
            if ($partialStart) { $receipt.acceptedStartBeforeCrash = $target[0] }
        }
    }
    if ($sameArtifactShed) {
        $receipt.initialCut = $marker; $receipt.initialStopped = $stopped; $receipt.initialProcessId = $probeProcess.Id
        $entries = @($stopped.source.selectedStarts)
        Assert-SameArtifactCohort $stopped $receipt.sameArtifactPublications
        $shedCase = 'same-shed-' + [guid]::NewGuid().ToString('N'); $shedDirectory = Join-Path $control $shedCase
        @{ caseId = $shedCase; phase = 'after-shed'; eventName = $eventName; loadArtifactId = $loadPublication.artifactId } | ConvertTo-Json | Set-Content (Join-Path $control 'plan.json')
        $settings.CShells.Shells.default.Features.BpmnRecoveryProbe.AdmissionStaticLimit = 1
        $settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
        Start-OwnedElsaServer -BaseUrl $base -ContentRoot $root -AssemblyPath $ProbeAssembly
        $probeProcess = $script:OwnedElsaServer.Process; $receipt.capacityProbeProcessId = $probeProcess.Id
        $ctx = Connect-Elsa -BaseUrl $base
        $client.Dispose()
        $handler = [System.Net.Http.HttpClientHandler]::new(); $handler.CookieContainer = $ctx.Session.Cookies
        $client = [System.Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromMinutes(5)
        $loadContent = [System.Net.Http.StringContent]::new((@{ sourceReferenceId = $loadPublication.sourceReferenceId } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json')
        $loadPending = $client.PostAsync("$base/runtime/workflows/executables/$($loadPublication.artifactId)/execute", $loadContent)
        $loadMarkerPath = Join-Path $shedDirectory 'load-held.json'; $deadline = [DateTimeOffset]::UtcNow.AddSeconds(40)
        while (-not (Test-Path $loadMarkerPath)) {
            if ($probeProcess.HasExited -or $loadPending.IsCompleted -or [DateTimeOffset]::UtcNow -ge $deadline) { throw 'The actual separately admitted workflow command did not hold its real admission charge.' }
            Start-Sleep -Milliseconds 100
        }
        $loadMarker = Get-Content -Raw $loadMarkerPath | ConvertFrom-Json -AsHashtable -DateKind String
        $receipt.actualLoadMarker = $loadMarker; $receipt.loadWorkflowId = $loadMarker.workflowExecutionId
        $loadApi = Get-WorkflowInstance -Ctx $ctx -ExecutionId $loadMarker.workflowExecutionId
        if ($loadMarker.artifactId -ne $loadPublication.artifactId -or -not $loadMarker.hasAmbientCharge -or $loadMarker.inFlightDispatches -lt 1 -or
            $loadMarker.limit -ne 1 -or @($loadApi.activities | Where-Object { $_.executableNodeId -like 'node-wait*' -and $_.status -eq 'Suspended' }).Count -ne 2 -or @($loadApi.incidents).Count -ne 0) { throw 'The real admitted load command did not durably park both waits while holding capacity.' }
        $receipt.actualLoadApiWhileHeld = $loadApi
        $shedMarkerPath = Join-Path $shedDirectory 'reached.json'; $deadline = [DateTimeOffset]::UtcNow.AddSeconds(180)
        while (-not (Test-Path $shedMarkerPath)) {
            if ($probeProcess.HasExited -or $loadPending.IsCompleted -or [DateTimeOffset]::UtcNow -ge $deadline) { throw 'Recovered source delivery did not encounter the real held admission capacity.' }
            Start-Sleep -Milliseconds 100
        }
        $caseDirectory = $shedDirectory; $markerPath = $shedMarkerPath
        $shedMarker = Get-Content -Raw $markerPath | ConvertFrom-Json -AsHashtable -DateKind String
        $shed = Observe 'actual-shed' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        $attempt = $shedMarker.observedStart
        Assert-ActualSameArtifactShed $stopped $shed $shedMarker $loadMarker
        if ($loadPending.IsCompleted) { throw 'The actual load command stopped holding capacity before the shed observation.' }
        $receipt.actualShedMarker = $shedMarker; $receipt.actualShed = $shed
        $acceptedCase = 'same-accepted-' + [guid]::NewGuid().ToString('N')
        @{ caseId = $acceptedCase; phase = 'after-start'; eventName = $eventName } | ConvertTo-Json | Set-Content (Join-Path $control 'plan.json')
        $receipt.actualLoadReleasedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        New-Item -ItemType File (Join-Path $shedDirectory 'release-load') | Out-Null
        $loadResponse = $loadPending.GetAwaiter().GetResult()
        if (-not $loadResponse.IsSuccessStatusCode) { throw 'The real capacity-load workflow command did not complete admission normally after release.' }
        $receipt.actualLoadAdmission = $loadResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json -AsHashtable -DateKind String
        if ($receipt.actualLoadAdmission.workflowExecutionId -ne $loadMarker.workflowExecutionId) { throw 'The released load response belongs to another actual workflow.' }
        $receipt.actualLoadRespondedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        New-Item -ItemType File (Join-Path $shedDirectory 'release') | Out-Null
        $caseDirectory = Join-Path $control $acceptedCase; $markerPath = Join-Path $caseDirectory 'reached.json'
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(40)
        while (-not (Test-Path $markerPath)) {
            if ($probeProcess.HasExited -or [DateTimeOffset]::UtcNow -ge $deadline) { throw 'The later binding did not reuse the shed artifact key after actual load release.' }
            Start-Sleep -Milliseconds 100
        }
        $marker = Get-Content -Raw $markerPath | ConvertFrom-Json -AsHashtable -DateKind String
        $accepted = Observe 'same-key-accepted' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        $next = $marker.observedStart
        Assert-ActualSameArtifactAccepted $receipt.initialStopped $shed $shedMarker $accepted $marker ([DateTimeOffset]::Parse($receipt.actualLoadReleasedAtUtc)) ([DateTimeOffset]::Parse($receipt.actualLoadRespondedAtUtc))
        $keyed = @($accepted.workflows | Where-Object workflowExecutionId -eq $entries[0].workflowExecutionId)[0]
        $acceptedApi = Get-WorkflowInstance -Ctx $ctx -ExecutionId $keyed.workflowExecutionId
        $chosen = @($accepted.source.snapshot.starts | Where-Object { $_.binding.triggerBindingId -eq $next.triggerBindingId })[0]
        if (-not $keyed.exists -or $keyed.bookmarks.Count -ne 1 -or $keyed.bookmarks[0].executableNodeId -ne 'node-park' -or
            $chosen.sourceCapture.version -ne 1 -or -not (Same-Snapshot $keyed.pinnedSource $chosen.sourceCapture.provenance) -or $acceptedApi.activityNextContinuationToken -or @($acceptedApi.incidents).Count -ne 0) { throw 'The accepted same-key execution lacks its real parked state or exact accepted binding provenance.' }
        $receipt.sameKeyAccepted = $accepted; $receipt.sameKeyAcceptedApi = $acceptedApi; $receipt.sameKeyAcceptedState = $keyed; $receipt.sameKeyAcceptedMarker = $marker
        Stop-OwnedElsaServer
        if (-not $probeProcess.HasExited) { throw 'The owned capacity probe did not terminate.' }
        $stopped = Observe 'same-key-stopped' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        if (-not (Same-Snapshot $stopped.source $accepted.source)) { throw 'The source changed before the second owned crash.' }
        $receipt.cut = $marker; $receipt.stopped = $stopped
    }
    $null = $settings.CShells.Shells.default.Features.Remove('BpmnRecoveryProbe')
    $settings | ConvertTo-Json -Depth 60 | Set-Content (Join-Path $recoveryRoot 'shells.json')
    # Never rewrite the paused host's configuration; its original shell must attempt its prepared proposal.
    # Begin the observer before startup: normal background delivery may reclaim and complete before readiness returns.
    if (-not $committedAck) {
        $watchOutput = Join-Path $caseDirectory 'successor.json'
        $info = [Diagnostics.ProcessStartInfo]::new(); $info.FileName = $Python; $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        foreach ($arg in @("$PSScriptRoot/inspect_broadcast_recovery.py", '--database', (Join-Path $root 'elsa.db'), '--reader', $ReaderAssembly, '--marker', $markerPath, '--output', $watchOutput, '--watch-claim', '--previous-fence', [string]$stopped.source.deliveryFencingToken, '--timeout', '180')) { $info.ArgumentList.Add($arg) }
        $watch = [Diagnostics.Process]::Start($info); $watchStdout = $watch.StandardOutput.ReadToEndAsync(); $watchStderr = $watch.StandardError.ReadToEndAsync()
    }
    $peerEnvironment = @{} + $sharedEnvironment
    if ($competing) { $peerEnvironment.Elsa__Cluster__Membership__HostId = $peerNodeId }
    Start-OwnedElsaServer -BaseUrl $recoveryBase -ContentRoot $recoveryRoot -AssemblyPath $NormalAssembly -Environment $peerEnvironment
    $receipt.normalProcessId = $script:OwnedElsaServer.Process.Id; $receipt.crashedOrPausedProcessId = $probeProcess.Id
    $ctx = Connect-Elsa -BaseUrl $recoveryBase
    if ($committedAck) {
        $originalDeadline = [DateTimeOffset]::Parse($marker.visibleAfter).AddSeconds(2)
        if ($originalDeadline -gt [DateTimeOffset]::UtcNow.AddSeconds(180)) { throw 'The actual original claim deadline exceeds this fixture bound.' }
        while ([DateTimeOffset]::UtcNow -le $originalDeadline) {
            if ($script:OwnedElsaServer.Process.HasExited) { throw 'The actual standard host exited before the committed-source recovery observation.' }
            Start-Sleep -Milliseconds 200
        }
        $controlEvent = 'post-restart-control-' + [guid]::NewGuid().ToString('N')
        $controlPublication = Import-Process ($publisherXml.Replace($eventName, $controlEvent).Replace('id="publisher"', 'id="control-publisher"'))
        $controlRun = Invoke-Artifact -Ctx $ctx -ArtifactId $controlPublication.artifactId -SourceReferenceId $controlPublication.sourceReferenceId
        $controlApi = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $controlRun.workflowExecutionId -TimeoutSeconds 30
        if ($controlApi.instance.status -ne 'Completed' -or @($controlApi.incidents).Count -ne 0) { throw 'The real post-deadline standard-host publisher control did not complete.' }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        do {
            $controlSource = Observe-Publisher 'post-restart-control' $controlRun.workflowExecutionId
            if ($controlSource.source.status -eq 'Delivered') { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($controlSource.source.status -ne 'Delivered' -or $controlSource.source.intentPayload.eventName -ne $controlEvent -or
            $null -eq $controlSource.source.snapshot -or $controlSource.source.selectedStarts.Count -ne 0 -or $controlSource.source.selectedResumes.Count -ne 0) { throw 'The standard host did not actually process its distinct post-deadline source.' }
        $receipt.postRestartControl = @{ observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); eventName = $controlEvent; api = $controlApi; source = $controlSource }
        $committed = Observe 'committed-after-original-deadline' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        if (-not (Same-Snapshot $committed.source $stopped.source)) { throw 'The completed source was reclaimed or changed after its original deadline.' }
        $receipt.committedRecoveryObservation = $committed
    } else {
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(180)
        while (-not $watch.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
        if (-not $watch.HasExited) { throw 'Successor observation remains live beyond the fixture bound.' }
        if ($watch.ExitCode -ne 0) { throw "Successor observation failed: $($watchStdout.GetAwaiter().GetResult()) $($watchStderr.GetAwaiter().GetResult())" }
        $successor = Get-Content -Raw $watchOutput | ConvertFrom-Json -AsHashtable -DateKind String
        $receipt.successorClaim = $successor
        if ($successor.source.deliveryFencingToken -le $stopped.source.deliveryFencingToken -or $successor.source.deliveringOwnerId -eq $stopped.source.deliveringOwnerId) { throw 'No distinct successor source claim was proven.' }
    }
    if ($unmatchedInitial -or $emptyWinner) {
        $finished = Get-WorkflowInstance -Ctx $ctx -ExecutionId $waitRun.workflowExecutionId
        if (@($finished.incidents).Count -ne 0) { throw 'An unmatched listener acquired an incident during empty-source recovery.' }
    } elseif ($Phase -ne 'after-resume') {
        $finished = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $waitRun.workflowExecutionId -TimeoutSeconds 60
        if ($finished.instance.status -ne 'Completed' -or @($finished.incidents).Count -ne 0) { throw 'Normal-host recovery did not complete the original catches/join cleanly.' }
    } else {
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        $selectedActivityIds = @($stopped.source.selectedResumes | ForEach-Object activityExecutionId)
        do {
            $finished = Get-WorkflowInstance -Ctx $ctx -ExecutionId $waitRun.workflowExecutionId
            $completed = @($finished.activities | Where-Object { $_.activityExecutionId -in $selectedActivityIds -and $_.status -eq 'Completed' })
            $armed = @($finished.activities | Where-Object { $_.executableNodeId -like 'node-wait*' -and $_.status -eq 'Suspended' })
            if ($completed.Count -eq 2 -and $armed.Count -eq 2) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($completed.Count -ne 2 -or $armed.Count -ne 2 -or @($finished.incidents).Count -ne 0) { throw 'Recovery did not complete only both saved callbacks and preserve rearmed listeners.' }
    }
    if ($emptyWinner) {
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        do {
            $selectedSource = Observe 'empty-winner' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
            if ($selectedSource.source.status -eq 'Delivered') { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($null -eq $selectedSource.source.snapshot -or $selectedSource.source.selectedStarts.Count -ne 0 -or $selectedSource.source.selectedResumes.Count -ne 0) { throw 'The successor did not install a nonnull empty winner.' }
        $receipt.firstSelected = $selectedSource
    } elseif ($selectsAfterRestart) {
        $lateCompleted = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $lateRun.workflowExecutionId -TimeoutSeconds 60
        if ($lateCompleted.instance.status -ne 'Completed' -or @($lateCompleted.incidents).Count -ne 0) { throw 'First delivery did not include the listener armed after publisher commit.' }
        $selectedSource = Observe 'selected-after-recovery' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        $expectedStarts = if ($emptyProposal) { 1 } else { 2 }; $expectedResumes = if ($emptyProposal) { 2 } else { 4 }
        if ($selectedSource.source.selectedStarts.Count -ne $expectedStarts -or $selectedSource.source.selectedResumes.Count -ne $expectedResumes) { throw 'Initial selection did not capture the complete delivery-time cohort.' }
        foreach ($before in $stopped.workflows | Where-Object { $_.workflowExecutionId -eq $lateRun.workflowExecutionId -or (-not $emptyProposal -and $_.workflowExecutionId -eq $waitRun.workflowExecutionId) }) {
            foreach ($wait in $before.bookmarks) {
                $captured = @($selectedSource.source.selectedResumes | Where-Object { $_.workflowExecutionId -eq $before.workflowExecutionId -and $_.bookmarkId -eq $wait.bookmarkId -and $_.generation -eq $wait.generation })
                if ($captured.Count -ne 1) { throw 'First delivery omitted or substituted a wait armed before selection.' }
            }
        }
        $lateBinding = @($selectedSource.source.selectedStarts | Where-Object artifactId -eq $lateStart.artifactId)
        if ($lateBinding.Count -ne 1 -or [DateTimeOffset]::Parse($lateBinding[0].bindingCreatedAt) -le [DateTimeOffset]::Parse($stopped.source.recordedAt)) { throw 'The selected later start binding was not created after publisher commit.' }
        $receipt.firstSelected = $selectedSource
    } else { $selectedSource = $stopped }
    $startedRuns = @()
    foreach ($selectedStart in $selectedSource.source.selectedStarts) {
        $keyedId = $selectedStart.workflowExecutionId
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        do {
            $started = Get-WorkflowInstance -Ctx $ctx -ExecutionId $keyedId
            $parked = @($started.activities | Where-Object { $_.executableNodeId -eq 'node-park' -and $_.status -eq 'Suspended' })
            if ($parked.Count -eq 1) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($parked.Count -ne 1 -or @($started.incidents).Count -ne 0) { throw 'An exact saved keyed start did not reach its parked child.' }
        $startedRuns += $started
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        $after = Observe 'recovered' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        if ($after.source.status -eq 'Delivered') { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    $receipt.recovered = $after
    if ($committedAck -and -not (Same-Snapshot $after.source $stopped.source)) { throw 'The committed source did not retain its entire original delivery record.' }
    if ($after.source.status -ne 'Delivered' -or -not (Same-Snapshot $after.source.snapshot $selectedSource.source.snapshot)) { throw 'Recovery did not settle its complete immutable source cohort.' }
    $expectedArtifacts = @()
    if (-not $unmatchedInitial -and -not $emptyWinner) { $expectedArtifacts += $selectedPublication.artifactId }
    if ($selectsAfterRestart) { $expectedArtifacts += $lateStart.artifactId }
    if ($partialStart) { $expectedArtifacts += $secondInitialPublication.artifactId }
    if ($completeFanout) {
        $receipt.completedRecipientApiAfterRecovery = @()
        foreach ($beforeApi in $receipt.completedRecipientApiBeforeCrash) {
            $identity = $beforeApi.workflowExecutionId
            $beforeState = @($stopped.workflows | Where-Object workflowExecutionId -eq $identity)[0]
            $afterState = @($after.workflows | Where-Object workflowExecutionId -eq $identity)[0]
            $api = Get-WorkflowInstance -Ctx $ctx -ExecutionId $identity
            if (-not (Same-Snapshot $beforeState $afterState) -or $api.activityNextContinuationToken -or @($api.incidents).Count -ne 0 -or
                -not (Same-Snapshot $beforeApi.instance $api.instance) -or -not (Same-Snapshot $beforeApi.activities $api.activities)) { throw 'Source acknowledgment recovery changed or reinvoked an already completed fan-out recipient.' }
            $receipt.completedRecipientApiAfterRecovery += @{ workflowExecutionId = $identity; instance = $api.instance; activities = $api.activities }
        }
    }
    $expectedStartEntries = if ($sameArtifactShed) { 2 } else { $expectedArtifacts.Count }
    if ($after.source.selectedStarts.Count -ne $expectedStartEntries -or @($after.source.selectedStarts | ForEach-Object artifactId | Sort-Object -Unique).Count -ne $expectedArtifacts.Count) { throw 'The final source has an unexpected binding-entry or unique-artifact start set.' }
    foreach ($selectedStart in $after.source.selectedStarts) {
        $selected = @($after.allWorkflows | Where-Object artifactId -eq $selectedStart.artifactId)
        if ($selectedStart.artifactId -notin $expectedArtifacts -or $selected.Count -ne 1 -or $selected[0].workflowExecutionId -ne $selectedStart.workflowExecutionId) { throw 'A saved start did not produce exactly its original deterministic keyed execution.' }
    }
    if ($Phase -eq 'empty-cohort' -or $emptyWinner) {
        if ($after.source.selectedResumes.Count -ne 0 -or $null -eq $after.source.snapshot) { throw 'Recovery changed an actual empty decision into new recipients.' }
        foreach ($before in $stopped.workflows | Where-Object { $_.workflowExecutionId -in @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId) }) {
            $current = @($after.workflows | Where-Object workflowExecutionId -eq $before.workflowExecutionId)
            if ($current.Count -ne 1 -or -not (Same-Snapshot $current[0] $before)) { throw 'Empty-source recovery changed an unmatched or later matching listener.' }
        }
        foreach ($artifactId in @($selectedPublication.artifactId, $lateStart.artifactId)) {
            if (@($after.allWorkflows | Where-Object artifactId -eq $artifactId).Count -ne 0) { throw 'An empty saved source started a workflow after new matches appeared.' }
        }
    }
    if ($emptyProposal) {
        $priorBefore = @($stopped.workflows | Where-Object workflowExecutionId -eq $waitRun.workflowExecutionId)[0]
        $priorAfter = @($after.workflows | Where-Object workflowExecutionId -eq $waitRun.workflowExecutionId)[0]
        $identity = $after.source.intentPayload
        if (-not (Same-Snapshot $priorBefore $priorAfter) -or $priorBefore.bookmarks.Count -ne 2 -or
            @($priorBefore.bookmarks | Where-Object { $_.stimulusType -eq $identity.stimulusType -and $_.stimulusHash -eq $identity.stimulusHash }).Count -ne 0 -or
            @($after.allWorkflows | Where-Object artifactId -eq $selectedPublication.artifactId).Count -ne 0) { throw 'The empty prepared proposal changed the unmatched original listener or started its artifact.' }
    }
    foreach ($recipient in $after.source.selectedResumes) {
        $wf = @($after.workflows | Where-Object workflowExecutionId -eq $recipient.workflowExecutionId)[0]
        $activity = @($wf.selectedActivities | Where-Object activityExecutionId -eq $recipient.activityExecutionId)
        if ($activity.Count -ne 1 -or $activity[0].status -ne 'Completed' -or @($wf.bookmarks | Where-Object bookmarkId -eq $recipient.bookmarkId).Count -ne 0) { throw 'A saved catch did not durably complete and retire its selected registration.' }
    }
    if ($Phase -eq 'after-resume') {
        $wf = @($after.workflows | Where-Object workflowExecutionId -eq $waitRun.workflowExecutionId)[0]
        $rearmed = @($wf.bookmarks | Where-Object bookmarkId -eq $receipt.rearmedWait.bookmarkId)
        if ($rearmed.Count -ne 1 -or -not (Same-Snapshot $rearmed[0] $receipt.rearmedWait) -or $wf.bookmarks.Count -ne 2) { throw 'Retry consumed or changed the listener rearmed by its first callback.' }
        $beforeWorkflow = @($stopped.workflows | Where-Object workflowExecutionId -eq $waitRun.workflowExecutionId)[0]
        foreach ($recipient in $after.source.selectedResumes) {
            $beforeActivity = @($beforeWorkflow.selectedActivities | Where-Object activityExecutionId -eq $recipient.activityExecutionId)[0]
            $afterActivity = @($wf.selectedActivities | Where-Object activityExecutionId -eq $recipient.activityExecutionId)[0]
            if ($recipient.bookmarkId -eq $receipt.acceptedRecipient.bookmarkId) {
                if (-not (Same-Snapshot $beforeActivity $afterActivity)) { throw 'Retry changed the already completed original callback or its attempt count.' }
            } elseif ($afterActivity.attemptCount -ne ($beforeActivity.attemptCount + 1)) { throw 'The remaining saved callback did not execute exactly one resume attempt.' }
            $current = @($wf.bookmarks | Where-Object executableNodeId -eq $recipient.executableNodeId)
            if ($current.Count -ne 1 -or $current[0].bookmarkId -eq $recipient.bookmarkId -or $current[0].generation -eq $recipient.generation -or $current[0].activityStatus -ne 'Suspended') { throw 'A saved recipient did not leave exactly one later physical wait.' }
        }
    }
    $late = @($after.workflows | Where-Object workflowExecutionId -eq $lateRun.workflowExecutionId)[0]
    $lateInstance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $lateRun.workflowExecutionId
    if (@($lateInstance.incidents).Count -ne 0) { throw 'The later listener acquired an incident during recovery.' }
    if (-not $selectsAfterRestart) {
        if (@($after.allWorkflows | Where-Object artifactId -eq $lateStart.artifactId).Count -ne 0) { throw 'A later binding joined an already saved source.' }
        $lateBefore = @($stopped.workflows | Where-Object workflowExecutionId -eq $lateRun.workflowExecutionId)[0]
        if ($late.bookmarks.Count -ne 2 -or $late.status -ne $lateBefore.status -or
            -not (Same-Snapshot $late.bookmarks $lateBefore.bookmarks)) { throw 'The old source changed a later exact wait or activity/generation.' }
    } elseif ($late.status -ne 'Completed' -or $late.bookmarks.Count -ne 0) { throw 'The delayed delivery-time listener did not complete and retire its waits.' }
    foreach ($selectedStart in $after.source.selectedStarts) {
        $keyedState = @($after.workflows | Where-Object workflowExecutionId -eq $selectedStart.workflowExecutionId)[0]
        $parkedWait = @($keyedState.bookmarks | Where-Object { $_.executableNodeId -eq 'node-park' -and $_.activityStatus -eq 'Suspended' })
        if ($parkedWait.Count -ne 1 -or $parkedWait[0].generation -le 0 -or $keyedState.bookmarks.Count -ne 1) { throw 'A saved keyed start lacks its exact physical park registration.' }
        if ($Phase -eq 'after-resume' -and -not (Same-Snapshot $parkedWait[0] $receipt.startedParkBeforeCrash)) { throw 'Source retry changed the start accepted before the crash.' }
        if ($partialStart -and $selectedStart.workflowExecutionId -eq $marker.acceptedExecution) {
            $afterApi = Get-WorkflowInstance -Ctx $ctx -ExecutionId $selectedStart.workflowExecutionId
            if (-not (Same-Snapshot $keyedState $receipt.acceptedStartBeforeCrash) -or $afterApi.activityNextContinuationToken -or
                -not (Same-Snapshot $afterApi.activities $receipt.acceptedStartApiBeforeCrash.activities) -or
                -not (Same-Snapshot $afterApi.instance $receipt.acceptedStartApiBeforeCrash.instance) -or @($afterApi.incidents).Count -ne 0) { throw 'Retry changed or reinvoked the already parked exact keyed start.' }
            $receipt.acceptedStartApiAfterRecovery = $afterApi
        }
        if ($selectsAfterRestart) {
            $oldWait = $stopped.workflows | ForEach-Object bookmarks | Where-Object bookmarkId -eq $parkedWait[0].bookmarkId
            $captured = @($after.source.selectedResumes | Where-Object { $_.workflowExecutionId -eq $keyedState.workflowExecutionId -and $_.bookmarkId -eq $parkedWait[0].bookmarkId })
            $original = @($after.source.selectedResumes)[0]
            $originalWorkflow = @($stopped.workflows | Where-Object workflowExecutionId -eq $original.workflowExecutionId)[0]
            $originalWait = @($originalWorkflow.bookmarks | Where-Object bookmarkId -eq $original.bookmarkId)[0]
            if ($null -ne $oldWait -or $captured.Count -ne 0 -or $parkedWait[0].stimulusType -ne $originalWait.stimulusType -or $parkedWait[0].stimulusHash -ne $originalWait.stimulusHash) { throw 'The matching wait created by a selected start was not excluded from its own source.' }
        }
    }
    if ($sameArtifactShed) {
        $state = @($after.workflows | Where-Object workflowExecutionId -eq $receipt.sameKeyAcceptedState.workflowExecutionId)[0]
        $api = Get-WorkflowInstance -Ctx $ctx -ExecutionId $state.workflowExecutionId
        if ($after.source.deliveryFencingToken -ne 3 -or -not (Same-Snapshot $state $receipt.sameKeyAcceptedState) -or
            -not (Same-Snapshot $api.instance $receipt.sameKeyAcceptedApi.instance) -or -not (Same-Snapshot $api.activities $receipt.sameKeyAcceptedApi.activities) -or
            @($api.incidents).Count -ne 0 -or $api.activityNextContinuationToken -or @($after.allWorkflows | Where-Object artifactId -eq $selectedPublication.artifactId).Count -ne 1) { throw 'Standard recovery changed or reinvoked the sole already accepted same-artifact execution.' }
        $receipt.sameKeyRecoveredApi = $api; $receipt.sameKeyRecoveredState = $state
    }
    if ($republishedCohort) {
        $originalStart = @($after.source.selectedStarts | Where-Object artifactId -eq $selectedPublication.artifactId)
        if ($originalStart.Count -ne 1 -or @($after.allWorkflows | Where-Object artifactId -eq $replacement.artifactId).Count -ne 0 -or
            @($after.allWorkflows | Where-Object artifactId -eq $collectionControl.artifactId).Count -ne 0) { throw 'Recovery selected the replacement or collection control instead of the saved original.' }
        $originalState = @($after.workflows | Where-Object workflowExecutionId -eq $originalStart[0].workflowExecutionId)[0]
        if ($originalState.pinnedExecutable.artifactId -ne $selectedPublication.artifactId -or $originalState.pinnedExecutable.artifactHash -ne $selectedPublication.artifactHash -or
            $originalState.pinnedSource.sourceReferenceId -ne $selectedPublication.sourceReferenceId -or $originalState.pinnedSource.publicationId -ne $selectedPublication.publicationId -or
            $originalState.pinnedSource.definitionVersionId -ne $selectedPublication.definitionVersionId) { throw 'The recovered saved start did not retain its exact original artifact and publication provenance.' }
        $receipt.recoveredOriginalPublication = $originalState
    }
    $publisherFinished = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $marker.workflowExecutionId -TimeoutSeconds 60
    if ($publisherFinished.instance.status -ne 'Completed' -or @($publisherFinished.incidents).Count -ne 0) { throw 'The original source publisher did not complete cleanly.' }
    $receipt.observations += @{ originalWaiter = $finished; selectedStarts = $startedRuns; laterListener = $lateInstance; publisher = $publisherFinished }
    if ($competing) {
        if ($probeProcess.HasExited) { throw 'The old owned process exited before its stale proposal could be tested.' }
        if (Same-Snapshot $marker.proposal $after.source.snapshot) { throw 'The successor did not choose a genuinely different full recipient set.' }
        $receipt.winnerBeforeOldResume = $after
        $receipt.membershipBeforeOldResume = Observe-Membership 'membership-before-old-resume'
        $oldMember = @($receipt.membershipBeforeOldResume.members | Where-Object hostId -eq $oldNodeId)
        $peerMember = @($receipt.membershipBeforeOldResume.members | Where-Object hostId -eq $peerNodeId)
        if ($oldMember.Count -ne 1 -or $peerMember.Count -ne 1 -or -not $peerMember[0].isLive -or $oldMember[0].isLive -or $oldMember[0].incarnation -eq $peerMember[0].incarnation) { throw 'Distinct actual shared membership identities and natural old-member lapse were not proven.' }
        New-Item -ItemType File (Join-Path $caseDirectory 'release') | Out-Null
        & $Python "$PSScriptRoot/pause_owned_broadcast_host.py" --resume --pid $probeProcess.Id --expected-start $registeredStart --assembly $ProbeAssembly --content-root $root --output (Join-Path $caseDirectory 'resumed.json')
        if ($LASTEXITCODE -ne 0) { throw 'The exact old owned process could not resume.' }
        $rejectedPath = Join-Path $caseDirectory 'rejected.json'
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        while (-not (Test-Path $rejectedPath) -and [DateTimeOffset]::UtcNow -lt $deadline) {
            if ($probeProcess.HasExited) { throw 'The old host exited without reporting its real proposal refusal.' }
            Start-Sleep -Milliseconds 100
        }
        if (-not (Test-Path $rejectedPath)) { throw 'No actual stale proposal refusal was observed.' }
        $rejected = Get-Content -Raw $rejectedPath | ConvertFrom-Json -AsHashtable -DateKind String
        $staleTypes = @('RuntimePostCommitOutboxStaleClaimException')
        if ($rejected.sourceOutboxItemId -ne $marker.sourceOutboxItemId -or $rejected.ownerId -ne $marker.ownerId -or
            $rejected.fencingToken -ne $marker.fencingToken -or -not $rejected.attemptedProviderInstall -or $rejected.cancellationRequested -or
            ($rejected.failureType -notin $staleTypes -and $rejected.innerFailureType -notin $staleTypes)) { throw 'The refusal does not prove the exact old source/claim lost authority.' }
        $receipt.oldProposalRefusal = $rejected
        $afterOld = Observe 'after-stale-owner' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        if (-not (Same-Snapshot $afterOld.source $after.source) -or -not (Same-Snapshot $afterOld.workflows $after.workflows) -or
            -not (Same-Snapshot $afterOld.allWorkflows $after.allWorkflows)) { throw 'The resumed old candidate changed the winning source or recipients.' }
        $receipt.afterOldResume = $afterOld
    }
    if (-not (Same-Snapshot (Read-SourceManifest) $sourceManifest)) { throw 'The observed recovery source changed during execution.' }
    if (-not (Same-Snapshot (Read-BuildManifest $ProbeAssembly) $probeManifest) -or
        -not (Same-Snapshot (Read-BuildManifest $NormalAssembly) $normalManifest) -or
        -not (Same-Snapshot (Read-BuildManifest $ReaderAssembly) $readerManifest) -or
        (Get-FileHash $ReaderAssembly).Hash.ToLowerInvariant() -ne $readerHash) { throw 'A recovery evidence binary changed during the run.' }
    $receipt.passed = $true
    Write-Host "Actual $Phase crash → standard-host recovery passed; saved starts/waits only."
} catch {
    $receipt.failures += @{ type = $_.Exception.GetType().Name; message = $_.Exception.Message }
    throw
} finally {
    $cleanupFailures = @()
    $cleanupActions = @(
        @{ name = 'source observer'; action = { if ($watch -and -not $watch.HasExited) { $watch.Kill(); $watch.WaitForExit() } } },
        @{ name = 'HTTP client'; action = { if ($client) { $client.Dispose() } } },
        @{ name = 'standard host'; action = { Stop-OwnedElsaServer } },
        @{ name = 'probe controller'; action = {
            if ($oldBroker) {
                if (-not $oldBroker.HasExited) {
                    New-Item -ItemType File -Force $oldBrokerStop | Out-Null
                    if (-not $oldBroker.WaitForExit(20000)) { throw 'The exact probe controller did not finish owned cleanup.' }
                }
                if ($oldBroker.ExitCode -ne 0) { throw "The owned probe controller exited with code $($oldBroker.ExitCode)." }
            }
        } }
    )
    foreach ($cleanup in $cleanupActions) {
        try { & $cleanup.action }
        catch { $cleanupFailures += @{ phase = $cleanup.name; type = $_.Exception.GetType().Name; message = $_.Exception.Message } }
    }
    if ($cleanupFailures.Count -gt 0) { $receipt.passed = $false; $receipt.failures += $cleanupFailures }
    $receipt.capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $receipt | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $EvidencePath
    Write-Host "Owned recovery evidence/logs retained at $root"
    if ($cleanupFailures.Count -gt 0) { throw 'Owned recovery cleanup failed; all cleanup actions were attempted and failures were recorded.' }
}
