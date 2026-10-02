<#
.SYNOPSIS
    Shared helpers for the file-based workflow deployment suite (spec 147).
.DESCRIPTION
    Adds on top of _ElsaCommon.ps1: definition-file authoring (envelope array with a pinned
    definitionId and a resolved actver_* id), and the environment that composes the
    JsonWorkflowReconciliation feature on a server launch (env vars layer above shells.json,
    so no repo file is edited). The server process itself is owned through ../_ServerLifecycle.ps1:
    the suite starts its own Workbench and stops only that process.
#>

. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"

# --- definition-file authoring -------------------------------------------------

# Write one definition file (a single-envelope array) into $Folder. The root activity is a WriteLine
# with a literal text input; $ActivityVersionId must be the resolved actver_* id from the catalog.
function Write-DefinitionFile {
    param(
        [Parameter(Mandatory)][string] $Folder,
        [Parameter(Mandatory)][string] $FileName,
        [Parameter(Mandatory)][string] $DefinitionId,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $ActivityVersionId,
        [string] $Version = "1.0.0"
    )
    $envelope = @(
        @{
            definitionId = $DefinitionId
            name         = $Name
            description  = "e2e file-based deployment (spec 147)"
            version      = $Version
            state        = @{
                variables               = @()
                inputs                  = @()
                outputs                 = @()
                workflowActivityOptions = $null
                strategyOptions         = $null
                rootActivity            = (New-ActivityNode -NodeId "write-line" -VersionId $ActivityVersionId `
                    -Inputs @((New-LiteralInput -ReferenceKey "text" -Value "deployed from file")))
            }
        }
    )
    $path = Join-Path $Folder $FileName
    # ConvertTo-Json without -AsArray (PS 5.1) unwraps single-element arrays; wrap explicitly.
    $json = "[" + (($envelope | ForEach-Object { $_ | ConvertTo-Json -Depth 30 }) -join ",") + "]"
    Set-Content -Path $path -Value $json -Encoding utf8
    Write-Host "  [defs] wrote $path (definitionId=$DefinitionId, name=$Name)"
    return $path
}

# --- feature composition ----------------------------------------------------------

# Environment for a server launch (env vars sit above shells.json in precedence; presence of the section
# enables the feature). With -FolderPath the launch composes JsonWorkflowReconciliation; without it the
# variables are removed for the child, so a value left in the caller's shell cannot compose it by accident.
function Get-FileDeploymentEnvironment {
    param([string] $SourceId, [string] $FolderPath)
    $options = 'CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__'
    if (-not $FolderPath) {
        return @{ "${options}SourceId" = $null; "${options}FolderPath" = $null; "${options}PublishOnReconcile" = $null }
    }
    Write-Host "  [server] composing JsonWorkflowReconciliation (SourceId=$SourceId, FolderPath=$FolderPath, PublishOnReconcile=true)"
    return @{ "${options}SourceId" = $SourceId; "${options}FolderPath" = $FolderPath; "${options}PublishOnReconcile" = 'true' }
}

# --- assertion tally ------------------------------------------------------------

$script:FPass = 0; $script:FTotal = 0

function Assert-That {
    param([Parameter(Mandatory)][string] $Label, [Parameter(Mandatory)][bool] $Condition, [string] $Detail = "")
    $script:FTotal++
    if ($Condition) {
        $script:FPass++
        Write-Host ("  OK   {0}" -f $Label) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL {0}  {1}" -f $Label, $Detail) -ForegroundColor Red
    }
}

function Complete-FileDeploymentSuite {
    param([string] $Area = "file-based deployment")
    Write-Host ""
    if ($script:FPass -eq $script:FTotal) {
        Write-Host ("== {0}: {1}/{2} passed ==" -f $Area, $script:FPass, $script:FTotal) -ForegroundColor Green
        exit 0
    }
    Write-Host ("== {0}: {1}/{2} passed ==" -f $Area, $script:FPass, $script:FTotal) -ForegroundColor Red
    exit 1
}
