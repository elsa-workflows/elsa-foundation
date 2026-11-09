<#
.SYNOPSIS
    Runs the opt-in live Studio ETL authoring and execution journey against isolated local hosts.
.DESCRIPTION
    Starts already-built Workbench and Studio processes on free loopback ports. Each host receives a private
    content root, and the Workbench binds the two-row file fixture and private output directories; -Http also starts an owned
    loopback source/receiver and runs the connector publication and retry/cancellation journeys.
    Studio authentication stays enabled. The browser test writes screenshots and failure traces beneath the retained
    fixture root; this script stops only the processes it started.
#>
[CmdletBinding()]
param(
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [switch] $Http
)

. "$PSScriptRoot/_EtlFixture.ps1"

$script:OwnedStudioServer = $null

function New-EtlStudioContentRoot {
    param(
        [Parameter(Mandatory)][string] $ParentContentRoot,
        [Parameter(Mandatory)][string] $StudioProjectDirectory
    )

    $contentRoot = Join-Path $ParentContentRoot 'studio'
    New-Item -Path $contentRoot -ItemType Directory -Force | Out-Null
    foreach ($fileName in @('appsettings.json', 'appsettings.Development.json', 'shells.json', 'nuplane-management.json')) {
        Copy-Item -LiteralPath (Join-Path $StudioProjectDirectory $fileName) -Destination $contentRoot
    }
    New-Item -Path (Join-Path $contentRoot 'packages') -ItemType Directory -Force | Out-Null

    # WeatherForecastSample is package-loaded and is not needed for the ETL journey. This private fixture has no
    # Nuplane package feed contents, so remove only that unavailable optional feature from its copied shell.
    $shellsPath = Join-Path $contentRoot 'shells.json'
    $shells = Get-Content -LiteralPath $shellsPath -Raw | ConvertFrom-Json
    $features = $shells.CShells.Shells.Default.Features
    $features.PSObject.Properties.Remove('WeatherForecastSample')
    [IO.File]::WriteAllText($shellsPath, ($shells | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))

    return $contentRoot
}

function Add-StudioReturnOriginToPrivateWorkbenchShell {
    param(
        [Parameter(Mandatory)][string] $WorkbenchContentRoot,
        [Parameter(Mandatory)][string] $StudioOrigin
    )

    $shellsPath = Join-Path $WorkbenchContentRoot 'shells.json'
    $shells = Get-Content -LiteralPath $shellsPath -Raw | ConvertFrom-Json
    $identity = $shells.CShells.Shells.default.Features.FoundationIdentityAspNetCoreIdentity
    $origins = @($identity.AllowedReturnUrlOrigins | ForEach-Object { [string]$_ })
    if ($StudioOrigin -notin $origins) {
        $identity.AllowedReturnUrlOrigins = @($origins + $StudioOrigin)
    }
    [IO.File]::WriteAllText($shellsPath, ($shells | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
}

function Test-OwnedStudioReady {
    param(
        [Parameter(Mandatory)][string] $BaseUrl,
        [Parameter(Mandatory)][string] $BackendBaseUrl
    )

    try {
        $runtime = Invoke-WebRequest "$BaseUrl/studio-runtime.js" -TimeoutSec 5 -UseBasicParsing
        if ($runtime.StatusCode -ne 200 -or $runtime.Content -notlike "*`"backendBaseUrl`":`"$BackendBaseUrl`"*") {
            return $false
        }
        $homeResponse = Invoke-WebRequest "$BaseUrl/" -TimeoutSec 5 -UseBasicParsing
        return $homeResponse.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Start-OwnedStudioServer {
    param(
        [Parameter(Mandatory)][string] $BaseUrl,
        [Parameter(Mandatory)][string] $BackendBaseUrl,
        [Parameter(Mandatory)][string] $ContentRoot,
        [int] $TimeoutSec = 180
    )

    if ($script:OwnedStudioServer) {
        throw "This script run already owns Studio process $($script:OwnedStudioServer.Process.Id)."
    }
    Assert-OwnableAddress -BaseUrl $BaseUrl

    $studioProjectDirectory = (Resolve-Path "$PSScriptRoot/../../Studio/src/apps/Elsa.Studio.Web").Path
    $dll = Join-Path $studioProjectDirectory 'bin/Debug/net10.0/Elsa.Studio.Web.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "Studio host DLL not found at $dll; build Elsa.Studio.Web with ETL enabled first."
    }

    $uri = [Uri]$BaseUrl
    $port = $uri.Port
    $childEnvironment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS = "$($uri.Scheme)://$($uri.Authority)"
        Studio__BackendBaseUrl = $BackendBaseUrl
        Studio__Auth__Enabled = 'true'
    }
    $previous = @{}
    foreach ($name in $childEnvironment.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    $log = Join-Path $ContentRoot 'studio-server'
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
        foreach ($name in $childEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $childEnvironment[$name], 'Process')
        }
        $process = Start-Process @start
    } finally {
        foreach ($name in $previous.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
        }
    }

    $script:OwnedStudioServer = @{ Process = $process; BaseUrl = $BaseUrl; ContentRoot = $ContentRoot }
    Write-Host "  [studio] started owned process $($process.Id) for $BaseUrl (content root $ContentRoot)"

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            throw "Owned Studio process $($process.Id) exited during startup (logs: $log.out.log and $log.err.log)."
        }
        if (Test-OwnedStudioReady -BaseUrl $BaseUrl -BackendBaseUrl $BackendBaseUrl) {
            $listeners = @(Get-PortListenerPid -Port $port)
            if ($listeners.Count -gt 0 -and $process.Id -notin $listeners) {
                throw "Port $port is answered by pid $($listeners -join ', '), not the Studio process this script started (pid $($process.Id))."
            }
            Write-Host "  [studio] owned process $($process.Id) ready on $port with auth enabled"
            return
        }
        Start-Sleep -Seconds 1
    }

    throw "Owned Studio process did not serve its configured runtime and home page on port $port within ${TimeoutSec}s (logs: $log.out.log and $log.err.log)."
}

