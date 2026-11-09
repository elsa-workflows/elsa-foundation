# Shared owned fixture composition for ETL HTTP and live Studio acceptance.
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

function Set-FeatureConfiguration {
    param(
        [Parameter(Mandatory)] $Features,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][AllowEmptyCollection()] $Value
    )
    $Features | Add-Member -MemberType NoteProperty -Name $Name -Value $Value -Force
}

function New-EtlFixture {
    param(
        [string] $SourceCsv = "id,name`n1,Ada`n2,Grace`n3,Linus`ninvalid,RejectMe`n",
        [string] $LookupCsv = "id,region`n1,Europe`n2,US`n3,Europe`n"
    )
    $contentRoot = New-ElsaContentRoot -Prefix 'elsa-finite-file-etl'
    $etlRoot = Join-Path $contentRoot 'etl'
    $sourceDirectory = Join-Path $etlRoot 'inputs'
    $mainRoot = Join-Path $etlRoot 'main-output'
    $errorRoot = Join-Path $etlRoot 'error-output'
    $snapshotRoot = Join-Path $etlRoot 'snapshots'
    New-Item -Path $sourceDirectory, $mainRoot, $errorRoot, $snapshotRoot -ItemType Directory -Force | Out-Null

    $sourcePath = Join-Path $sourceDirectory 'source.csv'
    $lookupPath = Join-Path $sourceDirectory 'lookup.csv'
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($sourcePath, $sourceCsv, $utf8)
    [IO.File]::WriteAllText($lookupPath, $lookupCsv, $utf8)
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $lookupHash = (Get-FileHash -LiteralPath $lookupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sourceBytes = (Get-Item -LiteralPath $sourcePath).Length
    $lookupBytes = (Get-Item -LiteralPath $lookupPath).Length

    # The JSON configuration is a private copy under this script's owned content root.
    $shellsPath = Join-Path $contentRoot 'shells.json'
    $shells = Get-Content -LiteralPath $shellsPath -Raw | ConvertFrom-Json
    $features = $shells.CShells.Shells.default.Features
    $designDatabase = Join-Path $etlRoot 'etl-design.db'
    $runtimeDatabase = Join-Path $etlRoot 'etl-runtime.db'
    Set-FeatureConfiguration -Features $features -Name 'EtlDesign' -Value @{}
    Set-FeatureConfiguration -Features $features -Name 'EtlDesignEntityFrameworkCore' -Value @{
        Provider = 'Sqlite'; ConnectionString = "Data Source=$designDatabase;Pooling=False"
    }
    Set-FeatureConfiguration -Features $features -Name 'EtlDesignApi' -Value @{
        CursorSigningKey = 'finite-file-etl-design-cursor-key-for-isolated-e2e'
    }
    Set-FeatureConfiguration -Features $features -Name 'EtlRuntime' -Value @{
        SnapshotRoot = [IO.Path]::GetFullPath($snapshotRoot)
        FileBindings = @(
            @{ TenantId = 'default'; PersistencePartition = 'default'; Alias = 'source'; Role = 'Source'; Revision = 'source-v1'; Path = [IO.Path]::GetFullPath($sourcePath); IsCurrent = $true; ExpectedSha256 = $sourceHash; ImmutableInput = $true; AtomicPublication = $false },
            @{ TenantId = 'default'; PersistencePartition = 'default'; Alias = 'lookup'; Role = 'Lookup'; Revision = 'lookup-v1'; Path = [IO.Path]::GetFullPath($lookupPath); IsCurrent = $true; ExpectedSha256 = $lookupHash; ImmutableInput = $true; AtomicPublication = $false },
            @{ TenantId = 'default'; PersistencePartition = 'default'; Alias = 'output'; Role = 'MainOutput'; Revision = 'output-v1'; Path = [IO.Path]::GetFullPath($mainRoot); IsCurrent = $true; ImmutableInput = $false; AtomicPublication = $true },
            @{ TenantId = 'default'; PersistencePartition = 'default'; Alias = 'errors'; Role = 'ErrorOutput'; Revision = 'errors-v1'; Path = [IO.Path]::GetFullPath($errorRoot); IsCurrent = $true; ImmutableInput = $false; AtomicPublication = $true }
        )
    }
    Set-FeatureConfiguration -Features $features -Name 'EtlRuntimeEntityFrameworkCore' -Value @{
        Provider = 'Sqlite'; ConnectionString = "Data Source=$runtimeDatabase;Pooling=False"
    }
    Set-FeatureConfiguration -Features $features -Name 'EtlRuntimeApi' -Value @{
        CursorSigningKey = 'finite-file-etl-runtime-cursor-key-for-isolated-e2e'
    }
    Set-FeatureConfiguration -Features $features -Name 'EtlWorkflowsDesign' -Value @{}
    Set-FeatureConfiguration -Features $features -Name 'EtlWorkflowsRuntime' -Value @{}
    [IO.File]::WriteAllText($shellsPath, ($shells | ConvertTo-Json -Depth 100), $utf8)

    return [pscustomobject]@{
        ContentRoot = $contentRoot
        MainRoot = $mainRoot
        ErrorRoot = $errorRoot
        SourceHash = $sourceHash
        LookupHash = $lookupHash
        SourceBytes = $sourceBytes
        LookupBytes = $lookupBytes
    }
}

function New-EtlHttpFixture {
    param(
        [string] $SourceCsv = "id,name`n1,Ada`n2,Grace`n3,Linus`ninvalid,RejectMe`n",
        [string] $LookupCsv = "id,region`n1,Europe`n2,US`n3,Europe`n"
    )

    $fixture = New-EtlFixture -SourceCsv $SourceCsv -LookupCsv $LookupCsv
    $process = $null
    try {
        $readyPath = Join-Path $fixture.ContentRoot 'etl-http-ready.json'
        $stdoutPath = Join-Path $fixture.ContentRoot 'etl-http-fixture.stdout.log'
        $stderrPath = Join-Path $fixture.ContentRoot 'etl-http-fixture.stderr.log'
        $nodeScript = Join-Path $PSScriptRoot 'EtlHttpFixture.mjs'
        if (-not (Test-Path -LiteralPath $nodeScript -PathType Leaf)) {
            throw 'The owned ETL loopback HTTP fixture script is missing.'
        }

        $arguments = '"{0}" --ready-file "{1}"' -f $nodeScript, $readyPath
        $process = Start-Process -FilePath 'node' -ArgumentList $arguments -NoNewWindow -PassThru `
            -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($process.HasExited) {
                throw 'The owned ETL loopback HTTP fixture stopped before becoming ready.'
            }
            if (Test-Path -LiteralPath $readyPath -PathType Leaf) { break }
            Start-Sleep -Milliseconds 100
        }
        if (-not (Test-Path -LiteralPath $readyPath -PathType Leaf)) {
            throw 'The owned ETL loopback HTTP fixture did not become ready within 15 seconds.'
        }
        $ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
        $port = 0
        if (-not [int]::TryParse([string]$ready.port, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
            throw 'The owned ETL loopback HTTP fixture returned an invalid port.'
        }
        $httpBaseUrl = "http://127.0.0.1:$port"

        $shellsPath = Join-Path $fixture.ContentRoot 'shells.json'
        $shells = Get-Content -LiteralPath $shellsPath -Raw | ConvertFrom-Json
        $features = $shells.CShells.Shells.default.Features
        $sourceAPath = '/source/shard-a/page'
        $sourceBPath = '/source/shard-b/page'
        $waitPath = '/wait/page'
        $outputPath = '/output/records'
        $httpBindings = @(
            @{
                TenantId = 'default'; PersistencePartition = 'default'; Alias = 'http-source'; Role = 'Source'; Revision = 'http-source-v1'
                Endpoint = "$httpBaseUrl$sourceAPath"; AllowedPaths = @($sourceAPath, $sourceBPath); AllowHttp = $true
                Shards = @(
                    @{ Id = 'shard-a'; SeedUri = "$httpBaseUrl$sourceAPath" },
                    @{ Id = 'shard-b'; SeedUri = "$httpBaseUrl$sourceBPath" }
                )
            },
            @{
                TenantId = 'default'; PersistencePartition = 'default'; Alias = 'http-wait'; Role = 'Source'; Revision = 'http-wait-v1'
                Endpoint = "$httpBaseUrl$waitPath"; AllowedPaths = @($waitPath); AllowHttp = $true
                Shards = @(@{ Id = 'wait'; SeedUri = "$httpBaseUrl$waitPath" })
            },
            @{
                TenantId = 'default'; PersistencePartition = 'default'; Alias = 'http-output'; Role = 'MainOutput'; Revision = 'http-output-v1'
                Endpoint = "$httpBaseUrl$outputPath"; AllowedPaths = @($outputPath); AllowHttp = $true
                Shards = @()
            }
        )
        $features.EtlRuntime | Add-Member -MemberType NoteProperty -Name HttpBindings -Value $httpBindings -Force
        [IO.File]::WriteAllText($shellsPath, ($shells | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))

        $fixture | Add-Member -MemberType NoteProperty -Name HttpBaseUrl -Value $httpBaseUrl
        $fixture | Add-Member -MemberType NoteProperty -Name HttpProcess -Value $process
        $fixture | Add-Member -MemberType NoteProperty -Name HttpReadyPath -Value $readyPath
        $fixture | Add-Member -MemberType NoteProperty -Name HttpStdoutPath -Value $stdoutPath
        $fixture | Add-Member -MemberType NoteProperty -Name HttpStderrPath -Value $stderrPath
        return $fixture
    } catch {
        $failure = $_
        if ($process) {
            if (-not $process.HasExited) {
                Stop-EtlHttpProcess -Process $process
            } else {
                $process.Dispose()
            }
        }
        throw "Owned ETL HTTP fixture setup failed; its temporary content root was retained at '$($fixture.ContentRoot)'. $($failure.Exception.Message)"
    }
}

function Stop-EtlHttpFixture {
    param([Parameter(Mandatory)] $Fixture)
    if ($Fixture.HttpProcess) {
        Stop-EtlHttpProcess -Process $Fixture.HttpProcess
    }
}

function Stop-EtlHttpProcess {
    param([Parameter(Mandatory)] [System.Diagnostics.Process] $Process)
    try {
        if (-not $Process.HasExited) {
            $Process.Kill()
            if (-not $Process.WaitForExit(5000)) {
                throw 'The owned ETL loopback HTTP fixture did not stop within five seconds.'
            }
        }
    } finally {
        $Process.Dispose()
    }
}

function Get-EtlHttpFixtureState {
    param([Parameter(Mandatory)] $Fixture)
    $state = Invoke-RestMethod -Uri "$($Fixture.HttpBaseUrl)/__fixture/state" -Method Get -TimeoutSec 5
    if ($state.overflow) { throw 'The loopback HTTP fixture exceeded its bounded observation capacity.' }
    return $state
}
