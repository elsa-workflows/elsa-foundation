<#
.SYNOPSIS
    Replay the documented dotnet run launch profile and WriteLine smoke on a hosted runner.
.DESCRIPTION
    Owns the dotnet run process tree launched from the repository root. It uses the fixed HTTP profile
    port, leaves pre-existing listeners alone, and retains startup/smoke diagnostics when a step fails.
    -InvalidCommandControl propagates a deliberately invalid project invocation's exit code to its caller.
#>
[CmdletBinding()]
param(
    [switch] $InvalidCommandControl
)

$ErrorActionPreference = 'Stop'
$baseUrl = 'http://localhost:5095'
$port = 5095
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runProfileIsWindows = $env:OS -eq 'Windows_NT'
$diagnosticsRoot = $null
$ownedProcess = $null
$ownedProcessSnapshots = @()
$ownedListenerSnapshots = @()
$journeyError = $null
$cleanupFailures = @()
$failureControlExitCode = $null

. "$PSScriptRoot/_ServerLifecycle.ps1"

function Add-RunProfileSummary {
    param([Parameter(Mandatory)][string[]] $Lines)
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) { return }
    $content = ($Lines -join [Environment]::NewLine) + [Environment]::NewLine
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $content, $utf8WithoutBom)
}

function New-RunProfileDiagnosticsRoot {
    $parent = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
    $runId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { 'local' }
    $attempt = if ($env:GITHUB_RUN_ATTEMPT) { $env:GITHUB_RUN_ATTEMPT } else { '1' }
    $path = Join-Path $parent ("elsa-run-profile-{0}-{1}-{2}" -f $runId, $attempt, [guid]::NewGuid().ToString('N'))
    New-Item -Path $path -ItemType Directory -Force | Out-Null
    return $path
}

function Set-RunProfileOutput {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][string] $Value)
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) { return }
    if ($Value.Contains("`r") -or $Value.Contains("`n")) { throw "The workflow output '$Name' contains a line break." }
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "$Name=$Value$([Environment]::NewLine)", $utf8WithoutBom)
}

function Assert-RunProfilePortFree {
    $listeners = @(Get-PortListenerPid -Port $port)
    if ($listeners.Count -eq 0) { return }
    $described = ($listeners | ForEach-Object {
        $process = Get-Process -Id $_ -ErrorAction SilentlyContinue
        if ($process) { "pid $_ ($($process.ProcessName))" } else { "pid $_" }
    }) -join ', '
    throw "Port $port is occupied by $described. No process was stopped and no alternate port was selected."
}

function Get-RunProfileProcessParentRows {
    if ($runProfileIsWindows) {
        $processes = @(Get-CimInstance -ClassName Win32_Process -Property ProcessId, ParentProcessId -ErrorAction Stop)
        return @($processes | ForEach-Object {
            [pscustomobject]@{ Id = [int]$_.ProcessId; ParentId = [int]$_.ParentProcessId }
        })
    }

    $lines = @(& ps -e -o pid=,ppid= 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "Could not inspect the owned process tree (ps exit code $exitCode)." }
    $rows = @()
    foreach ($line in $lines) {
        if ($line -match '^\s*(\d+)\s+(\d+)\s*$') {
            $rows += [pscustomobject]@{ Id = [int]$Matches[1]; ParentId = [int]$Matches[2] }
        }
    }
    return $rows
}

function Get-RunProfileProcessTreeIds {
    param([Parameter(Mandatory)][int] $RootProcessId)
    $rows = @(Get-RunProfileProcessParentRows)
    $known = @{}
    $known[[string]$RootProcessId] = $true
    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($row in $rows) {
            if ($known.ContainsKey([string]$row.ParentId) -and -not $known.ContainsKey([string]$row.Id)) {
                $known[[string]$row.Id] = $true
                $changed = $true
            }
        }
    }
    return @($known.Keys | ForEach-Object { [int]$_ })
}

function New-RunProfileProcessSnapshot {
    param([Parameter(Mandatory)][int] $ProcessId)
    $process = [Diagnostics.Process]::GetProcessById($ProcessId)
    try {
        return [pscustomobject]@{
            Id = $process.Id
            StartTimeTicks = $process.StartTime.ToUniversalTime().Ticks
        }
    } finally {
        $process.Dispose()
    }
}

