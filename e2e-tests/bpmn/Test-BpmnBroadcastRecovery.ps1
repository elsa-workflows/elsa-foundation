<#
.SYNOPSIS
    Crashes an owned source-built Workbench after its actual cohort commit; recovers with the standard build.
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
    [Parameter(Mandatory)][string] $SourceRevision
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
$caseId = 'after-cohort-' + [guid]::NewGuid().ToString('N')
$eventName = 'bpmn-source-recovery-' + [guid]::NewGuid().ToString('N')
$caseDirectory = Join-Path $control $caseId
$markerPath = Join-Path $caseDirectory 'reached.json'
$settingsPath = Join-Path $root 'shells.json'
$settings = Get-Content -Raw $settingsPath | ConvertFrom-Json -AsHashtable
$settings.CShells.Shells.default.Features.BpmnRecoveryProbe = @{ ControlDirectory = $control }
$settings.CShells.Shells.default.Features.WorkflowsRuntimeResumption = @{ SweepIntervalSeconds = 1 }
$settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
@{ caseId = $caseId; phase = 'after-cohort'; eventName = $eventName } | ConvertTo-Json | Set-Content (Join-Path $control 'plan.json')
$receipt = @{ sourceRevision = $SourceRevision; scenario = 'after-cohort-before-dispatch'; passed = $false;
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
    $waitPublication = Import-Process $waitXml
    $waitRun = Invoke-Artifact -Ctx $ctx -ArtifactId $waitPublication.artifactId -SourceReferenceId $waitPublication.sourceReferenceId
    $null = Wait-Catches $waitRun.workflowExecutionId 2
    # Published before the snapshot; this start must survive a process restart with its original artifact-scoped key.
    $startXml = @"
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:source-recovery"><signal id="signal" name="$eventName"/><signal id="park" name="park-$eventName"/>
<process id="selected-start" isExecutable="true"><startEvent id="receive"><signalEventDefinition signalRef="signal"/></startEvent><intermediateCatchEvent id="park"><signalEventDefinition signalRef="park"/></intermediateCatchEvent><endEvent id="end"/>
<sequenceFlow id="first" sourceRef="receive" targetRef="park"/><sequenceFlow id="last" sourceRef="park" targetRef="end"/></process></definitions>
"@
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
    if ($marker.snapshot.starts.Count -ne 1 -or $marker.snapshot.resumes.Count -ne 2) { throw 'The actual persisted cohort is incomplete at the cut.' }
    # A listener and a different publication created after selection must not join this source on recovery.
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
    foreach ($recipient in $stopped.source.selectedResumes) {
        $wf = @($stopped.workflows | Where-Object workflowExecutionId -eq $recipient.workflowExecutionId)[0]
        $actual = @($wf.bookmarks | Where-Object bookmarkId -eq $recipient.bookmarkId)
        if ($actual.Count -ne 1 -or $actual[0].generation -ne $recipient.generation) { throw 'A selected wait changed before any dispatch.' }
    }
    if (@($stopped.allWorkflows | Where-Object artifactId -eq $selectedPublication.artifactId).Count -ne 0) { throw 'A selected start ran before the post-cohort crash.' }
    foreach ($start in $stopped.source.selectedStarts) {
        $target = @($stopped.workflows | Where-Object workflowExecutionId -eq $start.workflowExecutionId)
        if ($target.Count -ne 1 -or $target[0].exists) { throw 'The checked pre-dispatch keyed start absence is unavailable.' }
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
    $finished = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $waitRun.workflowExecutionId -TimeoutSeconds 60
    if ($finished.instance.status -ne 'Completed' -or @($finished.incidents).Count -ne 0) { throw 'Normal-host recovery did not complete the original catches/join cleanly.' }
    $keyedId = $stopped.source.selectedStarts[0].workflowExecutionId
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        $started = Get-WorkflowInstance -Ctx $ctx -ExecutionId $keyedId
        $parked = @($started.activities | Where-Object { $_.executableNodeId -eq 'node-park' -and $_.status -eq 'Suspended' })
        if ($parked.Count -eq 1) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($parked.Count -ne 1 -or @($started.incidents).Count -ne 0) { throw 'The exact saved keyed start did not reach its parked child.' }
    $after = Observe 'recovered'  @($waitRun.workflowExecutionId, $lateRun.workflowExecutionId)
    $receipt.recovered = $after
    if ($after.source.status -ne 'Delivered' -or -not (Same-Snapshot $after.source.snapshot $stopped.source.snapshot)) { throw 'Recovery did not settle the original immutable source cohort.' }
    $selected = @($after.allWorkflows | Where-Object artifactId -eq $selectedPublication.artifactId)
    if ($selected.Count -ne 1 -or $after.source.selectedStarts.Count -ne 1 -or
        $selected[0].workflowExecutionId -ne $after.source.selectedStarts[0].workflowExecutionId) { throw 'The saved start did not produce exactly its original deterministic keyed execution.' }
    foreach ($recipient in $after.source.selectedResumes) {
        $wf = @($after.workflows | Where-Object workflowExecutionId -eq $recipient.workflowExecutionId)[0]
        $activity = @($wf.selectedActivities | Where-Object activityExecutionId -eq $recipient.activityExecutionId)
        if ($activity.Count -ne 1 -or $activity[0].status -ne 'Completed' -or @($wf.bookmarks | Where-Object bookmarkId -eq $recipient.bookmarkId).Count -ne 0) { throw 'A saved catch did not durably complete and retire its selected registration.' }
    }
    if (@($after.allWorkflows | Where-Object artifactId -eq $lateStart.artifactId).Count -ne 0) { throw 'A later binding joined an already saved source.' }
    $late = @($after.workflows | Where-Object workflowExecutionId -eq $lateRun.workflowExecutionId)[0]
    $lateBefore = @($stopped.workflows | Where-Object workflowExecutionId -eq $lateRun.workflowExecutionId)[0]
    if ($late.bookmarks.Count -ne 2 -or $late.status -ne $lateBefore.status -or
        -not (Same-Snapshot $late.bookmarks $lateBefore.bookmarks)) { throw 'The old source changed a later exact wait or activity/generation.' }
    $lateInstance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $lateRun.workflowExecutionId
    if (@($lateInstance.incidents).Count -ne 0) { throw 'The later listener acquired an incident during old-source recovery.' }
    $keyedState = @($after.workflows | Where-Object workflowExecutionId -eq $keyedId)[0]
    $parkedWait = @($keyedState.bookmarks | Where-Object { $_.executableNodeId -eq 'node-park' -and $_.activityStatus -eq 'Suspended' })
    if ($parkedWait.Count -ne 1 -or $parkedWait[0].generation -le 0 -or $keyedState.bookmarks.Count -ne 1) { throw 'The saved keyed start lacks its exact physical park registration.' }
    $publisherFinished = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $marker.workflowExecutionId -TimeoutSeconds 60
    if ($publisherFinished.instance.status -ne 'Completed' -or @($publisherFinished.incidents).Count -ne 0) { throw 'The original source publisher did not complete cleanly.' }
    $receipt.observations += @{ originalWaiter = $finished; selectedStart = $started; laterListener = $lateInstance; publisher = $publisherFinished }
    if (-not (Same-Snapshot (Read-SourceManifest) $sourceManifest)) { throw 'The observed recovery source changed during execution.' }
    if (-not (Same-Snapshot (Read-BuildManifest $ProbeAssembly) $probeManifest) -or
        -not (Same-Snapshot (Read-BuildManifest $NormalAssembly) $normalManifest) -or
        -not (Same-Snapshot (Read-BuildManifest $ReaderAssembly) $readerManifest) -or
        (Get-FileHash $ReaderAssembly).Hash.ToLowerInvariant() -ne $readerHash) { throw 'A recovery evidence binary changed during the run.' }
    $receipt.passed = $true
    Write-Host 'Actual post-cohort crash → standard-host recovery passed; saved starts/waits only.'
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
