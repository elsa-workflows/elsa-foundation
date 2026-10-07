[CmdletBinding()]
param([switch] $SkipMutationProof)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/_SequenceWorkflowVerdict.ps1"

function New-ValidSequenceResult {
    param([int] $LineCount = 1)
    $activities = @(@{
        executableNodeId = 'sequence-root'
        activityType = 'Elsa.Activities.Sequence.Activities.Sequence'
        status = 'Completed'
    })
    for ($line = 0; $line -lt $LineCount; $line++) {
        $activities += @{
            executableNodeId = "line-$line"
            activityType = 'Elsa.Activities.Primitives.Activities.WriteLine'
            status = 'Completed'
        }
    }
    @{
        instance = @{ status = 'Completed'; activityCount = $LineCount + 1; incidentCount = 0 }
        activities = $activities
        incidents = @()
    }
}

function Assert-SequenceRejected {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Mutate)
    $result = New-ValidSequenceResult
    & $Mutate $result
    try {
        Assert-SequenceWorkflowResult -Instance $result -ExpectedLineCount 1
    } catch {
        Write-Host "  REJECT $Name"
        return
    }
    throw "The Sequence verdict accepted invalid result: $Name"
}

function Invoke-TestPowerShell {
    param([Parameter(Mandatory)][string] $ScriptPath, [Parameter(Mandatory)][string] $LogPath, [string[]] $ScriptArguments = @())
    $childPowerShell = if ($env:OS -eq 'Windows_NT') { 'powershell.exe' } else { 'pwsh' }
    $child = Get-Command $childPowerShell -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $child) { throw "Required test shell '$childPowerShell' is unavailable." }
    $arguments = @('-NoProfile')
    if ($env:OS -eq 'Windows_NT') { $arguments += @('-ExecutionPolicy', 'Bypass') }
    $arguments += @('-File', $ScriptPath) + $ScriptArguments
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $child.Source @arguments *> $LogPath
        return [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
}

Write-Host '== Sequence workflow result verdict =='
Assert-SequenceWorkflowResult -Instance (New-ValidSequenceResult -LineCount 1) -ExpectedLineCount 1
Write-Host '  ACCEPT one completed Sequence root and one completed WriteLine child'
Assert-SequenceWorkflowResult -Instance (New-ValidSequenceResult -LineCount 3) -ExpectedLineCount 3
Write-Host '  ACCEPT default three-line Sequence result'

$jsonResult = (New-ValidSequenceResult -LineCount 1 | ConvertTo-Json -Depth 8) | ConvertFrom-Json
Assert-SequenceWorkflowResult -Instance $jsonResult -ExpectedLineCount 1
Write-Host '  ACCEPT JSON-deserialized REST-shaped result'

$cases = @(
    @{ Name = 'non-completed instance status'; Mutate = { param($r) $r.instance.status = 'Finished' } },
    @{ Name = 'missing instance status'; Mutate = { param($r) [void]$r.instance.Remove('status') } },
    @{ Name = 'reported activity count mismatch'; Mutate = { param($r) $r.instance.activityCount = 1 } },
    @{ Name = 'missing reported activity count'; Mutate = { param($r) [void]$r.instance.Remove('activityCount') } },
    @{ Name = 'missing Sequence root'; Mutate = { param($r) $r.activities = @($r.activities | Where-Object { $_.executableNodeId -ne 'sequence-root' }) } },
    @{ Name = 'case-mismatched Sequence root identity'; Mutate = { param($r) $r.activities[0].executableNodeId = 'SEQUENCE-ROOT' } },
    @{ Name = 'wrong Sequence root type'; Mutate = { param($r) $r.activities[0].activityType = 'Elsa.Activities.Primitives.Activities.WriteLine' } },
    @{ Name = 'non-completed Sequence root'; Mutate = { param($r) $r.activities[0].status = 'Faulted' } },
    @{ Name = 'missing WriteLine child'; Mutate = { param($r) $r.activities = @($r.activities | Where-Object { $_.executableNodeId -ne 'line-0' }); $r.instance.activityCount = 1 } },
    @{ Name = 'wrong WriteLine child identity'; Mutate = { param($r) $r.activities[1].executableNodeId = 'line-1' } },
    @{ Name = 'case-mismatched WriteLine child identity'; Mutate = { param($r) $r.activities[1].executableNodeId = 'LINE-0' } },
    @{ Name = 'wrong WriteLine child type'; Mutate = { param($r) $r.activities[1].activityType = 'Elsa.Activities.Sequence.Activities.Sequence' } },
    @{ Name = 'non-completed WriteLine child'; Mutate = { param($r) $r.activities[1].status = 'Faulted' } },
    @{ Name = 'additional returned activity'; Mutate = { param($r) $r.activities += @{ executableNodeId = 'extra'; activityType = 'WriteLine'; status = 'Completed' }; $r.instance.activityCount = 3 } },
    @{ Name = 'duplicate activity node'; Mutate = { param($r) $r.activities += $r.activities[1]; $r.instance.activityCount = 3 } },
    @{ Name = 'reported incident count'; Mutate = { param($r) $r.instance.incidentCount = 1 } },
    @{ Name = 'non-numeric incident count'; Mutate = { param($r) $r.instance.incidentCount = '0' } },
    @{ Name = 'missing reported incident count'; Mutate = { param($r) [void]$r.instance.Remove('incidentCount') } },
    @{ Name = 'returned incident'; Mutate = { param($r) $r.incidents = @(@{ message = 'injected incident' }) } },
    @{ Name = 'missing incident collection'; Mutate = { param($r) [void]$r.Remove('incidents') } }
)
foreach ($case in $cases) { Assert-SequenceRejected -Name $case.Name -Mutate $case.Mutate }

