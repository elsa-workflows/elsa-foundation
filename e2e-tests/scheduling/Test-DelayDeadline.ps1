<#
.SYNOPSIS
    Qualifies deadline-driven Delay resumption on an owned fresh-SQLite Workbench, including restart.
.DESCRIPTION
    Build Elsa.Workbench first. Uses the committed normal composition, a private port/content root and
    only processes this script starts. Timing is one correctness witness per path, not a benchmark.
#>
[CmdletBinding()]
param([string] $BaseUrl, [string] $EvidencePath)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/_SchedulingCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"
$BaseUrl = Resolve-ElsaServerBaseUrl -BaseUrl $BaseUrl -RestartServer
$contentRoot = New-ElsaContentRoot -Prefix 'elsa-delay-deadline'
$evidence = @{ contentRoot = $contentRoot; baseUrl = $BaseUrl; cases = @() }

try {
    Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $contentRoot
    $ctx = Connect-Elsa -BaseUrl $BaseUrl
    $wl = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    $seq = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $dl = Get-ActivityVersionId -Ctx $ctx -TypeKey $script:DelayType
    $before = New-ActivityNode -NodeId 'before' -VersionId $wl -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'before deadline'))
    $delay = New-DelayNode -NodeId 'delay' -VersionId $dl -Duration '00:00:05'
    $after = New-ActivityNode -NodeId 'after' -VersionId $wl -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'after deadline'))
    $root = New-ActivityNode -NodeId 'root' -VersionId $seq -Structure (New-SequenceStructure -Activities @($before, $delay, $after))
    $definition = Submit-Workflow -Ctx $ctx -Name "Delay deadline $([guid]::NewGuid().ToString('N'))" -RootActivity $root
    $published = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id

    foreach ($mode in @('local', 'restart')) {
        $run = Invoke-Artifact -Ctx $ctx -ArtifactId $published.artifactId -SourceReferenceId $published.sourceReferenceId
        $executionId = $run.workflowExecutionId
        $suspendDeadline = [DateTimeOffset]::UtcNow.AddSeconds(4)
        do {
            $instance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $executionId
            $waiting = @($instance.activities | Where-Object { $_.executableNodeId -eq 'delay' -and $_.status -eq 'Suspended' })
            if ($waiting.Count -eq 1) { break }
            Start-Sleep -Milliseconds 50
        } while ([DateTimeOffset]::UtcNow -lt $suspendDeadline)
        if ($waiting.Count -ne 1) { throw "$mode did not reach durable suspension." }
        if ($mode -eq 'restart') {
            Stop-OwnedElsaServer
            Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $contentRoot
            $ctx = Connect-Elsa -BaseUrl $BaseUrl
        }
        $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $executionId -TimeoutSeconds 30
        if ($instance.instance.status -notin @('Completed', 'Finished')) { throw "$mode did not complete." }
        $delayActivity = @($instance.activities | Where-Object executableNodeId -eq 'delay')
        $afterActivity = @($instance.activities | Where-Object executableNodeId -eq 'after')
        if ($delayActivity.Count -ne 1 -or $afterActivity.Count -ne 1) { throw "$mode did not resume exactly once." }
        $start = [DateTimeOffset]$delayActivity[0].startedAt
        $resumed = [DateTimeOffset]$afterActivity[0].startedAt
        $elapsed = ($resumed - $start).TotalSeconds
        if ($elapsed -lt 5) { throw "$mode resumed early after $elapsed seconds." }
        # Restart startup may extend past the deadline; deterministic tests cover future-deadline scheduling.
        if ($mode -eq 'local' -and $elapsed -gt 6) { throw "Local Delay took $elapsed seconds; expected five seconds plus at most one second of host overhead." }
        $evidence.cases += @{ mode = $mode; workflowExecutionId = $executionId; delayStartedAt = $start; afterStartedAt = $resumed; elapsedSeconds = $elapsed; status = $instance.instance.status }
        Write-Host "$mode deadline OK: $elapsed seconds, one completed resumption." -ForegroundColor Green
    }

    & "$PSScriptRoot/Test-Delay.ps1" -BaseUrl $BaseUrl -Duration '00:00:05'
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Delay e2e failed.' }
    & "$PSScriptRoot/Test-Timer.ps1" -BaseUrl $BaseUrl
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Timer e2e failed.' }
    & "$PSScriptRoot/Test-Cron.ps1" -BaseUrl $BaseUrl
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Cron e2e failed.' }
    if ($EvidencePath) { $evidence | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $EvidencePath }
} finally {
    Stop-OwnedElsaServer
    Write-Host "Evidence content root retained: $contentRoot"
}
