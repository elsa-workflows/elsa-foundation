<#
.SYNOPSIS
    Author and continue one real noninterrupting call pair for owned acknowledgment-loss cuts.
.DESCRIPTION
    Only normal public APIs are used. The caller owns probe/standard process lifecycle and frozen-store observations.
    Emit and FinishChild may lose their HTTP response when the caller deliberately kills its owned cut process;
    committed acceptance is proved independently from API and checked snapshots, not inferred from that response.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseUrl,
    [Parameter(Mandatory)][string] $EvidencePath,
    [Parameter(Mandatory)][string] $SourceRevision,
    [Parameter(Mandatory)][ValidateSet('Prepare', 'Emit', 'FinishChild', 'FinishWarning', 'Observe')][string] $Phase,
    [string] $PreparedEvidencePath,
    [ValidateSet('Prepared', 'NoticeAccepted', 'TerminalAccepted', 'Finished')][string] $ExpectedStage = 'Prepared',
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)
. "$PSScriptRoot/_CalledControlCommon.ps1"

function Assert-AckStage($Case, $Parent, $Child, [string] $Stage) {
    $calls = @($Parent.activities | Where-Object executableNodeId -eq 'node-call')
    $warnings = @($Parent.activities | Where-Object executableNodeId -eq 'node-warning')
    $normals = @($Parent.activities | Where-Object executableNodeId -eq 'node-normal')
    $terminal = $Stage -in @('TerminalAccepted', 'Finished')
    if ($Parent.instance.status -cne $(if ($Stage -ceq 'Finished') { 'Completed' } else { 'Running' }) -or
        $Child.instance.status -cne $(if ($terminal) { 'Completed' } else { 'Running' }) -or $calls.Count -ne 1 -or
        $calls[0].status -cne $(if ($terminal) { 'Completed' } else { 'Suspended' }) -or
        $warnings.Count -ne $(if ($Stage -ceq 'Prepared') { 0 } else { 1 }) -or $normals.Count -ne $(if ($terminal) { 1 } else { 0 }) -or
        @($Parent.incidents).Count -ne 0 -or @($Child.incidents).Count -ne 0 -or
        $Parent.instance.artifactId -cne $Case.parentPublication.artifactId -or $Child.instance.artifactId -cne $Case.childPublication.artifactId -or
        $Child.instance.parentWorkflowExecutionId -cne $Case.parentWorkflowExecutionId) { throw 'The actual call pair does not match the expected acknowledgment-cut stage.' }
    if ($warnings.Count -eq 1 -and $warnings[0].status -cne $(if ($Stage -ceq 'Finished') { 'Completed' } else { 'Suspended' })) { throw 'The original warning Event did not retain its expected live/finished status.' }
    if ($terminal -and ($normals[0].status -cne 'Completed' -or 'Completed' -notin @($calls[0].outcomeNames))) { throw 'The actual terminal child lost its truthful ordinary call completion.' }
    if ($Stage -ceq 'Prepared' -and -not (Test-NodeSuspended -Instance $Child -NodeId 'node-child-emit')) { throw 'The actual child is not at its original emit Event.' }
    if ($Stage -ceq 'NoticeAccepted' -and -not (Test-NodeSuspended -Instance $Child -NodeId 'node-child-finish')) { throw 'The actual nonterminal child is not at its original finish Event.' }
}

