<#
.SYNOPSIS
    Runs a finite CSV-to-JSONL pipeline through Workbench ETL APIs and native workflows.
.DESCRIPTION
    Owns an isolated, already-built Workbench process on a free loopback port. It configures
    four host-approved file bindings, authors and publishes one ETL definition, then runs it
    from persisted Sequence and Flowchart parents. Each run must continue to its successor,
    report accepted/rejected/filtered counts, and publish the expected JSONL chunk contents.
    Build Workbench with the opt-in ETL module references before running this script.
#>
[CmdletBinding()]
param(
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)

. "$PSScriptRoot/_EtlFixture.ps1"

function Invoke-EtlRequest {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        $Body = $null
    )
    $arguments = @{
        Uri = "$($Context.BaseUrl)$Path"
        Method = $Method
        WebSession = $Context.Session
        TimeoutSec = 60
    }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body | ConvertTo-Json -Depth 40 -Compress
    }
    Invoke-RestMethod @arguments
}

function Get-EtlRunSummaries {
    param([Parameter(Mandatory)] $Context)
    $cursor = $null
    $summaries = [System.Collections.Generic.List[object]]::new()
    $pages = 0
    do {
        if (++$pages -gt 100) { throw 'ETL run listing exceeded the bounded page count.' }
        $query = '?limit=200'
        if ($cursor) { $query += '&cursor=' + [Uri]::EscapeDataString($cursor) }
        $page = Invoke-EtlRequest -Context $Context -Method Get -Path "/etl/runs$query"
        foreach ($item in @($page.items)) { $summaries.Add($item) }
        $cursor = $page.nextCursor
    } while ($cursor)
    return $summaries.ToArray()
}

function Get-NewRouteChunks {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $PreviousNames
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return @() }
    $previous = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $PreviousNames) { [void]$previous.Add($name) }
    return @(Get-ChildItem -LiteralPath $Path -Filter '*.jsonl' -File | Where-Object { -not $previous.Contains($_.Name) })
}

function Get-RouteChunkNames {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $Path -Filter '*.jsonl' -File | Select-Object -ExpandProperty Name)
}

