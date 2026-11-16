#requires -Version 7.3
param([Parameter(Mandatory)][string] $EvidenceDirectory)
$ErrorActionPreference = 'Stop'
New-Item $EvidenceDirectory -ItemType Directory -Force | Out-Null
$directory = (Resolve-Path $EvidenceDirectory).Path
$helper = (Resolve-Path "$PSScriptRoot/_Assertions.ps1").Path.Replace("'", "''")
$results = @()
foreach ($succeeded in @($true, $false)) {
    $name = if ($succeeded) { 'success' } else { 'integrity-refusal' }
    $receipt = Join-Path $directory "$name-cleanup.txt"
    $receiptLiteral = $receipt.Replace("'", "''")
    $literal = if ($succeeded) { '$true' } else { '$false' }
    $command = ". '$helper'; try {} finally { [IO.File]::WriteAllText('$receiptLiteral', 'cleanup-and-receipt-retained') }; Assert-ProofSucceeded -Succeeded $literal"
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-Command', $command)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = $start
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        $output | Set-Content (Join-Path $directory "$name.log")
        $expected = if ($succeeded) { 0 } else { 1 }
        if ($process.ExitCode -ne $expected) { throw "$name returned $($process.ExitCode), expected $expected" }
        if (-not $succeeded -and $output -notmatch 'Activity data proof failed; inspect the retained receipt') { throw 'The negative case did not reach the actual final assertion' }
        if ((Get-Content -LiteralPath $receipt -Raw) -cne 'cleanup-and-receipt-retained') { throw 'The command did not retain cleanup evidence' }
        $results += @{name=$name;exitCode=$process.ExitCode;cleanupRetained=$true}
    } finally { $process.Dispose() }
}
$results | ConvertTo-Json | Set-Content (Join-Path $directory 'exit-guard-results.json')
Write-Output 'Final command gate: success exit0; integrity refusal exit1 after retained cleanup receipt.'
