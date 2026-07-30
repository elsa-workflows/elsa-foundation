[CmdletBinding()]
param(
    [switch]$ForceEvaluate,
    [string]$Receipt,
    [switch]$SelfCheck,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DriverProtocolVersion = 1
$DiscoveryRulesVersion = 1
$InputRulesVersion = 1
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$DriverPath = 'tools/architecture/restore-zero-ef-certification.ps1'
$ExcludedDirectoryNames = @('.git', 'bin', 'obj')

function Show-Usage {
    Write-Output 'Usage: restore-zero-ef-certification.ps1 -ForceEvaluate -Receipt PATH'
    Write-Output '       restore-zero-ef-certification.ps1 -SelfCheck'
    Write-Output ''
    Write-Output 'Discovers every repository project independently of solution membership, restores each'
    Write-Output 'project with -ForceEvaluate, and writes a fail-closed zero-EF certification receipt.'
}

function Stop-Driver([string]$Message) {
    throw "restore-zero-ef-certification: $Message"
}

function Get-Sha256File([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Sha256Bytes([byte[]]$Bytes) {
    if ($null -eq $Bytes) {
        $Bytes = [byte[]]::new(0)
    }
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($hasher.ComputeHash([byte[]]$Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Get-Sha256Text([string]$Text) {
    return Get-Sha256Bytes ([System.Text.UTF8Encoding]::new($false).GetBytes($Text))
}

function Convert-ToRepoRelative([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $fullPath)
    if ($relative -eq '..' -or $relative.StartsWith("..$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::Ordinal)) {
        Stop-Driver 'discovery returned a path outside the repository.'
    }
    if ($relative.IndexOfAny([char[]]"`r`n`t") -ge 0) {
        Stop-Driver 'receipt paths may not contain control characters.'
    }
    return $relative.Replace('\', '/')
}

function Get-RepositoryFiles {
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($RepoRoot)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in [System.IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $item = Get-Item -LiteralPath $entry -Force
            if ($item.PSIsContainer) {
                if (@($ExcludedDirectoryNames | Where-Object {
                        [System.StringComparer]::Ordinal.Equals($_, $item.Name)
                    }).Count -gt 0) {
                    continue
                }
                if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    Stop-Driver 'repository discovery does not follow reparse-point directories.'
                }
                $pending.Push($item.FullName)
                continue
            }
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Stop-Driver 'repository discovery does not follow reparse-point files.'
            }
            Write-Output $item.FullName
        }
    }
}

function Sort-Ordinal([string[]]$Values) {
    $result = [string[]]$Values
    [System.Array]::Sort($result, [System.StringComparer]::Ordinal)
    return $result
}

function Sort-OrdinalUnique([string[]]$Values) {
    $sorted = Sort-Ordinal $Values
    $result = [System.Collections.Generic.List[string]]::new()
    $previous = $null
    foreach ($value in $sorted) {
        if ($null -eq $previous -or -not [System.StringComparer]::Ordinal.Equals($previous, $value)) {
            $result.Add($value)
            $previous = $value
        }
    }
    return [string[]]$result.ToArray()
}

function Get-GitStatusBytes {
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = 'git'
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.ArgumentList.Add('-C')
    $info.ArgumentList.Add($RepoRoot)
    $info.ArgumentList.Add('status')
    $info.ArgumentList.Add('--porcelain=v1')
    $info.ArgumentList.Add('-z')
    $info.ArgumentList.Add('--untracked-files=all')
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    $bytes = [System.IO.MemoryStream]::new()
    $process.StandardOutput.BaseStream.CopyTo($bytes)
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        Stop-Driver 'could not read repository worktree status.'
    }
    return ,$bytes.ToArray()
}

function Resolve-AssetsProjectPath([string]$AssetsPath, [string]$RestoreProjectPath) {
    if ([string]::IsNullOrWhiteSpace($RestoreProjectPath)) {
        return $null
    }
    $candidate = if ([System.IO.Path]::IsPathRooted($RestoreProjectPath)) {
        [System.IO.Path]::GetFullPath($RestoreProjectPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $AssetsPath) $RestoreProjectPath))
    }
    if (-not [System.IO.File]::Exists($candidate)) {
        return $null
    }
    return $candidate
}

function Test-AssetsAdmitRootNuGetConfig([object]$Assets, [string]$AssetsPath) {
    $property = $Assets.project.restore.PSObject.Properties['configFilePaths']
    if ($null -eq $property -or -not ($property.Value -is [System.Collections.IList])) {
        return $false
    }
    $configPaths = $property.Value
    if ($configPaths.Count -ne 1 -or
        -not ($configPaths[0] -is [string]) -or
        [string]::IsNullOrWhiteSpace($configPaths[0])) {
        return $false
    }
    $configPath = $configPaths[0]
    $candidate = if ([System.IO.Path]::IsPathRooted($configPath)) {
        [System.IO.Path]::GetFullPath($configPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $AssetsPath) $configPath))
    }
    if (-not [System.IO.File]::Exists($candidate)) {
        return $false
    }
    if ($candidate.Equals($NuGetConfig, [System.StringComparison]::Ordinal)) {
        return $true
    }
    # NuGet can report the conventional Config spelling on a case-insensitive
    # filesystem even though the checked-out root file is NuGet.config. Enumerate
    # the actual entry before admitting that case-only alias; a case-sensitive
    # filesystem cannot pass this branch for a different file.
    $candidateDirectory = Split-Path -Parent $candidate
    $candidateName = Split-Path -Leaf $candidate
    foreach ($entry in [System.IO.Directory]::EnumerateFiles($candidateDirectory)) {
        if ([System.StringComparer]::OrdinalIgnoreCase.Equals([System.IO.Path]::GetFileName($entry), $candidateName) -and
            $entry.Equals($NuGetConfig, [System.StringComparison]::Ordinal)) {
            return $true
        }
    }
    return $false
}

