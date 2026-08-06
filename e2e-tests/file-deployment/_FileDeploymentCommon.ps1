<#
.SYNOPSIS
    Shared helpers for the file-based workflow deployment suite (spec 147).
.DESCRIPTION
    Adds on top of _ElsaCommon.ps1: definition-file authoring (envelope array with a pinned
    definitionId and a resolved actver_* id), and a server lifecycle that composes the
    JsonWorkflowReconciliation feature via environment variables (env vars layer above shells.json,
    so no repo file is edited). Lifecycle functions follow the durability suite's precedent:
    the already-built Elsa.Workbench.dll is launched directly, not via `dotnet run`.
#>

. "$PSScriptRoot/../_ElsaCommon.ps1"

$script:ServerProject = (Resolve-Path "$PSScriptRoot/../../src/Apps/Elsa.Workbench/Elsa.Workbench.csproj").Path

# --- definition-file authoring -------------------------------------------------

# Write one definition file (a single-envelope array) into $Folder. The root activity is a WriteLine
# with a literal text input; $ActivityVersionId must be the resolved actver_* id from the catalog.
function Write-DefinitionFile {
    param(
        [Parameter(Mandatory)][string] $Folder,
        [Parameter(Mandatory)][string] $FileName,
        [Parameter(Mandatory)][string] $DefinitionId,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $ActivityVersionId,
        [string] $Version = "1.0.0"
    )
    $envelope = @(
        @{
            definitionId = $DefinitionId
            name         = $Name
            description  = "e2e file-based deployment (spec 147)"
            version      = $Version
            state        = @{
                variables               = @()
                inputs                  = @()
                outputs                 = @()
                workflowActivityOptions = $null
                strategyOptions         = $null
                rootActivity            = (New-ActivityNode -NodeId "write-line" -VersionId $ActivityVersionId `
                    -Inputs @((New-LiteralInput -ReferenceKey "text" -Value "deployed from file")))
            }
        }
    )
    $path = Join-Path $Folder $FileName
    # ConvertTo-Json without -AsArray (PS 5.1) unwraps single-element arrays; wrap explicitly.
    $json = "[" + (($envelope | ForEach-Object { $_ | ConvertTo-Json -Depth 30 }) -join ",") + "]"
    Set-Content -Path $path -Value $json -Encoding utf8
    Write-Host "  [defs] wrote $path (definitionId=$DefinitionId, name=$Name)"
    return $path
}

# --- server process lifecycle (durability-suite pattern + feature env vars) ----

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
}

# Environment variable names composing the feature (env vars sit above shells.json in precedence;
# presence of the section enables the feature).
$script:FeatureEnvVars = @(
    'CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__SourceId',
    'CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath',
    'CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__PublishOnReconcile'
)

function Clear-FileDeploymentComposition {
    foreach ($name in $script:FeatureEnvVars) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
}

# Start the server with JsonWorkflowReconciliation composed via env vars. Pass -SourceId/-FolderPath;
# omit -FolderPath (with -Plain) to start without the feature (cleanup restore).
function Start-ElsaServer {
    param(
        [int] $Port = 5095,
        [string] $SourceId,
        [string] $FolderPath,
        [switch] $Plain,
        [int] $TimeoutSec = 90
    )
    $projDir = Split-Path $script:ServerProject
    $dll = Join-Path $projDir "bin/Debug/net10.0/Elsa.Workbench.dll"
    if (-not (Test-Path $dll)) { throw "Server DLL not found at $dll - build the server first (dotnet build)." }

    Clear-FileDeploymentComposition
    if (-not $Plain) {
        $env:CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__SourceId = $SourceId
        $env:CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath = $FolderPath
        $env:CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__PublishOnReconcile = "true"
        Write-Host "  [server] composing JsonWorkflowReconciliation (SourceId=$SourceId, FolderPath=$FolderPath, PublishOnReconcile=true)"
    }

    $tmp = [System.IO.Path]::GetTempPath()
    $out = Join-Path $tmp "elsa-filedeploy-server.out.log"
    $err = Join-Path $tmp "elsa-filedeploy-server.err.log"
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
    # Env vars are inherited by the child at spawn; clear them from this session immediately so a
    # later plain start (or the developer's own shell use) is not silently composed.
    Clear-FileDeploymentComposition

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try { if (Invoke-RestMethod "http://localhost:$Port/" -TimeoutSec 3) { return $true } } catch {}
        Start-Sleep -Seconds 1
    }
    throw "Elsa.Workbench did not start listening on port $Port within ${TimeoutSec}s (logs: $out / $err)"
}

# The deployment gate (spec 147 readiness note): /health/ready turns 200 only after shell activation,
# which includes the reconcile pass and (opt-in) publish-on-reconcile. '/' is NOT a gate.
function Wait-ElsaReady {
    param([string] $BaseUrl = "http://localhost:5095", [int] $TimeoutSec = 120)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $last = $null
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest "$BaseUrl/health/ready" -TimeoutSec 5 -UseBasicParsing
            if ($r.StatusCode -eq 200) { Write-Host "  [ready] /health/ready = 200"; return $true }
            $last = $r.StatusCode
        } catch {
            $last = try { [int]$_.Exception.Response.StatusCode } catch { "unreachable" }
        }
        Start-Sleep -Seconds 1
    }
    throw "/health/ready did not turn 200 within ${TimeoutSec}s (last: $last) - a failed reconcile pass fails shell activation; check the server logs."
}

# --- assertion tally ------------------------------------------------------------

$script:FPass = 0; $script:FTotal = 0

function Assert-That {
    param([Parameter(Mandatory)][string] $Label, [Parameter(Mandatory)][bool] $Condition, [string] $Detail = "")
    $script:FTotal++
    if ($Condition) {
        $script:FPass++
        Write-Host ("  OK   {0}" -f $Label) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL {0}  {1}" -f $Label, $Detail) -ForegroundColor Red
    }
}

function Complete-FileDeploymentSuite {
    param([string] $Area = "file-based deployment")
    Write-Host ""
    if ($script:FPass -eq $script:FTotal) {
        Write-Host ("== {0}: {1}/{2} passed ==" -f $Area, $script:FPass, $script:FTotal) -ForegroundColor Green
        exit 0
    }
    Write-Host ("== {0}: {1}/{2} passed ==" -f $Area, $script:FPass, $script:FTotal) -ForegroundColor Red
    exit 1
}
