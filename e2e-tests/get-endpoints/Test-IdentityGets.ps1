<#
.SYNOPSIS
    GET-endpoint coverage for the identity API (_elsa/identity/*).
.DESCRIPTION
    Exercises the identity-area GET endpoints using the authenticated cookie session.
    Covered: session, capabilities, bootstrap, token.
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

Write-Host "== GET coverage: identity API ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
Write-Host ""

Assert-Get -Ctx $ctx -Label "identity/session" -Path "_elsa/identity/session" | Out-Null
Assert-Get -Ctx $ctx -Label "identity/capabilities" -Path "_elsa/identity/capabilities" | Out-Null
Assert-Get -Ctx $ctx -Label "identity/bootstrap" -Path "_elsa/identity/bootstrap" | Out-Null
Assert-Get -Ctx $ctx -Label "identity/token" -Path "_elsa/identity/token" | Out-Null

Complete-GetSuite -Area "identity API"