if ($SelfCheck) {
    $emptyBytes = [byte[]]::new(0)
    $emptyHash = Get-Sha256Bytes -Bytes $emptyBytes
    if (-not $emptyHash.Equals(
            'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
            [System.StringComparison]::Ordinal)) {
        Stop-Driver 'empty-byte SHA-256 self-check failed.'
    }
    Write-Output 'PowerShell restore-driver self-check passed.'
    exit 0
}

if ($Help) {
    Show-Usage
    exit 0
}

if (-not $ForceEvaluate) {
    Stop-Driver '-ForceEvaluate is required.'
}
if ([string]::IsNullOrWhiteSpace($Receipt)) {
    Stop-Driver '-Receipt is required.'
}
try {
    $CanonicalRepoRoot = (& git -C $RepoRoot rev-parse --show-toplevel 2>$null).Trim()
}
catch {
    Stop-Driver 'a verifiable git repository root is required.'
}
if ([string]::IsNullOrWhiteSpace($CanonicalRepoRoot)) {
    Stop-Driver 'a verifiable git repository root is required.'
}
$RepoRoot = [System.IO.Path]::GetFullPath($CanonicalRepoRoot)
$NuGetConfig = Join-Path $RepoRoot 'NuGet.config'
if (-not [System.IO.File]::Exists($NuGetConfig)) {
    Stop-Driver 'repository NuGet.config is required.'
}

try {
    $GitHead = (& git -C $RepoRoot rev-parse --verify HEAD 2>$null).Trim()
}
catch {
    Stop-Driver 'a verifiable git HEAD is required.'
}
if ([string]::IsNullOrWhiteSpace($GitHead)) {
    Stop-Driver 'a verifiable git HEAD is required.'
}
try {
    $DotnetSdkVersion = (& dotnet --version 2>$null).Trim()
}
catch {
    Stop-Driver 'could not determine the dotnet SDK version.'
}
if ([string]::IsNullOrWhiteSpace($DotnetSdkVersion)) {
    Stop-Driver 'could not determine the dotnet SDK version.'
}

$files = @(Get-RepositoryFiles)
$projects = @(Sort-OrdinalUnique @($files |
    Where-Object { $_.EndsWith('.csproj', [System.StringComparison]::Ordinal) } |
    ForEach-Object { Convert-ToRepoRelative $_ }))
if ($projects.Count -eq 0) {
    Stop-Driver 'no repository projects were discovered.'
}

$initialProjects = [string[]]$projects
for ($projectIndex = 0; $projectIndex -lt $initialProjects.Count; $projectIndex++) {
    $path = $initialProjects[$projectIndex]
    Write-Output "Restoring project $($projectIndex + 1)/$($initialProjects.Count): $path"
    $projectPath = Join-Path $RepoRoot $path
    $restoreLog = [System.IO.Path]::GetTempFileName()
    try {
        & dotnet restore $projectPath --force-evaluate --configfile $NuGetConfig *> $restoreLog
        if ($LASTEXITCODE -ne 0) {
            Stop-Driver "restore failed for '$path'."
        }
    }
    finally {
        Remove-Item -LiteralPath $restoreLog -Force -ErrorAction SilentlyContinue
    }
}

$files = @(Get-RepositoryFiles)
$projects = @(Sort-OrdinalUnique @($files |
    Where-Object { $_.EndsWith('.csproj', [System.StringComparison]::Ordinal) } |
    ForEach-Object { Convert-ToRepoRelative $_ }))
if ($projects.Count -ne $initialProjects.Count) {
    Stop-Driver 'repository project discovery changed during restore.'
}
for ($index = 0; $index -lt $projects.Count; $index++) {
    if (-not [System.StringComparer]::Ordinal.Equals($projects[$index], $initialProjects[$index])) {
        Stop-Driver 'repository project discovery changed during restore.'
    }
}

$inputs = @(Sort-OrdinalUnique @($files |
    Where-Object {
        $name = [System.IO.Path]::GetFileName($_)
        $_.EndsWith('.csproj', [System.StringComparison]::Ordinal) -or
        $_.EndsWith('.props', [System.StringComparison]::Ordinal) -or
        $_.EndsWith('.targets', [System.StringComparison]::Ordinal) -or
        $_.EndsWith('.rsp', [System.StringComparison]::Ordinal) -or
        $name.Equals('NuGet.Config', [System.StringComparison]::OrdinalIgnoreCase) -or
        $name.Equals('global.json', [System.StringComparison]::Ordinal) -or
        $name.Equals('packages.lock.json', [System.StringComparison]::Ordinal)
    } |
    ForEach-Object { Convert-ToRepoRelative $_ }))
