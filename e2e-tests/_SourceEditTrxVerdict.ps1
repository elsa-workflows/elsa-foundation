<#
    Narrow TRX contract for the documented WriteLine source-edit exercise.
    A failing dotnet exit is expected only when this exact test and assertion failed.
#>

$script:SourceEditTargetTestSuffix = 'WriteLineBoundInputExecutionTests.WriteLine_hydrates_plain_text_without_an_argument_or_memory_wrapper'

function Get-SourceEditTrxRun {
    param([Parameter(Mandatory)][string] $TrxPath)

    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) { throw "Test result file is missing: $TrxPath" }
    $document = New-Object System.Xml.XmlDocument
    $document.Load($TrxPath)
    $summaries = @($document.SelectNodes("//*[local-name()='ResultSummary']"))
    $counterNodes = @($document.SelectNodes("//*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    $results = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($summaries.Count -ne 1 -or $counterNodes.Count -ne 1) { throw 'Expected one TRX result summary and one counters element.' }

    $counters = @{}
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'inProgress')) {
        if (-not $counterNodes[0].HasAttribute($name)) { throw "TRX counters are missing '$name'." }
        $value = 0
        if (-not [int]::TryParse($counterNodes[0].GetAttribute($name), [ref]$value)) { throw "TRX counter '$name' is not an integer." }
        $counters[$name] = $value
    }
    if ($results.Count -ne $counters.total) { throw "TRX reports $($counters.total) tests but contains $($results.Count) test results." }
    if ($counters.executed -ne $counters.total) { throw "TRX executed $($counters.executed) of $($counters.total) tests." }
    foreach ($name in @('error', 'timeout', 'aborted', 'inconclusive', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'inProgress')) {
        if ($counters[$name] -ne 0) { throw "TRX reports $($counters[$name]) '$name' test outcomes." }
    }

    $targetResults = @($results | Where-Object {
        $_.GetAttribute('testName').EndsWith($script:SourceEditTargetTestSuffix, [StringComparison]::Ordinal)
    })
    if ($targetResults.Count -ne 1) { throw "TRX does not contain exactly one result for the expected source-edit test." }
    if ($results.Count -ne 1) { throw "Expected only the focused source-edit test in TRX; got $($results.Count) test results." }

    [pscustomobject]@{
        SummaryOutcome = $summaries[0].GetAttribute('outcome')
        Counters = $counters
        TestOutcome = $targetResults[0].GetAttribute('outcome')
        TestResult = $targetResults[0]
    }
}

function Assert-SourceEditTestPassed {
    param([Parameter(Mandatory)][string] $TrxPath)
    $run = Get-SourceEditTrxRun -TrxPath $TrxPath
    if ($run.SummaryOutcome -cne 'Completed' -or $run.TestOutcome -cne 'Passed' -or
        $run.Counters.total -ne 1 -or $run.Counters.executed -ne 1 -or
        $run.Counters.passed -ne 1 -or $run.Counters.failed -ne 0) {
        throw 'The focused source-edit test did not produce one completed passing target result.'
    }
}

function Assert-SourceEditExpectedFailure {
    param(
        [Parameter(Mandatory)][string] $TrxPath,
        [Parameter(Mandatory)][string] $ConsoleLogPath,
        [Parameter(Mandatory)][int] $NativeExitCode
    )
    if ($NativeExitCode -ne 1) { throw "Expected the normal dotnet test assertion-failure exit code 1; got $NativeExitCode." }
    if (-not (Test-Path -LiteralPath $ConsoleLogPath -PathType Leaf)) { throw 'The expected-red test console log is missing.' }
    $log = [IO.File]::ReadAllText($ConsoleLogPath)
    $infrastructureErrors = @(
        '(?im)^\s*.*\berror\s+(?:NU|MSB|NETSDK|CS)\d+\s*:',
        '(?im)^\s*Test Run Aborted\b',
        '(?im)^\s*No test matches the given',
        '(?im)^\s*Testhost process.*(?:crash|exited|failed)',
        '(?im)^\s*Failed to restore\b',
        '(?im)^\s*The build failed\.'
    )
    foreach ($pattern in $infrastructureErrors) {
        if ([regex]::IsMatch($log, $pattern)) { throw 'The expected-red console log contains a restore, build, discovery, or test-host failure.' }
    }

    $run = Get-SourceEditTrxRun -TrxPath $TrxPath
    if ($run.SummaryOutcome -cne 'Failed' -or $run.TestOutcome -cne 'Failed' -or
        $run.Counters.total -ne 1 -or $run.Counters.executed -ne 1 -or
        $run.Counters.passed -ne 0 -or $run.Counters.failed -ne 1) {
        throw 'The expected-red phase did not contain exactly one failed target test and no other test outcomes.'
    }
    $messages = @($run.TestResult.SelectNodes("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']"))
    if ($messages.Count -ne 1) { throw 'The target failure does not contain exactly one assertion message.' }
    $message = $messages[0].InnerText
    $expectedLine = '(?m)^\s*Expected:\s*"Onboarding: Hello World!"\s*$'
    $actualLine = '(?m)^\s*Actual:\s*"Hello World!"\s*$'
    if (-not [regex]::IsMatch($message, $expectedLine) -or -not [regex]::IsMatch($message, $actualLine)) {
        throw 'The target failure was not the intended output assertion (expected Onboarding: Hello World!, actual Hello World!).'
    }
}
