Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$catalogPath = Join-Path $repoRoot "docs\skills\catalog.md"
$manifestPath = Join-Path $repoRoot ".specify\integrations\claude.manifest.json"
$skillsRoot = Join-Path $repoRoot ".claude\skills"

function To-RepoPath {
    param([string]$Path)
    $full = (Resolve-Path -LiteralPath $Path).Path
    return ($full.Substring($repoRoot.Length + 1) -replace '\\', '/')
}

function Add-Failure {
    param([string]$Message)
    $script:failures += $Message
}

function Get-GitHubHeadingAnchor {
    param([string]$Heading)
    $anchor = $Heading.Trim().ToLowerInvariant()
    $anchor = $anchor -replace '[^\p{L}\p{Nd}\s-]', ''
    $anchor = $anchor -replace '\s', '-'
    return $anchor
}

function Read-FrontMatter {
    param([string]$Path)
    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -notmatch '(?s)^---\r?\n(?<frontMatter>.*?)\r?\n---\r?\n(?<body>.*)$') {
        Add-Failure "$(To-RepoPath $Path): missing YAML front matter"
        return [pscustomobject]@{ Values = @{}; Body = $content; Content = $content }
    }

    $frontMatter = $Matches.frontMatter
    $body = $Matches.body
    $values = @{}
    foreach ($line in ($frontMatter -split "\r?\n")) {
        if ($line -match '^\s+(?<key>[A-Za-z0-9_-]+):\s*"?(?<value>.*?)"?\s*$') {
            $values["metadata.$($Matches.key)"] = $Matches.value
        }
        elseif ($line -match '^(?<key>[A-Za-z0-9_-]+):\s*"?(?<value>.*?)"?\s*$') {
            $values[$Matches.key] = $Matches.value
        }
    }

    return [pscustomobject]@{ Values = $values; Body = $body; Content = $content }
}

$failures = @()