function Get-RunProfileProcessForSnapshot {
    param([Parameter(Mandatory)] $Snapshot)
    try {
        $process = [Diagnostics.Process]::GetProcessById([int]$Snapshot.Id)
    } catch [ArgumentException] {
        return $null
    }
    if ($null -eq $process) { return $null }
    try {
        $process.Refresh()
        if ($process.HasExited) {
            $process.Dispose()
            return $null
        }
        $startTimeTicks = $process.StartTime.ToUniversalTime().Ticks
        if ($startTimeTicks -ne [long]$Snapshot.StartTimeTicks) {
            $process.Dispose()
            return $null
        }
        return $process
    } catch [InvalidOperationException] {
        $process.Dispose()
        return $null
    }
}

function Get-RunProfileReadiness {
    param([Parameter(Mandatory)][string] $CurlPath)
    $body = @(& $CurlPath --connect-timeout 3 --max-time 10 -fsS "$baseUrl/health/ready" 2>$null)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        return [pscustomobject]@{ ExitCode = $exitCode; Status = $null }
    }
    try {
        $health = ($body -join [Environment]::NewLine) | ConvertFrom-Json
    } catch {
        return [pscustomobject]@{ ExitCode = 0; Status = $null }
    }
    return [pscustomobject]@{ ExitCode = 0; Status = [string]$health.status }
}

function Start-RunProfileProcess {
    param(
        [Parameter(Mandatory)][string] $ProjectPath,
        [string] $LaunchProfile,
        [Parameter(Mandatory)][string] $OutputLog,
        [Parameter(Mandatory)][string] $ErrorLog
    )
    $arguments = @('run', '--no-build', '--project', $ProjectPath)
    if ($LaunchProfile) { $arguments += @('--launch-profile', $LaunchProfile) }
    $displayCommand = "dotnet $($arguments -join ' ')"
    Write-Host "[launch] $displayCommand"
    $start = @{
        FilePath = 'dotnet'
        ArgumentList = $arguments
        WorkingDirectory = $repoRoot
        RedirectStandardOutput = $OutputLog
        RedirectStandardError = $ErrorLog
        PassThru = $true
    }
    if ($runProfileIsWindows) { $start.WindowStyle = 'Hidden' }
    $process = Start-Process @start
    if ($runProfileIsWindows) {
        # Windows PowerShell 5.1 can lose ExitCode for redirected processes unless the
        # Process handle is opened and retained before the child exits.
        $null = $process.Handle
    }
    return $process
}

function Update-RunProfileObservedSnapshots {
    param(
        [Parameter(Mandatory)][int] $RootProcessId,
        [Parameter(Mandatory)][object[]] $CurrentSnapshots
    )
    $rootSnapshot = @($CurrentSnapshots | Where-Object { $_.Id -eq $RootProcessId } | Select-Object -First 1)
    if ($rootSnapshot.Count -eq 0) { throw "The owned dotnet run root $RootProcessId has no original process identity." }

    $liveRoot = Get-RunProfileProcessForSnapshot -Snapshot $rootSnapshot[0]
    if (-not $liveRoot) { return $CurrentSnapshots }
    $liveRoot.Dispose()

    $newSnapshots = @()
    foreach ($id in @(Get-RunProfileProcessTreeIds -RootProcessId $RootProcessId)) {
        try {
            $snapshot = New-RunProfileProcessSnapshot -ProcessId $id
            if ($id -eq $RootProcessId -and $snapshot.StartTimeTicks -ne $rootSnapshot[0].StartTimeTicks) { return $CurrentSnapshots }
            $newSnapshots += $snapshot
        }
        catch [ArgumentException] { }
        catch [InvalidOperationException] { }
    }
    $liveRoot = Get-RunProfileProcessForSnapshot -Snapshot $rootSnapshot[0]
    if (-not $liveRoot) { return $CurrentSnapshots }
    $liveRoot.Dispose()

    $updatedSnapshots = @($CurrentSnapshots)
    foreach ($snapshot in $newSnapshots) {
        $sameIdentity = @($updatedSnapshots | Where-Object { $_.Id -eq $snapshot.Id -and $_.StartTimeTicks -eq $snapshot.StartTimeTicks })
        if ($sameIdentity.Count -eq 0) {
            $updatedSnapshots += $snapshot
        }
    }
    return $updatedSnapshots
}

