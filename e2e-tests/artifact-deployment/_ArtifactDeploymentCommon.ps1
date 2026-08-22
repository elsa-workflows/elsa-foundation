<#
.SYNOPSIS
    Shared helpers for the executable-artifact deployment suite (spec 151).
.DESCRIPTION
    Adds on top of _ElsaCommon.ps1, mirroring file-deployment/_FileDeploymentCommon.ps1 (spec 147):
    a server lifecycle that composes JsonWorkflowArtifactReconciliation via environment variables
    (env vars layer above shells.json, so no repo file is edited), plus one thing the design-side
    suite does not need — a SECOND, empty Groundwork database for the importing engine.

    The second database is the point. Spec 151's claim is that an artifact built on one engine runs
    on another that never saw the source definitions; importing back into the same store the
    workflow was published from proves nothing, because the artifact is already there and the
    activation slot is already owned by the publish source (an idempotent no-op). So the suite
    exports from the developer's normal database, then restarts the server against a freshly
    schema-deployed temp database whose design catalog has never held these definitions.

    Lifecycle follows the durability/file-deployment precedent: the already-built Elsa.Workbench.dll
    is launched directly, not via `dotnet run`.
#>

. "$PSScriptRoot/../_ElsaCommon.ps1"

$script:RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$script:ServerProject = (Resolve-Path "$PSScriptRoot/../../src/Apps/Elsa.Workbench/Elsa.Workbench.csproj").Path
$script:ServerProjectDir = Split-Path $script:ServerProject
$script:ServerDll = Join-Path $script:ServerProjectDir "bin/Debug/net10.0/Elsa.Workbench.dll"

# --- importing-engine database -------------------------------------------------

# Deploy the reference-composition schema into a brand new SQLite file and return its path. Same
# manifest assembly/type the from-source setup in ../README.md uses, so the importing engine's store
# is shaped exactly like the developer's — only empty.
function New-ImportEngineDatabase {
    param([Parameter(Mandatory)][string] $DatabasePath)

    $manifestAssembly = Join-Path $script:ServerProjectDir "bin/Debug/net10.0/Elsa.Persistence.Groundwork.ReferenceComposition.dll"
    if (-not (Test-Path $manifestAssembly)) { throw "Groundwork manifest assembly not found at $manifestAssembly - build the server first (dotnet build)." }

    $log = Join-Path ([System.IO.Path]::GetTempPath()) "elsa-artifactdeploy-groundwork.log"
    Write-Host "  [db] deploying the Groundwork schema into $DatabasePath ..."
    # The tool manifest lives at the repo root, so `dotnet tool run` must be invoked from there.
    # Output is a ~500 KB JSON report; it goes to a file and only the outcome is asserted here.
    Push-Location $script:RepoRoot
    try {
        & dotnet tool run groundwork -- apply `
            --manifest-assembly $manifestAssembly `
            --manifest-type "Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithDiagnosticsDeploymentSchema" `
            --provider sqlite `
            --connection "Data Source=$DatabasePath" `
            --output json `
            --safe *> $log
        $exit = $LASTEXITCODE
    }
    finally { Pop-Location }

    if ($exit -ne 0) { throw "Groundwork schema deployment failed (exit $exit); see $log" }
    Write-Host "  [db] schema applied (report: $log)"
    return $DatabasePath
}

# --- server process lifecycle (file-deployment pattern + artifact feature env vars) --------------

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

# Environment variable names composing the feature and redirecting the store (env vars sit above
# shells.json in precedence; presence of the section enables the feature).
$script:FeatureEnvVars = @(
    'CShells__Shells__default__Features__JsonWorkflowArtifactReconciliation__Options__SourceId',
    'CShells__Shells__default__Features__JsonWorkflowArtifactReconciliation__Options__FolderPath',
    'CShells__Shells__default__Features__GroundworkUnifiedPersistenceSqlite__ConnectionString'
)

function Clear-ArtifactDeploymentComposition {
    foreach ($name in $script:FeatureEnvVars) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
}

$script:ServerOutLog = Join-Path ([System.IO.Path]::GetTempPath()) "elsa-artifactdeploy-server.out.log"
$script:ServerErrLog = Join-Path ([System.IO.Path]::GetTempPath()) "elsa-artifactdeploy-server.err.log"