if (-not (Test-Path -LiteralPath $catalogPath)) {
    throw "Missing catalog: docs/skills/catalog.md"
}
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Missing Claude manifest: .specify/integrations/claude.manifest.json"
}
if (-not (Test-Path -LiteralPath $skillsRoot)) {
    throw "Missing Claude skills root: .claude/skills"
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw
$agentsPath = Join-Path $repoRoot "AGENTS.md"
$frameworkConstitutionPath = Join-Path $repoRoot ".specify\memory\constitution-framework.md"
$elsaConstitutionPath = Join-Path $repoRoot ".specify\memory\constitution.md"

if (-not (Test-Path -LiteralPath $agentsPath)) {
    throw "Missing AGENTS.md"
}
if (-not (Test-Path -LiteralPath $frameworkConstitutionPath)) {
    throw "Missing framework constitution"
}
if (-not (Test-Path -LiteralPath $elsaConstitutionPath)) {
    throw "Missing Elsa constitution"
}

$agents = Get-Content -LiteralPath $agentsPath -Raw
$frameworkConstitution = Get-Content -LiteralPath $frameworkConstitutionPath -Raw
$elsaConstitution = Get-Content -LiteralPath $elsaConstitutionPath -Raw

if ($agents -notmatch [regex]::Escape("docs/program-goals/")) {
    Add-Failure "AGENTS.md: missing docs/program-goals/ routing"
}
if ($agents -notmatch [regex]::Escape("none/free-flow")) {
    Add-Failure "AGENTS.md: missing none/free-flow program-goal state"
}

foreach ($obsoletePhrase in @(
    "unfinished-work queue",
    "current repo-local tracking lives in",
    "tracked in [unfinished work]",
    "items are tracked in [unfinished work]"
)) {
    if ($frameworkConstitution.Contains($obsoletePhrase) -or $elsaConstitution.Contains($obsoletePhrase)) {
        Add-Failure "Constitutions: obsolete tracking phrase remains: '$obsoletePhrase'"
    }
}

foreach ($catalogExpectation in @(
    @{ Heading = "What's Next / Unfinished Work"; Pattern = "../program-goals/" },
    @{ Heading = "Program Goal Drift Review"; Pattern = "../program-goals/" },
    @{ Heading = "Work Unit Planner"; Pattern = "../program-goals/" }
)) {
    $heading = [regex]::Escape($catalogExpectation.Heading)
    $pattern = [regex]::Escape($catalogExpectation.Pattern)
    if ($catalog -notmatch "(?s)###\s+$heading.*?$pattern") {
        Add-Failure "docs/skills/catalog.md: '$($catalogExpectation.Heading)' must mention $($catalogExpectation.Pattern)"
    }
}

$catalogAnchors = [System.Collections.Generic.HashSet[string]]::new()
foreach ($match in [regex]::Matches($catalog, '(?m)^#{2,3}\s+(?<heading>.+?)\s*$')) {
    [void]$catalogAnchors.Add((Get-GitHubHeadingAnchor $match.Groups["heading"].Value))
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifestFiles = @{}
foreach ($property in $manifest.files.PSObject.Properties) {
    if ($property.Name -like ".claude/skills/elsa-*/SKILL.md") {
        $manifestFiles[$property.Name] = $property.Value
    }
}

$wrapperFiles = Get-ChildItem -LiteralPath $skillsRoot -Directory -Filter "elsa-*" |
    ForEach-Object { Join-Path $_.FullName "SKILL.md" } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object

foreach ($wrapperPath in $wrapperFiles) {
    $repoPath = To-RepoPath $wrapperPath
    if (-not $manifestFiles.ContainsKey($repoPath)) {
        Add-Failure "${repoPath}: missing from .specify/integrations/claude.manifest.json"
        continue
    }

    $actualHash = (Get-FileHash -LiteralPath $wrapperPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = [string]$manifestFiles[$repoPath]
    if ($actualHash -ne $expectedHash.ToLowerInvariant()) {
        Add-Failure "${repoPath}: manifest hash mismatch. expected $expectedHash, actual $actualHash"
    }

    $parsed = Read-FrontMatter $wrapperPath
    $values = $parsed.Values
    $expectedName = Split-Path (Split-Path $wrapperPath -Parent) -Leaf

    if (-not $values.ContainsKey("name") -or $values["name"] -ne $expectedName) {
        Add-Failure "${repoPath}: front matter name must match wrapper folder '$expectedName'"
    }
    if (-not $values.ContainsKey("metadata.author") -or $values["metadata.author"] -ne "elsa-foundation") {
        Add-Failure "${repoPath}: metadata.author must be 'elsa-foundation'"
    }
    if (-not $values.ContainsKey("metadata.source")) {
        Add-Failure "${repoPath}: missing metadata.source"
    }
    else {
        $source = $values["metadata.source"]
        if ($source -notmatch '^docs/skills/catalog\.md#(?<anchor>[A-Za-z0-9-]+)$') {
            Add-Failure "${repoPath}: metadata.source must point to docs/skills/catalog.md#..."
        }
        elseif (-not $catalogAnchors.Contains($Matches.anchor)) {
            Add-Failure "${repoPath}: metadata.source points to missing catalog anchor '$($Matches.anchor)'"
        }
    }

    $sectionHeadings = @([regex]::Matches($parsed.Body, '(?m)^##\s+') | ForEach-Object { $_.Value })
    if ($sectionHeadings.Count -gt 2 -or $parsed.Body -notmatch '(?m)^## User Input\s*$' -or $parsed.Body -notmatch '(?m)^## Outline\s*$') {
        Add-Failure "${repoPath}: wrapper should stay thin with only User Input and Outline sections"
    }

    if ($expectedName -in @("elsa-whats-next", "elsa-work-unit-planner", "elsa-program-goal-drift-review") -and $parsed.Content -notmatch [regex]::Escape("docs/program-goals/")) {
        Add-Failure "${repoPath}: must mention docs/program-goals/"
    }
}

$wrapperRepoPaths = @($wrapperFiles | ForEach-Object { To-RepoPath $_ })
foreach ($manifestPathEntry in ($manifestFiles.Keys | Sort-Object)) {
    if ($wrapperRepoPaths -notcontains $manifestPathEntry) {
        Add-Failure "${manifestPathEntry}: manifest lists an Elsa wrapper file that does not exist"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Claude Elsa wrapper drift validation failed:"
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host "Claude Elsa wrapper drift validation passed."
Write-Host " - Catalog anchors checked: $($catalogAnchors.Count)"
Write-Host " - Elsa wrappers checked: $($wrapperFiles.Count)"
Write-Host " - Manifest Elsa entries checked: $($manifestFiles.Count)"
