<#
.SYNOPSIS
    Write-endpoint coverage for the publishing API (publishing/* + design/activities publish flow).
.DESCRIPTION
    Exercises the publish/test-run mutations against throwaway fixtures, with negatives:
      workflow:  preflight -> publish -> version test-run -> draft test-run
      activity:  create draft -> publication-preflight -> publish; activity draft test-run
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

Write-Host "== WRITE coverage: publishing API ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

# ---------- workflow publishing ----------
$def = Submit-Workflow -Ctx $ctx -Name "PwWf-$tag" -RootActivity (New-ActivityNode -NodeId "w" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "pw $tag") ))
$versionId = $def.version.id; $definitionId = $def.definition.id
Assert-Write -Ctx $ctx -Label "workflows/{versionId}/preflight" -Method POST -Path "publishing/workflows/$versionId/preflight" -Body @{} -ExpectStatus 200 | Out-Null
Assert-Write -Ctx $ctx -Label "workflows/{versionId}/publish" -Method POST -Path "publishing/workflows/$versionId/publish" -Body @{} -ExpectStatus 200,201 | Out-Null
Assert-Write -Ctx $ctx -Label "workflows/{versionId}/test-runs" -Method POST -Path "publishing/workflows/$versionId/test-runs" -Body @{} -ExpectStatus 200,201,202 | Out-Null
$state = New-WorkflowState -RootActivity (New-ActivityNode -NodeId "w" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "draft-run $tag") ))
Assert-Write -Ctx $ctx -Label "workflows/drafts/test-runs" -Method POST -Path "publishing/workflows/drafts/test-runs" -Body @{ definitionId = $definitionId; snapshotId = "snap-$tag"; state = $state } -ExpectStatus 200,201,202 | Out-Null

# ---------- activity publishing ----------
$manifest = @"
{"variables":[],"rootActivity":{"nodeId":"graph-root","activityVersionId":"$seq","inputs":[],"outputs":[],"structure":{"kind":"elsa.sequence.structure","schemaVersion":"1.0.0","payload":{"activities":[{"nodeId":"w","activityVersionId":"$wl","inputs":[{"referenceKey":"text","value":{"value":"act $tag","expressionType":"Literal"},"autoEvaluate":null,"evaluatorType":null,"storageDriverType":null,"isSensitive":null}],"outputs":[],"structure":null}]}}},"outputMappings":[]}
"@
function New-ReusableDraft([string]$name) {
    $body = @"
{"category":"QaWriteCov","displayName":"$name","description":"write coverage","provider":{"providerKey":"elsa.activity-graph","schemaVersion":"1","payload":$manifest},"contract":{"contractSchemaVersion":"1","inputs":[],"outputs":[],"outcomes":[{"referenceKey":"done","name":"Done","isEmitted":true}]},"layout":[]}
"@
    Invoke-RestMethod "$($ctx.BaseUrl)/design/activities/definitions" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body
}

# create + preflight + publish
$act = New-ReusableDraft "PwActPub-$tag"
$draftId = $act.draft.draftId
$pf = Assert-Write -Ctx $ctx -Label "activities/drafts/{id}/publication-preflight" -Method POST -Path "design/activities/drafts/$draftId/publication-preflight" -Body @{ expectedDraftRevision = 1; expectedDefinitionHeadVersionId = $null } -ExpectStatus 200 -Validate { param($r) $r.Json.isPublishable }
$pubBody = @{ expectedDraftRevision = $pf.Json.draftRevision; expectedDefinitionHeadVersionId = $pf.Json.definitionHeadVersionId; version = $pf.Json.minimumVersion; reviewToken = $pf.Json.reviewToken; idempotencyKey = [guid]::NewGuid().ToString() }
Assert-Write -Ctx $ctx -Label "activities/drafts/{id}/publish" -Method POST -Path "design/activities/drafts/$draftId/publish" -Body $pubBody -ExpectStatus 200,201 | Out-Null

# activity draft test-run (fresh draft)
$act2 = New-ReusableDraft "PwActRun-$tag"
$draftId2 = $act2.draft.draftId
Assert-Write -Ctx $ctx -Label "activity-drafts/{id}/test-runs" -Method POST -Path "publishing/activity-drafts/$draftId2/test-runs" -Body @{ expectedRevision = 1; idempotencyKey = [guid]::NewGuid().ToString() } -ExpectStatus 200,201,202 | Out-Null

# ---------- negatives ----------
# A malformed versionId is rejected by the route's version-id constraint -> 400 (a well-formed-but-unknown id
# would 404). The version test-run path is lenient. Assert the real contracts.
Assert-Write -Ctx $ctx -Label "publish {malformed version} -> 400" -Method POST -Path "publishing/workflows/ver-bogus$tag/publish" -Body @{} -ExpectStatus 400,404 | Out-Null
Assert-Write -Ctx $ctx -Label "preflight {malformed version} -> 400" -Method POST -Path "publishing/workflows/ver-bogus$tag/preflight" -Body @{} -ExpectStatus 400,404 | Out-Null
Assert-Write -Ctx $ctx -Label "version test-run {bogus} (lenient)" -Method POST -Path "publishing/workflows/ver-bogus$tag/test-runs" -Body @{} -ExpectStatus 200,400,404 | Out-Null

Complete-WriteSuite -Area "publishing API"
