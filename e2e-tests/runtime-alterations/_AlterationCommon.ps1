<#
.SYNOPSIS
    Shared black-box helpers for durable runtime-alteration plan tests.
.DESCRIPTION
    These helpers intentionally use only the public runtime-alteration REST contract. They never reach into a
    scheduler, store, or server DI scope, so a successful run proves the HTTP -> durable plan -> hosted pump ->
    workflow checkpoint path.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

function Invoke-AlterationRequest {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][ValidateSet('POST','GET')][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        $Body = $null,
        [string] $IdempotencyKey = $null
    )

    $request = @{ Uri = "$($Ctx.BaseUrl)/$($Path.TrimStart('/'))"; Method = $Method; WebSession = $Ctx.Session; UseBasicParsing = $true }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json'
        $request.Body = $Body | ConvertTo-Json -Depth 30 -Compress
    }
    if ($IdempotencyKey) { $request.Headers = @{ 'Idempotency-Key' = $IdempotencyKey } }

    try {
        $response = Invoke-WebRequest @request
        $json = $null; try { $json = $response.Content | ConvertFrom-Json } catch {}
        [pscustomobject]@{ Code = [int]$response.StatusCode; Content = $response.Content; Json = $json; Headers = $response.Headers }
    } catch {
        $response = $_.Exception.Response
        $code = if ($response) { [int]$response.StatusCode } else { -1 }
        $content = $_.ErrorDetails.Message
        if (-not $content -and $response) { try { $content = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd() } catch {} }
        [pscustomobject]@{ Code = $code; Content = $content; Json = $null; Headers = $null }
    }
}

