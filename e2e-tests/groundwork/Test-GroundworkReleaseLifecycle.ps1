<#
.SYNOPSIS
    Groundwork release-adoption proof: author -> save/reload -> publish -> execute/suspend -> resume.
.DESCRIPTION
    Drives the real Elsa.Workbench HTTP API against its GroundworkUnifiedPersistenceSqlite composition.
    The version GET after submit is the persistence round-trip; the Event bookmark and later completion
    prove the published executable and runtime checkpoint survive the Groundwork store boundary.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!"
)
. "$PSScriptRoot/../durability/_DurabilityCommon.ps1"

Write-Host "== Groundwork release lifecycle ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password
$sequence = Invoke-Step "resolve Sequence" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Sequence.Activities.Sequence' }
$event = Invoke-Step "resolve Event" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.Event' }
$tag = Get-Random -Maximum 999999
$eventName = "groundwork-release-$tag"
$expectedOutput = "groundwork-0.4.0-preview.1-$tag"

$wait = New-EventWaitNode -NodeId "wait" -EventVersionId $event -EventName $eventName
$complete = New-SetOutputNode -NodeId "complete" -OutputName "GroundworkRelease" -Value $expectedOutput
$root = New-ActivityNode -NodeId "root" -VersionId $sequence `
    -Structure (New-SequenceStructure -Activities @($wait, $complete))

$submitted = Invoke-Step "author and save" {
    Submit-Workflow -Ctx $ctx -Name "GroundworkRelease-$tag" `
        -Description "Groundwork 0.4 release adoption lifecycle" -RootActivity $root
}
$versionId = $submitted.version.id
$reloaded = Invoke-Step "reload saved version" {
    Invoke-RestMethod "$($ctx.BaseUrl)/design/workflows/versions/$versionId" -WebSession $ctx.Session
}
if ($reloaded.id -ne $versionId -or $reloaded.state.rootActivity.nodeId -ne "root") {
    throw "Saved workflow did not round-trip through the design store. Expected version '$versionId' and root 'root'."
}
Write-Host ("[reload] version={0} root={1}" -f $reloaded.id, $reloaded.state.rootActivity.nodeId)

$published = Invoke-Step "publish reloaded version" {
    Publish-WorkflowVersion -Ctx $ctx -VersionId $reloaded.id
}
$run = Invoke-Step "execute published artifact" {
    Invoke-Artifact -Ctx $ctx -ArtifactId $published.artifactId -SourceReferenceId $published.sourceReferenceId
}
$executionId = $run.workflowExecutionId

$suspended = $null
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    $suspended = Get-WorkflowInstance -Ctx $ctx -ExecutionId $executionId
    if (Test-NodeSuspended -Instance $suspended -NodeId "wait") { break }
}
if (-not (Test-NodeSuspended -Instance $suspended -NodeId "wait")) {
    Show-WorkflowInstance -Instance $suspended
    throw "Published workflow did not suspend at its persisted Event bookmark."
}
Write-Host "[execute] persisted Event bookmark is suspended"

$resume = Invoke-Step "resume persisted bookmark" {
    Invoke-ResumeStimulus -Ctx $ctx -EventName $eventName
}
if ($resume.resumedCount -lt 1) {
    throw "Resume stimulus matched no persisted bookmark."
}

$completed = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $executionId -TimeoutSeconds 20
Show-WorkflowInstance -Instance $completed
$actualOutput = $completed.outputs.GroundworkRelease.value.preview
if ($completed.instance.status -notin @('Completed', 'Finished') -or $actualOutput -ne $expectedOutput) {
    throw "Expected completed Groundwork-backed run with output '$expectedOutput'; status='$($completed.instance.status)', output='$actualOutput'."
}

Write-Host "SUCCESS - author/save/reload/publish/execute/resume completed through the Groundwork-backed Elsa 4 host." -ForegroundColor Green
