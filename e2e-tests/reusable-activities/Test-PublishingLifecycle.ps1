<#
.SYNOPSIS
    End-to-end Publishing lifecycle across snapshot review, authority, reusable activity, and Test Run routes.
.DESCRIPTION
    Exercises the complete migrated Minimal API surface through a real Workbench/SQLite host: snapshot preflight
    and token-bound publish, route-over-body authority, Runtime Evidence preflight, policy CAS/stale rejection,
    slot read/unpublish/restore, reusable-activity receipt replay, and activity Test Run lookup/cancellation.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)

. "$PSScriptRoot/_ReusableCommon.ps1"
. "$PSScriptRoot/../write-endpoints/_WriteCommon.ps1"
$script:WPass = 0
$script:WTotal = 0

function Assert-LifecycleCondition {
    param([Parameter(Mandatory)][string] $Label, [Parameter(Mandatory)][bool] $Condition)
    $script:WTotal++
    if ($Condition) {
        $script:WPass++
        Write-Host "  OK         $Label" -ForegroundColor Green
    } else {
        Write-Host "  FAIL       $Label" -ForegroundColor Red
    }
}

Write-Host "== Publishing lifecycle (Minimal API) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

# Snapshot review -> route-authoritative publish.
$root = New-ActivityNode -NodeId "workflow-root" -VersionId $wl -Inputs @(
    (New-LiteralInput -ReferenceKey "text" -Value "publishing lifecycle $tag")
)
$state = New-WorkflowState -RootActivity $root
$definition = Invoke-Step "submit workflow snapshot" {
    Submit-Workflow -Ctx $ctx -Name "PublishingLifecycle-$tag" -RootActivity $root
}
$definitionId = $definition.definition.id
$versionId = $definition.version.id
$snapshot = Assert-Write -Ctx $ctx -Label "snapshot publication preflight" -Method POST `
    -Path "publishing/workflows/preflight" `
    -Body @{ definitionId = $definitionId; state = $state; layout = @() } `
    -ExpectStatus 200 `
    -Validate { param($response) $response.Json.preflightToken -and $response.Json.candidateHash }

$published = Assert-Write -Ctx $ctx -Label "token-bound publish uses route version over conflicting body" -Method POST `
    -Path "publishing/workflows/$versionId/publish" `
    -Body @{ versionId = "body-version-must-not-win-$tag"; preflightToken = $snapshot.Json.preflightToken } `
    -ExpectStatus 201 `
    -Validate { param($response) $response.Json.versionId -eq $versionId }
Assert-LifecycleCondition "route version remained authoritative" ($published.Json.versionId -eq $versionId)

$runtimePreflight = Assert-Write -Ctx $ctx -Label "Runtime Evidence preflight" -Method POST `
    -Path "publishing/preflight" `
    -Body @{ scope = "ActiveRetainedArtifacts"; artifactIds = @($published.Json.artifactId) } `
    -ExpectStatus 200 `
    -Validate { param($response) $response.Json.checkedArtifactCount -ge 1 }
Assert-LifecycleCondition "published artifact was evaluated" ($runtimePreflight.Json.checkedArtifactCount -ge 1)

# Policy CAS and stale-write rejection.
$policy = Assert-Write -Ctx $ctx -Label "effective workflow policy" -Method GET `
    -Path "publishing/workflows/$definitionId/policy" -ExpectStatus 200
$savedPolicy = Assert-Write -Ctx $ctx -Label "workflow policy CAS update" -Method PUT `
    -Path "publishing/workflows/$definitionId/policy" `
    -Body @{ definitionId = "body-definition-must-not-win-$tag"; defaultAction = "replace"; defaultSlotName = "default"; expectedRevision = $policy.Json.revision } `
    -ExpectStatus 200 `
    -Validate { param($response) $response.Json.definitionId -eq $definitionId -and $response.Json.revision -gt $policy.Json.revision }
Assert-Write -Ctx $ctx -Label "stale workflow policy CAS is rejected" -Method PUT `
    -Path "publishing/workflows/$definitionId/policy" `
    -Body @{ defaultAction = "replace"; defaultSlotName = "default"; expectedRevision = $policy.Json.revision } `
    -ExpectStatus 409 | Out-Null

# Slot lifecycle. Reads are runtime-owned; publishing keeps only lifecycle commands. Bodies deliberately
# carry conflicting identities; route values own the operation.
Assert-Write -Ctx $ctx -Label "retired publishing slot read" -Method GET `
    -Path "publishing/workflows/$definitionId/slots/default" -ExpectStatus 405 | Out-Null
$slot = Assert-Write -Ctx $ctx -Label "read active activation slot" -Method GET `
    -Path "runtime/workflows/activation-slots/$definitionId/default" -ExpectStatus 200 `
    -Validate { param($response) $null -ne $response.Json.activeActivationId }
$retired = Assert-Write -Ctx $ctx -Label "unpublish route identity wins over body" -Method DELETE `
    -Path "publishing/workflows/$definitionId/slots/default" `
    -Body @{ definitionId = "body-definition-must-not-win-$tag"; slotName = "body-slot-must-not-win" } `
    -ExpectStatus 200