# Every start gets its own log pair. The reconcile pass happens once, at activation, and is the only
# place the import's own diagnostics are written - a shared log file would be overwritten by the next
# phase's server and the evidence for a failed import would be gone by the time the suite reported it.
function Set-ServerLogLabel {
    param([Parameter(Mandatory)][string] $Label)
    $script:ServerOutLog = Join-Path ([System.IO.Path]::GetTempPath()) "elsa-artifactdeploy-$Label.out.log"
    $script:ServerErrLog = Join-Path ([System.IO.Path]::GetTempPath()) "elsa-artifactdeploy-$Label.err.log"
}

# Start the server. With -SourceId/-FolderPath/-DatabasePath it becomes the IMPORTING engine:
# JsonWorkflowArtifactReconciliation composed over the mount, backed by the empty temp database.
# With -Plain it is the developer's normal server again (no feature, stock connection string).
function Start-ElsaServer {
    param(
        [int] $Port = 5095,
        [string] $SourceId,
        [string] $FolderPath,
        [string] $DatabasePath,
        [string] $LogLabel,
        [switch] $Plain,
        [int] $TimeoutSec = 120
    )
    if (-not (Test-Path $script:ServerDll)) { throw "Server DLL not found at $script:ServerDll - build the server first (dotnet build)." }
    if ($LogLabel) { Set-ServerLogLabel -Label $LogLabel }

    Clear-ArtifactDeploymentComposition
    if (-not $Plain) {
        $env:CShells__Shells__default__Features__JsonWorkflowArtifactReconciliation__Options__SourceId = $SourceId
        $env:CShells__Shells__default__Features__JsonWorkflowArtifactReconciliation__Options__FolderPath = $FolderPath
        $env:CShells__Shells__default__Features__GroundworkUnifiedPersistenceSqlite__ConnectionString = "Data Source=$DatabasePath"
        Write-Host "  [server] composing JsonWorkflowArtifactReconciliation (SourceId=$SourceId, FolderPath=$FolderPath)"
        Write-Host "  [server] importing-engine store: $DatabasePath"
    }

    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    Write-Host "  [server] starting: dotnet <Elsa.Workbench.dll> (ASPNETCORE_URLS=$env:ASPNETCORE_URLS)"
    $start = @{
        FilePath = 'dotnet'
        ArgumentList = $script:ServerDll
        WorkingDirectory = $script:ServerProjectDir
        RedirectStandardOutput = $script:ServerOutLog
        RedirectStandardError = $script:ServerErrLog
    }
    if ($env:OS -eq 'Windows_NT') { $start.WindowStyle = 'Hidden' }
    Start-Process @start | Out-Null
    # Env vars are inherited by the child at spawn; clear them from this session immediately so a
    # later plain start (or the developer's own shell use) is not silently composed.
    Clear-ArtifactDeploymentComposition

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try { $probe = Invoke-WebRequest "http://localhost:$Port/health/live" -TimeoutSec 3 -UseBasicParsing
              if ($probe.StatusCode -eq 200) { return $true } } catch {}
        Start-Sleep -Seconds 1
    }
    throw "Elsa.Workbench did not start listening on port $Port within ${TimeoutSec}s (logs: $script:ServerOutLog / $script:ServerErrLog)"
}

# The deployment gate: /health/ready turns 200 only after shell activation, which includes the
# artifact reconcile pass. '/' is NOT a gate - it 500s while the shell is still activating.
# Fatal patterns in the server log. A shell that fails activation keeps answering 503 forever, so
# polling alone cannot distinguish "still starting" from "will never start" - it just burns the whole
# timeout, once per server start. These are the shapes that mean the wait is pointless.
$script:FatalStartupPatterns = @(
    'GroundworkRuntimeSchemaAdmissionException',
    'GroundworkStorageCompositionException',
    'FeatureNotFoundException',
    'GW-SCHEMA-\d+',
    'GW-PHYSICAL-\d+',
    'Unable to resolve service for type',
    'fail: Elsa\.Workbench\.Readiness\.DefaultShellWarmup'
)

function Get-FatalStartupReason {
    if (-not (Test-Path $script:ServerOutLog)) { return $null }
    foreach ($pattern in $script:FatalStartupPatterns) {
        $hit = Get-Content $script:ServerOutLog -ErrorAction SilentlyContinue |
            Select-String -Pattern $pattern | Select-Object -First 1
        if ($hit) { return $hit.Line.Trim() }
    }
    return $null
}