function Submit-AlterationPlan {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)] $Target,
        [Parameter(Mandatory)][array] $Alterations,
        [string] $IdempotencyKey = "alteration-$(New-Guid)"
    )
    $response = Invoke-AlterationRequest -Ctx $Ctx -Method POST -Path 'runtime/workflows/alteration-plans' `
        -Body @{ target = $Target; alterations = $Alterations } -IdempotencyKey $IdempotencyKey
    if ($response.Code -ne 202) { throw "Alteration plan admission returned HTTP $($response.Code): $($response.Content)" }
    if (-not $response.Json.planId) { throw 'Alteration plan receipt did not contain planId.' }
    [pscustomobject]@{ Response = $response; PlanId = [string]$response.Json.planId; IdempotencyKey = $IdempotencyKey }
}

function Get-AlterationPlan {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $PlanId)
    $response = Invoke-AlterationRequest -Ctx $Ctx -Method GET -Path "runtime/workflows/alteration-plans/$PlanId"
    if ($response.Code -ne 200) { throw "Plan read returned HTTP $($response.Code): $($response.Content)" }
    return $response
}

function Get-AlterationJobsPage {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $PlanId, [int] $Take = 100, [string] $Cursor = $null)
    $query = "?take=$Take"
    if ($Cursor) { $query += "&cursor=$([Uri]::EscapeDataString($Cursor))" }
    $response = Invoke-AlterationRequest -Ctx $Ctx -Method GET -Path "runtime/workflows/alteration-plans/$PlanId/jobs/page$query"
    if ($response.Code -ne 200) { throw "Jobs-page read returned HTTP $($response.Code): $($response.Content)" }
    return $response
}

function Get-AlterationJob {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $PlanId, [Parameter(Mandatory)][string] $JobId)
    $response = Invoke-AlterationRequest -Ctx $Ctx -Method GET -Path "runtime/workflows/alteration-plans/$PlanId/jobs/$JobId"
    if ($response.Code -ne 200) { throw "Alteration job read returned HTTP $($response.Code): $($response.Content)" }
    return $response
}

function Get-SingleAlterationJob {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $PlanId)
    $page = Get-AlterationJobsPage -Ctx $Ctx -PlanId $PlanId -Take 2
    if ($page.Json.count -ne 1 -or $page.Json.hasNext) { throw "Expected exactly one alteration job: $($page.Content)" }
    return Get-AlterationJob -Ctx $Ctx -PlanId $PlanId -JobId $page.Json.items[0].jobId
}

function Get-ExecutableIdentity {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)] $Published)
    $detail = Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/executables/$($Published.artifactId)" -WebSession $Ctx.Session -UseBasicParsing
    $reference = @($detail.references | Where-Object { $_.sourceReferenceId -eq $Published.sourceReferenceId -and $_.live } | Select-Object -First 1)
    if ($reference.Count -ne 1) { throw "Published artifact '$($Published.artifactId)' did not expose its live source reference." }
    [pscustomobject]@{
        artifactId = [string]$detail.artifactId
        definitionId = [string]$reference[0].definitionId
        definitionVersionId = [string]$reference[0].definitionVersionId
        artifactVersion = [string]$reference[0].artifactVersion
        artifactHash = [string]$detail.artifactHash
    }
}

function Wait-AlterationPlanTerminal {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $PlanId, [int] $TimeoutSeconds = 45)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $response = Get-AlterationPlan -Ctx $Ctx -PlanId $PlanId
        if ($response.Json.status -in @('Completed','CompletedWithFailures','Failed','Cancelled')) { return $response }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "Alteration plan '$PlanId' did not become terminal within $TimeoutSeconds seconds (last status '$($response.Json.status)')."
}

function Wait-AlterationPlanCaptureProgress {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $PlanId,
        [Parameter(Mandatory)][long] $ExpectedCaptured,
        [int] $TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $response = Get-AlterationPlan -Ctx $Ctx -PlanId $PlanId
        if ($response.Json.status -eq 'CapturingTargets' -and [long]$response.Json.counts.capturedSoFar -eq $ExpectedCaptured) {
            return $response
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Alteration plan '$PlanId' did not reach CapturingTargets with capturedSoFar=$ExpectedCaptured within $TimeoutSeconds seconds (last status '$($response.Json.status)', capturedSoFar '$($response.Json.counts.capturedSoFar)')."
}

function Wait-AlterationActivityObservation {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $ExecutionId,
        [Parameter(Mandatory)][string] $NodeId,
        [string[]] $ExpectedStatuses = @(),
        [int] $TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $instance = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $ExecutionId
        $activities = @($instance.activities | Where-Object { $_.executableNodeId -eq $NodeId })
        if ($activities.Count -gt 0 -and ($ExpectedStatuses.Count -eq 0 -or @($activities | Where-Object { $_.status -in $ExpectedStatuses }).Count -gt 0)) {
            return [pscustomobject]@{ Instance = $instance; Activities = $activities }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    $observed = if ($activities) { ($activities | ForEach-Object { "$($_.activityExecutionId):$($_.status)" }) -join ', ' } else { '<none>' }
    throw "Workflow '$ExecutionId' did not expose node '$NodeId' in expected status(es) '$($ExpectedStatuses -join ', ')'. Observed: $observed"
}

function Wait-AlterationRescheduleObservation {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $ExecutionId,
        [Parameter(Mandatory)][string] $NodeId,
        [Parameter(Mandatory)][string] $SourceActivityExecutionId,
        [int] $TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $instance = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $ExecutionId
        $source = @($instance.activities | Where-Object { $_.activityExecutionId -eq $SourceActivityExecutionId })
        $successors = @($instance.activities | Where-Object { $_.executableNodeId -eq $NodeId -and $_.activityExecutionId -ne $SourceActivityExecutionId })
        if ($source.Count -eq 1 -and $source[0].status -eq 'Superseded' -and $successors.Count -eq 1) {
            return [pscustomobject]@{ Instance = $instance; Source = $source[0]; Successor = $successors[0] }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    $sourceStatus = if ($source.Count -eq 1) { $source[0].status } else { '<missing>' }
    $successorCount = $successors.Count
    throw "Workflow '$ExecutionId' did not expose one Superseded source '$SourceActivityExecutionId' and one successor for node '$NodeId' (source status '$sourceStatus', successors '$successorCount')."
}

function New-AlterationWaiter {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $NamePrefix,
        [string] $RootVariableKey = $null,
        $RootVariableDefault = ''
    )
    $eventVersion = Get-ActivityVersionId -Ctx $Ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event'
    $eventName = "$NamePrefix-$(Get-Random -Maximum 999999)"
    $root = New-ActivityNode -NodeId 'wait' -VersionId $eventVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'EventName' -Value $eventName),
        (New-LiteralInput -ReferenceKey 'CanStartWorkflow' -Value $false)
    )
    $variables = if ($RootVariableKey) { @(New-VariableDef -Key $RootVariableKey -Default $RootVariableDefault) } else { @() }
    $definition = Submit-Workflow -Ctx $Ctx -Name "$NamePrefix-$(Get-Random -Maximum 999999)" -RootActivity $root -Variables $variables
    $published = Publish-WorkflowVersion -Ctx $ctx -VersionId $definition.version.id
    $run = Invoke-Artifact -Ctx $ctx -ArtifactId $published.artifactId -SourceReferenceId $published.sourceReferenceId
    $executionId = [string]$run.workflowExecutionId
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 300
        $instance = Get-WorkflowInstance -Ctx $ctx -ExecutionId $executionId
        $waitActivity = @($instance.activities | Where-Object { $_.executableNodeId -eq 'wait' } | Select-Object -First 1)
    } while (($waitActivity.Count -ne 1 -or $waitActivity[0].status -ne 'Suspended') -and (Get-Date) -lt $deadline)
    if ($waitActivity.Count -ne 1 -or $waitActivity[0].status -ne 'Suspended') {
        throw "Waiter '$executionId' did not expose one suspended root Event activity."
    }
    [pscustomobject]@{
        ExecutionId = $executionId; Instance = $instance; EventName = $eventName; Published = $published
        RootActivityExecutionId = [string]$waitActivity[0].activityExecutionId
        WaitActivityExecutionId = [string]$waitActivity[0].activityExecutionId
    }
}

# A Sequence parent that is waiting on its first child and has a distinct unscheduled direct child. Sequence composes
# the public operator-scheduling capability, making this a real-server success fixture for ScheduleActivity.
function New-AlterationSchedulableWaiter {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $NamePrefix)
    $sequenceVersion = Get-ActivityVersionId -Ctx $Ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
    $eventVersion = Get-ActivityVersionId -Ctx $Ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event'
    $writeLineVersion = Get-ActivityVersionId -Ctx $Ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    $eventName = "$NamePrefix-$(Get-Random -Maximum 999999)"
    $wait = New-ActivityNode -NodeId 'wait' -VersionId $eventVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'EventName' -Value $eventName),
        (New-LiteralInput -ReferenceKey 'CanStartWorkflow' -Value $false)
    )
    $operatorChild = New-ActivityNode -NodeId 'operator-child' -VersionId $writeLineVersion -Inputs @(
        (New-LiteralInput -ReferenceKey 'text' -Value "operator schedule $NamePrefix")
    )
    $root = New-ActivityNode -NodeId 'root' -VersionId $sequenceVersion -Structure (New-SequenceStructure -Activities @($wait, $operatorChild))
    $definition = Submit-Workflow -Ctx $Ctx -Name "$NamePrefix-$(Get-Random -Maximum 999999)" -RootActivity $root
    $published = Publish-WorkflowVersion -Ctx $Ctx -VersionId $definition.version.id
    $run = Invoke-Artifact -Ctx $Ctx -ArtifactId $published.artifactId -SourceReferenceId $published.sourceReferenceId
    $executionId = [string]$run.workflowExecutionId
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 300
        $instance = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $executionId
        $rootActivity = @($instance.activities | Where-Object { $_.executableNodeId -eq 'root' } | Select-Object -First 1)
        $waitActivity = @($instance.activities | Where-Object { $_.executableNodeId -eq 'wait' } | Select-Object -First 1)
    } while (($rootActivity.Count -ne 1 -or $rootActivity[0].status -notin @('Running','Waiting') -or
              $waitActivity.Count -ne 1 -or $waitActivity[0].status -ne 'Suspended') -and
             (Get-Date) -lt $deadline)
    if ($rootActivity.Count -ne 1 -or $rootActivity[0].status -notin @('Running','Waiting') -or
        $waitActivity.Count -ne 1 -or $waitActivity[0].status -ne 'Suspended') {
        $rootStatus = if ($rootActivity.Count -eq 1) { $rootActivity[0].status } else { '<missing>' }
        $waitStatus = if ($waitActivity.Count -eq 1) { $waitActivity[0].status } else { '<missing>' }
        throw "Schedulable waiter '$executionId' did not reach an active Sequence parent with a suspended first child (root '$rootStatus', child '$waitStatus')."
    }
    [pscustomobject]@{
        ExecutionId = $executionId; Instance = $instance; EventName = $eventName; Published = $published
        RootActivityExecutionId = [string]$rootActivity[0].activityExecutionId
        OperatorChildNodeId = 'operator-child'
    }
}

function Assert-AlterationResponseIsRedacted {
    param([Parameter(Mandatory)][string] $Content, [Parameter(Mandatory)][string[]] $Secrets)
    foreach ($secret in $Secrets) {
        if ($Content.Contains($secret, [StringComparison]::Ordinal)) { throw "Alteration read leaked protected material '$secret'." }
    }
    foreach ($forbidden in @('"payload"', '"idempotencyKey"', '"ciphertext"', '"handlerType"')) {
        if ($Content.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) { throw "Alteration read leaked forbidden field $forbidden." }
    }
}