function Assert-RunOutput {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)] $RunSummary,
        [Parameter(Mandatory)][string] $Selection,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $PreviousMainNames,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $PreviousErrorNames,
        [Parameter(Mandatory)][string] $MainRoot,
        [Parameter(Mandatory)][string] $ErrorRoot,
        [Parameter(Mandatory)][string] $ExpectedSourceHash,
        [Parameter(Mandatory)][string] $ExpectedLookupHash,
        [Parameter(Mandatory)][long] $ExpectedSourceBytes,
        [Parameter(Mandatory)][long] $ExpectedLookupBytes
    )
    $runId = [Uri]::EscapeDataString([string]$RunSummary.runId)
    $detail = Invoke-EtlRequest -Context $Context -Method Get -Path "/etl/runs/$runId"
    if ($detail.status -ne 'businessError' -or $detail.terminalResult.outcome -ne 'businessError' -or
        $detail.terminalResult.diagnostic.code -ne 'etl.rows-rejected') {
        throw "ETL run '$($RunSummary.runId)' did not settle the expected row-rejection outcome: status=$($detail.status), outcome=$($detail.terminalResult.outcome), diagnostic=$($detail.diagnostic | ConvertTo-Json -Compress)."
    }
    if ($detail.selection -ne $Selection -or $detail.sourceRecordCount -ne 4 -or
        $detail.acceptedRecords -ne 2 -or $detail.rejectedRecords -ne 1 -or $detail.filteredRecords -ne 1) {
        throw "ETL run '$($RunSummary.runId)' has unexpected selection, source, or disposition counts."
    }
    if ($detail.terminalResult.acceptedRecords -ne 2 -or $detail.terminalResult.rejectedRecords -ne 1 -or
        $detail.terminalResult.filteredRecords -ne 1) {
        throw "ETL terminal result '$($RunSummary.runId)' does not match the run disposition counters."
    }
    if ($detail.bindingAliases.source -ne 'source' -or $detail.bindingAliases.lookup -ne 'lookup' -or
        $detail.bindingAliases.output -ne 'output' -or $detail.bindingAliases.errors -ne 'errors') {
        throw "ETL run '$($RunSummary.runId)' did not retain the configured binding aliases."
    }
    if ($detail.sourceInput.expectedSha256 -ne $ExpectedSourceHash -or $detail.sourceInput.snapshotSha256 -ne $ExpectedSourceHash -or
        $detail.sourceInput.snapshotByteLength -ne $ExpectedSourceBytes -or
        $detail.lookupInput.expectedSha256 -ne $ExpectedLookupHash -or $detail.lookupInput.snapshotSha256 -ne $ExpectedLookupHash -or
        $detail.lookupInput.snapshotByteLength -ne $ExpectedLookupBytes) {
        throw "ETL run '$($RunSummary.runId)' did not retain the expected verified input snapshot identities."
    }

    $partitions = Invoke-EtlRequest -Context $Context -Method Get -Path "/etl/runs/$runId/partitions?limit=200"
    $partitionRows = @($partitions.items | Sort-Object partitionOrdinal)
    if ($partitionRows.Count -ne 2 -or $partitionRows[0].partitionOrdinal -ne 0 -or $partitionRows[1].partitionOrdinal -ne 1 -or
        $partitionRows[0].startOrdinal -ne 1 -or $partitionRows[0].endOrdinalExclusive -ne 3 -or
        $partitionRows[1].startOrdinal -ne 3 -or $partitionRows[1].endOrdinalExclusive -ne 5 -or
        @($partitionRows | Where-Object { $_.isClaimed -or $_.owedBatch -or $_.nextOrdinal -ne $_.endOrdinalExclusive }).Count -ne 0 -or
        $partitions.nextCursor) {
        throw "ETL run '$($RunSummary.runId)' did not finish its two exhaustive source partitions."
    }

    $mainChunks = @(Get-NewRouteChunks -Path $MainRoot -PreviousNames $PreviousMainNames)
    $errorChunks = @(Get-NewRouteChunks -Path $ErrorRoot -PreviousNames $PreviousErrorNames)
    $actualMain = [System.Collections.Generic.List[object]]::new()
    foreach ($chunk in $mainChunks) {
        foreach ($line in [IO.File]::ReadAllLines($chunk.FullName, [Text.Encoding]::UTF8)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $record = $line | ConvertFrom-Json
            if ($record.id -isnot [long] -and $record.id -isnot [int]) {
                throw "Main route emitted a non-integer 'id' in chunk '$($chunk.Name)'."
            }
            $actualMain.Add([pscustomobject]@{ Id = [long]$record.id; Line = $line })
        }
    }
    $actualMainLines = @($actualMain | Sort-Object Id | Select-Object -ExpandProperty Line)
    $expectedMainLines = @(
        '{"id":1,"customerName":"Ada","region":"Europe"}',
        '{"id":3,"customerName":"Linus","region":"Europe"}'
    )
    if ($mainChunks.Count -ne 2 -or ($actualMainLines -join "`n") -ne ($expectedMainLines -join "`n")) {
        throw "Main JSONL chunks for run '$($RunSummary.runId)' do not contain the two expected typed accepted records."
    }

    $errorLines = @()
    foreach ($chunk in $errorChunks) {
        $errorLines += [IO.File]::ReadAllLines($chunk.FullName, [Text.Encoding]::UTF8) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    $expectedError = '{"recordOrdinal":4,"code":"etl.invalid-value","field":"id"}'
    if ($errorChunks.Count -ne 1 -or $errorLines.Count -ne 1 -or $errorLines[0] -ne $expectedError) {
        throw "Error JSONL chunks for run '$($RunSummary.runId)' do not contain the expected safe typed-row rejection."
    }
    if ($detail.outstandingRecords -ne 0 -or $detail.outstandingBytes -ne 0) {
        throw "ETL run '$($RunSummary.runId)' left unsettled logical reservations after success."
    }

    Write-Host ("  [etl] run={0} accepted=2 rejected=1 filtered=1 partitions=2 outputChunks={1} errorChunks={2}" -f `
        $RunSummary.runId, $mainChunks.Count, $errorChunks.Count)
}

function Invoke-EtlNativeWorkflow {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)][string] $Kind,
        [Parameter(Mandatory)][string] $Selection,
        [Parameter(Mandatory)][string] $RunEtlVersion,
        [Parameter(Mandatory)][string] $CompositeVersion,
        [Parameter(Mandatory)][string] $WriteLineVersion
    )
    $suffix = [Guid]::NewGuid().ToString('N').Substring(0, 10)
    $runNodeId = 'etl-start'
    $afterNodeId = 'after-etl'
    $etlNode = New-ActivityNode -NodeId $runNodeId -VersionId $RunEtlVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'Pipeline' -Value $Selection)
    )
    $afterNode = New-ActivityNode -NodeId $afterNodeId -VersionId $WriteLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value "ETL $Kind continuation $suffix")
    )

    if ($Kind -eq 'Sequence') {
        $root = New-ActivityNode -NodeId 'sequence-root' -VersionId $CompositeVersion -Structure (
            New-SequenceStructure -Activities @($etlNode, $afterNode))
    } else {
        $structure = @{
            kind = 'elsa.flowchart.structure'
            schemaVersion = '1.0.0'
            payload = @{
                startNodeId = $runNodeId
                activities = @($etlNode, $afterNode)
                connections = @(
                    @{ source = @{ nodeId = $runNodeId; port = 'Done' }; target = @{ nodeId = $afterNodeId; port = 'Done' } },
                    @{ source = @{ nodeId = $runNodeId; port = 'BusinessError' }; target = @{ nodeId = $afterNodeId; port = 'Done' } }
                )
            }
        }
        $root = New-ActivityNode -NodeId 'flowchart-root' -VersionId $CompositeVersion -Structure $structure
    }

    $beforeRuns = @(Get-EtlRunSummaries -Context $Context | ForEach-Object { $_.runId })
    $definition = Submit-Workflow -Ctx $Context -Name "FiniteFileEtl-$Kind-$suffix" `
        -Description "Native $Kind waits for a published finite CSV-to-JSONL ETL run." -RootActivity $root
    $publication = Publish-WorkflowVersion -Ctx $Context -VersionId $definition.version.id
    $execution = Invoke-Artifact -Ctx $Context -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    $instance = Wait-WorkflowInstance -Ctx $Context -ExecutionId $execution.workflowExecutionId -TimeoutSeconds 180
    if ($instance.instance.status -notin @('Completed', 'Finished') -or @($instance.incidents).Count -ne 0) {
        Show-WorkflowInstance -Instance $instance
        throw "$Kind workflow did not complete without incidents."
    }
    if ((Get-NodeRunCount -Instance $instance -NodeId $runNodeId) -ne 1 -or
        (Get-NodeRunCount -Instance $instance -NodeId $afterNodeId) -ne 1) {
        throw "$Kind did not execute RunEtl once and continue to its following activity."
    }

    $deadline = (Get-Date).AddSeconds(30)
    $newRun = $null
    do {
        $existing = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($id in $beforeRuns) { [void]$existing.Add([string]$id) }
        $matchingRuns = @(Get-EtlRunSummaries -Context $Context | Where-Object {
            $_.selection -eq $Selection -and -not $existing.Contains([string]$_.runId)
        })
        if ($matchingRuns.Count -gt 1) { throw "$Kind created more than one ETL run for a single RunEtl activity." }
        if ($matchingRuns.Count -eq 1) { $newRun = $matchingRuns[0]; break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    if (-not $newRun) { throw "$Kind completed without a corresponding ETL run in the scoped runtime API." }
    return $newRun
}

$contentRoot = $null
$success = $false
try {
    $baseUrl = Resolve-ElsaServerBaseUrl
    $fixture = New-EtlFixture
    $contentRoot = $fixture.ContentRoot
    Start-OwnedElsaServer -BaseUrl $baseUrl -ContentRoot $contentRoot
    $context = Connect-Elsa -BaseUrl $baseUrl -Username $Username -Password $Password

    $bindingPage = Invoke-EtlRequest -Context $context -Method Get -Path '/etl/bindings?limit=200'
    foreach ($requiredBinding in @(
        @{ Alias = 'source'; Role = 'Source' }, @{ Alias = 'lookup'; Role = 'Lookup' },
        @{ Alias = 'output'; Role = 'MainOutput' }, @{ Alias = 'errors'; Role = 'ErrorOutput' }
    )) {
        if (-not (@($bindingPage.items | Where-Object { $_.alias -eq $requiredBinding.Alias -and $_.role -eq $requiredBinding.Role }).Count)) {
            throw "Workbench did not expose the host binding '$($requiredBinding.Alias)' with role '$($requiredBinding.Role)'."
        }
    }

    $pipeline = @{
        bindings = @{ source = 'source'; lookup = 'lookup'; output = 'output'; errors = 'errors' }
        sourceSchema = @(
            @{ name = 'id'; type = 'int64'; required = $true },
            @{ name = 'name'; type = 'string'; required = $true }
        )
        lookupSchema = @(
            @{ name = 'id'; type = 'int64'; required = $true },
            @{ name = 'region'; type = 'string'; required = $true }
        )
        sourceLookupField = 'id'
        lookupKeyField = 'id'
        mapping = @(
            @{ name = 'id'; origin = 'source'; field = 'id' },
            @{ name = 'customerName'; origin = 'source'; field = 'name' },
            @{ name = 'region'; origin = 'lookup'; field = 'region' }
        )
        filters = @(@{ field = 'region'; comparison = 'equal'; value = 'Europe' })
        limits = @{
            partitionCount = 2; maxRecordBytes = 65536; maxFields = 256; maxBatchRecords = 100
            maxBatchBytes = 4194304; maxOutstandingRecords = 100; maxOutstandingBytes = 4194304
            maxLookupRecords = 100; maxLookupBytes = 4194304; maxOutputBatchBytes = 4194304
            maxSnapshotBytes = 4194304; maxArtifactBytes = 262144
        }
    }
    $definitionId = 'finite-file-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $draft = Invoke-EtlRequest -Context $context -Method Post -Path '/etl/definitions' -Body @{
        id = $definitionId; pipeline = $pipeline
    }
    $validation = Invoke-EtlRequest -Context $context -Method Post -Path "/etl/definitions/$definitionId/validate" -Body @{
        expectedRevision = $draft.revision
    }
    if ($validation.diagnostics.Count -ne 0 -or [string]::IsNullOrWhiteSpace($validation.contentHash)) {
        throw "ETL pipeline validation did not produce a runnable artifact: $($validation.diagnostics | ConvertTo-Json -Depth 5 -Compress)."
    }
    $publication = Invoke-EtlRequest -Context $context -Method Post -Path "/etl/definitions/$definitionId/publish" -Body @{
        expectedRevision = $validation.revision
        definitionVersion = $validation.definitionVersion
        contentHash = $validation.contentHash
    }
    if ($publication.status -notin @('published', 'replay') -or -not $publication.version.selection) {
        throw 'ETL publish did not return the immutable version selection token.'
    }
    $selection = [string]$publication.version.selection
    Write-Host ("[etl] published definition={0} version={1}" -f $definitionId, $publication.version.definitionVersion)

    $runEtlVersion = Get-ActivityVersionId -Ctx $context -TypeKey 'Elsa.Etl.Workflows.Runtime.Activities.RunEtl'
    $sequenceVersion = Get-ActivityVersionId -Ctx $context -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $flowchartVersion = Get-ActivityVersionId -Ctx $context -TypeKey 'Elsa.Activities.Flowchart.Activities.Flowchart'
    $writeLineVersion = Get-ActivityVersionId -Ctx $context -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'

    foreach ($kind in @('Sequence', 'Flowchart')) {
        $oldMain = @(Get-RouteChunkNames -Path $fixture.MainRoot)
        $oldErrors = @(Get-RouteChunkNames -Path $fixture.ErrorRoot)
        $compositeVersion = if ($kind -eq 'Sequence') { $sequenceVersion } else { $flowchartVersion }
        $run = Invoke-EtlNativeWorkflow -Context $context -Kind $kind -Selection $selection `
            -RunEtlVersion $runEtlVersion -CompositeVersion $compositeVersion -WriteLineVersion $writeLineVersion
        Assert-RunOutput -Context $context -RunSummary $run -Selection $selection `
            -PreviousMainNames $oldMain -PreviousErrorNames $oldErrors -MainRoot $fixture.MainRoot -ErrorRoot $fixture.ErrorRoot `
            -ExpectedSourceHash $fixture.SourceHash -ExpectedLookupHash $fixture.LookupHash `
            -ExpectedSourceBytes $fixture.SourceBytes -ExpectedLookupBytes $fixture.LookupBytes
    }

    Write-Host 'SUCCESS - Sequence and Flowchart each completed the published ETL run, continued to their next activity, and produced the expected accepted/error JSONL chunks.' -ForegroundColor Green
    $success = $true
} finally {
    if ($contentRoot) {
        Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $success)
    } else {
        Remove-OwnedElsaServer
    }
}
