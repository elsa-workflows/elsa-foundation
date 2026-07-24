<#
.SYNOPSIS
    Execute a DRAFT workflow without publishing it, via the workflow draft test-run API.
.DESCRIPTION
    Foundation lets you run an unpublished draft's authored state directly: `POST publishing/workflows/drafts/test-runs`
    compiles the inline `State`, dispatches it under a short-lived TestRun reference (no durable publication), and
    returns a `WorkflowExecutionId`. This is the "execute a draft workflow" case.

    Flow: submit a definition (creates the definition + an unpublished draft), then test-run the draft's state
    inline (never calling publish). Asserts the test run reaches a running/completed state and the instance
    completes. Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Execute a DRAFT workflow (draft test-run, no publish) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$t = Get-Random -Max 999999

# A definition (+ unpublished draft). We do NOT publish it.
$root = New-ActivityNode -NodeId "w" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "DRAFT test-run ran t=$t") )
$def = Invoke-Step "submit definition (creates draft)" { Submit-Workflow -Ctx $ctx -Name "DraftRun-$t" -Description "draft test-run" -RootActivity $root }
$definitionId = $def.definition.id

# Test-run the DRAFT state inline (no publish). State mirrors the submit `state` shape.
$state = @{ variables = @(); inputs = @(); outputs = @(); workflowActivityOptions = $null; strategyOptions = $null; rootActivity = $root }
$body = @{ definitionId = $definitionId; snapshotId = "snap-$t"; state = $state } | ConvertTo-Json -Depth 40
$tr = Invoke-Step "draft test-run" { Invoke-RestMethod "$BaseUrl/publishing/workflows/drafts/test-runs" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body }
Write-Host ("[test-run] id={0} status={1} execution={2} artifact={3}" -f $tr.testRunId, $tr.status, $tr.workflowExecutionId, $tr.artifactId)

$ok = $false
if ($tr.workflowExecutionId) {
    $inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $tr.workflowExecutionId
    Show-WorkflowInstance -Instance $inst
    $ok = $inst.instance.status -in @('Completed','Finished')
    $status = $inst.instance.status
} else {
    $status = $tr.status
    $ok = $tr.status -in @('Completed','Finished','Running','Dispatched','Accepted')
}

Write-Host ""
if ($ok) {
    Write-Host ("SUCCESS - draft executed without publishing (status '{0}')." -f $status) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - draft test-run status '{0}'." -f $status) -ForegroundColor Red
    exit 1
}
