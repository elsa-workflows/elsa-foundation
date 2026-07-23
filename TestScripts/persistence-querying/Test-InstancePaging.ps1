<#
.SYNOPSIS
    Cursor paging over the instances/page endpoint: pages cover all matching instances with no duplicates.
.DESCRIPTION
    Exercises `GET runtime/workflows/instances/page` (`{ Items, NextCursor, HasNext, Count, TotalCount }`).
    Runs N instances of one definition, then walks the pages with a small `Take`, following `NextCursor` until
    `HasNext` is false. Asserts: every page has <= Take items; the union covers exactly the N instances with no
    duplicate execution ids; `TotalCount` reports N (scoped to the artifact filter).
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [int]    $Count    = 5,
    [int]    $Take     = 2
)
. "$PSScriptRoot/_PersistenceCommon.ps1"

Write-Host "== Instance cursor paging (instances/page) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

# Fixture: N instances of one definition (one artifact).
$def = Invoke-Step "publish" { Submit-Workflow -Ctx $ctx -Name "Page-$tag" -RootActivity (New-ActivityNode -NodeId "w" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "page $tag") )) }
$pub = Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id
$ids = @()
for ($i = 0; $i -lt $Count; $i++) {
    $r = Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId
    $ids += $r.workflowExecutionId
    Wait-WorkflowInstance -Ctx $ctx -ExecutionId $r.workflowExecutionId | Out-Null
}
Write-Host ("[fixture] ran {0} instances of artifact {1}" -f $Count, $pub.artifactId)

# Walk pages.
$seen = New-Object System.Collections.Generic.List[string]
$pageSizesOk = $true
$pages = 0
$cursor = $null
$totalCount = $null
do {
    $q = @{ artifactId = $pub.artifactId; take = $Take }
    if ($cursor) { $q.cursor = $cursor }
    $page = Get-InstancesPage -Ctx $ctx -Query $q
    $pages++
    $items = @($page.items)
    if ($items.Count -gt $Take) { $pageSizesOk = $false }
    $items | ForEach-Object { $seen.Add($_.workflowExecutionId) }
    $cursor = $page.nextCursor
    $totalCount = $page.totalCount
    $hasNext = [bool]$page.hasNext
    Write-Host ("  page {0}: {1} items, hasNext={2}" -f $pages, $items.Count, $hasNext)
} while ($hasNext -and $cursor -and $pages -lt ($Count + 5))

$unique = @($seen | Select-Object -Unique)
$noDupes = $unique.Count -eq $seen.Count
$coversAll = (@($ids | Where-Object { $unique -notcontains $_ }).Count -eq 0) -and ($unique.Count -eq $Count)

Write-Host ""
$fail = @()
if (-not $pageSizesOk) { $fail += "a page exceeded take=$Take" }
if (-not $noDupes) { $fail += "duplicate execution ids across pages ($($seen.Count) seen, $($unique.Count) unique)" }
if (-not $coversAll) { $fail += "pages did not cover exactly the $Count fixture instances (covered $($unique.Count))" }
if ($totalCount -ne $Count) { $fail += "TotalCount=$totalCount (expected $Count)" }

if ($fail.Count -eq 0) {
    Write-Host ("SUCCESS - {0} instances paged in {1} pages of <= {2}, no duplicates, TotalCount={3}." -f $Count, $pages, $Take, $totalCount) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
