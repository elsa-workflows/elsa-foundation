<#
.SYNOPSIS
    Exercises registered BPMN import/export and published execution on a source-built Workbench.
.DESCRIPTION
    Supply an already owned server. This script never starts or stops a server. Uses the normal
    authenticated HTTP APIs and real catalog references; port fakes and library tests are insufficient.
    Writes a portable receipt when EvidencePath is supplied. A failed scenario remains a failure.
#>
[CmdletBinding(DefaultParameterSetName = "Host")]
param(
    [Parameter(Mandatory, ParameterSetName = "Host")][string] $BaseUrl,
    [Parameter(Mandatory, ParameterSetName = "BroadcastPreflight")][string[]] $BroadcastPreflightDatabases,
    [string] $Python = "python3",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $EvidencePath,
    [string] $SourceRevision = "unspecified"
)
if ($PSCmdlet.ParameterSetName -eq "BroadcastPreflight") {
    $validatorProject = Join-Path $PSScriptRoot "../../tools/bpmn/DeliveryPreflight/DeliveryPreflight.csproj"
    & dotnet build $validatorProject --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "The checked outbox preflight validator did not build." }
    $preflightArgs = @("$PSScriptRoot/inspect_broadcast_delivery.py", "--source-revision", $SourceRevision)
    foreach ($database in $BroadcastPreflightDatabases) { $preflightArgs += @("--database", $database) }
    if ($EvidencePath) { $preflightArgs += @("--output", $EvidencePath) }
    & $Python @preflightArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Broadcast preflight did not qualify the supplied stores (exit $LASTEXITCODE). Responsibility remains owed; inspect its receipt."
    }
    return
}

. "$PSScriptRoot/../_ElsaCommon.ps1"
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$receipts = [System.Collections.Generic.List[object]]::new()
$token = [Guid]::NewGuid().ToString('N')
$scenariosPassed = $false

function Import-Bpmn {
    param([string] $Xml)
    Invoke-RestMethod "$($ctx.BaseUrl)/interchange/bpmn/import" -Method Post -WebSession $ctx.Session `
        -ContentType 'application/json' -Body (@{ xml = $Xml } | ConvertTo-Json -Depth 40)
}

function Invoke-ImportedProcess {
    param([string] $Name, [string] $Xml, [string] $ExpectedWaitNode)
    $import = Import-Bpmn $Xml
    if (-not $import.analysis.hostInventoryVerified -or -not $import.analysis.canExecute) {
        throw "$Name was not executable after actual catalog binding: $($import.analysis | ConvertTo-Json -Depth 20 -Compress)"
    }
    $export = Invoke-RestMethod "$($ctx.BaseUrl)/interchange/bpmn/export" -Method Post -WebSession $ctx.Session `
        -ContentType 'application/json' -Body (@{ processNode = $import.processNode } | ConvertTo-Json -Depth 40)
    $definition = Submit-Workflow -Ctx $ctx -Name "BpmnAdoption-$Name-$token" -RootActivity $import.processNode
    $publication = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $publication.artifactId -SourceReferenceId $publication.sourceReferenceId
    $observedSuspended = $false
    if ($ExpectedWaitNode) {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 100
            $observation = Get-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
            $observedSuspended = @($observation.activities | Where-Object { $_.executableNodeId -eq $ExpectedWaitNode -and $_.status -eq 'Suspended' }).Count -eq 1
        } while (-not $observedSuspended -and (Get-Date) -lt $deadline)
        if (-not $observedSuspended) { throw "$Name did not expose its required durable suspension before continuation." }
    }
    $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 30
    $receipts.Add(@{ scenario = $Name; workflowExecutionId = $run.workflowExecutionId; artifactId = $publication.artifactId;
        status = $instance.instance.status; observedSuspended = $observedSuspended; incidents = @($instance.incidents); activities = @($instance.activities | Select-Object executableNodeId,status,outcomeNames);
        processVersionId = $import.processNode.activityVersionId; exportedXml = $export.xml })
    Show-WorkflowInstance $instance
    if ($instance.instance.status -ne 'Completed' -or @($instance.incidents).Count -ne 0) {
        throw "$Name failed: expected Completed with zero incidents."
    }
    return @{ Import = $import; Export = $export; Instance = $instance }
}

