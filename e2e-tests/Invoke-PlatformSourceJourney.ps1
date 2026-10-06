<#
.SYNOPSIS
    Verify the documented one-WriteLine backend source journey on a hosted runner.
.DESCRIPTION
    -PreflightOnly records the runner/toolchain and verifies isolated cache paths before restore.
    The normal path launches the already-built Workbench through _ServerLifecycle.ps1, runs the canonical
    REST smoke in a child PowerShell process, verifies its server-console output, and explicitly checks
    owned-process exit and port release before deleting its isolated content root.
#>
[CmdletBinding()]
param(
    [switch] $PreflightOnly,
    [string] $BaseUrl,
    [string] $Message = 'Onboarding: Hosted source journey'
)

$ErrorActionPreference = 'Stop'

function Add-PlatformSourceSummary {
    param([Parameter(Mandatory)][string[]] $Lines)
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) { return }
    $content = ($Lines -join [Environment]::NewLine) + [Environment]::NewLine
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $content, $utf8WithoutBom)
}

function Initialize-PlatformSourceJobPaths {
    if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID)) { return }

    $basePath = Join-Path $env:RUNNER_TEMP ("elsa-platform-{0}-{1}" -f $env:GITHUB_RUN_ID, $env:GITHUB_RUN_ATTEMPT)
    $paths = @{
        NUGET_PACKAGES = Join-Path $basePath 'packages'
        NUGET_HTTP_CACHE_PATH = Join-Path $basePath 'http-cache'
        DOTNET_CLI_HOME = Join-Path $basePath 'cli-home'
    }
    foreach ($name in $paths.Keys) { Set-Item -Path "Env:$name" -Value $paths[$name] }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
        $lines = @($paths.Keys | Sort-Object | ForEach-Object { "$_=$($paths[$_])" })
        $content = ($lines -join [Environment]::NewLine) + [Environment]::NewLine
        $utf8WithoutBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
        [IO.File]::AppendAllText($env:GITHUB_ENV, $content, $utf8WithoutBom)
    }
}

function Assert-PlatformSourceCheckoutClean {
    param([Parameter(Mandatory)][string] $Phase, [Parameter(Mandatory)][string] $RepoRoot)

    & git -C $RepoRoot diff --exit-code --quiet --
    $workingExitCode = $LASTEXITCODE
    if ($workingExitCode -ne 0) {
        throw "The tracked working tree is not clean ${Phase}; inspect git status before running this journey."
    }

    & git -C $RepoRoot diff --cached --exit-code --quiet --
    $stagedExitCode = $LASTEXITCODE
    if ($stagedExitCode -ne 0) {
        throw "The staged tree is not clean ${Phase}; inspect git status before running this journey."
    }

    $status = @(& git -C $RepoRoot status --porcelain --untracked-files=all 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Could not read repository status ${Phase}: $($status -join ' ')" }
    if ($status.Count -gt 0) {
        throw "The repository checkout is not clean ${Phase}: $($status -join '; ')"
    }
}

function Get-PlatformSourceMetadata {
    param([switch] $CheckFreshJobPaths)

    $windowsRunner = $env:OS -eq 'Windows_NT'
    if ($env:RUNNER_OS -eq 'Windows' -and -not $windowsRunner) { throw 'The Windows runner did not start Windows PowerShell.' }
    if ($env:RUNNER_OS -eq 'Linux' -and $windowsRunner) { throw 'The Linux runner unexpectedly started Windows.' }

    if ($windowsRunner) {
        if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
            throw "Windows must run Windows PowerShell Desktop 5.1; got $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)."
        }
        if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
            throw 'Windows PowerShell prerequisite Get-NetTCPConnection is unavailable; no host tools will be installed.'
        }
    } else {
        if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
            throw "Linux must run PowerShell 7 or later; got $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)."
        }
        if (-not (Get-Command lsof -ErrorAction SilentlyContinue)) {
            throw 'Linux prerequisite lsof is unavailable; no host tools will be installed.'
        }
    }

    if ($CheckFreshJobPaths) {
        foreach ($name in @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'DOTNET_CLI_HOME')) {
            $path = [Environment]::GetEnvironmentVariable($name, 'Process')
            if ([string]::IsNullOrWhiteSpace($path)) { throw "The job did not set isolated $name." }
            if (Test-Path -LiteralPath $path) {
                $existing = Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1
                if ($null -ne $existing) { throw "The job-scoped $name path is not empty before restore: $path" }
            } else {
                New-Item -Path $path -ItemType Directory -Force | Out-Null
            }
        }
    }

    $sdk = (& dotnet --version 2>&1 | Out-String).Trim()
    $dotnetExitCode = $LASTEXITCODE
    if ($dotnetExitCode -ne 0) { throw "dotnet --version failed with exit code ${dotnetExitCode}: $sdk" }
    if ($sdk -notmatch '^10\.') { throw "The hosted source journey expects the existing .NET 10.x CI line; selected SDK was '$sdk'." }

    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $revision = (& git -C $repoRoot rev-parse HEAD 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') {
        throw "Could not record the exact source revision: $revision"
    }

    $image = if ($env:ImageOS -and $env:ImageVersion) { "$env:ImageOS $env:ImageVersion" } else { 'local runner (image metadata unavailable)' }
    return @{
        IsWindows = $windowsRunner
        Sdk = $sdk
        Revision = $revision
        Image = $image
        PowerShell = "$($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)"
        RepoRoot = $repoRoot
    }
}

