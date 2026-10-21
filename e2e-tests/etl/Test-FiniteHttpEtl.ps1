<#
.SYNOPSIS
    Runs finite HTTP/CSV ETL pipelines through Workbench APIs and native workflows.
.DESCRIPTION
    Uses an owned Workbench process and an owned loopback HTTP fixture. It publishes HTTP-to-HTTP,
    HTTP-to-JSONL, and CSV-to-HTTP v2 definitions, executes each from Sequence and Flowchart parents,
    observes a durable 429 retry while the other source shard advances, and cancels a delayed retry
    through the real runtime API. Build Workbench with the opt-in ETL module references before running.
#>
[CmdletBinding()]
param(
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)

. "$PSScriptRoot/_EtlFixture.ps1"
. "$PSScriptRoot/_EtlRestHelpers.ps1"

function New-FiniteConnectorPipeline {
    param(
        [Parameter(Mandatory)][ValidateSet('http', 'csv')][string] $SourceKind,
        [Parameter(Mandatory)][string] $SourceBinding,
        [Parameter(Mandatory)][ValidateSet('http', 'jsonl')][string] $MainKind,
        [Parameter(Mandatory)][string] $MainBinding
    )

    $source = if ($SourceKind -eq 'http') {
        [ordered]@{
            kind = 'httpJson'
            binding = $SourceBinding
            recordsPointer = '/items'
            fields = @(
                @{ name = 'id'; pointer = '/id'; type = 'int64'; required = $true },
                @{ name = 'name'; pointer = '/name'; type = 'string'; required = $true }
            )
            pagination = [ordered]@{ kind = 'token'; continuationPointer = '/next'; queryParameter = 'pageToken' }
        }
    } else {
        [ordered]@{
            kind = 'csv'
            binding = $SourceBinding
            schema = @(
                @{ name = 'id'; type = 'int64'; required = $true },
                @{ name = 'name'; type = 'string'; required = $true }
            )
        }
    }

    return @{
        source = $source
        lookup = $null
        routes = @{
            main = [ordered]@{ kind = $MainKind; binding = $MainBinding }
            errors = [ordered]@{ kind = 'jsonl'; binding = 'errors' }
        }
        mapping = @(
            @{ name = 'id'; origin = 'source'; field = 'id' },
            @{ name = 'name'; origin = 'source'; field = 'name' }
        )
        filters = @()
        limits = @{
            filePartitionCount = 2; maxPartitions = 8; maxRecordBytes = 65536; maxFields = 64
            maxBatchRecords = 100; maxBatchBytes = 4194304; maxOutstandingRecords = 100
            maxOutstandingBytes = 4194304; maxLookupRecords = 100; maxLookupBytes = 4194304
            maxOutputBatchBytes = 4194304; maxRetainedOutputBytes = 16777216
            maxSnapshotBytes = 16777216; maxArtifactBytes = 262144
        }
        http = @{
            maxPageBytes = 65536; maxPageRecords = 100; maxJsonDepth = 16; maxContinuationBytes = 8192
            maxPagesPerPartition = 4; maxRetainedInputBytes = 4194304; maxRetainedMetadataBytes = 4194304
            maxAttempts = 3; deadlineSeconds = 300; requestTimeoutSeconds = 10
            initialDelaySeconds = 1; maxDelaySeconds = 60
        }
    }
}

