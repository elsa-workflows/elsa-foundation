Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$docsMaps = Join-Path $repoRoot "docs\maps"
$docsReports = Join-Path $repoRoot "docs\reports"

function To-RepoPath {
    param([string]$Path)
    $full = (Resolve-Path $Path).Path
    return ($full.Substring($repoRoot.Length + 1) -replace '\\', '/')
}

function Escape-Cell {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "-" }
    return (($Value -replace '\|', '\|') -replace "`r?`n", "<br>")
}

function Get-XmlValue {
    param([System.Xml.XmlElement]$Node, [string]$Name)
    $attribute = $Node.GetAttribute($Name)
    if (-not [string]::IsNullOrWhiteSpace($attribute)) { return $attribute }
    $child = $Node.SelectSingleNode($Name)
    if ($null -ne $child) { return $child.InnerText }
    return ""
}

function Get-ChildText {
    param([System.Xml.XmlElement]$Node, [string]$Name)
    $child = $Node.SelectSingleNode($Name)
    if ($null -ne $child -and -not [string]::IsNullOrWhiteSpace($child.InnerText)) {
        return $child.InnerText
    }
    return ""
}

function Get-DomainGroup {
    param([string]$ProjectName)
    if ($ProjectName -eq "Server") { return "Server" }
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
        $dir = $dir.Parent
    }
    return $null
}

