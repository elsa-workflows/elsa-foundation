<#
.SYNOPSIS
    Idempotent start: an idempotencyKey dedups the START path - a duplicate delivery does not double-start.
.DESCRIPTION
    Publishes an Event START-trigger workflow (event name S). A StartOnly stimulus carrying idempotencyKey=K starts
    one instance (startedCount==1). A SECOND StartOnly with the SAME key must NOT start again: startedCount==0 and
    skippedStartCount==1, with the start entry status "SkippedDuplicate". A THIRD StartOnly with a DIFFERENT key
    starts a fresh instance (startedCount==1) - proving the dedup is keyed, not a blanket block.
    (The dedup store is process-local/in-memory by design; this asserts the single-process contract.)
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_StimuliCommon.ps1"

Write-Host "== Idempotent start (idempotencyKey dedups the start path) ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$ev  = Invoke-Step "resolve Event"     { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }

$S = "idem-evt-$(Get-Random -Max 999999)"
$start = New-EventStartNode -NodeId "start" -EventVersionId $ev -EventName $S
$tick  = New-ActivityNode -NodeId "tick" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "started") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($start, $tick))
$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "Idem-$(Get-Random -Max 9999)" -Description "start trigger" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
Start-Sleep -Milliseconds 500

$K = "delivery-$(Get-Random -Max 999999)"
$r1 = Invoke-Step "StartOnly key=K (1st)" { Invoke-Stimulus -Ctx $ctx -EventName $S -Mode "StartOnly" -IdempotencyKey $K }
$r2 = Invoke-Step "StartOnly key=K (dup)" { Invoke-Stimulus -Ctx $ctx -EventName $S -Mode "StartOnly" -IdempotencyKey $K }
$r3 = Invoke-Step "StartOnly key=K2 (new)" { Invoke-Stimulus -Ctx $ctx -EventName $S -Mode "StartOnly" -IdempotencyKey "$K-other" }

$dupStatus = @($r2.starts | ForEach-Object { $_.status }) -join ','
Write-Host ("1st : startedCount={0} skipped={1}" -f $r1.startedCount, $r1.skippedStartCount)
Write-Host ("dup : startedCount={0} skipped={1} startStatus='{2}'" -f $r2.startedCount, $r2.skippedStartCount, $dupStatus)
Write-Host ("new : startedCount={0} skipped={1}" -f $r3.startedCount, $r3.skippedStartCount)

Write-Host ""
$ok = ($r1.startedCount -eq 1) -and ($r2.startedCount -eq 0 -and $r2.skippedStartCount -ge 1 -and $dupStatus -match 'SkippedDuplicate') -and ($r3.startedCount -eq 1)
if ($ok) {
    Write-Host "SUCCESS - same key started once then deduped (SkippedDuplicate); a different key started a fresh instance." -ForegroundColor Green
} else {
    Write-Host "FAIL - idempotency contract not met (see counts above)." -ForegroundColor Red
    exit 1
}
