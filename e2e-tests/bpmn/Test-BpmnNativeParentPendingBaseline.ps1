<#
.SYNOPSIS
    Retain actual BPMN fault/incident/native-parent callback checkpoints before parent consumption.
.DESCRIPTION
    The test-only probe holds an actual EF transaction-committed return. Balanced
    folding is observed explicitly; no pending callback is manufactured when folded.
#>
[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Capture')][string] $ProbeAssembly,
    [Parameter(Mandatory, ParameterSetName = 'Capture')][string] $OutputDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Capture')][string] $SourceRevision,
    [Parameter(Mandatory, ParameterSetName = 'Emit')][switch] $Emit,
    [Parameter(Mandatory, ParameterSetName = 'Emit')][string] $BaseUrl,
    [Parameter(Mandatory, ParameterSetName = 'Emit')][string] $EventName,
    [Parameter(Mandatory, ParameterSetName = 'Emit')][string] $ExecutionId
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/_CalledControlCommon.ps1"
if ($Emit) {
    $ctx = Connect-Elsa -BaseUrl $BaseUrl
    $null = Send-ControlEvent $ctx $EventName $ExecutionId
    return
}
. "$PSScriptRoot/../_ServerLifecycle.ps1"
if (Test-Path $OutputDirectory) { throw 'A new capture directory is required.' }
$null = New-Item -ItemType Directory $OutputDirectory
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$receipt = @{ sourceRevision = $SourceRevision; passed = $false; cases = @(); failures = @(); upgradedHostRecoveryProved = $false }
$request = $null
try {
    foreach ($mode in @('Durable', 'Balanced')) {
        foreach ($kind in @('Sequence', 'Flowchart', 'CompensationScope')) {
            $root = New-ElsaContentRoot -Prefix 'elsa-bpmn-native-parent-baseline'
            $base = Resolve-ElsaServerBaseUrl
            $control = Join-Path $root 'recovery-control'; $null = New-Item -ItemType Directory $control
            $settingsPath = Join-Path $root 'shells.json'
            $settings = Get-Content -Raw $settingsPath | ConvertFrom-Json -AsHashtable
            $settings.CShells.Shells.default.Features.BpmnRecoveryProbe = @{ ControlDirectory = $control }
            foreach ($feature in @('IdentityIamEntityFrameworkCore', 'IdentityProviderConfigurationEntityFrameworkCore', 'FoundationIdentityAspNetCoreIdentityEntityFrameworkCore', 'SecretsEntityFrameworkCore')) {
                $settings.CShells.Shells.default.Features[$feature]['ConnectionString'] = 'Data Source=private-identity.db'
            }
            $settings | ConvertTo-Json -Depth 60 | Set-Content $settingsPath
            Start-OwnedElsaServer -BaseUrl $base -ContentRoot $root -AssemblyPath $ProbeAssembly
            $ctx = Connect-Elsa -BaseUrl $base
            $bpmn = Get-ActivityVersionId $ctx 'Elsa.Activities.Bpmn.Activities.BpmnProcess'
            $event = Get-ActivityVersionId $ctx 'Elsa.Activities.Primitives.Activities.Event'
            $write = Get-ActivityVersionId $ctx 'Elsa.Activities.Primitives.Activities.WriteLine'
            $type = if ($kind -eq 'CompensationScope') { 'Elsa.Activities.Compensation.Activities.CompensationScope' } else { "Elsa.Activities.$kind.Activities.$kind" }
            $parentVersion = Get-ActivityVersionId $ctx $type
            $name = "native-parent-$([guid]::NewGuid().ToString('N'))"
            $process = New-ActivityNode 'legacy-bpmn' $bpmn -Structure (New-ControlStructure @((New-ControlWait 'node-held' $event $name)) @(
                (New-ControlElement 'start' 'startEvent'), (New-ControlElement 'held' 'task' 'node-held'),
                (New-ControlElement 'end' 'endEvent' '' @(@{ type = 'error'; properties = @{ code = 'NATIVE-PENDING-BASELINE' } }))) @(
                (New-ControlFlow 'begin' 'start' 'held'), (New-ControlFlow 'finish' 'held' 'end')))
            $after = New-ActivityNode 'after-bpmn' $write -Inputs @((New-LiteralInput 'text' 'Native parent continued after the real BPMN completion.'))
            $structure = if ($kind -eq 'Flowchart') { @{ kind = 'elsa.flowchart.structure'; schemaVersion = '1.0.0'; payload = @{
                startNodeId = 'legacy-bpmn'; activities = @($process, $after); nodeMetadata = @{}; connectionMetadata = @{};
                connections = @(@{ id = 'after'; source = @{ nodeId = 'legacy-bpmn'; port = 'Done' }; target = @{ nodeId = 'after-bpmn' } })
            } } } elseif ($kind -eq 'CompensationScope') { @{ kind = 'elsa.compensation-scope.structure'; schemaVersion = '1.0.0'; payload = @{
                steps = @(@{ name = 'bpmn'; activity = $process }, @{ name = 'after'; activity = $after })
            } } }
            else { New-SequenceStructure -Activities @($process, $after) }
            $definition = Submit-Workflow $ctx "$name-$mode-$kind" -RootActivity (New-ActivityNode 'native-parent' $parentVersion -Structure $structure) -StrategyOptions @{ persistenceMode = $mode }
            $publication = Publish-WorkflowVersion $ctx $definition.version.id
            $run = Invoke-Artifact $ctx $publication.artifactId $publication.sourceReferenceId
            $before = Wait-ControlNode $ctx $run.workflowExecutionId 'node-held' 'Suspended'
            $caseId = "$($kind.ToLowerInvariant())-$($mode.ToLowerInvariant())"
            $childId = @($before.activities | Where-Object executableNodeId -ceq 'legacy-bpmn')[0].activityExecutionId
            @{ caseId = $caseId; workflowExecutionId = $run.workflowExecutionId; childNodeId = 'legacy-bpmn'; childActivityExecutionId = $childId; scope = 'default' } |
                ConvertTo-Json | Set-Content (Join-Path $control 'native-parent-plan.json')
            $info = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
            $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
            foreach ($arg in @('-NoProfile', '-File', $PSCommandPath, '-Emit', '-BaseUrl', $base, '-EventName', $name, '-ExecutionId', $run.workflowExecutionId)) { $info.ArgumentList.Add($arg) }
            $request = [Diagnostics.Process]::Start($info)
            $stdout = $request.StandardOutput.ReadToEndAsync(); $stderr = $request.StandardError.ReadToEndAsync()
            $marker = Join-Path $control "$caseId/reached.json"; $deadline = (Get-Date).AddSeconds(90)
            while (-not (Test-Path $marker) -and -not $request.HasExited) {
                if ((Get-Date) -ge $deadline) { throw 'The actual parent completion cut did not return or reach its physical marker.' }
                Start-Sleep -Milliseconds 100
            }
            if ($request.HasExited -and $request.ExitCode -ne 0) { throw "Normal Event command failed: $($stderr.GetAwaiter().GetResult())" }
            if ($mode -eq 'Durable' -and -not (Test-Path $marker)) { throw 'Durable completion did not reach the actual physical pending checkpoint.' }
            $view = Get-WorkflowInstance $ctx $run.workflowExecutionId
            $child = @($view.activities | Where-Object executableNodeId -ceq 'legacy-bpmn')
            $parent = @($view.activities | Where-Object executableNodeId -ceq 'native-parent')
            if ($child.Count -ne 1 -or $child[0].status -cne 'Faulted') { throw 'The actual faulted BPMN activity was not persisted.' }
            $pending = $parent.Count -eq 1 -and $parent[0].status -eq 'Running' -and @($view.activities | Where-Object executableNodeId -ceq 'after-bpmn').Count -eq 0
            if (-not $pending) { throw 'The native parent already consumed the promised fault callback cut.' }
            if (Test-Path $marker) { Copy-Item $marker (Join-Path $OutputDirectory "$caseId-marker.json") }
            Stop-OwnedElsaServer
            if (-not $request.WaitForExit(10000)) { $request.Kill(); $request.WaitForExit() }
            $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $root 'command.out.log')
            $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $root 'command.err.log'); $request = $null
            $capture = & python3 "$PSScriptRoot/capture_closed_hosting_store.py" --database (Join-Path $root 'elsa.db') --output (Join-Path $OutputDirectory "$caseId.sqlite") | ConvertFrom-Json -AsHashtable
            if ($LASTEXITCODE -ne 0) { throw 'The actual whole-store capture refused.' }
            $case = @{ parent = $kind; persistenceMode = $mode; sourceRevision = $SourceRevision; workflowExecutionId = $run.workflowExecutionId;
                publication = $publication; before = $before; after = $view; pendingParentConsumption = $pending;
                actualPhysicalCommitMarkerObserved = (Test-Path $marker); capture = $capture }
            $receipt.cases += $case
            $case | ConvertTo-Json -Depth 80 | Set-Content (Join-Path $OutputDirectory "$caseId-api.json")
        }
    }
    $receipt.passed = $true
} catch { $receipt.failures += $_.Exception.Message; throw }
finally {
    Stop-OwnedElsaServer
    if ($request -and -not $request.HasExited) { $request.Kill(); $request.WaitForExit() }
    $receipt | ConvertTo-Json -Depth 80 | Set-Content (Join-Path $OutputDirectory 'manifest.json')
}