function Read-Projects {
    $projectFiles = @()
    foreach ($root in @("src", "tests")) {
        $dir = Join-Path $repoRoot $root
        if (Test-Path -LiteralPath $dir) {
            $projectFiles += Get-ChildItem -LiteralPath $dir -Recurse -Filter "*.csproj" | Sort-Object FullName
        }
    }

    $projects = @()
    foreach ($file in $projectFiles) {
        [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
        $propertyGroups = @($xml.Project.PropertyGroup)
        $targetFramework = ($propertyGroups | ForEach-Object { Get-ChildText -Node $_ -Name "TargetFramework" } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
        if ([string]::IsNullOrWhiteSpace($targetFramework)) { $targetFramework = "-" }
        $isPackable = ($propertyGroups | ForEach-Object { Get-ChildText -Node $_ -Name "IsPackable" } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
        if ([string]::IsNullOrWhiteSpace($isPackable)) { $isPackable = "default" }

        $projectReferences = @()
        $packageReferences = @()
        foreach ($itemGroup in @($xml.Project.SelectNodes("ItemGroup"))) {
            foreach ($reference in @($itemGroup.SelectNodes("ProjectReference"))) {
                if ($null -eq $reference) { continue }
                $include = $reference.Include
                if (-not [string]::IsNullOrWhiteSpace($include)) {
                    $projectReferences += [pscustomobject]@{
                        Include = $include
                        ProjectName = [System.IO.Path]::GetFileNameWithoutExtension($include)
                    }
                }
            }

            foreach ($package in @($itemGroup.SelectNodes("PackageReference"))) {
                if ($null -eq $package) { continue }
                $include = Get-XmlValue -Node $package -Name "Include"
                $version = Get-XmlValue -Node $package -Name "Version"
                if (-not [string]::IsNullOrWhiteSpace($include)) {
                    $packageReferences += [pscustomobject]@{
                        Id = $include
                        Version = $(if ([string]::IsNullOrWhiteSpace($version)) { "(unspecified)" } else { $version })
                    }
                }
            }
        }

        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $projects += [pscustomobject]@{
            Name = $projectName
            Path = To-RepoPath $file.FullName
            Kind = $(if ($file.FullName -like "*\tests\*") { "test" } else { "source" })
            Domain = Get-DomainGroup $projectName
            TargetFramework = $targetFramework
            IsPackable = $isPackable
            ProjectReferences = @($projectReferences | Sort-Object ProjectName)
            PackageReferences = @($packageReferences | Sort-Object Id, Version)
        }
    }

    return @($projects | Sort-Object Kind, Domain, Name)
}

function Read-Features {
    $features = @()
    $sourceRoot = Join-Path $repoRoot "src"
    if (-not (Test-Path -LiteralPath $sourceRoot)) { return @() }

    $pattern = '(?m)^\s*public\s+(?:(?:abstract|sealed|partial)\s+)*class\s+(\w+)\s*:\s*([^{\r\n]+)'
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.cs" | Sort-Object FullName) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($text, $pattern)) {
            $className = $match.Groups[1].Value
            $baseText = ($match.Groups[2].Value -replace '\s+', ' ').Trim()
            if ($baseText -notmatch 'IShellFeature|FastEndpointsFeatureBase|EFCore.*FeatureBase|FeatureBase') { continue }
            $projectFile = Get-OwnerProject $file
            if ($null -eq $projectFile) { continue }
            $kind = "feature-base-derived"
            if ($baseText -match '\bIShellFeature\b') { $kind = "direct IShellFeature" }
            elseif ($baseText -match '\bFastEndpointsFeatureBase\b') { $kind = "FastEndpoints feature" }
            elseif ($baseText -match '\bEFCore.*FeatureBase') { $kind = "EF Core feature base" }
            $features += [pscustomobject]@{
                Class = $className
                Kind = $kind
                Base = $baseText
                Project = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
                Path = To-RepoPath $file.FullName
            }
        }
    }

    return @($features | Sort-Object Project, Class)
}

function Read-Specs {
    $specs = @()
    $specRoot = Join-Path $repoRoot "specs"
    if (-not (Test-Path -LiteralPath $specRoot)) { return @() }

    foreach ($dir in Get-ChildItem -LiteralPath $specRoot -Directory | Sort-Object Name) {
        $specFile = Join-Path $dir.FullName "spec.md"
        $planFile = Join-Path $dir.FullName "plan.md"
        $tasksFile = Join-Path $dir.FullName "tasks.md"
        $title = $dir.Name
        $status = "unknown"
        $notes = @()

        if (Test-Path -LiteralPath $specFile) {
            $text = Get-Content -LiteralPath $specFile -Raw
            $titleMatch = [regex]::Match($text, '(?m)^#\s+Feature Specification:\s*(.+)$')
            if ($titleMatch.Success) { $title = $titleMatch.Groups[1].Value.Trim() }
            $statusMatch = [regex]::Match($text, '(?m)^\*\*Status\*\*:\s*(.+)$')
            if ($statusMatch.Success) { $status = $statusMatch.Groups[1].Value.Trim() }
            $lower = $text.ToLowerInvariant()
            foreach ($keyword in @("superseded", "retained", "deferred", "out of scope", "construct-only")) {
                if ($lower.Contains($keyword)) { $notes += $keyword }
            }
        }

        $planVerdict = "-"
        if (Test-Path -LiteralPath $planFile) {
            $planText = Get-Content -LiteralPath $planFile -Raw
            if ($planText -match "(?:Initial )?Constitution Check verdict:\s*([^\r\n]+)") {
                $planVerdict = $Matches[1].Trim()
            }
        }

        $done = 0
        $open = 0
        if (Test-Path -LiteralPath $tasksFile) {
            $tasksText = Get-Content -LiteralPath $tasksFile -Raw
            $done = ([regex]::Matches($tasksText, '(?m)^\s*-\s+\[[xX]\]\s+T\d+')).Count
            $open = ([regex]::Matches($tasksText, '(?m)^\s*-\s+\[\s\]\s+T\d+')).Count
        }

        $specs += [pscustomobject]@{
            Id = $dir.Name
            Title = $title
            Status = $status
            PlanVerdict = $planVerdict
            TasksDone = $done
            TasksOpen = $open
            Notes = $(if ($notes.Count -eq 0) { "-" } else { ($notes | Sort-Object -Unique) -join ", " })
        }
    }

    return @($specs)
}

function Write-Lines {
    param([string]$Path, [string[]]$Lines)
    [System.IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

function Get-StringSha256 {
    param([string]$Value)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return ([System.BitConverter]::ToString($hash) -replace "-", "").ToLowerInvariant()
}

function Get-InputFiles {
    $files = @()
    $srcRoot = Join-Path $repoRoot "src"
    $testsRoot = Join-Path $repoRoot "tests"
    $specsRoot = Join-Path $repoRoot "specs"

    if (Test-Path -LiteralPath $srcRoot) {
        $files += Get-ChildItem -LiteralPath $srcRoot -Recurse -File -Include "*.csproj", "*.cs"
    }

    if (Test-Path -LiteralPath $testsRoot) {
        $files += Get-ChildItem -LiteralPath $testsRoot -Recurse -File -Filter "*.csproj"
    }

    if (Test-Path -LiteralPath $specsRoot) {
        $files += Get-ChildItem -LiteralPath $specsRoot -Recurse -File -Filter "*.md"
    }

    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-maps.ps1")
    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-maps.sh")
    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-domain-map.ps1")
    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-domain-map.sh")
    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-extension-point-map.ps1")
    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-extension-point-map.sh")
    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-architecture-reference-map.ps1")
    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-architecture-reference-map.sh")
    $files += Get-Item -LiteralPath (Join-Path $repoRoot "tools\maps\generate-feature-dependency-map.ps1")

    return @($files | Sort-Object FullName -Unique)
}

function Get-InputFingerprint {
    param([System.IO.FileInfo[]]$Files)
    $parts = @()
    foreach ($file in $Files) {
        $relative = To-RepoPath $file.FullName
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $parts += "$relative=$hash"
    }
    return Get-StringSha256 ($parts -join "`n")
}

function Get-GitHead {
    try {
        $head = (& git -C $repoRoot rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($head)) { return $head.Trim() }
    } catch {}
    return $null
}

function Get-RelevantGitDirty {
    try {
        $status = & git -C $repoRoot status --porcelain -- src tests specs tools/maps 2>$null
        if ($LASTEXITCODE -eq 0) { return @($status).Count -gt 0 }
    } catch {}
    return $null
}

function Write-Manifest {
    param([object[]]$Projects, [object[]]$Features, [object[]]$Specs)
    $inputFiles = Get-InputFiles
    $manifest = [ordered]@{
        schema_version = "1.0"
        generator = "tools/maps/generate-maps"
        freshness_rule = "Maps are fresh when input_fingerprint matches the current inputs. git_head is advisory because generated maps may be committed with their inputs."
        git_head = Get-GitHead
        relevant_inputs_dirty = Get-RelevantGitDirty
        input_fingerprint = Get-InputFingerprint $inputFiles
        input_file_count = $inputFiles.Count
        counts = [ordered]@{
            source_projects = @($Projects | Where-Object Kind -eq "source").Count
            test_projects = @($Projects | Where-Object Kind -eq "test").Count
            direct_project_references = [int](($Projects | ForEach-Object { $_.ProjectReferences.Count } | Measure-Object -Sum).Sum)
            direct_package_references = [int](($Projects | ForEach-Object { $_.PackageReferences.Count } | Measure-Object -Sum).Sum)
            feature_classes = $Features.Count
            specs = $Specs.Count
        }
        generated_files = @(
            "docs/maps/project-reference-map.md",
            "docs/maps/package-map.md",
            "docs/maps/feature-map.md",
            "docs/maps/test-map.md",
            "docs/maps/spec-status-map.md",
            "docs/maps/domain-map.md",
            "docs/maps/extension-point-map.md",
            "docs/maps/architecture-reference-map.md",
            "docs/maps/feature-dependency-map.md",
            "docs/reports/maps-v2-findings.md",
            "docs/reports/maps-v1-findings.md"
        )
    }

    $json = $manifest | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText((Join-Path $docsMaps "manifest.json"), ($json + "`n"), [System.Text.UTF8Encoding]::new($false))
}

function Get-PackageRows {
    param([object[]]$Projects)
    $rows = @()
    foreach ($project in $Projects) {
        foreach ($package in $project.PackageReferences) {
            $rows += [pscustomobject]@{
                Id = $package.Id
                Version = $package.Version
                Project = $project.Name
                Path = $project.Path
                Kind = $project.Kind
            }
        }
    }
    return @($rows | Sort-Object Project, Id, Version)
}

function Write-ProjectReferenceMap {
    param([object[]]$Projects)
    $sourceCount = @($Projects | Where-Object Kind -eq 'source').Count
    $testCount = @($Projects | Where-Object Kind -eq 'test').Count
    $directReferenceCount = ($Projects | ForEach-Object { $_.ProjectReferences.Count } | Measure-Object -Sum).Sum
    $lines = @("# Project Reference Map", "", 'Generated by `tools/maps/generate-maps`.', "", "Records direct project references only.", "", "## Summary", "", "- Source projects: $sourceCount", "- Test projects: $testCount", "- Direct project references: $directReferenceCount", "", "## Projects", "", "| Project | Kind | Domain | Target | Packable | Direct project references |", "|---|---|---|---|---|---|")
    foreach ($project in $Projects) {
        $refs = if ($project.ProjectReferences.Count -eq 0) { "-" } else { ($project.ProjectReferences.ProjectName -join "<br>") }
        $lines += "| [$($project.Name)](../../$($project.Path)) | $($project.Kind) | $($project.Domain) | $($project.TargetFramework) | $($project.IsPackable) | $(Escape-Cell $refs) |"
    }
    $lines += @("", "## Domain Groups", "", "| Domain | Source projects | Test projects |", "|---|---:|---:|")
    foreach ($group in $Projects | Group-Object Domain | Sort-Object Name) {
        $groupSourceCount = @($group.Group | Where-Object Kind -eq 'source').Count
        $groupTestCount = @($group.Group | Where-Object Kind -eq 'test').Count
        $lines += "| $($group.Name) | $groupSourceCount | $groupTestCount |"
    }
    Write-Lines -Path (Join-Path $docsMaps "project-reference-map.md") -Lines $lines
}

function Write-PackageMap {
    param([object[]]$Projects)
    $rows = Get-PackageRows $Projects
    $lines = @("# Package Map", "", 'Generated by `tools/maps/generate-maps`.', "", 'Records direct `PackageReference` entries only; it does not compute transitive closure.', "", "## Package Version Clusters", "", "| Package | Versions | Projects |", "|---|---|---|")
    foreach ($group in $rows | Group-Object Id | Sort-Object Name) {
        $versions = ($group.Group | Select-Object -ExpandProperty Version -Unique | Sort-Object) -join "<br>"
        $projects = ($group.Group | Sort-Object Project | ForEach-Object { "$($_.Project) ($($_.Version))" }) -join "<br>"
        $lines += "| $($group.Name) | $(Escape-Cell $versions) | $(Escape-Cell $projects) |"
    }
    $lines += @("", "## Direct Package References", "", "| Project | Kind | Package | Version |", "|---|---|---|---|")
    foreach ($row in $rows) {
        $lines += "| [$($row.Project)](../../$($row.Path)) | $($row.Kind) | $($row.Id) | $($row.Version) |"
    }
    Write-Lines -Path (Join-Path $docsMaps "package-map.md") -Lines $lines
}

function Write-FeatureMap {
    param([object[]]$Features)
    $lines = @("# Feature Map", "", 'Generated by `tools/maps/generate-maps`.', "", "Discovers public CShells feature classes and feature-base-derived classes by scanning source text.", "", "## Summary", "", "- Discovered feature classes: $($Features.Count)", "", "## Features", "", "| Feature class | Kind | Project | Base/interface | File |", "|---|---|---|---|---|")
    foreach ($feature in $Features) {
        $lines += "| $($feature.Class) | $($feature.Kind) | $($feature.Project) | $(Escape-Cell $feature.Base) | [$([System.IO.Path]::GetFileName($feature.Path))](../../$($feature.Path)) |"
    }
    Write-Lines -Path (Join-Path $docsMaps "feature-map.md") -Lines $lines
}

function Write-TestMap {
    param([object[]]$Projects)
    $sourceProjects = @($Projects | Where-Object Kind -eq "source" | Sort-Object Name)
    $testProjects = @($Projects | Where-Object Kind -eq "test" | Sort-Object Name)
    $referencedByTests = @{}
    foreach ($test in $testProjects) {
        foreach ($reference in $test.ProjectReferences) { $referencedByTests[$reference.ProjectName] = $true }
    }
    $sourceWithoutDirectTestCount = @($sourceProjects | Where-Object { -not $referencedByTests.ContainsKey($_.Name) }).Count
    $lines = @("# Test Map", "", 'Generated by `tools/maps/generate-maps`.', "", "Records direct test-project references only; it does not claim behavioral coverage.", "", "## Summary", "", "- Test projects: $($testProjects.Count)", "- Source projects directly referenced by at least one test project: $($referencedByTests.Keys.Count)", "- Source projects not directly referenced by test projects: $sourceWithoutDirectTestCount", "", "## Test Projects", "", "| Test project | Direct production references |", "|---|---|")
    foreach ($test in $testProjects) {
        $refs = if ($test.ProjectReferences.Count -eq 0) { "-" } else { ($test.ProjectReferences.ProjectName -join "<br>") }
        $lines += "| [$($test.Name)](../../$($test.Path)) | $(Escape-Cell $refs) |"
    }
    $lines += @("", "## Source Projects Without Direct Test Reference", "", "| Project | Domain |", "|---|---|")
    foreach ($project in $sourceProjects | Where-Object { -not $referencedByTests.ContainsKey($_.Name) }) {
        $lines += "| [$($project.Name)](../../$($project.Path)) | $($project.Domain) |"
    }
    Write-Lines -Path (Join-Path $docsMaps "test-map.md") -Lines $lines
}

function Write-SpecStatusMap {
    param([object[]]$Specs)
    $lines = @("# Spec Status Map", "", 'Generated by `tools/maps/generate-maps`.', "", 'Reads current `specs/*` folders and task checkboxes. Status clues are textual, not authoritative workflow state.', "", "## Specs", "", "| Spec | Title | Status | Plan verdict | Tasks done | Tasks open | Notes |", "|---|---|---|---|---:|---:|---|")
    foreach ($spec in $Specs) {
        $lines += "| [$($spec.Id)](../../specs/$($spec.Id)/spec.md) | $(Escape-Cell $spec.Title) | $(Escape-Cell $spec.Status) | $(Escape-Cell $spec.PlanVerdict) | $($spec.TasksDone) | $($spec.TasksOpen) | $(Escape-Cell $spec.Notes) |"
    }
    Write-Lines -Path (Join-Path $docsMaps "spec-status-map.md") -Lines $lines
}

function Write-FindingsReport {
    param([object[]]$Projects, [object[]]$Features, [object[]]$Specs)
    $rows = Get-PackageRows $Projects
    $multiVersion = @($rows | Group-Object Id | Where-Object { @($_.Group | Select-Object -ExpandProperty Version -Unique).Count -gt 1 } | Sort-Object Name)
    $sourceProjects = @($Projects | Where-Object Kind -eq "source")
    $testProjects = @($Projects | Where-Object Kind -eq "test")
    $referencedByTests = @{}
    foreach ($test in $testProjects) {
        foreach ($reference in $test.ProjectReferences) { $referencedByTests[$reference.ProjectName] = $true }
    }
    $spec006 = $Specs | Where-Object Id -eq "006-activity-construction-seam" | Select-Object -First 1
    $runtimeDesignRefs = @()
    foreach ($project in $sourceProjects | Where-Object { $_.Name -like "*Runtime*" -or $_.Name -eq "Elsa.Activities.Primitives" -or $_.Name -eq "Elsa.Activities.Composition.Runtime" }) {
        foreach ($reference in $project.ProjectReferences) {
            if ($reference.ProjectName -like "*Design*") { $runtimeDesignRefs += "$($project.Name) -> $($reference.ProjectName)" }
        }
    }
    $lines = @("# Maps v1 Findings", "", 'Generated by `tools/maps/generate-maps`.', "", "This is a point-in-time report from direct repo facts. It is not a constitution compliance verdict.", "", "## Summary", "", "- Source projects: $($sourceProjects.Count)", "- Test projects: $($testProjects.Count)", "- Discovered CShells feature classes: $($Features.Count)", "- Specs: $($Specs.Count)", "- Direct package IDs with multiple versions: $($multiVersion.Count)", "", "## Package Version Clusters", "")
    if ($multiVersion.Count -eq 0) {
        $lines += "No direct package ID has multiple direct versions."
    } else {
        $lines += @("| Package | Versions | Projects |", "|---|---|---|")
        foreach ($group in $multiVersion) {
            $versions = ($group.Group | Select-Object -ExpandProperty Version -Unique | Sort-Object) -join "<br>"
            $projects = ($group.Group | Sort-Object Project | ForEach-Object { "$($_.Project) ($($_.Version))" }) -join "<br>"
            $lines += "| $($group.Name) | $(Escape-Cell $versions) | $(Escape-Cell $projects) |"
        }
    }
    $sourceWithoutDirectTestCount = @($sourceProjects | Where-Object { -not $referencedByTests.ContainsKey($_.Name) }).Count
    $lines += @("", "## Test Visibility", "", "- Source projects without a direct test-project reference: $sourceWithoutDirectTestCount", "- This is only a visibility signal; tests may cover behavior indirectly through higher-level projects.", "", "## Spec 006 State", "")
    if ($null -ne $spec006) {
        $lines += '- `006-activity-construction-seam`: ' + $spec006.TasksDone + ' tasks done, ' + $spec006.TasksOpen + ' tasks open; notes: ' + $spec006.Notes + '.'
    } else {
        $lines += "- Spec 006 was not found."
    }
    $lines += @("", "## Runtime/Design Reference Signal", "")
    if ($runtimeDesignRefs.Count -eq 0) {
        $lines += "No direct runtime-path project reference to an `Elsa.*Design*` project was found by the Maps v1 heuristic."
    } else {
        foreach ($item in $runtimeDesignRefs | Sort-Object) { $lines += "- $item" }
    }
    $lines += @("", "## Next Map Work", "", "- Add feature dependency/activation IDs from CShells configuration.", "- Add constitution-specific verification reports that consume these maps.", "- Add richer test maturity classification beyond direct project references.")
    Write-Lines -Path (Join-Path $docsReports "maps-v1-findings.md") -Lines $lines
}

New-Item -ItemType Directory -Force -Path $docsMaps | Out-Null
New-Item -ItemType Directory -Force -Path $docsReports | Out-Null

$projects = Read-Projects
$features = Read-Features
$specs = Read-Specs

Write-ProjectReferenceMap $projects
Write-PackageMap $projects
Write-FeatureMap $features
Write-TestMap $projects
Write-SpecStatusMap $specs
Write-FindingsReport $projects $features $specs
Write-Manifest $projects $features $specs

Write-Host "Generated maps:"
Write-Host " - docs/maps/project-reference-map.md"
Write-Host " - docs/maps/package-map.md"
Write-Host " - docs/maps/feature-map.md"
Write-Host " - docs/maps/test-map.md"
Write-Host " - docs/maps/spec-status-map.md"
Write-Host " - docs/reports/maps-v1-findings.md"
Write-Host " - docs/maps/manifest.json"