function Stop-OwnedStudioServer {
    param([int] $TimeoutSec = 20)

    $owned = $script:OwnedStudioServer
    if (-not $owned) { return }
    $process = $owned.Process
    if (-not $process.HasExited) {
        Write-Host "  [studio] stopping owned process $($process.Id)"
        $process.Kill()
        if (-not $process.WaitForExit($TimeoutSec * 1000)) {
            throw "Owned Studio process $($process.Id) did not stop within ${TimeoutSec}s."
        }
    }
    $script:OwnedStudioServer = $null
}

$fixture = $null
$journeyFailure = $null
try {
    $repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
    $workbenchDll = Join-Path $repoRoot 'Essentials/apps/Elsa.Workbench/bin/Debug/net10.0/Elsa.Workbench.dll'
    $studioProjectDirectory = Join-Path $repoRoot 'Studio/src/apps/Elsa.Studio.Web'
    $studioDll = Join-Path $studioProjectDirectory 'bin/Debug/net10.0/Elsa.Studio.Web.dll'
    if (-not (Test-Path -LiteralPath $workbenchDll -PathType Leaf)) {
        throw "Workbench host DLL not found at $workbenchDll; build Elsa.Workbench with ETL enabled first."
    }
    if (-not (Test-Path -LiteralPath $studioDll -PathType Leaf)) {
        throw "Studio host DLL not found at $studioDll; build Elsa.Studio.Web with ETL enabled first."
    }
    if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
        throw 'pnpm was not found on PATH; install the Studio workspace dependencies before running this journey.'
    }

    $fixtureArguments = @{ SourceCsv = "id,name`n1,Ada`n2,Grace`n"; LookupCsv = "id,category`n1,One`n2,Two`n" }
    $fixture = if ($Http) { New-EtlHttpFixture @fixtureArguments } else { New-EtlFixture @fixtureArguments }
    $studioPort = Get-FreeLoopbackPort
    do { $workbenchPort = Get-FreeLoopbackPort } while ($workbenchPort -eq $studioPort)
    $studioUrl = "http://127.0.0.1:$studioPort"
    $workbenchUrl = "http://127.0.0.1:$workbenchPort"
    Add-StudioReturnOriginToPrivateWorkbenchShell -WorkbenchContentRoot $fixture.ContentRoot -StudioOrigin $studioUrl

    $studioContentRoot = New-EtlStudioContentRoot -ParentContentRoot $fixture.ContentRoot -StudioProjectDirectory $studioProjectDirectory
    Start-OwnedElsaServer -BaseUrl $workbenchUrl -ContentRoot $fixture.ContentRoot -Environment @{
        Cors__AllowedOrigins__5 = $studioUrl
    }
    Start-OwnedStudioServer -BaseUrl $studioUrl -BackendBaseUrl $workbenchUrl -ContentRoot $studioContentRoot

    $studioDirectory = Join-Path $repoRoot 'Studio'
    $journeyLog = Join-Path $fixture.ContentRoot 'studio-journey.log'
    $playwrightOutput = Join-Path $fixture.ContentRoot 'playwright-output'
    $reportPath = Join-Path $fixture.ContentRoot 'studio-journey.json'
    $environmentNames = @('ELSA_ETL_HTTP_LIVE_JOURNEY', 'ELSA_ETL_LIVE_JOURNEY', 'ELSA_STUDIO_URL', 'ELSA_STUDIO_USER', 'ELSA_STUDIO_PASSWORD', 'PLAYWRIGHT_JSON_OUTPUT_FILE')
    $previous = @{}
    foreach ($name in $environmentNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    try {
        [Environment]::SetEnvironmentVariable('ELSA_ETL_LIVE_JOURNEY', '1', 'Process')
        [Environment]::SetEnvironmentVariable('ELSA_ETL_HTTP_LIVE_JOURNEY', $(if ($Http) { '1' } else { $null }), 'Process')
        $testPattern = if ($Http) { 'HTTP connector' } else { 'authors and runs an immutable ETL pipeline through a Sequence workflow' }
        [Environment]::SetEnvironmentVariable('ELSA_STUDIO_URL', $studioUrl, 'Process')
        [Environment]::SetEnvironmentVariable('ELSA_STUDIO_USER', $Username, 'Process')
        [Environment]::SetEnvironmentVariable('ELSA_STUDIO_PASSWORD', $Password, 'Process')
        [Environment]::SetEnvironmentVariable('PLAYWRIGHT_JSON_OUTPUT_FILE', $reportPath, 'Process')
        Push-Location $studioDirectory
        try {
            & pnpm exec playwright test --config tests/live/playwright.live.config.ts --reporter=line,json --output $playwrightOutput --grep $testPattern tests/live/etl-journey.spec.ts *>&1 |
                Tee-Object -FilePath $journeyLog
            $journeyExitCode = $LASTEXITCODE
        } finally {
            Pop-Location
        }
    } finally {
        foreach ($name in $previous.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
        }
    }

    if ($journeyExitCode -ne 0) {
        throw "Live Studio ETL journey failed with exit code $journeyExitCode. See $journeyLog and $playwrightOutput."
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $expectedTests = if ($Http) { 2 } else { 1 }
    if ($report.stats.expected -ne $expectedTests -or $report.stats.unexpected -ne 0 -or $report.stats.skipped -ne 0 -or $report.stats.flaky -ne 0) {
        throw "The ETL journey did not execute exactly $expectedTests passing tests without retries/skips. See $reportPath."
    }

    Write-Host "SUCCESS - the live Studio journey authored, published, selected, and ran the finite ETL pipeline."
} catch {
    $journeyFailure = $_
    throw
} finally {
    $cleanupErrors = [System.Collections.Generic.List[string]]::new()
    try { Stop-OwnedStudioServer } catch { $cleanupErrors.Add("Studio: $_") }
    try { Stop-OwnedElsaServer } catch { $cleanupErrors.Add("Workbench: $_") }
    if ($Http -and $fixture) {
        try { Stop-EtlHttpFixture -Fixture $fixture } catch { $cleanupErrors.Add("HTTP fixture: $_") }
    }
    if ($fixture) {
        Write-Host "  [evidence] retained private hosts, databases, inputs, outputs, logs, and Playwright screenshots at $($fixture.ContentRoot)"
    }
    if ($cleanupErrors.Count -gt 0) {
        throw "Owned process cleanup failed: $($cleanupErrors -join '; '). Journey error: $journeyFailure"
    }
}
