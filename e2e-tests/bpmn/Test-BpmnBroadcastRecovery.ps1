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
    [ValidateSet('after-cohort', 'after-resume', 'before-selection')][string] $Phase = 'after-cohort'
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../_ServerLifecycle.ps1"
. "$PSScriptRoot/../_ElsaCommon.ps1"
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
    $files = & git -C $repository ls-files --cached --others --exclude-standard -- Essentials/src/Activities/Bpmn Essentials/src/Activities/Primitives Essentials/src/Workflows/Runtime Essentials/apps/Elsa.Workbench e2e-tests/bpmn tools/bpmn/DeliveryRecoveryRead
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
$settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
@{ caseId = $caseId; phase = $Phase; eventName = $eventName } | ConvertTo-Json | Set-Content (Join-Path $control 'plan.json')
$receipt = @{ sourceRevision = $SourceRevision; scenario = switch ($Phase) { 'after-resume' { 'partial-callback-and-rearm-before-source-ack' } 'before-selection' { 'delayed-first-selection-and-pre-start-wait-exclusion' } default { 'after-cohort-before-dispatch' } }; passed = $false;
    probeAssemblySha256 = (Get-FileHash $ProbeAssembly).Hash.ToLowerInvariant(); normalAssemblySha256 = (Get-FileHash $NormalAssembly).Hash.ToLowerInvariant();
    sourceCommit = $sourceCommit; sourceFiles = $sourceManifest; probeBuild = $probeManifest; normalBuild = $normalManifest; readerSha256 = $readerHash; readerBuild = $readerManifest;
    cut = $null; stopped = $null; successorClaim = $null; recovered = $null; observations = @(); failures = @() }
