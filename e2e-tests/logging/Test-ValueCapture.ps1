<#
.SYNOPSIS
    Verify activity input keys and resolve captured SendHttpRequest outputs through the value-evidence API.
.DESCRIPTION
    Runs a bare WriteLine and a Sequence containing SendHttpRequest(GET /) and the same WriteLine.
    Both root and nested activities must capture the text input. Activity-execution details expose metadata
    only; this script resolves selected evidence by id and inspects its bounded diagnostic snapshot. It does not change runtime diagnostics settings.
    Requires the Workbench server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

Write-Host "== Per-activity value capture ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$script:failures = [System.Collections.Generic.List[string]]::new()

function Get-ActivityEvidence {
    param(
        [Parameter(Mandatory)] $Detail,
        [Parameter(Mandatory)][string] $WorkflowExecutionId,
        [Parameter(Mandatory)][string] $ActivityExecutionId,
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][string] $Subject,
        [string] $InputKey,
        [string] $Name
    )

    $selector = if ($InputKey) { "inputKey '$InputKey'" } else { "name '$Name'" }
    $snapshot = if ($InputKey) {
        $Detail.valueSnapshots | Where-Object { $_.subject -ieq $Subject -and $_.inputKey -ceq $InputKey } | Select-Object -First 1
    } else {
        $Detail.valueSnapshots | Where-Object { $_.subject -ieq $Subject -and $_.name -ieq $Name } | Select-Object -First 1
    }
    if (-not $snapshot) {
        [void]$script:failures.Add("$Label $Subject evidence with $selector was not captured")
        return $null
    }

    if ($snapshot.captureState -ne 'diagnosticSnapshotCaptured') {
        [void]$script:failures.Add("$Label $selector captureState was '$($snapshot.captureState)'")
    }
    if ($null -ne $snapshot.payload -or $null -ne $snapshot.snapshot) {
        [void]$script:failures.Add("$Label $selector payload was inlined in activity detail")
    }
    if ([string]::IsNullOrWhiteSpace($snapshot.evidenceId)) {
        [void]$script:failures.Add("$Label $selector has no evidenceId")
        return $null
    }

    $resolved = Invoke-Step "$Label $selector payload" {
        Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$WorkflowExecutionId/activity-executions/$ActivityExecutionId/value-evidence/$($snapshot.evidenceId)/payload" -WebSession $ctx.Session -UseBasicParsing
    }
    if ($null -eq $resolved -or $null -eq $resolved.payload) {
        [void]$script:failures.Add("$Label $selector payload could not be resolved")
        return $null
    }

    [pscustomobject]@{ Snapshot = $snapshot; Payload = $resolved.payload }
}

function Invoke-CaptureWorkflow {
    param([Parameter(Mandatory)][string] $Label, [Parameter(Mandatory)] $RootActivity)
    $def = Invoke-Step "$Label submit" { Submit-Workflow -Ctx $ctx -Name "ValueCapture-$Label-$([guid]::NewGuid().ToString('N'))" -Description "activity input and output capture" -RootActivity $RootActivity }
    $pub = Invoke-Step "$Label publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
    $run = Invoke-Step "$Label execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
    $instance = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId -TimeoutSeconds 120
    Show-WorkflowInstance -Instance $instance
    if ($instance.instance.status -notin @('Completed', 'Finished') -or $instance.instance.incidentCount -ne 0) {
        throw "$Label workflow did not complete without incidents (status '$($instance.instance.status)')"
    }
    return $instance
}

$marker = "captured-$(Get-Random -Max 999999)"
$wl = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
$send = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Http.Activities.SendHttpRequest'
$seq = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence'
$healthUrl = "$($BaseUrl.TrimEnd('/'))/"

# Sequence supplies a representative workflow around two typed leaf activities.
$http = New-ActivityNode -NodeId "http" -VersionId $send -Inputs @(
    (New-LiteralInput -ReferenceKey "Url" -Value $healthUrl),
    (New-LiteralInput -ReferenceKey "Method" -Value "GET")
)
$writer = New-ActivityNode -NodeId "writer" -VersionId $wl -Inputs @(
    (New-LiteralInput -ReferenceKey "text" -Value $marker)
)
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($http, $writer))

$inst = Invoke-CaptureWorkflow -Label 'nested' -RootActivity $root
$bareInstance = Invoke-CaptureWorkflow -Label 'root' -RootActivity $writer