function Get-RunProfileLiveSnapshots {
    param([Parameter(Mandatory)][object[]] $Snapshots)
    $liveSnapshots = @()
    foreach ($snapshot in $Snapshots) {
        $process = Get-RunProfileProcessForSnapshot -Snapshot $snapshot
        if (-not $process) { continue }
        $process.Dispose()
        $liveSnapshots += $snapshot
    }
    return $liveSnapshots
}

function Stop-RunProfileProcessTree {
    param([Parameter(Mandatory)] $RootSnapshot, [Parameter(Mandatory)][object[]] $Snapshots)
    $failures = @()
    $root = Get-RunProfileProcessForSnapshot -Snapshot $RootSnapshot
    if ($root) {
        try {
            if ($runProfileIsWindows) {
                $taskkill = Get-Command taskkill.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $taskkill) { throw 'taskkill.exe is unavailable; cannot stop the owned dotnet run process tree safely.' }
                & $taskkill.Source /PID ([string]$RootSnapshot.Id) /T /F *> $null
                $killExitCode = $LASTEXITCODE
                $stillRunning = Get-RunProfileProcessForSnapshot -Snapshot $RootSnapshot
                if ($killExitCode -ne 0 -and $stillRunning) {
                    $stillRunning.Dispose()
                    throw "taskkill.exe could not stop owned process $($RootSnapshot.Id) (exit code $killExitCode)."
                }
                if ($stillRunning) { $stillRunning.Dispose() }
            } else {
                $root.Kill($true)
            }
        } catch {
            $failures += "Could not stop owned dotnet run process tree rooted at pid $($RootSnapshot.Id): $($_.Exception.Message)"
        } finally {
            $root.Dispose()
        }
    }

    foreach ($snapshot in $Snapshots) {
        $process = Get-RunProfileProcessForSnapshot -Snapshot $snapshot
        if (-not $process) { continue }
        try {
            if ($runProfileIsWindows) {
                $taskkill = Get-Command taskkill.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $taskkill) { throw 'taskkill.exe is unavailable; cannot stop a recorded child safely.' }
                & $taskkill.Source /PID ([string]$snapshot.Id) /T /F *> $null
                $killExitCode = $LASTEXITCODE
                $stillRunning = Get-RunProfileProcessForSnapshot -Snapshot $snapshot
                if ($killExitCode -ne 0 -and $stillRunning) {
                    $stillRunning.Dispose()
                    throw "taskkill.exe could not stop recorded process $($snapshot.Id) (exit code $killExitCode)."
                }
                if ($stillRunning) { $stillRunning.Dispose() }
            } else {
                $process.Kill()
            }
        } catch {
            $failures += "Could not stop recorded process pid $($snapshot.Id): $($_.Exception.Message)"
        } finally {
            $process.Dispose()
        }
    }

    $deadline = (Get-Date).AddSeconds(20)
    do {
        $alive = @()
        foreach ($snapshot in $Snapshots) {
            $process = Get-RunProfileProcessForSnapshot -Snapshot $snapshot
            if ($process) {
                $alive += [int]$snapshot.Id
                $process.Dispose()
            }
        }
        if ($alive.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if ($alive.Count -gt 0) { $failures += "Owned process tree still has live recorded pid(s): $($alive -join ', ')." }
    return $failures
}

function Assert-RunProfilePortReleased {
    param([int] $TimeoutSec = 10)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $listeners = @(Get-PortListenerPid -Port $port)
        if ($listeners.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "Port $port remains occupied by pid(s) $($listeners -join ', '); no listener was stopped unless it was a recorded child of this dotnet run process."
}

function Invoke-WorkflowSmoke {
    param([Parameter(Mandatory)][string] $SmokeLog)
    $scriptPath = if ($runProfileIsWindows) { '.\e2e-tests\Test-WorkflowFlow.ps1' } else { './e2e-tests/Test-WorkflowFlow.ps1' }
    $childPowerShell = if ($runProfileIsWindows) { 'powershell.exe' } else { 'pwsh' }
    $child = Get-Command $childPowerShell -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $child) { throw "Required smoke-test shell '$childPowerShell' is unavailable." }
    $arguments = @('-NoProfile')
    if ($runProfileIsWindows) { $arguments += @('-ExecutionPolicy', 'Bypass') }
    $arguments += @('-File', $scriptPath, '-BaseUrl', $baseUrl)
    Write-Host ("[smoke] {0} {1}" -f $childPowerShell, ($arguments -join ' '))
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $child.Source @arguments *> $SmokeLog
        return [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
}

function Write-RunProfileFailureDiagnostics {
    param([string] $SmokeLog, [string] $ServerOutputLog, [string] $ServerErrorLog)
    foreach ($path in @($SmokeLog, $ServerOutputLog, $ServerErrorLog)) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        Write-Host "--- $([IO.Path]::GetFileName($path)) (last 60 lines) ---" -ForegroundColor Yellow
        Get-Content -LiteralPath $path -Tail 60 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
    }
}

$diagnosticsRoot = New-RunProfileDiagnosticsRoot
$serverOutputLog = Join-Path $diagnosticsRoot 'dotnet-run.stdout.log'
$serverErrorLog = Join-Path $diagnosticsRoot 'dotnet-run.stderr.log'
$smokeLog = Join-Path $diagnosticsRoot 'workflow-smoke.log'
Set-RunProfileOutput -Name 'diagnostics_path' -Value $diagnosticsRoot

try {
    $revision = (& git -C $repoRoot rev-parse HEAD 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') { throw 'Could not record the exact source revision.' }
    $sdk = (& dotnet --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.') { throw "Expected the repository's stable .NET 10 SDK selection; selected '$sdk'." }
    if ($runProfileIsWindows -and ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1)) {
        throw "The Windows journey requires Windows PowerShell 5.1; found $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)."
    }
    if (-not $runProfileIsWindows -and ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7)) {
        throw "The Linux journey requires PowerShell 7 or later; found $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)."
    }
    Write-Host "[environment] runner=$($env:RUNNER_OS); image=$($env:ImageOS) $($env:ImageVersion); SDK=$sdk; PowerShell=$($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion); revision=$revision"

    Assert-RunProfilePortFree

    if ($InvalidCommandControl) {
        $invalidProject = 'src/apps/Elsa.Workbench/NoSuchRunProfileControl.csproj'
        $ownedProcess = Start-RunProfileProcess -ProjectPath $invalidProject -OutputLog $serverOutputLog -ErrorLog $serverErrorLog
        $ownedProcessSnapshots = @(New-RunProfileProcessSnapshot -ProcessId $ownedProcess.Id)
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline) {
            $listeners = @(Get-PortListenerPid -Port $port)
            if ($listeners.Count -gt 0) {
                throw "Port $port became occupied while the nonexistent-project control ran (pid(s) $($listeners -join ', ')); no listener was adopted or stopped."
            }
            $ownedProcess.Refresh()
            if ($ownedProcess.HasExited) { break }
            Start-Sleep -Milliseconds 250
        }
        $ownedProcess.Refresh()
        if (-not $ownedProcess.HasExited) { throw 'The invalid project command did not fail within 30 seconds.' }
        $ownedProcess.WaitForExit()
        $ownedProcess.Refresh()
        $nativeExitCode = $ownedProcess.ExitCode
        if ($null -eq $nativeExitCode) { throw 'The invalid project process exited, but its native exit code was unavailable.' }
        $failureControlExitCode = [int]$nativeExitCode
        if ($failureControlExitCode -eq 0) { throw 'The deliberately invalid project command unexpectedly returned success.' }
        Assert-RunProfilePortFree
        Write-Host "[negative control] nonexistent-project dotnet run returned exit code $failureControlExitCode and left port $port free."
    } else {
        $ownedProcess = Start-RunProfileProcess -ProjectPath 'src/apps/Elsa.Workbench/Elsa.Workbench.csproj' -LaunchProfile 'http' -OutputLog $serverOutputLog -ErrorLog $serverErrorLog
        $ownedProcessSnapshots = @(New-RunProfileProcessSnapshot -ProcessId $ownedProcess.Id)
        $curlName = if ($runProfileIsWindows) { 'curl.exe' } else { 'curl' }
        $curl = Get-Command $curlName -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $curl) { throw "Required readiness command '$curlName' is unavailable." }

        $ready = $false
        $lastCurlExitCode = $null
        $deadline = (Get-Date).AddSeconds(300)
        while ((Get-Date) -lt $deadline) {
            $ownedProcessSnapshots = @(Update-RunProfileObservedSnapshots -RootProcessId $ownedProcess.Id -CurrentSnapshots $ownedProcessSnapshots)
            $ownedProcess.Refresh()
            if ($ownedProcess.HasExited) {
                throw "Owned dotnet run process $($ownedProcess.Id) exited with code $($ownedProcess.ExitCode) before readiness; logs are retained in $diagnosticsRoot."
            }
            $listeners = @(Get-PortListenerPid -Port $port)
            if ($listeners.Count -gt 0) {
                $ownedProcessSnapshots = @(Update-RunProfileObservedSnapshots -RootProcessId $ownedProcess.Id -CurrentSnapshots $ownedProcessSnapshots)
                $liveOwnedSnapshots = @(Get-RunProfileLiveSnapshots -Snapshots $ownedProcessSnapshots)
                $liveOwnedIds = @($liveOwnedSnapshots | ForEach-Object { $_.Id } | Select-Object -Unique)
                $foreignListeners = @($listeners | Where-Object { $_ -notin $liveOwnedIds })
                if ($foreignListeners.Count -gt 0) {
                    throw "Port $port answered through a process outside the owned dotnet run tree (pid(s) $($foreignListeners -join ', ')); no listener was stopped."
                }
                $readiness = Get-RunProfileReadiness -CurlPath $curl.Source
                $lastCurlExitCode = $readiness.ExitCode
                if ($readiness.ExitCode -eq 0 -and $readiness.Status -ceq 'ready') {
                    $ownedProcessSnapshots = @(Update-RunProfileObservedSnapshots -RootProcessId $ownedProcess.Id -CurrentSnapshots $ownedProcessSnapshots)
                    $readyListeners = @(Get-PortListenerPid -Port $port)
                    $liveOwnedSnapshots = @(Get-RunProfileLiveSnapshots -Snapshots $ownedProcessSnapshots)
                    $liveOwnedIds = @($liveOwnedSnapshots | ForEach-Object { $_.Id } | Select-Object -Unique)
                    if ($readyListeners.Count -ne 1) {
                        throw "Expected one listener on port $port after readiness; found $($readyListeners.Count)."
                    }
                    if ($readyListeners[0] -notin $liveOwnedIds) {
                        throw "Expected one listener still owned by a recorded live process on port $port after readiness; found $($readyListeners.Count)."
                    }
                    $ready = $true
                    $ownedListenerSnapshots = @($liveOwnedSnapshots | Where-Object { $_.Id -eq $readyListeners[0] })
                    Write-Host "[readiness] curl exit code 0; response status=ready; owned listener pid=$($readyListeners[0]) on port $port."
                    break
                }
            }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { throw "Owned dotnet run did not report readiness on port $port within 300 seconds (last curl exit code: $lastCurlExitCode)." }

        $smokeExitCode = Invoke-WorkflowSmoke -SmokeLog $smokeLog
        if ($smokeExitCode -ne 0) { throw "The canonical workflow smoke exited with code $smokeExitCode; logs are retained in $diagnosticsRoot." }
        Write-Host '[smoke] exit code 0; the strict workflow verdict requires one completed write-root WriteLine and zero reported/returned incidents.'

        if (-not (Test-Path -LiteralPath $serverOutputLog -PathType Leaf)) { throw 'The owned dotnet run stdout log was not created.' }
        $serverOutput = Get-Content -LiteralPath $serverOutputLog -Raw
        $serverLines = if ($null -eq $serverOutput) { @() } else { @([regex]::Split($serverOutput, "`r`n|`n|`r")) }
        $expectedOutput = 'Hello World from the single-activity flow!'
        if ($serverLines -cnotcontains $expectedOutput) { throw 'The exact WriteLine text was not found as a complete line in the owned dotnet run stdout log.' }
        Write-Host "[server stdout] exact WriteLine line observed: $expectedOutput"
    }
} catch {
    $journeyError = $_.Exception.Message
} finally {
    if ($ownedProcess) {
        try { $ownedProcessSnapshots = @(Update-RunProfileObservedSnapshots -RootProcessId $ownedProcess.Id -CurrentSnapshots $ownedProcessSnapshots) }
        catch { $cleanupFailures += "Could not refresh observed owned processes before cleanup: $($_.Exception.Message)" }
        if ($ownedProcessSnapshots.Count -eq 0) {
            $cleanupFailures += "No original process identity was recorded for dotnet run pid $($ownedProcess.Id); no process was stopped by PID alone."
        }
        $snapshots = @($ownedProcessSnapshots + $ownedListenerSnapshots | Sort-Object Id, StartTimeTicks -Unique)
        if ($snapshots.Count -gt 0) {
            $rootSnapshot = @($ownedProcessSnapshots | Where-Object { $_.Id -eq $ownedProcess.Id } | Select-Object -First 1)
            if ($rootSnapshot.Count -eq 0) {
                $cleanupFailures += "Could not identify the recorded root process $($ownedProcess.Id) for owned cleanup."
            } else {
                $cleanupFailures += @(Stop-RunProfileProcessTree -RootSnapshot $rootSnapshot[0] -Snapshots $snapshots)
            }
        }
    }

    try { Assert-RunProfilePortReleased }
    catch { $cleanupFailures += $_.Exception.Message }

    if ($diagnosticsRoot) {
        if ($null -eq $journeyError -and $cleanupFailures.Count -eq 0) {
            try {
                Remove-Item -LiteralPath $diagnosticsRoot -Recurse -Force
                Write-Host "[cleanup] stopped recorded process tree and released port $port; removed only the owned diagnostics directory."
            } catch { $cleanupFailures += "Could not remove owned diagnostics directory '$diagnosticsRoot': $($_.Exception.Message)" }
        }
        if ($journeyError -or $cleanupFailures.Count -gt 0) {
            Write-Host "[cleanup] diagnostics retained at $diagnosticsRoot" -ForegroundColor Yellow
            Write-RunProfileFailureDiagnostics -SmokeLog $smokeLog -ServerOutputLog $serverOutputLog -ServerErrorLog $serverErrorLog
        }
    }
}