function Publish-FiniteConnectorPipeline {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)][string] $DefinitionId,
        [Parameter(Mandatory)] $Pipeline,
        [Parameter(Mandatory)] $ExpectedBindings
    )
    $available = Invoke-EtlRequest -Context $Context -Method Get -Path '/etl/v2/bindings?limit=200'
    foreach ($required in $ExpectedBindings) {
        if (-not @($available.items | Where-Object {
            $_.alias -eq $required.Alias -and $_.role -eq $required.Role
        }).Count) {
            throw "Workbench did not expose host binding '$($required.Alias)' with role '$($required.Role)'."
        }
    }

    $draft = Invoke-EtlRequest -Context $Context -Method Post -Path '/etl/v2/definitions' -Body @{
        id = $DefinitionId; pipeline = $Pipeline
    }
    $validation = Invoke-EtlRequest -Context $Context -Method Post -Path "/etl/v2/definitions/$DefinitionId/validate" -Body @{
        expectedRevision = $draft.revision
    }
    if (@($validation.diagnostics).Count -ne 0 -or [string]::IsNullOrWhiteSpace($validation.contentHash)) {
        throw "V2 ETL validation failed: $($validation.diagnostics | ConvertTo-Json -Depth 6 -Compress)."
    }
    $publication = Invoke-EtlRequest -Context $Context -Method Post -Path "/etl/v2/definitions/$DefinitionId/publish" -Body @{
        expectedRevision = $validation.revision
        definitionVersion = $validation.definitionVersion
        contentHash = $validation.contentHash
    }
    if ($publication.status -notin @('published', 'replay') -or [string]::IsNullOrWhiteSpace($publication.version.selection)) {
        throw "V2 ETL publication '$DefinitionId' did not return an immutable selection."
    }
    Write-Host ("[etl-http] published {0} version={1}" -f $DefinitionId, $publication.version.definitionVersion)
    return [string]$publication.version.selection
}

function Wait-EtlRunForWorkflow {
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)] $Started, [int] $TimeoutSeconds = 30)
    $before = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($runId in $Started.BeforeRunIds) { [void]$before.Add([string]$runId) }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $matches = @(Get-EtlRunSummaries -Context $Context | Where-Object {
            $_.selection -eq $Started.Selection -and -not $before.Contains([string]$_.runId)
        })
        if ($matches.Count -gt 1) { throw 'One RunEtl activity created multiple connector runs.' }
        if ($matches.Count -eq 1) { return $matches[0] }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "RunEtl did not admit ETL selection '$($Started.Selection)' within $TimeoutSeconds seconds."
}

function Get-EtlRunDetail {
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)][string] $RunId)
    $escapedId = [Uri]::EscapeDataString($RunId)
    Invoke-EtlRequest -Context $Context -Method Get -Path "/etl/runs/$escapedId"
}

function Get-EtlConnectorPartitions {
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)][string] $RunId)
    $escapedId = [Uri]::EscapeDataString($RunId)
    Invoke-EtlRequest -Context $Context -Method Get -Path "/etl/runs/$escapedId/connector-partitions?limit=50"
}

