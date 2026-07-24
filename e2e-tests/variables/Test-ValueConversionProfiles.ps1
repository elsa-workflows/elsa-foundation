<#
.SYNOPSIS
    Value-conversion profiles + variable-type descriptors: the registered converters and type catalog.
.DESCRIPTION
    Asserts the value-conversion profile registry (`publishing/value-conversion/profiles`) advertises the
    built-in `elsa.json` and `elsa.xml` converters with their source representations / target aliases, and that
    the variable-type descriptor catalog (`expressions/variable-types`) lists the well-known type aliases.
    (Conversion itself is applied at capture time, not via a standalone convert endpoint - exercised in
    Test-CrossTypeCoercion.ps1.) Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_VarCommon.ps1"

Write-Host "== Value-conversion profiles + variable-type descriptors ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$fail = @()

# --- profiles ---
$profiles = Invoke-Step "GET value-conversion/profiles" { Invoke-RestMethod "$BaseUrl/publishing/value-conversion/profiles" -WebSession $ctx.Session -UseBasicParsing }
$items = @(if ($profiles.items) { $profiles.items } else { $profiles })
$ids = @($items | ForEach-Object { $_.profile.id })
Write-Host ("  profiles: {0}" -f ($ids -join ', '))
if ($ids -contains 'elsa.json') { Write-Host "  OK  elsa.json profile registered" -ForegroundColor Green } else { $fail += "elsa.json profile" }
if ($ids -contains 'elsa.xml')  { Write-Host "  OK  elsa.xml profile registered"  -ForegroundColor Green } else { $fail += "elsa.xml profile" }
$json = $items | Where-Object { $_.profile.id -eq 'elsa.json' } | Select-Object -First 1
if ($json -and @($json.supportedSourceRepresentations).Count -ge 1 -and @($json.supportedTargetAliases).Count -ge 1) {
    Write-Host ("  OK  elsa.json capabilities: sources=[{0}] targets=[{1}]" -f (($json.supportedSourceRepresentations) -join ','), (($json.supportedTargetAliases) -join ',')) -ForegroundColor Green
} else { $fail += "elsa.json capabilities" }

# --- variable-type descriptors ---
$vt = Invoke-Step "GET expressions/variable-types" { Invoke-RestMethod "$BaseUrl/expressions/variable-types" -WebSession $ctx.Session -UseBasicParsing }
$vtJson = $vt | ConvertTo-Json -Depth 8
foreach ($alias in @('Int32','Boolean','Decimal','String','Object')) {
    if ($vtJson -match [regex]::Escape($alias)) { Write-Host ("  OK  variable-types lists '{0}'" -f $alias) -ForegroundColor Green }
    else { Write-Host ("  FAIL variable-types missing '{0}'" -f $alias) -ForegroundColor Red; $fail += "variable-type $alias" }
}

Write-Host ""
if ($fail.Count -eq 0) {
    Write-Host "SUCCESS - conversion profiles (elsa.json/elsa.xml) and variable-type catalog are as expected." -ForegroundColor Green
} else {
    Write-Host ("MISMATCH - {0}" -f ($fail -join '; ')) -ForegroundColor Red
    exit 1
}
