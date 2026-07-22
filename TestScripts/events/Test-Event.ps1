<#
.SYNOPSIS
    Events: an Event-triggered workflow is started by publishing a stimulus to the runtime.
.DESCRIPTION
    Foundation-native equivalent of the JTest "basic-event" pub/sub case. Classic Elsa used a PublishEvent
    activity + an Event listener workflow; Foundation has no PublishEvent activity - instead an `Event` activity
    is a start trigger, and events are published by POSTing a stimulus to `runtime/workflows/stimuli`.

    Flow: publish a workflow whose root is `Event(EventName=<unique>, CanStartWorkflow=true) -> SetOutput`, then
    POST the matching stimulus. The stimulus identity is (StimulusType="Event", StimulusHash="sha256:"+hex(SHA256(eventName))).
    The response returns the started workflowExecutionId directly; we assert that instance completed.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl   = "http://localhost:5095",
    [string] $Username  = "admin",
    [string] $Password  = "Password123!",
    [string] $EventName = "evt-$(Get-Random -Maximum 999999)"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

# StimulusHash = "sha256:" + lowercase-hex(SHA256(utf8(eventName))) - must match Elsa's EventStimulus.Hash.
function Get-EventStimulusHash([string] $name) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hex = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($name)) | ForEach-Object { $_.ToString('x2') }) -join ''
    return "sha256:$hex"
}

Write-Host "== Event trigger + stimulus ==  -> $BaseUrl  (event '$EventName')" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq   = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$event = Invoke-Step "resolve Event"    { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }

$eventNode = New-ActivityNode -NodeId "wait-event" -VersionId $event -Inputs @(
    (New-LiteralInput -ReferenceKey "EventName" -Value $EventName),
    (New-LiteralInput -ReferenceKey "CanStartWorkflow" -Value $true)
)
$done = New-SetOutputNode -NodeId "handled" -OutputName "Fired" -Value "yes"
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($eventNode, $done))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Event-$(Get-Random -Max 9999)" -Description "event-triggered" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
Write-Host ("[publish] event trigger registered (artifact {0})" -f $pub.artifactId)
Start-Sleep -Seconds 1

$hash = Get-EventStimulusHash $EventName
Write-Host ("[publish event] stimulusType=Event hash=$hash")
$stimBody = @{ stimulusType = "Event"; stimulusHash = $hash; mode = "StartOnly" } | ConvertTo-Json
$resp = Invoke-Step "dispatch stimulus" { Invoke-RestMethod "$BaseUrl/runtime/workflows/stimuli" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $stimBody }
Write-Host ("[stimulus] startedCount={0}" -f $resp.startedCount)

if ($resp.startedCount -lt 1 -or -not $resp.starts) {
    Write-Host "FAIL - stimulus started no workflow (trigger not registered for this event?)" -ForegroundColor Red; exit 1
}
$execId = $resp.starts[0].workflowExecutionId
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $execId
Show-WorkflowInstance -Instance $inst
$fired = ($inst.outputs.PSObject.Properties | Where-Object { $_.Name -ieq 'Fired' } | Select-Object -First 1).Value.value.preview

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished') -and $fired -eq 'yes') {
    Write-Host ("SUCCESS - event '{0}' started workflow {1}, which completed" -f $EventName, $execId) -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - status '{0}', Fired='{1}'" -f $inst.instance.status, $fired) -ForegroundColor Red
}
