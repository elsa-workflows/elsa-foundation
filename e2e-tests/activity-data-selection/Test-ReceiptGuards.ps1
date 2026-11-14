#requires -Version 7.3
param([Parameter(Mandatory)][string] $EvidenceDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/_Assertions.ps1"
$receipt = Get-Content (Join-Path $EvidenceDirectory 'receipt.json') -Raw | ConvertFrom-Json -AsHashtable
if ($receipt.status -ne 'passed' -or @($receipt.cases).Count -ne 4 -or @($receipt.undeclaredRequests | Where-Object status -eq passed).Count -ne 2) { throw 'Controls require a real complete four-runtime/two-refusal receipt' }
foreach ($case in $receipt.cases) { Assert-CommittedText $case.runtime.committedInput $case.expected }
$refused = $false
try { Assert-CommittedText $receipt.cases[0].runtime.committedInput 'WRONG-SENTINEL' } catch { $refused = $true; Write-Host "Expected negative committed-input control: $_" }
if (-not $refused) { throw 'Committed-input oracle failed to bite' }
$before = Get-Content (Join-Path $EvidenceDirectory 'publication-before.json') -Raw | ConvertFrom-Json -AsHashtable
$after = Get-Content (Join-Path $EvidenceDirectory 'publication-after.json') -Raw | ConvertFrom-Json -AsHashtable
Assert-PublicationUnchanged $before $after
$after.elsa_publication_records.sha256 = 'changed-row-with-same-count'
$refused = $false
try { Assert-PublicationUnchanged $before $after } catch { $refused = $true; Write-Host "Expected negative publication-fingerprint control: $_" }
if (-not $refused) { throw 'Publication fingerprint oracle failed to bite' }
Write-Host 'Receipt guards: real values/fingerprints pass; two isolated negative mutations refused.'
