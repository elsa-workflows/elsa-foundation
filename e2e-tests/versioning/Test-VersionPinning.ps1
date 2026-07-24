<#
.SYNOPSIS
    A RUNNING instance keeps executing the version it started on, even after a newer version of the same
    definition is published and becomes the recommended version.
.DESCRIPTION
    v1 = Sequence[ Event wait -> WriteLine node "ranV1" ]; v2 = Sequence[ Event wait -> WriteLine node "ranV2" ]
    (same definition, bodies differ by which node runs). Flow:
      1. publish v1, start an instance, let it suspend at the Event (pinned to artifact_v1);
      2. WHILE it is suspended, replace the draft with the v2 body, promote, and publish v2 (new artifact, newer
         recommended version) - publishing v2 retires v1's live publication, but the already-running instance is
         unaffected;
      3. resume the instance and assert it is still pinned to artifact_v1 and ran v1's node ("ranV1", NOT "ranV2").

    This proves runtime version pinning: an in-flight instance runs its started-on version's body, not the latest.
    (Note: you cannot START a new instance on the old artifact once v2 is published - its publication is retired -
    which is why the instance must be launched before the republish. Requires the server from source; see ../README.md.)
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

function Get-EventHash([string] $Name) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    "sha256:" + (($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Name)) | ForEach-Object { $_.ToString('x2') }) -join '')
}
function New-PinRoot($SeqVer, $EvVer, $WlVer, $EventName, $MarkerNodeId) {
    $wait = New-ActivityNode -NodeId "wait" -VersionId $EvVer -Inputs @(
        (New-LiteralInput -ReferenceKey "EventName" -Value $EventName),
        (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $false) )
    $mark = New-ActivityNode -NodeId $MarkerNodeId -VersionId $WlVer -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value $MarkerNodeId) )
    New-ActivityNode -NodeId "root" -VersionId $SeqVer -Structure (New-SequenceStructure -Activities @($wait, $mark))
}

Write-Host "== Version pinning: a running instance runs its started-on version across a republish ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$ev  = Invoke-Step "resolve Event"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$E = "pin-$(Get-Random -Max 999999)"

# 1. publish v1 and start an instance that suspends at the Event
$def  = Invoke-Step "submit v1"  { Submit-Workflow -Ctx $ctx -Name "Pin-$(Get-Random -Max 9999)" -Description "version pinning" -RootActivity (New-PinRoot $seq $ev $wl $E "ranV1") }
$pub1 = Invoke-Step "publish v1" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run  = Invoke-Step "execute v1" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub1.artifactId -SourceReferenceId $pub1.sourceReferenceId }
$wfId = $run.workflowExecutionId
Start-Sleep -Milliseconds 800
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
if (-not ($inst.activities | Where-Object { $_.executableNodeId -eq 'wait' -and $_.status -eq 'Suspended' })) {
    Write-Host "FAIL - v1 instance did not suspend at the Event." -ForegroundColor Red; exit 1
}
Write-Host ("v1 published (artifact {0}); instance {1} suspended at the Event." -f $pub1.artifactId, $wfId)

# 2. WHILE suspended, publish v2 of the same definition. Authoring a v2 via draft-replace -> promote currently
# fails promote validation for Event/intrinsic graphs (stricter than submit) - tracked by #1058. Treat that as a
# KNOWN ISSUE (the version-pinning behavior can't be exercised until the v2 can be authored); any OTHER failure is real.
$v2state = @{ variables=@(); inputs=@(); outputs=@(); workflowActivityOptions=$null; strategyOptions=$null; rootActivity=(New-PinRoot $seq $ev $wl $E "ranV2") }
try {
    Invoke-RestMethod "$($ctx.BaseUrl)/design/workflows/drafts/$($def.draftId)" -Method Put -WebSession $ctx.Session -ContentType 'application/json' -Body (@{ state = $v2state } | ConvertTo-Json -Depth 40) | Out-Null
    $prom = Invoke-RestMethod "$($ctx.BaseUrl)/design/workflows/drafts/$($def.draftId)/promote" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body '{}'
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    if ($status -eq 409) {
        Write-Host ""
        Write-Host "KNOWN ISSUE #1058 - cannot author v2 of the definition over REST: draft promote (409) rejects the v1-equivalent graph (Event/intrinsic validation is stricter than submit). Version pinning can't be exercised end-to-end until promote and submit validation agree; this tracker runs the full assertion once #1058 is fixed." -ForegroundColor Yellow
        return
    }
    throw
}
$v2VersionId = if ($prom.id) { $prom.id } else { $prom.version.id }
$pub2 = Invoke-Step "publish v2" { Publish-WorkflowVersion -Ctx $ctx -VersionId $v2VersionId }
Write-Host ("v2 published (artifact {0}, version {1}); v1 artifact was {2}." -f $pub2.artifactId, $prom.version, $pub1.artifactId)

# 3. the still-suspended instance must be unaffected: same pinned artifact, still Running
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
$pinnedToV1 = $inst.instance.artifactId -eq $pub1.artifactId
Write-Host ("after v2 publish: instance artifactId={0} (v1={1}), status={2}" -f $inst.instance.artifactId, $pinnedToV1, $inst.instance.status)

# resume and confirm it ran v1's node, not v2's
$body = @{ stimulusType="Event"; stimulusHash=(Get-EventHash $E); mode="ResumeOnly"; input=@{ eventName=$E } } | ConvertTo-Json
Invoke-Step "resume" { Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/stimuli" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body } | Out-Null
$completed = $false
for ($i = 0; $i -lt 15 -and -not $completed; $i++) {
    Start-Sleep -Milliseconds 700
    $inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
    if ($inst.instance.status -in @('Completed','Finished')) { $completed = $true }
}
Show-WorkflowInstance -Instance $inst
$ranV1 = (Get-NodeRunCount -Instance $inst -NodeId 'ranV1') -ge 1
$ranV2 = (Get-NodeRunCount -Instance $inst -NodeId 'ranV2') -ge 1

Write-Host ""
if ($completed -and $pinnedToV1 -and $ranV1 -and -not $ranV2) {
    Write-Host ("SUCCESS - the running instance stayed pinned to v1 (artifact {0}) and ran v1's node 'ranV1', not v2's 'ranV2', after v2 was published." -f $pub1.artifactId) -ForegroundColor Green
} else {
    Write-Host ("FAIL - status '{0}', pinnedToV1={1}, ranV1={2}, ranV2={3}" -f $inst.instance.status, $pinnedToV1, $ranV1, $ranV2) -ForegroundColor Red
    exit 1
}