if ($inputs.Count -eq 0) {
    Stop-Driver 'no dependency-affecting inputs were discovered.'
}

$inputEntries = foreach ($path in $inputs) {
    [ordered]@{
        path = $path
        sha256 = Get-Sha256File (Join-Path $RepoRoot $path)
    }
}
$inputFingerprintText = (($inputEntries | ForEach-Object { "$($_.path)`t$($_.sha256)`n" }) -join '')
$inputFingerprintSha256 = Get-Sha256Text $inputFingerprintText
$projectSetSha256 = Get-Sha256Text (($projects | ForEach-Object { "$_`n" }) -join '')

$projectEntries = [System.Collections.Generic.List[object]]::new()
foreach ($path in $projects) {
    $projectPath = Join-Path $RepoRoot $path
    $assetsPath = Join-Path (Join-Path (Split-Path -Parent $projectPath) 'obj') 'project.assets.json'
    if (-not [System.IO.File]::Exists($assetsPath)) {
        Stop-Driver "restore did not produce project.assets.json for '$path'."
    }
    try {
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        $resolvedRestoreProjectPath = Resolve-AssetsProjectPath $assetsPath $assets.project.restore.projectPath
    }
    catch {
        Stop-Driver "project.assets.json does not identify its restored project for '$path'."
    }
    $comparison = if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    if ($null -eq $resolvedRestoreProjectPath -or -not $resolvedRestoreProjectPath.Equals([System.IO.Path]::GetFullPath($projectPath), $comparison)) {
        Stop-Driver "project.assets.json does not bind to discovered project '$path'."
    }
    if (-not (Test-AssetsAdmitRootNuGetConfig $assets $assetsPath)) {
        Stop-Driver "project.assets.json does not admit the repository NuGet.config for '$path'."
    }
    $projectEntries.Add([ordered]@{
        path = $path
        inputFingerprintSha256 = $inputFingerprintSha256
        assets = [ordered]@{
            path = Convert-ToRepoRelative $assetsPath
            sha256 = Get-Sha256File $assetsPath
            restoreProjectPath = $path
        }
    })
}

$worktreeStatusSha256 = Get-Sha256Bytes (Get-GitStatusBytes)
$driverSha256 = Get-Sha256File (Join-Path $RepoRoot $DriverPath)
$receiptPath = if ([System.IO.Path]::IsPathRooted($Receipt)) {
    [System.IO.Path]::GetFullPath($Receipt)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Receipt))
}
$receiptRelative = Convert-ToRepoRelative $receiptPath
if ($receiptRelative -eq '.') {
    Stop-Driver 'receipt path must name a file within the repository.'
}
$receiptDirectory = Split-Path -Parent $receiptPath
if ([string]::IsNullOrWhiteSpace($receiptDirectory)) {
    Stop-Driver 'receipt path must have a parent directory.'
}
[System.IO.Directory]::CreateDirectory($receiptDirectory) | Out-Null

$receiptDocument = [ordered]@{
    schemaVersion = 1
    kind = 'elsa-zero-ef-all-project-restore'
    generatedAtUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [System.Globalization.CultureInfo]::InvariantCulture)
    repository = [ordered]@{
        gitHead = $GitHead
        worktreeStatusSha256 = $worktreeStatusSha256
    }
    restore = [ordered]@{
        driverProtocolVersion = $DriverProtocolVersion
        commandTemplate = @('dotnet', 'restore', '<project>', '--force-evaluate', '--configfile', 'NuGet.config')
        dotnetSdkVersion = $DotnetSdkVersion
        driverPath = $DriverPath
        driverSha256 = $driverSha256
    }
    discovery = [ordered]@{
        rulesVersion = $DiscoveryRulesVersion
        excludedDirectoryNames = $ExcludedDirectoryNames
        projects = $projects
        projectSetSha256 = $projectSetSha256
    }
    inputs = [ordered]@{
        rulesVersion = $InputRulesVersion
        entries = @($inputEntries)
        fingerprintSha256 = $inputFingerprintSha256
    }
    projects = @($projectEntries)
}

$temporaryReceiptPath = Join-Path $receiptDirectory ('.zero-ef-restore-receipt.' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [System.IO.File]::WriteAllText(
        $temporaryReceiptPath,
        (($receiptDocument | ConvertTo-Json -Depth 12) + "`n"),
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($temporaryReceiptPath, $receiptPath, $true)
}
finally {
    if ([System.IO.File]::Exists($temporaryReceiptPath)) {
        Remove-Item -LiteralPath $temporaryReceiptPath -Force -ErrorAction SilentlyContinue
    }
}

Write-Output "Wrote zero-EF all-project restore receipt: $Receipt"
