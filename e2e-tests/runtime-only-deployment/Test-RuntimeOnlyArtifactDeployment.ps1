<#
.SYNOPSIS
    Spec 151's headline claim against a genuinely runtime-only engine: an executable artifact built on
    one server runs on another that has no design surface, no publishing surface and no compiler.
.DESCRIPTION
    Two Elsa.Foundation.Host processes, each composed entirely from a package feed this suite packs on
    every run. Neither is the developer's server: separate ports, separate databases, separate content
    roots, nothing in the repository is written to.

    Phase 0  Pack both feeds from source and build the CLR activity scan folder.
    Phase A  Instance A (design + publishing + runtime) authors a CHILD workflow and a PARENT that
             waits on a DispatchWorkflow of it, publishes both, and exports the parent's closure over
             the real endpoint GET publishing/workflows/{versionId}/executable-export. A v2 of the
             parent is authored, published and exported for phase D. Instance A is then stopped, so
             nothing that can compile a workflow is running for the rest of the suite.
    Phase B  Instance B (runtime + reconciliation + locking + persistence) starts over a mount holding
             the v1 closure. Everything asserted was true before the suite made a single call: the
             import happens during shell activation, which /health/ready gates.
    Phase C  Restart over the unchanged mount: same activation, no duplicate.
    Phase D  Drop the v2 closure in and restart: the newer ArtifactVersion supersedes (T095).
    Phase E  The claim itself: instance B cannot resolve a compiler. Asserted three ways - its feed
             carries no Design/Publishing package, its process loaded no Design/Publishing assembly,
             and the publishing and design routes that phase A used are absent from it.
    Phase F  The composition mistake the README and quickstart both warn about: the same engine with
             the locking feature removed must refuse to activate. Invisible in-process, because
             in-process tests always compose the lock themselves.

    HOW EXECUTION IS OBSERVED, AND WHY NOT THROUGH THE INSTANCE API. The runtime instance-detail
    endpoints exist on instance B and are reachable, but they gate on a trusted principal
    (HttpContextActivityExecutionInspectionAuthorizationContext denies an anonymous one and the
    handler answers 404), and ApiSecurity.AllowAnonymous does not make a principal trusted. Composing
    an identity stack purely to read a status would add a feature no assertion needs. So execution is
    observed where it actually happens: the WriteLine activities inside the imported artifact print
    per-run stamped markers to instance B's own stdout. That is stronger than a status field - it is
    the activity running - and it keeps instance B minimal, which is the whole point of the exercise.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File ./e2e-tests/runtime-only-deployment/Test-RuntimeOnlyArtifactDeployment.ps1
#>
[CmdletBinding()]
param(
    [int] $PublishPort = 5401,
    [int] $RuntimePort = 5402,
    [int] $LocklessPort = 5403,
    [switch] $SkipPack
)

. "$PSScriptRoot/_RuntimeOnlyDeploymentCommon.ps1"

Write-Host "== E2E: runtime-only artifact deployment (spec 151, T126) ==" -ForegroundColor Cyan

$stamp = Get-Date -Format "yyyyMMddHHmmss"
$sourceId = "e2e-runtime-only"
$childName = "e2e-ro-child-$stamp"
$parentName = "e2e-ro-parent-$stamp"
$publishBase = "http://localhost:$PublishPort"
$runtimeBase = "http://localhost:$RuntimePort"

# Feeds are reused across runs when -SkipPack is passed (iterating on the assertions); the default is
# to rebuild them, because a stale feed is how instance B would run old code and pass for the wrong reason.
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) "elsa-runtimeonly"
$stage = Join-Path $workRoot "packed"
$feedA = Join-Path $workRoot "feed-publish"
$feedA3 = Join-Path $workRoot "feed-publish-thirdparty"
$feedB = Join-Path $workRoot "feed-runtime"
$feedB3 = Join-Path $workRoot "feed-runtime-thirdparty"
$scanFolder = Join-Path $workRoot "activity-scan"

