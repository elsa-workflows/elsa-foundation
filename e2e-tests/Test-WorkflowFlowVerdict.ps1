<#
.SYNOPSIS
    Focused contract test for the single-WriteLine workflow smoke verdict; needs no server or build.
#>
[CmdletBinding()]
param(
    [string] $VerdictModulePath,
    [switch] $SkipMutationProof
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 does not populate PSScriptRoot during parameter-default binding.
if (-not $PSBoundParameters.ContainsKey('VerdictModulePath')) {
    $VerdictModulePath = Join-Path $PSScriptRoot '_WorkflowFlowVerdict.ps1'
}
. $VerdictModulePath

function Invoke-TestPowerShell {
    param([string] $ScriptPath, [string] $LogPath, [string[]] $ScriptArguments = @())
    $childPowerShell = if ($env:OS -eq 'Windows_NT') { 'powershell' } else { 'pwsh' }
    if (-not (Get-Command $childPowerShell -CommandType Application -ErrorAction SilentlyContinue)) {
        throw "Required child PowerShell executable '$childPowerShell' is unavailable."
    }
    $childArguments = @('-NoProfile')
    if ($env:OS -eq 'Windows_NT') { $childArguments += @('-ExecutionPolicy', 'Bypass') }
    $childArguments += @('-File', $ScriptPath) + $ScriptArguments
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 promotes redirected native stderr under Stop.
        $ErrorActionPreference = 'Continue'
        & $childPowerShell @childArguments *> $LogPath
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
}

function New-ValidWorkflowResult {
    @{
        instance = @{ status = 'Completed'; incidentCount = 0 }
        activities = @(@{ executableNodeId = 'write-root'; activityType = 'Elsa.Activities.Primitives.Activities.WriteLine'; status = 'Completed' })
        incidents = @()
    }
}

function Assert-Rejected {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Mutate)
    $result = New-ValidWorkflowResult
    & $Mutate $result
    try {
        Assert-WorkflowFlowResult -Instance $result
    } catch {
        Write-Host "  REJECT $Name"
        return
    }
    throw "The workflow verdict accepted invalid result: $Name"
}

Write-Host '== Single WriteLine workflow verdict =='
Assert-WorkflowFlowResult -Instance (New-ValidWorkflowResult)
Write-Host '  ACCEPT completed write-root / WriteLine with zero incidents'

$jsonResult = '{"instance":{"status":"Completed","incidentCount":0},"activities":[{"executableNodeId":"write-root","activityType":"Elsa.Activities.Primitives.Activities.WriteLine","status":"Completed"}],"incidents":[]}' | ConvertFrom-Json
Assert-WorkflowFlowResult -Instance $jsonResult
Write-Host '  ACCEPT JSON-deserialized REST-shaped result'

$cases = @(
    @{ Name = 'non-completed instance status'; Mutate = { param($r) $r.instance.status = 'Finished' } },
    @{ Name = 'missing returned activity'; Mutate = { param($r) $r.activities = @() } },
    @{ Name = 'additional returned activity'; Mutate = { param($r) $r.activities += @{ executableNodeId = 'other'; activityType = 'WriteLine'; status = 'Completed' } } },
    @{ Name = 'wrong node identity'; Mutate = { param($r) $r.activities[0].executableNodeId = 'other' } },
    @{ Name = 'wrong activity type'; Mutate = { param($r) $r.activities[0].activityType = 'Elsa.Activities.Primitives.Activities.SetVariable' } },
    @{ Name = 'non-completed activity status'; Mutate = { param($r) $r.activities[0].status = 'Faulted' } },
    @{ Name = 'reported incident count'; Mutate = { param($r) $r.instance.incidentCount = 1 } },
    @{ Name = 'missing reported incident count'; Mutate = { param($r) [void]$r.instance.Remove('incidentCount') } },
    @{ Name = 'returned incident collection'; Mutate = { param($r) $r.incidents = @(@{ message = 'injected incident' }) } },
    @{ Name = 'missing returned incident collection'; Mutate = { param($r) [void]$r.Remove('incidents') } }
)

foreach ($case in $cases) {
    Assert-Rejected -Name $case.Name -Mutate $case.Mutate
}

