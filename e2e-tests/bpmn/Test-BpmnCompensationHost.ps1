<#
.SYNOPSIS
    Exercise ordinary bound-task compensation through actual public Foundation APIs.
.DESCRIPTION
    The supplied source-built standard host is owned by the caller. No process lifecycle or runtime
    state edits occur here. Prepare parks the newest handler; later phases resume only original
    correlated Events and assert exact handler order, identity, pins and final incident-free completion.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseUrl,
    [Parameter(Mandatory)][string] $EvidencePath,
    [Parameter(Mandatory)][string] $SourceRevision,
    [Parameter(Mandatory)][ValidateSet('Prepare', 'Observe', 'FinishB', 'FinishA', 'Finish')][string] $Phase,
    [string] $PreparedEvidencePath,
    [ValidateSet('HandlerB', 'HandlerA', 'Settled', 'Finished')][string] $ExpectedStage = 'HandlerB',
    [string] $Username = 'admin', [string] $Password = 'Password123!'
)
. "$PSScriptRoot/_CalledControlCommon.ps1"

function Assert-CompensationStage($Case, $View, [string] $Stage) {
    if ($View.instance.workflowExecutionId -cne $Case.workflowExecutionId -or $View.instance.artifactId -cne $Case.publication.artifactId -or
        $View.instance.status -cne $(if ($Stage -ceq 'Finished') { 'Completed' } else { 'Running' }) -or @($View.incidents).Count -ne 0) {
        throw 'The actual compensation workflow lost its original pin/status or acquired an incident.'
    }
    foreach ($id in @('node-host-a', 'node-host-b')) {
        $items = @($View.activities | Where-Object executableNodeId -CEQ $id)
        if ($items.Count -ne 1 -or $items[0].status -cne 'Completed') { throw 'The ordinary bound task did not complete exactly once.' }
    }
    foreach ($id in @('node-handler-b', 'node-handler-a', 'node-hold')) {
        $items = @($View.activities | Where-Object executableNodeId -CEQ $id)
        $expected = switch ($id) {
            'node-handler-b' { if ($Stage -ceq 'HandlerB') { 'Suspended' } else { 'Completed' } }
            'node-handler-a' { if ($Stage -ceq 'HandlerB') { 'Absent' } elseif ($Stage -ceq 'HandlerA') { 'Suspended' } else { 'Completed' } }
            'node-hold' { if ($Stage -in @('HandlerB', 'HandlerA')) { 'Absent' } elseif ($Stage -ceq 'Settled') { 'Suspended' } else { 'Completed' } }
        }
        if (($expected -ceq 'Absent' -and $items.Count -ne 0) -or ($expected -cne 'Absent' -and ($items.Count -ne 1 -or $items[0].status -cne $expected))) {
            throw "The actual $id execution does not preserve one-at-a-time reverse compensation order."
        }
    }
    if ($Case.ContainsKey('handlerBActivityExecutionId')) {
        $handler = @($View.activities | Where-Object executableNodeId -CEQ 'node-handler-b')
        if ($handler[0].activityExecutionId -cne $Case.handlerBActivityExecutionId) { throw 'The original handler B identity changed.' }
    }
}
$receipt = @{ schemaVersion = 1; phase = $Phase; expectedStage = $ExpectedStage; sourceRevision = $SourceRevision; passed = $false }
try {
    if ((Invoke-WebRequest "$BaseUrl/health/ready" -TimeoutSec 10).StatusCode -ne 200) { throw 'The supplied owned host is not ready.' }
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    if ($Phase -ceq 'Prepare') {
        $bpmn = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Bpmn.Activities.BpmnProcess'
        $event = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event'
        $write = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
        $suffix = [guid]::NewGuid().ToString('N'); $eventA = "comp-a-$suffix"; $eventB = "comp-b-$suffix"; $hold = "comp-hold-$suffix"
        $handlerA = New-ControlElement 'handler-a' 'task' 'node-handler-a'; $handlerA.isForCompensation = $true
        $handlerB = New-ControlElement 'handler-b' 'task' 'node-handler-b'; $handlerB.isForCompensation = $true
        $boundaryA = New-ControlElement 'cb-a' 'boundaryEvent' '' @(@{ type = 'compensation' }); $boundaryA.attachedToRef = 'host-a'; $boundaryA.compensationHandlerElementId = 'handler-a'
        $boundaryB = New-ControlElement 'cb-b' 'boundaryEvent' '' @(@{ type = 'compensation' }); $boundaryB.attachedToRef = 'host-b'; $boundaryB.compensationHandlerElementId = 'handler-b'
        $root = New-ActivityNode -NodeId 'compensation-bpmn' -VersionId $bpmn -Structure (New-ControlStructure @(
            (New-ActivityNode -NodeId 'node-host-a' -VersionId $write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'ordinary-compensable-a'))),
            (New-ActivityNode -NodeId 'node-host-b' -VersionId $write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'ordinary-compensable-b'))),
            (New-ControlWait 'node-handler-a' $event $eventA), (New-ControlWait 'node-handler-b' $event $eventB), (New-ControlWait 'node-hold' $event $hold)
        ) @((New-ControlElement 'start' 'startEvent'), (New-ControlElement 'host-a' 'serviceTask' 'node-host-a'),
            (New-ControlElement 'host-b' 'serviceTask' 'node-host-b'), $handlerA, $handlerB, $boundaryA, $boundaryB,
            (New-ControlElement 'replay' 'intermediateThrowEvent' '' @(@{ type = 'compensation' })),
            (New-ControlElement 'hold' 'task' 'node-hold'), (New-ControlElement 'end' 'endEvent')) @(
            (New-ControlFlow 'begin' 'start' 'host-a'), (New-ControlFlow 'second' 'host-a' 'host-b'),
            (New-ControlFlow 'compensate' 'host-b' 'replay'), (New-ControlFlow 'settled' 'replay' 'hold'), (New-ControlFlow 'finish' 'hold' 'end')))
        $definition = Submit-Workflow -Ctx $ctx -Name "OrdinaryCompensation-$suffix" -RootActivity $root
        $publication = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
        $run = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
        $case = @{ workflowExecutionId = $run.workflowExecutionId; definition = $definition; publication = $publication; eventA = $eventA; eventB = $eventB; holdEvent = $hold }
        $view = Wait-ControlNode $ctx $case.workflowExecutionId 'node-handler-b' 'Suspended'
        $case.handlerBActivityExecutionId = @($view.activities | Where-Object executableNodeId -CEQ 'node-handler-b')[0].activityExecutionId
        $receipt.catalogVersions = @{ bpmn = $bpmn; event = $event; write = $write }
    } else {
        if (-not $PreparedEvidencePath) { throw 'Original successful preparation evidence is required.' }
        $prepared = Get-Content -LiteralPath $PreparedEvidencePath -Raw | ConvertFrom-Json -AsHashtable
        if (-not $prepared.passed -or $prepared.phase -cne 'Prepare') { throw 'A successful actual preparation is required.' }
        $case = $prepared.case
        if ($Phase -ceq 'FinishB') { $receipt.delivery = Send-ControlEvent $ctx $case.eventB $case.workflowExecutionId }
        if ($Phase -ceq 'FinishA') { $receipt.delivery = Send-ControlEvent $ctx $case.eventA $case.workflowExecutionId }
        if ($Phase -ceq 'Finish') { $receipt.delivery = Send-ControlEvent $ctx $case.holdEvent $case.workflowExecutionId }
        $view = switch ($ExpectedStage) {
            'HandlerB' { Wait-ControlNode $ctx $case.workflowExecutionId 'node-handler-b' 'Suspended' }
            'HandlerA' { Wait-ControlNode $ctx $case.workflowExecutionId 'node-handler-a' 'Suspended' }
            'Settled' { Wait-ControlNode $ctx $case.workflowExecutionId 'node-hold' 'Suspended' }
            'Finished' { Wait-WorkflowInstance -Ctx $ctx -ExecutionId $case.workflowExecutionId -TimeoutSeconds 60 }
        }
    }
    Assert-CompensationStage $case $view $ExpectedStage
    $receipt.case = $case; $receipt.workflow = $view; $receipt.passed = $true
} finally {
    $receipt | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $EvidencePath
}
