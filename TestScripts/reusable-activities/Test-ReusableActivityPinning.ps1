<#
.SYNOPSIS
    Exact-version pinning / no auto-cascade: a consumer binds a child reusable activity by an exact, immutable
    version id, not a floating "latest".
.DESCRIPTION
    Foundation has NO "auto-publish consuming workflows" cascade. A consumer binds a child reusable activity by
    EXACT `activityVersionId` (recorded as an authoritative outbound dependency edge) and stays pinned; there is
    no floating reference that would auto-move when the child changes. Propagating an update up a hierarchy is a
    deliberate, staged **upgrade-plan** operation (see ../README.md), not automatic.

    This test publishes child C (v1) and a consumer B whose graph root wraps C, then publishes C v2 and asserts
    B's authoritative outbound dependency remains EXACTLY C v1's version id. Because the binding is an immutable
    version id (not "latest"), republishing C cannot silently move B — the essence of "no auto-cascade".

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
$cV2Draft = Invoke-Step "create C v2 draft" { Add-ReusableActivityDraft -Ctx $ctx -DefinitionId $C.DefinitionId -PayloadJson (New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson ("[" + (New-WriteLineNodeJson -NodeId "c" -WriteLineVersionId $wl -Text "C v2 t=$t") + "]")) -SourceVersionId $C.VersionId }
$C2 = Invoke-Step "publish C v2" { Publish-ReusableDraft -Ctx $ctx -DraftId $cV2Draft.DraftId -Revision $cV2Draft.Revision -HeadVersionId $C.VersionId }
Write-Host ("[C] v1={0}" -f $C.VersionId)
Write-Host ("[C] v2={0}" -f $C2.VersionId)
Write-Host ("[B] v1={0}" -f $B.VersionId)

$deps = Invoke-Step "read B outbound deps after C v2" { Get-OutboundDependencyVersionIds -Ctx $ctx -VersionId $B.VersionId }
Write-Host ("B outbound deps: {0}" -f ($deps -join ', '))
$pinnedToExactC = ($deps -contains $C.VersionId) -and ($deps -notcontains $C2.VersionId)

Write-Host ""
if ($pinnedToExactC) {
    Write-Host ("SUCCESS - after publishing C v2 '{0}', consumer B remains pinned to C v1 '{1}'." -f $C2.VersionId, $C.VersionId) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - B did not remain pinned to C v1 after C v2 publication (deps: {0})" -f ($deps -join ', ')) -ForegroundColor Red
    exit 1
}