if ($PreflightOnly) { Initialize-PlatformSourceJobPaths }
$metadata = Get-PlatformSourceMetadata -CheckFreshJobPaths:$PreflightOnly
Write-Host "Runner image: $($metadata.Image)"
Write-Host "Source revision: $($metadata.Revision)"
Write-Host "Selected .NET SDK: $($metadata.Sdk)"
Write-Host "PowerShell: $($metadata.PowerShell)"

if ($PreflightOnly) {
    Assert-PlatformSourceCheckoutClean -Phase 'before restore/build/test' -RepoRoot $metadata.RepoRoot
    dotnet --info
    if ($LASTEXITCODE -ne 0) { throw "dotnet --info failed with exit code $LASTEXITCODE." }
    Add-PlatformSourceSummary -Lines @(
        '## Backend source platform journey',
        "- Runner image: $($metadata.Image)",
        "- Source revision: ``$($metadata.Revision)``",
        "- Selected SDK: ``$($metadata.Sdk)``; CI selects the existing .NET ``10.x`` line.",
        "- PowerShell: ``$($metadata.PowerShell)``.",
        '- Before restore, job-scoped NuGet package, NuGet HTTP, and CLI-home paths were empty; no package cache action is used.',
        '- The hosted VM still includes runner-provided SDK packs; this is not a freshly installed bare OS.',
        '- The matrix uses locked restore followed by no-restore build/test, then the owned-DLL REST journey; it does not literally replay the guide build/run/Ctrl+C commands or temporary source edit.',
        '- This automated result does not claim Windows human acceptance, a fresh bare-OS install, or ongoing .NET support policy.'
    )
    return
}

if ($Message.Contains("`r") -or $Message.Contains("`n")) { throw 'The WriteLine message must be a single line for exact stdout verification.' }

. "$PSScriptRoot/_ServerLifecycle.ps1"

$journeyError = $null
$cleanupFailures = @()
$contentRoot = $null
$ownedProcess = $null
$port = $null
$smokeLog = $null

