<#
.SYNOPSIS
    GET-endpoint coverage for the runtime API (runtime/workflows/*).
.DESCRIPTION
    Exercises every reachable GET endpoint under the runtime area against a controlled fixture (a published,
    executed workflow with a captured value snapshot), plus filter/paging parameters and negative (404) cases.
    Covered: health; executables (list/get/provenance/input-sources); instances (list/filtered/paged/get/
    incidents); activity-executions (get/descendants/layout/value-evidence payload); dispatches (list/get);
    activation slots (list/get); diagnostics settings.
    Requires the server running from source (see ../README.md).
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/_GetCommon.ps1"
$script:GetPass = 0; $script:GetTotal = 0

Write-Host "== GET coverage: runtime API ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$seq = Invoke-Step "resolve Sequence"  { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$wl  = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }
$tag = Get-Random -Max 999999

# --- fixture: Sequence[WriteLine] (nested -> captures a value snapshot), executed to completion ---
$child = New-ActivityNode -NodeId "w" -VersionId $wl -Inputs @( (New-LiteralInput -ReferenceKey "text" -Value "runtime-get $tag") )
$root = New-ActivityNode -NodeId "root" -VersionId $seq -Structure (New-SequenceStructure -Activities @($child))
$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "RuntimeGet-$tag" -RootActivity $root }
$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
$wfId = $inst.instance.workflowExecutionId
$artifactId = $pub.artifactId
$srcRef = $pub.sourceReferenceId
$ae = ($inst.activities | Where-Object { $_.executableNodeId -eq 'w' } | Select-Object -First 1).activityExecutionId
# value-snapshot evidence id (for the payload endpoint)
$detail = (Invoke-Get -Ctx $ctx -Path "runtime/workflows/instances/$wfId/activity-executions/$ae").Json
$evidenceId = ($detail.valueSnapshots | Select-Object -First 1).evidenceId
Write-Host ("[fixture] wf={0} artifact={1} ae={2} evidence={3}" -f $wfId, $artifactId, $ae, $evidenceId)
Write-Host ""

# --- health ---
Assert-Get -Ctx $ctx -Label "health /" -Path "/" -Validate { param($r) $r.Json.status -eq 'Healthy' } | Out-Null

# --- executables ---
Assert-Get -Ctx $ctx -Label "executables (list)" -Path "runtime/workflows/executables" | Out-Null
Assert-Get -Ctx $ctx -Label "executables/{artifactId}" -Path "runtime/workflows/executables/$artifactId" | Out-Null
Assert-Get -Ctx $ctx -Label "executables/{artifactId}/provenance" -Path "runtime/workflows/executables/$artifactId/provenance" | Out-Null
Assert-Get -Ctx $ctx -Label "executables/{artifactId}/source-references/{srcRef}/input-sources" -Path "runtime/workflows/executables/$artifactId/source-references/$srcRef/input-sources" | Out-Null
Assert-Get -Ctx $ctx -Label "executables/{bogus} -> 404" -Path "runtime/workflows/executables/artifact-bogus$tag" -ExpectStatus 404 | Out-Null

# --- instances ---
Assert-Get -Ctx $ctx -Label "instances (list)" -Path "runtime/workflows/instances" | Out-Null
Assert-Get -Ctx $ctx -Label "instances?status=Completed&artifactId" -Path "runtime/workflows/instances?status=Completed&artifactId=$artifactId" -Validate { param($r) @($r.Json).Count -ge 1 } | Out-Null
Assert-Get -Ctx $ctx -Label "instances/page?take=1" -Path "runtime/workflows/instances/page?take=1" -Validate { param($r) $null -ne $r.Json.items } | Out-Null
Assert-Get -Ctx $ctx -Label "instances/{id}" -Path "runtime/workflows/instances/$wfId" -Validate { param($r) $r.Json.instance.workflowExecutionId -eq $wfId } | Out-Null
Assert-Get -Ctx $ctx -Label "instances/{id}/incidents" -Path "runtime/workflows/instances/$wfId/incidents" | Out-Null
Assert-Get -Ctx $ctx -Label "instances/{bogus} -> 404" -Path "runtime/workflows/instances/wf-bogus$tag" -ExpectStatus 404 | Out-Null

# --- activity executions ---
Assert-Get -Ctx $ctx -Label "activity-executions/{ae}" -Path "runtime/workflows/instances/$wfId/activity-executions/$ae" | Out-Null
# descendants/layout are data-dependent sub-resources: 200 with data, or 404 when the ae has none (simple workflow).
Assert-Get -Ctx $ctx -Label "activity-executions/{ae}/descendants (200|404)" -Path "runtime/workflows/instances/$wfId/activity-executions/$ae/descendants" -ExpectStatus 200,404 | Out-Null
Assert-Get -Ctx $ctx -Label "activity-executions/{ae}/layout (200|404)" -Path "runtime/workflows/instances/$wfId/activity-executions/$ae/layout" -ExpectStatus 200,404 | Out-Null
if ($evidenceId) {
    Assert-Get -Ctx $ctx -Label "activity-executions/{ae}/value-evidence/{id}/payload" -Path "runtime/workflows/instances/$wfId/activity-executions/$ae/value-evidence/$evidenceId/payload" -Validate { param($r) $null -ne $r.Json.payload } | Out-Null
} else { Write-Host "  (skip value-evidence payload: no snapshot captured)" -ForegroundColor Yellow }

# --- dispatches (the list REQUIRES a parent/child/status filter; no-filter is a 400 by contract) ---
Assert-Get -Ctx $ctx -Label "dispatches?parentWorkflowExecutionId (filtered)" -Path "runtime/workflows/dispatches?parentWorkflowExecutionId=$wfId" | Out-Null
Assert-Get -Ctx $ctx -Label "dispatches (no filter) -> 400" -Path "runtime/workflows/dispatches" -ExpectStatus 400 | Out-Null
Assert-Get -Ctx $ctx -Label "dispatches/{bogus} -> 404" -Path "runtime/workflows/dispatches/dispatch-bogus$tag" -ExpectStatus 404 | Out-Null

# --- activation slots (T117: runtime owns the activation ledger) ---
Assert-Get -Ctx $ctx -Label "activation-slots/{definitionId}" -Path "runtime/workflows/activation-slots/$($def.definition.id)" -Validate { param($r) @($r.Json.items).Count -ge 1 } | Out-Null
Assert-Get -Ctx $ctx -Label "activation-slots/{definitionId}/default" -Path "runtime/workflows/activation-slots/$($def.definition.id)/default" -Validate { param($r) $r.Json.slotName -eq 'default' } | Out-Null
Assert-Get -Ctx $ctx -Label "activation-slots/{bogus} (lenient 200 empty)" -Path "runtime/workflows/activation-slots/def-bogus$tag" -ExpectStatus 200 | Out-Null
Assert-Get -Ctx $ctx -Label "activation-slots/{bogus}/default -> 404" -Path "runtime/workflows/activation-slots/def-bogus$tag/default" -ExpectStatus 404 | Out-Null

# --- diagnostics ---
Assert-Get -Ctx $ctx -Label "diagnostics/settings" -Path "runtime/workflows/diagnostics/settings" -Validate { param($r) $null -ne $r.Json.effective } | Out-Null

Complete-GetSuite -Area "runtime API"