$passed = $cases.Count + 2
if (-not $SkipMutationProof) {
    $mutationRoot = Join-Path ([IO.Path]::GetTempPath()) "elsa-workflow-verdict-mutation-$([guid]::NewGuid().ToString('N'))"
    New-Item -Path $mutationRoot -ItemType Directory -Force | Out-Null
    $mutantPath = Join-Path $mutationRoot '_WorkflowFlowVerdict.ps1'
    $mutantLog = Join-Path $mutationRoot 'mutant-test.log'
    @'
function Assert-WorkflowFlowResult {
    param([AllowNull()] $Instance)
}
'@ | Set-Content -LiteralPath $mutantPath

    try {
        $mutantExitCode = Invoke-TestPowerShell -ScriptPath $PSCommandPath -LogPath $mutantLog -ScriptArguments @('-VerdictModulePath', $mutantPath, '-SkipMutationProof')
        $mutantOutput = Get-Content -LiteralPath $mutantLog -Raw
        if ($mutantExitCode -eq 0 -or $mutantOutput.IndexOf('The workflow verdict accepted invalid result', [StringComparison]::Ordinal) -lt 0) {
            Write-Host $mutantOutput
            throw "The negative-case tests did not kill the assertion-bypass mutant (exit $mutantExitCode)."
        }
        Write-Host '  MUTATION REJECTED: bypassing the verdict makes the negative-case test process fail'
        $passed++

        # Exercise the actual smoke script's verdict wiring with stubbed REST boundaries.
        # This proves process failure propagation, not an HTTP or running-server journey.
        $smokePath = Join-Path $mutationRoot 'Test-WorkflowFlow.ps1'
        $fixturePath = Join-Path $mutationRoot 'result.json'
        $smokeLog = Join-Path $mutationRoot 'smoke-test.log'
        $canonicalSmoke = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-WorkflowFlow.ps1') -Raw
        $canonicalSmoke | Set-Content -LiteralPath $smokePath
        Copy-Item -LiteralPath $VerdictModulePath -Destination $mutantPath -Force
        @'
$ErrorActionPreference = 'Stop'
function Invoke-Step { param($Step, [scriptblock] $Action) & $Action }
function Connect-Elsa { return @{} }
function Get-ActivityVersionId { return 'fixture-version' }
function New-ActivityNode { return @{} }
function New-LiteralInput { return @{} }
function Submit-Workflow { return @{ definition = @{ id = 'fixture-definition' }; version = @{ id = 'fixture-version' } } }
function Publish-WorkflowVersion { return @{ artifactId = 'fixture-artifact'; sourceReferenceId = 'fixture-source' } }
function Invoke-Artifact { return @{ workflowExecutionId = 'fixture-execution'; commandDispatchStatus = 'Dispatched' } }
function Wait-WorkflowInstance { return (Get-Content -LiteralPath "$PSScriptRoot/result.json" -Raw | ConvertFrom-Json) }
function Show-WorkflowInstance { }
'@ | Set-Content -LiteralPath (Join-Path $mutationRoot '_ElsaCommon.ps1')

        New-ValidWorkflowResult | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fixturePath
        $validExitCode = Invoke-TestPowerShell -ScriptPath $smokePath -LogPath $smokeLog
        if ($validExitCode -ne 0) { throw "Canonical smoke rejected a valid boundary fixture: $(Get-Content -LiteralPath $smokeLog -Raw)" }
        $invalidResult = New-ValidWorkflowResult
        $invalidResult.instance.status = 'Finished'
        $invalidResult | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fixturePath
        $invalidExitCode = Invoke-TestPowerShell -ScriptPath $smokePath -LogPath $smokeLog
        $invalidOutput = Get-Content -LiteralPath $smokeLog -Raw
        if ($invalidExitCode -eq 0 -or $invalidOutput -notlike "*Expected workflow status 'Completed'*") {
            throw "Canonical smoke failed to reject invalid status through its child exit: $invalidOutput"
        }
        Write-Host '  WIRING: canonical smoke accepts valid fixture and exits nonzero for invalid status'
        $passed++

        $assertionCall = 'Assert-WorkflowFlowResult -Instance $inst'
        if (-not $canonicalSmoke.Contains($assertionCall)) { throw 'Canonical verdict call changed; update the wiring mutation deliberately.' }
        $canonicalSmoke.Replace($assertionCall, '# verdict call removed by mutation proof') | Set-Content -LiteralPath $smokePath
        $bypassExitCode = Invoke-TestPowerShell -ScriptPath $smokePath -LogPath $smokeLog
        if ($bypassExitCode -ne 0) { throw 'Removing the verdict call did not reproduce the false-green smoke; the wiring mutation is inconclusive.' }
        Write-Host '  MUTATION REJECTED: removing the canonical verdict call falsely succeeds for the same invalid fixture'
        $passed++

    } finally {
        Remove-Item -LiteralPath $mutationRoot -Recurse -Force
    }
}

Write-Host "== verdict: $passed/$passed passed =="
