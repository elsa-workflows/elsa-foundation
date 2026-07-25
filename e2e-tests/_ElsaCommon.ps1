<#
    _ElsaCommon.ps1 - shared helpers for the Elsa backend flow tests.

    Dot-source it from a test script:
        . "$PSScriptRoot/_ElsaCommon.ps1"

    Everything here was verified live against Elsa Foundation @ 6b5996de (.NET 10,
    default SQLite composition, server on http://localhost:5095). The functions bake
    in the contract details we learned the hard way (see the notes on each).
#>

$ErrorActionPreference = "Stop"

# Run a labelled step; on failure print the step + HTTP status + ProblemDetails body, then stop.
function Invoke-Step {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][scriptblock] $Action)
    try { & $Action }
    catch {
        $resp = $_.Exception.Response
        $code = if ($resp) { try { [int]$resp.StatusCode } catch { "?" } } else { "?" }
        $body = $_.ErrorDetails.Message
        if (-not $body -and $resp) { try { $body = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch {} }
        Write-Host ""
        Write-Host "FAILED at step: $Step  (HTTP $code)" -ForegroundColor Red
        Write-Host ($(if ($body) { $body } else { $_.Exception.Message })) -ForegroundColor Yellow
        exit 1
    }
}

# Health-check + login. Returns a hashtable @{ Session; Login; BaseUrl }.
# NOTE: login returns a session object, not a token - auth rides an HttpOnly cookie,
# so every later call must pass -WebSession $ctx.Session.
function Connect-Elsa {
    param([string] $BaseUrl = "http://localhost:5095", [string] $Username = "admin", [string] $Password = "Password123!")
    $ctx = $null
    Invoke-Step "health check (server at $BaseUrl)" {
        $h = Invoke-RestMethod "$BaseUrl/" -TimeoutSec 10
        Write-Host ("[login] health : {0}" -f $h.status)
    }
    Invoke-Step "login" {
        $login = Invoke-RestMethod "$BaseUrl/_elsa/identity/login" -Method Post -SessionVariable session `
            -ContentType 'application/json' -Body (@{ username = $Username; password = $Password } | ConvertTo-Json)
        $script:__elsaCtx = @{ Session = $session; Login = $login; BaseUrl = $BaseUrl }
        Write-Host ("[login] auth   : {0} as {1}" -f $login.status, $login.displayName)
    }
    return $script:__elsaCtx
}

# Resolve an activity's current (head) version id by its CLR type key, e.g.
# 'Elsa.Activities.Primitives.Activities.WriteLine'. Search server-side so the result is
# not hidden beyond the first bounded management page.
function Get-ActivityVersionId {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $TypeKey)
    $search = [Uri]::EscapeDataString($TypeKey)
    $acts = Invoke-RestMethod "$($Ctx.BaseUrl)/design/activities/definitions?search=$search" -WebSession $Ctx.Session
    $hit = $acts.items | Where-Object { $_.definition.activityTypeKey -eq $TypeKey } | Select-Object -First 1
    if (-not $hit) { throw "Activity '$TypeKey' not found in the catalog." }
    return $hit.definition.headVersionId
}

# Build one authored input as a Literal binding.
# WARNING: referenceKey casing is per-activity - inspect it with
#   GET /publishing/activities/{versionId}/construct
# e.g. WriteLine uses lowercase 'text', HttpEndpoint uses 'Path'/'SupportedMethods'.
function New-LiteralInput {
    param([Parameter(Mandatory)][string] $ReferenceKey, [Parameter(Mandatory)][AllowNull()] $Value)
    @{
        referenceKey      = $ReferenceKey
        value             = @{ value = $Value; expressionType = "Literal" }
        autoEvaluate      = $null
        evaluatorType     = $null
        storageDriverType = $null
        isSensitive       = $null
    }
}

# Build an activity node. Pass -Structure for composites (e.g. a Sequence).
function New-ActivityNode {
    param(
        [Parameter(Mandatory)][string] $NodeId,
        [Parameter(Mandatory)][string] $VersionId,
        [array] $Inputs = @(),
        [array] $Outputs = @(),
        $Structure = $null
    )
    $node = @{ nodeId = $NodeId; activityVersionId = $VersionId; inputs = $Inputs; outputs = $Outputs }
    if ($Structure) { $node.structure = $Structure }
    return $node
}

# Wrap child nodes in a Sequence structure payload. Pass -Variables to declare CONTAINER-SCOPED variables
# (this is how a variable becomes visible to Set/Variable-read intrinsics in descendant nodes; declaring at
# workflow-state level is NOT enough - the runtime seeds the scope frame from the container's structure).
function New-SequenceStructure {
    param([Parameter(Mandatory)][array] $Activities, [array] $Variables = @())
    $payload = @{ activities = $Activities }
    if ($Variables.Count -gt 0) { $payload.variables = $Variables }
    @{ kind = "elsa.sequence.structure"; schemaVersion = "1.0.0"; payload = $payload }
}

# A workflow input declaration (state.inputs). A dispatched child must DECLARE the inputs a parent passes it.
function New-InputDef {
    param([Parameter(Mandatory)][string] $Key, [string] $Alias = "String")
    @{ referenceKey = $Key; name = $Key; type = @{ alias = $Alias; collectionKind = "single" }; storageDriverType = $null; displayName = $Key; category = $null; isNullable = $true }
}

# A container-scoped variable declaration (referenceKey is the key you reference from Set/Variable expressions).
function New-VariableDef {
    param([Parameter(Mandatory)][string] $Key, [string] $Alias = "String", $Default = "")
    @{ referenceKey = $Key; name = $Key; type = @{ alias = $Alias; collectionKind = "single" }; storageDriverType = $null; default = @{ value = $Default; expressionType = "Literal" } }
}

# Set intrinsic node: assigns $Value to the container-scoped variable $VariableKey. $Value can be a plain
# literal, or a hashtable @{ value=<js>; expressionType="JavaScript" } / @{ value="<key>"; expressionType="Variable" }.
function New-SetVariableNode {
    param(
        [Parameter(Mandatory)][string] $NodeId,
        [Parameter(Mandatory)][string] $VariableKey,
        [Parameter(Mandatory)][AllowNull()] $Value,
        [string] $ValueAlias = "String"
    )
    $valueBinding = if ($Value -is [hashtable]) { $Value } else { @{ value = $Value; expressionType = "Literal" } }
    @{
        nodeId            = $NodeId
        activityVersionId = "elsa.intrinsic.set@1"
        outputs           = @()
        intrinsic         = @{ kind = "set"; valueType = @{ alias = $ValueAlias; collectionKind = "single" }; variable = @{ referenceKey = $VariableKey } }
        inputs            = @( @{ referenceKey = "value"; value = $valueBinding; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null } )
    }
}

# If structure: a Then branch (condition true) and an Else branch (condition false), each a single node.
function New-IfStructure {
    param($Then, $Else)
    @{ kind = "elsa.if.structure"; schemaVersion = "1.0.0"; payload = @{ then = $Then; "else" = $Else } }
}

# Switch structure: ordered cases (each @{ match = "<value>"; activity = <node> }) plus an optional default node.
function New-SwitchStructure {
    param([Parameter(Mandatory)][array] $Cases, $Default)
    @{ kind = "elsa.switch.structure"; schemaVersion = "1.0.0"; payload = @{ cases = $Cases; "default" = $Default } }
}

# For / ForEach / While loop structures (single body node), and Parallel (fork/join) branches.
function New-ForStructure     { param([Parameter(Mandatory)] $Body) @{ kind = "elsa.for.structure";     schemaVersion = "1.0.0"; payload = @{ body = $Body } } }
function New-ForEachStructure { param([Parameter(Mandatory)] $Body) @{ kind = "elsa.foreach.structure"; schemaVersion = "1.0.0"; payload = @{ body = $Body } } }
function New-WhileStructure   { param([Parameter(Mandatory)] $Body) @{ kind = "elsa.while.structure";   schemaVersion = "1.0.0"; payload = @{ body = $Body } } }

# Parallel structure: named branches (each @{ name = "<n>"; activity = <node> }); optional completion threshold.
function New-ParallelStructure {
    param([Parameter(Mandatory)][array] $Branches, [int] $Threshold = 0)
    $payload = @{ branches = $Branches }
    if ($Threshold -gt 0) { $payload.threshold = $Threshold }
    @{ kind = "elsa.parallel.structure"; schemaVersion = "1.0.0"; payload = $payload }
}

# SetOutput intrinsic node: assigns a workflow output (surfaces under instance.outputs.<name>).
# Intrinsic nodes carry an `intrinsic` object (kind + valueType) and bypass the activity catalog.
# ValueAlias is the value's type alias (e.g. "String"); the enum kind is camelCase per the API serializer.
function New-SetOutputNode {
    param(
        [Parameter(Mandatory)][string] $NodeId,
        [Parameter(Mandatory)][string] $OutputName,
        [Parameter(Mandatory)][AllowNull()] $Value,
        [string] $ValueAlias = "String"
    )
    @{
        nodeId            = $NodeId
        activityVersionId = "elsa.intrinsic.set-output@1"
        outputs           = @()
        intrinsic         = @{ kind = "setOutput"; valueType = @{ alias = $ValueAlias; collectionKind = "single" } }
        inputs            = @(
            (New-LiteralInput -ReferenceKey "name" -Value $OutputName),
            @{ referenceKey = "value"; value = $(if ($Value -is [hashtable]) { $Value } else { @{ value = $Value; expressionType = "Literal" } }); autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null }
        )
    }
}

# Count how many times a given node executed in an instance (for loop/parallel iteration assertions).
function Get-NodeRunCount {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $NodeId)
    @($Instance.activities | Where-Object { $_.executableNodeId -eq $NodeId }).Count
}

# Submit a definition + its initial version 1.0.0 in one call. Returns the SubmittedWorkflowDefinitionView
# (.definition.id, .draftId, .version.id).
function Submit-Workflow {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $Name,
        [string] $Description = "",
        [Parameter(Mandatory)] $RootActivity,
        [array] $Variables = @(),
        [array] $Inputs = @(),
        $StrategyOptions = $null
    )
    $state = @{
        variables = $Variables; inputs = $Inputs; outputs = @()
        workflowActivityOptions = $null; strategyOptions = $StrategyOptions
        rootActivity = $RootActivity
    }
    $body = @{ name = $Name; description = $Description; state = $state } | ConvertTo-Json -Depth 30
    Invoke-RestMethod "$($Ctx.BaseUrl)/design/workflows/definitions/submit" -Method Post `
        -WebSession $Ctx.Session -ContentType 'application/json' -Body $body
}

# Publish a version into an executable artifact. Returns .artifactId.
function Publish-WorkflowVersion {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $VersionId)
    Invoke-RestMethod "$($Ctx.BaseUrl)/publishing/workflows/$VersionId/publish" -Method Post `
        -WebSession $Ctx.Session -ContentType 'application/json' -Body '{}'
}

# Execute a published artifact. NOTE the 'executables' path segment.
# Pass -SourceReferenceId (from the publish response) to pin exactly one publication: a content-addressed
# artifact can accumulate several live published references when the same workflow content is published more
# than once, and executing by bare artifact id then 409s ("must identify exactly one").
function Invoke-Artifact {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $ArtifactId, [string] $SourceReferenceId)
    $body = if ($SourceReferenceId) { @{ sourceReferenceId = $SourceReferenceId } | ConvertTo-Json } else { '{}' }
    Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/executables/$ArtifactId/execute" -Method Post `
        -WebSession $Ctx.Session -ContentType 'application/json' -Body $body
}