function Wait-EtlSourceRetryAndSiblingProgress {
    param([Parameter(Mandatory)] $Context, [Parameter(Mandatory)][string] $RunId, [int] $TimeoutSeconds = 25)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $partitions = @(Get-EtlConnectorPartitions -Context $Context -RunId $RunId | Select-Object -ExpandProperty items)
        $waiting = @($partitions | Where-Object {
            $_.http.fetch.phase -eq 'waiting' -and $_.http.fetch.lastStatusCode -eq 429
        })
        if ($waiting.Count -gt 0) {
            $retryingOrdinal = $waiting[0].partitionOrdinal
            $advancedSibling = @($partitions | Where-Object {
                $_.partitionOrdinal -ne $retryingOrdinal -and $_.http -and
                ($_.http.pageSequence -gt 0 -or $_.http.nextItem -gt 0 -or $_.http.exhausted)
            })
            if ($advancedSibling.Count -gt 0) {
                return [pscustomobject]@{ Retrying = $waiting[0]; AdvancedSibling = $advancedSibling[0] }
            }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Run '$RunId' did not expose a durable 429 wait while its other HTTP shard advanced."
}

function Assert-FiniteRun {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)] $RunSummary,
        [Parameter(Mandatory)][int] $ExpectedAccepted,
        [Parameter(Mandatory)][int] $ExpectedRejected,
        [Parameter(Mandatory)][ValidateSet('http', 'csv')][string] $ExpectedSourceKind,
        [Parameter(Mandatory)][ValidateSet('http', 'jsonl')][string] $ExpectedMainKind
    )
    $detail = Get-EtlRunDetail -Context $Context -RunId $RunSummary.runId
    $expectedStatus = if ($ExpectedRejected -gt 0) { 'businessError' } else { 'succeeded' }
    if ($detail.formatVersion -ne 2 -or $detail.status -ne $expectedStatus -or
        $detail.terminalResult.outcome -ne $expectedStatus -or $detail.acceptedRecords -ne $ExpectedAccepted -or
        $detail.rejectedRecords -ne $ExpectedRejected -or $detail.filteredRecords -ne 0 -or
        $detail.outstandingRecords -ne 0 -or $detail.outstandingBytes -ne 0 -or
        $detail.connector.sourceKind -ne $ExpectedSourceKind -or $detail.connector.mainKind -ne $ExpectedMainKind) {
        throw "ETL run '$($RunSummary.runId)' did not settle the expected v2 connector outcome."
    }

    # Claim metadata describes the last persisted owner, including after terminal settlement.
    # Completion is proved by exhausted cursors and no owed work, not deletion of that metadata.
    $partitions = Get-EtlConnectorPartitions -Context $Context -RunId $RunSummary.runId
    $rows = @($partitions.items)
    if ($rows.Count -ne 2 -or $partitions.nextCursor -or
        @($rows | Where-Object { $_.owedBatch -or (-not $_.http -and -not $_.file) }).Count -ne 0) {
        throw "ETL run '$($RunSummary.runId)' did not settle its two connector partitions."
    }
    if ($ExpectedSourceKind -eq 'http' -and @($rows | Where-Object { -not $_.http.exhausted -or $_.http.pageSequence -ne 1 }).Count -ne 0) {
        throw "HTTP source run '$($RunSummary.runId)' did not exhaust both two-page shards."
    }
    if ($ExpectedSourceKind -eq 'csv' -and @($rows | Where-Object { $_.file.nextOrdinal -ne $_.file.endOrdinalExclusive }).Count -ne 0) {
        throw "CSV source run '$($RunSummary.runId)' did not consume both finite file ranges."
    }
    if ($detail.connector.acceptedMainEffects -lt 1) {
        throw "ETL run '$($RunSummary.runId)' did not expose accepted main route effects."
    }
    return $detail
}

function Assert-MappedFixtureRecord {
    param([Parameter(Mandatory)] $Record)
    $names = @{ 1 = 'Ada'; 2 = 'Grace'; 3 = 'Linus'; 4 = 'Margaret' }
    $id = [int]$Record.id
    if (-not $names.ContainsKey($id) -or $Record.name -cne $names[$id]) {
        throw "The output record did not preserve the fixture ID-to-name mapping."
    }
}