function Wait-ElsaReady {
    param([string] $BaseUrl = "http://localhost:5095", [int] $TimeoutSec = 180, [int] $Port = 5095)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $last = $null
    while ((Get-Date) -lt $deadline) {
        # Fail fast on a dead process. Waiting for readiness from a server that has exited is the
        # single most expensive way this suite can waste time.
        if (-not (Get-ServerPid -Port $Port)) {
            Show-ServerLogTail
            throw "The server is no longer listening on port ${Port}; it exited during startup. See the log tail above."
        }

        # Fail fast on a fatal already recorded in the log. Startup faults are terminal: the shell does
        # not retry activation, so every further poll is guaranteed to return 503.
        $fatal = Get-FatalStartupReason
        if ($fatal) {
            Show-ServerLogTail
            throw "Shell startup failed and will not recover: $fatal"
        }

        try {
            $r = Invoke-WebRequest "$BaseUrl/health/ready" -TimeoutSec 5 -UseBasicParsing
            if ($r.StatusCode -eq 200) { Write-Host "  [ready] /health/ready = 200"; return $true }
            $last = $r.StatusCode
        } catch {
            $last = "unreachable"
            try { $last = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch {}
            # A failed reconcile pass fails shell activation outright; there is nothing to wait for.
            if ($last -match 'shell_activation_failed') {
                Show-ServerLogTail
                throw "/health/ready reports a failed shell activation: $last"
            }
        }
        Start-Sleep -Seconds 2
    }
    Show-ServerLogTail
    throw "/health/ready did not turn 200 within ${TimeoutSec}s (last: $last) - check the server logs."
}

# Print what the reconcile pass itself said. This is the suite's primary evidence: the import happens
# before readiness and leaves no HTTP trace, so a silent failure is only visible here.
function Show-ReconcileDiagnostics {
    if (-not (Test-Path $script:ServerOutLog)) { return }
    $pattern = 'JsonWorkflowArtifactReconciliationSource|WorkflowArtifactReconciler|WorkflowArtifactReconcilerStartupTask|closure|was not imported'
    $lines = @(Get-Content $script:ServerOutLog | Select-String -Pattern $pattern -CaseSensitive:$false |
        Where-Object { $_.Line -notmatch 'feature catalog generation|Registering endpoint' -and $_.Line.Trim() -notmatch '^(at |--- End of)' } |
        Select-Object -First 12 -ExpandProperty Line)
    if ($lines.Count -eq 0) {
        Write-Host "  [reconcile] the pass logged nothing - the feature may not be composed" -ForegroundColor Yellow
        return
    }
    Write-Host "  [reconcile] pass diagnostics:" -ForegroundColor DarkGray
    foreach ($line in $lines) { Write-Host ("  [reconcile] " + $line.Trim()) -ForegroundColor DarkGray }
}

function Show-ServerLogTail {
    param([int] $Lines = 40)
    foreach ($log in @($script:ServerErrLog, $script:ServerOutLog)) {
        if (Test-Path $log) {
            Write-Host "  [log] --- tail of $log ---" -ForegroundColor DarkGray
            Get-Content $log -Tail $Lines | ForEach-Object { Write-Host "  [log] $_" -ForegroundColor DarkGray }
        }
    }
}

# --- assertion tally ------------------------------------------------------------

$script:APass = 0; $script:ATotal = 0

function Assert-That {
    param([Parameter(Mandatory)][string] $Label, [Parameter(Mandatory)][bool] $Condition, [string] $Detail = "")
    $script:ATotal++
    if ($Condition) {
        $script:APass++
        Write-Host ("  OK   {0}" -f $Label) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL {0}  {1}" -f $Label, $Detail) -ForegroundColor Red
    }
}

function Complete-ArtifactDeploymentSuite {
    param([string] $Area = "executable-artifact deployment")
    Write-Host ""
    if ($script:APass -eq $script:ATotal) {
        Write-Host ("== {0}: {1}/{2} passed ==" -f $Area, $script:APass, $script:ATotal) -ForegroundColor Green
        exit 0
    }
    Write-Host ("== {0}: {1}/{2} passed ==" -f $Area, $script:APass, $script:ATotal) -ForegroundColor Red
    exit 1
}
