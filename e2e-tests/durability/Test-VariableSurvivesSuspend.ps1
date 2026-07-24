<#
.SYNOPSIS
    A workflow variable set BEFORE a durable suspend survives the checkpoint round-trip and is readable AFTER resume.
.DESCRIPTION
    Root Sequence is [ Set(Msg=<token>) -> Event wait -> SetOutput(Echo = Variable Msg) ]. Executing it sets the
    workflow-scope variable and suspends at the Event; a ResumeOnly stimulus (with input, #1014) then resumes it,
    and the post-wait SetOutput reads the SAME variable back. Asserts the echoed output equals the token - proving
    the variable was materialized from the persisted checkpoint across the suspension, not just held in memory.

    (This is the no-restart half of durability: it isolates the checkpoint materialization of the root variable
    frame - #972 - across a bookmark suspend. Test-RestartRecovery.ps1 adds a real process restart.)
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_DurabilityCommon.ps1"

Write-Host "== Variable survives suspend -> resume ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$ev  = Invoke-Step "resolve Event"    { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }
$E = "durvar-$(Get-Random -Max 999999)"
$token = "survived-$(Get-Random -Max 999999)"

$setVar = New-SetVariableNode -NodeId "set" -VariableKey "Msg" -Value $token
$wait   = New-EventWaitNode   -NodeId "wait" -EventVersionId $ev -EventName $E
$echo   = New-SetOutputNode   -NodeId "echo" -OutputName "Echo" -Value @{ value = "Msg"; expressionType = "Variable" }
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($setVar, $wait, $echo))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "DurVar-$(Get-Random -Max 9999)" -Description "variable survives suspend" -RootActivity $root -Variables @( (New-VariableDef -Key "Msg") ) }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$wfId = $run.workflowExecutionId
Start-Sleep -Milliseconds 800
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
Show-WorkflowInstance -Instance $inst

if (-not ((Get-NodeRunCount -Instance $inst -NodeId 'set') -ge 1 -and (Test-NodeSuspended -Instance $inst) -and (Get-NodeRunCount -Instance $inst -NodeId 'echo') -eq 0)) {
    Write-Host "FAIL (suspend) - expected Set-ran, Event-suspended, SetOutput-not-run." -ForegroundColor Red; exit 1
}
Write-Host "set the variable, then suspended at the Event (echo not yet run)."

$resp = Invoke-Step "resume stimulus" { Invoke-ResumeStimulus -Ctx $ctx -EventName $E }
Write-Host ("stimulus: resumedCount={0}" -f $resp.resumedCount)
$completed = $false
for ($i = 0; $i -lt 15 -and -not $completed; $i++) {
    Start-Sleep -Milliseconds 700
    $inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
    if ($inst.instance.status -in @('Completed','Finished') -and (Get-NodeRunCount -Instance $inst -NodeId 'echo') -ge 1) { $completed = $true }
}
Show-WorkflowInstance -Instance $inst
$echoed = ($inst.outputs.PSObject.Properties | Where-Object { $_.Name -ieq 'Echo' } | Select-Object -First 1).Value.value.preview

Write-Host ""
if ($completed -and $echoed -eq $token) {
    Write-Host ("SUCCESS - the variable set before the suspend survived and read back after resume: '{0}'" -f $echoed) -ForegroundColor Green
} else {
    Write-Host ("FAIL - status '{0}', echoed '{1}' (expected '{2}')" -f $inst.instance.status, $echoed, $token) -ForegroundColor Red
    exit 1
}
