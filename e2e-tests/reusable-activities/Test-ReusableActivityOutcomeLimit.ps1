<#
.SYNOPSIS
    Publishes a schema-2 reusable activity with multiple named boundary outcomes and proves a parent
    Flowchart runs only the matching branch.
.DESCRIPTION
    The reusable graph uses an If root whose emitted True/False outcomes are mapped to public
    Accepted/Declined boundary outcomes. Two executions cover both mappings. Requires the server
    running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Reusable-activity custom boundary outcomes ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$ifVersion = Invoke-Step "resolve If" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.If.Activities.If' }
$flowchartVersion = Invoke-Step "resolve Flowchart" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart' }
$writeLineVersion = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$contract = '{"contractSchemaVersion":"1","inputs":[],"outputs":[],"outcomes":[{"referenceKey":"accepted","name":"Accepted","isEmitted":true},{"referenceKey":"declined","name":"Declined","isEmitted":true}]}'
$outcomeMappings = '[{"sourceOutcomeReferenceKey":"True","boundaryOutcomeReferenceKey":"accepted"},{"sourceOutcomeReferenceKey":"False","boundaryOutcomeReferenceKey":"declined"}]'

function Invoke-ReusableOutcomeCase {
    param(
        [Parameter(Mandatory)][bool] $Condition,
        [Parameter(Mandatory)][string] $ExpectedNodeId,
        [Parameter(Mandatory)][string] $OtherNodeId
    )

    $token = Get-Random -Max 999999
    $conditionJson = if ($Condition) { "true" } else { "false" }
    $manifest = @"
{"variables":[],"rootActivity":{"nodeId":"graph-root","activityVersionId":"$ifVersion","inputs":[{"referenceKey":"Condition","value":{"value":$conditionJson,"expressionType":"Literal"},"autoEvaluate":null,"evaluatorType":null,"storageDriverType":null,"isSensitive":null}],"outputs":[],"structure":{"kind":"elsa.if.structure","schemaVersion":"1.0.0","payload":{"then":null,"else":null}}},"outputMappings":[],"outcomeMappings":$outcomeMappings}
"@
    $reusable = Publish-ReusableActivity `
        -Ctx $ctx `
        -DisplayName "BoundaryOutcomes-$token" `
        -PayloadJson $manifest `
        -ContractJson $contract `
        -SchemaVersion "2"

    $boundary = New-ActivityNode -NodeId "use-reusable" -VersionId $reusable.VersionId
    $accepted = New-ActivityNode -NodeId "accepted" -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey "text" -Value "ACCEPTED branch t=$token")
    )
    $declined = New-ActivityNode -NodeId "declined" -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey "text" -Value "DECLINED branch t=$token")
    )
    $structure = @{
        kind = "elsa.flowchart.structure"
        schemaVersion = "1.0.0"
        payload = @{
            startNodeId = "use-reusable"
            activities = @($boundary, $accepted, $declined)
            connections = @(
                @{ source = @{ nodeId = "use-reusable"; port = "Accepted" }; target = @{ nodeId = "accepted"; port = "Done" } },
                @{ source = @{ nodeId = "use-reusable"; port = "Declined" }; target = @{ nodeId = "declined"; port = "Done" } }
            )
        }
    }
    $root = New-ActivityNode -NodeId "root" -VersionId $flowchartVersion -Structure $structure
    $definition = Submit-Workflow -Ctx $ctx -Name "ReusableOutcome-$token" -RootActivity $root
    $publication = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId

    $expectedRan = (Get-NodeRunCount -Instance $instance -NodeId $ExpectedNodeId) -ge 1
    $otherRan = (Get-NodeRunCount -Instance $instance -NodeId $OtherNodeId) -ge 1
    if ($instance.instance.status -notin @("Completed", "Finished") -or -not $expectedRan -or $otherRan) {
        throw "Reusable outcome mismatch: status=$($instance.instance.status), expectedRan=$expectedRan, otherRan=$otherRan"
    }
}

Invoke-Step "True maps to Accepted" {
    Invoke-ReusableOutcomeCase -Condition $true -ExpectedNodeId "accepted" -OtherNodeId "declined"
}
Invoke-Step "False maps to Declined" {
    Invoke-ReusableOutcomeCase -Condition $false -ExpectedNodeId "declined" -OtherNodeId "accepted"
}

Write-Host ""
Write-Host "SUCCESS - both reusable boundary outcomes routed only their matching parent branch." -ForegroundColor Green
