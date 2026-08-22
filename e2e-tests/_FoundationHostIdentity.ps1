<#
.SYNOPSIS
    Shared identity composition for e2e suites that build an Elsa.Foundation.Host from packages.
.DESCRIPTION
    Elsa.Foundation.Host compiles in no Elsa feature, so a suite that stands one up has to compose
    identity itself. Every such suite needs the same four features, the same four packages in its
    feed, and the same login -- so it lives here rather than in any one suite.

    Until main retired FastEndpoints, suites avoided this entirely with
    ApiSecurity.AllowAnonymous, which Elsa.Api.FastEndpoints provided. That feature went with it.
    Logging in is the better probe anyway: an anonymous kill-switch showed the endpoints answered,
    not that they authorize the caller they are given.

    WHY THE EF CORE STORE AND NOT THE GROUNDWORK ONE. The Groundwork variant is the natural fit for a
    host that already composes Groundwork, and it ships a GroundworkIdentitySeeder -- but nothing
    populates that seeder's IdentitySeedOptions. Only the EF Core feature builds them, so only the EF
    Core feature yields an account to log in as. If a store-agnostic seeder ever lands, this is the
    one place that has to change.

.EXAMPLE
    . "$PSScriptRoot/../_FoundationHostIdentity.ps1"
    $features += Get-FoundationHostIdentityFeatures
    $projects += Get-FoundationHostIdentityProjects
    $ctx = Connect-FoundationHost -BaseUrl "http://localhost:5401"
#>

# The well-known development account these features seed. Matches what Elsa.Workbench seeds, so a
# suite can move between hosts without changing credentials.
$script:FoundationHostAdminUserName = 'admin'
$script:FoundationHostAdminPassword = 'Password123!'
$script:FoundationHostAdminEmail = 'admin@localhost'

<#
.SYNOPSIS
    The identity features a package-composed Foundation.Host needs to serve an authenticated API.
.DESCRIPTION
    Returned as an ordered hashtable so a caller can splat it into the Features block it writes to
    shells.json. Seeding is gated on the Development environment by DevelopmentOrDemoGuard, so the
    host must be started with ASPNETCORE_ENVIRONMENT=Development or startup fails closed.
#>
function Get-FoundationHostIdentityFeatures {
    param(
        [string] $UserName = $script:FoundationHostAdminUserName,
        [string] $Password = $script:FoundationHostAdminPassword,
        [string] $Email = $script:FoundationHostAdminEmail
    )
    return [ordered]@{
        FoundationIdentityAbstractions = @{}
        FoundationIdentityApi = @{}
        FoundationIdentityAspNetCoreIdentity = @{ IsDefault = $true }
        FoundationIdentityAspNetCoreIdentityEntityFrameworkCore = @{
            IsDevelopmentOrDemo = $true
            SeedAdminUserName = $UserName
            SeedAdminPassword = $Password
            SeedAdminEmail = $Email
        }
    }
}

<#
.SYNOPSIS
    The projects a suite must pack so the features above can resolve from its feed.
#>
function Get-FoundationHostIdentityProjects {
    return @(
        'src/Elsa/Foundation/Identity/Abstractions/Elsa.Foundation.Identity.Abstractions.csproj'
        'src/Elsa/Foundation/Identity/Api/Elsa.Foundation.Identity.Api.csproj'
        'src/Elsa/Foundation/Identity/AspNetCoreIdentity/Elsa.Foundation.Identity.AspNetCoreIdentity.csproj'
        'src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.csproj'
    )
}

<#
.SYNOPSIS
    Logs into a Foundation.Host composed with the features above and returns a session context.
.DESCRIPTION
    Shaped like the context Connect-Elsa returns, so call sites that already thread -WebSession
    need no change.
#>
function Connect-FoundationHost {
    param(
        [Parameter(Mandatory)][string] $BaseUrl,
        [string] $UserName = $script:FoundationHostAdminUserName,
        [string] $Password = $script:FoundationHostAdminPassword,
        [int] $TimeoutSec = 30
    )
    $session = $null
    $login = Invoke-RestMethod "$BaseUrl/_elsa/identity/login" -Method Post -SessionVariable session `
        -ContentType 'application/json' -TimeoutSec $TimeoutSec `
        -Body (@{ username = $UserName; password = $Password } | ConvertTo-Json)
    Write-Host ("  [login] {0} -> {1} as {2}" -f $BaseUrl, $login.status, $login.displayName)
    return @{ Session = $session; BaseUrl = $BaseUrl; Login = $login }
}
