<#
.SYNOPSIS
    Workflow template CRUD, revision conflicts, restart persistence and independent workflow creation.
.DESCRIPTION
    Owns a rebuilt Workbench and fresh SQLite on a free loopback port. Never stops another server.
#>
[CmdletBinding()]
param()
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"
$contentRoot = New-ElsaContentRoot -Prefix 'elsa-workflow-templates'
$baseUrl = Resolve-ElsaServerBaseUrl
$success = $false
try {
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot
    $ctx = Connect-Elsa -BaseUrl $baseUrl
    $sequence = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $writeLine = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    $child = New-ActivityNode -NodeId 'write' -VersionId $writeLine -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'from template'))
    $root = New-ActivityNode -NodeId 'root' -VersionId $sequence -Structure (New-SequenceStructure -Activities @($child))
    $state = @{ rootActivity = $root; variables = @(); inputs = @(); outputs = @() }
    $content = @{ state = $state; layout = @(@{ nodeId = 'write'; x = 120; y = 80 }); activityPresentation = @(@{ nodeId = 'write'; displayName = 'Template message'; description = 'retained label' }) }
    $request = @{ operationKey = 'template-create-proof'; name = 'Reusable starter'; description = 'Created through the real API'; content = $content }
    $template = Invoke-RestMethod "$baseUrl/design/workflows/templates" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body ($request | ConvertTo-Json -Depth 50)
    $duplicate = Invoke-RestMethod "$baseUrl/design/workflows/templates" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body ($request | ConvertTo-Json -Depth 50)
    if ($duplicate.id -ne $template.id) { throw 'Retrying the same create operation duplicated the template.' }
    $list = Invoke-RestMethod "$baseUrl/design/workflows/templates" -WebSession $ctx.Session
    if (@($list).Count -ne 1) { throw 'Expected one template in the catalog.' }
    Restart-OwnedElsaServer
    $ctx = Connect-Elsa -BaseUrl $baseUrl
    $reloaded = Invoke-RestMethod "$baseUrl/design/workflows/templates/$($template.id)" -WebSession $ctx.Session
    if ($reloaded.revision -ne $template.revision -or $reloaded.content.layout[0].x -ne 120 -or $reloaded.content.activityPresentation[0].displayName -ne 'Template message') { throw 'Template document or revision did not survive restart.' }
    $workflowRequest = @{ name = 'Independent workflow'; initialState = $reloaded.content.state; layout = $reloaded.content.layout; activityPresentation = $reloaded.content.activityPresentation }
    $workflow = Invoke-RestMethod "$baseUrl/design/workflows/definitions" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body ($workflowRequest | ConvertTo-Json -Depth 50)
    $update = @{ name = 'Updated starter'; description = 'Changed later'; content = $content; expectedRevision = $template.revision; operationKey = 'template-update-proof' }
    $updated = Invoke-RestMethod "$baseUrl/design/workflows/templates/$($template.id)" -Method Put -WebSession $ctx.Session -ContentType 'application/json' -Body ($update | ConvertTo-Json -Depth 50)
    if ($updated.revision -eq $template.revision) { throw 'Update did not advance the template revision.' }
    $update.operationKey = 'stale-template-update'
    try {
        Invoke-RestMethod "$baseUrl/design/workflows/templates/$($template.id)" -Method Put -WebSession $ctx.Session -ContentType 'application/json' -Body ($update | ConvertTo-Json -Depth 50) | Out-Null
        throw 'Stale edit was accepted.'
    } catch { if ([int]$_.Exception.Response.StatusCode -ne 409) { throw } }
    Invoke-RestMethod "$baseUrl/design/workflows/templates/$($template.id)/delete" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (@{ expectedRevision = $updated.revision; operationKey = 'template-delete-proof' } | ConvertTo-Json) | Out-Null
    $remaining = Invoke-RestMethod "$baseUrl/design/workflows/templates" -WebSession $ctx.Session
    if (@($remaining).Count -ne 0) { throw 'Template deletion did not remove the catalog entry.' }
    $independent = Invoke-RestMethod "$baseUrl/design/workflows/definitions/$($workflow.definition.id)" -WebSession $ctx.Session
    if ($independent.draft.state.rootActivity.structure.payload.activities[0].inputs[0].value.value -ne 'from template' -or $independent.draft.layout[0].x -ne 120 -or $independent.draft.activityPresentation[0].displayName -ne 'Template message') { throw 'Template edit/deletion changed the independently created workflow.' }
    Write-Host 'SUCCESS - template create/retry/read/edit/delete, stale refusal, SQLite restart, layout/labels and independent workflow copy.' -ForegroundColor Green
    $success = $true
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
}
