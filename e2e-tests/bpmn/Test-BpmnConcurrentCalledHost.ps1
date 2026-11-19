<#
.SYNOPSIS
    Prove public-host isolation of two simultaneous semantic tokens at one call element and binding.
.DESCRIPTION
    The caller owns the standard host lifecycle and checked snapshots. This runner uses only normal public APIs.
    Selection comes from the checked immutable prepared snapshot, whose actual second active-work token must
    correlate to exactly one DispatchWorkflow invocation and child. No store order or iteration ID selects it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseUrl,
    [Parameter(Mandatory)][string] $EvidencePath,
    [Parameter(Mandatory)][string] $SourceRevision,
    [Parameter(Mandatory)][ValidateSet('Prepare', 'HandleSelected', 'ObserveSelected', 'HandleOther')][string] $Phase,
    [string] $PreparedEvidencePath,
    [string] $SelectionEvidencePath,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)
. "$PSScriptRoot/_CalledControlCommon.ps1"

function Wait-ConcurrentChildren($Ctx, [string] $ArtifactId, [string] $ParentId) {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $list = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances?artifactId=$ArtifactId" -WebSession $Ctx.Session
        $items = if ($list -is [array]) { $list } else { $list.items }
        $matches = @($items | Where-Object { $_.artifactId -ceq $ArtifactId -and $_.parentWorkflowExecutionId -ceq $ParentId })
        if ($matches.Count -eq 2) { return @($matches | ForEach-Object { Get-WorkflowInstance -Ctx $Ctx -ExecutionId $_.workflowExecutionId }) }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw 'The actual same-binding caller did not dispatch two exact children.'
}
function Wait-ConcurrentView($Ctx, [string] $ExecutionId, [string] $NodeId, [int] $Total, [string] $Status, [int] $StatusCount) {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $view = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $ExecutionId
        $nodes = @($view.activities | Where-Object executableNodeId -eq $NodeId)
        if ($nodes.Count -eq $Total -and @($nodes | Where-Object status -eq $Status).Count -eq $StatusCount) { return $view }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw 'The actual concurrent caller did not reach the required exact invocation counts.'
}
function Assert-ConcurrentStage($Case, $Parent, $Children, [int] $Handled) {
    $finished = $Handled -eq 2; $interrupting = [bool]$Case.interrupting
    if ($Parent.instance.status -cne $(if ($finished) { 'Completed' } else { 'Running' }) -or @($Parent.incidents).Count -ne 0 -or
        $Parent.instance.artifactId -cne $Case.parentPublication.artifactId -or @($Children).Count -ne 2) { throw 'Actual concurrent caller status, pin or child count differs.' }
    $calls = @($Parent.activities | Where-Object executableNodeId -eq 'node-call')
    $warnings = @($Parent.activities | Where-Object executableNodeId -eq 'node-warning')
    $normals = @($Parent.activities | Where-Object executableNodeId -eq 'node-normal')
    $done = if ($interrupting) { 'Cancelled' } else { 'Completed' }
    if ($calls.Count -ne 2 -or @($calls | Where-Object status -eq $done).Count -ne $Handled -or
        @($calls | Where-Object status -eq 'Suspended').Count -ne (2 - $Handled) -or $warnings.Count -ne $Handled -or
        @($warnings | Where-Object status -eq 'Completed').Count -ne $Handled -or $normals.Count -ne $(if ($interrupting) { 0 } else { $Handled }) -or
        @($normals | Where-Object status -ne 'Completed').Count -ne 0) { throw 'Actual concurrent call/branch counts differ from exact independent handling.' }
    foreach ($child in $Children) {
        if ($child.instance.artifactId -cne $Case.childPublication.artifactId -or $child.instance.parentWorkflowExecutionId -cne $Case.parentWorkflowExecutionId -or @($child.incidents).Count -ne 0) { throw 'Actual sibling child pin, ancestry or incidents changed.' }
    }
}

