<#
.SYNOPSIS
    File-based workflow deployment at startup (spec 147 / #1157): mounted definition files are
    imported AND published — executable — when /health/ready turns 200, with zero API calls.
.DESCRIPTION
    Phase A (id resolution): against the normally-running server, resolves the WriteLine activity's
    actver_* id and authors a definition file (pinned definitionId) into a temp folder.
    Phase B: restarts the server with JsonWorkflowReconciliation composed via env vars
    (SourceId + FolderPath + PublishOnReconcile=true), waits on /health/ready, then asserts:
    definition imported, active publication in the slot, executable executes to completion.
    Phase C (idempotency, SC-002): restarts again with the folder unchanged and asserts the same
    active publication (no republish) and no duplicate definition.
    Cleanup restarts the server without the feature. Requires the server running from source
    (see ../README.md) and manages the server process like the durability suite.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)

. "$PSScriptRoot/_FileDeploymentCommon.ps1"

Write-Host "== E2E: file-based workflow deployment (spec 147) ==  -> $BaseUrl" -ForegroundColor Cyan

$stamp = Get-Date -Format "yyyyMMddHHmmss"
$definitionId = "wfdef-filedeploy-$stamp"
$workflowName = "e2e-file-deploy-$stamp"
$defsFolder = Join-Path ([System.IO.Path]::GetTempPath()) "elsa-filedeploy-defs-$stamp"
New-Item -ItemType Directory -Force $defsFolder | Out-Null

try {
    # --- Phase A: resolve the activity id and author the definition file -----------------
    if (-not (Get-ServerPid)) {
        Write-Host "  [server] nothing on 5095 - starting a plain server for phase A"
        Start-ElsaServer -Plain | Out-Null
    }
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $writeLineId = $null
    Invoke-Step "resolve WriteLine actver id" {
        $script:writeLineId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
        Write-Host "  [defs] WriteLine = $script:writeLineId"
    }
    Write-DefinitionFile -Folder $defsFolder -FileName "orders.json" -DefinitionId $definitionId `
        -Name $workflowName -ActivityVersionId $script:writeLineId | Out-Null

    # --- Phase B: deploy from files at startup --------------------------------------------
    Stop-ElsaServer
    Start-ElsaServer -SourceId "e2e-mounted-definitions" -FolderPath $defsFolder | Out-Null
    Wait-ElsaReady -BaseUrl $BaseUrl | Out-Null
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

    $definition = $null
    Invoke-Step "definition imported (GET /design/workflows/definitions?name=...)" {
        $list = Invoke-RestMethod "$BaseUrl/design/workflows/definitions?name=$workflowName" -WebSession $ctx.Session
        $script:definition = $list.items | Where-Object { $_.id -eq $definitionId } | Select-Object -First 1
    }
    Assert-That "definition '$workflowName' exists with the pinned id" ($null -ne $script:definition) "expected id $definitionId in the list response"

    $slots = $null
    Invoke-Step "publication slots (GET /publishing/workflows/{definitionId}/slots)" {
        $script:slots = Invoke-RestMethod "$BaseUrl/publishing/workflows/$definitionId/slots" -WebSession $ctx.Session
    }
    $slot = $script:slots.items | Select-Object -First 1
    Assert-That "slot exists with an active publication" ($null -ne $slot -and $null -ne $slot.activePublicationId) ("slots: " + ($script:slots | ConvertTo-Json -Compress -Depth 5))
    $publication = $slot.publication
    Assert-That "publication is Active with an artifactId" ($null -ne $publication -and "$($publication.status)" -eq "Active" -and $publication.artifactId) ("publication: " + ($publication | ConvertTo-Json -Compress -Depth 5))
    $firstPublicationId = $slot.activePublicationId

    if ($publication -and $publication.artifactId) {
        $run = $null
        Invoke-Step "execute (POST /runtime/workflows/executables/{artifactId}/execute)" {
            $script:run = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
        }
        $inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $script:run.workflowExecutionId
        Show-WorkflowInstance -Instance $inst
        Assert-That "execution completes" ($inst.instance.status -in @('Completed', 'Finished')) "status = $($inst.instance.status)"
    } else {
        Assert-That "execution completes" $false "no artifact to execute"
    }

    # --- Phase C: restart with unchanged files — idempotent (SC-002) ----------------------
    Stop-ElsaServer
    Start-ElsaServer -SourceId "e2e-mounted-definitions" -FolderPath $defsFolder | Out-Null
    Wait-ElsaReady -BaseUrl $BaseUrl | Out-Null
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

    Invoke-Step "slots after restart" {
        $script:slots = Invoke-RestMethod "$BaseUrl/publishing/workflows/$definitionId/slots" -WebSession $ctx.Session
    }
    $slotAfter = $script:slots.items | Select-Object -First 1
    Assert-That "restart did not republish (same activePublicationId)" ($null -ne $slotAfter -and $slotAfter.activePublicationId -eq $firstPublicationId) "before=$firstPublicationId after=$($slotAfter.activePublicationId)"

    Invoke-Step "definitions after restart" {
        $list = Invoke-RestMethod "$BaseUrl/design/workflows/definitions?name=$workflowName" -WebSession $ctx.Session
        $script:matching = @($list.items | Where-Object { $_.name -eq $workflowName })
    }
    Assert-That "restart did not duplicate the definition" ($script:matching.Count -eq 1) "count = $($script:matching.Count)"
}
finally {
    # Restore the developer's normal server (no feature composition) and drop the temp folder.
    Write-Host ""
    Write-Host "  [cleanup] restoring plain server ..." -ForegroundColor DarkGray
    try { Stop-ElsaServer } catch {}
    try { Start-ElsaServer -Plain | Out-Null } catch { Write-Host "  [cleanup] plain restart failed: $_" -ForegroundColor Yellow }
    try { Remove-Item -Recurse -Force $defsFolder -ErrorAction SilentlyContinue } catch {}
}

Complete-FileDeploymentSuite
