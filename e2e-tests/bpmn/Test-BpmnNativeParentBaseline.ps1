<#
.SYNOPSIS
    Capture actual legacy native-parent fault/cancellation projections before hosting refactoring.
.DESCRIPTION
    Uses normal publish/execute/CancelWorkflow commands against an explicitly owned existing host.
    Does not edit stores or claim direct-model hosting, upgrade recovery or arbitrary alteration support.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseUrl,
    [Parameter(Mandatory)][string] $EvidencePath,
    [Parameter(Mandatory)][string] $SourceRevision,
    [string] $Username = 'admin', [string] $Password = 'Password123!'
)
. "$PSScriptRoot/_CalledControlCommon.ps1"
. "$PSScriptRoot/../runtime-alterations/_AlterationCommon.ps1"

function New-BaselineParent([string] $Kind, $Child, [hashtable] $Versions) {
    $after = New-ActivityNode 'after-bpmn' $Versions.Write -Inputs @((New-LiteralInput 'text' 'This successor must not run after failure/cancel.'))
    $structure = switch ($Kind) {
        'Sequence' { New-SequenceStructure -Activities @($Child, $after) }
        'Flowchart' { @{ kind = 'elsa.flowchart.structure'; schemaVersion = '1.0.0'; payload = @{
            startNodeId = $Child.nodeId; activities = @($Child, $after); nodeMetadata = @{}; connectionMetadata = @{};
            connections = @(@{ id = 'after'; source = @{ nodeId = $Child.nodeId; port = 'Done' }; target = @{ nodeId = $after.nodeId } })
        } } }
        'CompensationScope' { @{ kind = 'elsa.compensation-scope.structure'; schemaVersion = '1.0.0'; payload = @{
            steps = @(@{ name = 'bpmn'; activity = $Child }, @{ name = 'after'; activity = $after })
        } } }
    }
    New-ActivityNode 'native-parent' $Versions[$Kind] -Structure $structure
}

$receipt = @{ schemaVersion = 1; phase = 'LegacyNativeParentTerminals'; sourceRevision = $SourceRevision;
    passed = $false; cases = @(); failures = @(); scope = 'Existing native behavior, not direct-model or upgrade acceptance.' }
try {
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $versions = @{
        Bpmn = Get-ActivityVersionId $ctx 'Elsa.Activities.Bpmn.Activities.BpmnProcess'
        Event = Get-ActivityVersionId $ctx 'Elsa.Activities.Primitives.Activities.Event'
        Write = Get-ActivityVersionId $ctx 'Elsa.Activities.Primitives.Activities.WriteLine'
        Sequence = Get-ActivityVersionId $ctx 'Elsa.Activities.Sequence.Activities.Sequence'
        Flowchart = Get-ActivityVersionId $ctx 'Elsa.Activities.Flowchart.Activities.Flowchart'
        CompensationScope = Get-ActivityVersionId $ctx 'Elsa.Activities.Compensation.Activities.CompensationScope'
    }
    $receipt.catalogVersions = $versions
    foreach ($mode in @('Balanced', 'Durable')) {
        foreach ($parent in @('Sequence', 'Flowchart', 'CompensationScope')) {
            foreach ($outcome in @('Fault', 'Cancel')) {
                $suffix = [guid]::NewGuid().ToString('N')
                if ($outcome -ceq 'Fault') {
                    $elements = @((New-ControlElement 'start' 'startEvent'),
                        (New-ControlElement 'failure' 'endEvent' '' @(@{ type = 'error'; properties = @{ code = 'BASELINE' } })))
                    $flows = @((New-ControlFlow 'raise' 'start' 'failure'))
                    $children = @()
                } else {
                    $eventName = "native-baseline-$suffix"
                    $children = @((New-ControlWait 'node-held' $versions.Event $eventName))
                    $elements = @((New-ControlElement 'start' 'startEvent'), (New-ControlElement 'held' 'task' 'node-held'),
                        (New-ControlElement 'end' 'endEvent'))
                    $flows = @((New-ControlFlow 'begin' 'start' 'held'), (New-ControlFlow 'finish' 'held' 'end'))
                }
                $bpmn = New-ActivityNode 'legacy-bpmn' $versions.Bpmn -Structure (New-ControlStructure $children $elements $flows)
                $root = New-BaselineParent $parent $bpmn $versions
                $definition = Submit-Workflow -Ctx $ctx -Name "NativeBpmnBaseline-$parent-$mode-$outcome-$suffix" `
                    -RootActivity $root -StrategyOptions @{ persistenceMode = $mode }
                $publication = Publish-WorkflowVersion $ctx $definition.version.id
                $run = Invoke-Artifact $ctx $publication.artifactId $publication.sourceReferenceId
                $case = @{ parent = $parent; persistenceMode = $mode; outcome = $outcome;
                    definition = $definition; publication = $publication; workflowExecutionId = $run.workflowExecutionId }
                if ($outcome -ceq 'Cancel') {
                    $case.before = Wait-ControlNode $ctx $run.workflowExecutionId 'node-held' 'Suspended'
                    $accepted = Submit-AlterationPlan -Ctx $ctx -Target @{ executionIds = @($run.workflowExecutionId) } `
                        -Alterations @(@{ kind = 'CancelWorkflow'; schemaVersion = 1; payload = @{} })
                    $case.cancellation = (Wait-AlterationPlanTerminal $ctx $accepted.PlanId).Json
                    if ($case.cancellation.status -cne 'Completed') { throw 'The actual cancellation command did not settle.' }
                }
                $view = Wait-WorkflowInstance $ctx $run.workflowExecutionId -TimeoutSeconds 60
                $case.after = $view
                $receipt.cases += $case
                $expectedWorkflow = if ($outcome -ceq 'Cancel') { 'Cancelled' } elseif ($parent -ceq 'CompensationScope') { 'Completed' } else { 'Faulted' }
                if ($view.instance.status -cne $expectedWorkflow -or $view.persistenceMode -cne $mode -or
                    $view.instance.artifactId -cne $publication.artifactId -or @($view.activities | Where-Object executableNodeId -ceq 'after-bpmn').Count -ne 0) {
                    throw 'The actual native parent terminal result/pin/mode or blocked successor differs.'
                }
                $native = @($view.activities | Where-Object executableNodeId -ceq 'native-parent')
                $process = @($view.activities | Where-Object executableNodeId -ceq 'legacy-bpmn')
                if ($native.Count -ne 1 -or $process.Count -ne 1) { throw 'The actual native/BPMN invocations are not exact.' }
                if ($outcome -ceq 'Fault') {
                    $expectedParent = if ($parent -ceq 'Sequence') { 'Running' } elseif ($parent -ceq 'Flowchart') { 'Faulted' } else { 'Completed' }
                    if ($native[0].status -cne $expectedParent -or $process[0].status -cne 'Faulted' -or
                        ($parent -ceq 'CompensationScope' -and 'Compensated' -notin @($native[0].outcomeNames))) {
                        throw 'The existing composite-specific fault projection changed; do not normalize it under #3242.'
                    }
                } elseif ($process[0].status -cne 'Cancelled' -or @($view.activities | Where-Object status -ceq 'Suspended').Count -ne 0) {
                    throw 'The actual cancelled BPMN invocation retained a live wait or lost its cancellation.'
                }
            }
        }
    }
    $receipt.passed = $true
} catch {
    $receipt.failures += @{ type = $_.Exception.GetType().Name; message = $_.Exception.Message }
    throw
} finally {
    $receipt | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $EvidencePath
}
