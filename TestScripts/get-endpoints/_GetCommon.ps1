<#
.SYNOPSIS
    Shared helpers for the GET-endpoint coverage scripts.
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds a GET harness: `Invoke-Get` (captures status code + body, never
    throws) and `Assert-Get` (asserts an expected status and an optional body predicate, tallying OK/FAIL).
    Each coverage script exercises every reachable GET endpoint in one API area, with valid + edge parameters.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

# GET a path (absolute or relative to BaseUrl). Returns { Code, Content, Json, Ok } and never throws.
function Invoke-Get {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $Path)
    $url = if ($Path -match '^https?://') { $Path } else { "$($Ctx.BaseUrl)/$($Path.TrimStart('/'))" }
    try {
        $resp = Invoke-WebRequest $url -Method GET -WebSession $Ctx.Session -UseBasicParsing
        $json = $null; try { $json = $resp.Content | ConvertFrom-Json } catch {}
        [pscustomobject]@{ Code = [int]$resp.StatusCode; Content = $resp.Content; Json = $json; Ok = $true }
    } catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { -1 }
        $body = ""; if ($_.Exception.Response) { try { $sr = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $body = $sr.ReadToEnd() } catch {} }
        [pscustomobject]@{ Code = $code; Content = $body; Json = $null; Ok = $false }
    }
}

# Assert a GET returns ExpectStatus (default 200) and, optionally, that Validate (a predicate over the response) is true.
# Uses script-scoped $script:GetPass / $script:GetTotal counters, initialized by the caller.
function Assert-Get {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][string] $Path,
        [int[]] $ExpectStatus = @(200),
        [scriptblock] $Validate = $null
    )
    $script:GetTotal++
    $r = Invoke-Get -Ctx $Ctx -Path $Path
    $statusOk = $ExpectStatus -contains $r.Code
    $validOk = $true
    if ($statusOk -and $Validate) { try { $validOk = [bool](& $Validate $r) } catch { $validOk = $false } }
    if ($statusOk -and $validOk) {
        $script:GetPass++
        Write-Host ("  OK   [{0}] {1}" -f $r.Code, $Label) -ForegroundColor Green
    } else {
        $reason = if (-not $statusOk) { "status {0} (expected {1})" -f $r.Code, ($ExpectStatus -join '/') } else { "body validation failed" }
        Write-Host ("  FAIL [{0}] {1} -- {2}" -f $r.Code, $Label, $reason) -ForegroundColor Red
    }
    return $r
}

# Print the tally and exit non-zero on any failure.
function Complete-GetSuite {
    param([Parameter(Mandatory)][string] $Area)
    Write-Host ""
    if ($script:GetPass -eq $script:GetTotal) {
        Write-Host ("SUCCESS - {0}: {1}/{2} GET endpoints/cases passed" -f $Area, $script:GetPass, $script:GetTotal) -ForegroundColor Green
    } else {
        Write-Host ("MISMATCH - {0}: {1}/{2} passed" -f $Area, $script:GetPass, $script:GetTotal) -ForegroundColor Red
        exit 1
    }
}
