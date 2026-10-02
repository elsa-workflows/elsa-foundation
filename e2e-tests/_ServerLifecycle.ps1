<#
.SYNOPSIS
    Server process lifecycle for the e2e scripts that restart Elsa.Workbench (dot-sourced).
.DESCRIPTION
    A restart-style script owns the server it restarts: it launches the ALREADY-BUILT Elsa.Workbench.dll itself,
    keeps the process it launched, and stops only that process. Nothing here looks up "whatever listens on the
    port" in order to stop it. If the port is held by a process this script run did not start, the launch fails
    with the port and the pid in the message and that process is left alone (issue #2329: the earlier helpers
    force-killed the listener on a port that defaulted to 5095 independently of -BaseUrl).

    The server location is taken once, as a base URL. The port always derives from it.

    The owned server runs from a fresh temporary content root (copied appsettings + shells.json), so its relative
    SQLite files stay there and it never shares a database with a Workbench the developer runs from the checkout.
    The DLL is launched directly (dotnet <dll>) rather than through `dotnet run`, which would re-evaluate this
    large solution's MSBuild graph on every relaunch (minutes).
#>

$script:WorkbenchProject = (Resolve-Path "$PSScriptRoot/../src/apps/Elsa.Workbench/Elsa.Workbench.csproj").Path
$script:OwnedElsaServer  = $null   # the one launch this script run made: Process, BaseUrl, ContentRoot, Environment
$script:ElsaServerLaunchCount = 0

function Get-FreeLoopbackPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally {
        $listener.Stop()
    }
}

# The port a base URL names (the scheme default when the URL carries none).
function Get-ElsaServerPort {
    param([Parameter(Mandatory)][string] $BaseUrl)
    $uri = $null
    if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref] $uri) -or $uri.Scheme -notin @('http', 'https')) {
        throw "'$BaseUrl' is not an absolute http(s) URL."
    }
    return $uri.Port
}

# Where a restart-style script's server lives. Owned mode without -BaseUrl gets a free loopback port; only
# external mode, which never touches a process, falls back to the documented launch-profile address. An owned
# address is checked here, before the script creates anything, and again by every launch.
function Resolve-ElsaServerBaseUrl {
    param([string] $BaseUrl, [switch] $UseExternalServer, [switch] $RestartServer)
    if ($UseExternalServer -and $RestartServer) {
        throw 'External-server mode cannot restart a process it does not own. Pass -RestartServer:$false.'
    }
    if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
        if ($UseExternalServer) { return 'http://localhost:5095' }
        return "http://127.0.0.1:$(Get-FreeLoopbackPort)"
    }
    $BaseUrl = $BaseUrl.TrimEnd('/')
    if ($UseExternalServer) { Get-ElsaServerPort -BaseUrl $BaseUrl | Out-Null } else { Assert-OwnableAddress -BaseUrl $BaseUrl }
    return $BaseUrl
}

# Ids of the processes listening on a TCP port, on any address. Empty when the port is free. Read-only.
function Get-PortListenerPid {
    param([Parameter(Mandatory)][int] $Port)
    if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        return @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique)
    }
    if (Get-Command lsof -ErrorAction SilentlyContinue) {
        return @(& lsof '-nP' "-iTCP:$Port" '-sTCP:LISTEN' '-t' 2>$null | ForEach-Object { [int]$_ } | Select-Object -Unique)
    }
    throw "Cannot tell whether port ${Port} is free: neither Get-NetTCPConnection nor lsof is available."
}

function Assert-PortFree {
    param([Parameter(Mandatory)][int] $Port)
    $listeners = @(Get-PortListenerPid -Port $Port)
    if ($listeners.Count -eq 0) { return }
    $described = ($listeners | ForEach-Object {
        $name = (Get-Process -Id $_ -ErrorAction SilentlyContinue).ProcessName
        if ($name) { "pid $_ ($name)" } else { "pid $_" }
    }) -join ', '
    throw ("Port $Port is in use by $described, which this script run did not start. Refusing to stop it. " +
        "Stop that process yourself, or pass a -BaseUrl whose port is free (omit -BaseUrl to get a free port).")
}

# An owned server needs a loopback address whose port nothing else holds.
function Assert-OwnableAddress {
    param([Parameter(Mandatory)][string] $BaseUrl)
    $port = Get-ElsaServerPort -BaseUrl $BaseUrl
    if (-not ([Uri] $BaseUrl).IsLoopback) { throw "Cannot start a server for '$BaseUrl': an owned server must use a loopback address." }
    Assert-PortFree -Port $port
}

# A fresh temporary content root holding the committed Workbench configuration.
function New-ElsaContentRoot {
    param([string] $Prefix = 'elsa-e2e')
    $projectDirectory = Split-Path $script:WorkbenchProject
    $contentRoot = Join-Path ([IO.Path]::GetTempPath()) "$Prefix-$([guid]::NewGuid().ToString('N'))"
    New-Item -Path $contentRoot -ItemType Directory -Force | Out-Null
    foreach ($fileName in @('appsettings.json', 'appsettings.Development.json', 'shells.json')) {
        Copy-Item -LiteralPath (Join-Path $projectDirectory $fileName) -Destination $contentRoot
    }
    New-Item -Path (Join-Path $contentRoot 'packages') -ItemType Directory -Force | Out-Null
    return $contentRoot
}

