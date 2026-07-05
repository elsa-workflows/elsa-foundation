Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$docsMaps = Join-Path $repoRoot "docs\maps"

function To-RepoPath {
    param([string]$Path)
    # Pure string normalization: Resolve-Path would substitute on-disk casing,
    # churning committed maps on case-insensitive filesystems.
    $full = [System.IO.Path]::GetFullPath($Path)
    return ($full.Substring($repoRoot.Length + 1) -replace '\\', '/')
}

# Enumerate repo files via git so .gitignore is respected and paths use the
# git-tracked casing; a raw directory walk picks up machine-local scratch
# files and on-disk casing, which churns the committed maps.
function Get-RepoFiles {
    param([string[]]$PathSpecs)
    try {
        $listing = & git -C $repoRoot ls-files --cached --others --exclude-standard -- @PathSpecs 2>$null
    } catch {
        throw "git is required to enumerate repo files for map generation: $_"
    }
    if ($LASTEXITCODE -ne 0) { throw "git ls-files failed; map generation requires a git checkout." }
    $files = @()
    foreach ($relative in @($listing)) {
        if ([string]::IsNullOrWhiteSpace($relative)) { continue }
        $file = [System.IO.FileInfo]::new((Join-Path $repoRoot $relative))
        if ($file.Exists) { $files += $file }
    }
    return @($files | Sort-Object FullName)
}

function Escape-Cell {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "-" }
    return (($Value -replace '\|', '\|') -replace "`r?`n", "<br>")
}

$centralPackageVersionsPath = Join-Path $repoRoot "Directory.Packages.props"

function Read-CentralPackageVersions {
    if (-not (Test-Path -LiteralPath $centralPackageVersionsPath)) { return @() }
    [xml]$xml = Get-Content -LiteralPath $centralPackageVersionsPath -Raw
    $versions = @()
    foreach ($package in @($xml.Project.SelectNodes("ItemGroup/PackageVersion"))) {
        if ($null -eq $package) { continue }
        $versions += [pscustomobject]@{
            Id = $package.GetAttribute("Include")
            Version = $package.GetAttribute("Version")
            Condition = $package.GetAttribute("Condition")
        }
    }
    return @($versions)
}

$centralPackageVersions = @(Read-CentralPackageVersions)

function Test-CentralPackageVersionCondition {
    param([string]$Condition, [string]$ProjectName)
    if ([string]::IsNullOrWhiteSpace($Condition)) { return $true }
    $equals = "'`$(MSBuildProjectName)' == '$ProjectName'"
    $notEquals = "'`$(MSBuildProjectName)' != '$ProjectName'"
    if ($Condition -eq $equals) { return $true }
    if ($Condition -eq $notEquals) { return $false }
    if ($Condition -match "^'\$\(MSBuildProjectName\)' != '(.+)'$") { return $Matches[1] -ne $ProjectName }
    return $false
}

function Get-CentralPackageVersion {
    param([string]$Id, [string]$ProjectName)
    $selected = ""
    foreach ($package in $centralPackageVersions) {
        if ($package.Id -ne $Id) { continue }
        if (Test-CentralPackageVersionCondition -Condition $package.Condition -ProjectName $ProjectName) {
            $selected = $package.Version
        }
    }
    return $selected
}

function Get-DomainGroup {
    param([string]$ProjectName)
    if ($ProjectName -eq "Server") { return "Elsa.Server" }
    if ($ProjectName -like "Test.*") { return "Test" }
    if ($ProjectName -like "Elsa3.*") { return "Elsa3" }
    if ($ProjectName -notlike "Elsa.*") { return "Other" }
    $parts = $ProjectName.Split(".")
    if ($parts.Count -ge 2) { return "$($parts[0]).$($parts[1])" }
    return $ProjectName
}

function Get-OwnerProject {
    param([System.IO.FileInfo]$File)
    $dir = $File.Directory
    while ($null -ne $dir) {
        $project = Get-ChildItem -LiteralPath $dir.FullName -Filter "*.csproj" -File | Select-Object -First 1
        if ($null -ne $project) { return $project }
        if ($dir.FullName -eq $repoRoot) { break }
        $dir = $dir.Parent
    }
    return $null
}

