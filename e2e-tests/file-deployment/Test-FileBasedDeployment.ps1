<#
.SYNOPSIS
    File-based workflow deployment at startup (spec 147 / #1157): mounted definition files are
    imported AND published — executable — when /health/ready turns 200, with zero API calls.
.DESCRIPTION
    Phase A (id resolution): starts its own plain server, resolves the WriteLine activity's
    actver_* id and authors a definition file (pinned definitionId) into a temp folder.
    Phase B: relaunches that server with JsonWorkflowReconciliation composed via env vars
    (SourceId + FolderPath + PublishOnReconcile=true), waits on /health/ready, then asserts:
    definition imported, active activation in the runtime slot, executable executes to completion.
    Phase C (idempotency, SC-002): restarts again with the folder unchanged and asserts the same
    active publication (no republish) and no duplicate definition.
    The script owns its server: an already-built Workbench on a free loopback port with a fresh temporary
    content root, stopped again at the end. -BaseUrl is the one place the server location is given: pass it
    to choose the address the owned server listens on (the port must be free; a process this script did
    not start is never stopped). Build Elsa.Workbench first (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl,
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)

. "$PSScriptRoot/_FileDeploymentCommon.ps1"

$BaseUrl = Resolve-ElsaServerBaseUrl -BaseUrl $BaseUrl

Write-Host "== E2E: file-based workflow deployment (spec 147) ==  -> $BaseUrl" -ForegroundColor Cyan

$stamp = Get-Date -Format "yyyyMMddHHmmss"
$definitionId = "wfdef-filedeploy-$stamp"
$workflowName = "e2e-file-deploy-$stamp"
$defsFolder = Join-Path ([System.IO.Path]::GetTempPath()) "elsa-filedeploy-defs-$stamp"
New-Item -ItemType Directory -Force $defsFolder | Out-Null
$contentRoot = $null
$completed = $false

try {
    # --- Phase A: resolve the activity id and author the definition file -----------------
    $contentRoot = New-ElsaContentRoot -Prefix 'elsa-filedeploy'
    Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $contentRoot -Environment (Get-FileDeploymentEnvironment)
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $writeLineId = $null
    Invoke-Step "resolve WriteLine actver id" {
        $script:writeLineId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
        Write-Host "  [defs] WriteLine = $script:writeLineId"
    }
    Write-DefinitionFile -Folder $defsFolder -FileName "orders.json" -DefinitionId $definitionId `
        -Name $workflowName -ActivityVersionId $script:writeLineId | Out-Null

    # --- Phase B: deploy from files at startup --------------------------------------------
    # Start-OwnedElsaServer returns once /health/ready is 200: the deployment gate (spec 147), which
    # includes the reconcile pass and publish-on-reconcile. A failed pass fails shell activation.
    Stop-OwnedElsaServer
    Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $contentRoot `
        -Environment (Get-FileDeploymentEnvironment -SourceId "e2e-mounted-definitions" -FolderPath $defsFolder)
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

    $definition = $null
    Invoke-Step "definition imported (GET /design/workflows/definitions?name=...)" {
        $list = Invoke-RestMethod "$BaseUrl/design/workflows/definitions?name=$workflowName" -WebSession $ctx.Session
        $script:definition = $list.items | Where-Object { $_.id -eq $definitionId } | Select-Object -First 1
    }
    Assert-That "definition '$workflowName' exists with the pinned id" ($null -ne $script:definition) "expected id $definitionId in the list response"

    # T117: activation slots are runtime-owned. The executable to execute comes from the runtime
    # listing instead of a publishing-owned publication join.
    $slots = $null
    Invoke-Step "activation slots (GET /runtime/workflows/activation-slots/{definitionId})" {
        $script:slots = Invoke-RestMethod "$BaseUrl/runtime/workflows/activation-slots/$definitionId" -WebSession $ctx.Session
    }
    $slot = $script:slots.items | Select-Object -First 1
    Assert-That "slot exists with a live activation" ($null -ne $slot -and $null -ne $slot.activeActivationId) ("slots: " + ($script:slots | ConvertTo-Json -Compress -Depth 5))
    $firstActivationId = $slot.activeActivationId

    $executable = $null
    Invoke-Step "executable for the definition (GET /runtime/workflows/executables)" {
        $list = Invoke-RestMethod "$BaseUrl/runtime/workflows/executables" -WebSession $ctx.Session
        $script:executable = $list.items | Where-Object { $_.definitionId -eq $definitionId } | Select-Object -First 1
    }
    Assert-That "a published executable exists for the definition" ($null -ne $script:executable -and $script:executable.artifactId) ("executables: " + ($script:executable | ConvertTo-Json -Compress -Depth 5))
    $sourceReferenceId = ($script:executable.references | Where-Object { $_.live } | Select-Object -First 1).sourceReferenceId

    if ($script:executable -and $script:executable.artifactId) {
        $run = $null
        Invoke-Step "execute (POST /runtime/workflows/executables/{artifactId}/execute)" {
            $script:run = Invoke-Artifact -Ctx $ctx -ArtifactId $script:executable.artifactId -SourceReferenceId $sourceReferenceId
        }
        $inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $script:run.workflowExecutionId
        Show-WorkflowInstance -Instance $inst
        Assert-That "execution completes" ($inst.instance.status -in @('Completed', 'Finished')) "status = $($inst.instance.status)"
    } else {
        Assert-That "execution completes" $false "no artifact to execute"
    }

    # --- Phase C: restart with unchanged files — idempotent (SC-002) ----------------------
    Restart-OwnedElsaServer
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

    Invoke-Step "slots after restart" {
        $script:slots = Invoke-RestMethod "$BaseUrl/runtime/workflows/activation-slots/$definitionId" -WebSession $ctx.Session
    }
    $slotAfter = $script:slots.items | Select-Object -First 1
    Assert-That "restart did not republish (same activeActivationId)" ($null -ne $slotAfter -and $slotAfter.activeActivationId -eq $firstActivationId) "before=$firstActivationId after=$($slotAfter.activeActivationId)"

    Invoke-Step "definitions after restart" {
        $list = Invoke-RestMethod "$BaseUrl/design/workflows/definitions?name=$workflowName" -WebSession $ctx.Session
        $script:matching = @($list.items | Where-Object { $_.name -eq $workflowName })
    }
    Assert-That "restart did not duplicate the definition" ($script:matching.Count -eq 1) "count = $($script:matching.Count)"
    $completed = $true
}
finally {
    # Stop the owned server and drop the temp folders; a failed run keeps the content root and its logs.
    Write-Host ""
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $completed -or $script:FPass -ne $script:FTotal)
    try { Remove-Item -Recurse -Force $defsFolder -ErrorAction SilentlyContinue } catch {}
}

Complete-FileDeploymentSuite
