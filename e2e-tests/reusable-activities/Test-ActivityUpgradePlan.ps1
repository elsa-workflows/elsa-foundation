<#
.SYNOPSIS
    Persisted activity upgrade-plan journey: create, read, apply one stage, publish its handoff,
    refresh, apply the successor stage, and verify exact-version dependency pinning.
.DESCRIPTION
    Builds a three-level reusable-activity chain A -> B -> C. C is published twice; the plan replaces
    C v1 with C v2 and starts at the published A root. Because published dependents are upgraded in
    dependency order, the first apply creates a B draft and waits for its publication. Refreshing with
    that exact publication creates the successor plan for A. The final assertions prove that B points
    to C v2 and A points to the newly published B version, while the old exact versions are absent.

    This is intentionally a persisted REST journey, not an in-process planner test. Requires the server
    running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

function Get-UpgradePlan {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $PlanId)
    Invoke-RestMethod "$($Ctx.BaseUrl)/design/activities/upgrade-plans/$PlanId" -WebSession $Ctx.Session
}

function Get-UpgradeReceipt {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $PlanId,
        [Parameter(Mandatory)][string] $ReceiptId
    )
    Invoke-RestMethod "$($Ctx.BaseUrl)/design/activities/upgrade-plans/$PlanId/receipts/$ReceiptId" -WebSession $Ctx.Session
}

function Assert-Upgrade {
    param([Parameter(Mandatory)][bool] $Condition, [Parameter(Mandatory)][string] $Message)
    if (-not $Condition) { throw "Upgrade-plan assertion failed: $Message" }
}

Write-Host "== Persisted activity upgrade-plan journey ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

# Build a published dependency chain A -> B -> C v1, then publish C v2 without changing B or A.
$C = Invoke-Step "publish C v1" {
    Publish-ReusableActivity -Ctx $ctx -DisplayName "UpgradeC-$tag" -PayloadJson (
        New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson (
            "[" + (New-WriteLineNodeJson -NodeId "c" -WriteLineVersionId $wl -Text "C v1 t=$tag") + "]"))
}
$B = Invoke-Step "publish B (root-wraps C v1)" {
    Publish-ReusableActivity -Ctx $ctx -DisplayName "UpgradeB-$tag" -PayloadJson (New-RootWrapManifestJson -RootVersionId $C.VersionId)
}
$A = Invoke-Step "publish A (root-wraps B v1)" {
    Publish-ReusableActivity -Ctx $ctx -DisplayName "UpgradeA-$tag" -PayloadJson (New-RootWrapManifestJson -RootVersionId $B.VersionId)
}
$cV2Draft = Invoke-Step "create C v2 draft" {
    Add-ReusableActivityDraft -Ctx $ctx -DefinitionId $C.DefinitionId -PayloadJson (
        New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson (
            "[" + (New-WriteLineNodeJson -NodeId "c" -WriteLineVersionId $wl -Text "C v2 t=$tag") + "]"))
}
$C2 = Invoke-Step "publish C v2" {
    Publish-ReusableDraft -Ctx $ctx -DraftId $cV2Draft.DraftId -Revision $cV2Draft.Revision -HeadVersionId $C.VersionId
}

Write-Host ("[chain] C v1={0} C v2={1} B v1={2} A v1={3}" -f $C.VersionId, $C2.VersionId, $B.VersionId, $A.VersionId)

$planRequest = @{
    replacements = @(@{ fromVersionId = $C.VersionId; toVersionId = $C2.VersionId })
    roots = @(@{ kind = "ActivityVersion"; id = $A.VersionId })
    includeTransitiveDependents = $true
    createDraftsForPublishedDependents = $true
} | ConvertTo-Json -Depth 20
$plan = Invoke-Step "create persisted upgrade plan rooted at A" {
    Invoke-RestMethod "$($ctx.BaseUrl)/design/activities/upgrade-plans" -Method Post -WebSession $ctx.Session `
        -ContentType 'application/json' -Body $planRequest
}
Assert-Upgrade ($plan.planId -and $plan.status -in @('Ready', 'AwaitingPublication')) "create returned a persisted plan and a staged status"
$storedPlan = Invoke-Step "get persisted upgrade plan" { Get-UpgradePlan -Ctx $ctx -PlanId $plan.planId }
Assert-Upgrade ($storedPlan.planId -eq $plan.planId) "GET returned the created plan"
Assert-Upgrade (@($storedPlan.steps).Count -ge 2) "plan contains dependent stages for B and A"

$readyStage = @($storedPlan.stages | Where-Object { $_.status -eq 'Ready' }) | Select-Object -First 1
Assert-Upgrade ($null -ne $readyStage) "the first dependency stage is ready"
Write-Host ("[plan] id={0} stages={1} first-ready={2}" -f $plan.planId, @($storedPlan.stages).Count, $readyStage.stageId)

$firstApply = Invoke-Step "apply first ready upgrade stage" {
    $body = @{ stageId = $readyStage.stageId; idempotencyKey = "upgrade-$tag-stage-1" } | ConvertTo-Json
    Invoke-RestMethod "$($ctx.BaseUrl)/design/activities/upgrade-plans/$($plan.planId)/apply" -Method Post `
        -WebSession $ctx.Session -ContentType 'application/json' -Body $body
}
Assert-Upgrade ($firstApply.status -eq 'AwaitingPublication') "first stage waits for the dependent publication"
Assert-Upgrade (@($firstApply.drafts).Count -eq 1) "first stage produced exactly one dependent draft"
Assert-Upgrade (@($firstApply.awaitingPublications).Count -eq 1) "first stage returned one publication handoff"
$firstHandoff = $firstApply.awaitingPublications[0]
$firstReceipt = Invoke-Step "get first apply receipt" {
    Get-UpgradeReceipt -Ctx $ctx -PlanId $plan.planId -ReceiptId $firstApply.receiptId
}
Assert-Upgrade ($firstReceipt.status -eq 'Applied' -and $firstReceipt.result.receiptId -eq $firstApply.receiptId) "first apply receipt is persisted and applied"

