<#
.SYNOPSIS
    GET-endpoint coverage for the activity-design API (design/activities/*).
.DESCRIPTION
    Exercises the reachable GET endpoints under the activity-design area. Uses existing catalog entries for the
    definition/version reads, and creates one reusable-activity draft (create-only, no publish) for the draft
    read. Plus negative (404) cases.
    Covered: authoring-capabilities; catalog; definitions (list/get/picker/drafts/versions); drafts/{id};
    versions/{id}; versions/{id}/dependencies.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_GetCommon.ps1"
$script:GetPass = 0; $script:GetTotal = 0

Write-Host "== GET coverage: activity-design API ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$tag = Get-Random -Max 999999

# --- pick an existing catalog definition + its head version ---
$catalog = (Invoke-Get -Ctx $ctx -Path "design/activities/definitions?searchTerm=").Json
$items = if ($catalog.items) { $catalog.items } else { $catalog }
$sample = $items | Where-Object { $_.definition.headVersionId } | Select-Object -First 1
$existingDef = $sample.definition.definitionId
$existingVer = $sample.definition.headVersionId
Write-Host ("[fixture] existing definition={0} version={1}" -f $existingDef, $existingVer)

# --- create a reusable-activity draft (create-only) for the draft read ---
$seq = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
$wl  = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
$manifest = @"
{"variables":[],"rootActivity":{"nodeId":"graph-root","activityVersionId":"$seq","inputs":[],"outputs":[],"structure":{"kind":"Sequence","schemaVersion":"1","payload":{"activities":[{"nodeId":"w","activityVersionId":"$wl","inputs":[{"referenceKey":"text","value":{"value":"draft $tag","expressionType":"Literal"},"autoEvaluate":null,"evaluatorType":null,"storageDriverType":null,"isSensitive":null}],"outputs":[],"structure":null}]}}},"outputMappings":[]}
"@
$body = @"
{"category":"QaGetCoverage","displayName":"GetCov-$tag","description":"draft read fixture","provider":{"providerKey":"elsa.activity-graph","schemaVersion":"1","payload":$manifest},"contract":{"contractSchemaVersion":"1","inputs":[],"outputs":[],"outcomes":[{"referenceKey":"done","name":"Done","isEmitted":true}]},"layout":[]}
"@
$created = Invoke-RestMethod "$($ctx.BaseUrl)/design/activities/definitions" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body
$draftId = $created.draft.draftId
$newDef = $created.definition.definitionId
Write-Host ("[fixture] reusable draft={0} (def {1})" -f $draftId, $newDef)
Write-Host ""

Assert-Get -Ctx $ctx -Label "authoring-capabilities" -Path "design/activities/authoring-capabilities" | Out-Null
Assert-Get -Ctx $ctx -Label "catalog" -Path "design/activities/catalog" | Out-Null
Assert-Get -Ctx $ctx -Label "definitions (list)" -Path "design/activities/definitions?searchTerm=" | Out-Null
Assert-Get -Ctx $ctx -Label "definitions/picker" -Path "design/activities/definitions/picker" | Out-Null
Assert-Get -Ctx $ctx -Label "definitions/{id}" -Path "design/activities/definitions/$existingDef" | Out-Null
Assert-Get -Ctx $ctx -Label "definitions/{id}/versions" -Path "design/activities/definitions/$existingDef/versions" | Out-Null
Assert-Get -Ctx $ctx -Label "definitions/{id}/drafts" -Path "design/activities/definitions/$newDef/drafts" | Out-Null
Assert-Get -Ctx $ctx -Label "versions/{id}" -Path "design/activities/versions/$existingVer" | Out-Null
Assert-Get -Ctx $ctx -Label "versions/{id}/dependencies" -Path "design/activities/versions/$existingVer/dependencies" | Out-Null
Assert-Get -Ctx $ctx -Label "drafts/{draftId}" -Path "design/activities/drafts/$draftId" | Out-Null

Assert-Get -Ctx $ctx -Label "definitions/{bogus} -> 404" -Path "design/activities/definitions/actdef-bogus$tag" -ExpectStatus 404 | Out-Null
Assert-Get -Ctx $ctx -Label "versions/{bogus} -> 404" -Path "design/activities/versions/actver-bogus$tag" -ExpectStatus 404 | Out-Null
Assert-Get -Ctx $ctx -Label "drafts/{bogus} -> 404" -Path "design/activities/drafts/draft-bogus$tag" -ExpectStatus 404 | Out-Null

Complete-GetSuite -Area "activity-design API"
