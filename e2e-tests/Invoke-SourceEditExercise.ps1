<#
.SYNOPSIS
    Replays the documented temporary WriteLine source-edit exercise and restores the checkout.
.DESCRIPTION
    Owns only the two documented source edits. The run-profile helper remains the sole owner of the
    Workbench process tree, fixed port, readiness, smoke and server-output checks.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/_SourceEditTrxVerdict.ps1"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRelative = 'tests/essentials/Activities/Runtime/Tests/WriteLineBoundInputExecutionTests.cs'
$sourceRelative = 'src/essentials/Activities/Primitives/Activities/WriteLine.cs'
$testPath = Join-Path $repoRoot ($testRelative -replace '/', [IO.Path]::DirectorySeparatorChar)
$sourcePath = Join-Path $repoRoot ($sourceRelative -replace '/', [IO.Path]::DirectorySeparatorChar)
$testProject = 'tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj'
$workbenchProject = 'src/apps/Elsa.Workbench/Elsa.Workbench.csproj'
$testFilter = 'FullyQualifiedName~WriteLineBoundInputExecutionTests'
$diagnosticsParent = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$runId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { 'local' }
$diagnosticsRoot = Join-Path $diagnosticsParent ("elsa-source-edit-{0}-{1}-{2}" -f $runId, (Get-Date -Format 'yyyyMMddHHmmss'), [guid]::NewGuid().ToString('N'))
$originalTestBytes = $null
$originalSourceBytes = $null
$journeyError = $null
$cleanupFailures = @()
$phaseResults = @()
$snapshotsCaptured = $false

function Set-SourceEditOutput {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][string] $Value)
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) { return }
    if ($Value.Contains("`r") -or $Value.Contains("`n")) { throw "The workflow output '$Name' contains a line break." }
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "$Name=$Value$([Environment]::NewLine)", $utf8WithoutBom)
}

function Add-SourceEditSummary {
    param([Parameter(Mandatory)][string[]] $Lines)
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) { return }
    $content = ($Lines -join [Environment]::NewLine) + [Environment]::NewLine
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $content, $utf8WithoutBom)
}

function Get-ByteHash {
    param([Parameter(Mandatory)][byte[]] $Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Test-BytesEqual {
    param([Parameter(Mandatory)][byte[]] $Left, [Parameter(Mandatory)][byte[]] $Right)
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) { return $false }
    }
    return $true
}

function Set-ExactUtf8Replacement {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Before, [Parameter(Mandatory)][string] $After)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $offset = 0
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if ($hasBom) { $offset = 3 }
    $decoder = New-Object System.Text.UTF8Encoding -ArgumentList @($false, $true)
    $text = $decoder.GetString($bytes, $offset, $bytes.Length - $offset)
    $first = $text.IndexOf($Before, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $text.IndexOf($Before, $first + $Before.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected exactly one documented source-edit target in '$Path'."
    }
    $changed = $text.Substring(0, $first) + $After + $text.Substring($first + $Before.Length)
    $body = (New-Object System.Text.UTF8Encoding -ArgumentList $false).GetBytes($changed)
    if (-not $hasBom) { [IO.File]::WriteAllBytes($Path, $body); return }
    $withBom = New-Object byte[] ($body.Length + 3)
    $withBom[0] = 0xEF; $withBom[1] = 0xBB; $withBom[2] = 0xBF
    [Array]::Copy($body, 0, $withBom, 3, $body.Length)
    [IO.File]::WriteAllBytes($Path, $withBom)
}

function Assert-ExerciseFilesClean {
    $status = @(& git -C $repoRoot status --porcelain -- $testRelative $sourceRelative 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify the exercise files are clean before editing.' }
    if ($status.Count -gt 0) { throw "Exercise files already have changes; refusing to edit them: $($status -join '; ')" }
}

function Invoke-LoggedCommand {
    param([Parameter(Mandatory)][string] $FilePath, [Parameter(Mandatory)][string[]] $Arguments, [Parameter(Mandatory)][string] $LogPath)
    $application = Get-Command $FilePath -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $application) { throw "Required native command '$FilePath' is unavailable." }
    $exitCode = $null
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $LASTEXITCODE = $null
        & $application.Source @Arguments *> $LogPath
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
    if ($null -eq $exitCode) { throw "Native command '$FilePath' completed without an available exit code." }
    [pscustomobject]@{ ExitCode = [int]$exitCode; LogPath = $LogPath }
}

