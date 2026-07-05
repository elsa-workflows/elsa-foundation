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

function Get-ProjectNameFromInclude {
    param([string]$Include)
    # Include paths use MSBuild backslashes; normalize so the name extraction
    # also works on non-Windows filesystems.
    return [System.IO.Path]::GetFileNameWithoutExtension(($Include -replace '\\', '/'))
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

function Get-SubDomain {
    param([string]$ProjectName, [string]$Domain)
    if ($Domain -in @("Elsa.Server", "Test", "Elsa3", "Other")) { return "-" }
    if (-not $ProjectName.StartsWith($Domain)) { return "-" }
    $tail = $ProjectName.Substring($Domain.Length).TrimStart(".")
    if ([string]::IsNullOrWhiteSpace($tail)) { return "(root)" }
    return $tail
}

function Get-Role {
    param([string]$ProjectName, [string]$Path, [string]$Kind)
    if ($Kind -eq "test") { return "test" }
    if ($ProjectName -eq "Elsa.Server") { return "host" }
    if ($ProjectName -like "*.Core") { return "contract" }
    if ($ProjectName -like "*.Strategies" -or $ProjectName -like "*.Schedules" -or $ProjectName -like "*.Libraries") { return "helper" }
    if ($ProjectName -match "\.(Sqlite|EFCore|FileSystem|Jint|Liquid)$") { return "provider/implementation" }
    if ($ProjectName -match "\.(Api|Http|JavaScript|Rendering|Runtime|Design|Reconciliation|Validations|Persistence|Publishing|Primitives|Composition)(\.|$)") { return "feature/implementation" }
    return "feature/implementation"
}

function Read-Projects {
    $projectFiles = Get-RepoFiles @("src/*.csproj", "tests/*.csproj")

    $projects = @()
    foreach ($file in $projectFiles) {
        [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $path = To-RepoPath $file.FullName
        $kind = if ($path -like "tests/*") { "test" } else { "source" }
        $domain = Get-DomainGroup $projectName
        $refs = @()
        foreach ($reference in @($xml.Project.SelectNodes("ItemGroup/ProjectReference"))) {
            if ($null -ne $reference -and -not [string]::IsNullOrWhiteSpace($reference.Include)) {
                $refs += Get-ProjectNameFromInclude $reference.Include
            }
        }
        $projects += [pscustomobject]@{
            Name = $projectName
            Path = $path
            Kind = $kind
            Domain = $domain
            SubDomain = Get-SubDomain $projectName $domain
            Role = Get-Role $projectName $path $kind
            References = @($refs | Sort-Object -Unique)
        }
    }
    return @($projects | Sort-Object Kind, Domain, Name)
}

function Write-Lines {
    param([string]$Path, [string[]]$Lines)
    [System.IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

New-Item -ItemType Directory -Force -Path $docsMaps | Out-Null
$projects = Read-Projects
$byName = @{}
foreach ($project in $projects) { $byName[$project.Name] = $project }

$crossRows = @()
foreach ($project in $projects) {
    foreach ($refName in $project.References) {
        if (-not $byName.ContainsKey($refName)) { continue }
        $target = $byName[$refName]
        if ($project.Domain -ne $target.Domain) {
            $crossRows += [pscustomobject]@{ From = $project.Name; FromPath = $project.Path; To = $target.Name; ToPath = $target.Path; FromDomain = $project.Domain; ToDomain = $target.Domain }
        }
    }
}

$lines = @(
    "# Domain Map",
    "",
    'Generated by `tools/maps/generate-domain-map`.',
    "",
    "Records project grouping and direct reference facts only. Roles are heuristic navigation labels, not constitution verdicts.",
    "",
    "## Summary",
    "",
    "- Source projects: $(@($projects | Where-Object Kind -eq 'source').Count)",
    "- Test projects: $(@($projects | Where-Object Kind -eq 'test').Count)",
    "- Domains: $(@($projects | Select-Object -ExpandProperty Domain -Unique).Count)",
    "- Direct cross-domain references: $($crossRows.Count)",
    "",
    "## Domains",
    "",
    "| Domain | Source projects | Test projects | Roles |",
    "|---|---:|---:|---|"
)

foreach ($group in $projects | Group-Object Domain | Sort-Object Name) {
    $sourceCount = @($group.Group | Where-Object Kind -eq "source").Count
    $testCount = @($group.Group | Where-Object Kind -eq "test").Count
    $roles = ($group.Group | Select-Object -ExpandProperty Role -Unique | Sort-Object) -join "<br>"
    $lines += "| $($group.Name) | $sourceCount | $testCount | $(Escape-Cell $roles) |"
}

$lines += @("", "## Projects", "", "| Project | Kind | Domain | Sub-domain | Role | Direct references |", "|---|---|---|---|---|---|")
foreach ($project in $projects) {
    $refs = if ($project.References.Count -eq 0) { "-" } else { $project.References -join "<br>" }
    $lines += "| [$($project.Name)](../../$($project.Path)) | $($project.Kind) | $($project.Domain) | $(Escape-Cell $project.SubDomain) | $($project.Role) | $(Escape-Cell $refs) |"
}

$lines += @("", "## Direct Cross-Domain References", "", "| From | From domain | To | To domain |", "|---|---|---|---|")
foreach ($row in $crossRows | Sort-Object FromDomain, From, ToDomain, To) {
    $lines += "| [$($row.From)](../../$($row.FromPath)) | $($row.FromDomain) | [$($row.To)](../../$($row.ToPath)) | $($row.ToDomain) |"
}

Write-Lines -Path (Join-Path $docsMaps "domain-map.md") -Lines $lines
Write-Host "Generated maps:"
Write-Host " - docs/maps/domain-map.md"
