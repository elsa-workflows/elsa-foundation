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
