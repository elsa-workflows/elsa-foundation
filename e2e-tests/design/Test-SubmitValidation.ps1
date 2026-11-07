<#
.SYNOPSIS
    Headless submit refuses invalid drafts and permits a corrected retry and a committed replay (#2792).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5095',
    [string] $Username = 'admin',
    [string] $Password = 'Password123!'
)
. "$PSScriptRoot/../_ElsaCommon.ps1"

$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$writeLine = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
$operationKey = "submit-validation-$([guid]::NewGuid().ToString('N'))"
$request = @{
    operationKey = $operationKey
    name = $operationKey
    state = @{
        variables = @(); inputs = @(); outputs = @()
        rootActivity = New-ActivityNode -NodeId 'root' -VersionId 'uninstalled-submit-validation-activity'
    }
}
$url = "$($ctx.BaseUrl)/design/workflows/definitions/submit"

Invoke-Step 'submit refuses an unknown activity with node errors' {
    $refused = $false
    try {
        Invoke-RestMethod $url -Method Post -WebSession $ctx.Session -ContentType 'application/json' `
            -Body ($request | ConvertTo-Json -Depth 30) | Out-Null
    }
    catch {
        if ([int]$_.Exception.Response.StatusCode -ne 409) { throw }
        $problem = $_.ErrorDetails.Message | ConvertFrom-Json
        if ($problem.statusCode -ne 409 -or @($problem.errors.root).Count -eq 0) { throw }
        $refused = $true
    }
    if (-not $refused) { throw 'Invalid submit created a version instead of reporting draft validation errors.' }
}

$request.state.rootActivity = New-ActivityNode -NodeId 'root' -VersionId $writeLine -Inputs @(
    (New-LiteralInput -ReferenceKey 'text' -Value $operationKey)
)
$body = $request | ConvertTo-Json -Depth 30
$accepted = Invoke-Step 'corrected content reuses the refused operation key' {
    Invoke-RestMethod $url -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body
}
$replay = Invoke-Step 'committed replay returns the same definition draft and version' {
    Invoke-RestMethod $url -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body $body
}
if (-not $accepted.definition.id -or -not $accepted.draftId -or -not $accepted.version.id -or
    $accepted.definition.id -ne $replay.definition.id -or $accepted.draftId -ne $replay.draftId -or
    $accepted.version.id -ne $replay.version.id) {
    throw 'A corrected submission and its replay did not return the same persisted identities.'
}
Write-Host 'SUCCESS - invalid submit refused; corrected retry accepted; committed replay preserved all identities.' -ForegroundColor Green
