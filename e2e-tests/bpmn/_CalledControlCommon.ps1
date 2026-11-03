# Public API helpers for the owned called-control host fixtures.
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