function Assert-HttpOutputDelta {
    param(
        [Parameter(Mandatory)] $Fixture,
        [Parameter(Mandatory)][int] $StartPostCount,
        [Parameter(Mandatory)][int[]] $ExpectedIds
    )
    $state = Get-EtlHttpFixtureState -Fixture $Fixture
    $posts = @($state.outputPosts)
    $newPosts = @($posts | Select-Object -Skip $StartPostCount)
    if ($newPosts.Count -eq 0) { throw 'The ETL run did not send an HTTP output request to the loopback receiver.' }
    $ids = [System.Collections.Generic.List[long]]::new()
    foreach ($post in $newPosts) {
        if (-not $post.hasIdempotencyKey -or $null -eq $post.records -or $post.records -isnot [array]) {
            throw 'The HTTP receiver did not observe a JSON array with the output idempotency header.'
        }
        foreach ($record in $post.records) {
            if ($null -eq $record.id -or $null -eq $record.name) { throw 'The HTTP output record lost a mapped field.' }
            Assert-MappedFixtureRecord -Record $record
            $ids.Add([long]$record.id)
        }
    }
    $expectedSet = [System.Collections.Generic.HashSet[long]]::new()
    foreach ($id in $ExpectedIds) { [void]$expectedSet.Add([long]$id) }
    $receivedSet = [System.Collections.Generic.HashSet[long]]::new()
    foreach ($id in $ids) { [void]$receivedSet.Add($id) }
    if (-not $expectedSet.SetEquals($receivedSet)) {
        throw "HTTP receiver IDs were '$(@($receivedSet | Sort-Object) -join ',')', expected '$(@($expectedSet | Sort-Object) -join ',')'."
    }
    $duplicateDeliveries = $ids.Count - $receivedSet.Count
    Write-Host ("[etl-http] HTTP receiver posts={0} records={1} duplicateRecords={2} (at-least-once delivery)" -f `
        $newPosts.Count, $ids.Count, $duplicateDeliveries)
}

function Assert-JsonlIds {
    param([Parameter(Mandatory)][string] $Root, [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $PreviousNames,
        [Parameter(Mandatory)][int[]] $ExpectedIds)
    $chunks = @(Get-NewRouteChunks -Path $Root -PreviousNames $PreviousNames)
    if ($chunks.Count -eq 0) { throw "No new JSONL route chunks were published in '$Root'." }
    $ids = [System.Collections.Generic.List[long]]::new()
    foreach ($chunk in $chunks) {
        foreach ($line in [IO.File]::ReadAllLines($chunk.FullName, [Text.Encoding]::UTF8)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $record = $line | ConvertFrom-Json
            Assert-MappedFixtureRecord -Record $record
            $ids.Add([long]$record.id)
        }
    }
    $actual = @($ids | Sort-Object)
    $expected = @($ExpectedIds | Sort-Object)
    if (($actual -join ',') -ne ($expected -join ',')) {
        throw "JSONL output IDs were '$($actual -join ',')', expected '$($expected -join ',')'."
    }
}

$fixture = $null
$contentRoot = $null
$success = $false
try {
    $baseUrl = Resolve-ElsaServerBaseUrl
    $fixture = New-EtlHttpFixture
    $contentRoot = $fixture.ContentRoot
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot
    $context = Connect-Elsa -BaseUrl $baseUrl -Username $Username -Password $Password

    $runEtlVersion = Get-ActivityVersionId -Ctx $context -TypeKey 'Elsa.Etl.Workflows.Runtime.Activities.RunEtl'
    $sequenceVersion = Get-ActivityVersionId -Ctx $context -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $flowchartVersion = Get-ActivityVersionId -Ctx $context -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart'
    $writeLineVersion = Get-ActivityVersionId -Ctx $context -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'

    $bindings = @(
        @{ Alias = 'http-source'; Role = 'Source' }, @{ Alias = 'http-wait'; Role = 'Source' },
        @{ Alias = 'http-output'; Role = 'MainOutput' }, @{ Alias = 'source'; Role = 'Source' },
        @{ Alias = 'output'; Role = 'MainOutput' }, @{ Alias = 'errors'; Role = 'ErrorOutput' }
    )
    $httpToHttpSelection = Publish-FiniteConnectorPipeline -Context $context `
        -DefinitionId ('finite-http-http-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)) `
        -Pipeline (New-FiniteConnectorPipeline -SourceKind http -SourceBinding 'http-source' -MainKind http -MainBinding 'http-output') `
        -ExpectedBindings $bindings
    $httpToJsonlSelection = Publish-FiniteConnectorPipeline -Context $context `
        -DefinitionId ('finite-http-jsonl-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)) `
        -Pipeline (New-FiniteConnectorPipeline -SourceKind http -SourceBinding 'http-source' -MainKind jsonl -MainBinding 'output') `
        -ExpectedBindings $bindings
    $csvToHttpSelection = Publish-FiniteConnectorPipeline -Context $context `
        -DefinitionId ('finite-csv-http-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)) `
        -Pipeline (New-FiniteConnectorPipeline -SourceKind csv -SourceBinding 'source' -MainKind http -MainBinding 'http-output') `
        -ExpectedBindings $bindings
    $cancelSelection = Publish-FiniteConnectorPipeline -Context $context `
        -DefinitionId ('finite-http-cancel-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)) `
        -Pipeline (New-FiniteConnectorPipeline -SourceKind http -SourceBinding 'http-wait' -MainKind jsonl -MainBinding 'output') `
        -ExpectedBindings $bindings

    $journeys = @(
        @{ Kind = 'Sequence'; Selection = $httpToHttpSelection; Source = 'http'; Main = 'http'; Accepted = 4; Rejected = 0 },
        @{ Kind = 'Flowchart'; Selection = $httpToHttpSelection; Source = 'http'; Main = 'http'; Accepted = 4; Rejected = 0 },
        @{ Kind = 'Sequence'; Selection = $httpToJsonlSelection; Source = 'http'; Main = 'jsonl'; Accepted = 4; Rejected = 0 },
        @{ Kind = 'Flowchart'; Selection = $httpToJsonlSelection; Source = 'http'; Main = 'jsonl'; Accepted = 4; Rejected = 0 },
        @{ Kind = 'Sequence'; Selection = $csvToHttpSelection; Source = 'csv'; Main = 'http'; Accepted = 3; Rejected = 1 },
        @{ Kind = 'Flowchart'; Selection = $csvToHttpSelection; Source = 'csv'; Main = 'http'; Accepted = 3; Rejected = 1 }
    )

    $source429Proved = $false
    foreach ($journey in $journeys) {
        $retryEvidence = $null
        $beforeState = Get-EtlHttpFixtureState -Fixture $fixture
        $beforePosts = @($beforeState.outputPosts).Count
        $previousMain = @(Get-RouteChunkNames -Path $fixture.MainRoot)
        $previousErrors = @(Get-RouteChunkNames -Path $fixture.ErrorRoot)
        $compositeVersion = if ($journey.Kind -eq 'Sequence') { $sequenceVersion } else { $flowchartVersion }
        $started = Start-EtlNativeWorkflow -Context $context -Kind $journey.Kind -Selection $journey.Selection `
            -RunEtlVersion $runEtlVersion -CompositeVersion $compositeVersion -WriteLineVersion $writeLineVersion `
            -NamePrefix 'FiniteHttpEtl'
        $run = Wait-EtlRunForWorkflow -Context $context -Started $started

        if ($journey.Source -eq 'http' -and -not $source429Proved) {
            $retryProgress = Wait-EtlSourceRetryAndSiblingProgress -Context $context -RunId $run.runId
            $retryNotBefore = [DateTimeOffset]$retryProgress.Retrying.http.fetch.notBefore
            if ($retryNotBefore -le [DateTimeOffset]::UtcNow) {
                throw 'The persisted source Retry-After deadline was not in the future while waiting.'
            }
            $wireState = Get-EtlHttpFixtureState -Fixture $fixture
            $shardB429 = @($wireState.requests | Where-Object { $_.path -eq '/source/shard-b/page' -and $_.status -eq 429 })
            $earlyShardBRetry = @($wireState.requests | Where-Object { $_.path -eq '/source/shard-b/page' -and $_.status -eq 200 -and $_.page -eq 0 })
            if ($shardB429.Count -ne 1 -or $earlyShardBRetry.Count -ne 0) {
                throw 'The loopback fixture did not observe one shard-b 429 before the persisted retry deadline.'
            }
            $retryEvidence = [pscustomobject]@{
                RunId = [string]$run.runId
                RetryProgress = $retryProgress
                ShardB429ReceivedAtMs = [double]$shardB429[0].receivedAtMs
            }
        }

        $run = Wait-EtlNativeWorkflow -Context $context -Started $started -TimeoutSeconds 180
        $detail = Assert-FiniteRun -Context $context -RunSummary $run -ExpectedAccepted $journey.Accepted `
            -ExpectedRejected $journey.Rejected -ExpectedSourceKind $journey.Source -ExpectedMainKind $journey.Main
        if ($retryEvidence) {
            $wireState = Get-EtlHttpFixtureState -Fixture $fixture
            $shardB429 = @($wireState.requests | Where-Object { $_.path -eq '/source/shard-b/page' -and $_.status -eq 429 })
            $shardBRetry = @($wireState.requests | Where-Object { $_.path -eq '/source/shard-b/page' -and $_.status -eq 200 -and $_.page -eq 0 })
            if ($shardB429.Count -ne 1 -or $shardBRetry.Count -ne 1) {
                throw 'The loopback fixture did not observe one shard-b 429 followed by one successful retry.'
            }
            $retryElapsedMs = [double]$shardBRetry[0].receivedAtMs - $retryEvidence.ShardB429ReceivedAtMs
            if ($retryElapsedMs -lt 3000) {
                throw ("The loopback fixture observed shard-b retry after only {0:N3}ms, before its 3-second Retry-After." -f $retryElapsedMs)
            }
            $source429Proved = $true
            Write-Host ("[etl-http] run={0} shard-b=429 waiting {1}ms; sibling partition {2} advanced" -f `
                $retryEvidence.RunId, [Math]::Round($retryElapsedMs), $retryEvidence.RetryProgress.AdvancedSibling.partitionOrdinal)
        }
        if ($journey.Main -eq 'http') {
            $expectedIds = if ($journey.Source -eq 'http') { @(1, 2, 3, 4) } else { @(1, 2, 3) }
            Assert-HttpOutputDelta -Fixture $fixture -StartPostCount $beforePosts -ExpectedIds $expectedIds
        } else {
            Assert-JsonlIds -Root $fixture.MainRoot -PreviousNames $previousMain -ExpectedIds @(1, 2, 3, 4)
        }

        if ($journey.Rejected -gt 0) {
            $errorChunks = @(Get-NewRouteChunks -Path $fixture.ErrorRoot -PreviousNames $previousErrors)
            $errorLines = @($errorChunks | ForEach-Object {
                [IO.File]::ReadAllLines($_.FullName, [Text.Encoding]::UTF8)
            } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($errorLines.Count -ne 1 -or ($errorLines[0] | ConvertFrom-Json).code -ne 'etl.invalid-value') {
                throw "CSV-to-HTTP run '$($run.runId)' did not publish its typed safe row-rejection output."
            }
        }
        Write-Host ("[etl-http] {0} {1}->{2} run={3} accepted={4} rejected={5} continuation=complete" -f `
            $journey.Kind, $journey.Source, $journey.Main, $run.runId, $detail.acceptedRecords, $detail.rejectedRecords)
    }
    if (-not $source429Proved) { throw 'No real HTTP source Retry-After journey was observed.' }

    $cancelStarted = Start-EtlNativeWorkflow -Context $context -Kind 'Flowchart' -Selection $cancelSelection `
        -RunEtlVersion $runEtlVersion -CompositeVersion $flowchartVersion -WriteLineVersion $writeLineVersion `
        -NamePrefix 'FiniteHttpEtlCancel'
    $cancelRun = Wait-EtlRunForWorkflow -Context $context -Started $cancelStarted
    $cancelDeadline = [DateTime]::UtcNow.AddSeconds(25)
    $waitingDetail = $null
    do {
        $waitingDetail = Get-EtlRunDetail -Context $context -RunId $cancelRun.runId
        $waitingParts = @(Get-EtlConnectorPartitions -Context $context -RunId $cancelRun.runId | Select-Object -ExpandProperty items)
        $delayedRetryPartitions = @($waitingParts | Where-Object {
            $_.http.fetch.phase -eq 'waiting' -and $_.http.fetch.lastStatusCode -eq 429 -and
            $_.http.fetch.attemptCount -eq 1 -and $_.http.fetch.notBefore -and
            [DateTimeOffset]$_.http.fetch.notBefore -gt [DateTimeOffset]::UtcNow
        })
        $hasDelayedRetry = $delayedRetryPartitions.Count -gt 0
        if ($hasDelayedRetry) { break }
        if ($waitingDetail.status -in @('succeeded', 'cancelled', 'businessError', 'failed')) {
            throw "The delayed-retry run reached '$($waitingDetail.status)' before cancellation could be requested."
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $cancelDeadline)
    if (-not $hasDelayedRetry) { throw 'The http-wait source did not expose its persisted 30-second Retry-After state.' }
    $retryNotBefore = [DateTimeOffset]$delayedRetryPartitions[0].http.fetch.notBefore

    $cancelRunId = [Uri]::EscapeDataString([string]$cancelRun.runId)
    $cancelResult = Invoke-EtlRequest -Context $context -Method Post -Path "/etl/runs/$cancelRunId/cancel"
    if (-not $cancelResult.cancellationAccepted -or $cancelResult.terminalResult.outcome -ne 'cancelled') {
        throw "The real ETL cancellation API did not record the expected cancelled terminal result for '$($cancelRun.runId)'."
    }
    $cancelledDetail = Get-EtlRunDetail -Context $context -RunId $cancelRun.runId
    if ($cancelledDetail.status -ne 'cancelled' -or $cancelledDetail.terminalResult.outcome -ne 'cancelled') {
        throw 'The delayed HTTP retry overwrote the cancellation terminal result.'
    }
    $nativeCancelled = Wait-WorkflowInstance -Ctx $context -ExecutionId $cancelStarted.WorkflowExecutionId -TimeoutSeconds 30
    if ($nativeCancelled.instance.status -ne 'Cancelled' -or
        (Get-NodeRunCount -Instance $nativeCancelled -NodeId $cancelStarted.AfterNodeId) -ne 0) {
        Show-WorkflowInstance -Instance $nativeCancelled
        throw 'The cancelled ETL run did not cancel its native Flowchart or it ran the successor activity.'
    }

    # Keep Workbench alive past the persisted retry time so a late background wake cannot be
    # mistaken for cancellation merely because the first post-cancel observation was immediate.
    $observeUntil = $retryNotBefore.AddSeconds(2)
    while ([DateTimeOffset]::UtcNow -lt $observeUntil) {
        $cancelledDetail = Get-EtlRunDetail -Context $context -RunId $cancelRun.runId
        if ($cancelledDetail.status -ne 'cancelled' -or $cancelledDetail.terminalResult.outcome -ne 'cancelled') {
            throw 'The cancelled ETL terminal result changed while its former retry deadline elapsed.'
        }
        $cancelState = Get-EtlHttpFixtureState -Fixture $fixture
        $waitRequests = @($cancelState.requests | Where-Object { $_.path -eq '/wait/page' })
        if ($waitRequests.Count -ne 1 -or $waitRequests[0].status -ne 429) {
            throw 'The cancellation fixture observed a second request before or after the retained retry deadline.'
        }
        $remaining = ($observeUntil - [DateTimeOffset]::UtcNow).TotalMilliseconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds ([int][Math]::Min(250, [Math]::Ceiling($remaining))) }
    }
    $cancelState = Get-EtlHttpFixtureState -Fixture $fixture
    $waitRequests = @($cancelState.requests | Where-Object { $_.path -eq '/wait/page' })
    if ($waitRequests.Count -ne 1 -or $waitRequests[0].status -ne 429) {
        throw 'The cancellation fixture observed an unexpected physical request count.'
    }
    Write-Host ("[etl-http] cancelled run={0} after durable 429 wait; no second HTTP attempt" -f $cancelRun.runId)

    $success = $true
    Write-Host 'SUCCESS - Workbench published and ran all three v2 connector combinations through Sequence and Flowchart, exposed shard retry progress, and cancelled a delayed source retry.' -ForegroundColor Green
} finally {
    try {
        if ($fixture) { Stop-EtlHttpFixture -Fixture $fixture }
    } finally {
        if ($contentRoot) {
            Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
        } else {
            Remove-OwnedElsaServer
        }
    }
}
