<#
.SYNOPSIS
    Exercise opted-in called BPMN control through public authoring/publishing/execution APIs.
.DESCRIPTION
    Requires an explicitly owned source-built host. The caller manages lifecycle and checked closed-store
    snapshots; this script never edits runtime state or starts/stops a process.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseUrl,
    [Parameter(Mandatory)][string] $EvidencePath,
    [Parameter(Mandatory)][string] $SourceRevision,
    [ValidateSet('CodedErrors', 'PrepareEscalations', 'ObserveEscalations', 'FinishEscalations')][string] $Phase = 'CodedErrors',
    [string] $PreparedEvidencePath,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)
. "$PSScriptRoot/../stimuli/_StimuliCommon.ps1"

function New-ControlStructure([array] $Activities, [array] $Elements, [array] $Flows) {
    @{ kind = 'elsa.bpmn.structure'; schemaVersion = '1.0.0'; payload = @{ activities = $Activities; elements = $Elements; sequenceFlows = $Flows } }
}
function New-ControlElement([string] $Id, [string] $Type, [string] $ChildNodeId, [array] $Definitions = @()) {
    $element = @{ elementId = $Id; elementType = $Type }
    if ($ChildNodeId) { $element.childNodeId = $ChildNodeId }
    if ($Definitions.Count -gt 0) { $element.eventDefinitions = $Definitions }
    $element
}
function New-ControlFlow([string] $Id, [string] $Source, [string] $Target) {
    @{ flowId = $Id; sourceRef = $Source; targetRef = $Target }
}
function Wait-ControlChild($Ctx, [string] $ArtifactId, [string] $ParentId) {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $list = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances?artifactId=$ArtifactId" -WebSession $Ctx.Session
        $items = if ($list -is [array]) { $list } else { $list.items }
        $matches = @($items | Where-Object { $_.artifactId -ceq $ArtifactId -and $_.parentWorkflowExecutionId -ceq $ParentId })
        if ($matches.Count -eq 1) { return Get-WorkflowInstance -Ctx $Ctx -ExecutionId $matches[0].workflowExecutionId }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw 'The actual exact dispatched child did not appear.'
}