function Read-Projects {
    $projects = @()
    foreach ($file in Get-RepoFiles @("src/*.csproj")) {
        [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $refs = @()
        $packages = @()
        foreach ($reference in @($xml.Project.SelectNodes("ItemGroup/ProjectReference"))) {
            if ($null -ne $reference -and -not [string]::IsNullOrWhiteSpace($reference.Include)) {
                # MSBuild backslash paths; normalize for non-Windows.
                $refs += [System.IO.Path]::GetFileNameWithoutExtension(($reference.Include -replace '\\', '/'))
            }
        }
        foreach ($package in @($xml.Project.SelectNodes("ItemGroup/PackageReference"))) {
            if ($null -eq $package) { continue }
            $id = $package.GetAttribute("Include")
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            $version = $package.GetAttribute("Version")
            if ([string]::IsNullOrWhiteSpace($version)) { $version = Get-CentralPackageVersion -Id $id -ProjectName $projectName }
            if ([string]::IsNullOrWhiteSpace($version)) { $version = "(unspecified)" }
            $packages += "$id $version"
        }

        $projects += [pscustomobject]@{
            Name = $projectName
            Path = To-RepoPath $file.FullName
            Domain = Get-DomainGroup $projectName
            References = @($refs | Sort-Object -Unique)
            Packages = @($packages | Sort-Object -Unique)
        }
    }
    return @($projects | Sort-Object Domain, Name)
}

function Get-ShellFeatureName {
    param([string]$Text, [string]$ClassName)
    $pattern = "(?s)\[ShellFeature\((?<args>.*?)\)\]\s*(?:\[[^\]]+\]\s*)*public\s+(?:(?:abstract|sealed|partial)\s+)*class\s+$ClassName\b"
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) { return "" }

    $args = $match.Groups["args"].Value
    $named = [regex]::Match($args, 'name\s*:\s*"([^"]+)"')
    if ($named.Success) { return $named.Groups[1].Value }

    $positional = [regex]::Match($args, '^\s*"([^"]+)"')
    if ($positional.Success) { return $positional.Groups[1].Value }

    return ""
}

function Get-FeatureProperties {
    param([string]$Text, [string]$ClassName)
    $classMatch = [regex]::Match($Text, "(?s)public\s+(?:(?:abstract|sealed|partial)\s+)*class\s+$ClassName\b.*?\{(?<body>.*)\n\s*\}")
    if (-not $classMatch.Success) { return @() }

    $properties = @()
    foreach ($match in [regex]::Matches($classMatch.Groups["body"].Value, '(?m)^\s*public\s+(?<type>[A-Za-z0-9_<>,\?\.\[\] ]+)\s+(?<name>[A-Za-z0-9_]+)\s*\{\s*get;\s*set;\s*\}')) {
        $properties += "$($match.Groups["name"].Value): $($match.Groups["type"].Value.Trim())"
    }
    return @($properties | Sort-Object -Unique)
}

function Get-FeatureOptionSignals {
    param([string]$Text)
    $signals = @()
    if ($Text -match 'FeatureConfigurationException|requires exactly one|requires a non-empty|must specify|not configured') {
        $signals += "validation/requiredness guard in code"
    }
    if ($Text -match 'Path\.GetTempPath|DefaultConnectionString|TimeSpan\.From|=\s*true|=\s*false|=\s*100|=\s*string\.Empty') {
        $signals += "code default"
    }
    if ($Text -match 'ConnectionString|Secret|Password|Token|Key') {
        $signals += "sensitive or deployment-specific value signal"
    }
    if ($Text -match 'FolderPath|Directory|FilePath|LocalCacheDirectory|LocksFolderPath') {
        $signals += "filesystem path signal"
    }
    if ($Text -match 'Type\s*\{|TypeName|Type\s*=|GetLoadedType') {
        $signals += "type-name selection signal"
    }
    return @($signals | Sort-Object -Unique)
}

