<#
.SYNOPSIS
    Capture or continue real legacy waited Call Activity executions through an explicitly supplied host.
.DESCRIPTION
    The caller owns and identifies the source-built host and its isolated runtime database. This script
    never starts/stops a server or edits persisted state. Capture uses the public authoring/publishing APIs;
    Resume uses normal Event stimulus dispatch. Stop the old writer and freeze its complete runtime store
    before running either an old-host control or an upgraded-host continuation on separate writable copies.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Capture', 'Resume')][string] $Phase,
    [Parameter(Mandatory)][string] $BaseUrl,
    [Parameter(Mandatory)][string] $EvidencePath,
    [Parameter(Mandatory)][string] $SourceRevision,
    [string] $CapturePath,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)
. "$PSScriptRoot/../stimuli/_StimuliCommon.ps1"

function New-LegacyBpmnStructure([array] $Activities, [array] $Elements, [array] $Flows) {
    @{ kind = 'elsa.bpmn.structure'; schemaVersion = '1.0.0'; payload = @{ activities = $Activities; elements = $Elements; sequenceFlows = $Flows } }
}
function New-LegacyBpmnElement([string] $Id, [string] $Type, [string] $ChildNodeId) {
    $element = @{ elementId = $Id; elementType = $Type }
    if ($ChildNodeId) { $element.childNodeId = $ChildNodeId }
    $element
}
function New-LegacyBpmnFlow([string] $Id, [string] $Source, [string] $Target) {
    @{ flowId = $Id; sourceRef = $Source; targetRef = $Target }
}
function Wait-LegacyCallParked($Ctx, [string] $ParentId, [string] $ChildArtifact) {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $parent = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $ParentId
        $list = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances?artifactId=$ChildArtifact" -WebSession $Ctx.Session
        $items = if ($list -is [array]) { $list } else { $list.items }
        $children = @($items | Where-Object artifactId -eq $ChildArtifact)
        if ($children.Count -eq 1) {
            $child = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $children[0].workflowExecutionId
            $call = @($parent.activities | Where-Object { $_.executableNodeId -eq 'node-call' -and $_.status -eq 'Suspended' })
            $wait = @($child.activities | Where-Object { $_.executableNodeId -eq 'node-child-wait' -and $_.status -eq 'Suspended' })
            if ($parent.instance.status -eq 'Running' -and $child.instance.status -eq 'Running' -and
                $call.Count -eq 1 -and $wait.Count -eq 1 -and @($parent.incidents).Count -eq 0 -and @($child.incidents).Count -eq 0) {
                return @{ parent = $parent; child = $child; childWorkflowExecutionId = $children[0].workflowExecutionId }
            }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw 'The exact public caller/child did not reach their required real dispatch/Event waits.'
}
function Assert-LegacyInstanceIdentity($Current, $Captured) {
    foreach ($property in @('workflowExecutionId', 'artifactId', 'definitionId', 'definitionVersionId', 'artifactVersion', 'artifactHash',
        'parentWorkflowExecutionId', 'tenantId', 'runKind', 'sourceReferenceId', 'publicationId')) {
        if ($Current.instance.$property -cne $Captured.instance.$property) { throw "Captured workflow identity changed: $property." }
    }
    if ($Current.persistenceMode -cne $Captured.persistenceMode) { throw 'Captured persistence mode changed.' }
}
function Get-ExactLegacyActivity($Current, $Captured, [string] $NodeId) {
    $previous = @($Captured.activities | Where-Object executableNodeId -eq $NodeId)
    $now = @($Current.activities | Where-Object executableNodeId -eq $NodeId)
    if ($previous.Count -ne 1 -or $now.Count -ne 1 -or
        [string]::IsNullOrWhiteSpace($previous[0].activityExecutionId) -or
        $now[0].activityExecutionId -cne $previous[0].activityExecutionId -or
        $now[0].workflowExecutionId -cne $previous[0].workflowExecutionId -or
        $now[0].provenance.parentActivityExecutionId -cne $previous[0].provenance.parentActivityExecutionId) {
        throw "The original captured activity was replaced or ambiguous: $NodeId."
    }
    $now[0]
}

$receipt = @{ schemaVersion = 1; phase = $Phase; sourceRevision = $SourceRevision; passed = $false; cases = @(); failures = @() }
try {
    if ((Invoke-WebRequest "$BaseUrl/health/ready" -TimeoutSec 10).StatusCode -ne 200) { throw 'The supplied host is not ready.' }
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    if ($Phase -eq 'Capture') {
        $types = @{}
        foreach ($entry in @(
            @('bpmn', 'Elsa.Activities.Bpmn.Activities.BpmnProcess'),
            @('event', 'Elsa.Activities.Primitives.Activities.Event'),
            @('write', 'Elsa.Activities.Primitives.Activities.WriteLine'),
            @('fault', 'Elsa.Activities.Primitives.Activities.Fault'),
            @('dispatch', 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow'))) {
            $types[$entry[0]] = Get-ActivityVersionId -Ctx $ctx -TypeKey $entry[1]
        }
        $receipt.catalogVersions = $types
        foreach ($faultChild in @($false, $true)) {
            $name = if ($faultChild) { 'ordinary-fault' } else { 'normal-completion' }
            $eventName = "legacy-called-$name-$([guid]::NewGuid().ToString('N'))"
            $tail = if ($faultChild) {
                New-ActivityNode -NodeId 'node-child-tail' -VersionId $types.fault -Inputs @((New-LiteralInput -ReferenceKey 'message' -Value 'Actual legacy called child ordinary fault.'))
            } else {
                New-ActivityNode -NodeId 'node-child-tail' -VersionId $types.write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'Actual legacy called child completed.'))
            }
            $childRoot = New-ActivityNode -NodeId 'child-bpmn' -VersionId $types.bpmn -Structure (New-LegacyBpmnStructure @(
                (New-EventWaitNode -NodeId 'node-child-wait' -EventVersionId $types.event -EventName $eventName), $tail
            ) @(
                (New-LegacyBpmnElement 'start' 'startEvent'), (New-LegacyBpmnElement 'wait' 'task' 'node-child-wait'),
                (New-LegacyBpmnElement 'tail' 'task' 'node-child-tail'), (New-LegacyBpmnElement 'end' 'endEvent')
            ) @((New-LegacyBpmnFlow 'begin' 'start' 'wait'), (New-LegacyBpmnFlow 'resume' 'wait' 'tail'), (New-LegacyBpmnFlow 'finish' 'tail' 'end')))
            $childDefinition = Submit-Workflow -Ctx $ctx -Name "LegacyCalledChild-$eventName" -RootActivity $childRoot
            $childPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $childDefinition.version.id
            $dispatch = New-ActivityNode -NodeId 'node-call' -VersionId $types.dispatch -Inputs @(
                (New-LiteralInput -ReferenceKey 'WorkflowDefinitionId' -Value $childDefinition.definition.id),
                (New-LiteralInput -ReferenceKey 'WaitForCompletion' -Value $true))
            $normal = New-ActivityNode -NodeId 'node-normal' -VersionId $types.write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'Legacy ordinary normal path.'))
            $handler = New-ActivityNode -NodeId 'node-handler' -VersionId $types.write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'Legacy ordinary Faulted-outcome path.'))
            $boundary = @{ elementId = 'error'; elementType = 'boundaryEvent'; attachedToRef = 'call'; eventDefinitions = @(@{ type = 'error' }) }
            $parentRoot = New-ActivityNode -NodeId 'parent-bpmn' -VersionId $types.bpmn -Structure (New-LegacyBpmnStructure @($dispatch, $normal, $handler) @(
                (New-LegacyBpmnElement 'start' 'startEvent'), (New-LegacyBpmnElement 'call' 'callActivity' 'node-call'),
                (New-LegacyBpmnElement 'normal' 'task' 'node-normal'), (New-LegacyBpmnElement 'normal-end' 'endEvent'),
                $boundary, (New-LegacyBpmnElement 'handler' 'task' 'node-handler'), (New-LegacyBpmnElement 'caught-end' 'endEvent')
            ) @((New-LegacyBpmnFlow 'begin' 'start' 'call'), (New-LegacyBpmnFlow 'normal' 'call' 'normal'),
                (New-LegacyBpmnFlow 'finish' 'normal' 'normal-end'), (New-LegacyBpmnFlow 'catch' 'error' 'handler'),
                (New-LegacyBpmnFlow 'caught' 'handler' 'caught-end')))
            $parentDefinition = Submit-Workflow -Ctx $ctx -Name "LegacyCalledParent-$eventName" -RootActivity $parentRoot
            $parentPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDefinition.version.id
            $run = Invoke-Artifact -Ctx $ctx -ArtifactId $parentPublication.artifactId -SourceReferenceId $parentPublication.sourceReferenceId
            $parked = Wait-LegacyCallParked -Ctx $ctx -ParentId $run.workflowExecutionId -ChildArtifact $childPublication.artifactId
            $receipt.cases += @{ name = $name; faultChild = $faultChild; eventName = $eventName;
                parentWorkflowExecutionId = $run.workflowExecutionId; childWorkflowExecutionId = $parked.childWorkflowExecutionId;
                childPublication = $childPublication; parentPublication = $parentPublication;
                parentAtCapture = $parked.parent; childAtCapture = $parked.child }
        }
    } else {
        if (-not $CapturePath) { throw 'Resume requires the actual capture receipt.' }
        $capture = Get-Content -Raw $CapturePath | ConvertFrom-Json -Depth 100
        if (-not $capture.passed -or $capture.phase -ne 'Capture' -or @($capture.cases).Count -ne 2) { throw 'Capture receipt is incomplete.' }
        foreach ($case in $capture.cases) {
            $beforeParent = Get-WorkflowInstance -Ctx $ctx -ExecutionId $case.parentWorkflowExecutionId
            $beforeChild = Get-WorkflowInstance -Ctx $ctx -ExecutionId $case.childWorkflowExecutionId
            Assert-LegacyInstanceIdentity $beforeParent $case.parentAtCapture
            Assert-LegacyInstanceIdentity $beforeChild $case.childAtCapture
            $beforeCall = Get-ExactLegacyActivity $beforeParent $case.parentAtCapture 'node-call'
            $beforeWait = Get-ExactLegacyActivity $beforeChild $case.childAtCapture 'node-child-wait'
            $beforeParentRoot = Get-ExactLegacyActivity $beforeParent $case.parentAtCapture 'parent-bpmn'
            $beforeChildRoot = Get-ExactLegacyActivity $beforeChild $case.childAtCapture 'child-bpmn'
            if ($beforeParent.instance.status -ne 'Running' -or $beforeChild.instance.status -ne 'Running') { throw 'A captured execution is not live before continuation.' }
            if ($beforeCall.status -ne 'Suspended' -or $beforeWait.status -ne 'Suspended' -or
                $beforeParentRoot.status -ne 'Running' -or $beforeChildRoot.status -ne 'Running' -or
                $beforeChild.instance.parentWorkflowExecutionId -cne $case.parentWorkflowExecutionId) { throw 'The original captured caller/root/child waits or workflow linkage changed.' }
            $delivery = Invoke-Stimulus -Ctx $ctx -EventName $case.eventName -Mode 'ResumeOnly' -InputObject @{ eventName = $case.eventName }
            if ($delivery.resumedCount -ne 1) { throw 'Normal Event dispatch did not resume exactly the captured child wait.' }
            $parent = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $case.parentWorkflowExecutionId -TimeoutSeconds 60
            $child = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $case.childWorkflowExecutionId -TimeoutSeconds 60
            Assert-LegacyInstanceIdentity $parent $case.parentAtCapture
            Assert-LegacyInstanceIdentity $child $case.childAtCapture
            $originalCall = Get-ExactLegacyActivity $parent $case.parentAtCapture 'node-call'
            $originalWait = Get-ExactLegacyActivity $child $case.childAtCapture 'node-child-wait'
            $originalParentRoot = Get-ExactLegacyActivity $parent $case.parentAtCapture 'parent-bpmn'
            $originalChildRoot = Get-ExactLegacyActivity $child $case.childAtCapture 'child-bpmn'
            if ($originalWait.status -ne 'Completed' -or $originalParentRoot.status -ne 'Completed' -or
                $originalChildRoot.status -ne $(if ($case.faultChild) { 'Faulted' } else { 'Completed' })) { throw 'The exact captured wait/roots did not finish their expected original invocations.' }
            $call = @($parent.activities | Where-Object executableNodeId -eq 'node-call')
            $expectedOutcome = if ($case.faultChild) { 'Faulted' } else { 'Completed' }
            $expectedChild = if ($case.faultChild) { 'Faulted' } else { 'Completed' }
            $expectedNode = if ($case.faultChild) { 'node-handler' } else { 'node-normal' }
            $unexpectedNode = if ($case.faultChild) { 'node-normal' } else { 'node-handler' }
            $receipt.cases += @{ name = $case.name; delivery = $delivery; beforeParent = $beforeParent; beforeChild = $beforeChild; parent = $parent; child = $child }
            if ($parent.instance.status -ne 'Completed' -or $child.instance.status -ne $expectedChild -or
                $call.Count -ne 1 -or $call[0].status -ne 'Completed' -or $expectedOutcome -notin @($call[0].outcomeNames) -or
                @($parent.activities | Where-Object { $_.executableNodeId -eq $expectedNode -and $_.status -eq 'Completed' }).Count -ne 1 -or
                @($parent.activities | Where-Object executableNodeId -eq $unexpectedNode).Count -ne 0 -or @($parent.incidents).Count -ne 0) {
                throw 'Actual legacy ordinary-result policy did not complete the expected caller path with truthful child outcome.'
            }
            if (($case.faultChild -and @($child.incidents).Count -eq 0) -or (-not $case.faultChild -and @($child.incidents).Count -ne 0)) {
                throw 'Actual child incident truth did not match its ordinary outcome.'
            }
            $tail = @($child.activities | Where-Object executableNodeId -eq 'node-child-tail')
            if ($tail.Count -ne 1 -or $tail[0].status -ne $expectedChild -or
                $tail[0].provenance.parentActivityExecutionId -cne $originalChildRoot.activityExecutionId) { throw 'The intended tail did not run under the original captured child root.' }
            if ($case.faultChild) {
                $tailIncident = @($child.incidents | Where-Object { $_.activityExecutionId -ceq $tail[0].activityExecutionId -and
                    $_.executableNodeId -eq 'node-child-tail' -and $_.failureType -eq 'ActivityReturnedFault' -and
                    $_.isBlocking -eq $true -and $_.message -ceq 'Actual legacy called child ordinary fault.' })
                if ($tailIncident.Count -ne 1) { throw 'The intended real Fault tail did not retain its own blocking typed-return incident/message.' }
            }
        }
    }
    $receipt.passed = $true
} catch {
    $receipt.failures += $_.Exception.Message
    throw
} finally {
    $receipt | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $EvidencePath
}