function New-ControlWait([string] $NodeId, [string] $Version, [string] $EventName) {
    $correlation = New-LiteralInput -ReferenceKey 'CorrelationId' -Value $null
    $correlation.value = @{ expressionType = 'JavaScript'; value = @{
        language = 'JavaScript'; source = 'args.instance'; capabilityProfile = 'binding-pure-v1';
        resultType = @{ alias = 'String'; collectionKind = 'single' };
        parameters = @{ instance = @{ kind = 'WorkflowInstance'; member = 'instanceId' } }; options = @{}
    } }
    New-ActivityNode -NodeId $NodeId -VersionId $Version -Inputs @(
        (New-LiteralInput -ReferenceKey 'EventName' -Value $EventName),
        (New-LiteralInput -ReferenceKey 'CanStartWorkflow' -Value $false), $correlation)
}
function Wait-ControlNode($Ctx, [string] $ExecutionId, [string] $NodeId, [string] $Status) {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $view = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $ExecutionId
        $nodes = @($view.activities | Where-Object { $_.executableNodeId -ceq $NodeId })
        if ($nodes.Count -eq 1 -and $nodes[0].status -ceq $Status) { return $view }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "The actual $NodeId did not reach $Status."
}
function Send-ControlEvent($Ctx, [string] $EventName, [string] $ExecutionId) {
    $delivery = Invoke-Stimulus -Ctx $Ctx -EventName $EventName -Mode 'ResumeOnly' -CorrelationId $ExecutionId -InputObject @{ eventName = $EventName }
    if ($delivery.startedCount -ne 0 -or $delivery.resumedCount -ne 1 -or
        @($delivery.resumes | Where-Object { $_.workflowExecutionId -ceq $ExecutionId }).Count -ne 1) {
        throw 'The actual public Event did not resume exactly its correlated workflow.'
    }
    $delivery
}
function Assert-ControlStage($Case, $Parent, $Child, [bool] $Finished) {
    $kind = $Case.kind; $interrupting = [bool]$Case.interrupting
    $failure = $kind -ceq 'SignalAndFailure'
    $terminal = $kind -cne 'RunningEscalation'
    $expectedChild = if ($failure) { 'Faulted' } elseif ($terminal -or ($Finished -and -not $interrupting)) { 'Completed' } elseif ($interrupting) { 'Cancelled' } else { 'Running' }
    $expectedParent = if ($failure -and -not $interrupting) { 'Faulted' } elseif ($Finished) { 'Completed' } else { 'Running' }
    $expectedCall = if ($terminal -or ($Finished -and -not $interrupting)) { 'Completed' } elseif ($interrupting) { 'Cancelled' } else { 'Suspended' }
    $calls = @($Parent.activities | Where-Object executableNodeId -eq 'node-call')
    $warnings = @($Parent.activities | Where-Object executableNodeId -eq 'node-warning')
    $normals = @($Parent.activities | Where-Object executableNodeId -eq 'node-normal')
    $expectedWarnings = if ($failure -and -not $interrupting) { 0 } else { 1 }
    $expectedNormals = if (-not $interrupting -and -not $failure -and ($terminal -or $Finished)) { 1 } else { 0 }
    if ($Parent.instance.status -cne $expectedParent -or $Child.instance.status -cne $expectedChild -or
        $calls.Count -ne 1 -or $calls[0].status -cne $expectedCall -or $warnings.Count -ne $expectedWarnings -or $normals.Count -ne $expectedNormals -or
        $Parent.instance.artifactId -cne $Case.parentPublication.artifactId -or $Child.instance.artifactId -cne $Case.childPublication.artifactId -or
        $Child.instance.parentWorkflowExecutionId -cne $Case.parentWorkflowExecutionId) { throw "Actual $kind/$interrupting stage differs from its exact expected workflow/call/branch truth." }
    if ($expectedWarnings -eq 1 -and $warnings[0].status -cne $(if ($Finished) { 'Completed' } else { 'Suspended' })) { throw 'The actual warning handler lost its expected single Event wait.' }
    if ($expectedNormals -eq 1 -and $normals[0].status -cne 'Completed') { throw 'The actual normal branch did not complete.' }
    if ($failure) {
        if (@($Child.incidents).Count -eq 0 -or (-not $interrupting -and @($Parent.incidents).Count -eq 0) -or
            ($interrupting -and @($Parent.incidents).Count -ne 0) -or 'Faulted' -notin @($calls[0].outcomeNames)) { throw 'Actual signal plus failure lost truthful faults or its terminal dispatch outcome.' }
    } elseif (@($Parent.incidents).Count -ne 0 -or @($Child.incidents).Count -ne 0) { throw 'An actual escalation acquired an incident.' }
}

