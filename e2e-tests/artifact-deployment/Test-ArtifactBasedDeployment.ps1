<#
.SYNOPSIS
    Executable-artifact deployment across engines (spec 151 / #1304): a closure exported from a
    publish-capable engine is imported and activated at startup by an engine that never saw the
    source definitions, with zero API calls.
.DESCRIPTION
    Phase A (build engine): against the normally-running server, publishes a CHILD workflow and a
    PARENT that waits on a DispatchWorkflow of it, then exports the parent's closure through the real
    endpoint - GET publishing/workflows/{versionId}/executable-export. A v2 of the parent is authored,
    published and exported too, and staged for phase D.

    Phase B (import engine): restarts the server with JsonWorkflowArtifactReconciliation composed via
    env vars over a mount holding the v1 closure, AND against a freshly schema-deployed empty database.
    The empty store is what makes the claim testable: importing back into the store the workflow was
    published from is an idempotent no-op. Asserts the importing engine's design catalog holds neither
    definition, that both the parent's and the child's activation slots are claimed, and that the
    parent executes to completion - including the child, which is the portability claim.

    Phase C (idempotency): restarts over the unchanged mount; same activation, no duplicate.
    Phase D (latest-wins, T095): drops the v2 closure into the mount, restarts, asserts v2 supersedes.

    Cleanup restarts the developer's plain server on its own database and removes the mount and the
    temp database. Requires the server running from source (see ../README.md) and manages the server
    process like the durability and file-deployment suites.

    NOT PROVEN HERE: a genuinely runtime-only composition. Elsa.Workbench composes design, publishing
    and the compiler in one shell, and this suite restarts that same host - so what is proven is
    "an engine whose store never held these definitions", not "an engine with no design surface".
    See README.md.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File ./e2e-tests/artifact-deployment/Test-ArtifactBasedDeployment.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)

. "$PSScriptRoot/_ArtifactDeploymentCommon.ps1"

Write-Host "== E2E: executable-artifact deployment (spec 151) ==  -> $BaseUrl" -ForegroundColor Cyan

$stamp = Get-Date -Format "yyyyMMddHHmmss"
$sourceId = "e2e-artifact-drop"
$childName = "e2e-artifact-child-$stamp"
$parentName = "e2e-artifact-parent-$stamp"
$tempRoot = [System.IO.Path]::GetTempPath()
$mount = Join-Path $tempRoot "elsa-artifactdeploy-mount-$stamp"
$staging = Join-Path $tempRoot "elsa-artifactdeploy-staging-$stamp"
$importDb = Join-Path $tempRoot "elsa-artifactdeploy-import-$stamp.db"
New-Item -ItemType Directory -Force $mount | Out-Null
New-Item -ItemType Directory -Force $staging | Out-Null

$v1File = Join-Path $staging "closure-v1.json"
$v2File = Join-Path $staging "closure-v2.json"

