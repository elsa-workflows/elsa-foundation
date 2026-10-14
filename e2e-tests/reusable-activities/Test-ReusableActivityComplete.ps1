<#
.SYNOPSIS
    Complete (spec 237): a Complete node deep inside a reusable activity's Flowchart body ends the reusable
    activity with the chosen boundary outcome, and invalid placements are rejected at publication.
.DESCRIPTION
    The reusable activity declares boundary outcomes succeed/fail and has NO outcome mappings: its Flowchart body
    routes an If to Complete(succeed) or Complete(fail), so both outcomes are reached only through Complete nodes.
    A parent Flowchart connects each boundary outcome to its own WriteLine. Two executions cover both outcomes and
    assert only the matching parent branch runs. Two negative cases assert that publishing Complete(unknown) is
    refused with activity.graph.complete.outcome-unknown and that publishing a plain workflow containing Complete
    is refused with Graph/CompleteOutsideGraph. Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_ReusableCommon.ps1"

Write-Host "== Reusable activity ended by a Complete node ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$completeVersion = Invoke-Step "resolve Complete" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Graph.Runtime.Activities.Complete' }
$ifVersion = Invoke-Step "resolve If" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.If.Activities.If' }
$flowchartVersion = Invoke-Step "resolve Flowchart" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart' }
$writeLineVersion = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$contract = '{"contractSchemaVersion":"1","inputs":[],"outputs":[],"outcomes":[{"referenceKey":"succeed","name":"Succeeded","isEmitted":true},{"referenceKey":"fail","name":"Failed","isEmitted":true}]}'

