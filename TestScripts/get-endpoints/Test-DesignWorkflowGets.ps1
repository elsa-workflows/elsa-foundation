<#
.SYNOPSIS
    GET-endpoint coverage for the workflow-design API (design/workflows/*).
.DESCRIPTION
    Exercises every reachable GET endpoint under the workflow-design area against a fixture definition
    (submit creates a definition + draft + version 1.0.0), plus negative (404) cases.
    Covered: definitions (list/get/versions), versions/{id}, drafts/{id}, drafts/{id}/validations.
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

Write-Host "== GET coverage: workflow-design API ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

$def = Invoke-Step "submit fixture" { Submit-Workflow -Ctx $ctx -Name "DwGet-$tag" -RootActivity (New-ActivityNode -NodeId "w" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "dw $tag") )) }
$definitionId = $def.definition.id
$versionId = $def.version.id
$draftId = $def.draftId
Write-Host ("[fixture] definition={0} version={1} draft={2}" -f $definitionId, $versionId, $draftId)
Write-Host ""

Assert-Get -Ctx $ctx -Label "definitions (list)" -Path "design/workflows/definitions" | Out-Null
Assert-Get -Ctx $ctx -Label "definitions/{id}" -Path "design/workflows/definitions/$definitionId" -Validate { param($r) $r.Json.definition.id -eq $definitionId } | Out-Null
Assert-Get -Ctx $ctx -Label "definitions/{id}/versions" -Path "design/workflows/definitions/$definitionId/versions" | Out-Null
Assert-Get -Ctx $ctx -Label "versions/{versionId}" -Path "design/workflows/versions/$versionId" | Out-Null
Assert-Get -Ctx $ctx -Label "drafts/{draftId}" -Path "design/workflows/drafts/$draftId" -Validate { param($r) $r.Json.draft.id -eq $draftId -or $r.Json.id -eq $draftId } | Out-Null
Assert-Get -Ctx $ctx -Label "drafts/{draftId}/validations" -Path "design/workflows/drafts/$draftId/validations" | Out-Null

Assert-Get -Ctx $ctx -Label "definitions/{bogus} -> 404" -Path "design/workflows/definitions/def-bogus$tag" -ExpectStatus 404 | Out-Null
Assert-Get -Ctx $ctx -Label "drafts/{bogus} -> 404" -Path "design/workflows/drafts/draft-bogus$tag" -ExpectStatus 404 | Out-Null

Complete-GetSuite -Area "workflow-design API"