try {
    if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
        $BaseUrl = Resolve-ElsaServerBaseUrl
    } else {
        $BaseUrl = Resolve-ElsaServerBaseUrl -BaseUrl $BaseUrl
    }
    $port = Get-ElsaServerPort -BaseUrl $BaseUrl
    $contentRoot = New-ElsaContentRoot -Prefix 'elsa-platform-source'
    $smokeLog = Join-Path $contentRoot 'workflow-smoke.log'

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        $outputLine = "diagnostics_path=$contentRoot$([Environment]::NewLine)"
        $utf8WithoutBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
        [IO.File]::AppendAllText($env:GITHUB_OUTPUT, $outputLine, $utf8WithoutBom)
    }

    Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $contentRoot
    $owned = $script:OwnedElsaServer
    if (-not $owned) { throw 'The lifecycle helper returned without an owned Workbench process.' }
    $ownedProcess = $owned.Process
    $listeners = @(Get-PortListenerPid -Port $port)
    if ($listeners.Count -ne 1 -or $ownedProcess.Id -notin $listeners) {
        throw "Expected only owned Workbench pid $($ownedProcess.Id) to listen on port $port; found pids $($listeners -join ', ')."
    }

    $smokePath = Join-Path $PSScriptRoot 'Test-WorkflowFlow.ps1'
    $childPowerShell = if ($metadata.IsWindows) { 'powershell' } else { 'pwsh' }
    if (-not (Get-Command $childPowerShell -CommandType Application -ErrorAction SilentlyContinue)) {
        throw "Required child PowerShell executable '$childPowerShell' is unavailable."
    }
    $savedErrorActionPreference = $ErrorActionPreference
    if ($metadata.IsWindows) {
        try {
            # Windows PowerShell 5.1 can promote redirected native stderr under Stop.
            $ErrorActionPreference = 'Continue'
            powershell -NoProfile -ExecutionPolicy Bypass -File $smokePath -BaseUrl $BaseUrl -Message $Message *> $smokeLog
            $smokeExitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $savedErrorActionPreference
        }
    } else {
        try {
            $ErrorActionPreference = 'Continue'
            pwsh -NoProfile -File $smokePath -BaseUrl $BaseUrl -Message $Message *> $smokeLog
            $smokeExitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $savedErrorActionPreference
        }
    }
    if (Test-Path -LiteralPath $smokeLog) { Get-Content -LiteralPath $smokeLog | ForEach-Object { Write-Host $_ } }
    if ($smokeExitCode -ne 0) { throw "The canonical Test-WorkflowFlow.ps1 child failed with exit code $smokeExitCode." }

    $serverLogs = @(Get-ChildItem -LiteralPath $contentRoot -Filter 'server-*.out.log' -File)
    if ($serverLogs.Count -ne 1) { throw "Expected one owned Workbench stdout log; found $($serverLogs.Count)." }
    $serverOutput = Get-Content -LiteralPath $serverLogs[0].FullName -Raw
    $serverLines = if ($null -eq $serverOutput) { @() } else { @([regex]::Split($serverOutput, "`r`n|`n|`r")) }
    if ($serverLines -cnotcontains $Message) {
        throw "The workflow completed but its exact WriteLine message was not found in owned server stdout ($($serverLogs[0].Name))."
    }
    Write-Host "  [server stdout] observed exact WriteLine message: $Message"
} catch {
    $journeyError = $_
} finally {
    if (-not $ownedProcess -and $script:OwnedElsaServer) { $ownedProcess = $script:OwnedElsaServer.Process }
    if ($script:OwnedElsaServer) {
        try { Stop-OwnedElsaServer } catch { $cleanupFailures += "Stop-OwnedElsaServer failed: $($_.Exception.Message)" }
    }

    if ($ownedProcess) {
        try {
            $ownedProcess.Refresh()
            if (-not $ownedProcess.HasExited) {
                $cleanupFailures += "Owned Workbench pid $($ownedProcess.Id) is still running after Stop-OwnedElsaServer."
            }
        } catch {
            $cleanupFailures += "Could not verify owned Workbench pid exit: $($_.Exception.Message)"
        }
    }

    if ($null -ne $port) {
        try {
            $remainingListeners = @(Get-PortListenerPid -Port $port)
            if ($remainingListeners.Count -ne 0) {
                $cleanupFailures += "Port $port remains occupied by pid(s) $($remainingListeners -join ', '); no listener was stopped."
            }
        } catch {
            $cleanupFailures += "Could not verify release of port ${port}: $($_.Exception.Message)"
        }
    }

    try { Assert-PlatformSourceCheckoutClean -Phase 'after restore/build/test/host journey' -RepoRoot $metadata.RepoRoot }
    catch { $cleanupFailures += $_.Exception.Message }

    if ($contentRoot) {
        if ($null -eq $journeyError -and $cleanupFailures.Count -eq 0) {
            try {
                Remove-Item -LiteralPath $contentRoot -Recurse -Force
                Write-Host "  [cleanup] stopped owned pid, released port $port, and removed only $contentRoot"
            } catch {
                $cleanupFailures += "Could not remove owned content root '$contentRoot': $($_.Exception.Message)"
            }
        }
        if ($journeyError -or $cleanupFailures.Count -gt 0) {
            Write-Host "  [cleanup] retained diagnostics in owned content root: $contentRoot" -ForegroundColor Yellow
            foreach ($path in @(Get-ChildItem -LiteralPath $contentRoot -File -ErrorAction SilentlyContinue)) {
                if ($path.Name -like 'server-*.log' -or $path.Name -eq 'workflow-smoke.log') {
                    Write-Host "--- $($path.Name) (last 80 lines) ---" -ForegroundColor Yellow
                    Get-Content -LiteralPath $path.FullName -Tail 80 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
                }
            }
        }
    }
}

if ($journeyError -or $cleanupFailures.Count -gt 0) {
    $failures = @()
    if ($journeyError) { $failures += "Journey failure: $($journeyError.Exception.Message)" }
    foreach ($cleanupFailure in $cleanupFailures) { $failures += "Cleanup failure: $cleanupFailure" }
    if ($contentRoot) { $failures += "Diagnostics retained at: $contentRoot" }
    Add-PlatformSourceSummary -Lines (@('### Backend source journey: FAILED') + $failures)
    throw ($failures -join [Environment]::NewLine)
}

Add-PlatformSourceSummary -Lines @(
    '### Backend source journey: PASSED',
    "- Exact revision ``$($metadata.Revision)`` completed the single-WriteLine REST flow; the exact message was observed in the owned Workbench stdout.",
    '- The harness stopped its owned PID, verified process exit and port release, and removed only its isolated content root.',
    '- Repository tracked/untracked status was unchanged by the journey.',
    "- This automated DLL-host path is separate from the contributor guide's literal `dotnet run` / Ctrl+C flow and does not claim a temporary source-edit exercise or human acceptance."
)