# Observability: fetch instance detail (instance + activity executions + incidents).
function Get-WorkflowInstance {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $ExecutionId)
    Invoke-RestMethod "$($Ctx.BaseUrl)/runtime/workflows/instances/$ExecutionId" -WebSession $Ctx.Session
}

# Poll instance detail until the run reaches a terminal status (or timeout). Execution is async,
# so callers should wait before asserting on the result.
function Wait-WorkflowInstance {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $ExecutionId, [int] $TimeoutSeconds = 15)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $inst = Get-WorkflowInstance -Ctx $Ctx -ExecutionId $ExecutionId
        $status = $inst.instance.status
    } while ($status -notin @('Completed', 'Faulted', 'Cancelled', 'Finished') -and (Get-Date) -lt $deadline)
    return $inst
}

# Pretty-print an instance-detail payload (status, counts, per-activity list in run order).
function Show-WorkflowInstance {
    param([Parameter(Mandatory)] $Instance)
    $i = $Instance.instance
    Write-Host ("  instance   : status={0}  activities={1}  incidents={2}" -f $i.status, $i.activityCount, $i.incidentCount)
    foreach ($a in ($Instance.activities | Sort-Object startedAt)) {
        $outcomes = if ($a.outcomeNames) { " -> " + ($a.outcomeNames -join ",") } else { "" }
        Write-Host ("    - {0,-14} [{1}] {2}{3}" -f $a.executableNodeId, $a.status, ($a.activityType -replace '^.*\.', ''), $outcomes)
    }
    if ($Instance.incidents.Count -gt 0) {
        Write-Host "  incidents  :" -ForegroundColor Yellow
        $Instance.incidents | ForEach-Object { Write-Host ("    ! {0}" -f ($_ | ConvertTo-Json -Compress)) -ForegroundColor Yellow }
    }
}
