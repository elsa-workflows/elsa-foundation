<#
.SYNOPSIS
    Verifies exact workflow-version preflight and promotion against the live Design API.
.DESCRIPTION
    Creates an unpublished workflow draft, asks the server to assess an author-requested
    forward SemVer, promotes that exact version, and confirms the immutable version read.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)

. "$PSScriptRoot/../write-endpoints/_WriteCommon.ps1"

$script:WPass = 0
$script:WTotal = 0

Write-Host "== Workflow exact-version promotion ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Invoke-Step "resolve WriteLine" {
    Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
}
$tag = Get-Random -Maximum 999999
$requestedVersion = "2.3.0-beta.$tag"
$root = New-ActivityNode -NodeId "write" -VersionId $writeLine -Inputs @(
    (New-LiteralInput -ReferenceKey "text" -Value "exact version $requestedVersion")
)

$created = Assert-Write -Ctx $ctx -Label "create unpublished workflow draft" -Method POST `
    -Path "design/workflows/definitions" `
    -Body @{
        name = "ExactVersion-$tag"
        description = "Exact-version promotion e2e fixture"
        initialState = (New-WorkflowState -RootActivity $root)
    } `
    -Validate { param($response) $response.Json.definition.id -and $response.Json.draft.id }

$definitionId = $created.Json.definition.id
$draftId = $created.Json.draft.id

Assert-Write -Ctx $ctx -Label "automatic promotion preflight" -Method POST `
    -Path "design/workflows/drafts/$draftId/promotion-preflight" `
    -Body @{} `
    -Validate {
        param($response)
        $response.Json.isReady -and
        $response.Json.assignmentMode -eq "automatic" -and
        $response.Json.resolvedVersion -eq "1.0.0" -and
        @($response.Json.issues).Count -eq 0
    } | Out-Null

Assert-Write -Ctx $ctx -Label "exact promotion preflight" -Method POST `
    -Path "design/workflows/drafts/$draftId/promotion-preflight" `
    -Body @{ requestedVersion = $requestedVersion } `
    -Validate {
        param($response)
        $response.Json.isReady -and
        $response.Json.assignmentMode -eq "exact" -and
        $response.Json.requestedVersion -eq $requestedVersion -and
        $response.Json.resolvedVersion -eq $requestedVersion -and
        @($response.Json.issues).Count -eq 0
    } | Out-Null

$promoted = Assert-Write -Ctx $ctx -Label "promote requested exact version" -Method POST `
    -Path "design/workflows/drafts/$draftId/promote" `
    -Body @{ requestedVersion = $requestedVersion } `
    -ExpectStatus 201 `
    -Validate {
        param($response)
        $response.Json.definitionVersion.version -eq $requestedVersion -or
        $response.Json.version -eq $requestedVersion
    }

$versionId = if ($promoted.Json.definitionVersion.id) {
    $promoted.Json.definitionVersion.id
} else {
    $promoted.Json.id
}

Assert-Write -Ctx $ctx -Label "read promoted immutable version" -Method GET `
    -Path "design/workflows/versions/$versionId" `
    -Validate {
        param($response)
        ($response.Json.definition.id -eq $definitionId -or
            $response.Json.definitionVersion.definitionId -eq $definitionId -or
            $response.Json.definitionId -eq $definitionId) -and
        ($response.Json.definitionVersion.version -eq $requestedVersion -or $response.Json.version -eq $requestedVersion)
    } | Out-Null

Complete-WriteSuite -Area "workflow exact-version promotion"