function Invoke-SourceEditTest {
    param([Parameter(Mandatory)][string] $Phase)
    $phaseDirectory = Join-Path $diagnosticsRoot $Phase
    New-Item -Path $phaseDirectory -ItemType Directory -Force | Out-Null
    $trxName = "$Phase.trx"
    $trxPath = Join-Path $phaseDirectory $trxName
    $logPath = Join-Path $phaseDirectory 'dotnet-test.log'
    $arguments = @(
        'test', $testProject,
        '--filter', $testFilter,
        '-p:RestoreLockedMode=true',
        '--results-directory', $phaseDirectory,
        '--logger', "trx;LogFileName=$trxName"
    )
    Write-Host "[command:$Phase] dotnet $($arguments -join ' ')"
    $result = Invoke-LoggedCommand -FilePath 'dotnet' -Arguments $arguments -LogPath $logPath
    $result | Add-Member -NotePropertyName TrxPath -NotePropertyValue $trxPath
    Write-Host "[phase:$Phase] dotnet test exit code $($result.ExitCode); log and TRX retained at $phaseDirectory"
    return $result
}

function Invoke-SourceEditBuild {
    param([Parameter(Mandatory)][string] $Phase)
    $phaseDirectory = Join-Path $diagnosticsRoot $Phase
    New-Item -Path $phaseDirectory -ItemType Directory -Force | Out-Null
    $logPath = Join-Path $phaseDirectory 'dotnet-build.log'
    $arguments = @('build', $workbenchProject, '-p:RestoreLockedMode=true')
    Write-Host "[command:$Phase] dotnet $($arguments -join ' ')"
    $result = Invoke-LoggedCommand -FilePath 'dotnet' -Arguments $arguments -LogPath $logPath
    Write-Host "[phase:$Phase] dotnet build exit code $($result.ExitCode); log retained at $phaseDirectory"
    return $result
}

function Invoke-SequenceSourceSmoke {
    param([Parameter(Mandatory)][string] $DiagnosticsDirectory)
    $isWindowsRunner = $env:OS -eq 'Windows_NT'
    $childName = if ($isWindowsRunner) { 'powershell.exe' } else { 'pwsh' }
    $child = Get-Command $childName -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $child) { throw "Required smoke shell '$childName' is unavailable." }
    $arguments = @('-NoProfile')
    if ($isWindowsRunner) { $arguments += @('-ExecutionPolicy', 'Bypass') }
    $arguments += @('-File', (Join-Path $PSScriptRoot 'Invoke-RunProfileSourceJourney.ps1'), '-SequenceSmoke', '-DiagnosticsOutputName', 'sequence_diagnostics_path')
    $rawLogPath = Join-Path ([IO.Path]::GetTempPath()) ("elsa-source-edit-sequence-{0}.log" -f [guid]::NewGuid().ToString('N'))
    $evidencePath = Join-Path $DiagnosticsDirectory 'evidence.txt'
    $savedErrorActionPreference = $ErrorActionPreference
    $exitCode = $null
    try {
        $ErrorActionPreference = 'Continue'
        Write-Host "[command:sequence-smoke] $childName $($arguments -join ' ')"
        $LASTEXITCODE = $null
        & $child.Source @arguments *> $rawLogPath
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
    if ($null -eq $exitCode) { throw "The child PowerShell '$childName' completed without an available exit code." }

    try {
        $rawLines = if (Test-Path -LiteralPath $rawLogPath -PathType Leaf) { @(Get-Content -LiteralPath $rawLogPath) } else { @() }
        $safePatterns = @(
            '^\[environment\] runner=.*; image=.*; SDK=10\.[^;]+; PowerShell=[^;]+; revision=[0-9a-f]{40}$',
            '^\[readiness\] curl exit code 0; response status=ready; owned listener pid=\d+ on port 5095\.$',
            '^\[smoke\] exit code 0; the strict Sequence verdict requires .+$',
            '^\[sequence verdict\] instance=Completed reportedActivities=\d+ returnedActivities=\d+ reportedIncidents=0 returnedIncidents=0 records=[A-Za-z0-9_=,:.-]+$',
            '^\[server stdout\] exact WriteLine line observed: Onboarding: Contributor exercise$',
            '^\[cleanup\] stopped recorded process tree and released port 5095; removed only the owned diagnostics directory\.$'
        )
        $safeLines = @($rawLines | Where-Object {
            $line = $_
            @($safePatterns | Where-Object { $line -cmatch $_ }).Count -gt 0
        })
        $utf8WithoutBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
        [IO.File]::WriteAllLines($evidencePath, [string[]]$safeLines, $utf8WithoutBom)
        foreach ($line in $safeLines) { Write-Host $line }
    } finally {
        if (Test-Path -LiteralPath $rawLogPath -PathType Leaf) { Remove-Item -LiteralPath $rawLogPath -Force }
    }

    [pscustomobject]@{ ExitCode = [int]$exitCode; LogPath = $evidencePath }
}

