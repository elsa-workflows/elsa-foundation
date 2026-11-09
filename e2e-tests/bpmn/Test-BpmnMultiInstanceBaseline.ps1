<#
.SYNOPSIS
    Park actual partially completed sequential/parallel multi-instance calls in both persistence modes.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $BaseUrl, [Parameter(Mandatory)][string] $EvidencePath,
      [Parameter(Mandatory)][string] $SourceRevision)
. "$PSScriptRoot/_CalledControlCommon.ps1"
function Wait-MiChildren($Ctx, [string] $Artifact, [string] $Parent, [int] $Count) {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $list = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances?artifactId=$Artifact" -WebSession $Ctx.Session
        $items = if ($list -is [array]) { $list } else { $list.items }
        $children = @($items | Where-Object { $_.artifactId -ceq $Artifact -and $_.parentWorkflowExecutionId -ceq $Parent })
        if ($children.Count -eq $Count) { return @($children | ForEach-Object { Get-WorkflowInstance $Ctx $_.workflowExecutionId }) }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw 'The actual multi-instance call did not create the expected exact children.'
}
$receipt = @{ schemaVersion = 1; sourceRevision = $SourceRevision; phase = 'PartialMultiInstanceCalls'; passed = $false; cases = @(); failures = @() }
try {
    $ctx = Connect-Elsa -BaseUrl $BaseUrl
    $bpmn = Get-ActivityVersionId $ctx 'Elsa.Activities.Bpmn.Activities.BpmnProcess'
    $dispatch = Get-ActivityVersionId $ctx 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow'
    $event = Get-ActivityVersionId $ctx 'Elsa.Activities.Primitives.Activities.Event'
    foreach ($mode in @('Balanced', 'Durable')) {
        foreach ($sequential in @($true, $false)) {
            $suffix = [guid]::NewGuid().ToString('N'); $eventName = "mi-baseline-$suffix"
            $childRoot = New-ControlWait 'child-wait' $event $eventName
            $childDefinition = Submit-Workflow $ctx "MiChild-$mode-$sequential-$suffix" -RootActivity $childRoot -StrategyOptions @{ persistenceMode = $mode }
            $childPublication = Publish-WorkflowVersion $ctx $childDefinition.version.id
            $call = New-ActivityNode 'node-call' $dispatch -Inputs @(
                (New-LiteralInput 'WorkflowDefinitionId' $childDefinition.definition.id), (New-LiteralInput 'WaitForCompletion' $true))
            $element = New-ControlElement 'mi-call' 'callActivity' 'node-call'
            $element.loopCharacteristics = @{ isSequential = $sequential; cardinality = 3 }
            $root = New-ActivityNode 'mi-bpmn' $bpmn -Structure (New-ControlStructure @($call) @(
                (New-ControlElement 'start' 'startEvent'), $element, (New-ControlElement 'end' 'endEvent')) @(
                (New-ControlFlow 'begin' 'start' 'mi-call'), (New-ControlFlow 'finish' 'mi-call' 'end')))
            $definition = Submit-Workflow $ctx "MiParent-$mode-$sequential-$suffix" -RootActivity $root -StrategyOptions @{ persistenceMode = $mode }
            $publication = Publish-WorkflowVersion $ctx $definition.version.id
            $run = Invoke-Artifact $ctx $publication.artifactId $publication.sourceReferenceId
            $children = @(Wait-MiChildren $ctx $childPublication.artifactId $run.workflowExecutionId $(if ($sequential) { 1 } else { 3 }))
            foreach ($child in $children) { $null = Wait-ControlNode $ctx $child.instance.workflowExecutionId 'child-wait' 'Suspended' }
            $selected = ($children | Sort-Object { $_.instance.workflowExecutionId })[0].instance.workflowExecutionId
            $delivery = Send-ControlEvent $ctx $eventName $selected
            $completed = Wait-WorkflowInstance $ctx $selected -TimeoutSeconds 60
            if ($completed.instance.status -cne 'Completed') { throw 'The actual selected MI child did not complete.' }
            $children = @(Wait-MiChildren $ctx $childPublication.artifactId $run.workflowExecutionId $(if ($sequential) { 2 } else { 3 }))
            $deadline = (Get-Date).AddSeconds(60)
            do {
                $parent = Get-WorkflowInstance $ctx $run.workflowExecutionId
                $calls = @($parent.activities | Where-Object executableNodeId -ceq 'node-call')
                if (@($calls | Where-Object status -ceq 'Completed').Count -eq 1 -and
                    @($calls | Where-Object status -ceq 'Suspended').Count -eq $(if ($sequential) { 1 } else { 2 })) { break }
                if ((Get-Date) -ge $deadline) { throw 'The actual partial MI parent was not held at the expected iteration boundary.' }
                Start-Sleep -Milliseconds 100
            } while ($true)
            if ($parent.instance.status -cne 'Running' -or $parent.persistenceMode -cne $mode -or @($parent.incidents).Count -ne 0) { throw 'The actual MI parent is not cleanly running in the selected mode.' }
            $receipt.cases += @{ isSequential = $sequential; cardinality = 3; persistenceMode = $mode; parentWorkflowExecutionId = $run.workflowExecutionId;
                parentPublication = $publication; childPublication = $childPublication; parent = $parent; children = $children;
                completedChildWorkflowExecutionId = $selected; eventName = $eventName; delivery = $delivery }
        }
    }
    $receipt.passed = $true
} catch { $receipt.failures += $_.Exception.Message; throw }
finally { $receipt | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $EvidencePath }
