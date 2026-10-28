<# Mechanical failure controls for the runner's actual finally block; no host recovery qualification. #>
param([string] $RunnerPath = (Join-Path $PSScriptRoot 'Test-BpmnBroadcastRecovery.ps1'))
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($RunnerPath, [ref] $tokens, [ref] $errors)
if ($errors.Count -gt 0) { throw 'The recovery runner has parse errors.' }
$statement = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.TryStatementAst] -and
    $node.Finally -and $node.Finally.Extent.Text.Contains('$receipt.capturedAtUtc') }, $true)
if ($statement.Count -ne 1) { throw 'Expected the actual recovery cleanup block.' }
$cleanupBlock = [scriptblock]::Create($statement[0].Finally.Extent.Text.Trim().Substring(1).TrimEnd().TrimEnd('}'))
$root = Join-Path ([IO.Path]::GetTempPath()) ('elsa-bpmn-cleanup-control-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
try {
    foreach ($case in @('peer-stop-failure', 'observer-stop-failure', 'already-failed-broker')) {
        $receipt = @{ passed = $true; failures = @() }
        $EvidencePath = Join-Path $root "$case.json"
        $oldBrokerStop = Join-Path $root "$case.stop"
        $client = $null; $watch = $null
        $oldBroker = [pscustomobject]@{ HasExited = ($case -eq 'already-failed-broker'); ExitCode = $(if ($case -eq 'already-failed-broker') { 17 } else { 0 }); Waited = $false }
        $oldBroker | Add-Member ScriptMethod WaitForExit { param($timeout) $this.Waited = $true; $this.HasExited = $true; return $true }
        function Stop-OwnedElsaServer { if ($case -eq 'peer-stop-failure') { throw 'Injected peer cleanup failure.' } }
        if ($case -eq 'observer-stop-failure') {
            $watch = [pscustomobject]@{ HasExited = $false }
            $watch | Add-Member ScriptMethod Kill { throw 'Injected observer cleanup failure.' }
        }
        $failed = $false
        try { . $cleanupBlock } catch { $failed = $true }
        if (-not $failed -or -not (Test-Path $EvidencePath)) { throw "Missing cleanup failure receipt for $case." }
        $saved = Get-Content -Raw $EvidencePath | ConvertFrom-Json
        if ($saved.passed -or $saved.failures.Count -ne 1) { throw "Incorrect cleanup failure evidence for $case." }
        if ($case -ne 'already-failed-broker' -and (-not $oldBroker.Waited -or -not (Test-Path $oldBrokerStop))) { throw "Other cleanup prevented owned probe cleanup for $case." }
        Write-Host "PASS $case"
    }
} finally { Remove-Item -Recurse -Force $root }
