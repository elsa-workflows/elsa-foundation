<#
.SYNOPSIS
    The owned-server lifecycle never stops a process it did not start (#2329).
.DESCRIPTION
    Needs no Elsa server and no build. Starts a bystander process that listens on a free loopback port, then
    drives ../_ServerLifecycle.ps1 at that port the way a restart-style script would:
      1. choosing that address and starting an owned server there are both refused, and the message names the
         port and the bystander's pid;
      2. restart, stop and cleanup without an owned server leave the bystander running and listening;
      3. the port derives from the base URL, and external mode refuses to restart.
    The bystander is this script's own child and is the only process it stops.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/_ServerLifecycle.ps1"

$script:Pass = 0; $script:Total = 0
function Assert-That {
    param([Parameter(Mandatory)][string] $Label, [Parameter(Mandatory)][bool] $Condition, [string] $Detail = '')
    $script:Total++
    if ($Condition) { $script:Pass++; Write-Host "  OK   $Label" -ForegroundColor Green }
    else { Write-Host "  FAIL $Label  $Detail" -ForegroundColor Red }
}

# The message of the error a script block throws, or $null when it does not throw.
function Get-ThrownMessage {
    param([Parameter(Mandatory)][scriptblock] $Action)
    try { & $Action | Out-Null; return $null } catch { return $_.Exception.Message }
}

Write-Host '== Owned-server lifecycle guard ==' -ForegroundColor Cyan
$port = Get-FreeLoopbackPort
$baseUrl = "http://127.0.0.1:$port"
$listen = "`$l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port); `$l.Start(); Start-Sleep -Seconds 600"
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($listen))
$bystander = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile', '-EncodedCommand', $encoded) -PassThru

function Test-BystanderListening { (-not $bystander.HasExited) -and ($bystander.Id -in @(Get-PortListenerPid -Port $port)) }

try {
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-BystanderListening) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    if (-not (Test-BystanderListening)) { throw "The bystander (pid $($bystander.Id)) did not start listening on port $port." }
    Write-Host "  [bystander] pid $($bystander.Id) listening on $port"

    $neverCreated = Join-Path ([IO.Path]::GetTempPath()) "elsa-guard-$([guid]::NewGuid().ToString('N'))"
    foreach ($attempt in @(
            @{ Name = 'choosing an occupied port'; Action = { Resolve-ElsaServerBaseUrl -BaseUrl $baseUrl } },
            @{ Name = 'starting on an occupied port'; Action = { Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $neverCreated } })) {
        $refusal = Get-ThrownMessage $attempt.Action
        Assert-That "$($attempt.Name) is refused, naming the port and the pid" `
            ("$refusal" -match "Port $port\b" -and "$refusal" -match "pid $($bystander.Id)\b") "message: $refusal"
    }
    Assert-That 'the refusals left the bystander listening' (Test-BystanderListening)

    $restart = Get-ThrownMessage { Restart-OwnedElsaServer }
    Assert-That 'restart without an owned server is refused' ("$restart" -match 'has not started a server') "message: $restart"
    Stop-OwnedElsaServer
    Remove-OwnedElsaServer
    Assert-That 'restart, stop and cleanup left the bystander listening' (Test-BystanderListening)

    Assert-That 'the port derives from the base URL' ((Get-ElsaServerPort -BaseUrl 'http://localhost:5295/') -eq 5295)
    Assert-That 'an owned server without -BaseUrl gets a free port, not 5095' ((Get-ElsaServerPort -BaseUrl (Resolve-ElsaServerBaseUrl)) -ne 5095)
    $external = Get-ThrownMessage { Resolve-ElsaServerBaseUrl -BaseUrl $baseUrl -UseExternalServer -RestartServer }
    Assert-That 'external mode refuses to restart' ("$external" -match 'does not own') "message: $external"
}
finally {
    if (-not $bystander.HasExited) { $bystander.Kill() }
}

Write-Host ''
$color = if ($script:Pass -eq $script:Total) { 'Green' } else { 'Red' }
Write-Host "== lifecycle guard: $($script:Pass)/$($script:Total) passed ==" -ForegroundColor $color
if ($script:Pass -ne $script:Total) { exit 1 }