function Read-Features {
    $features = @()
    $classPattern = '(?m)^\s*public\s+(?:(?<abstract>abstract)\s+)?(?:(?:sealed|partial)\s+)*class\s+(?<class>\w+)\s*:\s*(?<base>[^{\r\n]+)'

    foreach ($file in Get-RepoFiles @("src/*.cs")) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($text, $classPattern)) {
            $className = $match.Groups["class"].Value
            $baseText = ($match.Groups["base"].Value -replace '\s+', ' ').Trim()
            if ($baseText -notmatch 'IShellFeature|FastEndpointsFeatureBase|EFCore.*FeatureBase|FeatureBase') { continue }

            $projectFile = Get-OwnerProject $file
            if ($null -eq $projectFile) { continue }

            $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
            $featureId = Get-ShellFeatureName -Text $text -ClassName $className
            $isAbstract = $match.Groups["abstract"].Success
            $features += [pscustomobject]@{
                Class = $className
                FeatureId = $(if ([string]::IsNullOrWhiteSpace($featureId)) { "-" } else { $featureId })
                IsAbstract = $isAbstract
                Base = $baseText
                Project = $projectName
                Domain = Get-DomainGroup $projectName
                Path = To-RepoPath $file.FullName
                Properties = @(Get-FeatureProperties -Text $text -ClassName $className)
                OptionSignals = @(Get-FeatureOptionSignals -Text $text)
            }
        }
    }

    return @($features | Sort-Object Domain, Project, Class)
}

