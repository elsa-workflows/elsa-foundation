# Shared Workbench REST and native workflow helpers for ETL acceptance journeys.

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

function Start-EtlNativeWorkflow {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)][string] $Kind,
        [Parameter(Mandatory)][string] $Selection,
        [Parameter(Mandatory)][string] $RunEtlVersion,
        [Parameter(Mandatory)][string] $CompositeVersion,
        [Parameter(Mandatory)][string] $WriteLineVersion,
        [string] $NamePrefix = 'FiniteEtl'
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
    } elseif ($Kind -eq 'Flowchart') {
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
    } else {
        throw "Unsupported native ETL parent kind '$Kind'."
    }

    $beforeRunIds = @(Get-EtlRunSummaries -Context $Context | ForEach-Object { [string]$_.runId })
    $definition = Submit-Workflow -Ctx $Context -Name "$NamePrefix-$Kind-$suffix" `
        -Description "Native $Kind runs one published finite ETL selection and continues to a following activity." -RootActivity $root
    $publication = Publish-WorkflowVersion -Ctx $Context -VersionId $definition.version.id
    $execution = Invoke-Artifact -Ctx $Context -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    return [pscustomobject]@{
        Kind = $Kind
        Selection = $Selection
        RunNodeId = $runNodeId
        AfterNodeId = $afterNodeId
        BeforeRunIds = $beforeRunIds
        WorkflowExecutionId = $execution.workflowExecutionId
    }
}

function Wait-EtlNativeWorkflow {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)] $Started,
        [int] $TimeoutSeconds = 180
    )
    $instance = Wait-WorkflowInstance -Ctx $Context -ExecutionId $Started.WorkflowExecutionId -TimeoutSeconds $TimeoutSeconds
    if ($instance.instance.status -notin @('Completed', 'Finished') -or @($instance.incidents).Count -ne 0) {
        Show-WorkflowInstance -Instance $instance
        throw "$($Started.Kind) workflow did not complete without incidents."
    }
    if ((Get-NodeRunCount -Instance $instance -NodeId $Started.RunNodeId) -ne 1 -or
        (Get-NodeRunCount -Instance $instance -NodeId $Started.AfterNodeId) -ne 1) {
        throw "$($Started.Kind) did not execute RunEtl once and continue to its following activity."
    }

    $before = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in $Started.BeforeRunIds) { [void]$before.Add([string]$id) }
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $matchingRuns = @(Get-EtlRunSummaries -Context $Context | Where-Object {
            $_.selection -eq $Started.Selection -and -not $before.Contains([string]$_.runId)
        })
        if ($matchingRuns.Count -gt 1) { throw "$($Started.Kind) created more than one ETL run for one RunEtl activity." }
        if ($matchingRuns.Count -eq 1) { return $matchingRuns[0] }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "$($Started.Kind) completed without a corresponding ETL run in the scoped runtime API."
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
    $started = Start-EtlNativeWorkflow -Context $Context -Kind $Kind -Selection $Selection `
        -RunEtlVersion $RunEtlVersion -CompositeVersion $CompositeVersion -WriteLineVersion $WriteLineVersion
    Wait-EtlNativeWorkflow -Context $Context -Started $started
}