foreach ($case in @(@{ Label = 'nested WriteLine'; Instance = $inst }, @{ Label = 'root WriteLine'; Instance = $bareInstance })) {
    $execution = @($case.Instance.activities | Where-Object { $_.executableNodeId -eq 'writer' })
    if ($execution.Count -ne 1) {
        throw "$($case.Label) expected exactly one activity execution, got $($execution.Count)"
    }
    $wfId = $case.Instance.instance.workflowExecutionId
    $writerExecution = $execution[0]
    if ($writerExecution.valueSnapshotCount -lt 1) {
        [void]$script:failures.Add("$($case.Label) instance projection reports no captured values")
    }
    $writerDetail = Invoke-Step "$($case.Label) activity-execution detail" {
        Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$wfId/activity-executions/$($writerExecution.activityExecutionId)" -WebSession $ctx.Session -UseBasicParsing
    }
    $textSnapshots = @($writerDetail.valueSnapshots | Where-Object { $_.subject -ieq 'ActivityInput' -and $_.inputKey -ceq 'text' })
    if ($textSnapshots.Count -ne 1) {
        [void]$script:failures.Add("$($case.Label) expected one text input snapshot, got $($textSnapshots.Count)")
    }
    $writerEvidence = Get-ActivityEvidence -Detail $writerDetail -WorkflowExecutionId $wfId `
        -ActivityExecutionId $writerExecution.activityExecutionId -Label $case.Label -Subject 'ActivityInput' -InputKey 'text'
    if ($writerEvidence -and $writerEvidence.Payload.preview -cne $marker) {
        [void]$script:failures.Add("$($case.Label) resolved text preview did not match the authored marker")
    }
}

$wfId = $inst.instance.workflowExecutionId
$httpExecution = $inst.activities | Where-Object { $_.executableNodeId -eq 'http' } | Select-Object -First 1
if (-not $httpExecution) { throw 'Expected SendHttpRequest execution on the nested workflow instance' }
$httpDetail = Invoke-Step "SendHttpRequest activity-execution detail" {
    Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$wfId/activity-executions/$($httpExecution.activityExecutionId)" -WebSession $ctx.Session -UseBasicParsing
}

$declaredHttpInputKeys = @('Url', 'Method', 'Content', 'ContentType', 'RequestHeaders', 'ExpectedStatusCodes', 'Timeout')
$httpInputSnapshots = @($httpDetail.valueSnapshots | Where-Object { $_.subject -ieq 'ActivityInput' })
$httpInputKeys = @($httpInputSnapshots | ForEach-Object { $_.inputKey } | Sort-Object -Unique)
$unkeyedHttpInputs = @($httpInputSnapshots | Where-Object { [string]::IsNullOrWhiteSpace($_.inputKey) })
$unknownHttpInputKeys = @($httpInputSnapshots | Where-Object { $_.inputKey -and $_.inputKey -cnotin $declaredHttpInputKeys })
$missingHttpInputKeys = @($declaredHttpInputKeys | Where-Object { $_ -cnotin $httpInputKeys })
$inputKeyProblems = @()
if ($unkeyedHttpInputs.Count -gt 0) {
    $inputKeyProblems += 'one or more recorded inputs have no inputKey'
}
if ($unknownHttpInputKeys.Count -gt 0) {
    $inputKeyProblems += 'one or more recorded inputKeys are not declared by SendHttpRequest'
}
if ($missingHttpInputKeys.Count -gt 0) {
    $inputKeyProblems += "declared input keys are missing: $($missingHttpInputKeys -join ', ')"
}
if ($inputKeyProblems.Count -gt 0) {
    [void]$script:failures.Add("SendHttpRequest input keys: $($inputKeyProblems -join '; ')")
} else {
    Write-Host ("SendHttpRequest input keys: {0}" -f ($httpInputKeys -join ', '))
}

$statusEvidence = Get-ActivityEvidence -Detail $httpDetail -WorkflowExecutionId $wfId `
    -ActivityExecutionId $httpExecution.activityExecutionId -Label 'SendHttpRequest' -Subject 'ActivityOutput' -Name 'StatusCode'
$bodyEvidence = Get-ActivityEvidence -Detail $httpDetail -WorkflowExecutionId $wfId `
    -ActivityExecutionId $httpExecution.activityExecutionId -Label 'SendHttpRequest' -Subject 'ActivityOutput' -Name 'ResponseBody'

if ($statusEvidence -and ($statusEvidence.Payload.kind -ne 'number' -or "$($statusEvidence.Payload.value)" -ne '200')) {
    [void]$script:failures.Add('resolved StatusCode snapshot did not contain number 200')
}
if ($bodyEvidence) {
    $bodyPreview = $bodyEvidence.Payload.preview
    $health = if ($bodyPreview) { $bodyPreview | ConvertFrom-Json -ErrorAction SilentlyContinue } else { $null }
    if ($bodyEvidence.Payload.kind -ne 'string' -or $health.status -ne 'Healthy' -or $health.service -ne 'elsa-workbench') {
        [void]$script:failures.Add('resolved ResponseBody snapshot did not contain the Workbench health JSON')
    }
}

if ($script:failures.Count -eq 0) {
    Write-Host "SUCCESS - root and nested WriteLine input capture, canonical input keys, and SendHttpRequest output snapshots were verified." -ForegroundColor Green
} else {
    $script:failures | ForEach-Object { Write-Host "FAIL - $_" -ForegroundColor Red }
    exit 1
}