function Write-Lines {
    param([string]$Path, [string[]]$Lines)
    [System.IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

New-Item -ItemType Directory -Force -Path $docsMaps | Out-Null

$projects = Read-Projects
$features = Read-Features
$featureProjects = @{}
foreach ($feature in $features) {
    if (-not $featureProjects.ContainsKey($feature.Project)) { $featureProjects[$feature.Project] = @() }
    $featureProjects[$feature.Project] += $feature
}
$projectByName = @{}
foreach ($project in $projects) { $projectByName[$project.Name] = $project }

$activatedFeatureIds = @()
$appsettingsPath = Join-Path $repoRoot "src\Server\appsettings.json"
if (Test-Path -LiteralPath $appsettingsPath) {
    $appsettings = Get-Content -LiteralPath $appsettingsPath -Raw | ConvertFrom-Json
    $shells = $appsettings.CShells.Shells
    if ($null -ne $shells -and $null -ne $shells.default -and $null -ne $shells.default.Features) {
        $activatedFeatureIds = @($shells.default.Features.PSObject.Properties.Name | Sort-Object)
    }
}

$missingConcreteIds = @($features | Where-Object { -not $_.IsAbstract -and $_.FeatureId -eq "-" })
$duplicateIds = @($features | Where-Object { $_.FeatureId -ne "-" } | Group-Object FeatureId | Where-Object Count -gt 1)

$lines = @(
    "# Feature Dependency Map",
    "",
    'Generated by `tools/maps/generate-feature-dependency-map`.',
    "",
    'Records CShells feature identity, public feature properties, and dependency evidence from direct project/package references. It does not generate appsettings JSON and does not treat `src/Apps/Elsa.Server` composition choices as canonical.',
    "",
    "## Summary",
    "",
    "- Feature classes: $($features.Count)",
    "- Concrete features missing explicit ShellFeature ID: $($missingConcreteIds.Count)",
    "- Duplicate explicit feature IDs: $($duplicateIds.Count)",
    "- Feature-bearing source projects: $($featureProjects.Keys.Count)",
    '- IConfiguration feature-registration shape observed from: `src/Apps/Elsa.Server/appsettings.json`',
    "",
    "## IConfiguration Shape Evidence",
    "",
    'The test shell is not a composition source of truth. The only evidence taken from `src/Apps/Elsa.Server/appsettings.json` is the JSON shape: `CShells:Shells:{shellName}:Features:{featureId}` with optional nested feature settings such as `Options`, plus shell-level `Configuration`.',
    "",
    "Observed default-shell feature keys:",
    ""
)

if ($activatedFeatureIds.Count -eq 0) {
    $lines += "- none"
} else {
    foreach ($featureId in $activatedFeatureIds) {
        $lines += "- ``" + $featureId + "``"
    }
}

$lines += @(
    "",
    "## Duplicate Feature IDs",
    ""
)

if ($duplicateIds.Count -eq 0) {
    $lines += "No duplicate explicit feature IDs were discovered."
} else {
    $lines += @("| Feature ID | Feature classes | Projects |", "|---|---|---|")
    foreach ($group in $duplicateIds | Sort-Object Name) {
        $classes = ($group.Group | Sort-Object Class | ForEach-Object { $_.Class }) -join "<br>"
        $projects = ($group.Group | Sort-Object Project | ForEach-Object { $_.Project }) -join "<br>"
        $lines += "| $($group.Name) | $(Escape-Cell $classes) | $(Escape-Cell $projects) |"
    }
}

$lines += @(
    "",
    "## Feature Identity And Configuration Inputs",
    "",
    "| Feature ID | Feature class | Abstract | Project | Public feature properties | Option/config signals | File |",
    "|---|---|---|---|---|---|---|"
)

foreach ($feature in $features) {
    $props = if ($feature.Properties.Count -eq 0) { "-" } else { $feature.Properties -join "<br>" }
    $signals = if ($feature.OptionSignals.Count -eq 0) { "-" } else { $feature.OptionSignals -join "<br>" }
    $lines += "| $($feature.FeatureId) | $($feature.Class) | $($feature.IsAbstract) | $($feature.Project) | $(Escape-Cell $props) | $(Escape-Cell $signals) | [$([System.IO.Path]::GetFileName($feature.Path))](../../$($feature.Path)) |"
}

$lines += @(
    "",
    "## Feature Dependency Evidence",
    "",
    "Rows below are dependency evidence, not final policy. `Feature-project references` means the feature's owning project directly references another project that also declares one or more feature classes. `Core/helper references` are still important runtime prerequisites, but they are not feature activation IDs by themselves.",
    "",
    "| Feature ID | Project | Feature-project references | Core/helper project references | Direct external packages |",
    "|---|---|---|---|---|"
)

foreach ($feature in $features | Where-Object { -not $_.IsAbstract }) {
    $project = $projectByName[$feature.Project]
    if ($null -eq $project) { continue }

    $featureRefs = @()
    $coreRefs = @()
    foreach ($ref in $project.References) {
        if ($featureProjects.ContainsKey($ref)) {
            $ids = @($featureProjects[$ref] | ForEach-Object { if ($_.FeatureId -ne "-") { $_.FeatureId } else { $_.Class } } | Sort-Object -Unique)
            $featureRefs += "$ref ($($ids -join ', '))"
        } else {
            $coreRefs += $ref
        }
    }

    $featureRefCell = if ($featureRefs.Count -eq 0) { "-" } else { $featureRefs -join "<br>" }
    $coreRefCell = if ($coreRefs.Count -eq 0) { "-" } else { $coreRefs -join "<br>" }
    $packageCell = if ($project.Packages.Count -eq 0) { "-" } else { $project.Packages -join "<br>" }
    $lines += "| $($feature.FeatureId) | $($feature.Project) | $(Escape-Cell $featureRefCell) | $(Escape-Cell $coreRefCell) | $(Escape-Cell $packageCell) |"
}

$lines += @(
    "",
    "## Gaps For Generator Work",
    "",
    "- Direct project references do not prove that a referenced feature must be activated in the same shell.",
    "- Public feature properties expose possible IConfiguration keys, but required/default/secret/path/type-name classification still needs an architecture decision.",
    "- Feature IDs must stay explicit on concrete features; abstract feature bases may remain unannotated unless base metadata becomes useful.",
    "- Nuplane assembly-loading settings are host/loading evidence, not feature-selection evidence.",
    "- A CShells appsettings generator should consume this map plus an approved configuration/settings classification before emitting JSON."
)

Write-Lines -Path (Join-Path $docsMaps "feature-dependency-map.md") -Lines $lines
Write-Host "Generated maps:"
Write-Host " - docs/maps/feature-dependency-map.md"
