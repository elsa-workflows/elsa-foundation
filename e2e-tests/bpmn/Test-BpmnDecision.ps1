<#
.SYNOPSIS
    Publishes and executes BPMN Decisions with real catalog contracts and value bindings.
.DESCRIPTION
    Requires an already owned Workbench with BPMN and JavaScript composed. Never starts or stops a server.
    Covers named literal/expression outcomes, trimming, default routing and deterministic no-match failure.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BaseUrl,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [string] $EvidencePath,
    [string] $SourceRevision = 'unspecified'
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

$receipt = @{ sourceRevision = $SourceRevision; passed = $false; cases = @() }
try {
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $processVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Bpmn.Activities.BpmnProcess'
    $decisionVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Bpmn.Activities.BpmnDecision'
    $writeVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    $cases = @(
        @{ name = 'literal-high'; value = 'High'; syntax = 'Literal'; gateway = 'exclusiveGateway'; expected = 'high'; default = $true },
        @{ name = 'trimmed-low'; value = ' Low '; syntax = 'Literal'; gateway = 'exclusiveGateway'; expected = 'low'; default = $true },
        @{ name = 'javascript-high'; value = '2000 > 1000 ? "High" : "Low"'; syntax = 'JavaScript'; gateway = 'exclusiveGateway'; expected = 'high'; default = $true },
        @{ name = 'javascript-low'; value = '20 > 1000 ? "High" : "Low"'; syntax = 'JavaScript'; gateway = 'inclusiveGateway'; expected = 'low'; default = $true },
        @{ name = 'blank-default'; value = ''; syntax = 'Literal'; gateway = 'exclusiveGateway'; expected = 'fallback'; default = $true },
        @{ name = 'blank-no-default'; value = ''; syntax = 'Literal'; gateway = 'exclusiveGateway'; expected = ''; default = $false }
    )
    foreach ($case in $cases) {
        $inputValue = New-LiteralInput -ReferenceKey 'Outcome' -Value $case.value
        $inputValue.value.expressionType = $case.syntax
        $children = @((New-ActivityNode -NodeId 'decision' -VersionId $decisionVersion -Inputs @($inputValue)))
        $elements = @(@{ elementId = 'start'; elementType = 'startEvent' },
            @{ elementId = 'gateway'; elementType = $case.gateway; childNodeId = 'decision' })
        $flows = @(@{ flowId = 'enter'; sourceRef = 'start'; targetRef = 'gateway' })
        foreach ($branch in @('high', 'low', 'fallback')) {
            $children += New-ActivityNode -NodeId $branch -VersionId $writeVersion -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value "BPMN Decision $($case.name): $branch"))
            $elements += @{ elementId = "$branch-task"; elementType = 'task'; childNodeId = $branch }
            $elements += @{ elementId = "$branch-end"; elementType = 'endEvent' }
            $flows += @{ flowId = "$branch-end-flow"; sourceRef = "$branch-task"; targetRef = "$branch-end" }
            if ($branch -eq 'fallback') {
                if ($case.default) { $flows += @{ flowId = 'fallback-flow'; sourceRef = 'gateway'; targetRef = 'fallback-task'; isDefault = $true } }
            } else {
                $flows += @{ flowId = "$branch-flow"; sourceRef = 'gateway'; targetRef = "$branch-task"; conditionOutcome = $(if ($branch -eq 'high') { 'High' } else { 'Low' }) }
            }
        }
        # The no-default case has no fallback branch: avoid an unrelated unreachable-element rejection.
        if (-not $case.default) {
            $children = @($children | Where-Object nodeId -NE 'fallback')
            $elements = @($elements | Where-Object elementId -NotLike 'fallback-*')
            $flows = @($flows | Where-Object flowId -NotLike 'fallback-*')
        }
        $root = New-ActivityNode -NodeId 'bpmn-root' -VersionId $processVersion -Structure @{
            kind = 'elsa.bpmn.structure'; schemaVersion = '1.0.0'
            payload = @{ activities = $children; elements = $elements; sequenceFlows = $flows }
        }
        $definition = Submit-Workflow -Ctx $ctx -Name "BpmnDecision-$($case.name)-$([guid]::NewGuid().ToString('N'))" -RootActivity $root
        $published = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
        $run = Invoke-Artifact -Ctx $ctx -ArtifactId $published.artifactId -SourceReferenceId $published.sourceReferenceId
        $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 60
        $expectedStatus = if ($case.expected) { 'Completed' } else { 'Faulted' }
        if ($instance.instance.status -ne $expectedStatus) {
            Show-WorkflowInstance -Instance $instance
            throw "$($case.name): expected $expectedStatus, got $($instance.instance.status)."
        }
        foreach ($branch in @('high', 'low', 'fallback')) {
            $expectedCount = if ($branch -eq $case.expected) { 1 } else { 0 }
            if ((Get-NodeRunCount -Instance $instance -NodeId $branch) -ne $expectedCount) {
                throw "$($case.name): $branch did not run exactly $expectedCount times."
            }
        }
        if ($case.expected -and @($instance.incidents).Count -ne 0) { throw "$($case.name): unexpected incidents." }
        $receipt.cases += @{ name = $case.name; status = $instance.instance.status; executionId = $run.workflowExecutionId; expectedBranch = $case.expected }
        Write-Host "PASS: $($case.name) -> $expectedStatus $($case.expected)"
    }
    $receipt.passed = $true
} finally {
    if ($EvidencePath) { $receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $EvidencePath }
}