try {
    # --- Phase A: build the artifact on the publish-capable engine and export it ---------------
    Write-Host ""
    Write-Host "-- Phase A: build engine (publish + export) --" -ForegroundColor Cyan
    # Always the suite's own server on the suite's own store, never whatever happens to be running.
    # This used to reuse a server already on 5095, which made the run depend on the developer's local
    # database: one applied by an older build fails schema admission, the shell never activates, and
    # the failure looks like a defect in this feature rather than a stale local file. A suite that
    # means different things on two machines is not evidence.
    if (Get-ServerPid) {
        Write-Host "  [server] stopping the server already on 5095 - this suite brings its own"
        Stop-ElsaServer
    }
    Start-ElsaServer -Plain -LogLabel "phase-a" | Out-Null
    Wait-ElsaReady -BaseUrl $BaseUrl | Out-Null
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

    $writeLine = Invoke-Step "resolve WriteLine"        { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
    $sequence  = Invoke-Step "resolve Sequence"         { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
    $dispatch  = Invoke-Step "resolve DispatchWorkflow" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow' }

    # The child is published first: publishing the parent pins the child by definition id, so the
    # child must resolve to exactly one live Published artifact. That pinned edge is what makes the
    # exported closure more than one artifact.
    $childRoot = New-ActivityNode -NodeId "child-write" -VersionId $writeLine -Inputs @(
        (New-LiteralInput -ReferenceKey "text" -Value "CHILD ran from an imported artifact. stamp=$stamp")
    )
    $childDef = Invoke-Step "submit child"  { Submit-Workflow -Ctx $ctx -Name $childName -Description "spec 151 closure child" -RootActivity $childRoot }
    $childPub = Invoke-Step "publish child" { Publish-WorkflowVersion -Ctx $ctx -VersionId $childDef.version.id }
    Write-Host ("  [build] child  definition={0} artifact={1}" -f $childDef.definition.id, $childPub.artifactId)

    function New-ParentRoot {
        param([string] $Marker)
        $dispatchNode = New-ActivityNode -NodeId "dispatch-child" -VersionId $dispatch -Inputs @(
            (New-LiteralInput -ReferenceKey "WorkflowDefinitionId" -Value $childDef.definition.id),
            (New-LiteralInput -ReferenceKey "WaitForCompletion"    -Value $true)
        )
        $afterNode = New-ActivityNode -NodeId "parent-after" -VersionId $writeLine -Inputs @(
            (New-LiteralInput -ReferenceKey "text" -Value "PARENT ($Marker) resumed after the child. stamp=$stamp")
        )
        New-ActivityNode -NodeId "parent-root" -VersionId $sequence -Structure (New-SequenceStructure -Activities @($dispatchNode, $afterNode))
    }

    $parentDef = Invoke-Step "submit parent"  { Submit-Workflow -Ctx $ctx -Name $parentName -Description "spec 151 closure root" -RootActivity (New-ParentRoot "v1") }
    $parentPub = Invoke-Step "publish parent" { Publish-WorkflowVersion -Ctx $ctx -VersionId $parentDef.version.id }
    $parentDefinitionId = $parentDef.definition.id
    $childDefinitionId = $childDef.definition.id
    Write-Host ("  [build] parent definition={0} artifact={1}" -f $parentDefinitionId, $parentPub.artifactId)

    Invoke-Step "export v1 closure (GET publishing/workflows/{versionId}/executable-export)" {
        Invoke-WebRequest "$BaseUrl/publishing/workflows/$($parentDef.version.id)/executable-export" `
            -WebSession $ctx.Session -OutFile $v1File -UseBasicParsing
    }
    $v1 = Get-Content $v1File -Raw | ConvertFrom-Json
    Write-Host ("  [build] v1 closure: formatVersion={0} root={1} artifacts={2} references={3}" -f `
        $v1.formatVersion, $v1.rootArtifactId, @($v1.artifacts).Count, @($v1.sourceReferences).Count)
    Assert-That "exported closure carries the parent AND its pinned child (>= 2 artifacts)" (@($v1.artifacts).Count -ge 2) `
        ("artifact ids: " + (@($v1.artifacts) | ForEach-Object { $_.identity.artifactId } | Sort-Object) -join ', ')

    # v2 of the same definition: draft-replace -> promote -> publish, then export. Supersession
    # (T095) is version-ordered, so v2 must carry a higher ArtifactVersion than v1.
    $haveV2 = $false
    $v2state = @{ variables = @(); inputs = @(); outputs = @(); workflowActivityOptions = $null; strategyOptions = $null; rootActivity = (New-ParentRoot "v2") }
    try {
        Invoke-RestMethod "$BaseUrl/design/workflows/drafts/$($parentDef.draftId)" -Method Put -WebSession $ctx.Session `
            -ContentType 'application/json' -Body (@{ state = $v2state } | ConvertTo-Json -Depth 40) | Out-Null
        $promoted = Invoke-RestMethod "$BaseUrl/design/workflows/drafts/$($parentDef.draftId)/promote" -Method Post `
            -WebSession $ctx.Session -ContentType 'application/json' -Body '{}'
        $v2VersionId = if ($promoted.id) { $promoted.id } else { $promoted.version.id }
        $parentPub2 = Invoke-Step "publish parent v2" { Publish-WorkflowVersion -Ctx $ctx -VersionId $v2VersionId }
        Invoke-WebRequest "$BaseUrl/publishing/workflows/$v2VersionId/executable-export" -WebSession $ctx.Session -OutFile $v2File -UseBasicParsing
        $v2 = Get-Content $v2File -Raw | ConvertFrom-Json
        $haveV2 = $true
        Write-Host ("  [build] v2 closure: root={0} artifacts={1} artifact={2}" -f $v2.rootArtifactId, @($v2.artifacts).Count, $parentPub2.artifactId)
    } catch {
        # Authoring a v2 over REST (draft PUT -> promote) is a design-side path this suite only
        # consumes. If it refuses, phase D cannot run; that is a design-side gap, not a spec 151
        # regression, so it is reported rather than silently skipped.
        $status = "?"; try { $status = [int]$_.Exception.Response.StatusCode } catch {}
        Write-Host ("  [build] v2 authoring failed (HTTP {0}) - phase D (latest-wins) will be reported as not run: {1}" -f $status, $_.Exception.Message) -ForegroundColor Yellow
    }

    $v1RootArtifactId = $v1.rootArtifactId
    $v1ChildArtifactId = (@($v1.artifacts) | Where-Object { $_.identity.artifactId -ne $v1RootArtifactId } | Select-Object -First 1).identity.artifactId
    $v1ArtifactVersion = (@($v1.artifacts) | Where-Object { $_.identity.artifactId -eq $v1RootArtifactId } | Select-Object -First 1).identity.artifactVersion

    # --- Phase B: import into an engine whose store never held these definitions ----------------
    Write-Host ""
    Write-Host "-- Phase B: import engine (empty store + mounted closure) --" -ForegroundColor Cyan
    Stop-ElsaServer
    New-ImportEngineDatabase -DatabasePath $importDb | Out-Null
    Copy-Item $v1File (Join-Path $mount "parent-v1.json")
    Start-ElsaServer -SourceId $sourceId -FolderPath $mount -DatabasePath $importDb -LogLabel "import-b" | Out-Null
    Wait-ElsaReady -BaseUrl $BaseUrl | Out-Null
    Show-ReconcileDiagnostics
    # Everything asserted below was true BEFORE this session made a single call: the import happened
    # during shell activation, which /health/ready gates. The reads here observe it, they do not cause it.
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

    Invoke-Step "design catalog on the importing engine" {
        $script:designHits = @()
        foreach ($name in @($parentName, $childName)) {
            $list = Invoke-RestMethod "$BaseUrl/design/workflows/definitions?name=$name" -WebSession $ctx.Session
            $script:designHits += @($list.items | Where-Object { $_.name -eq $name })
        }
    }
    Assert-That "the importing engine never saw the source definitions (design catalog empty)" ($script:designHits.Count -eq 0) `
        ("found: " + (($script:designHits | ForEach-Object { $_.name }) -join ', '))

    function Get-ActivationSlot {
        param([Parameter(Mandatory)][string] $DefinitionId)
        $slots = Invoke-RestMethod "$BaseUrl/runtime/workflows/activation-slots/$DefinitionId" -WebSession $ctx.Session
        return $slots.items | Select-Object -First 1
    }

    $parentSlot = $null; $childSlot = $null
    Invoke-Step "activation slots (GET /runtime/workflows/activation-slots/{definitionId})" {
        $script:parentSlot = Get-ActivationSlot -DefinitionId $parentDefinitionId
        $script:childSlot = Get-ActivationSlot -DefinitionId $childDefinitionId
    }
    Assert-That "the imported parent claimed its activation slot" ($null -ne $script:parentSlot -and $null -ne $script:parentSlot.activeActivationId) `
        ("parent slot: " + ($script:parentSlot | ConvertTo-Json -Compress -Depth 5))
    Assert-That "the closure's child claimed its activation slot too" ($null -ne $script:childSlot -and $null -ne $script:childSlot.activeActivationId) `
        ("child slot: " + ($script:childSlot | ConvertTo-Json -Compress -Depth 5))
    $firstActivationId = $script:parentSlot.activeActivationId

    function Get-Executable {
        param([Parameter(Mandatory)][string] $DefinitionId)
        $list = Invoke-RestMethod "$BaseUrl/runtime/workflows/executables" -WebSession $ctx.Session
        return $list.items | Where-Object { $_.definitionId -eq $DefinitionId } | Select-Object -First 1
    }

    $parentExec = $null
    Invoke-Step "executables on the importing engine (GET /runtime/workflows/executables)" {
        $script:parentExec = Get-Executable -DefinitionId $parentDefinitionId
    }
    Assert-That "the imported artifact is executable and matches the exported root id" `
        ($null -ne $script:parentExec -and $script:parentExec.artifactId -eq $v1RootArtifactId) `
        ("exported root=$v1RootArtifactId, imported=" + $script:parentExec.artifactId)

    function Invoke-ImportedParent {
        param([Parameter(Mandatory)] $Executable)
        $liveRef = ($Executable.references | Where-Object { $_.live } | Select-Object -First 1).sourceReferenceId
        $run = Invoke-Artifact -Ctx $ctx -ArtifactId $Executable.artifactId -SourceReferenceId $liveRef
        return Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 30
    }

    if ($script:parentExec) {
        $inst = $null
        Invoke-Step "execute the imported artifact" { $script:inst = Invoke-ImportedParent -Executable $script:parentExec }
        Show-WorkflowInstance -Instance $script:inst
        Assert-That "the imported workflow executes to completion" ($script:inst.instance.status -in @('Completed', 'Finished')) `
            ("status = " + $script:inst.instance.status)
        $dispatchOutcome = ($script:inst.activities | Where-Object { $_.executableNodeId -eq 'dispatch-child' }).outcomeNames -join ','
        Assert-That "the pinned CHILD from the closure ran (waited dispatch completed)" ($dispatchOutcome -match 'Completed') `
            ("dispatch outcome = '$dispatchOutcome' - the parent resumes only on the child's terminal outcome")
        Assert-That "the parent continued past the dispatch on the importing engine" `
            (@($script:inst.activities | Where-Object { $_.executableNodeId -eq 'parent-after' -and $_.status -eq 'Completed' }).Count -eq 1) `
            ("activities: " + (($script:inst.activities | ForEach-Object { "$($_.executableNodeId)=$($_.status)" }) -join ' '))
    } else {
        Assert-That "the imported workflow executes to completion" $false "no imported executable to run"
        Assert-That "the pinned CHILD from the closure ran (waited dispatch completed)" $false "no imported executable to run"
        Assert-That "the parent continued past the dispatch on the importing engine" $false "no imported executable to run"
    }

    # --- Phase C: restart over the unchanged mount - idempotent ---------------------------------
    Write-Host ""
    Write-Host "-- Phase C: restart over the unchanged mount (idempotency) --" -ForegroundColor Cyan
    Stop-ElsaServer
    Start-ElsaServer -SourceId $sourceId -FolderPath $mount -DatabasePath $importDb -LogLabel "import-c" | Out-Null
    Wait-ElsaReady -BaseUrl $BaseUrl | Out-Null
    Show-ReconcileDiagnostics
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

    $slotAfter = $null; $execCount = 0
    Invoke-Step "slots and executables after the idempotent restart" {
        $script:slotAfter = Get-ActivationSlot -DefinitionId $parentDefinitionId
        $list = Invoke-RestMethod "$BaseUrl/runtime/workflows/executables" -WebSession $ctx.Session
        $script:execCount = @($list.items | Where-Object { $_.definitionId -eq $parentDefinitionId }).Count
    }
    Assert-That "re-importing the unchanged mount kept the same activation" `
        ($null -ne $script:slotAfter -and $script:slotAfter.activeActivationId -eq $firstActivationId) `
        ("before=$firstActivationId after=" + $script:slotAfter.activeActivationId)
    Assert-That "re-importing the unchanged mount created no duplicate executable" ($script:execCount -eq 1) `
        ("executables for the definition = " + $script:execCount)

    # --- Phase D: a newer ArtifactVersion supersedes (T095, latest-wins) ------------------------
    Write-Host ""
    Write-Host "-- Phase D: newer ArtifactVersion supersedes (latest-wins) --" -ForegroundColor Cyan
    if ($haveV2) {
        Stop-ElsaServer
        Copy-Item $v2File (Join-Path $mount "parent-v2.json")
        Start-ElsaServer -SourceId $sourceId -FolderPath $mount -DatabasePath $importDb -LogLabel "import-d" | Out-Null
        Wait-ElsaReady -BaseUrl $BaseUrl | Out-Null
        Show-ReconcileDiagnostics
        $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

        $v2RootArtifactId = $v2.rootArtifactId
        $v2ArtifactVersion = (@($v2.artifacts) | Where-Object { $_.identity.artifactId -eq $v2RootArtifactId } | Select-Object -First 1).identity.artifactVersion
        Write-Host ("  [rollout] v1 artifactVersion={0} -> v2 artifactVersion={1}" -f $v1ArtifactVersion, $v2ArtifactVersion)

        $slotV2 = $null; $liveExec = $null
        Invoke-Step "slot and live executable after the v2 rollout" {
            $script:slotV2 = Get-ActivationSlot -DefinitionId $parentDefinitionId
            $list = Invoke-RestMethod "$BaseUrl/runtime/workflows/executables" -WebSession $ctx.Session
            $script:liveExec = @($list.items | Where-Object { $_.definitionId -eq $parentDefinitionId -and $_.artifactId -eq $v2RootArtifactId }) | Select-Object -First 1
        }
        Assert-That "the v2 closure superseded v1 (activation replaced)" `
            ($null -ne $script:slotV2 -and $null -ne $script:slotV2.activeActivationId -and $script:slotV2.activeActivationId -ne $firstActivationId) `
            ("v1=$firstActivationId now=" + $script:slotV2.activeActivationId)
        Assert-That "the v2 artifact is the one now serving the definition" ($null -ne $script:liveExec) `
            ("expected artifact $v2RootArtifactId in the executables listing")
        if ($script:liveExec) {
            $instV2 = $null
            Invoke-Step "execute the superseding v2 artifact" { $script:instV2 = Invoke-ImportedParent -Executable $script:liveExec }
            Show-WorkflowInstance -Instance $script:instV2
            Assert-That "the superseding v2 artifact executes to completion" ($script:instV2.instance.status -in @('Completed', 'Finished')) `
                ("status = " + $script:instV2.instance.status)
        } else {
            Assert-That "the superseding v2 artifact executes to completion" $false "no v2 executable to run"
        }
    } else {
        Assert-That "the v2 closure superseded v1 (activation replaced)" $false "phase A could not author a v2 over REST - see the warning above"
        Assert-That "the v2 artifact is the one now serving the definition" $false "phase A could not author a v2 over REST"
        Assert-That "the superseding v2 artifact executes to completion" $false "phase A could not author a v2 over REST"
    }
}
finally {
    # Restore the developer's normal server (no feature composition, stock database) and drop the
    # temp mount, staging folder and importing-engine database.
    Write-Host ""
    Write-Host "  [cleanup] restoring the plain server ..." -ForegroundColor DarkGray
    try { Stop-ElsaServer } catch {}
    # -StockDatabase, so the developer is handed back their own server and not the suite's scratch store.
    try { Start-ElsaServer -Plain -StockDatabase -LogLabel "cleanup" | Out-Null } catch { Write-Host "  [cleanup] plain restart failed: $_" -ForegroundColor Yellow }
    foreach ($path in @($mount, $staging)) { try { Remove-Item -Recurse -Force $path -ErrorAction SilentlyContinue } catch {} }
    try { Remove-Item -Force "$importDb*" -ErrorAction SilentlyContinue } catch {}
    try { if ($script:PublishingDbPath) { Remove-Item -Force "$($script:PublishingDbPath)*" -ErrorAction SilentlyContinue } } catch {}
}

Complete-ArtifactDeploymentSuite
