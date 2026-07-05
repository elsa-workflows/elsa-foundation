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

function Get-DomainGroup {
    param([string]$ProjectName)
    if ($ProjectName -eq "Server") { return "Elsa.Server" }
    if ($ProjectName -like "Elsa3.*") { return "Elsa3" }
    if ($ProjectName -notlike "Elsa.*") { return "Other" }
    $parts = $ProjectName.Split(".")
    if ($parts.Count -ge 2) { return "$($parts[0]).$($parts[1])" }
    return $ProjectName
}

function Test-RepoIgnored {
    param([string]$Path)
    try {
        & git -C $repoRoot check-ignore -q $Path 2>$null
        return ($LASTEXITCODE -eq 0)
    } catch { return $false }
}

# Owner lookup stays inside the repo and skips gitignored scratch csprojs so
# machine-local files cannot claim ownership of a catalog.
function Get-OwnerProjectName {
    param([System.IO.FileInfo]$File)
    $dir = $File.Directory
    while ($null -ne $dir) {
        $project = Get-ChildItem -LiteralPath $dir.FullName -Filter "*.csproj" -File |
            Sort-Object Name |
            Where-Object { -not (Test-RepoIgnored $_.FullName) } |
            Select-Object -First 1
        if ($null -ne $project) { return [System.IO.Path]::GetFileNameWithoutExtension($project.Name) }
        if ($dir.FullName -eq $repoRoot) { break }
        $dir = $dir.Parent
    }
    return "-"
}

function Read-Catalog {
    param([System.IO.FileInfo]$File)
    $text = Get-Content -LiteralPath $File.FullName -Raw
    $headings = @([regex]::Matches($text, '(?m)^###\s+(.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() })
    $sections = @([regex]::Matches($text, '(?m)^##\s+(.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() })
    $kinds = @([regex]::Matches($text, '(?m)^-\s+\*\*Kind:\*\*\s*(.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() })
    $knownCount = ([regex]::Matches($text, '(?m)^\*\*Known implementations')).Count
    $project = Get-OwnerProjectName $File
    [pscustomobject]@{
        Path = To-RepoPath $File.FullName
        Project = $project
        Domain = if ($project -eq "-") { "repo root" } else { Get-DomainGroup $project }
        Sections = @($sections | Where-Object { $_ -notmatch '^Cross-references$|^Constitutional basis$' })
        ExtensionPoints = @($headings)
        Kinds = @($kinds)
        KnownImplementationBlocks = $knownCount
    }
}

function Read-RootIndexedCatalogs {
    $rootIndex = Join-Path $repoRoot "EXTENSION_POINTS.md"
    if (-not (Test-Path -LiteralPath $rootIndex)) { return @() }
    $text = Get-Content -LiteralPath $rootIndex -Raw
    $paths = @()
    foreach ($match in [regex]::Matches($text, '\]\((src/[^)]+/EXTENSION_POINTS\.md)\)')) {
        $paths += $match.Groups[1].Value
    }
    return @($paths | Sort-Object -Unique)
}

function Write-Lines {
    param([string]$Path, [string[]]$Lines)
    [System.IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

New-Item -ItemType Directory -Force -Path $docsMaps | Out-Null
$catalogFiles = @()
$rootCatalog = Join-Path $repoRoot "EXTENSION_POINTS.md"
if (Test-Path -LiteralPath $rootCatalog) { $catalogFiles += Get-Item -LiteralPath $rootCatalog }
$catalogFiles += Get-RepoFiles @("src/EXTENSION_POINTS.md", "src/*/EXTENSION_POINTS.md")
$catalogs = @($catalogFiles | Sort-Object FullName | ForEach-Object { Read-Catalog $_ })
$indexed = Read-RootIndexedCatalogs
$discoveredSrc = @($catalogs | Where-Object { $_.Path -like "src/*" } | Select-Object -ExpandProperty Path)
$indexedSet = @{}
foreach ($path in $indexed) { $indexedSet[$path] = $true }
$discoveredSet = @{}
foreach ($path in $discoveredSrc) { $discoveredSet[$path] = $true }
$notIndexed = @($discoveredSrc | Where-Object { -not $indexedSet.ContainsKey($_) } | Sort-Object)
$missing = @($indexed | Where-Object { -not $discoveredSet.ContainsKey($_) } | Sort-Object)

$lines = @(
    "# Extension-Point Map",
    "",
    'Generated by `tools/maps/generate-extension-point-map`.',
    "",
    "Records Markdown catalog facts from `EXTENSION_POINTS.md` files. It does not validate catalog completeness.",
    "",
    "## Summary",
    "",
    "- Catalog files discovered: $($catalogs.Count)",
    "- Source catalogs discovered: $($discoveredSrc.Count)",
    "- Source catalogs indexed from root: $($indexed.Count)",
    "- Discovered source catalogs not linked from root index: $($notIndexed.Count)",
    "- Root-indexed catalogs missing on disk: $($missing.Count)",
    "",
    "## Catalogs",
    "",
    "| Catalog | Owner project | Domain | Sections | Extension-point headings | Kind lines | Known implementation blocks | Root indexed |",
    "|---|---|---|---|---|---|---:|---|"
)
foreach ($catalog in $catalogs) {
    $rootIndexed = if ($catalog.Path -eq "EXTENSION_POINTS.md") { "root" } elseif ($indexedSet.ContainsKey($catalog.Path)) { "yes" } else { "no" }
    $lines += "| [$($catalog.Path)](../../$($catalog.Path)) | $($catalog.Project) | $($catalog.Domain) | $(Escape-Cell ($catalog.Sections -join '<br>')) | $(Escape-Cell ($catalog.ExtensionPoints -join '<br>')) | $(Escape-Cell ($catalog.Kinds -join '<br>')) | $($catalog.KnownImplementationBlocks) | $rootIndexed |"
}

$lines += @("", "## Root Index Coverage", "", "| Status | Catalog |", "|---|---|")
foreach ($path in $notIndexed) { $lines += "| discovered but not root-indexed | [$path](../../$path) |" }
foreach ($path in $missing) { $lines += "| root-indexed but missing on disk | $path |" }
if ($notIndexed.Count -eq 0 -and $missing.Count -eq 0) { $lines += "| ok | Root index and discovered source catalogs match. |" }

Write-Lines -Path (Join-Path $docsMaps "extension-point-map.md") -Lines $lines
Write-Host "Generated maps:"
Write-Host " - docs/maps/extension-point-map.md"