$runRoot = Join-Path $workRoot "run-$stamp"
$instanceA = Join-Path $runRoot "instance-a"
$instanceB = Join-Path $runRoot "instance-b"
$instanceLockless = Join-Path $runRoot "instance-b-lockless"
$mount = Join-Path $runRoot "mount"
$staging = Join-Path $runRoot "staging"
$databaseA = Join-Path $runRoot "publish-engine.db"
$databaseB = Join-Path $runRoot "runtime-engine.db"
$databaseLockless = Join-Path $runRoot "lockless-engine.db"
New-Item -ItemType Directory -Force $mount, $staging | Out-Null

$v1File = Join-Path $staging "closure-v1.json"
$v2File = Join-Path $staging "closure-v2.json"

$startedPorts = @()

try {
    # --- Phase 0: build both feeds from source ---------------------------------------------
    Write-Host ""
    Write-Host "-- Phase 0: pack the feeds (this is the slow part; ~100 projects, Release) --" -ForegroundColor Cyan
    if ($SkipPack -and (Test-Path $feedB)) {
        Write-Host "  [feed] -SkipPack: reusing the feeds from the previous run" -ForegroundColor Yellow
    }
    else {
        $runtimeProjects = Get-ProjectClosure -Roots $script:RuntimeOnlyProjects
        $publishProjects = Get-ProjectClosure -Roots $script:PublishCapableProjects
        $union = @($runtimeProjects + $publishProjects | Sort-Object -Unique)
        Write-Host ("  [feed] runtime-only closure: {0} projects; publish-capable closure: {1}; union to pack: {2}" -f `
            $runtimeProjects.Count, $publishProjects.Count, $union.Count)
        New-PackedStage -Stage $stage -Projects $union
        Copy-FeedSubset -Stage $stage -Feed $feedA -Projects $publishProjects
        Copy-FeedSubset -Stage $stage -Feed $feedB -Projects $runtimeProjects
        $thirdPartyA = New-ThirdPartyFeed -ElsaFeed $feedA -Feed $feedA3
        $thirdPartyB = New-ThirdPartyFeed -ElsaFeed $feedB -Feed $feedB3
        $scanned = New-ActivityScanFolder -Feed $feedA -ScanFolder $scanFolder
        Write-Host ("  [feed] publish-capable: {0} Elsa + {1} third-party; runtime-only: {2} Elsa + {3} third-party; {4} assemblies in the scan folder" -f `
            $publishProjects.Count, $thirdPartyA, $runtimeProjects.Count, $thirdPartyB, $scanned)
    }

    # --- Phase A: build the artifact on the publish-capable engine and export it -------------
    Write-Host ""
    Write-Host "-- Phase A: publish-capable engine (author, publish, export) --" -ForegroundColor Cyan
    New-InstanceRoot -Path $instanceA -ElsaFeed $feedA -ThirdPartyFeed $feedA3 `
        -Features (New-PublishCapableFeatures -DatabasePath $databaseA -ScanFolder $scanFolder) | Out-Null
    Start-FoundationHost -ContentRoot $instanceA -Port $PublishPort -Label "publish" | Out-Null
    $startedPorts += $PublishPort
    if (-not (Wait-FoundationReady -Port $PublishPort)) { Show-HostLogTail -Label "publish"; throw "The publish-capable engine never became ready on $PublishPort (log: %TEMP%\elsa-runtimeonly-publish.out.log)." }
    $ctxA = New-AnonymousContext -BaseUrl $publishBase

    # The design activity catalog is populated by ClrActivityReconciliation scanning the packed
    # activity assemblies; if that pass found nothing these three resolutions fail immediately.
    $writeLine = Invoke-Step "resolve WriteLine"        { Get-ActivityVersionId -Ctx $ctxA -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
    $sequence  = Invoke-Step "resolve Sequence"         { Get-ActivityVersionId -Ctx $ctxA -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
    $dispatch  = Invoke-Step "resolve DispatchWorkflow" { Get-ActivityVersionId -Ctx $ctxA -TypeKey 'Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow' }

    # The child is published first: publishing the parent pins the child by definition id, so the
    # child must already resolve to exactly one live Published artifact. That pinned edge is what
    # makes the exported closure more than one artifact.
    $childMarker = "CHILD-RAN-$stamp"
    $childRoot = New-ActivityNode -NodeId "child-write" -VersionId $writeLine -Inputs @(
        (New-LiteralInput -ReferenceKey "text" -Value $childMarker)
    )
    $childDef = Invoke-Step "submit child"  { Submit-Workflow -Ctx $ctxA -Name $childName -Description "spec 151 closure child" -RootActivity $childRoot }
    $childPub = Invoke-Step "publish child" { Publish-WorkflowVersion -Ctx $ctxA -VersionId $childDef.version.id }
    Write-Host ("  [build] child  definition={0} artifact={1}" -f $childDef.definition.id, $childPub.artifactId)

    function New-ParentRoot {
        param([string] $Marker)
        $dispatchNode = New-ActivityNode -NodeId "dispatch-child" -VersionId $dispatch -Inputs @(
            (New-LiteralInput -ReferenceKey "WorkflowDefinitionId" -Value $childDef.definition.id),
            (New-LiteralInput -ReferenceKey "WaitForCompletion"    -Value $true)
        )
        # This node runs only after the dispatched child reaches a terminal outcome, and it is the
        # last node in the sequence - so its marker in instance B's log means both "the child ran"
        # and "the parent ran to the end".
        $afterNode = New-ActivityNode -NodeId "parent-after" -VersionId $writeLine -Inputs @(
            (New-LiteralInput -ReferenceKey "text" -Value $Marker)
        )
        New-ActivityNode -NodeId "parent-root" -VersionId $sequence -Structure (New-SequenceStructure -Activities @($dispatchNode, $afterNode))
    }

    $parentMarkerV1 = "PARENT-V1-RESUMED-$stamp"
    $parentMarkerV2 = "PARENT-V2-RESUMED-$stamp"
    $parentDef = Invoke-Step "submit parent"  { Submit-Workflow -Ctx $ctxA -Name $parentName -Description "spec 151 closure root" -RootActivity (New-ParentRoot $parentMarkerV1) }
    $parentPub = Invoke-Step "publish parent" { Publish-WorkflowVersion -Ctx $ctxA -VersionId $parentDef.version.id }
    $parentDefinitionId = $parentDef.definition.id
    $childDefinitionId = $childDef.definition.id
    Write-Host ("  [build] parent definition={0} artifact={1}" -f $parentDefinitionId, $parentPub.artifactId)

    Invoke-Step "export v1 closure (GET publishing/workflows/{versionId}/executable-export)" {
        Invoke-WebRequest "$publishBase/publishing/workflows/$($parentDef.version.id)/executable-export" `
            -WebSession $ctxA.Session -OutFile $v1File -UseBasicParsing
    }
    $v1 = Get-Content $v1File -Raw | ConvertFrom-Json
    Write-Host ("  [build] v1 closure: formatVersion={0} root={1} artifacts={2} references={3}" -f `
        $v1.formatVersion, $v1.rootArtifactId, @($v1.artifacts).Count, @($v1.sourceReferences).Count)
    Assert-That "the exported closure carries the parent AND its pinned child (>= 2 artifacts)" (@($v1.artifacts).Count -ge 2) `
        ("artifact ids: " + ((@($v1.artifacts) | ForEach-Object { $_.identity.artifactId } | Sort-Object) -join ', '))

    # v2 of the same definition: draft-replace -> promote -> publish, then export. Supersession (T095)
    # is version-ordered, so v2 must carry a higher ArtifactVersion than v1.
    $haveV2 = $false
    $v2 = $null
    try {
        $v2state = @{ variables = @(); inputs = @(); outputs = @(); workflowActivityOptions = $null; strategyOptions = $null; rootActivity = (New-ParentRoot $parentMarkerV2) }
        Invoke-RestMethod "$publishBase/design/workflows/drafts/$($parentDef.draftId)" -Method Put -WebSession $ctxA.Session `
            -ContentType 'application/json' -Body (@{ state = $v2state } | ConvertTo-Json -Depth 40) | Out-Null
        $promoted = Invoke-RestMethod "$publishBase/design/workflows/drafts/$($parentDef.draftId)/promote" -Method Post `
            -WebSession $ctxA.Session -ContentType 'application/json' -Body '{}'
        $v2VersionId = if ($promoted.id) { $promoted.id } else { $promoted.version.id }
        Invoke-Step "publish parent v2" { Publish-WorkflowVersion -Ctx $ctxA -VersionId $v2VersionId } | Out-Null
        Invoke-WebRequest "$publishBase/publishing/workflows/$v2VersionId/executable-export" -WebSession $ctxA.Session -OutFile $v2File -UseBasicParsing
        $v2 = Get-Content $v2File -Raw | ConvertFrom-Json
        $haveV2 = $true
        Write-Host ("  [build] v2 closure: root={0} artifacts={1}" -f $v2.rootArtifactId, @($v2.artifacts).Count)
    }
    catch {
        # Authoring a v2 over REST is a design-side path this suite only consumes. If it refuses,
        # phase D cannot run; that is a design-side gap, not a spec 151 regression, so it is reported
        # rather than silently skipped.
        $status = "?"; try { $status = [int]$_.Exception.Response.StatusCode } catch {}
        Write-Host ("  [build] v2 authoring failed (HTTP {0}) - phase D will be reported as not run: {1}" -f $status, $_.Exception.Message) -ForegroundColor Yellow
    }

    $v1RootArtifactId = $v1.rootArtifactId
    $v1ArtifactVersion = (@($v1.artifacts) | Where-Object { $_.identity.artifactId -eq $v1RootArtifactId } | Select-Object -First 1).identity.artifactVersion

    # Nothing that can compile a workflow runs from here on.
    Stop-FoundationHost -Port $PublishPort
    $startedPorts = @($startedPorts | Where-Object { $_ -ne $PublishPort })

    # --- Phase B: import into the runtime-only engine ----------------------------------------
    Write-Host ""
    Write-Host "-- Phase B: runtime-only engine (import at startup, zero API calls) --" -ForegroundColor Cyan
    Copy-Item $v1File (Join-Path $mount "parent-v1.json")
    New-InstanceRoot -Path $instanceB -ElsaFeed $feedB -ThirdPartyFeed $feedB3 `
        -Features (New-RuntimeOnlyFeatures -DatabasePath $databaseB -MountPath $mount -SourceId $sourceId) | Out-Null
    Start-FoundationHost -ContentRoot $instanceB -Port $RuntimePort -Label "runtime-b" | Out-Null
    $startedPorts += $RuntimePort
    if (-not (Wait-FoundationReady -Port $RuntimePort)) { Show-HostLogTail -Label "runtime-b"; throw "The runtime-only engine never became ready on $RuntimePort." }
    Show-HostDiagnostics -Label "runtime-b"
    $ctxB = New-AnonymousContext -BaseUrl $runtimeBase

    $importLine = @(Get-HostLog -Label "runtime-b" | Select-String -Pattern 'Workflow artifact reconciliation completed' | Select-Object -Last 1 -ExpandProperty Line)
    Assert-That "the mounted closure was imported during shell activation, before any API call" `
        (($importLine -join ' ') -match '2 imported, 0 already current, 0 skipped, 0 rejected') `
        ("reconcile line: " + ($importLine -join ' '))

    function Get-ActivationSlot {
        param([Parameter(Mandatory)][string] $DefinitionId)
        $slots = Invoke-RestMethod "$runtimeBase/runtime/workflows/activation-slots/$DefinitionId" -WebSession $ctxB.Session
        return $slots.items | Select-Object -First 1
    }

    $parentSlot = $null; $childSlot = $null
    Invoke-Step "activation slots (GET /runtime/workflows/activation-slots/{definitionId})" {
        $script:parentSlot = Get-ActivationSlot -DefinitionId $parentDefinitionId
        $script:childSlot = Get-ActivationSlot -DefinitionId $childDefinitionId
    }
    Assert-That "the imported parent claimed its activation slot" `
        ($null -ne $script:parentSlot -and $null -ne $script:parentSlot.activeActivationId -and $script:parentSlot.sourceId -eq $sourceId) `
        ("parent slot: " + ($script:parentSlot | ConvertTo-Json -Compress -Depth 5))
    Assert-That "the closure's pinned child claimed its activation slot too" `
        ($null -ne $script:childSlot -and $null -ne $script:childSlot.activeActivationId -and $script:childSlot.sourceId -eq $sourceId) `
        ("child slot: " + ($script:childSlot | ConvertTo-Json -Compress -Depth 5))
    $firstActivationId = $script:parentSlot.activeActivationId

    function Get-Executable {
        param([Parameter(Mandatory)][string] $DefinitionId, [string] $ArtifactId)
        $list = Invoke-RestMethod "$runtimeBase/runtime/workflows/executables" -WebSession $ctxB.Session
        return $list.items | Where-Object { $_.definitionId -eq $DefinitionId -and (-not $ArtifactId -or $_.artifactId -eq $ArtifactId) } | Select-Object -First 1
    }

    $parentExec = $null
    Invoke-Step "executables on the importing engine (GET /runtime/workflows/executables)" {
        $script:parentExec = Get-Executable -DefinitionId $parentDefinitionId
    }
    Assert-That "the imported artifact is executable and matches the exported root id" `
        ($null -ne $script:parentExec -and $script:parentExec.artifactId -eq $v1RootArtifactId) `
        ("exported root=$v1RootArtifactId, imported=" + $script:parentExec.artifactId)

    # Execution evidence comes from the host log, so each run reads only the lines its own dispatch
    # produced. See the note in the file header for why not the instance API.
    function Invoke-ImportedArtifact {
        param([Parameter(Mandatory)] $Executable, [Parameter(Mandatory)][string] $Label,
              [Parameter(Mandatory)][string[]] $ExpectMarkers, [int] $TimeoutSeconds = 60)
        $before = @(Get-HostLog -Label $Label).Count
        $liveRef = ($Executable.references | Where-Object { $_.live } | Select-Object -First 1).sourceReferenceId
        $run = Invoke-Artifact -Ctx $ctxB -ArtifactId $Executable.artifactId -SourceReferenceId $liveRef
        Write-Host ("  [run] executionId={0} dispatch={1}" -f $run.workflowExecutionId, $run.commandDispatchStatus)
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $seen = @{}
        while ((Get-Date) -lt $deadline -and $seen.Count -lt $ExpectMarkers.Count) {
            Start-Sleep -Milliseconds 500
            $fresh = @(Get-HostLog -Label $Label | Select-Object -Skip $before)
            foreach ($marker in $ExpectMarkers) { if ($fresh -match [regex]::Escape($marker)) { $seen[$marker] = $true } }
        }
        return [pscustomobject]@{ Run = $run; Seen = $seen }
    }

    if ($script:parentExec) {
        $result = Invoke-ImportedArtifact -Executable $script:parentExec -Label "runtime-b" -ExpectMarkers @($childMarker, $parentMarkerV1)
        Assert-That "the runtime-only engine accepted the execution command" ($result.Run.commandDispatchStatus -eq 'Accepted') `
            ("dispatch status = " + $result.Run.commandDispatchStatus)
        Assert-That "the pinned CHILD from the closure ran on the runtime-only engine" ($result.Seen.ContainsKey($childMarker)) `
            ("expected '$childMarker' in the runtime engine's own output")
        Assert-That "the parent resumed after the child and ran to its last node" ($result.Seen.ContainsKey($parentMarkerV1)) `
            ("expected '$parentMarkerV1' - the node after a WaitForCompletion dispatch, and the last in the sequence")
    }
    else {
        Assert-That "the runtime-only engine accepted the execution command" $false "no imported executable to run"
        Assert-That "the pinned CHILD from the closure ran on the runtime-only engine" $false "no imported executable to run"
        Assert-That "the parent resumed after the child and ran to its last node" $false "no imported executable to run"
    }

    # --- Phase C: restart over the unchanged mount - idempotent -------------------------------
    Write-Host ""
    Write-Host "-- Phase C: restart over the unchanged mount (idempotency) --" -ForegroundColor Cyan
    Stop-FoundationHost -Port $RuntimePort
    Start-FoundationHost -ContentRoot $instanceB -Port $RuntimePort -Label "runtime-c" | Out-Null
    if (-not (Wait-FoundationReady -Port $RuntimePort)) { Show-HostLogTail -Label "runtime-c"; throw "The runtime-only engine never became ready after the idempotency restart." }
    Show-HostDiagnostics -Label "runtime-c"
    $ctxB = New-AnonymousContext -BaseUrl $runtimeBase

    $slotAfter = $null; $execCount = 0
    Invoke-Step "slots and executables after the idempotent restart" {
        $script:slotAfter = Get-ActivationSlot -DefinitionId $parentDefinitionId
        $list = Invoke-RestMethod "$runtimeBase/runtime/workflows/executables" -WebSession $ctxB.Session
        $script:execCount = @($list.items | Where-Object { $_.definitionId -eq $parentDefinitionId }).Count
    }
    Assert-That "re-importing the unchanged mount kept the same activation" `
        ($null -ne $script:slotAfter -and $script:slotAfter.activeActivationId -eq $firstActivationId) `
        ("before=$firstActivationId after=" + $script:slotAfter.activeActivationId)
    Assert-That "re-importing the unchanged mount created no duplicate executable" ($script:execCount -eq 1) `
        ("executables for the definition = " + $script:execCount)

    # --- Phase D: a newer ArtifactVersion supersedes (T095, latest-wins) ----------------------
    Write-Host ""
    Write-Host "-- Phase D: newer ArtifactVersion supersedes (latest-wins) --" -ForegroundColor Cyan
    if ($haveV2) {
        Stop-FoundationHost -Port $RuntimePort
        Copy-Item $v2File (Join-Path $mount "parent-v2.json")
        Start-FoundationHost -ContentRoot $instanceB -Port $RuntimePort -Label "runtime-d" | Out-Null
        if (-not (Wait-FoundationReady -Port $RuntimePort)) { Show-HostLogTail -Label "runtime-d"; throw "The runtime-only engine never became ready after the v2 rollout." }
        Show-HostDiagnostics -Label "runtime-d"
        $ctxB = New-AnonymousContext -BaseUrl $runtimeBase

        $v2RootArtifactId = $v2.rootArtifactId
        $v2ArtifactVersion = (@($v2.artifacts) | Where-Object { $_.identity.artifactId -eq $v2RootArtifactId } | Select-Object -First 1).identity.artifactVersion
        Write-Host ("  [rollout] v1 artifactVersion={0} -> v2 artifactVersion={1}" -f $v1ArtifactVersion, $v2ArtifactVersion)

        $slotV2 = $null; $liveExec = $null
        Invoke-Step "slot and live executable after the v2 rollout" {
            $script:slotV2 = Get-ActivationSlot -DefinitionId $parentDefinitionId
            $script:liveExec = Get-Executable -DefinitionId $parentDefinitionId -ArtifactId $v2RootArtifactId
        }
        Assert-That "the v2 closure superseded v1 (activation replaced)" `
            ($null -ne $script:slotV2 -and $null -ne $script:slotV2.activeActivationId -and $script:slotV2.activeActivationId -ne $firstActivationId) `
            ("v1=$firstActivationId now=" + $script:slotV2.activeActivationId)
        Assert-That "the v2 artifact is the one now serving the definition" ($null -ne $script:liveExec) `
            ("expected artifact $v2RootArtifactId in the executables listing")
        if ($script:liveExec) {
            $resultV2 = Invoke-ImportedArtifact -Executable $script:liveExec -Label "runtime-d" -ExpectMarkers @($parentMarkerV2)
            Assert-That "the superseding v2 artifact executes on the runtime-only engine" ($resultV2.Seen.ContainsKey($parentMarkerV2)) `
                ("expected '$parentMarkerV2' in the runtime engine's own output")
        }
        else {
            Assert-That "the superseding v2 artifact executes on the runtime-only engine" $false "no v2 executable to run"
        }
    }
    else {
        Assert-That "the v2 closure superseded v1 (activation replaced)" $false "phase A could not author a v2 over REST - see the warning above"
        Assert-That "the v2 artifact is the one now serving the definition" $false "phase A could not author a v2 over REST"
        Assert-That "the superseding v2 artifact executes on the runtime-only engine" $false "phase A could not author a v2 over REST"
    }

    # --- Phase E: the engine that just did all of that cannot compile a workflow --------------
    Write-Host ""
    Write-Host "-- Phase E: the runtime-only engine cannot resolve a compiler --" -ForegroundColor Cyan

    # E1 - the feed IS the feature surface of this host, so a feed with no Design/Publishing package
    # is an engine that could not compose one even if its shells.json asked.
    $feedPackages = @(Get-ChildItem $feedB -Filter *.nupkg | ForEach-Object { [regex]::Match($_.BaseName, '^(?<id>.+?)\.\d').Groups['id'].Value })
    $forbiddenInFeed = @($feedPackages | Where-Object { $id = $_; @($script:ForbiddenPrefixes | Where-Object { $id.StartsWith($_, [StringComparison]::Ordinal) }).Count -gt 0 })
    Assert-That "the runtime-only feed contains no Design or Publishing package" ($forbiddenInFeed.Count -eq 0) `
        ("found: " + ($forbiddenInFeed -join ', '))
    Write-Host ("  [closure] runtime-only feed = {0} Elsa packages, none matching {1}" -f $feedPackages.Count, ($script:ForbiddenPrefixes -join '|'))

    # E2 - what the process actually loaded, not what it could have. Nuplane logs one line per package
    # it loads into the shell context; a guard that found nothing to inspect would be vacuous, so the
    # count is asserted too.
    $loaded = @(Get-HostLog -Label "runtime-d" | Select-String -Pattern 'Loaded package (?<id>[^@]+)@' |
        ForEach-Object { $_.Matches[0].Groups['id'].Value } | Sort-Object -Unique)
    if ($loaded.Count -eq 0) { $loaded = @(Get-HostLog -Label "runtime-b" | Select-String -Pattern 'Loaded package (?<id>[^@]+)@' | ForEach-Object { $_.Matches[0].Groups['id'].Value } | Sort-Object -Unique) }
    $forbiddenLoaded = @($loaded | Where-Object { $id = $_; @($script:ForbiddenPrefixes | Where-Object { $id.StartsWith($_, [StringComparison]::Ordinal) }).Count -gt 0 })
    Assert-That "the running engine loaded no Design or Publishing assembly" `
        ($loaded.Count -gt 15 -and $forbiddenLoaded.Count -eq 0) `
        ("loaded packages = " + $loaded.Count + "; forbidden = " + ($forbiddenLoaded -join ', '))

    # E3 - behaviour, not structure: the two routes phase A used to compile and export are simply not
    # there, while the runtime route the imported artifact is served from is.
    $publishStatus = Get-HttpStatus -Url "$runtimeBase/publishing/workflows/$($parentDef.version.id)/publish" -Method Post -Body '{}'
    $exportStatus = Get-HttpStatus -Url "$runtimeBase/publishing/workflows/$($parentDef.version.id)/executable-export"
    $designStatus = Get-HttpStatus -Url "$runtimeBase/design/workflows/definitions?name=$parentName"
    $runtimeStatus = Get-HttpStatus -Url "$runtimeBase/runtime/workflows/executables"
    Assert-That "the publish route that produced this artifact does not exist on the importing engine" ($publishStatus -eq 404) `
        ("POST publishing/workflows/{versionId}/publish -> $publishStatus")
    Assert-That "the executable-export route does not exist on the importing engine" ($exportStatus -eq 404) `
        ("GET publishing/workflows/{versionId}/executable-export -> $exportStatus")
    Assert-That "the design catalog route does not exist on the importing engine" ($designStatus -eq 404) `
        ("GET design/workflows/definitions -> $designStatus")
    Assert-That "the runtime route the artifact is served from does exist (the probe above is not vacuous)" ($runtimeStatus -eq 200) `
        ("GET runtime/workflows/executables -> $runtimeStatus")

    # --- Phase F: the same engine without a locking feature must refuse to start ---------------
    Write-Host ""
    Write-Host "-- Phase F: composing the reconciler without a lock must fail at startup --" -ForegroundColor Cyan
    Stop-FoundationHost -Port $RuntimePort
    $startedPorts = @($startedPorts | Where-Object { $_ -ne $RuntimePort })
    New-InstanceRoot -Path $instanceLockless -ElsaFeed $feedB -ThirdPartyFeed $feedB3 `
        -Features (New-RuntimeOnlyFeatures -DatabasePath $databaseLockless -MountPath $mount -SourceId $sourceId -WithoutLocking) | Out-Null
    Start-FoundationHost -ContentRoot $instanceLockless -Port $LocklessPort -Label "lockless" | Out-Null
    $startedPorts += $LocklessPort
    # A shorter budget on purpose: this host must never become ready, and the whole suite would
    # otherwise wait out the full activation timeout to learn nothing.
    $locklessReady = Wait-FoundationReady -Port $LocklessPort -TimeoutSec 90
    $locklessLog = @(Get-HostLog -Label "lockless") -join "`n"
    Assert-That "the same engine without a locking feature never becomes ready" (-not $locklessReady) `
        "/health/ready returned 200 on a composition that has no IDistributedLockProvider"
    Assert-That "and it says why: IDistributedLockProvider cannot be resolved" `
        ($locklessLog -match 'Unable to resolve service for type ''Elsa\.Locking\.Core\.IDistributedLockProvider''') `
        "the host log does not name the missing lock provider"
    $locklessHealth = Get-HttpStatus -Url "http://localhost:$LocklessPort/health/ready"
    Assert-That "its shell stays inactive (503 from /health/ready)" ($locklessHealth -eq 503) `
        ("/health/ready -> $locklessHealth")
}
finally {
    Write-Host ""
    Write-Host "  [cleanup] stopping every host this suite started ..." -ForegroundColor DarkGray
    foreach ($port in @($PublishPort, $RuntimePort, $LocklessPort)) { try { Stop-FoundationHost -Port $port } catch {} }
    # The feeds and the packed stage are deliberately kept: they are large, rebuilt on the next run
    # anyway, and useful when a failure needs explaining. The per-run directory goes.
    try { Remove-Tree $runRoot } catch {}
}

Complete-RuntimeOnlySuite
