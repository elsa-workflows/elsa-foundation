<#
.SYNOPSIS
    Shared harness for the write-endpoint coverage scripts (POST/PUT/PATCH/DELETE).
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds a mutation harness: `Invoke-Write` (any verb + optional JSON body,
    captures status, never throws) and `Assert-Write` (asserts an expected status set + optional body predicate,
    tallying OK/FAIL). All fixtures are created by the tests themselves, so destructive ops (permanent delete,
    discard) only ever touch throwaway data.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

function Invoke-Write {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][ValidateSet('POST','PUT','PATCH','DELETE','GET')][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        $Body = $null,
        [hashtable] $Headers = @{}
    )
    $url = if ($Path -match '^https?://') { $Path } else { "$($Ctx.BaseUrl)/$($Path.TrimStart('/'))" }
    $p = @{ Uri = $url; Method = $Method; WebSession = $Ctx.Session; UseBasicParsing = $true }
    if ($null -ne $Body) {
        $p.ContentType = 'application/json'
        $p.Body = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 40 }
    }
    if ($Headers.Count -gt 0) { $p.Headers = $Headers }
    try {
        $resp = Invoke-WebRequest @p
        $json = $null; try { $json = $resp.Content | ConvertFrom-Json } catch {}
        [pscustomobject]@{ Code = [int]$resp.StatusCode; Content = $resp.Content; Json = $json; Ok = $true }
    } catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { -1 }
        $body = ""; if ($_.Exception.Response) { try { $sr = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $body = $sr.ReadToEnd() } catch {} }
        [pscustomobject]@{ Code = $code; Content = $body; Json = $null; Ok = $false }
    }
}

# Assert a write returns one of ExpectStatus (default any 2xx) and, optionally, that Validate(response) is true.
# Returns the response so callers can chain (capture created ids). Uses $script:WPass / $script:WTotal.
function Assert-Write {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][ValidateSet('POST','PUT','PATCH','DELETE','GET')][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        $Body = $null,
        [int[]] $ExpectStatus = @(200,201,202,204),
        [scriptblock] $Validate = $null,
        [hashtable] $Headers = @{}
    )
    $script:WTotal++
    $r = Invoke-Write -Ctx $Ctx -Method $Method -Path $Path -Body $Body -Headers $Headers
    $statusOk = $ExpectStatus -contains $r.Code
    $validOk = $true
    if ($statusOk -and $Validate) { try { $validOk = [bool](& $Validate $r) } catch { $validOk = $false } }
    if ($statusOk -and $validOk) {
        $script:WPass++
        Write-Host ("  OK   [{0}] {1} {2}" -f $r.Code, $Method, $Label) -ForegroundColor Green
    } else {
        $reason = if (-not $statusOk) { "status {0} (expected {1})" -f $r.Code, ($ExpectStatus -join '/') } else { "body validation failed" }
        Write-Host ("  FAIL [{0}] {1} {2} -- {3}" -f $r.Code, $Method, $Label, $reason) -ForegroundColor Red
    }
    return $r
}

function Complete-WriteSuite {
    param([Parameter(Mandatory)][string] $Area)
    Write-Host ""
    if ($script:WPass -eq $script:WTotal) {
        Write-Host ("SUCCESS - {0}: {1}/{2} write endpoints/cases passed" -f $Area, $script:WPass, $script:WTotal) -ForegroundColor Green
    } else {
        Write-Host ("MISMATCH - {0}: {1}/{2} passed" -f $Area, $script:WPass, $script:WTotal) -ForegroundColor Red
        exit 1
    }
}

# Build a WorkflowDefinitionStateView (same shape Submit uses) around a root activity node.
function New-WorkflowState {
    param([Parameter(Mandatory)] $RootActivity, [array] $Variables = @(), [array] $Inputs = @())
    @{ variables = $Variables; inputs = $Inputs; outputs = @(); workflowActivityOptions = $null; strategyOptions = $null; rootActivity = $RootActivity }
}