$firstDraft = $firstApply.drafts | Select-Object -First 1
Assert-Upgrade ($firstDraft.draftId -eq $firstHandoff.draftId) "the handoff identifies the exact applied draft"
$B2 = Invoke-Step "publish upgraded B handoff draft" {
    Publish-ReusableDraft -Ctx $ctx -DraftId $firstDraft.draftId -Revision ([long]$firstDraft.revision) `
        -HeadVersionId $B.VersionId
}
Write-Host ("[handoff] published B v2={0} from draft={1}" -f $B2.VersionId, $firstDraft.draftId)

$refreshRequest = @{
    publications = @(@{
        receiptId = $firstApply.receiptId
        publishedDrafts = @(@{ draftId = $firstDraft.draftId; publishedVersionId = $B2.VersionId })
    })
} | ConvertTo-Json -Depth 20
$successor = Invoke-Step "refresh plan with exact B publication" {
    Invoke-RestMethod "$($ctx.BaseUrl)/design/activities/upgrade-plans/$($plan.planId)/refresh" -Method Post `
        -WebSession $ctx.Session -ContentType 'application/json' -Body $refreshRequest
}
Assert-Upgrade ($successor.predecessorPlanId -eq $plan.planId) "refresh persisted a successor plan linked to its predecessor"
Assert-Upgrade (@($successor.stages | Where-Object { $_.status -eq 'Ready' }).Count -ge 1) "successor plan exposes the next ready stage"
$storedSuccessor = Invoke-Step "get refreshed successor plan" { Get-UpgradePlan -Ctx $ctx -PlanId $successor.planId }
Assert-Upgrade ($storedSuccessor.planId -eq $successor.planId) "GET returned the refreshed successor"

$nextStage = @($storedSuccessor.stages | Where-Object { $_.status -eq 'Ready' }) | Select-Object -First 1
$secondApply = Invoke-Step "apply successor A stage" {
    $body = @{ stageId = $nextStage.stageId; idempotencyKey = "upgrade-$tag-stage-2" } | ConvertTo-Json
    Invoke-RestMethod "$($ctx.BaseUrl)/design/activities/upgrade-plans/$($successor.planId)/apply" -Method Post `
        -WebSession $ctx.Session -ContentType 'application/json' -Body $body
}
Assert-Upgrade ($secondApply.status -eq 'Applied') "final stage completes the successor plan"
Assert-Upgrade (@($secondApply.drafts).Count -eq 1 -and @($secondApply.awaitingPublications).Count -eq 0) "final stage produced one A draft with no further handoff"
$secondReceipt = Invoke-Step "get final apply receipt" {
    Get-UpgradeReceipt -Ctx $ctx -PlanId $successor.planId -ReceiptId $secondApply.receiptId
}
Assert-Upgrade ($secondReceipt.status -eq 'Applied' -and $secondReceipt.result.status -eq 'Applied') "final apply receipt is persisted and applied"

$finalDraft = $secondApply.drafts | Select-Object -First 1
$A2 = Invoke-Step "publish final upgraded A handoff draft" {
    Publish-ReusableDraft -Ctx $ctx -DraftId $finalDraft.draftId -Revision ([long]$finalDraft.revision) `
        -HeadVersionId $A.VersionId
}
$finalPlan = Invoke-Step "read completed successor plan" { Get-UpgradePlan -Ctx $ctx -PlanId $successor.planId }
Assert-Upgrade ($finalPlan.status -eq 'Applied') "completed successor plan remains persisted as Applied"

# The authoritative dependency projection must contain the new exact identities and must not retain the old pins.
$bDeps = Invoke-Step "read upgraded B outbound dependencies" { Get-OutboundDependencyVersionIds -Ctx $ctx -VersionId $B2.VersionId }
$aDeps = Invoke-Step "read upgraded A outbound dependencies" { Get-OutboundDependencyVersionIds -Ctx $ctx -VersionId $A2.VersionId }
Assert-Upgrade (($bDeps -contains $C2.VersionId) -and ($bDeps -notcontains $C.VersionId)) "B is pinned to C v2 and no longer to C v1"
Assert-Upgrade (($aDeps -contains $B2.VersionId) -and ($aDeps -notcontains $B.VersionId)) "A is pinned to upgraded B and no longer to B v1"

Write-Host ""
Write-Host ("SUCCESS - persisted upgrade plan {0} -> {1} upgraded A/B through exact publication handoffs; B depends on C v2 and A depends on B v2." -f $plan.planId, $successor.planId) -ForegroundColor Green
