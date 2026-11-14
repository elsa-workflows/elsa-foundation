#requires -Version 7.3
<# Real REST proof. Build the opt-in fixture first; never attaches to an existing server. #>
param(
    [Parameter(Mandatory)][string] $EvidenceDirectory,
    [string] $BaseUrl,
    [string] $AssemblyPath,
    [string] $Python = 'python3'
)
. "$PSScriptRoot/../_ElsaCommon.ps1"
. "$PSScriptRoot/../_ServerLifecycle.ps1"
. "$PSScriptRoot/_Assertions.ps1"
$ErrorActionPreference = 'Stop'
if ((Test-Path $EvidenceDirectory) -and @(Get-ChildItem $EvidenceDirectory -Force).Count -gt 0) { throw 'Use a new empty evidence directory; previous attempts must be retained.' }
New-Item $EvidenceDirectory -ItemType Directory -Force | Out-Null
$EvidenceDirectory = (Resolve-Path $EvidenceDirectory).Path
Copy-Item $PSCommandPath (Join-Path $EvidenceDirectory 'driver.ps1')
foreach ($file in @('_Assertions.ps1','publication_snapshot.py','fixtures/CustomerFixture.cs','fixtures/fixture.targets')) { Copy-Item (Join-Path $PSScriptRoot $file) $EvidenceDirectory }
$success = $false
$root = $null
$sourceBefore = (& git -c core.fsmonitor=false rev-parse HEAD).Trim()
$sourceStatusBefore = @(& git -c core.fsmonitor=false status --porcelain)
$receipt = @{ status = 'running'; sourceBefore=$sourceBefore; sourceStatusBefore=$sourceStatusBefore; cases = @(); failures = @(); restart = 'not-run' }
function Save-Json($Name, $Value) { $Value | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $EvidenceDirectory "$Name.json") }
function Assert-That($Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Api([string] $Path, [string] $Method = 'Get', $Body = $null) {
    $args = @{ Uri = "$($ctx.BaseUrl)$Path"; Method = $Method; WebSession = $ctx.Session }
    if ($null -ne $Body) { $args.ContentType = 'application/json'; $args.Body = ConvertTo-Json -InputObject $Body -Depth 100 }
    Invoke-RestMethod @args
}
function Canonical($Value) { ConvertTo-Json -InputObject $Value -Depth 100 -Compress }
function Get-Context($Draft, $Source, [array] $Path) {
    Api '/design/workflows/activity-data/context' 'Post' @{
        contractVersion = '1'; definitionId = $Draft.definitionId; draftId = $Draft.id
        expectedBaseRevision = $Draft.authoringRevision; documentRevision = [guid]::NewGuid().ToString()
        nodeId = 'consumer'; inputKey = 'text'; snapshot = $Draft.state
        query = @{ includeSources = $true; skip = 0; take = 100; source = $Source; memberPath = $Path
            shapeLimits = @{ maximumDepth = 4; maximumMembers = 128 } }
    }
}
function Member-Offer($Draft, $Source, [array] $Path) {
    $answer = Get-Context $Draft $Source $Path
    Assert-That ($answer.state -eq 'supported' -and $answer.analysis.memberReadsEnabled -eq $true) 'Expected supported member analysis'
    $offers = @($answer.analysis.sources | Where-Object { $null -ne $_.binding -and $_.binding.expressionType -like '*Member' })
    Assert-That ($offers.Count -eq 1) "Expected one canonical focused member offer; got $($offers.Count)"
    return @{ answer = $answer; offer = $offers[0] }
}
function Save-Binding($Draft, $Binding) {
    $Draft.state.rootActivity.structure.payload.activities[-1].inputs = @(@{
        referenceKey = 'text'; value = $Binding; autoEvaluate = $null; evaluatorType = $null; storageDriverType = $null; isSensitive = $null
    })
    Api "/design/workflows/drafts/$($Draft.id)" 'Put' @{state=$Draft.state;layout=$Draft.layout;activityPresentation=$Draft.activityPresentation}
}
function Read-Snapshot($DefinitionId, $RootNodeId) {
    $json = & $Python "$PSScriptRoot/publication_snapshot.py" $root $DefinitionId $RootNodeId --require-published
    Assert-That ($LASTEXITCODE -eq 0) 'Publication snapshot helper failed closed'
    return ($json | ConvertFrom-Json -AsHashtable)
}
try {
    $BaseUrl = Resolve-ElsaServerBaseUrl -BaseUrl $BaseUrl
    $root = New-ElsaContentRoot -Prefix 'elsa-activity-data-selection'
    $shellPath = Join-Path $root 'shells.json'
    $shells = Get-Content $shellPath -Raw | ConvertFrom-Json -AsHashtable
    $features = $shells.CShells.Shells.default.Features
    $features['WorkflowDesignValidations']['MemberReads'] = $true
    foreach ($key in @('HttpJavaScript','JavaScriptActivities','JavaScriptEndpoints','JavaScriptExpressions','JavaScriptJintEngine','JavaScriptWorkflowsDesign','Liquid')) { $features.Remove($key) }
    $shells | ConvertTo-Json -Depth 100 | Set-Content $shellPath
    Copy-Item $shellPath (Join-Path $EvidenceDirectory 'shells.json')
    $environment = @{ DOTNET_PROCESSOR_COUNT = '2' }
    # Configuration providers must not redirect this disposable host to inherited services/stores.
    foreach ($entry in Get-ChildItem Env:) {
        if ($entry.Name -match '(__|^ConnectionStrings|^ASPNETCORE_|^DOTNET_)') { $environment[$entry.Name] = $null }
    }
    $environment.DOTNET_PROCESSOR_COUNT = '2'
    Start-OwnedElsaServer -BaseUrl $BaseUrl -ContentRoot $root -Environment $environment -AssemblyPath $AssemblyPath
    $receipt.contentRoot = $root
    $receipt.ownedProcessIds = @($script:OwnedElsaServer.Process.Id)
    $binaryDirectory = Split-Path $script:OwnedElsaServer.AssemblyPath
    $binaryBefore = @(Get-ChildItem $binaryDirectory -Filter '*.dll' | Sort-Object Name | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash} })
    Save-Json 'binary-fingerprints' $binaryBefore
    $receipt.configSha256 = (Get-FileHash $shellPath -Algorithm SHA256).Hash
    $receipt.binarySha256 = (Get-FileHash $script:OwnedElsaServer.AssemblyPath -Algorithm SHA256).Hash
    $receipt.source = (& git -c core.fsmonitor=false rev-parse HEAD).Trim()
    $receipt.snapshotHelperSha256 = (Get-FileHash "$PSScriptRoot/publication_snapshot.py" -Algorithm SHA256).Hash
    $receipt.fixtureSha256 = (Get-FileHash "$PSScriptRoot/fixtures/CustomerFixture.cs" -Algorithm SHA256).Hash
    $receipt.driverSha256 = (Get-FileHash $PSCommandPath -Algorithm SHA256).Hash
    $ctx = Connect-Elsa -BaseUrl $BaseUrl
    $sequence = Get-ActivityVersionId $ctx 'Elsa.Activities.Sequence.Activities.Sequence'
    $writeLine = Get-ActivityVersionId $ctx 'Elsa.Activities.Primitives.Activities.WriteLine'
    $customer = Get-ActivityVersionId $ctx 'Elsa.Workbench.ActivityDataSelectionProof.GetCustomer'
    $catalog = Api "/design/activities/versions/$customer"
    Assert-That (@($catalog.contract.outputs).Count -eq 1 -and $catalog.contract.outputs[0].referenceKey -ceq 'Customer' -and $catalog.contract.outputs[0].type.alias -ceq 'Elsa.Workbench.ActivityDataSelectionProof.Customer') 'Fixture must expose exactly one typed Customer object output'
    Save-Json 'customer-catalog' $catalog
    $plans = @(
        @{name='result';expected='cust-1042';path=@('customerId');source=@{expressionType='ActivityResult';value=@{producerNodeId='producer';projectionKey='Customer';isOptional=$false}}},
        @{name='inner-variable';expected='inner-id';path=@('a.b','');scope='root'},
        @{name='outer-variable';expected='outer-id';path=@('a.b','');scope='workflow'},
        @{name='declared-request';expected='declared-id';path=@('a.b','');request='payload'}
    )
    foreach ($plan in $plans) {
        $rootId = "root-$([guid]::NewGuid().ToString('N'))"
        $nodes = @()
        if ($plan.name -eq 'result') { $nodes += New-ActivityNode -NodeId 'producer' -VersionId $customer }
        $nodes += New-ActivityNode -NodeId 'consumer' -VersionId $writeLine
        $inner = @{referenceKey='inner-payload';name='Payload';type=@{alias='Object';collectionKind='Single'};storageDriverType='elsa.json';default=@{expressionType='Object';value='{"a.b":{"":"inner-id"}}'}}
        $outer = @{referenceKey='outer-payload';name='Payload';type=@{alias='Object';collectionKind='Single'};storageDriverType='elsa.json';default=@{expressionType='Object';value='{"a.b":{"":"outer-id"}}'}}
        $variables = @(); $scoped = @(); $inputs = @()
        if ($plan.scope) { $variables = @($outer); $scoped = @($inner) }
        if ($plan.name -eq 'declared-request') {
            $inputs = @(@{referenceKey='request-payload';name='payload';displayName='Request payload';category=$null;storageDriverType=$null;type=@{alias='Object';collectionKind='Single'};isNullable=$false;isRequired=$false})
        }
        $node = New-ActivityNode -NodeId $rootId -VersionId $sequence -Structure (New-SequenceStructure -Activities $nodes -Variables $scoped)
        $submitted = Submit-Workflow -Ctx $ctx -Name "Activity data proof $($plan.name) $rootId" -RootActivity $node -Variables $variables -Inputs $inputs
        $draft = Api "/design/workflows/drafts/$($submitted.draftId)"
        if ($plan.scope) { $plan.source = @{expressionType='Variable';value=@{referenceKey=$(if($plan.scope -eq 'root'){'inner-payload'}else{'outer-payload'});declaringScopeId=$(if($plan.scope -eq 'root'){$rootId}else{'workflow'})}} }
        if ($plan.request) { $plan.source = @{expressionType='WorkflowRequest';value=@{memberKey=$plan.request;path=$null}} }
        $selected = Member-Offer $draft $plan.source $plan.path
        Save-Json "$($plan.name)-analysis" $selected.answer
        if ($plan.name -eq 'result') { Assert-That ($selected.offer.shape.state -eq 'Known' -and $selected.offer.shape.type.alias -eq 'String') 'Typed result member must carry known String facts' }
        else { Assert-That ($selected.offer.shape.state -ne 'Known') 'Dynamic member facts must remain deferred' }
        Assert-That ((Canonical $selected.offer.binding.value.memberPath) -ceq (Canonical $plan.path)) 'Literal member segments changed'
        if ($plan.scope) { Assert-That ($selected.offer.binding.value.declaringScopeId -ceq $plan.source.value.declaringScopeId) 'Explicit declaring scope lost' }
        if ($plan.request) { Assert-That ($selected.offer.binding.expressionType -eq 'WorkflowRequestMember' -and $selected.offer.binding.value.memberKey -ceq $plan.request) 'Request must use canonical name, not referenceKey' }
        $saved = Save-Binding $draft $selected.offer.binding
        $reopened = Api "/design/workflows/drafts/$($draft.id)"
        Assert-That ((Canonical $reopened.state.rootActivity.structure.payload.activities[-1].inputs[0].value) -ceq (Canonical $selected.offer.binding)) 'Save/reopen changed server binding'
        $version = Api "/design/workflows/drafts/$($draft.id)/promote" 'Post' @{}
        $publication = Publish-WorkflowVersion $ctx $version.id
        Assert-That ($publication.status -eq 'Active') 'Publication not active'
        $case = @{name=$plan.name;definitionId=$draft.definitionId;draftId=$draft.id;rootNodeId=$rootId;binding=$selected.offer.binding;shape=$selected.offer.shape;publication=$publication;expected=$plan.expected;inputs=@{};status='published'}
        if ($plan.request) { $case.inputs[$plan.request] = @{'a.b'=@{''=$plan.expected}} }
        $receipt.cases += $case
        Save-Json 'receipt' $receipt
    }
    # A previous valid publication makes every scoped no-write filter a positive control.
    $result = $receipt.cases[0]
    $draft = Api "/design/workflows/drafts/$($result.draftId)"
    $rootContext = Get-Context $draft $null @()
    Save-Json 'no-input-root-discovery' $rootContext
    Assert-That (@($rootContext.analysis.sources | Where-Object {$_.binding.expressionType -in @('WorkflowRequest','WorkflowRequestMember')}).Count -eq 0) 'A workflow without input declarations must not discover request roots'
    $bad = Member-Offer $draft @{expressionType='ActivityResult';value=@{producerNodeId='producer';projectionKey='$result';isOptional=$false}} @('customer')
    Save-Json 'typefit-analysis' $bad.answer
    $null = Save-Binding $draft $bad.offer.binding
    $badVersion = Api "/design/workflows/drafts/$($draft.id)/promote" 'Post' @{}
    $before = Read-Snapshot $result.definitionId $result.rootNodeId
    Save-Json 'publication-before' $before
    $response = Invoke-WebRequest "$BaseUrl/publishing/workflows/$($badVersion.id)/publish" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck
    $problemText = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { $response.Content }
    $problem = $problemText | ConvertFrom-Json
    Save-Json 'publication-refusal' @{statusCode=[int]$response.StatusCode;problem=$problem}
    Assert-That ($response.StatusCode -eq 400 -and $problem.errorCode -eq 'VF-COER-001') 'Known Customer object to String must be refused by the actual compiler'
    $after = Read-Snapshot $result.definitionId $result.rootNodeId
    Save-Json 'publication-after' $after
    Assert-PublicationUnchanged $before $after
    $receipt.publicationRefusal = @{status='passed';code=$problem.errorCode;fingerprintsUnchanged=$true}
    # Existing unsupported authored values remain saved; the compiler and promotion gate refuse them.
    $receipt.undeclaredRequests = @()
    foreach ($kind in @('WorkflowRequest', 'WorkflowRequestMember')) {
        $draft = Api "/design/workflows/drafts/$($result.draftId)"
        $legacy = @{expressionType=$kind;value=@{memberKey='requestPayload';futureField=@{preserved=$true}}}
        $path = @()
        if ($kind -eq 'WorkflowRequestMember') { $legacy.value.memberPath=@('a.b',''); $path=@('a.b','') }
        else { $legacy.value.path=$null }
        $null = Save-Binding $draft $legacy
        $reopened = Api "/design/workflows/drafts/$($draft.id)"
        Assert-That ($reopened.state.rootActivity.structure.payload.activities[-1].inputs[0].value.expressionType -ceq $legacy.expressionType -and (Canonical $reopened.state.rootActivity.structure.payload.activities[-1].inputs[0].value.value) -ceq (Canonical $legacy.value)) 'Unsupported authored reference was changed'
        $context = Get-Context $reopened @{expressionType='WorkflowRequest';value=@{memberKey='requestPayload';path=$null}} $path
        Save-Json "undeclared-$kind-context" $context
        Assert-That ($context.state -eq 'invalid' -and @($context.analysis.sources).Count -eq 0 -and @($context.analysis.findings | Where-Object type -eq 'ActivityData/UnknownInput').Count -gt 0) 'Undeclared request must have a finding and no focused canonical offer'
        $before = Read-Snapshot $result.definitionId $result.rootNodeId
        $preflightBody = @{definitionId=$result.definitionId;state=$reopened.state;layout=@($reopened.layout | Where-Object {$null -ne $_})}
        $response = Invoke-WebRequest "$BaseUrl/publishing/workflows/preflight" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body (Canonical $preflightBody) -SkipHttpErrorCheck
        $problemText = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { $response.Content }
        $problem = $problemText | ConvertFrom-Json -AsHashtable
        Save-Json "undeclared-$kind-preflight" @{statusCode=[int]$response.StatusCode;problem=$problem}
        Assert-That ($response.StatusCode -eq 400 -and $problem.errorCode -eq 'activity-data.invalid' -and @($problem.diagnostics | Where-Object code -eq 'ActivityData/UnknownInput').Count -gt 0) 'Actual publication compiler must reject undeclared requests'
        $promotion = Invoke-WebRequest "$BaseUrl/design/workflows/drafts/$($draft.id)/promote" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck
        Assert-That ($promotion.StatusCode -eq 409) 'Promotion must preserve the invalid-draft gate'
        $after = Read-Snapshot $result.definitionId $result.rootNodeId
        Save-Json "undeclared-$kind-before" $before
        Save-Json "undeclared-$kind-after" $after
        Assert-PublicationUnchanged $before $after
        $unchanged = Api "/design/workflows/drafts/$($draft.id)"
        Assert-That ($unchanged.state.rootActivity.structure.payload.activities[-1].inputs[0].value.expressionType -ceq $legacy.expressionType -and (Canonical $unchanged.state.rootActivity.structure.payload.activities[-1].inputs[0].value.value) -ceq (Canonical $legacy.value)) 'Refusal rewrote the authored value'
        $receipt.undeclaredRequests += @{kind=$kind;status='passed';authoredBinding=$legacy;compilerStatus=400;promotionStatus=409;fingerprintsUnchanged=$true}
    }
    Restart-OwnedElsaServer
    $receipt.ownedProcessIds += $script:OwnedElsaServer.Process.Id
    $receipt.restart = 'passed-after-save-and-publish-before-execution'
    $ctx = Connect-Elsa -BaseUrl $BaseUrl
    foreach ($case in $receipt.cases) {
        $draft = Api "/design/workflows/drafts/$($case.draftId)"
        # The result draft intentionally holds the rejected replacement; execute its earlier pinned publication.
        if ($case.name -ne 'result') { Assert-That ((Canonical $draft.state.rootActivity.structure.payload.activities[-1].inputs[0].value) -ceq (Canonical $case.binding)) 'Restart changed saved canonical binding' }
        $run = Api "/runtime/workflows/executables/$($case.publication.artifactId)/execute" 'Post' @{inputs=$case.inputs;sourceReferenceId=$case.publication.sourceReferenceId}
        $instance = Wait-WorkflowInstance $ctx $run.workflowExecutionId -TimeoutSeconds 30
        Save-Json "$($case.name)-instance" $instance
        Assert-That ($instance.instance.status -eq 'Completed' -and $instance.instance.incidentCount -eq 0) "Runtime failed for $($case.name)"
        $executions = Api "/runtime/workflows/instances/$($run.workflowExecutionId)/activity-executions"
        $consumer = @($executions.items | Where-Object {$_.execution.authoredActivityId -eq 'consumer'})
        Assert-That ($consumer.Count -eq 1 -and $consumer[0].execution.status -eq 'Completed') 'Expected one Completed consumer'
        $detail = Api "/runtime/workflows/instances/$($run.workflowExecutionId)/activity-executions/$($consumer[0].execution.activityExecutionId)"
        $snapshots = @($detail.valueSnapshots | Where-Object {$_.inputKey -eq 'text'})
        Assert-That ($snapshots.Count -eq 1) 'Expected one committed text snapshot'
        $payload = Api "/runtime/workflows/instances/$($run.workflowExecutionId)/activity-executions/$($consumer[0].execution.activityExecutionId)/value-evidence/$($snapshots[0].evidenceId)/payload"
        Assert-CommittedText $payload $case.expected
        $producers = @($executions.items | Where-Object {$_.execution.authoredActivityId -eq 'producer'})
        if ($case.name -eq 'result') { Assert-That ($producers.Count -eq 1 -and $producers[0].execution.status -eq 'Completed') 'Expected one Completed Customer producer' }
        $case.runtime = @{workflowExecutionId=$run.workflowExecutionId;committedInput=$payload;producerCount=$producers.Count;consumerStatus=$consumer[0].execution.status}
        $case.status = 'passed'
        Save-Json 'receipt' $receipt
    }
    $binaryAfter = @(Get-ChildItem $binaryDirectory -Filter '*.dll' | Sort-Object Name | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash} })
    Assert-That ((Canonical $binaryBefore) -ceq (Canonical $binaryAfter)) 'Tested binaries changed while host was running'
    $receipt.status = 'passed'; $success = $true
} catch {
    $receipt.status = 'failed'; $receipt.failures += @{message=$_.Exception.Message;detail=$_.ErrorDetails.Message;stack=$_.ScriptStackTrace}
    throw
} finally {
    $receipt.sourceAfter = (& git -c core.fsmonitor=false rev-parse HEAD).Trim()
    $receipt.sourceStatusAfter = @(& git -c core.fsmonitor=false status --porcelain)
    if ($receipt.sourceAfter -cne $sourceBefore -or (Canonical $receipt.sourceStatusAfter) -cne (Canonical $sourceStatusBefore)) { $receipt.status='failed'; $success=$false; $receipt.failures += @{message='Source changed during owned proof'} }
    if ($root) {
        Stop-OwnedElsaServer
        foreach ($log in Get-ChildItem $root -Filter 'server-*.log') { Copy-Item $log.FullName $EvidenceDirectory }
        Remove-OwnedElsaServer -ContentRoot $root -KeepContentRoot:(-not $success)
        $receipt.cleanup = @{ownedProcessesExited=(@($receipt.ownedProcessIds | Where-Object {Get-Process -Id $_ -ErrorAction SilentlyContinue}).Count -eq 0);contentRootRetained=(Test-Path $root)}
    }
    Save-Json 'receipt' $receipt
}