if ($journeyError -or $cleanupFailures.Count -gt 0) {
    $failures = @()
    if ($journeyError) { $failures += "Journey failure: $journeyError" }
    foreach ($failure in $cleanupFailures) { $failures += "Cleanup failure: $failure" }
    if ($diagnosticsRoot) { $failures += "Diagnostics retained at: $diagnosticsRoot" }
    Add-RunProfileSummary -Lines (@('### Documented run-profile journey: FAILED') + $failures)
    throw ($failures -join [Environment]::NewLine)
}

if ($InvalidCommandControl) {
    if ($null -eq $failureControlExitCode -or $failureControlExitCode -eq 0) { throw 'The invalid run-profile control did not produce its expected nonzero exit code.' }
    Add-RunProfileSummary -Lines @(
        '### dotnet run failure-propagation control: PASSED',
        '- A run against a nonexistent project returned a nonzero exit code and left the fixed port free.'
    )
    Write-Host "RUN_PROFILE_FAILURE_PROPAGATED:$failureControlExitCode"
    exit $failureControlExitCode
}

Add-RunProfileSummary -Lines @(
    '### Documented run-profile journey: PASSED',
    '- Replayed `dotnet run --no-build --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http` from the repository root on the fixed documented port 5095.',
    '- A bounded curl readiness request reported `ready`; the canonical REST smoke exited 0 with the strict one-WriteLine/zero-incident verdict and exact stdout line.',
    '- The wrapper stopped only the recorded dotnet run process tree, verified recorded process exit and port release, and removed only its temporary diagnostics directory.',
    '- This is automated command replay on a hosted runner, not a fresh bare-OS installation, an interactive Ctrl+C session, or a human newcomer trial.'
)