# /health/ready turns 200 only once the default shell is active, which is when the shell's own routes (login
# included) exist. `GET /` answers "Healthy" well before that, so it is not a gate.
function Test-ElsaServerReady {
    param([Parameter(Mandatory)][string] $BaseUrl)
    try {
        return (Invoke-WebRequest "$BaseUrl/health/ready" -TimeoutSec 10 -UseBasicParsing).StatusCode -eq 200
    } catch {
        return $false
    }
}

# Launches the server this script run owns. -Environment adds variables for the child only; a $null value
# removes an inherited one. The caller's own environment is restored before this returns.
# The readiness wait is generous on purpose: shell warm-up took 57 to 86 s on a machine loaded by parallel
# sessions, and a process that dies is reported at once, so the long limit only costs time when one hangs.
function Start-OwnedElsaServer {
    param(
        [Parameter(Mandatory)][string] $BaseUrl,
        [Parameter(Mandatory)][string] $ContentRoot,
        [hashtable] $Environment = @{},
        [int] $TimeoutSec = 300
    )
    if ($script:OwnedElsaServer) { throw "This script run already owns server process $($script:OwnedElsaServer.Process.Id); stop it first." }

    Assert-OwnableAddress -BaseUrl $BaseUrl
    $uri = [Uri] $BaseUrl
    $port = $uri.Port

    $dll = Join-Path (Split-Path $script:WorkbenchProject) 'bin/Debug/net10.0/Elsa.Workbench.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Server DLL not found at $dll; build Elsa.Workbench first." }

    $childEnvironment = @{ ASPNETCORE_ENVIRONMENT = 'Development' }
    foreach ($name in $Environment.Keys) { $childEnvironment[$name] = $Environment[$name] }
    $childEnvironment['ASPNETCORE_URLS'] = "$($uri.Scheme)://$($uri.Authority)"
    $previous = @{}
    foreach ($name in $childEnvironment.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

    $script:ElsaServerLaunchCount++
    $log = Join-Path $ContentRoot "server-$($script:ElsaServerLaunchCount)"
    $start = @{
        FilePath               = 'dotnet'
        ArgumentList           = @("`"$dll`"", '--contentRoot', "`"$ContentRoot`"")
        WorkingDirectory       = $ContentRoot
        RedirectStandardOutput = "$log.out.log"
        RedirectStandardError  = "$log.err.log"
        PassThru               = $true
    }
    if ($env:OS -eq 'Windows_NT') { $start.WindowStyle = 'Hidden' }
    try {
        foreach ($name in $childEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $childEnvironment[$name], 'Process') }
        $process = Start-Process @start
    } finally {
        foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    }
    $script:OwnedElsaServer = @{ Process = $process; BaseUrl = $BaseUrl; ContentRoot = $ContentRoot; Environment = $Environment }
    Write-Host "  [server] started owned process $($process.Id) for $BaseUrl (content root $ContentRoot)"

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            $script:OwnedElsaServer = $null
            throw "Owned Elsa.Workbench process $($process.Id) exited during startup (logs: $log.out.log and $log.err.log)."
        }
        if (Test-ElsaServerReady -BaseUrl $BaseUrl) {
            # A response proves only that something answers on the port. Make sure it is the owned process.
            $listeners = @(Get-PortListenerPid -Port $port)
            if ($listeners.Count -gt 0 -and $process.Id -notin $listeners) {
                Stop-OwnedElsaServer
                throw "Port $port is answered by pid $($listeners -join ', '), not by the server this script started (pid $($process.Id))."
            }
            Write-Host "  [server] owned process $($process.Id) ready on $port"
            return
        }
        Start-Sleep -Seconds 1
    }
    Stop-OwnedElsaServer
    throw "Owned Elsa.Workbench process did not turn /health/ready 200 on port $port within ${TimeoutSec}s; a failed startup task fails shell activation (logs: $log.out.log and $log.err.log)."
}

# Stops the server this script run started, and nothing else. Without an owned server it does nothing.
function Stop-OwnedElsaServer {
    param([int] $TimeoutSec = 20)
    $owned = $script:OwnedElsaServer
    if (-not $owned) { return }
    $process = $owned.Process
    if (-not $process.HasExited) {
        Write-Host "  [server] stopping owned process $($process.Id)"
        $process.Kill()
        if (-not $process.WaitForExit($TimeoutSec * 1000)) {
            throw "Owned Elsa.Workbench process $($process.Id) did not stop within ${TimeoutSec}s."
        }
        Start-Sleep -Milliseconds 800   # let SQLite file handles release
    }
    $script:OwnedElsaServer = $null
}

# Kills and relaunches the owned server with the same address, content root and environment.
function Restart-OwnedElsaServer {
    $owned = $script:OwnedElsaServer
    if (-not $owned) { throw 'This script run has not started a server, so there is none it may restart.' }
    Stop-OwnedElsaServer
    Start-OwnedElsaServer -BaseUrl $owned.BaseUrl -ContentRoot $owned.ContentRoot -Environment $owned.Environment
}

# Cleanup for a finally block: stops the owned server, then removes its content root or, after a failed run,
# keeps it (it holds the server logs and the SQLite files).
function Remove-OwnedElsaServer {
    param([string] $ContentRoot, [switch] $KeepContentRoot)
    try { Stop-OwnedElsaServer } catch { Write-Host "  [cleanup] failed to stop the owned server: $_" -ForegroundColor Yellow }
    if (-not $ContentRoot) { return }
    if ($KeepContentRoot) {
        Write-Host "  [cleanup] retained isolated content root for diagnosis: $ContentRoot" -ForegroundColor Yellow
    } else {
        Remove-Item -LiteralPath $ContentRoot -Recurse -Force
    }
}