function Assert-NoTrackedLockChanges {
    $lockDiff = @(& git -C $repoRoot diff --exit-code -- ':(glob)**/packages.lock.json' 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'A tracked packages.lock.json changed during the source-edit exercise.' }
    $cachedLockDiff = @(& git -C $repoRoot diff --cached --exit-code -- ':(glob)**/packages.lock.json' 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'A staged packages.lock.json change is present after the source-edit exercise.' }
}

New-Item -Path $diagnosticsRoot -ItemType Directory -Force | Out-Null
Set-SourceEditOutput -Name 'diagnostics_path' -Value $diagnosticsRoot

try {
    Assert-ExerciseFilesClean
    $originalTestBytes = [IO.File]::ReadAllBytes($testPath)
    $originalSourceBytes = [IO.File]::ReadAllBytes($sourcePath)
    $testHash = Get-ByteHash -Bytes $originalTestBytes
    $sourceHash = Get-ByteHash -Bytes $originalSourceBytes
    $snapshotsCaptured = $true
    Write-Host "[preflight] clean exercise paths; original SHA256 test=$testHash source=$sourceHash"

    $baseline = Invoke-SourceEditTest -Phase 'baseline-test'
    if ($baseline.ExitCode -ne 0) { throw "Untouched focused test failed with exit code $($baseline.ExitCode); see $($baseline.LogPath)." }
    Assert-SourceEditTestPassed -TrxPath $baseline.TrxPath
    $phaseResults += 'Untouched focused test: PASSED'

    Set-ExactUtf8Replacement -Path $testPath -Before '        Assert.Equal("Hello World!", output.Trim());' -After '        Assert.Equal("Onboarding: Hello World!", output.Trim());'
    if (-not (Test-BytesEqual -Left ([IO.File]::ReadAllBytes($sourcePath)) -Right $originalSourceBytes)) {
        throw 'The expected-red phase changed the implementation file.'
    }
    $expectedRed = Invoke-SourceEditTest -Phase 'expected-red-test'
    Assert-SourceEditExpectedFailure -TrxPath $expectedRed.TrxPath -ConsoleLogPath $expectedRed.LogPath -NativeExitCode $expectedRed.ExitCode
    $expectedRedVerdict = "target=$($script:SourceEditTargetTestSuffix); outcome=Failed; expected=`"Onboarding: Hello World!`"; actual=`"Hello World!`"; nativeExit=$($expectedRed.ExitCode)"
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $expectedRed.TrxPath) 'verdict.txt'), $expectedRedVerdict + [Environment]::NewLine)
    Write-Host "[phase:expected-red-test] verdict=$expectedRedVerdict"
    $phaseResults += 'Expected-red focused test: PASSED (only the intended assertion failed)'

    Set-ExactUtf8Replacement -Path $sourcePath -Before '        Console.WriteLine(Text);' -After '        Console.WriteLine($"Onboarding: {Text}");'
    $green = Invoke-SourceEditTest -Phase 'fixed-test'
    if ($green.ExitCode -ne 0) { throw "Fixed focused test failed with exit code $($green.ExitCode); see $($green.LogPath)." }
    Assert-SourceEditTestPassed -TrxPath $green.TrxPath
    $phaseResults += 'Fixed focused test: PASSED'

    $editedBuild = Invoke-SourceEditBuild -Phase 'edited-workbench-build'
    if ($editedBuild.ExitCode -ne 0) { throw "Edited Workbench build failed with exit code $($editedBuild.ExitCode); see $($editedBuild.LogPath)." }
    $phaseResults += 'Edited Workbench build: PASSED'

    $sequenceDirectory = Join-Path $diagnosticsRoot 'sequence-smoke'
    New-Item -Path $sequenceDirectory -ItemType Directory -Force | Out-Null
    $sequence = Invoke-SequenceSourceSmoke -DiagnosticsDirectory $sequenceDirectory
    if ($sequence.ExitCode -ne 0) { throw "Owned Sequence source smoke failed with exit code $($sequence.ExitCode); see $($sequence.LogPath)." }
    $phaseResults += 'Owned Sequence smoke and exact server stdout: PASSED'
    Assert-NoTrackedLockChanges
} catch {
    $journeyError = $_.Exception.Message
} finally {
    if ($snapshotsCaptured) {
        try { [IO.File]::WriteAllBytes($testPath, $originalTestBytes) }
        catch { $cleanupFailures += "Could not restore the original focused test bytes: $($_.Exception.Message)" }
        try { [IO.File]::WriteAllBytes($sourcePath, $originalSourceBytes) }
        catch { $cleanupFailures += "Could not restore the original WriteLine source bytes: $($_.Exception.Message)" }

        try {
            if (-not (Test-BytesEqual -Left ([IO.File]::ReadAllBytes($testPath)) -Right $originalTestBytes)) {
                throw 'The focused test does not match its captured original bytes.'
            }
            if (-not (Test-BytesEqual -Left ([IO.File]::ReadAllBytes($sourcePath)) -Right $originalSourceBytes)) {
                throw 'The WriteLine source does not match its captured original bytes.'
            }
            $phaseResults += 'Byte-exact source restoration: PASSED'
        } catch { $cleanupFailures += "Source restoration verification failed: $($_.Exception.Message)" }

        if ($cleanupFailures.Count -eq 0) {
            try { Assert-ExerciseFilesClean }
            catch { $cleanupFailures += "Exercise paths remain changed after restoration: $($_.Exception.Message)" }
        }

        if ($cleanupFailures.Count -eq 0) {
            try {
                $restoredTest = Invoke-SourceEditTest -Phase 'restored-test'
                if ($restoredTest.ExitCode -ne 0) { throw "Restored focused test failed with exit code $($restoredTest.ExitCode); see $($restoredTest.LogPath)." }
                Assert-SourceEditTestPassed -TrxPath $restoredTest.TrxPath
                $phaseResults += 'Restored focused test: PASSED'
            } catch { $cleanupFailures += "Could not verify restored focused test: $($_.Exception.Message)" }

            try {
                $restoredBuild = Invoke-SourceEditBuild -Phase 'restored-workbench-build'
                if ($restoredBuild.ExitCode -ne 0) { throw "Restored Workbench build failed with exit code $($restoredBuild.ExitCode); see $($restoredBuild.LogPath)." }
                Assert-NoTrackedLockChanges
                $phaseResults += 'Restored Workbench build: PASSED'
            } catch { $cleanupFailures += "Could not verify restored Workbench output: $($_.Exception.Message)" }
        }
    }

    Write-Host "[evidence] focused test TRX and command logs retained at $diagnosticsRoot"
}

if ($journeyError -or $cleanupFailures.Count -gt 0) {
    $failures = @()
    if ($journeyError) { $failures += "Source-edit failure: $journeyError" }
    foreach ($failure in $cleanupFailures) { $failures += "Restoration failure: $failure" }
    $failures += "Diagnostics retained at: $diagnosticsRoot"
    Add-SourceEditSummary -Lines (@('### Backend source-edit replay: FAILED') + $phaseResults + $failures)
    throw ($failures -join [Environment]::NewLine)
}

Add-SourceEditSummary -Lines (@('### Backend source-edit replay: PASSED') + $phaseResults + @('- The documented source and test files were restored byte-for-byte; the original Workbench output was rebuilt.', "- Focused TRX and command logs retained at $diagnosticsRoot for review."))
Write-Host "[result] all source-edit phases passed; both exercise files match the clean starting revision and the original Workbench output was rebuilt. Evidence: $diagnosticsRoot"