$receipt = @{ schemaVersion = 1; phase = $Phase; sourceRevision = $SourceRevision; passed = $false; cases = @(); failures = @() }
try {
    if ((Invoke-WebRequest "$BaseUrl/health/ready" -TimeoutSec 10).StatusCode -ne 200) { throw 'The explicitly supplied owned host is not ready.' }
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    if ($Phase -ceq 'Prepare') {
        $bpmn = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Bpmn.Activities.BpmnProcess'
        $dispatch = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow'
        $write = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
        $event = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event'
        $receipt.catalogVersions = @{ bpmn = $bpmn; dispatch = $dispatch; write = $write; event = $event }
        foreach ($interrupting in @($false, $true)) {
            $suffix = [guid]::NewGuid().ToString('N')
            $emit = "concurrent-emit-$suffix"; $finish = "concurrent-finish-$suffix"; $warn = "concurrent-warning-$suffix"
            $childRoot = New-ActivityNode -NodeId 'child-bpmn' -VersionId $bpmn -Structure (New-ControlStructure @(
                (New-ControlWait 'node-child-emit' $event $emit), (New-ControlWait 'node-child-finish' $event $finish)
            ) @((New-ControlElement 'start' 'startEvent'), (New-ControlElement 'emit' 'task' 'node-child-emit'),
                (New-ControlElement 'notice' 'intermediateThrowEvent' '' @(@{ type = 'escalation'; properties = @{ code = 'WARN' } })),
                (New-ControlElement 'finish' 'task' 'node-child-finish'), (New-ControlElement 'end' 'endEvent')) @(
                (New-ControlFlow 'begin' 'start' 'emit'), (New-ControlFlow 'raise' 'emit' 'notice'), (New-ControlFlow 'park' 'notice' 'finish'), (New-ControlFlow 'end' 'finish' 'end')))
            $childDefinition = Submit-Workflow -Ctx $ctx -Name "ConcurrentCalledChild-$interrupting-$suffix" -RootActivity $childRoot
            $childPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $childDefinition.version.id
            $call = New-ActivityNode -NodeId 'node-call' -VersionId $dispatch -Inputs @(
                (New-LiteralInput -ReferenceKey 'WorkflowDefinitionId' -Value $childDefinition.definition.id), (New-LiteralInput -ReferenceKey 'WaitForCompletion' -Value $true))
            $normal = New-ActivityNode -NodeId 'node-normal' -VersionId $write -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'Actual independent same-binding normal continuation.'))
            $warning = New-ControlWait 'node-warning' $event $warn
            $boundary = @{ elementId = 'boundary'; elementType = 'boundaryEvent'; attachedToRef = 'call'; cancelActivity = $interrupting;
                eventDefinitions = @(@{ type = 'escalation'; properties = @{ code = 'WARN' } }) }
            $parentRoot = New-ActivityNode -NodeId 'parent-bpmn' -VersionId $bpmn -Structure (New-ControlStructure @($call, $normal, $warning) @(
                (New-ControlElement 'start' 'startEvent'), (New-ControlElement 'fork' 'parallelGateway'), (New-ControlElement 'call' 'callActivity' 'node-call'), $boundary,
                (New-ControlElement 'normal' 'task' 'node-normal'), (New-ControlElement 'normal-end' 'endEvent'),
                (New-ControlElement 'warning' 'task' 'node-warning'), (New-ControlElement 'warning-end' 'endEvent')
            ) @((New-ControlFlow 'begin' 'start' 'fork'), (New-ControlFlow 'left' 'fork' 'call'), (New-ControlFlow 'right' 'fork' 'call'),
                (New-ControlFlow 'normal' 'call' 'normal'), (New-ControlFlow 'normal-end' 'normal' 'normal-end'),
                (New-ControlFlow 'warn' 'boundary' 'warning'), (New-ControlFlow 'warn-end' 'warning' 'warning-end')))
            $parentDefinition = Submit-Workflow -Ctx $ctx -Name "ConcurrentCalledParent-$interrupting-$suffix" -RootActivity $parentRoot
            $parentPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDefinition.version.id
            $run = Invoke-Artifact -Ctx $ctx -ArtifactId $parentPublication.artifactId -SourceReferenceId $parentPublication.sourceReferenceId
            $children = Wait-ConcurrentChildren $ctx $childPublication.artifactId $run.workflowExecutionId
            $children = @($children | ForEach-Object { Wait-ControlNode $ctx $_.instance.workflowExecutionId 'node-child-emit' 'Suspended' })
            $parent = Wait-ConcurrentView $ctx $run.workflowExecutionId 'node-call' 2 'Suspended' 2
            $case = @{ interrupting = $interrupting; emitEventName = $emit; finishEventName = $finish; warningEventName = $warn;
                parentWorkflowExecutionId = $run.workflowExecutionId; parentPublication = $parentPublication; childPublication = $childPublication;
                parent = $parent; children = $children }
            Assert-ConcurrentStage $case $parent $children 0
            $receipt.cases += $case
        }
    } else {
        if (-not $PreparedEvidencePath -or -not $SelectionEvidencePath) { throw 'The actual prepared receipt and checked second-token selection are required.' }
        $prepared = Get-Content -LiteralPath $PreparedEvidencePath -Raw | ConvertFrom-Json -AsHashtable
        $selection = Get-Content -LiteralPath $SelectionEvidencePath -Raw | ConvertFrom-Json -AsHashtable
        if (-not $prepared.passed -or $prepared.phase -cne 'Prepare' -or $prepared.cases.Count -ne 2 -or
            -not $selection.providerChecked -or -not $selection.readOnly -or $selection.calledWorkflows.Count -ne 2) { throw 'Both actual prepared cases and complete checked selection are required.' }
        foreach ($case in $prepared.cases) {
            $choice = @($selection.calledWorkflows | Where-Object parentWorkflowExecutionId -ceq $case.parentWorkflowExecutionId)
            if ($choice.Count -ne 1 -or $choice[0].interrupting -ne $case.interrupting) { throw 'The checked selection does not match the actual prepared caller policy.' }
            $choice = $choice[0]
            $expectedChildren = @($case.children | ForEach-Object { $_.instance.workflowExecutionId } | Sort-Object)
            if ((@($choice.selected.child.workflowExecutionId, $choice.other.child.workflowExecutionId | Sort-Object) -join ',') -cne ($expectedChildren -join ',')) { throw 'The checked selection has foreign actual siblings.' }
            $case.selection = @{
                selectedCallActivityExecutionId = $choice.selected.call.invocationId; otherCallActivityExecutionId = $choice.other.call.invocationId;
                selectedChildWorkflowExecutionId = $choice.selected.child.workflowExecutionId; otherChildWorkflowExecutionId = $choice.other.child.workflowExecutionId;
                selectedSemanticTokenId = $choice.selected.call.provenance.executionPathId; otherSemanticTokenId = $choice.other.call.provenance.executionPathId
            }
            $parent = Get-WorkflowInstance -Ctx $ctx -ExecutionId $case.parentWorkflowExecutionId
            $children = @($expectedChildren | ForEach-Object { Get-WorkflowInstance -Ctx $ctx -ExecutionId $_ })
            Assert-ConcurrentStage $case $parent $children $(if ($Phase -ceq 'HandleSelected') { 0 } else { 1 })
            $target = if ($Phase -cne 'HandleOther') { $choice.selected } else { $choice.other }
            $case.targetChildWorkflowExecutionId = $target.child.workflowExecutionId
            $case.targetCallActivityExecutionId = $target.call.invocationId
            $case.targetSemanticTokenId = $target.call.provenance.executionPathId
            if ($Phase -ceq 'ObserveSelected') {
                $case.parent = $parent; $case.children = $children; $receipt.cases += $case
                continue
            }
            $case.emitDelivery = Send-ControlEvent $ctx $case.emitEventName $case.targetChildWorkflowExecutionId
            $handled = if ($Phase -ceq 'HandleSelected') { 1 } else { 2 }
            $null = Wait-ConcurrentView $ctx $case.parentWorkflowExecutionId 'node-warning' $handled 'Suspended' 1
            if ($case.interrupting) {
                $targetChild = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $case.targetChildWorkflowExecutionId -TimeoutSeconds 60
                if ($targetChild.instance.status -cne 'Cancelled') { throw 'The exact selected running child was not actually canceled.' }
            } else { $null = Wait-ControlNode $ctx $case.targetChildWorkflowExecutionId 'node-child-finish' 'Suspended' }
            $case.warningDelivery = Send-ControlEvent $ctx $case.warningEventName $case.parentWorkflowExecutionId
            if (-not $case.interrupting) { $case.finishDelivery = Send-ControlEvent $ctx $case.finishEventName $case.targetChildWorkflowExecutionId }
            $parent = Wait-ConcurrentView $ctx $case.parentWorkflowExecutionId 'node-warning' $handled 'Completed' $handled
            $deadline = (Get-Date).AddSeconds(60)
            do {
                $parent = Get-WorkflowInstance -Ctx $ctx -ExecutionId $case.parentWorkflowExecutionId
                $children = @($expectedChildren | ForEach-Object { Get-WorkflowInstance -Ctx $ctx -ExecutionId $_ })
                try { Assert-ConcurrentStage $case $parent $children $handled; break } catch { if ((Get-Date) -ge $deadline) { throw }; Start-Sleep -Milliseconds 100 }
            } while ($true)
            $case.parent = $parent; $case.children = $children; $receipt.cases += $case
        }
    }
    $receipt.passed = $true
} catch { $receipt.failures += $_.Exception.Message; throw }
finally { $receipt | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $EvidencePath }
