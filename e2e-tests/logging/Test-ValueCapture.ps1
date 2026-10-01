<#
.SYNOPSIS
    Verify activity input keys and resolve captured SendHttpRequest outputs through the value-evidence API.
.DESCRIPTION
    Runs a Sequence containing SendHttpRequest(GET /) and WriteLine("<marker>"). Activity-execution
    details expose metadata only; this script resolves selected evidence by id and inspects its bounded
    diagnostic snapshot. It does not change runtime diagnostics settings.
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

$def = Invoke-Step "submit" { Submit-Workflow -Ctx $ctx -Name "ValueCapture-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "activity input and output capture" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

$wfId = $inst.instance.workflowExecutionId
$writerExecution = $inst.activities | Where-Object { $_.executableNodeId -eq 'writer' } | Select-Object -First 1
$httpExecution = $inst.activities | Where-Object { $_.executableNodeId -eq 'http' } | Select-Object -First 1
if (-not $writerExecution -or -not $httpExecution) {
    Write-Host "FAIL - expected both WriteLine and SendHttpRequest executions on the workflow instance" -ForegroundColor Red
    exit 1
}

$writerDetail = Invoke-Step "WriteLine activity-execution detail" {
    Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$wfId/activity-executions/$($writerExecution.activityExecutionId)" -WebSession $ctx.Session -UseBasicParsing
}
$httpDetail = Invoke-Step "SendHttpRequest activity-execution detail" {
    Invoke-RestMethod "$($ctx.BaseUrl)/runtime/workflows/instances/$wfId/activity-executions/$($httpExecution.activityExecutionId)" -WebSession $ctx.Session -UseBasicParsing
}

$writerEvidence = Get-ActivityEvidence -Detail $writerDetail -WorkflowExecutionId $wfId `
    -ActivityExecutionId $writerExecution.activityExecutionId -Label 'WriteLine' -Subject 'ActivityInput' -InputKey 'text'
if ($writerEvidence -and $writerEvidence.Payload.preview -ne $marker) {
    [void]$script:failures.Add('resolved WriteLine text preview did not match the authored marker')
}

$declaredHttpInputKeys = @('Url', 'Method', 'Content', 'ContentType', 'RequestHeaders', 'ExpectedStatusCodes', 'Timeout')
$authoredHttpInputKeys = @('Url', 'Method')
$httpInputSnapshots = @($httpDetail.valueSnapshots | Where-Object { $_.subject -ieq 'ActivityInput' })
$httpInputKeys = @($httpInputSnapshots | ForEach-Object { $_.inputKey } | Sort-Object -Unique)
$unkeyedHttpInputs = @($httpInputSnapshots | Where-Object { [string]::IsNullOrWhiteSpace($_.inputKey) })
$unknownHttpInputKeys = @($httpInputSnapshots | Where-Object { $_.inputKey -and $_.inputKey -cnotin $declaredHttpInputKeys })
$missingAuthoredHttpKeys = @($authoredHttpInputKeys | Where-Object { $_ -cnotin $httpInputKeys })
$inputKeyProblems = @()
if ($unkeyedHttpInputs.Count -gt 0) {
    $inputKeyProblems += 'one or more recorded inputs have no inputKey'
}
if ($unknownHttpInputKeys.Count -gt 0) {
    $inputKeyProblems += 'one or more recorded inputKeys are not declared by SendHttpRequest'
}
if ($missingAuthoredHttpKeys.Count -gt 0) {
    $inputKeyProblems += 'authored Url or Method inputKey is missing'
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

if ($inst.instance.status -notin @('Completed', 'Finished')) {
    [void]$script:failures.Add("workflow status was '$($inst.instance.status)'")
}
if ($script:failures.Count -eq 0) {
    Write-Host "SUCCESS - canonical input keys and resolved SendHttpRequest output snapshots were verified." -ForegroundColor Green
} else {
    $script:failures | ForEach-Object { Write-Host "FAIL - $_" -ForegroundColor Red }
    exit 1
}