$restored = Assert-Write -Ctx $ctx -Label "restore route identity wins over body" -Method POST `
    -Path "publishing/workflows/$definitionId/slots/default/restore" `
    -Body @{ definitionId = "body-definition-must-not-win-$tag"; slotName = "body-slot-must-not-win" } `
    -ExpectStatus 200 `
    -Validate { param($response) $response.Json.definitionId -eq $definitionId -and $response.Json.slotName -eq "default" }
Assert-LifecycleCondition "slot transitioned away from the original active publication" ($retired.Json.activePublicationId -ne $slot.Json.activePublicationId)
Assert-LifecycleCondition "slot restore completed for the route-owned slot" ($restored.Json.definitionId -eq $definitionId)

# Reusable-activity publication and durable receipt replay.
$manifest = New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson (
    "[" + (New-WriteLineNodeJson -NodeId "activity-write" -WriteLineVersionId $wl -Text "activity lifecycle $tag") + "]")
$activity = Invoke-Step "create reusable activity draft" {
    New-ReusableActivityDefinition -Ctx $ctx -DisplayName "PublishingLifecycleActivity-$tag" -PayloadJson $manifest
}
$activityPreflight = Assert-Write -Ctx $ctx -Label "activity publication preflight" -Method POST `
    -Path "design/activities/drafts/$($activity.DraftId)/publication-preflight" `
    -Body @{ expectedDraftRevision = $activity.Revision; expectedDefinitionHeadVersionId = $null } `
    -ExpectStatus 200 `
    -Validate { param($response) $response.Json.isPublishable -and $response.Json.reviewToken }
$activityKey = [guid]::NewGuid().ToString()
$activityPublishBody = @{
    expectedDraftRevision = $activityPreflight.Json.draftRevision
    expectedDefinitionHeadVersionId = $activityPreflight.Json.definitionHeadVersionId
    version = $activityPreflight.Json.minimumVersion
    reviewToken = $activityPreflight.Json.reviewToken
    idempotencyKey = $activityKey
}
$activityReceipt = Assert-Write -Ctx $ctx -Label "publish reusable activity" -Method POST `
    -Path "design/activities/drafts/$($activity.DraftId)/publish" -Body $activityPublishBody -ExpectStatus 201
$activityReplay = Assert-Write -Ctx $ctx -Label "replay reusable activity publication" -Method POST `
    -Path "design/activities/drafts/$($activity.DraftId)/publish" -Body $activityPublishBody -ExpectStatus 201
$activityLookup = Assert-Write -Ctx $ctx -Label "read durable activity publication receipt" -Method GET `
    -Path "design/activities/publications/$activityKey" -ExpectStatus 200
Assert-LifecycleCondition "publication replay returned the same durable receipt" (
    $activityReceipt.Json.idempotencyKey -eq $activityReplay.Json.idempotencyKey -and
    $activityReplay.Json.idempotencyKey -eq $activityLookup.Json.idempotencyKey)

# Activity Test Run creation, both lookup identities, and cancellation endpoint.
$testActivity = Invoke-Step "create activity Test Run draft" {
    New-ReusableActivityDefinition -Ctx $ctx -DisplayName "PublishingLifecycleTestRun-$tag" -PayloadJson $manifest
}
$testRunKey = [guid]::NewGuid().ToString()
$testRun = Assert-Write -Ctx $ctx -Label "start activity draft Test Run" -Method POST `
    -Path "publishing/activity-drafts/$($testActivity.DraftId)/test-runs" `
    -Body @{ expectedRevision = $testActivity.Revision; idempotencyKey = $testRunKey } `
    -ExpectStatus 202 `
    -Validate { param($response) $response.Json.testRunId }
$testRunById = Assert-Write -Ctx $ctx -Label "lookup activity Test Run by id" -Method GET `
    -Path "publishing/activity-test-runs/$($testRun.Json.testRunId)" -ExpectStatus 200
$testRunByKey = Assert-Write -Ctx $ctx -Label "lookup activity Test Run by idempotency key" -Method GET `
    -Path "publishing/activity-drafts/$($testActivity.DraftId)/test-runs/idempotency/$testRunKey" -ExpectStatus 200
Assert-LifecycleCondition "both Test Run lookup identities resolve the same receipt" (
    $testRunById.Json.testRunId -eq $testRunByKey.Json.testRunId)
Assert-Write -Ctx $ctx -Label "cancel activity Test Run (or deterministic terminal denial)" -Method POST `
    -Path "publishing/activity-test-runs/$($testRun.Json.testRunId)/cancel" `
    -ExpectStatus 202,409 | Out-Null

Complete-WriteSuite -Area "Publishing lifecycle"
