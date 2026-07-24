<#
.SYNOPSIS
    A suspended instance survives a real server-process restart and resumes correctly from the on-disk checkpoint.
.DESCRIPTION
    The end-to-end durability proof the in-process C# crash tests cannot give (they stub the store in memory and
    drive the sweep by hand). Root Sequence is [ Set(Msg=<token>) -> Event wait -> SetOutput(Echo = Variable Msg) ]:
      1. execute -> Set runs, the instance suspends at the Event (persisted to SQLite on disk);
      2. **kill the Elsa.Server process and relaunch it** against the same SQLite database (-RestartServer, default on);
      3. after restart, the instance is still Suspended with identical state (Set ran exactly once - no duplicate
         replay - and SetOutput has not run);
      4. a ResumeOnly stimulus (with input, #1014) resumes it to completion and the post-wait SetOutput reads the
         variable back, proving the variable set BEFORE the crash was materialized from the persisted checkpoint.

    Restart uses `dotnet run --no-build`, so the server must already be built (see _DurabilityCommon.ps1). Pass
    -RestartServer:$false to run the suspend->resume assertions WITHOUT a restart (weaker; use where the runner
    cannot own the server process). Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [int]    $Port     = 5095,
    [switch] $RestartServer = $true
)
. "$PSScriptRoot/_DurabilityCommon.ps1"

Write-Host "== Restart recovery (kill + relaunch mid-suspend) ==  -> $BaseUrl  (restart=$RestartServer)" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$ev  = Invoke-Step "resolve Event"    { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }
$E = "durrestart-$(Get-Random -Max 999999)"
$token = "persisted-$(Get-Random -Max 999999)"

$setVar = New-SetVariableNode -NodeId "set" -VariableKey "Msg" -Value $token
$wait   = New-EventWaitNode   -NodeId "wait" -EventVersionId $ev -EventName $E
$echo   = New-SetOutputNode   -NodeId "echo" -OutputName "Echo" -Value @{ value = "Msg"; expressionType = "Variable" }
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($setVar, $wait, $echo))

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "DurRestart-$(Get-Random -Max 9999)" -Description "restart recovery" -RootActivity $root -Variables @( (New-VariableDef -Key "Msg") ) }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$wfId = $run.workflowExecutionId
Start-Sleep -Milliseconds 800
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
Show-WorkflowInstance -Instance $inst
$setBefore = Get-NodeRunCount -Instance $inst -NodeId 'set'
if (-not ($setBefore -ge 1 -and (Test-NodeSuspended -Instance $inst) -and (Get-NodeRunCount -Instance $inst -NodeId 'echo') -eq 0)) {
    Write-Host "FAIL (suspend) - expected Set-ran, Event-suspended, SetOutput-not-run before restart." -ForegroundColor Red; exit 1
}
Write-Host "suspended at the Event with the variable set (set ran $setBefore x)."

# --- kill + relaunch the server ---
if ($RestartServer) {
    Write-Host "`n--- restarting the server process ---" -ForegroundColor Cyan
    Restart-ElsaServer -Port $Port
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password   # fresh session after restart
    Write-Host "--- server back; re-authenticated ---`n" -ForegroundColor Cyan
} else {
    Write-Host "(skipping restart: -RestartServer:`$false)" -ForegroundColor Yellow
}

# --- after restart: instance must still be suspended with identical state ---
$inst = Get-WorkflowInstance -Ctx $ctx -ExecutionId $wfId
Show-WorkflowInstance -Instance $inst
$setAfter = Get-NodeRunCount -Instance $inst -NodeId 'set'
$stillSuspended = Test-NodeSuspended -Instance $inst
$echoStillNotRun = (Get-NodeRunCount -Instance $inst -NodeId 'echo') -eq 0
if (-not ($stillSuspended -and $setAfter -eq $setBefore -and $echoStillNotRun)) {
    Write-Host ("FAIL (recovery) - after restart: suspended={0}, set-count {1}->{2} (want no dup), echo-not-run={3}" -f $stillSuspended, $setBefore, $setAfter, $echoStillNotRun) -ForegroundColor Red
    exit 1
}
Write-Host "recovered: still suspended, Set ran exactly once (no duplicate replay), SetOutput not run."

# --- resume + assert the variable survived ---
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
if ($completed -and $echoed -eq $token -and (Get-NodeRunCount -Instance $inst -NodeId 'set') -eq $setBefore) {
    Write-Host ("SUCCESS - suspended instance survived the restart and resumed to completion; variable intact ('{0}'), no duplicate effects." -f $echoed) -ForegroundColor Green
} else {
    Write-Host ("FAIL (resume) - status '{0}', echoed '{1}' (expected '{2}'), set-count={3}" -f $inst.instance.status, $echoed, $token, (Get-NodeRunCount -Instance $inst -NodeId 'set')) -ForegroundColor Red
    exit 1
}