$receipt = @{ schemaVersion = 1; phase = $Phase; sourceRevision = $SourceRevision; passed = $false; cases = @(); failures = @() }
try {
    if ((Invoke-WebRequest "$BaseUrl/health/ready" -TimeoutSec 10).StatusCode -ne 200) { throw 'The supplied owned host is not ready.' }
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $bpmn = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Bpmn.Activities.BpmnProcess'
    $dispatch = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow'
    $write = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    $receipt.catalogVersions = @{ bpmn = $bpmn; dispatch = $dispatch; write = $write }
    if ($Phase -ceq 'CodedErrors') {
    foreach ($code in @('ORDER', 'OTHER')) {
        $suffix = [guid]::NewGuid().ToString('N')
        $childRoot = New-ActivityNode -NodeId 'child-bpmn' -VersionId $bpmn -Structure (New-ControlStructure @() @(
            (New-ControlElement 'start' 'startEvent'),
            (New-ControlElement 'error' 'endEvent' '' @(@{ type = 'error'; properties = @{ code = $code } }))
        ) @((New-ControlFlow 'raise' 'start' 'error')))
        $childDefinition = Submit-Workflow -Ctx $ctx -Name "CalledErrorChild-$code-$suffix" -RootActivity $childRoot
        $childPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $childDefinition.version.id
        $call = New-ActivityNode -NodeId 'node-call' -VersionId $dispatch -Inputs @(
            (New-LiteralInput -ReferenceKey 'WorkflowDefinitionId' -Value $childDefinition.definition.id),
            (New-LiteralInput -ReferenceKey 'WaitForCompletion' -Value $true))
        $handler = New-ActivityNode -NodeId 'node-error-handler' -VersionId $write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'Actual authored ORDER boundary handled.'))
        $boundary = @{ elementId = 'boundary'; elementType = 'boundaryEvent'; attachedToRef = 'call'; cancelActivity = $true;
            eventDefinitions = @(@{ type = 'error'; properties = @{ code = 'ORDER' } }) }
        $parentRoot = New-ActivityNode -NodeId 'parent-bpmn' -VersionId $bpmn -Structure (New-ControlStructure @($call, $handler) @(
            (New-ControlElement 'start' 'startEvent'), (New-ControlElement 'call' 'callActivity' 'node-call'),
            (New-ControlElement 'normal-end' 'endEvent'), $boundary,
            (New-ControlElement 'handler' 'task' 'node-error-handler'), (New-ControlElement 'caught-end' 'endEvent')
        ) @((New-ControlFlow 'begin' 'start' 'call'), (New-ControlFlow 'normal' 'call' 'normal-end'),
            (New-ControlFlow 'catch' 'boundary' 'handler'), (New-ControlFlow 'caught' 'handler' 'caught-end')))
        $parentDefinition = Submit-Workflow -Ctx $ctx -Name "CalledErrorParent-$code-$suffix" -RootActivity $parentRoot
        $parentPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDefinition.version.id
        $run = Invoke-Artifact -Ctx $ctx -ArtifactId $parentPublication.artifactId -SourceReferenceId $parentPublication.sourceReferenceId
        $parent = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 60
        $child = Wait-ControlChild -Ctx $ctx -ArtifactId $childPublication.artifactId -ParentId $run.workflowExecutionId
        $child = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $child.instance.workflowExecutionId -TimeoutSeconds 60
        $case = @{ kind = 'coded-error'; authoredCode = $code; parentWorkflowExecutionId = $run.workflowExecutionId;
            childWorkflowExecutionId = $child.instance.workflowExecutionId; parentPublication = $parentPublication;
            childPublication = $childPublication; parent = $parent; child = $child }
        $receipt.cases += $case
        $expectedParent = if ($code -eq 'ORDER') { 'Completed' } else { 'Faulted' }
        $calls = @($parent.activities | Where-Object executableNodeId -eq 'node-call')
        $handlers = @($parent.activities | Where-Object executableNodeId -eq 'node-error-handler')
        if ($parent.instance.status -ne $expectedParent -or $child.instance.status -ne 'Faulted' -or
            $calls.Count -ne 1 -or $calls[0].status -ne 'Completed' -or 'Faulted' -notin @($calls[0].outcomeNames) -or
            @($child.incidents).Count -eq 0 -or $parent.instance.artifactId -cne $parentPublication.artifactId -or
            $child.instance.artifactId -cne $childPublication.artifactId -or $child.instance.parentWorkflowExecutionId -cne $run.workflowExecutionId) {
            throw "Actual called authored $code did not preserve its expected caller/child result."
        }
        if ($code -eq 'ORDER') {
            if ($handlers.Count -ne 1 -or $handlers[0].status -ne 'Completed' -or @($parent.incidents).Count -ne 0) { throw 'The actual matching ORDER boundary did not handle exactly once.' }
        } elseif ($handlers.Count -ne 0 -or @($parent.incidents).Count -eq 0) { throw 'The OTHER mismatch incorrectly reached the ORDER handler or lost its actual caller fault.' }
    }
    } elseif ($Phase -ceq 'PrepareEscalations') {
        $event = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event'
        $receipt.catalogVersions.event = $event
        foreach ($kind in @('RunningEscalation', 'TerminalEscalation', 'SignalAndFailure')) {
            foreach ($interrupting in @($false, $true)) {
                $suffix = [guid]::NewGuid().ToString('N')
                $emitName = "called-emit-$suffix"; $finishName = "called-finish-$suffix"; $warningName = "called-warning-$suffix"
                $notice = New-ControlElement 'notice' $(if ($kind -ceq 'RunningEscalation') { 'intermediateThrowEvent' } else { 'endEvent' }) '' @(@{ type = 'escalation'; properties = @{ code = 'WARN' } })
                if ($kind -ceq 'RunningEscalation') {
                    $childActivities = @((New-ControlWait 'node-child-emit' $event $emitName), (New-ControlWait 'node-child-finish' $event $finishName))
                    $childElements = @((New-ControlElement 'start' 'startEvent'), (New-ControlElement 'emit' 'task' 'node-child-emit'), $notice,
                        (New-ControlElement 'finish' 'task' 'node-child-finish'), (New-ControlElement 'end' 'endEvent'))
                    $childFlows = @((New-ControlFlow 'begin' 'start' 'emit'), (New-ControlFlow 'raise' 'emit' 'notice'), (New-ControlFlow 'park' 'notice' 'finish'), (New-ControlFlow 'end' 'finish' 'end'))
                } elseif ($kind -ceq 'TerminalEscalation') {
                    $childActivities = @(); $childElements = @((New-ControlElement 'start' 'startEvent'), $notice)
                    $childFlows = @((New-ControlFlow 'raise' 'start' 'notice'))
                } else {
                    $childActivities = @(); $childElements = @((New-ControlElement 'start' 'startEvent'), (New-ControlElement 'fork' 'parallelGateway'), $notice,
                        (New-ControlElement 'error' 'endEvent' '' @(@{ type = 'error'; properties = @{ code = 'OTHER' } })))
                    $childFlows = @((New-ControlFlow 'begin' 'start' 'fork'), (New-ControlFlow 'raise' 'fork' 'notice'), (New-ControlFlow 'fail' 'fork' 'error'))
                }
                $childRoot = New-ActivityNode -NodeId 'child-bpmn' -VersionId $bpmn -Structure (New-ControlStructure $childActivities $childElements $childFlows)
                $childDefinition = Submit-Workflow -Ctx $ctx -Name "CalledControlChild-$kind-$interrupting-$suffix" -RootActivity $childRoot
                $childPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $childDefinition.version.id
                $call = New-ActivityNode -NodeId 'node-call' -VersionId $dispatch -Inputs @(
                    (New-LiteralInput -ReferenceKey 'WorkflowDefinitionId' -Value $childDefinition.definition.id),
                    (New-LiteralInput -ReferenceKey 'WaitForCompletion' -Value $true))
                $warning = New-ControlWait 'node-warning' $event $warningName
                $normal = New-ActivityNode -NodeId 'node-normal' -VersionId $write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'Actual called-control normal continuation.'))
                $boundary = @{ elementId = 'boundary'; elementType = 'boundaryEvent'; attachedToRef = 'call'; cancelActivity = $interrupting;
                    eventDefinitions = @(@{ type = 'escalation'; properties = @{ code = 'WARN' } }) }
                $parentRoot = New-ActivityNode -NodeId 'parent-bpmn' -VersionId $bpmn -Structure (New-ControlStructure @($call, $warning, $normal) @(
                    (New-ControlElement 'start' 'startEvent'), (New-ControlElement 'call' 'callActivity' 'node-call'), $boundary,
                    (New-ControlElement 'normal' 'task' 'node-normal'), (New-ControlElement 'normal-end' 'endEvent'),
                    (New-ControlElement 'warning' 'task' 'node-warning'), (New-ControlElement 'warning-end' 'endEvent')
                ) @((New-ControlFlow 'begin' 'start' 'call'), (New-ControlFlow 'normal' 'call' 'normal'), (New-ControlFlow 'normal-end' 'normal' 'normal-end'),
                    (New-ControlFlow 'warn' 'boundary' 'warning'), (New-ControlFlow 'warn-end' 'warning' 'warning-end')))
                $parentDefinition = Submit-Workflow -Ctx $ctx -Name "CalledControlParent-$kind-$interrupting-$suffix" -RootActivity $parentRoot
                $parentPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDefinition.version.id
                $run = Invoke-Artifact -Ctx $ctx -ArtifactId $parentPublication.artifactId -SourceReferenceId $parentPublication.sourceReferenceId
                $child = Wait-ControlChild -Ctx $ctx -ArtifactId $childPublication.artifactId -ParentId $run.workflowExecutionId
                $case = @{ kind = $kind; interrupting = $interrupting; emitEventName = $emitName; finishEventName = $finishName; warningEventName = $warningName;
                    parentWorkflowExecutionId = $run.workflowExecutionId; childWorkflowExecutionId = $child.instance.workflowExecutionId;
                    parentPublication = $parentPublication; childPublication = $childPublication }
                if ($kind -ceq 'RunningEscalation') {
                    $case.beforeEmission = Wait-ControlNode $ctx $case.childWorkflowExecutionId 'node-child-emit' 'Suspended'
                    $case.emitDelivery = Send-ControlEvent $ctx $emitName $case.childWorkflowExecutionId
                }
                $parent = if ($kind -ceq 'SignalAndFailure' -and -not $interrupting) { Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 60 } else { Wait-ControlNode $ctx $run.workflowExecutionId 'node-warning' 'Suspended' }
                # Wait for actual asynchronous child cancellation as well as caller boundary consumption.
                $deadline = (Get-Date).AddSeconds(60)
                do {
                    $child = Get-WorkflowInstance -Ctx $ctx -ExecutionId $case.childWorkflowExecutionId
                    try { Assert-ControlStage $case $parent $child $false; break } catch { if ((Get-Date) -ge $deadline) { throw }; Start-Sleep -Milliseconds 100; $parent = Get-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId }
                } while ($true)
                $case.parent = $parent; $case.child = $child; $receipt.cases += $case
            }
        }
    } else {
        if (-not $PreparedEvidencePath) { throw 'An actual prepared receipt is required for continuation.' }
        $prepared = Get-Content -LiteralPath $PreparedEvidencePath -Raw | ConvertFrom-Json -AsHashtable
        if (-not $prepared.passed -or $prepared.phase -cne 'PrepareEscalations' -or $prepared.cases.Count -ne 6) { throw 'Six successfully prepared actual cases are required.' }
        $receipt.preparedSourceRevision = $prepared.sourceRevision
        foreach ($case in $prepared.cases) {
            $parent = Get-WorkflowInstance -Ctx $ctx -ExecutionId $case.parentWorkflowExecutionId
            $child = Get-WorkflowInstance -Ctx $ctx -ExecutionId $case.childWorkflowExecutionId
            Assert-ControlStage $case $parent $child $false
            $case.recoveredParent = $parent; $case.recoveredChild = $child
            if ($Phase -ceq 'FinishEscalations') {
                if ($case.kind -cne 'SignalAndFailure' -or $case.interrupting) {
                    $case.warningDelivery = Send-ControlEvent $ctx $case.warningEventName $case.parentWorkflowExecutionId
                    if ($case.kind -ceq 'RunningEscalation' -and -not $case.interrupting) {
                        $null = Wait-ControlNode $ctx $case.childWorkflowExecutionId 'node-child-finish' 'Suspended'
                        $case.finishDelivery = Send-ControlEvent $ctx $case.finishEventName $case.childWorkflowExecutionId
                    }
                    $parent = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $case.parentWorkflowExecutionId -TimeoutSeconds 60
                    $child = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $case.childWorkflowExecutionId -TimeoutSeconds 60
                }
                Assert-ControlStage $case $parent $child $true
            }
            $case.parent = $parent; $case.child = $child; $receipt.cases += $case
        }
    }
    $receipt.passed = $true
} catch {
    $receipt.failures += $_.Exception.Message
    throw
} finally {
    $receipt | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $EvidencePath
}
