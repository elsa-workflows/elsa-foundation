<#
.SYNOPSIS
    Independent JSON sources import through one reconcile startup pass (#1158).
.DESCRIPTION
    Owns a rebuilt Workbench with an isolated SQLite content root. Resolves the activity catalog ID,
    then restarts with two independent JSON catalogs. Readiness must include both imports, and the
    startup log must show exactly one successful workflow reconcile task. JSON/Git feature composition
    is covered by NodeLocalReconcilerStartupTaskTests through the real task executor.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl,
    [string] $Username = 'admin',
    [string] $Password = 'Password123!',
    [int] $StartupTimeoutSeconds = 300
)
. "$PSScriptRoot/_FileDeploymentCommon.ps1"

$BaseUrl = Resolve-ElsaServerBaseUrl -BaseUrl $BaseUrl
$contentRoot = New-ElsaContentRoot -Prefix 'elsa-reconcile-composition'
$completed = $false
try {
    Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $contentRoot -TimeoutSec $StartupTimeoutSeconds
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    $writeLineId = Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine'
    Stop-OwnedElsaServer

    $stamp = [guid]::NewGuid().ToString('N')
    $catalogs = @{}
    $definitions = @{}
    foreach ($source in @('primary', 'secondary')) {
        $folder = Join-Path $contentRoot "fixtures/$source"
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        $definitions[$source] = "wfdef-$source-$stamp"
        $catalogs[$source] = Write-DefinitionFile -Folder $folder -FileName 'catalog.json' `
            -DefinitionId $definitions[$source] -Name "reconcile-$source-$stamp" -ActivityVersionId $writeLineId
    }

    $settingsPath = Join-Path $contentRoot 'shells.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $features = $settings.CShells.Shells.default.Features
    $features | Add-Member -NotePropertyName JsonWorkflowReconciliation -Force -NotePropertyValue @{
        ReconcilerOptions = @{ DuplicateHandling = 'Throw' }
        Options = @{ SourceId = 'primary'; FilePath = $catalogs.primary }
        AdditionalSources = @(@{ SourceId = 'secondary'; FilePath = $catalogs.secondary })
    }
    $settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $settingsPath

    Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $contentRoot -TimeoutSec $StartupTimeoutSeconds
    $ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
    foreach ($source in @('primary', 'secondary')) {
        $name = [Uri]::EscapeDataString("reconcile-$source-$stamp")
        $list = Invoke-RestMethod "$BaseUrl/design/workflows/definitions?name=$name" -WebSession $ctx.Session
        $matching = @($list.items | Where-Object { $_.id -eq $definitions[$source] })
        Assert-That "$source source imported its pinned definition" ($matching.Count -eq 1) "count=$($matching.Count)"
    }
    $passes = @(Select-String -LiteralPath (Join-Path $contentRoot "server-$script:ElsaServerLaunchCount.out.log") `
        -SimpleMatch 'Startup task Elsa.Workflows.Design.Reconciliation.Services.WorkflowsVersionReconcilerStartupTask completed with outcome success')
    Assert-That 'one shared workflow reconcile startup pass' ($passes.Count -eq 1) "count=$($passes.Count)"
    $completed = $true
} finally {
    Remove-OwnedElsaServer -ContentRoot $contentRoot -KeepContentRoot:(-not $completed -or $script:FPass -ne $script:FTotal)
}
Complete-FileDeploymentSuite -Area 'reconciliation source composition'
