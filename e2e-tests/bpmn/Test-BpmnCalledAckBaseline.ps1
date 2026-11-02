<#
.SYNOPSIS
    Retain two actual called-control pre-acknowledgment stores before writer changes.
.DESCRIPTION
    Uses normal authoring/commands, the existing test-only cut and checked provider reader.
    A standard-host restart between cuts is a baseline control, not upgraded-host evidence.
    Authentication is stored separately from the whole execution/design store from startup.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ProbeAssembly,
    [Parameter(Mandatory)][string] $NormalAssembly,
    [Parameter(Mandatory)][string] $ReaderAssembly,
    [Parameter(Mandatory)][string] $SourceRevision,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $Python = 'python3'
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../_ServerLifecycle.ps1"
if (Test-Path $OutputDirectory) { throw 'A new capture output directory is required.' }
$null = New-Item -ItemType Directory $OutputDirectory
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$sourceCommit = (& git -C "$PSScriptRoot/../.." rev-parse HEAD).Trim()
if ($SourceRevision -cne $sourceCommit) { throw 'Name the actual current full source commit.' }
foreach ($entry in @(@{ path = $ProbeAssembly; probe = $true }, @{ path = $NormalAssembly; probe = $false })) {
    $value = & dotnet $ReaderAssembly --inspect-host $entry.path | ConvertFrom-Json -AsHashtable
    if ($LASTEXITCODE -ne 0 -or $value.containsRecoveryProbe -ne $entry.probe) { throw 'Probe and ordinary host binaries are not distinct.' }
}
$root = New-ElsaContentRoot -Prefix 'elsa-bpmn-called-ack-baseline'
$base = Resolve-ElsaServerBaseUrl
$control = Join-Path $root 'recovery-control'
$null = New-Item -ItemType Directory $control
$settingsPath = Join-Path $root 'shells.json'
$settings = Get-Content -Raw $settingsPath | ConvertFrom-Json -AsHashtable
$settings.CShells.Shells.default.Features.BpmnRecoveryProbe = @{ ControlDirectory = $control }
$settings.CShells.Shells.default.Features.WorkflowsRuntimeResumption = @{ SweepIntervalSeconds = 1 }
foreach ($feature in @('IdentityIamEntityFrameworkCore', 'IdentityProviderConfigurationEntityFrameworkCore', 'FoundationIdentityAspNetCoreIdentityEntityFrameworkCore', 'SecretsEntityFrameworkCore')) {
    $settings.CShells.Shells.default.Features[$feature]['ConnectionString'] = 'Data Source=private-identity.db'
}
$settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
$receipt = @{ sourceRevision = $SourceRevision; passed = $false; captures = @(); failures = @(); upgradedHostRecoveryProved = $false }
$request = $null
function Invoke-Phase([string] $Phase, [string] $Name, [string] $Stage = 'Prepared') {
    $arguments = @{ BaseUrl = $base; EvidencePath = (Join-Path $OutputDirectory "$Name.json"); SourceRevision = $SourceRevision;
        Phase = $Phase; ExpectedStage = $Stage }
    if ($Phase -ne 'Prepare') { $arguments.PreparedEvidencePath = Join-Path $OutputDirectory 'prepared.json' }
    & "$PSScriptRoot/Test-BpmnCalledAckHost.ps1" @arguments
}
try {
    Start-OwnedElsaServer -BaseUrl $base -ContentRoot $root -AssemblyPath $ProbeAssembly
    Invoke-Phase 'Prepare' 'prepared'
    $prepared = Get-Content -Raw (Join-Path $OutputDirectory 'prepared.json') | ConvertFrom-Json -AsHashtable
    foreach ($cut in @(@{ name = 'notice'; kind = 'Elsa.Activities.DispatchWorkflow.NotifyControl'; ordinal = 0; phase = 'Emit'; stage = 'NoticeAccepted' },
                       @{ name = 'terminal'; kind = 'Elsa.Activities.DispatchWorkflow.ResumeParent'; ordinal = 1; phase = 'FinishChild'; stage = 'TerminalAccepted' })) {
        $caseId = "$($cut.name)-$([guid]::NewGuid().ToString('N'))"
        @{ caseId = $caseId; intentKind = $cut.kind; childWorkflowExecutionId = $prepared.cases[0].childWorkflowExecutionId; ordinal = $cut.ordinal } |
            ConvertTo-Json | Set-Content (Join-Path $control 'called-control-plan.json')
        $info = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        foreach ($arg in @('-NoProfile', '-File', "$PSScriptRoot/Test-BpmnCalledAckHost.ps1", '-BaseUrl', $base,
                '-EvidencePath', (Join-Path $OutputDirectory "$($cut.name)-command.json"), '-SourceRevision', $SourceRevision,
                '-Phase', $cut.phase, '-ExpectedStage', $cut.stage, '-PreparedEvidencePath', (Join-Path $OutputDirectory 'prepared.json'))) { $info.ArgumentList.Add($arg) }
        $request = [Diagnostics.Process]::Start($info)
        $stdout = $request.StandardOutput.ReadToEndAsync(); $stderr = $request.StandardError.ReadToEndAsync()
        $markerPath = Join-Path $control "$caseId/reached.json"
        $deadline = (Get-Date).AddSeconds(90)
        while (-not (Test-Path $markerPath)) {
            if ($request.HasExited -and $request.ExitCode -ne 0) { throw "Normal command failed before the actual cut: $($stderr.GetAwaiter().GetResult())" }
            if ((Get-Date) -ge $deadline) { throw 'The actual called-control pre-acknowledgment marker was not reached.' }
            Start-Sleep -Milliseconds 100
        }
        Copy-Item $markerPath (Join-Path $OutputDirectory "$($cut.name)-marker.json")
        Invoke-Phase 'Observe' "$($cut.name)-held-api" $cut.stage
        Stop-OwnedElsaServer
        if (-not $request.WaitForExit(10000)) { $request.Kill(); $request.WaitForExit() }
        $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $root "$($cut.name)-command.out.log")
        $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $root "$($cut.name)-command.err.log")
        $request = $null
        $database = Join-Path $OutputDirectory "$($cut.name)-held.sqlite"
        $capture = & $Python "$PSScriptRoot/capture_closed_hosting_store.py" --database (Join-Path $root 'elsa.db') --output $database | ConvertFrom-Json -AsHashtable
        if ($LASTEXITCODE -ne 0) { throw 'Whole-store capture refused.' }
        & dotnet $ReaderAssembly --inspect-called-ack $database $capture.sha256 default (Join-Path $OutputDirectory "$($cut.name)-held-api.json") (Join-Path $OutputDirectory "$($cut.name)-marker.json") held |
            Set-Content (Join-Path $OutputDirectory "$($cut.name)-held-checked.json")
        if ($LASTEXITCODE -ne 0) { throw 'The actual provider refused the retained pre-acknowledgment input.' }
        $capture.name = $cut.name; $capture.expectedStage = $cut.stage; $receipt.captures += $capture
        # Remove the cut feature for the unmodified ordinary-host baseline restart.
        $settings.CShells.Shells.default.Features.Remove('BpmnRecoveryProbe')
        $settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
        Start-OwnedElsaServer -BaseUrl $base -ContentRoot $root -AssemblyPath $NormalAssembly
        Invoke-Phase 'Observe' "$($cut.name)-ordinary-restart-api" $cut.stage
        if ($cut.name -eq 'notice') {
            # Wait for normal lease expiry/acknowledgment, using checked copies without changing time/state.
            $deadline = (Get-Date).AddSeconds(180)
            do {
                $candidate = Join-Path $root 'ordinary-recovery-check.sqlite'
                Remove-Item $candidate -ErrorAction SilentlyContinue
                $observed = & $Python "$PSScriptRoot/capture_closed_hosting_store.py" --database (Join-Path $root 'elsa.db') --output $candidate | ConvertFrom-Json -AsHashtable
                if ($LASTEXITCODE -ne 0) { throw 'Recovery observation refused.' }
                $checked = & dotnet $ReaderAssembly --inspect-called-ack $candidate $observed.sha256 default (Join-Path $OutputDirectory 'notice-ordinary-restart-api.json') (Join-Path $OutputDirectory 'notice-marker.json') recovered
                if ($LASTEXITCODE -eq 0) { $checked | Set-Content (Join-Path $OutputDirectory 'notice-ordinary-restart-checked.json'); break }
                if ((Get-Date) -ge $deadline) { throw 'Ordinary host did not acknowledge the original source after natural expiry.' }
                Start-Sleep -Seconds 2
            } while ($true)
            Stop-OwnedElsaServer
            $settings.CShells.Shells.default.Features.BpmnRecoveryProbe = @{ ControlDirectory = $control }
            $settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
            Start-OwnedElsaServer -BaseUrl $base -ContentRoot $root -AssemblyPath $ProbeAssembly
        }
    }
    $receipt.passed = $true
} catch { $receipt.failures += $_.Exception.Message; throw }
finally {
    Stop-OwnedElsaServer
    if ($request -and -not $request.HasExited) { $request.Kill(); $request.WaitForExit() }
    $receipt | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $OutputDirectory 'manifest.json')
}