$passed = 3 + $cases.Count
if (-not $SkipMutationProof) {
    $mutationRoot = Join-Path ([IO.Path]::GetTempPath()) "elsa-sequence-verdict-$([guid]::NewGuid().ToString('N'))"
    New-Item -Path $mutationRoot -ItemType Directory -Force | Out-Null
    try {
        $smokePath = Join-Path $mutationRoot 'Test-SequenceWorkflow.ps1'
        $fixturePath = Join-Path $mutationRoot 'result.json'
        $smokeLog = Join-Path $mutationRoot 'smoke.log'
        $canonicalSmoke = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-SequenceWorkflow.ps1') -Raw
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '_SequenceWorkflowVerdict.ps1') -Destination (Join-Path $mutationRoot '_SequenceWorkflowVerdict.ps1')
        $canonicalSmoke | Set-Content -LiteralPath $smokePath
        @'
$ErrorActionPreference = 'Stop'
function Invoke-Step { param($Step, [scriptblock] $Action) & $Action }
function Connect-Elsa { param($BaseUrl, $Username, $Password) return @{} }
function Get-ActivityVersionId { param($Ctx, $TypeKey) return 'fixture-version' }
function New-ActivityNode { param($NodeId, $VersionId, $Inputs, $Outputs, $Structure) return @{ nodeId = $NodeId } }
function New-LiteralInput { param($ReferenceKey, $Value) return @{ referenceKey = $ReferenceKey; value = $Value } }
function New-SequenceStructure { param($Activities, $Variables) return @{ activities = $Activities } }
function Submit-Workflow { param($Ctx, $Name, $Description, $RootActivity) return @{ definition = @{ id = 'fixture-definition' }; version = @{ id = 'fixture-version' } } }
function Publish-WorkflowVersion { param($Ctx, $VersionId) return @{ artifactId = 'fixture-artifact'; sourceReferenceId = 'fixture-source' } }
function Invoke-Artifact { param($Ctx, $ArtifactId, $SourceReferenceId) return @{ workflowExecutionId = 'fixture-execution'; commandDispatchStatus = 'Dispatched' } }
function Wait-WorkflowInstance { param($Ctx, $ExecutionId) return (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'result.json') -Raw | ConvertFrom-Json) }
function Show-WorkflowInstance { param($Instance) }
'@ | Set-Content -LiteralPath (Join-Path $mutationRoot '_ElsaCommon.ps1')

        (New-ValidSequenceResult -LineCount 1 | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $fixturePath
        $validExitCode = Invoke-TestPowerShell -ScriptPath $smokePath -LogPath $smokeLog -ScriptArguments @('-Lines', 'fixture')
        if ($validExitCode -ne 0) { throw "Canonical Sequence smoke rejected a valid fixture: $(Get-Content -LiteralPath $smokeLog -Raw)" }

        $invalidResult = New-ValidSequenceResult -LineCount 1
        $invalidResult.instance.status = 'Faulted'
        ($invalidResult | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $fixturePath
        $invalidExitCode = Invoke-TestPowerShell -ScriptPath $smokePath -LogPath $smokeLog -ScriptArguments @('-Lines', 'fixture')
        $invalidOutput = Get-Content -LiteralPath $smokeLog -Raw
        if ($invalidExitCode -eq 0 -or $invalidOutput -notlike "*Expected Sequence workflow status 'Completed'*") {
            throw "Canonical Sequence smoke did not reject invalid status through its child exit: $invalidOutput"
        }
        Write-Host '  WIRING: canonical Sequence smoke exits nonzero for an invalid result'
        $passed++

        $assertionCall = 'Assert-SequenceWorkflowResult -Instance $inst -ExpectedLineCount $Lines.Count'
        if (-not $canonicalSmoke.Contains($assertionCall)) { throw 'Canonical Sequence verdict call changed; update the wiring mutation deliberately.' }
        $mutant = $canonicalSmoke.Replace($assertionCall, '# verdict call removed by mutation proof')
        $mutant | Set-Content -LiteralPath $smokePath
        $bypassExitCode = Invoke-TestPowerShell -ScriptPath $smokePath -LogPath $smokeLog -ScriptArguments @('-Lines', 'fixture')
        if ($bypassExitCode -ne 0) { throw 'Removing the Sequence verdict call did not reproduce the false-green smoke.' }
        Write-Host '  MUTATION REJECTED: removing the canonical verdict falsely accepts the invalid fixture'
        $passed++
    } finally {
        Remove-Item -LiteralPath $mutationRoot -Recurse -Force
    }
}

Write-Host "== Sequence verdict: $passed/$passed passed =="
