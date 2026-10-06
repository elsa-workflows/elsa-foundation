<#
.SYNOPSIS
    Focused contract test for the single-WriteLine workflow smoke verdict; needs no server or build.
#>
[CmdletBinding()]
param(
    [string] $VerdictModulePath = (Join-Path $PSScriptRoot '_WorkflowFlowVerdict.ps1'),
    [switch] $SkipMutationProof
)

$ErrorActionPreference = 'Stop'
. $VerdictModulePath

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
        $childPowerShell = if ($env:OS -eq 'Windows_NT') { 'powershell' } else { 'pwsh' }
        if (-not (Get-Command $childPowerShell -CommandType Application -ErrorAction SilentlyContinue)) {
            throw "Required child PowerShell executable '$childPowerShell' is unavailable."
        }
        $savedErrorActionPreference = $ErrorActionPreference
        if ($env:OS -eq 'Windows_NT') {
            try {
                # Windows PowerShell 5.1 can promote redirected native stderr under Stop.
                $ErrorActionPreference = 'Continue'
                powershell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -VerdictModulePath $mutantPath -SkipMutationProof *> $mutantLog
                $mutantExitCode = $LASTEXITCODE
            } finally {
                $ErrorActionPreference = $savedErrorActionPreference
            }
        } else {
            try {
                $ErrorActionPreference = 'Continue'
                pwsh -NoProfile -File $PSCommandPath -VerdictModulePath $mutantPath -SkipMutationProof *> $mutantLog
                $mutantExitCode = $LASTEXITCODE
            } finally {
                $ErrorActionPreference = $savedErrorActionPreference
            }
        }
        $mutantOutput = Get-Content -LiteralPath $mutantLog -Raw
        if ($mutantExitCode -eq 0 -or $mutantOutput.IndexOf('The workflow verdict accepted invalid result', [StringComparison]::Ordinal) -lt 0) {
            Write-Host $mutantOutput
            throw "The negative-case tests did not kill the assertion-bypass mutant (exit $mutantExitCode)."
        }
        Write-Host '  MUTATION REJECTED: bypassing the verdict makes the negative-case test process fail'
        $passed++
    } finally {
        Remove-Item -LiteralPath $mutationRoot -Recurse -Force
    }
}

Write-Host "== verdict: $passed/$passed passed =="