try {
    $simple = '<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:adoption"><process id="simple" isExecutable="true"><startEvent id="start"/><endEvent id="end"/><sequenceFlow id="flow" sourceRef="start" targetRef="end"/></process></definitions>'
    $null = Invoke-ImportedProcess -Name 'none-start' -Xml $simple
    $timer = '<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:adoption"><process id="timer" isExecutable="true"><startEvent id="start"/><intermediateCatchEvent id="wait"><timerEventDefinition><timeDuration>PT5S</timeDuration></timerEventDefinition></intermediateCatchEvent><endEvent id="end"/><sequenceFlow id="first" sourceRef="start" targetRef="wait"/><sequenceFlow id="last" sourceRef="wait" targetRef="end"/></process></definitions>'
    $timerResult = Invoke-ImportedProcess -Name 'timer-duration' -Xml $timer -ExpectedWaitNode 'node-wait'
    if ($timerResult.Export.xml -notmatch '<[^>]*timeDuration[^>]*>PT5S</') { throw 'Timer XML descriptor was not retained on export.' }
    # Two durable catches must be committed before one imported signal publisher broadcasts.
    $signalName = "bpmn-adoption-$token"
    $waiter = @"
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:adoption">
<signal id="signal" name="$signalName"/><process id="broadcast" isExecutable="true">
<startEvent id="start"/><parallelGateway id="fork"/><parallelGateway id="join"/><endEvent id="end"/>
<intermediateCatchEvent id="wait-a"><signalEventDefinition signalRef="signal"/></intermediateCatchEvent>
<intermediateCatchEvent id="wait-b"><signalEventDefinition signalRef="signal"/></intermediateCatchEvent>
<sequenceFlow id="begin" sourceRef="start" targetRef="fork"/>
<sequenceFlow id="a" sourceRef="fork" targetRef="wait-a"/><sequenceFlow id="b" sourceRef="fork" targetRef="wait-b"/>
<sequenceFlow id="a-join" sourceRef="wait-a" targetRef="join"/><sequenceFlow id="b-join" sourceRef="wait-b" targetRef="join"/>
<sequenceFlow id="finish" sourceRef="join" targetRef="end"/></process></definitions>
"@
    $waitImport = Import-Bpmn $waiter
    if (-not $waitImport.analysis.canExecute) { throw 'Actual catalog did not resolve the signal catch bindings.' }
    $waitDefinition = Submit-Workflow -Ctx $ctx -Name "BpmnAdoption-broadcast-$token" -RootActivity $waitImport.processNode
    $waitPublication = Publish-WorkflowVersion -Ctx $ctx -VersionId $waitDefinition.version.id
    $waitRun = Invoke-Artifact -Ctx $ctx -ArtifactId $waitPublication.artifactId -SourceReferenceId $waitPublication.sourceReferenceId
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $waiting = Get-WorkflowInstance -Ctx $ctx -ExecutionId $waitRun.workflowExecutionId
        $catches = @($waiting.activities | Where-Object { $_.executableNodeId -in @('node-wait-a', 'node-wait-b') -and $_.status -eq 'Suspended' })
    } while ($catches.Count -ne 2 -and (Get-Date) -lt $deadline)
    if ($catches.Count -ne 2) { throw 'Broadcast precondition failed: both exact signal catches must be durably suspended before publication.' }
    $publisher = @"
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:elsa:adoption">
<signal id="signal" name="$signalName"/><process id="publisher" isExecutable="true"><startEvent id="start"/>
<intermediateThrowEvent id="send"><signalEventDefinition signalRef="signal"/></intermediateThrowEvent><endEvent id="end"/>
<sequenceFlow id="begin" sourceRef="start" targetRef="send"/><sequenceFlow id="finish" sourceRef="send" targetRef="end"/>
</process></definitions>
"@
    $null = Invoke-ImportedProcess -Name 'signal-publisher' -Xml $publisher
    $delivered = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $waitRun.workflowExecutionId -TimeoutSeconds 30
    $completedCatches = @($delivered.activities | Where-Object { $_.executableNodeId -in @('node-wait-a','node-wait-b') -and $_.status -eq 'Completed' })
    $receipts.Add(@{scenario='signal-broadcast-two-catches'; workflowExecutionId=$waitRun.workflowExecutionId;
        status=$delivered.instance.status; incidents=@($delivered.incidents); suspendedBeforePublish=$catches.Count;
        completedCatches=$completedCatches.Count; activities=@($delivered.activities | Select-Object executableNodeId,status,outcomeNames)})
    Show-WorkflowInstance $delivered
    if ($delivered.instance.status -ne 'Completed' -or $completedCatches.Count -ne 2 -or @($delivered.incidents).Count -ne 0) {
        throw 'One imported signal publication did not complete both committed catches and their parallel join without incidents.'
    }
    $scenariosPassed = $true
    Write-Host 'Registered BPMN import/publish/execute/export scenarios passed.' -ForegroundColor Green
} finally {
    if ($EvidencePath) {
        @{ sourceRevision = $SourceRevision; passed = $scenariosPassed; capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); scenarios = @($receipts) } |
            ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $EvidencePath
    }
}
