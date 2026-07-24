<#
.SYNOPSIS
    Single-activity workflow: login -> submit (definition + version) -> publish -> execute -> observe.
.DESCRIPTION
    Simplest end-to-end backend flow. Builds a one-node WriteLine workflow, runs it, and prints
    the resulting instance (status + activity executions). Requires the server running from source:
        dotnet run --project src/Apps/Elsa.Server/Elsa.Server.csproj --launch-profile http
.EXAMPLE
    pwsh ./e2e-tests/Test-WorkflowFlow.ps1
    pwsh ./e2e-tests/Test-WorkflowFlow.ps1 -Message "Hi there"
#>
[CmdletBinding()]
param(
    [string] $BaseUrl  = "http://localhost:5095",
    [string] $Username = "admin",
    [string] $Password = "Password123!",
    [string] $Message  = "Hello World from the single-activity flow!"
)
. "$PSScriptRoot/_ElsaCommon.ps1"

Write-Host "== Single WriteLine workflow ==  -> $BaseUrl" -ForegroundColor Cyan
$ctx = Connect-Elsa -BaseUrl $BaseUrl -Username $Username -Password $Password

$writeLine = Invoke-Step "resolve WriteLine" { Get-ActivityVersionId -Ctx $ctx -TypeKey 'Elsa.Activities.Primitives.Activities.WriteLine' }

$root = New-ActivityNode -NodeId "write-root" -VersionId $writeLine -Inputs @(
    (New-LiteralInput -ReferenceKey "text" -Value $Message)   # WriteLine's input key is lowercase 'text'
)

$def = Invoke-Step "submit"  { Submit-Workflow -Ctx $ctx -Name "SingleWriteLine-$(Get-Date -Format HHmmss)-$(Get-Random -Max 9999)" -Description "single WriteLine" -RootActivity $root }
Write-Host ("[submit]  definition={0} version={1}" -f $def.definition.id, $def.version.id)

$pub = Invoke-Step "publish" { Publish-WorkflowVersion -Ctx $ctx -VersionId $def.version.id }
Write-Host ("[publish] artifact={0}" -f $pub.artifactId)

$run = Invoke-Step "execute" { Invoke-Artifact -Ctx $ctx -ArtifactId $pub.artifactId -SourceReferenceId $pub.sourceReferenceId }
Write-Host ("[execute] execution={0} dispatch={1}" -f $run.workflowExecutionId, $run.commandDispatchStatus)

Write-Host "[observe] instance detail:"
$inst = Wait-WorkflowInstance -Ctx $ctx -ExecutionId $run.workflowExecutionId
Show-WorkflowInstance -Instance $inst

Write-Host ""
if ($inst.instance.status -in @('Completed','Finished')) {
    Write-Host "SUCCESS - workflow completed. Look for this line in the SERVER console:" -ForegroundColor Green
    Write-Host ("  $Message")
} else {
    Write-Host ("FINISHED with status '{0}' (see incidents above)." -f $inst.instance.status) -ForegroundColor Yellow
}
