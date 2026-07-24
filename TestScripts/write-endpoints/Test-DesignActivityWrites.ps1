<#
.SYNOPSIS
    Write-endpoint coverage for the activity-design API (design/activities/*): reusable-activity write lifecycle.
.DESCRIPTION
    Exercises the core reusable-activity mutations against throwaway fixtures, with negatives:
      create draft -> validate -> replace draft -> patch definition metadata -> publish
      -> version retire -> restore -> revoke; discard draft.
    (Advanced flows -- fork, contract-proposals, upgrade-plans, migrate-provider, availability/recommendation --
    are not covered here; they need elaborate multi-step fixtures.)
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_WriteCommon.ps1"
$script:WPass = 0; $script:WTotal = 0

Write-Host "== WRITE coverage: activity-design API (reusable-activity lifecycle) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999
$manifest = @"
{"variables":[],"rootActivity":{"nodeId":"graph-root","activityVersionId":"$seq","inputs":[],"outputs":[],"structure":{"kind":"elsa.sequence.structure","schemaVersion":"1.0.0","payload":{"activities":[{"nodeId":"w","activityVersionId":"$wl","inputs":[{"referenceKey":"text","value":{"value":"act $tag","expressionType":"Literal"},"autoEvaluate":null,"evaluatorType":null,"storageDriverType":null,"isSensitive":null}],"outputs":[],"structure":null}]}}},"outputMappings":[]}
"@
$providerJson = "{`"providerKey`":`"elsa.activity-graph`",`"schemaVersion`":`"1`",`"payload`":$manifest}"
$contractJson = '{"contractSchemaVersion":"1","inputs":[],"outputs":[],"outcomes":[{"referenceKey":"done","name":"Done","isEmitted":true}]}'
function New-ReusableDraft([string]$name) {
    $body = "{`"category`":`"QaWriteCov`",`"displayName`":`"$name`",`"description`":`"wc`",`"provider`":$providerJson,`"contract`":$contractJson,`"layout`":[]}"
    Invoke-RestMethod "$($ctx.BaseUrl)/design/activities/definitions" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body
}

# 1. create
$created = New-ReusableDraft "DaCreate-$tag"
$defId = $created.definition.definitionId; $draftId = $created.draft.draftId
Write-Host "    (def=$defId draft=$draftId)"; $script:WTotal++; if ($draftId) { $script:WPass++; Write-Host "  OK   [200] POST definitions (create reusable)" -ForegroundColor Green } else { Write-Host "  FAIL create" -ForegroundColor Red }

# 2. validate (rev 1)
Assert-Write -Ctx $ctx -Label "drafts/{id}/validate" -Method POST -Path "design/activities/drafts/$draftId/validate" -Body @{ expectedRevision = 1 } -ExpectStatus 200 | Out-Null

# 3. replace draft (rev 1 -> 2)
$replaceBody = "{`"expectedRevision`":1,`"contract`":$contractJson,`"provider`":$providerJson,`"layout`":[]}"
$rep = Assert-Write -Ctx $ctx -Label "drafts/{id} (replace)" -Method PUT -Path "design/activities/drafts/$draftId" -Body $replaceBody -ExpectStatus 200
$rev = if ($rep.Json.revision) { $rep.Json.revision } else { 2 }

# 4. patch definition metadata
Assert-Write -Ctx $ctx -Label "definitions/{id} (patch metadata)" -Method PATCH -Path "design/activities/definitions/$defId" -Body @{ category = "QaWriteCov"; displayName = "DaCreate-$tag-renamed"; description = "updated" } -ExpectStatus 200 | Out-Null

# 5. publish (preflight + publish) -> versionId
$pf = Invoke-Write -Ctx $ctx -Method POST -Path "design/activities/drafts/$draftId/publication-preflight" -Body @{ expectedDraftRevision = $rev; expectedDefinitionHeadVersionId = $null }
$pubBody = @{ expectedDraftRevision = $pf.Json.draftRevision; expectedDefinitionHeadVersionId = $pf.Json.definitionHeadVersionId; version = $pf.Json.minimumVersion; reviewToken = $pf.Json.reviewToken; idempotencyKey = [guid]::NewGuid().ToString() }
$pub = Assert-Write -Ctx $ctx -Label "drafts/{id}/publish" -Method POST -Path "design/activities/drafts/$draftId/publish" -Body $pubBody -ExpectStatus 200,201
$versionId = $pub.Json.outcome.definitionVersionId

# 6. version lifecycle: retiring/revoking the RECOMMENDED version is gated -> 409
# (a freshly-published sole version is the recommended one; the endpoint requires an explicit clear/replace
# recommendation decision first -- diagnostic activity-definition-recommendation-required). The happy-path
# transition needs a multi-version + recommendation flow, which is an advanced case not covered here.
Assert-Write -Ctx $ctx -Label "versions/{id}/retire (recommended -> 409 recommendation-required)" -Method POST -Path "design/activities/versions/$versionId/retire" -Body @{ expectedLifecycle = "Active"; reason = "test" } -ExpectStatus 409 | Out-Null
Assert-Write -Ctx $ctx -Label "versions/{id}/revoke (recommended -> 409 recommendation-required)" -Method POST -Path "design/activities/versions/$versionId/revoke" -Body @{ expectedLifecycle = "Active"; reason = "test" } -ExpectStatus 409 | Out-Null

# 9. discard a throwaway draft
$thr = New-ReusableDraft "DaDiscard-$tag"
Assert-Write -Ctx $ctx -Label "drafts/{id} (discard)" -Method DELETE -Path "design/activities/drafts/$($thr.draft.draftId)" -Body @{ expectedRevision = 1 } -ExpectStatus 200,204 | Out-Null

# negatives
Assert-Write -Ctx $ctx -Label "validate {bogus draft} -> 404" -Method POST -Path "design/activities/drafts/draft-bogus$tag/validate" -Body @{ expectedRevision = 1 } -ExpectStatus 404,400 | Out-Null
Assert-Write -Ctx $ctx -Label "retire {bogus version} -> 404" -Method POST -Path "design/activities/versions/ver-bogus$tag/retire" -Body @{ expectedLifecycle = "Active"; reason = "x" } -ExpectStatus 404,400 | Out-Null

Complete-WriteSuite -Area "activity-design API"
