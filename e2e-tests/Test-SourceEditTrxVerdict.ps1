[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/_SourceEditTrxVerdict.ps1"

function New-TestTrx {
    param(
        [string] $SummaryOutcome = 'Completed',
        [string] $TestOutcome = 'Passed',
        [string] $Message = '',
        [int] $Passed = 1,
        [int] $Failed = 0,
        [int] $ErrorCount = 0,
        [int] $Total = 1,
        [int] $Executed = 1,
        [string] $TestName = 'Elsa.Activities.Runtime.Tests.WriteLineBoundInputExecutionTests.WriteLine_hydrates_plain_text_without_an_argument_or_memory_wrapper'
    )
        @"
<TestRun>
  <ResultSummary outcome="$SummaryOutcome">
    <Counters total="$Total" executed="$Executed" passed="$Passed" failed="$Failed" error="$ErrorCount" timeout="0" aborted="0" inconclusive="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" inProgress="0" />
  </ResultSummary>
  <Results>
    <UnitTestResult testName="$TestName" outcome="$TestOutcome">
      <Output><ErrorInfo><Message>$Message</Message></ErrorInfo></Output>
    </UnitTestResult>
  </Results>
</TestRun>
"@
}

function Assert-Rejected {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Action)
    try { & $Action } catch { Write-Host "  REJECT $Name"; return }
    throw "The source-edit TRX verdict accepted invalid evidence: $Name"
}

$temp = Join-Path ([IO.Path]::GetTempPath()) "elsa-source-edit-trx-$([guid]::NewGuid().ToString('N'))"
New-Item -Path $temp -ItemType Directory -Force | Out-Null
try {
    $trxPath = Join-Path $temp 'result.trx'
    $logPath = Join-Path $temp 'console.log'
    'Test run completed.' | Set-Content -LiteralPath $logPath

    New-TestTrx | Set-Content -LiteralPath $trxPath
    Assert-SourceEditTestPassed -TrxPath $trxPath
    Write-Host '  ACCEPT one completed passing focused test'

    $expectedMessage = "Assert.Equal() Failure: Strings differ.`nExpected: `"Onboarding: Hello World!`"`nActual:   `"Hello World!`""
    New-TestTrx -SummaryOutcome 'Failed' -TestOutcome 'Failed' -Message $expectedMessage -Passed 0 -Failed 1 | Set-Content -LiteralPath $trxPath
    Assert-SourceEditExpectedFailure -TrxPath $trxPath -ConsoleLogPath $logPath -NativeExitCode 1
    Write-Host '  ACCEPT exact expected-red assertion result'

    Assert-Rejected -Name 'interrupted nonzero test process' -Action { Assert-SourceEditExpectedFailure -TrxPath $trxPath -ConsoleLogPath $logPath -NativeExitCode 143 }

    New-TestTrx -SummaryOutcome 'Failed' -TestOutcome 'Failed' -Message 'Expected: "other" Actual: "value"' -Passed 0 -Failed 1 | Set-Content -LiteralPath $trxPath
    Assert-Rejected -Name 'wrong assertion output' -Action { Assert-SourceEditExpectedFailure -TrxPath $trxPath -ConsoleLogPath $logPath -NativeExitCode 1 }

    New-TestTrx -SummaryOutcome 'Failed' -TestOutcome 'Failed' -Message $expectedMessage -Passed 0 -Failed 1 -ErrorCount 1 | Set-Content -LiteralPath $trxPath
    Assert-Rejected -Name 'TRX infrastructure error counter' -Action { Assert-SourceEditExpectedFailure -TrxPath $trxPath -ConsoleLogPath $logPath -NativeExitCode 1 }

    New-TestTrx -SummaryOutcome 'Failed' -TestOutcome 'Failed' -Message $expectedMessage -Passed 0 -Failed 1 -TestName 'Another.Unrelated.Test' | Set-Content -LiteralPath $trxPath
    Assert-Rejected -Name 'wrong test identity' -Action { Assert-SourceEditExpectedFailure -TrxPath $trxPath -ConsoleLogPath $logPath -NativeExitCode 1 }

    New-TestTrx -SummaryOutcome 'Failed' -TestOutcome 'Failed' -Message $expectedMessage -Passed 0 -Failed 1 -Total 2 -Executed 2 | Set-Content -LiteralPath $trxPath
    Assert-Rejected -Name 'additional test result' -Action { Assert-SourceEditExpectedFailure -TrxPath $trxPath -ConsoleLogPath $logPath -NativeExitCode 1 }

    New-TestTrx -SummaryOutcome 'Failed' -TestOutcome 'Failed' -Message $expectedMessage -Passed 0 -Failed 1 | Set-Content -LiteralPath $trxPath
    'C:\temp\project.csproj(1,2): error NU1301: package restore failed' | Set-Content -LiteralPath $logPath
    Assert-Rejected -Name 'restore failure in console log' -Action { Assert-SourceEditExpectedFailure -TrxPath $trxPath -ConsoleLogPath $logPath -NativeExitCode 1 }

    Write-Host '== TRX verdict: 8/8 controls passed =='
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}