$receipt = @{ schemaVersion = 1; phase = $Phase; expectedStage = $ExpectedStage; sourceRevision = $SourceRevision; passed = $false; cases = @(); failures = @() }
try {
    if ((Invoke-WebRequest "$BaseUrl/health/ready" -TimeoutSec 10).StatusCode -ne 200) { throw 'The supplied owned host is not ready.' }
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    if ($Phase -ceq 'Prepare') {
        $bpmn = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Bpmn.Activities.BpmnProcess'
        $dispatch = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow'
        $event = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event'
        $write = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
        $receipt.catalogVersions = @{ bpmn = $bpmn; dispatch = $dispatch; event = $event; write = $write }
        $suffix = [guid]::NewGuid().ToString('N'); $emit = "ack-emit-$suffix"; $finish = "ack-finish-$suffix"; $warn = "ack-warning-$suffix"
        $childRoot = New-ActivityNode -NodeId 'child-bpmn' -VersionId $bpmn -Structure (New-ControlStructure @(
            (New-ControlWait 'node-child-emit' $event $emit), (New-ControlWait 'node-child-finish' $event $finish)
        ) @((New-ControlElement 'start' 'startEvent'), (New-ControlElement 'emit' 'task' 'node-child-emit'),
            (New-ControlElement 'notice' 'intermediateThrowEvent' '' @(@{ type = 'escalation'; properties = @{ code = 'WARN' } })),
            (New-ControlElement 'finish' 'task' 'node-child-finish'), (New-ControlElement 'end' 'endEvent')) @(
            (New-ControlFlow 'begin' 'start' 'emit'), (New-ControlFlow 'raise' 'emit' 'notice'), (New-ControlFlow 'park' 'notice' 'finish'), (New-ControlFlow 'end' 'finish' 'end')))
        $childDefinition = Submit-Workflow -Ctx $ctx -Name "AckCalledChild-$suffix" -RootActivity $childRoot
        $childPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $childDefinition.version.id
        $call = New-ActivityNode -NodeId 'node-call' -VersionId $dispatch -Inputs @(
            (New-LiteralInput -ReferenceKey 'WorkflowDefinitionId' -Value $childDefinition.definition.id), (New-LiteralInput -ReferenceKey 'WaitForCompletion' -Value $true))
        $warning = New-ControlWait 'node-warning' $event $warn
        $normal = New-ActivityNode -NodeId 'node-normal' -VersionId $write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'Real child result committed before source acknowledgment.'))
        $boundary = @{ elementId = 'boundary'; elementType = 'boundaryEvent'; attachedToRef = 'call'; cancelActivity = $false;
            eventDefinitions = @(@{ type = 'escalation'; properties = @{ code = 'WARN' } }) }
        $parentRoot = New-ActivityNode -NodeId 'parent-bpmn' -VersionId $bpmn -Structure (New-ControlStructure @($call, $warning, $normal) @(
            (New-ControlElement 'start' 'startEvent'), (New-ControlElement 'call' 'callActivity' 'node-call'), $boundary,
            (New-ControlElement 'normal' 'task' 'node-normal'), (New-ControlElement 'normal-end' 'endEvent'),
            (New-ControlElement 'warning' 'task' 'node-warning'), (New-ControlElement 'warning-end' 'endEvent')
        ) @((New-ControlFlow 'begin' 'start' 'call'), (New-ControlFlow 'normal' 'call' 'normal'), (New-ControlFlow 'normal-end' 'normal' 'normal-end'),
            (New-ControlFlow 'warn' 'boundary' 'warning'), (New-ControlFlow 'warn-end' 'warning' 'warning-end')))
        $parentDefinition = Submit-Workflow -Ctx $ctx -Name "AckCalledParent-$suffix" -RootActivity $parentRoot
        $parentPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDefinition.version.id
        $run = Invoke-Artifact -Ctx $ctx -ArtifactId $parentPublication.artifactId -SourceReferenceId $parentPublication.sourceReferenceId
        $child = Wait-ControlChild $ctx $childPublication.artifactId $run.workflowExecutionId
        $child = Wait-ControlNode $ctx $child.instance.workflowExecutionId 'node-child-emit' 'Suspended'
        $parent = Wait-ControlNode $ctx $run.workflowExecutionId 'node-call' 'Suspended'
        $case = @{ parentWorkflowExecutionId = $run.workflowExecutionId; childWorkflowExecutionId = $child.instance.workflowExecutionId;
            emitEventName = $emit; finishEventName = $finish; warningEventName = $warn; parentPublication = $parentPublication; childPublication = $childPublication }
        $receipt.expectedStage = 'Prepared'
    } else {
        if (-not $PreparedEvidencePath) { throw 'The actual original prepared receipt is required.' }
        $prepared = Get-Content -LiteralPath $PreparedEvidencePath -Raw | ConvertFrom-Json -AsHashtable
        if (-not $prepared.passed -or $prepared.phase -cne 'Prepare' -or $prepared.cases.Count -ne 1) { throw 'One actual prepared call pair is required.' }
        $case = $prepared.cases[0]
        if ($Phase -cne 'Observe') {
            $eventName = switch ($Phase) { 'Emit' { $case.emitEventName }; 'FinishChild' { $case.finishEventName }; 'FinishWarning' { $case.warningEventName } }
            $target = if ($Phase -ceq 'FinishWarning') { $case.parentWorkflowExecutionId } else { $case.childWorkflowExecutionId }
            $case.delivery = Send-ControlEvent $ctx $eventName $target
        }
        $deadline = (Get-Date).AddSeconds(60)
        do {
            $parent = Get-WorkflowInstance $ctx $case.parentWorkflowExecutionId
            $child = Get-WorkflowInstance $ctx $case.childWorkflowExecutionId
            try { Assert-AckStage $case $parent $child $ExpectedStage; break } catch { if ((Get-Date) -ge $deadline) { throw }; Start-Sleep -Milliseconds 100 }
        } while ($true)
    }
    Assert-AckStage $case $parent $child $receipt.expectedStage
    $case.parent = $parent; $case.child = $child; $receipt.cases += $case; $receipt.passed = $true
} catch { $receipt.failures += $_.Exception.Message; throw }
finally { $receipt | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $EvidencePath }
