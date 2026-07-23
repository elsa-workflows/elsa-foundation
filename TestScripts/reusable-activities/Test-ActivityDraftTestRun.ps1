<#
.SYNOPSIS
    Execute a reusable-activity DRAFT without publishing it, via the activity draft test-run API.
.DESCRIPTION
    The reusable-activity counterpart of the workflow draft test-run: `POST publishing/activity-drafts/{draftId}/test-runs`
    compiles the exact draft revision, wraps it as the root of a synthetic workflow, and dispatches it under a
    short-lived TestRun reference. Lets you run a reusable activity while it is still an unpublished draft.

    Flow: create a reusable-activity definition (+ draft) but do NOT publish it; test-run the draft; assert the
    run reaches a running/completed state. Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Execute a reusable-activity DRAFT (activity draft test-run) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$t = Get-Random -Max 999999

# Create a reusable-activity DRAFT only (no publish).
$manifest = New-GraphManifestJson -SeqVersionId $seq -ActivitiesJson ("[" + (New-WriteLineNodeJson -NodeId "w" -WriteLineVersionId $wl -Text "DRAFT reusable ran t=$t") + "]")
$d = Invoke-Step "create reusable draft" { New-ReusableActivityDefinition -Ctx $ctx -DisplayName "DraftAct-$t" -PayloadJson $manifest }
Write-Host ("[draft] def={0} draft={1} rev={2}" -f $d.DefinitionId, $d.DraftId, $d.Revision)

$body = @{ expectedRevision = $d.Revision; idempotencyKey = [guid]::NewGuid().ToString() } | ConvertTo-Json
$tr = Invoke-Step "activity draft test-run" { Invoke-RestMethod "$BaseUrl/publishing/activity-drafts/$($d.DraftId)/test-runs" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body }
Write-Host "[test-run response]:"
$tr | ConvertTo-Json -Depth 6

$execId = $tr.workflowExecutionId
$ok = $false; $status = $tr.status
if ($execId) {
    $inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $execId
    Show-WorkflowInstance -Instance $inst
    $status = $inst.instance.status
    $ok = $status -in @('Completed','Finished')
} else {
    $ok = $tr.status -in @('Completed','Finished','Running','Dispatched','Accepted','DispatchAccepted')
}

Write-Host ""
if ($ok) {
    Write-Host ("SUCCESS - reusable-activity draft executed without publishing (status '{0}')." -f $status) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - activity draft test-run status '{0}'." -f $status) -ForegroundColor Red
    exit 1
}
