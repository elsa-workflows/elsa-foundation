<#
.SYNOPSIS
    Publish a real User Task Flowchart; validate its form, complete each action and prove matching branch/data once.
.DESCRIPTION
    Uses the supplied server only. It creates uniquely named disposable workflows/forms, never stops a host,
    and exercises the real Identity, Design, Publishing, Runtime, journal, recovery pump and HTTP task API.
    -LeaveOpen writes a receipt for a signed-in Studio browser journey instead of completing the first run.
#>
[CmdletBinding()]
param([string] $BaseUrl = 'http://localhost:5095', [string] $Username = 'admin', [string] $Password = 'Password123!',
      [switch] $LeaveOpen, [string] $ReceiptPath)
. "$PSScriptRoot/../_ElsaCommon.ps1"
$ErrorActionPreference = 'Stop'
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$actor = Invoke-RestMethod "$BaseUrl/runtime/user-task-identity" -WebSession $ctx.Session
$taskVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.CreateUserTask'
$flowVersion = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart'
$unique = [Guid]::NewGuid().ToString('N')
$form = @{ id = "purchase-$unique"; version = '1'; name = 'Purchase response'; provider = 'elsa.basic-forms/1'; fields = @(
    @{ key = 'AccountID'; label = 'Account'; kind = 'text'; required = $true; readOnly = $true },
    @{ key = 'AmountUSD'; label = 'Amount'; kind = 'number'; required = $true; minimum = 1; maximum = 10000 },
    @{ key = 'Confirmed'; label = 'Confirmed'; kind = 'boolean'; required = $true },
    @{ key = 'Note'; label = 'Note'; kind = 'multiline'; maxLength = 100 }
) }
Invoke-RestMethod "$BaseUrl/runtime/user-task-forms" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body ($form | ConvertTo-Json -Depth 15) | Out-Null
$actions = @('Approve', 'Reject', 'Request More Info')
$inputs = @(
    # Keep an unbroken reference long enough to expose clipping in the narrow Studio journey.
    (New-LiteralInput -ReferenceKey Title -Value "Purchase approval $unique$unique"),
    (New-LiteralInput -ReferenceKey AssigneeId -Value $actor.userId),
    (New-LiteralInput -ReferenceKey Actions -Value $actions),
    (New-LiteralInput -ReferenceKey OptionalFormActions -Value @('Reject')),
    (New-LiteralInput -ReferenceKey FormId -Value $form.id),
    (New-LiteralInput -ReferenceKey FormVersion -Value $form.version),
    (New-LiteralInput -ReferenceKey Prefill -Value @{ AccountID = 'A-107' })
)
$task = New-ActivityNode -NodeId 'human-work' -VersionId $taskVersion -Inputs $inputs -Outputs @(
    @{ referenceKey = 'Data'; value = @{ expressionType = 'Variable'; value = @{ referenceKey = 'task-data'; declaringScopeId = 'workflow' } } }
)
$children = @($task); $connections = @()
for ($index = 0; $index -lt $actions.Count; $index++) {
    $nodeId = "branch-$index"
    $children += New-SetOutputNode -NodeId $nodeId -OutputName Branch -Value $actions[$index]
    $connections += @{ id = "outcome-$index"; source = @{ nodeId = 'human-work'; port = $actions[$index] }; target = @{ nodeId = $nodeId } }
    $connections += @{ id = "data-$index"; source = @{ nodeId = $nodeId; port = 'Done' }; target = @{ nodeId = 'response-data' } }
}
$children += New-SetOutputNode -NodeId 'response-data' -OutputName ResponseData -ValueAlias JsonElement -Value @{ expressionType = 'Variable'; value = @{ referenceKey = 'task-data'; declaringScopeId = 'workflow' } }
$root = New-ActivityNode -NodeId 'approval-flow' -VersionId $flowVersion -Structure @{ kind = 'elsa.flowchart.structure'; schemaVersion = '1.0.0'; payload = @{
    activities = $children; connections = $connections; startNodeId = 'human-work'; nodeMetadata = @{}; connectionMetadata = @{};
    variables = @((New-VariableDef -Key 'task-data' -Alias JsonElement -Default @{}))
} }
$definition = Submit-Workflow -Ctx $ctx -Name "User Tasks delivery $unique" -RootActivity $root -Variables @((New-VariableDef -Key 'task-data' -Alias JsonElement -Default @{}))
$published = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
foreach ($action in $actions) {
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $published.artifactId -SourceReferenceId $published.sourceReferenceId
    $deadline = (Get-Date).AddSeconds(30); $item = $null
    do {
        $page = Invoke-RestMethod "$BaseUrl/runtime/user-tasks?view=mine&limit=100" -WebSession $ctx.Session
        $item = $page.items | Where-Object workflowExecutionId -eq $run.workflowExecutionId | Select-Object -First 1
        if (-not $item) { Start-Sleep -Milliseconds 250 }
    } while (-not $item -and (Get-Date) -lt $deadline)
    if (-not $item) { throw "No open task for run $($run.workflowExecutionId)." }
    if ($item.status -ne 'Open') { throw "Expected Open, got $($item.status)." }
    if ($LeaveOpen) {
        if (-not $ReceiptPath) { throw '-LeaveOpen requires -ReceiptPath.' }
        @{ taskId = $item.id; executionId = $run.workflowExecutionId; definitionId = $definition.definition.id; artifactId = $published.artifactId; sourceReferenceId = $published.sourceReferenceId; formId = $form.id; userId = $actor.userId; baseUrl = $BaseUrl; title = $item.title } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ReceiptPath
        Write-Host "READY - Studio task $($item.id), run $($run.workflowExecutionId)" -ForegroundColor Green
        return
    }
    try {
        Invoke-RestMethod "$BaseUrl/runtime/user-tasks/$($item.id)/complete" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (@{ revision = $item.revision; requestId = "invalid-$unique"; action = 'Approve'; data = @{ AccountID = 'A-107' } } | ConvertTo-Json -Depth 10) | Out-Null
        throw 'Missing required response was accepted.'
    } catch { if (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 400) { throw } }
    $request = @{ revision = $item.revision; requestId = "$unique-$action"; action = $action; data = @{ AccountID = 'A-107'; AmountUSD = 42; Confirmed = $false; Note = 'Case preserved' } }
    $accepted = Invoke-RestMethod "$BaseUrl/runtime/user-tasks/$($item.id)/complete" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body ($request | ConvertTo-Json -Depth 10)
    $replay = Invoke-RestMethod "$BaseUrl/runtime/user-tasks/$($item.id)/complete" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body ($request | ConvertTo-Json -Depth 10)
    if ($accepted.decision.response.completionId -ne $replay.decision.response.completionId) { throw 'Replay changed the accepted completion.' }
    $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 30
    if ($instance.instance.status -ne 'Completed') { Show-WorkflowInstance -Instance $instance; throw 'Workflow did not complete.' }
    if (@($instance.activities | Where-Object { $_.executableNodeId -like 'branch-*' -and $_.status -eq 'Completed' }).Count -ne 1) { throw 'More or fewer than one action branch executed.' }
    $selected = $actions.IndexOf($action)
    if (@($instance.activities | Where-Object { $_.executableNodeId -eq "branch-$selected" -and $_.status -eq 'Completed' }).Count -ne 1) { throw 'The wrong action branch executed.' }
    $detail = Invoke-RestMethod "$BaseUrl/runtime/user-tasks/$($item.id)" -WebSession $ctx.Session
    if ($detail.status -ne 'Completed') { throw "Journal did not acknowledge canonical result: $($detail.status)." }
    if (@($detail.history | Where-Object operation -eq accepted).Count -ne 1 -or @($detail.history | Where-Object operation -eq completed).Count -ne 1) { throw 'History contains duplicate/missing acceptance or completion.' }
    $response = $instance.outputs.ResponseData.value
    $encoded = $response | ConvertTo-Json -Depth 20 -Compress
    if ($encoded -cnotmatch 'AccountID' -or $encoded -cnotmatch 'AmountUSD' -or $encoded -cnotmatch 'Case preserved') { throw "Submitted data did not reach workflow output: $encoded" }
    Write-Host "PASS - $action reached exactly its branch with form data; duplicate response reused its receipt." -ForegroundColor Green
}
Write-Host 'SUCCESS - User Task authoring/publication, required/readonly form validation, all action paths, form data and duplicate delivery.' -ForegroundColor Green
