<#
.SYNOPSIS
    Shared helpers for the durability test scripts (checkpoint survival, restart recovery, variable persistence).
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds:
      - a mid-flow Event wait node + a ResumeOnly stimulus dispatcher (resume delivery needs a non-empty input, #1014);
      - server process lifecycle control (stop / start / restart) so a test can KILL the running Elsa.Workbench mid-suspend
        and relaunch it against the same on-disk SQLite database, proving durable checkpoint recovery end-to-end.

    The reference server persists to SQLite on disk (GroundworkUnifiedPersistenceSqlite) and composes
    WorkflowsRuntimeResumption + WorkflowsRuntimeCheckpointPersistence (Coalesced/50), so a suspended instance
    survives a real process restart and the resumption pump re-drives it. These are the behaviors the in-process
    C# crash tests stub out (in-memory store + hand-driven sweep); only an e2e restart exercises the real substrate.

    Server restart uses `dotnet run --no-build` (the server must already be built) against the same content root,
    so the SQLite files under src/Apps/Elsa.Workbench/ are reused. After a restart, re-authenticate (Connect-Elsa):
    the identity cookie does not necessarily survive a fresh process.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

$script:RepoRoot      = (Resolve-Path "$PSScriptRoot/../..").Path
$script:ServerProject = Join-Path $script:RepoRoot "src/Apps/Elsa.Workbench/Elsa.Workbench.csproj"

# StimulusHash = "sha256:" + lowercase-hex(SHA256(utf8(eventName))).
function Get-EventHash([string] $Name) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hex = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Name)) | ForEach-Object { $_.ToString('x2') }) -join ''
    return "sha256:$hex"
}

# Resume every waiter on the named event. A non-empty input is REQUIRED for delivery (#1014).
function Invoke-ResumeStimulus {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $EventName, $InputObject = @{ resumed = $true })
    $body = @{ stimulusType = "Event"; stimulusHash = (Get-EventHash $EventName); mode = "ResumeOnly"; input = $InputObject } | ConvertTo-Json
    Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/stimuli" -Method Post -WebSession $Ctx.Session -ContentType 'application/json' -Body $body
}

# A mid-flow Event (wait) node: CanStartWorkflow=false suspends until the named event.
function New-EventWaitNode {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $EventVersionId, [Parameter(Mandatory)][string] $EventName)
    New-ActivityNode -NodeId $NodeId -VersionId $EventVersionId -Inputs @(
        (New-LiteralInput -ReferenceKey "EventName" -Value $EventName),
        (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $false)
    )
}

# True if the named node reached Suspended.
function Test-NodeSuspended {
    param([Parameter(Mandatory)] $Instance, [string] $NodeId = 'wait')
    [bool]($Instance.activities | Where-Object { $_.executableNodeId -eq $NodeId -and $_.status -eq 'Suspended' })
}

# --- server process lifecycle -------------------------------------------------

function Get-ServerPid { param([int] $Port = 5095)
    if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        return (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess
    }

    if (Get-Command lsof -ErrorAction SilentlyContinue) {
        return (& lsof '-nP' "-iTCP:$Port" '-sTCP:LISTEN' '-t' 2>$null | Select-Object -First 1)
    }

    throw "Cannot identify the process listening on port ${Port}: neither Get-NetTCPConnection nor lsof is available."
}

function Stop-ElsaServer {
    param([int] $Port = 5095, [int] $TimeoutSec = 20)
    $serverPid = Get-ServerPid -Port $Port
    if (-not $serverPid) { Write-Host "  [server] nothing listening on $Port"; return }
    Write-Host "  [server] stopping pid $serverPid on port $Port ..."
    Stop-Process -Id $serverPid -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-ServerPid -Port $Port) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    Start-Sleep -Milliseconds 800   # let SQLite file handles release
    Write-Host "  [server] stopped (port free = $([bool](-not (Get-ServerPid -Port $Port))))"
}

function Start-ElsaServer {
    # Launches the ALREADY-BUILT server DLL directly (dotnet <dll>) rather than `dotnet run`, which would
    # re-evaluate this large solution's MSBuild graph on every relaunch (minutes). The content root is the
    # project directory (set as the working dir) so the on-disk SQLite files under src/Apps/Elsa.Workbench/ are
    # reused across the restart; ASPNETCORE_URLS + ASPNETCORE_ENVIRONMENT replicate the `http` launch profile.
    param([int] $Port = 5095, [string] $Project = $script:ServerProject, [int] $TimeoutSec = 90)
    $projDir = Split-Path $Project
    $dll = Join-Path $projDir "bin/Debug/net10.0/Elsa.Workbench.dll"
    if (-not (Test-Path $dll)) { throw "Server DLL not found at $dll - build the server first (dotnet build)." }
    $tmp = [System.IO.Path]::GetTempPath()
    $out = Join-Path $tmp "elsa-durability-server.out.log"
    $err = Join-Path $tmp "elsa-durability-server.err.log"
    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    Write-Host "  [server] starting: dotnet <Elsa.Workbench.dll> (ASPNETCORE_URLS=$env:ASPNETCORE_URLS)"
    $start = @{
        FilePath = 'dotnet'
        ArgumentList = $dll
        WorkingDirectory = $projDir
        RedirectStandardOutput = $out
        RedirectStandardError = $err
    }
    if ($env:OS -eq 'Windows_NT') { $start.WindowStyle = 'Hidden' }
    Start-Process @start | Out-Null
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try { if (Invoke-RestMethod "http://localhost:$Port/" -TimeoutSec 3) { Write-Host "  [server] healthy on $Port"; return $true } } catch {}
        Start-Sleep -Seconds 1
    }
    throw "Elsa.Workbench did not become healthy on port $Port within ${TimeoutSec}s (logs: $out / $err)"
}

function Restart-ElsaServer {
    param([int] $Port = 5095, [string] $Project = $script:ServerProject)
    Stop-ElsaServer -Port $Port
    Start-ElsaServer -Port $Port -Project $Project | Out-Null
}
