<#
.SYNOPSIS
    Documents the reusable-activity boundary outcome limitation: a graph activity may emit only the single
    `done` outcome; custom/named boundary outcomes are rejected.
.DESCRIPTION
    A parent cannot branch on a CUSTOM outcome coming out of a "workflow used as an activity": the
    `elsa.activity-graph` boundary is validation-restricted to exactly one emitted outcome, `done`
    (diagnostic `activity.graph.outcome-done-required`). Custom-outcome branching works only INSIDE a workflow
    (see Test-SetOutcome.ps1 — the Control intrinsic + Flowchart), not across a reusable-activity boundary.

    This test authors a reusable activity whose contract declares a custom emitted outcome (`approved`) instead
    of `done`, and asserts publication is refused with the `outcome-done-required` diagnostic. It is a living
    limitation tracker: green while the single-`done` rule holds; reports RELAXED if a custom boundary outcome
    ever publishes. Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Reusable-activity boundary outcome limitation (single 'done') ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$t = Get-Random -Max 999999

# Contract declares a CUSTOM emitted outcome ('approved') instead of the required 'done'.
$customContract = '{"contractSchemaVersion":"1","inputs":[],"outputs":[],"outcomes":[{"referenceKey":"approved","name":"Approved","isEmitted":true}]}'
$manifest = New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson ("[" + (New-WriteLineNodeJson -NodeId "w" -WriteLineVersionId $wl -Text "custom outcome t=$t") + "]")

$rejectedAtCreate = $false
$createErr = ""
try {
    $d = New-ReusableActivityDefinition -Ctx $ctx -DisplayName "CustomOutcome-$t" -PayloadJson $manifest -ContractJson $customContract
} catch {
    $rejectedAtCreate = $true
    if ($_.Exception.Response) { $sr = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $createErr = $sr.ReadToEnd() }
}

$publishable = $null
$hasDoneRequired = $false
if (-not $rejectedAtCreate) {
    $pfBody = @{ expectedDraftRevision = $d.Revision; expectedDefinitionHeadVersionId = $null } | ConvertTo-Json
    $pf = Invoke-Step "publication-preflight" { Invoke-RestMethod "$BaseUrl/design/activities/drafts/$($d.DraftId)/publication-preflight" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $pfBody }
    $publishable = $pf.isPublishable
    $hasDoneRequired = [bool]($pf.diagnostics | Where-Object { $_.code -match 'outcome-done-required' })
    $pf.diagnostics | ForEach-Object { Write-Host ("  diag: {0} {1}" -f $_.severity, $_.code) -ForegroundColor Yellow }
}

Write-Host ""
if ($rejectedAtCreate -and ($createErr -match 'outcome-done-required')) {
    Write-Host "LIMITATION CONFIRMED - custom boundary outcome rejected at create (outcome-done-required)." -ForegroundColor Green
} elseif ($hasDoneRequired -and -not $publishable) {
    Write-Host "LIMITATION CONFIRMED - reusable-activity boundary requires the single 'done' outcome; custom outcome is not publishable (activity.graph.outcome-done-required)." -ForegroundColor Green
} elseif ($publishable -eq $true) {
    Write-Host "RELAXED - a custom boundary outcome is now publishable. Reusable activities may support multi/named outcomes; revisit Test-SetOutcome coverage across boundaries." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - unexpected result (rejectedAtCreate={0}, publishable={1}, doneRequired={2})." -f $rejectedAtCreate, $publishable, $hasDoneRequired) -ForegroundColor Red
    exit 1
}
