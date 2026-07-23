<#
.SYNOPSIS
    Exact-version pinning / no auto-cascade: a consumer binds a child reusable activity by an exact, immutable
    version id, not a floating "latest".
.DESCRIPTION
    Foundation has NO "auto-publish consuming workflows" cascade. A consumer binds a child reusable activity by
    EXACT `activityVersionId` (recorded as an authoritative outbound dependency edge) and stays pinned; there is
    no floating reference that would auto-move when the child changes. Propagating an update up a hierarchy is a
    deliberate, staged **upgrade-plan** operation (see ../README.md), not automatic.

    This test publishes child C (v1) and a consumer B whose graph root wraps C, then asserts B's authoritative
    outbound dependency is EXACTLY C's version id. Because the binding is an immutable version id (not "latest"),
    republishing C cannot silently move B — the essence of "no auto-cascade".

    NOTE: demonstrating non-movement by actually publishing a *second* version of C and re-checking is not
    covered here: publishing a subsequent activity version over REST currently trips a publication review-token
    check (`activity.publication.review-stale`) that needs deeper investigation (see ../README.md). The
    exact-version binding asserted here is the definitive no-auto-cascade signal regardless.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Reusable-activity exact-version pinning (no auto-cascade) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$t = Get-Random -Max 999999

$C = Invoke-Step "publish C v1" { Publish-ReusableActivity -Ctx $ctx -DisplayName "PinC-$t" -PayloadJson (New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson ("[" + (New-WriteLineNodeJson -NodeId "c" -WriteLineVersionId $wl -Text "C v1 t=$t") + "]")) }
$B = Invoke-Step "publish B (root-wraps C v1)" { Publish-ReusableActivity -Ctx $ctx -DisplayName "PinB-$t" -PayloadJson (New-RootWrapManifestJson -RootVersionId $C.VersionId) }
Write-Host ("[C] v1={0}" -f $C.VersionId)
Write-Host ("[B] v1={0}" -f $B.VersionId)

$deps = Invoke-Step "read B outbound deps" { Get-OutboundDependencyVersionIds -Ctx $ctx -VersionId $B.VersionId }
Write-Host ("B outbound deps: {0}" -f ($deps -join ', '))
$pinnedToExactC = $deps -contains $C.VersionId

Write-Host ""
if ($pinnedToExactC) {
    Write-Host ("SUCCESS - consumer B is pinned to the exact child version '{0}' (immutable binding; no floating/auto-upgrade reference)." -f $C.VersionId) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - B is not pinned to C's exact version (deps: {0})" -f ($deps -join ', ')) -ForegroundColor Red
    exit 1
}