$client = $null; $pending = $null; $watch = $null
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
function Observe([string] $Name, [string[]] $ExtraIds) {
    $path = Join-Path $caseDirectory "$Name.json"
    $arguments = @("$PSScriptRoot/inspect_broadcast_recovery.py", '--database', (Join-Path $root 'elsa.db'), '--reader', $ReaderAssembly, '--marker', $markerPath, '--output', $path)
    foreach ($id in $ExtraIds) { $arguments += @('--extra-workflow', $id) }
    & $Python @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "The checked $Name observation failed." }
    return Get-Content -Raw $path | ConvertFrom-Json -AsHashtable -DateKind String
}
function Same-Snapshot($Left, $Right) {
    if ($null -eq $Left -or $null -eq $Right) { return ($null -eq $Left -and $null -eq $Right) }
    $leftDocument = [System.Text.Json.JsonDocument]::Parse(($Left | ConvertTo-Json -Depth 60 -Compress))
    $rightDocument = [System.Text.Json.JsonDocument]::Parse(($Right | ConvertTo-Json -Depth 60 -Compress))
    try { return [System.Text.Json.JsonElement]::DeepEquals($leftDocument.RootElement, $rightDocument.RootElement) }
    finally { $leftDocument.Dispose(); $rightDocument.Dispose() }
}
try {
    Start-OwnedElsaServer -BaseUrl $base -ContentRoot $root -AssemblyPath $ProbeAssembly
    $probeProcess = $script:OwnedElsaServer.Process
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
    $waitPublication = Import-Process $waitXml
    $waitRun = Invoke-Artifact -Ctx $ctx -ArtifactId $waitPublication.artifactId -SourceReferenceId $waitPublication.sourceReferenceId
    $null = Wait-Catches $waitRun.workflowExecutionId 2
    # Published before the snapshot; this start must survive a process restart with its original artifact-scoped key.
    $parkName = if ($Phase -eq 'before-selection') { $eventName } else { "park-$eventName" }
    $startXml = @"
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:source-recovery"><signal id="signal" name="$eventName"/><signal id="park" name="$parkName"/>
<process id="selected-start" isExecutable="true"><startEvent id="receive"><signalEventDefinition signalRef="signal"/></startEvent><intermediateCatchEvent id="park"><signalEventDefinition signalRef="park"/></intermediateCatchEvent><endEvent id="end"/>
<sequenceFlow id="first" sourceRef="receive" targetRef="park"/><sequenceFlow id="last" sourceRef="park" targetRef="end"/></process></definitions>
"@
    if ($Phase -eq 'before-selection') {
        $startXml = $startXml.Replace("<signal id=`"park`" name=`"$parkName`"/>", '').Replace('signalRef="park"', 'signalRef="signal"')
    }
    $selectedPublication = Import-Process $startXml
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
    if ($Phase -eq 'before-selection') {
        if ($null -ne $marker.snapshot) { throw 'Selection already occurred before the delayed-delivery cut.' }
    } elseif ($marker.snapshot.starts.Count -ne 1 -or $marker.snapshot.resumes.Count -ne 2) { throw 'The actual persisted cohort is incomplete at the cut.' }
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
    # These recipients are created after publisher commit. They join only if selection has not yet happened.
    $latePublication = Import-Process ($waitXml.Replace('id="waiter"', 'id="late-waiter"'))
    $lateRun = Invoke-Artifact -Ctx $ctx -ArtifactId $latePublication.artifactId -SourceReferenceId $latePublication.sourceReferenceId
    $null = Wait-Catches $lateRun.workflowExecutionId 2
    $lateStart = Import-Process ($startXml.Replace('id="selected-start"', 'id="late-start"'))
    Stop-OwnedElsaServer
    if (-not $probeProcess.HasExited) { throw 'The owned probe process remains live after termination.' }
    $stopped = Observe 'stopped' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
    $receipt.stopped = $stopped
    if ($stopped.source.status -ne 'Delivering' -or $stopped.source.deliveryFencingToken -ne $marker.fencingToken -or $stopped.source.deliveringOwnerId -ne $marker.ownerId) { throw 'The stopped source lost its actual claim facts.' }
    if (-not (Same-Snapshot $stopped.source.snapshot $marker.snapshot)) { throw 'The physical source differs from the captured winner.' }
    if ($Phase -eq 'before-selection') {
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
        if ($Phase -eq 'after-resume' -and $recipient.bookmarkId -eq $marker.acceptedBookmark) {
            if ($actual.Count -ne 0 -or $activity.Count -ne 1 -or $activity[0].status -ne 'Completed') { throw 'The accepted callback was not durably completed before crash.' }
            $rearmed = @($wf.bookmarks | Where-Object bookmarkId -eq $receipt.rearmedWait.bookmarkId)
            if ($rearmed.Count -ne 1 -or -not (Same-Snapshot $rearmed[0] $receipt.rearmedWait)) { throw 'The completed callback rearm changed before crash.' }
        } elseif ($actual.Count -ne 1 -or $actual[0].generation -ne $recipient.generation -or $activity.Count -ne 1 -or $activity[0].status -ne 'Suspended') { throw 'An undispatched selected wait changed before crash.' }
    }
    foreach ($start in $stopped.source.selectedStarts) {
        $target = @($stopped.workflows | Where-Object workflowExecutionId -eq $start.workflowExecutionId)
        if ($target.Count -ne 1) { throw 'The exact selected keyed target observation is unavailable.' }
        if ($Phase -eq 'after-cohort') {
            if ($target[0].exists -or @($stopped.allWorkflows | Where-Object artifactId -eq $selectedPublication.artifactId).Count -ne 0) { throw 'A selected start ran before the post-cohort crash.' }
        } else {
            $park = @($target[0].bookmarks | Where-Object { $_.executableNodeId -eq 'node-park' -and $_.activityStatus -eq 'Suspended' })
            if (-not $target[0].exists -or $park.Count -ne 1 -or $target[0].bookmarks.Count -ne 1) { throw 'Partial delivery did not durably start and park the original keyed target.' }
            $receipt.startedParkBeforeCrash = $park[0]
        }
    }
    $null = $settings.CShells.Shells.default.Features.Remove('BpmnRecoveryProbe')
    $settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
    # Begin the observer before startup: normal background delivery may reclaim and complete before readiness returns.
    $watchOutput = Join-Path $caseDirectory 'successor.json'
    $info = [Diagnostics.ProcessStartInfo]::new(); $info.FileName = $Python; $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($arg in @("$PSScriptRoot/inspect_broadcast_recovery.py", '--database', (Join-Path $root 'elsa.db'), '--reader', $ReaderAssembly, '--marker', $markerPath, '--output', $watchOutput, '--watch-claim', '--previous-fence', [string]$stopped.source.deliveryFencingToken, '--timeout', '180')) { $info.ArgumentList.Add($arg) }
    $watch = [Diagnostics.Process]::Start($info); $watchStdout = $watch.StandardOutput.ReadToEndAsync(); $watchStderr = $watch.StandardError.ReadToEndAsync()
    Start-OwnedElsaServer -BaseUrl $base -ContentRoot $root -AssemblyPath $NormalAssembly
    $receipt.normalProcessId = $script:OwnedElsaServer.Process.Id; $receipt.crashedProcessId = $probeProcess.Id
    $ctx = Connect-Elsa -BaseUrl $base
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(180)
    while (-not $watch.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not $watch.HasExited) { throw 'Successor observation remains live beyond the fixture bound.' }
    if ($watch.ExitCode -ne 0) { throw "Successor observation failed: $($watchStdout.GetAwaiter().GetResult()) $($watchStderr.GetAwaiter().GetResult())" }
    $successor = Get-Content -Raw $watchOutput | ConvertFrom-Json -AsHashtable -DateKind String
    $receipt.successorClaim = $successor
    if ($successor.source.deliveryFencingToken -le $stopped.source.deliveryFencingToken -or $successor.source.deliveringOwnerId -eq $stopped.source.deliveringOwnerId) { throw 'No distinct successor source claim was proven.' }
    if ($Phase -ne 'after-resume') {
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
    if ($Phase -eq 'before-selection') {
        $lateCompleted = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $lateRun.workflowExecutionId -TimeoutSeconds 60
        if ($lateCompleted.instance.status -ne 'Completed' -or @($lateCompleted.incidents).Count -ne 0) { throw 'First delivery did not include the listener armed after publisher commit.' }
        $selectedSource = Observe 'selected-after-recovery' @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
        if ($selectedSource.source.selectedStarts.Count -ne 2 -or $selectedSource.source.selectedResumes.Count -ne 4) { throw 'Initial selection did not capture the complete delivery-time cohort.' }
        foreach ($before in $stopped.workflows | Where-Object { $_.workflowExecutionId -in @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId) }) {
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
    if ($after.source.status -ne 'Delivered' -or -not (Same-Snapshot $after.source.snapshot $selectedSource.source.snapshot)) { throw 'Recovery did not settle its complete immutable source cohort.' }
    $expectedArtifacts = @($selectedPublication.artifactId)
    if ($Phase -eq 'before-selection') { $expectedArtifacts += $lateStart.artifactId }
    if ($after.source.selectedStarts.Count -ne $expectedArtifacts.Count) { throw 'The final source has an unexpected start set.' }
    foreach ($selectedStart in $after.source.selectedStarts) {
        $selected = @($after.allWorkflows | Where-Object artifactId -eq $selectedStart.artifactId)
        if ($selectedStart.artifactId -notin $expectedArtifacts -or $selected.Count -ne 1 -or $selected[0].workflowExecutionId -ne $selectedStart.workflowExecutionId) { throw 'A saved start did not produce exactly its original deterministic keyed execution.' }
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
    if ($Phase -ne 'before-selection') {
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
        if ($Phase -eq 'before-selection') {
            $oldWait = $stopped.workflows | ForEach-Object bookmarks | Where-Object bookmarkId -eq $parkedWait[0].bookmarkId
            $captured = @($after.source.selectedResumes | Where-Object { $_.workflowExecutionId -eq $keyedState.workflowExecutionId -and $_.bookmarkId -eq $parkedWait[0].bookmarkId })
            $original = @($after.source.selectedResumes)[0]
            $originalWorkflow = @($stopped.workflows | Where-Object workflowExecutionId -eq $original.workflowExecutionId)[0]
            $originalWait = @($originalWorkflow.bookmarks | Where-Object bookmarkId -eq $original.bookmarkId)[0]
            if ($null -ne $oldWait -or $captured.Count -ne 0 -or $parkedWait[0].stimulusType -ne $originalWait.stimulusType -or $parkedWait[0].stimulusHash -ne $originalWait.stimulusHash) { throw 'The matching wait created by a selected start was not excluded from its own source.' }
        }
    }
    $publisherFinished = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $marker.workflowExecutionId -TimeoutSeconds 60
    if ($publisherFinished.instance.status -ne 'Completed' -or @($publisherFinished.incidents).Count -ne 0) { throw 'The original source publisher did not complete cleanly.' }
    $receipt.observations += @{ originalWaiter = $finished; selectedStarts = $startedRuns; laterListener = $lateInstance; publisher = $publisherFinished }
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
    if ($watch -and -not $watch.HasExited) { $watch.Kill(); $watch.WaitForExit() }
    if ($client) { $client.Dispose() }
    Stop-OwnedElsaServer
    $receipt.capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $receipt | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $EvidencePath
    Write-Host "Owned recovery evidence/logs retained at $root"
}