function New-CompleteNodeJson {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $Outcome)
    return "{`"nodeId`":`"$NodeId`",`"activityVersionId`":`"$completeVersion`",`"inputs`":[{`"referenceKey`":`"outcome`",`"value`":{`"value`":`"$Outcome`",`"expressionType`":`"Literal`"},`"autoEvaluate`":null,`"evaluatorType`":null,`"storageDriverType`":null,`"isSensitive`":null}],`"outputs`":[],`"structure`":null}"
}

# Flowchart body: decide (If) --True--> Complete(succeed), --False--> Complete(failOutcome). No outcome mappings.
function New-CompleteManifestJson {
    param([Parameter(Mandatory)][bool] $Condition, [string] $FailOutcome = "fail")
    $conditionJson = if ($Condition) { "true" } else { "false" }
    $decide = "{`"nodeId`":`"decide`",`"activityVersionId`":`"$ifVersion`",`"inputs`":[{`"referenceKey`":`"Condition`",`"value`":{`"value`":$conditionJson,`"expressionType`":`"Literal`"},`"autoEvaluate`":null,`"evaluatorType`":null,`"storageDriverType`":null,`"isSensitive`":null}],`"outputs`":[],`"structure`":{`"kind`":`"elsa.if.structure`",`"schemaVersion`":`"1.0.0`",`"payload`":{`"then`":null,`"else`":null}}}"
    $succeed = New-CompleteNodeJson -NodeId "exit-succeed" -Outcome "succeed"
    $fail = New-CompleteNodeJson -NodeId "exit-fail" -Outcome $FailOutcome
    $connections = '[{"source":{"nodeId":"decide","port":"True"},"target":{"nodeId":"exit-succeed","port":"Done"}},{"source":{"nodeId":"decide","port":"False"},"target":{"nodeId":"exit-fail","port":"Done"}}]'
    return "{`"variables`":[],`"rootActivity`":{`"nodeId`":`"graph-root`",`"activityVersionId`":`"$flowchartVersion`",`"inputs`":[],`"outputs`":[],`"structure`":{`"kind`":`"elsa.flowchart.structure`",`"schemaVersion`":`"1.0.0`",`"payload`":{`"startNodeId`":`"decide`",`"activities`":[$decide,$succeed,$fail],`"connections`":$connections}}},`"outputMappings`":[],`"outcomeMappings`":[]}"
}

function Invoke-CompleteCase {
    param(
        [Parameter(Mandatory)][bool] $Condition,
        [Parameter(Mandatory)][string] $ExpectedNodeId,
        [Parameter(Mandatory)][string] $OtherNodeId
    )

    $token = Get-Random -Max 999999
    $reusable = Publish-ReusableActivity `
        -Ctx $ctx `
        -DisplayName "CompleteExit-$token" `
        -PayloadJson (New-CompleteManifestJson -Condition $Condition) `
        -ContractJson $contract `
        -SchemaVersion "2"

    $boundary = New-ActivityNode -NodeId "use-reusable" -VersionId $reusable.VersionId
    $succeeded = New-ActivityNode -NodeId "on-succeeded" -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey "text" -Value "SUCCEEDED branch t=$token")
    )
    $failed = New-ActivityNode -NodeId "on-failed" -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey "text" -Value "FAILED branch t=$token")
    )
    $structure = @{
        kind = "elsa.flowchart.structure"
        schemaVersion = "1.0.0"
        payload = @{
            startNodeId = "use-reusable"
            activities = @($boundary, $succeeded, $failed)
            connections = @(
                @{ source = @{ nodeId = "use-reusable"; port = "Succeeded" }; target = @{ nodeId = "on-succeeded"; port = "Done" } },
                @{ source = @{ nodeId = "use-reusable"; port = "Failed" }; target = @{ nodeId = "on-failed"; port = "Done" } }
            )
        }
    }
    $root = New-ActivityNode -NodeId "root" -VersionId $flowchartVersion -Structure $structure
    $definition = Submit-Workflow -Ctx $ctx -Name "CompleteExit-$token" -RootActivity $root
    $publication = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId

    $expectedRan = (Get-NodeRunCount -Instance $instance -NodeId $ExpectedNodeId) -ge 1
    $otherRan = (Get-NodeRunCount -Instance $instance -NodeId $OtherNodeId) -ge 1
    if ($instance.instance.status -notin @("Completed", "Finished") -or -not $expectedRan -or $otherRan) {
        throw "Complete outcome mismatch: status=$($instance.instance.status), expectedRan=$expectedRan, otherRan=$otherRan"
    }
}

Invoke-Step "Complete(succeed) routes Succeeded" {
    Invoke-CompleteCase -Condition $true -ExpectedNodeId "on-succeeded" -OtherNodeId "on-failed"
}
Invoke-Step "Complete(fail) routes Failed" {
    Invoke-CompleteCase -Condition $false -ExpectedNodeId "on-failed" -OtherNodeId "on-succeeded"
}

Invoke-Step "Complete(unknown) is refused at publication" {
    $token = Get-Random -Max 999999
    $refused = $false
    try {
        Publish-ReusableActivity `
            -Ctx $ctx `
            -DisplayName "CompleteUnknown-$token" `
            -PayloadJson (New-CompleteManifestJson -Condition $true -FailOutcome "unknown") `
            -ContractJson $contract `
            -SchemaVersion "2" | Out-Null
    }
    catch {
        if ("$($_.Exception.Message) $($_.ErrorDetails.Message)" -notlike "*activity.graph.complete.outcome-unknown*") { throw }
        $refused = $true
    }
    if (-not $refused) { throw "A reusable activity with Complete(unknown) was published." }
}

# The headless submit endpoint creates a version without running draft validators (as for every IDraftValidator,
# e.g. Switch duplicate cases), so the rule is asserted where it is enforced: the submitted draft's validation
# errors, which also block promoting that draft. An unenclosed Complete that still runs faults at run time.
Invoke-Step "Complete in a plain workflow fails draft validation" {
    $token = Get-Random -Max 999999
    $exit = New-ActivityNode -NodeId "exit" -VersionId $completeVersion -Inputs @(
        (New-LiteralInput -ReferenceKey "outcome" -Value "fail")
    )
    $structure = @{
        kind = "elsa.flowchart.structure"
        schemaVersion = "1.0.0"
        payload = @{ startNodeId = "exit"; activities = @($exit); connections = @() }
    }
    $root = New-ActivityNode -NodeId "root" -VersionId $flowchartVersion -Structure $structure
    $definition = Submit-Workflow -Ctx $ctx -Name "CompleteOutsideGraph-$token" -RootActivity $root
    $validations = Invoke-RestMethod "$($ctx.BaseUrl)/design/workflows/drafts/$($definition.draftId)/validations" -WebSession $ctx.Session
    $errors = @($validations.errors | Where-Object { $_.code -eq "Graph/CompleteOutsideGraph" -and $_.nodeId -eq "exit" })
    if ($errors.Count -ne 1) {
        throw "Expected one Graph/CompleteOutsideGraph error on node 'exit'; got: $($validations.errors | ConvertTo-Json -Compress)"
    }
}

Write-Host ""
Write-Host "SUCCESS - Complete ended the reusable activity with the chosen outcome, and invalid placements were refused." -ForegroundColor Green
