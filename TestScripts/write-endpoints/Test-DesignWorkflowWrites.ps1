<#
.SYNOPSIS
    Write-endpoint coverage for the workflow-design API (design/workflows/*): the full CRUD lifecycle.
.DESCRIPTION
    Exercises every mutating endpoint under the workflow-design area against throwaway fixtures, with valid and
    negative (404/409) cases: submit; add; patch metadata; replace draft; promote draft; soft-delete + restore;
    permanent-delete; discard draft. Route-bound ids are in the path; bodies exclude them.
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

Write-Host "== WRITE coverage: workflow-design API (CRUD lifecycle) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999
function Node([string]$id,[string]$text) { New-ActivityNode -NodeId $id -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value $text) ) }

# 1. submit (create def + draft + v1.0.0)
$submitBody = @{ name = "WwSubmit-$tag"; description = "write coverage"; state = (New-WorkflowState -RootActivity (Node "w" "v1")) }
$submit = Assert-Write -Ctx $ctx -Label "definitions/submit" -Method POST -Path "design/workflows/definitions/submit" -Body $submitBody -Validate { param($r) $r.Json.draftId }
$defId = $submit.Json.definition.id; $draftId = $submit.Json.draftId
Write-Host "    (def=$defId draft=$draftId)"

# 2. add (create def + draft, no version)
$addBody = @{ name = "WwAdd-$tag"; description = "added"; initialState = (New-WorkflowState -RootActivity (Node "w" "add")) }
$add = Assert-Write -Ctx $ctx -Label "definitions (add)" -Method POST -Path "design/workflows/definitions" -Body $addBody
$addDefId = $add.Json.definition.id

# 3. patch metadata
Assert-Write -Ctx $ctx -Label "definitions/{id} (patch metadata)" -Method PATCH -Path "design/workflows/definitions/$defId" -Body @{ name = "WwSubmit-$tag-renamed" } | Out-Null

# 4. replace draft (update state)
Assert-Write -Ctx $ctx -Label "drafts/{id} (replace)" -Method PUT -Path "design/workflows/drafts/$draftId" -Body @{ state = (New-WorkflowState -RootActivity (Node "w" "v2-edited")) } | Out-Null

# 5. promote draft -> new version
Assert-Write -Ctx $ctx -Label "drafts/{id}/promote" -Method POST -Path "design/workflows/drafts/$draftId/promote" -Body @{} | Out-Null

# 6. soft-delete then restore (the submitted def)
Assert-Write -Ctx $ctx -Label "definitions/{id} (soft delete)" -Method DELETE -Path "design/workflows/definitions/$defId" -Body @{} | Out-Null
Assert-Write -Ctx $ctx -Label "definitions/{id}/restore" -Method POST -Path "design/workflows/definitions/$defId/restore" -Body @{} | Out-Null

# 7. permanent-delete a throwaway def (valid lifecycle: soft-delete FIRST, then permanent)
Assert-Write -Ctx $ctx -Label "definitions/{id} (soft delete, pre-permanent)" -Method DELETE -Path "design/workflows/definitions/$addDefId" -Body @{} | Out-Null
Assert-Write -Ctx $ctx -Label "definitions/{id}/permanent (hard delete)" -Method DELETE -Path "design/workflows/definitions/$addDefId/permanent" -Body @{} | Out-Null

# 8. discard a throwaway draft
$thr = Assert-Write -Ctx $ctx -Label "definitions (add, throwaway for discard)" -Method POST -Path "design/workflows/definitions" -Body @{ name = "WwDiscard-$tag"; initialState = (New-WorkflowState -RootActivity (Node "w" "d")) }
$thrDraft = $thr.Json.draft.id
Assert-Write -Ctx $ctx -Label "drafts/{id} (discard)" -Method DELETE -Path "design/workflows/drafts/$thrDraft" -Body @{} | Out-Null

# --- negatives ---
Assert-Write -Ctx $ctx -Label "patch {bogus} -> 404" -Method PATCH -Path "design/workflows/definitions/def-bogus$tag" -Body @{ name = "x" } -ExpectStatus 404 | Out-Null
Assert-Write -Ctx $ctx -Label "delete {bogus} -> 404" -Method DELETE -Path "design/workflows/definitions/def-bogus$tag" -Body @{} -ExpectStatus 404 | Out-Null
Assert-Write -Ctx $ctx -Label "promote {bogus} -> 404" -Method POST -Path "design/workflows/drafts/draft-bogus$tag/promote" -Body @{} -ExpectStatus 404 | Out-Null
Assert-Write -Ctx $ctx -Label "permanent-delete {bogus} -> 404" -Method DELETE -Path "design/workflows/definitions/def-bogus$tag/permanent" -Body @{} -ExpectStatus 404 | Out-Null
$thr2 = Assert-Write -Ctx $ctx -Label "definitions (add, throwaway for permanent-without-soft)" -Method POST -Path "design/workflows/definitions" -Body @{ name = "WwNoSoft-$tag"; initialState = (New-WorkflowState -RootActivity (Node "w" "n")) }
Assert-Write -Ctx $ctx -Label "permanent without prior soft-delete -> 409" -Method DELETE -Path "design/workflows/definitions/$($thr2.Json.definition.id)/permanent" -Body @{} -ExpectStatus 409 | Out-Null

Complete-WriteSuite -Area "workflow-design API"
