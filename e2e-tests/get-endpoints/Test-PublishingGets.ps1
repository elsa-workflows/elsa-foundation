<#
.SYNOPSIS
    GET-endpoint coverage for the publishing API (publishing/*).
.DESCRIPTION
    Exercises the reachable GET endpoints under the publishing area against a fixture (a published workflow
    definition + a known activity version), plus negative (404) cases.
    Covered: activities (list) + activities/{id}/construct; value-conversion/profiles; incident-strategies; descriptors;
    workflows/{definitionId}/slots + /policy.
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

Write-Host "== GET coverage: publishing API ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

# fixture: a published workflow definition (slots/policy are per-definition)
$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "PubGet-$tag" -RootActivity (New-ActivityNode -NodeId "w" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "pub $tag") )) }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$definitionId = $def.definition.id
Write-Host ("[fixture] definition={0} writeLineVersion={1}" -f $definitionId, $wl)
Write-Host ""

Assert-Get -Ctx $ctx -Label "activities (list)" -Path "publishing/activities" | Out-Null
Assert-Get -Ctx $ctx -Label "activities/{id}/construct" -Path "publishing/activities/$wl/construct" -Validate { param($r) $null -ne $r.Json } | Out-Null
Assert-Get -Ctx $ctx -Label "value-conversion/profiles" -Path "publishing/value-conversion/profiles" -Validate { param($r) $null -ne $r.Json.items } | Out-Null
Assert-Get -Ctx $ctx -Label "incident-strategies" -Path "publishing/incident-strategies" -Validate {
    param($r)
    $r.Json.items.Count -ge 2 -and
    $r.Json.defaultStrategy.alias -eq "Fault" -and
    $r.Json.defaultStrategy.version -eq "1"
} | Out-Null
Assert-Get -Ctx $ctx -Label "workflows/{definitionId}/slots" -Path "publishing/workflows/$definitionId/slots" | Out-Null
Assert-Get -Ctx $ctx -Label "workflows/{definitionId}/policy" -Path "publishing/workflows/$definitionId/policy" | Out-Null

Assert-Get -Ctx $ctx -Label "activities/{bogus}/construct -> 404" -Path "publishing/activities/actver-bogus$tag/construct" -ExpectStatus 404 | Out-Null
# slots is lenient: an unknown definitionId returns an empty list (200), not 404.
Assert-Get -Ctx $ctx -Label "workflows/{bogus}/slots (lenient 200 empty)" -Path "publishing/workflows/def-bogus$tag/slots" -ExpectStatus 200 | Out-Null

Complete-GetSuite -Area "publishing API"
