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
. "$PSScriptRoot/../reusable-activities/_ReusableCommon.ps1"
$ErrorActionPreference = 'Stop'
if ((Test-Path $EvidenceDirectory) -and @(Get-ChildItem $EvidenceDirectory -Force).Count -gt 0) { throw 'Use a new empty evidence directory; previous attempts must be retained.' }
New-Item $EvidenceDirectory -ItemType Directory -Force | Out-Null
$EvidenceDirectory = (Resolve-Path $EvidenceDirectory).Path
Copy-Item $PSCommandPath (Join-Path $EvidenceDirectory 'driver.ps1')
foreach ($file in @('_Assertions.ps1','publication_snapshot.py','fixtures/CustomerFixture.cs','fixtures/fixture.targets')) { Copy-Item (Join-Path $PSScriptRoot $file) $EvidenceDirectory }
Copy-Item "$PSScriptRoot/../reusable-activities/_ReusableCommon.ps1" $EvidenceDirectory
$success = $false
$root = $null
$sourceBefore = (& git -c core.fsmonitor=false rev-parse HEAD).Trim()
$sourceStatusBefore = @(& git -c core.fsmonitor=false status --porcelain)
$receipt = @{ status = 'running'; sourceBefore=$sourceBefore; sourceStatusBefore=$sourceStatusBefore; cases = @(); failures = @(); restart = 'not-run'; placedReusableCases = @(); placedBodyPublicationRefusal = @{status='not-run'} }
function Save-Json($Name, $Value) { $Value | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $EvidenceDirectory "$Name.json") }
function Assert-That($Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Api([string] $Path, [string] $Method = 'Get', $Body = $null) {
    $args = @{ Uri = "$($ctx.BaseUrl)$Path"; Method = $Method; WebSession = $ctx.Session }
    if ($null -ne $Body) { $args.ContentType = 'application/json'; $args.Body = ConvertTo-Json -InputObject $Body -Depth 100 }
    Invoke-RestMethod @args
}
function Canonical($Value) { ConvertTo-Json -InputObject $Value -Depth 100 -Compress }
function Source-Fingerprints([array] $Paths) {
    @($Paths | ForEach-Object { @{path=$_;sha256=$(if(Test-Path -LiteralPath $_){(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}else{'missing'})} })
}
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
function Bound-Input([string] $Key, $Binding) {
    @{referenceKey=$Key;value=$Binding;autoEvaluate=$null;evaluatorType=$null;storageDriverType=$null;isSensitive=$null}
}
function String-InputContract([string] $Key, [string] $Name, [bool] $Required, $Default = $null) {
    @{referenceKey=$Key;name=$Name;type=@{alias='String';collectionKind='Single'};isRequired=$Required;isNullable=$false;
        default=$(if($null -ne $Default){@{syntax='Literal';value=$Default}}else{$null});storageDriverKey='elsa.json'}
}
function Activity-Offer($Draft, [string] $NodeId, [string] $InputKey, $Source, [array] $Path, [string] $Role) {
    $answer = Api "/design/activities/drafts/$($Draft.draftId)/activity-data/context" 'Post' @{
        contractVersion='1';expectedBaseRevision=[long]$Draft.revision;documentRevision=[guid]::NewGuid().ToString()
        target=@{nodeId=$NodeId;inputKey=$InputKey};query=@{includeSources=$true;skip=0;take=100;source=$Source;memberPath=$Path;shapeLimits=@{maximumDepth=4;maximumMembers=128}}
    }
    $offers = @($answer.analysis.sources | Where-Object {$null -ne $_.binding -and $_.binding.expressionType -ceq $Role})
    Assert-That ($answer.state -eq 'supported' -and $answer.analysis.memberReadsEnabled -eq $true -and $offers.Count -eq 1) 'Expected one actual activity-draft canonical offer'
    @{answer=$answer;offer=$offers[0]}
}
function Save-ActivityDraft($Draft) {
    Api "/design/activities/drafts/$($Draft.draftId)" 'Put' @{expectedRevision=[long]$Draft.revision;contract=$Draft.contract;provider=$Draft.provider;layout=@($Draft.layout);presentationLabel=$Draft.presentationLabel}
}
function New-PlacedLeaf([string] $Name, [bool] $Incompatible = $false) {
    $producer = New-ActivityNode -NodeId 'producer' -VersionId $customer -Inputs @((Bound-Input 'customerId' @{expressionType='Input';value=@{referenceKey='customerId'}}))
    $reader = New-ActivityNode -NodeId 'consumer' -VersionId $writeLine -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'pending'))
    $setter = New-SetOutputNode -NodeId 'return-id' -OutputName 'SelectedId' -Value 'pending'
    $contract = @{contractSchemaVersion='1';inputs=@((String-InputContract 'customerId' 'CustomerId' $false 'cust-1042'));
        outputs=@(@{referenceKey='selected-id';name='SelectedId';type=@{alias='String';collectionKind='Single'};isRequired=$true;isNullable=$false;storageDriverKey='elsa.json'});
        outcomes=@(@{referenceKey='done';name='Done';isEmitted=$true})}
    $created = New-ReusableActivityDefinition -Ctx $ctx -DisplayName $Name -ContractJson (Canonical $contract) -PayloadJson (New-GraphManifestJson -SeqVersionId $sequence -ActivitiesJson (Canonical @($producer,$reader,$setter)))
    $draft = Api "/design/activities/drafts/$($created.DraftId)"
    $source = @{expressionType='ActivityResult';value=@{producerNodeId='producer';projectionKey=$(if($Incompatible){'$result'}else{'Customer'});isOptional=$false}}
    $path = @($(if($Incompatible){'customer'}else{'customerId'}))
    $body = Activity-Offer $draft 'consumer' 'text' $source $path 'ActivityResultMember'
    $returned = Activity-Offer $draft 'return-id' 'value' @{expressionType='ActivityResult';value=@{producerNodeId='producer';projectionKey='Customer';isOptional=$false}} @('customerId') 'ActivityResultMember'
    Assert-That ($body.offer.shape.state -eq 'Known' -and $returned.offer.shape.state -eq 'Known' -and $returned.offer.shape.type.alias -ceq 'String') 'Reusable body must use authoritative known Customer facts'
    if (-not $Incompatible) { Assert-That ($body.offer.shape.type.alias -ceq 'String') 'Body member must be a known String' }
    Assert-That ((Canonical @($body.offer.binding.value.memberPath)) -ceq (Canonical $path)) 'Reusable member segments changed'
    $nodes = $draft.provider.payload.rootActivity.structure.payload.activities
    $bodyNode = @($nodes | Where-Object nodeId -CEQ 'consumer')[0]
    $bodyNode.inputs = @((Bound-Input 'text' $body.offer.binding))
    $returnNode = @($nodes | Where-Object nodeId -CEQ 'return-id')[0]
    $returnNode.inputs = @($returnNode.inputs | Where-Object referenceKey -CNE 'value') + @((Bound-Input 'value' $returned.offer.binding))
    $null = Save-ActivityDraft $draft
    $reopened = Api "/design/activities/drafts/$($created.DraftId)"
    $savedNodes = $reopened.provider.payload.rootActivity.structure.payload.activities
    $savedBody = @($savedNodes | Where-Object nodeId -CEQ 'consumer')[0]
    $savedReturn = @($savedNodes | Where-Object nodeId -CEQ 'return-id')[0]
    $savedReturnValue = @($savedReturn.inputs | Where-Object referenceKey -CEQ 'value')[0]
    Assert-That ((Canonical $savedBody.inputs[0].value) -ceq (Canonical $body.offer.binding)) 'Reusable save/reopen changed canonical body binding'
    Assert-That ((Canonical $savedReturnValue.value) -ceq (Canonical $returned.offer.binding)) 'Reusable save/reopen changed canonical return binding'
    $published = Publish-ReusableDraft -Ctx $ctx -DraftId $created.DraftId -Revision $reopened.revision
    $view = Api "/design/activities/versions/$($published.VersionId)"
    Assert-That ($view.template.templateHash -ceq $published.TemplateHash -and (Canonical $view.provider.payload) -ceq (Canonical $reopened.provider.payload)) 'Immutable reusable publication differs from its actual saved provider'
    Save-Json "$Name-authoring" @{bodyAnalysis=$body.answer;returnAnalysis=$returned.answer;draft=$reopened;publication=$published;version=$view}
    @{DefinitionId=$created.DefinitionId;DraftId=$created.DraftId;ActivityTypeKey=$created.ActivityTypeKey;VersionId=$published.VersionId;SourceReferenceId=$published.SourceReferenceId;TemplateHash=$published.TemplateHash;providerPayload=$view.provider.payload}
}
function New-PlacedWrapper($Leaf, [string] $Name) {
    $children = @()
    foreach ($side in @('first','second')) {
        $children += New-ActivityNode -NodeId "call-$side" -VersionId $Leaf.VersionId -Inputs @((Bound-Input 'customerId' @{expressionType='Input';value=@{referenceKey="$($side)Id"}}))
        $children += New-ActivityNode -NodeId "outside-$side" -VersionId $writeLine -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'pending'))
    }
    $contract = @{contractSchemaVersion='1';inputs=@((String-InputContract 'firstId' 'FirstId' $true),(String-InputContract 'secondId' 'SecondId' $true));outputs=@();outcomes=@(@{referenceKey='done';name='Done';isEmitted=$true})}
    $created = New-ReusableActivityDefinition -Ctx $ctx -DisplayName $Name -ContractJson (Canonical $contract) -PayloadJson (New-GraphManifestJson -SeqVersionId $sequence -ActivitiesJson (Canonical $children))
    $draft = Api "/design/activities/drafts/$($created.DraftId)"
    foreach ($side in @('first','second')) {
        $offer = Activity-Offer $draft "outside-$side" 'text' @{expressionType='ActivityResult';value=@{producerNodeId="call-$side";projectionKey='selected-id';isOptional=$false}} @() 'ActivityResult'
        $callerNode = @($draft.provider.payload.rootActivity.structure.payload.activities | Where-Object nodeId -CEQ "outside-$side")[0]
        $callerNode.inputs = @((Bound-Input 'text' $offer.offer.binding))
        Save-Json "$Name-$side-caller-offer" $offer.answer
    }
    $null = Save-ActivityDraft $draft
    $reopened = Api "/design/activities/drafts/$($created.DraftId)"
    Assert-That ((Canonical $draft.provider.payload) -ceq (Canonical $reopened.provider.payload)) 'Wrapper save/reopen changed caller bindings or exact dependency pins'
    $published = Publish-ReusableDraft -Ctx $ctx -DraftId $created.DraftId -Revision $reopened.revision
    $dependencies = @(Get-OutboundDependencyVersionIds -Ctx $ctx -VersionId $published.VersionId)
    Assert-That ($dependencies -contains $Leaf.VersionId) 'Published wrapper lost its exact leaf version dependency'
    @{VersionId=$published.VersionId;Version=$published.Version;SourceReferenceId=$published.SourceReferenceId;TemplateHash=$published.TemplateHash;DefinitionId=$created.DefinitionId;ActivityTypeKey=$created.ActivityTypeKey}
}
function New-PlacedWorkflowCase([string] $Name, $Leaf, $Wrapper = $null) {
    $rootId = "placed-root-$([guid]::NewGuid().ToString('N'))"
    if ($null -eq $Wrapper) {
        $children = @((New-ActivityNode -NodeId 'call-c' -VersionId $Leaf.VersionId -Inputs @((New-LiteralInput -ReferenceKey 'customerId' -Value 'cust-1042'))),
            (New-ActivityNode -NodeId 'consumer' -VersionId $writeLine -Inputs @((New-LiteralInput -ReferenceKey 'text' -Value 'pending'))))
        $expected = @('cust-1042')
    } else {
        $children = @((New-ActivityNode -NodeId 'first-b' -VersionId $Wrapper.VersionId -Inputs @((New-LiteralInput -ReferenceKey 'firstId' -Value 'cust-A'),(New-LiteralInput -ReferenceKey 'secondId' -Value 'cust-B'))),
            (New-ActivityNode -NodeId 'second-b' -VersionId $Wrapper.VersionId -Inputs @((New-LiteralInput -ReferenceKey 'firstId' -Value 'cust-C'),(New-LiteralInput -ReferenceKey 'secondId' -Value 'cust-D'))))
        $expected = @('cust-A','cust-B','cust-C','cust-D')
    }
    $submitted = Submit-Workflow -Ctx $ctx -Name "$Name $rootId" -RootActivity (New-ActivityNode -NodeId $rootId -VersionId $sequence -Structure (New-SequenceStructure -Activities $children))
    $draft = Api "/design/workflows/drafts/$($submitted.draftId)"
    if ($null -eq $Wrapper) {
        $answer = Get-Context $draft @{expressionType='ActivityResult';value=@{producerNodeId='call-c';projectionKey='selected-id';isOptional=$false}} @()
        $offers = @($answer.analysis.sources | Where-Object {$null -ne $_.binding -and $_.binding.expressionType -ceq 'ActivityResult'})
        Assert-That ($answer.state -eq 'supported' -and $offers.Count -eq 1) 'Expected actual caller whole-public-output offer'
        $null = Save-Binding $draft $offers[0].binding
        Save-Json "$Name-caller-offer" $answer
    }
    $version = Api "/design/workflows/drafts/$($draft.id)/promote" 'Post' @{}
    $publication = Publish-WorkflowVersion $ctx $version.id
    Assert-That ($publication.status -eq 'Active') 'Placed parent publication not active'
    @{name=$Name;definitionId=$draft.definitionId;draftId=$draft.id;rootNodeId=$rootId;publication=$publication;leaf=$Leaf;wrapper=$Wrapper;expectedValues=$expected;frames=@();status='published'}
}
function Executable-Nodes($Node) {
    $Node
    foreach ($slot in $Node.childSlots) { foreach ($child in $slot.activities) { Executable-Nodes $child } }
}
function Compiled-Binding($Sources, [string] $NodeId, [string] $InputKey) {
    $matches = @($Sources.compiledInputs | Where-Object {$_.executableNodeId -ceq $NodeId -and $_.binding.inputKey -ceq $InputKey})
    Assert-That ($matches.Count -eq 1 -and $matches[0].accessState -eq 'allowed') 'Expected one publicly inspectable compiled input'
    $matches[0].binding
}
function Read-PlacedExecutable($Case) {
    $Case.executable = Api "/runtime/workflows/executables/$($Case.publication.artifactId)"
    $Case.inputSources = Api "/runtime/workflows/executables/$($Case.publication.artifactId)/source-references/$($Case.publication.sourceReferenceId)/input-sources"
    $Case.versionView = Api "/design/activities/versions/$($Case.leaf.VersionId)"
    $Case.wrappers = @()
    if ($null -ne $Case.wrapper) {
        $Case.wrapperView = Api "/design/activities/versions/$($Case.wrapper.VersionId)"
        $wrapperNodes = @(Executable-Nodes $Case.executable.rootActivity | Where-Object activityType -CEQ $Case.wrapper.ActivityTypeKey)
        Assert-That ($wrapperNodes.Count -eq 2) 'Nested executable must retain exactly two B boundaries'
        foreach ($node in $wrapperNodes) {
            $descendants = @(Executable-Nodes $node)
            Assert-That (@($descendants | Where-Object activityType -CEQ $Case.leaf.ActivityTypeKey).Count -eq 2) 'Each B executable must own exactly two C boundaries'
            $Case.wrappers += @{boundaryNodeId=$node.executableNodeId;subtreeNodeIds=@($descendants.executableNodeId)}
        }
    }
    $all = @(Executable-Nodes $Case.executable.rootActivity)
    $leaves = @($all | Where-Object activityType -CEQ $Case.leaf.ActivityTypeKey)
    Assert-That ($leaves.Count -eq $Case.expectedValues.Count) 'Actual executable has the wrong number of placed leaf frames'
    $index = 0
    foreach ($boundary in $leaves) {
        $nodes = @(Executable-Nodes $boundary)
        $producer = @($nodes | Where-Object activityType -CEQ 'Elsa.Workbench.ActivityDataSelectionProof.GetCustomer')
        $body = @($nodes | Where-Object activityType -CEQ 'Elsa.Activities.Primitives.Activities.WriteLine')
        $returned = @($nodes | Where-Object activityType -CEQ 'elsa.intrinsic.setoutput')
        Assert-That ($producer.Count -eq 1 -and $body.Count -eq 1 -and $returned.Count -eq 1) 'Placed leaf must contain the genuine QA producer, body reader and scoped SetOutput'
        $caller = @($Case.inputSources.compiledInputs | Where-Object {$_.binding.source -ceq 'ActivityResult' -and $_.binding.activityResult.producerExecutableNodeId -ceq $boundary.executableNodeId -and $_.binding.activityResult.projectionKey -ceq 'selected-id'})
        Assert-That ($caller.Count -eq 1 -and $caller[0].accessState -eq 'allowed') 'Expected one caller of each exact public output'
        $frame = @{boundaryNodeId=$boundary.executableNodeId;producerNodeId=$producer[0].executableNodeId;bodyNodeId=$body[0].executableNodeId;returnNodeId=$returned[0].executableNodeId;callerNodeId=$caller[0].executableNodeId;
            bodyBinding=(Compiled-Binding $Case.inputSources $body[0].executableNodeId 'text');returnBinding=(Compiled-Binding $Case.inputSources $returned[0].executableNodeId 'value');callerBinding=$caller[0].binding;expected=$Case.expectedValues[$index++]}
        if ($null -ne $Case.wrapper) {
            $owners = @($Case.wrappers | Where-Object {$_.subtreeNodeIds -ccontains $frame.boundaryNodeId -and $_.subtreeNodeIds -ccontains $frame.callerNodeId})
            Assert-That ($owners.Count -eq 1) 'Each C and outside caller must belong to one actual B executable'
            $frame.wrapperNodeId = $owners[0].boundaryNodeId
        }
        Assert-PlacedMemberBinding $frame.bodyBinding $frame.producerNodeId
        Assert-PlacedMemberBinding $frame.returnBinding $frame.producerNodeId
        $Case.frames += $frame
    }
    Save-Json "$($Case.name)-executable-before-run" @{executable=$Case.executable;inputSources=$Case.inputSources;version=$Case.versionView;frames=$Case.frames}
}
function Read-AllExecutions([string] $ExecutionId) {
    $items = @(); $cursor = $null; $seen = @{}
    do {
        $path = "/runtime/workflows/instances/$ExecutionId/activity-executions?limit=100"
        if ($cursor) { Assert-That (-not $seen.ContainsKey($cursor)) 'Execution paging cursor repeated'; $seen[$cursor]=$true; $path += "&cursor=$([Uri]::EscapeDataString($cursor))" }
        $page = Api $path; $items += @($page.items); $cursor = $page.nextCursor
    } while ($cursor)
    Assert-That ($items.Count -eq $page.totalCount) 'Execution paging omitted rows'
    return ,$items
}
function Leaf-Owner($Detail, $Details, [string] $LeafVersionId) {
    $parent = $Detail.provenance.parentActivityExecutionId; $seen = @{}
    while ($parent) {
        Assert-That (-not $seen.ContainsKey($parent) -and $Details.ContainsKey($parent)) 'Missing or cyclic runtime parent provenance'
        $seen[$parent]=$true; $ancestor=$Details[$parent]
        if ($ancestor.boundary.definitionVersionId -ceq $LeafVersionId) { return $ancestor.activityExecutionId }
        $parent=$ancestor.provenance.parentActivityExecutionId
    }
    return $null
}
function Read-CommittedInput([string] $ExecutionId, $Detail, [string] $InputKey) {
    $snapshots = @($Detail.valueSnapshots | Where-Object inputKey -CEQ $InputKey)
    Assert-That ($snapshots.Count -eq 1) "Expected one committed $InputKey evidence snapshot"
    Api "/runtime/workflows/instances/$ExecutionId/activity-executions/$($Detail.activityExecutionId)/value-evidence/$($snapshots[0].evidenceId)/payload"
}
function Run-PlacedCase($Case) {
    Read-PlacedExecutable $Case
    $run = Api "/runtime/workflows/executables/$($Case.publication.artifactId)/execute" 'Post' @{inputs=@{};sourceReferenceId=$Case.publication.sourceReferenceId}
    $instance = Wait-WorkflowInstance $ctx $run.workflowExecutionId -TimeoutSeconds 30
    $entries = Read-AllExecutions $run.workflowExecutionId
    $details = @{}
    foreach ($entry in $entries) { $id=$entry.execution.activityExecutionId; $details[$id]=Api "/runtime/workflows/instances/$($run.workflowExecutionId)/activity-executions/$id" }
    $Case.runtime = @{start=$run;instance=$instance;executionEntries=$entries;details=$details}
    foreach ($wrapper in $Case.wrappers) {
        $matches = @($entries | Where-Object {$_.execution.executableNodeId -ceq $wrapper.boundaryNodeId})
        Assert-That ($matches.Count -eq 1) 'Expected one actual B boundary execution'
        $wrapper.runtime = $details[$matches[0].execution.activityExecutionId]
    }
    foreach ($frame in $Case.frames) {
        $runtime = @{}
        foreach ($role in @('boundary','producer','body','return','caller')) {
            $key=(@{boundary='boundaryNodeId';producer='producerNodeId';body='bodyNodeId';return='returnNodeId';caller='callerNodeId'})[$role]
            $matches=@($entries | Where-Object {$_.execution.executableNodeId -ceq $frame[$key]})
            Assert-That ($matches.Count -eq 1) "Expected one placed $role execution"
            $runtime[$role]=$details[$matches[0].execution.activityExecutionId]
        }
        foreach ($role in @('producer','body','return','caller')) { $runtime["$($role)OwnerId"]=Leaf-Owner $runtime[$role] $details $Case.leaf.VersionId }
        $runtime.producerInput=Read-CommittedInput $run.workflowExecutionId $runtime.producer 'customerId'
        $runtime.bodyInput=Read-CommittedInput $run.workflowExecutionId $runtime.body 'text'
        $runtime.callerInput=Read-CommittedInput $run.workflowExecutionId $runtime.caller 'text'
        $frame.runtime=$runtime
    }
    $after=Api "/runtime/workflows/executables/$($Case.publication.artifactId)"
    $versionAfter=Api "/design/activities/versions/$($Case.leaf.VersionId)"
    Assert-That ($after.artifactHash -ceq $Case.executable.artifactHash -and $versionAfter.template.templateHash -ceq $Case.leaf.TemplateHash -and (Canonical $versionAfter.provider.payload) -ceq (Canonical $Case.leaf.providerPayload)) 'Execution changed immutable executable/template/provider identity'
    $Case.afterExecution=@{executable=$after;version=$versionAfter}; $Case.status='passed'
    Assert-PlacedReusableCase $Case
    Save-Json "$($Case.name)-runtime" $Case
}

try {
    $sourceHashPaths = @(@(& git -c core.fsmonitor=false diff HEAD --name-only) + @($PSCommandPath,
        "$PSScriptRoot/_Assertions.ps1", "$PSScriptRoot/publication_snapshot.py", "$PSScriptRoot/fixtures/CustomerFixture.cs",
        "$PSScriptRoot/fixtures/fixture.targets", "$PSScriptRoot/../_ElsaCommon.ps1", "$PSScriptRoot/../_ServerLifecycle.ps1",
        "$PSScriptRoot/../reusable-activities/_ReusableCommon.ps1") | Sort-Object -Unique)
    $receipt.sourceFilesBefore = Source-Fingerprints $sourceHashPaths
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
    $receipt.reusableHelperSha256 = (Get-FileHash "$PSScriptRoot/../reusable-activities/_ReusableCommon.ps1" -Algorithm SHA256).Hash
    $receipt.fixtureSha256 = (Get-FileHash "$PSScriptRoot/fixtures/CustomerFixture.cs" -Algorithm SHA256).Hash
    $receipt.driverSha256 = (Get-FileHash $PSCommandPath -Algorithm SHA256).Hash
    $ctx = Connect-Elsa -BaseUrl $BaseUrl
    $sequence = Get-ActivityVersionId $ctx 'Elsa.Activities.Sequence.Activities.Sequence'
    $writeLine = Get-ActivityVersionId $ctx 'Elsa.Activities.Primitives.Activities.WriteLine'
    $customer = Get-ActivityVersionId $ctx 'Elsa.Workbench.ActivityDataSelectionProof.GetCustomer'
    $catalog = Api "/design/activities/versions/$customer"
    Assert-That (@($catalog.contract.outputs).Count -eq 1 -and $catalog.contract.outputs[0].referenceKey -ceq 'Customer' -and $catalog.contract.outputs[0].type.alias -ceq 'Elsa.Workbench.ActivityDataSelectionProof.Customer') 'Fixture must expose exactly one typed Customer object output'
    $seedInput = @($catalog.contract.inputs | Where-Object referenceKey -CEQ 'customerId')
    Assert-That ($seedInput.Count -eq 1 -and $seedInput[0].type.alias -ceq 'String' -and $seedInput[0].isRequired -eq $false -and
        $seedInput[0].default.syntax -ceq 'Literal' -and $seedInput[0].default.value -ceq 'cust-1042') 'Opt-in fixture must preserve its optional defaulted CustomerId seed'
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
    # These publish stored reusable definitions before placing them in independent parent artifacts.
    $leaf = New-PlacedLeaf 'placed-customer-leaf'
    $wrapper = New-PlacedWrapper $leaf 'placed-customer-wrapper'
    $receipt.placedReusableCases += New-PlacedWorkflowCase 'placed-member-single' $leaf
    $receipt.placedReusableCases += New-PlacedWorkflowCase 'placed-member-nested' $leaf $wrapper
    Save-Json 'receipt' $receipt

    # Keep the original active parent and its root identity while replacing only its exact leaf version.
    $badLeaf = New-PlacedLeaf 'placed-customer-incompatible-leaf' $true
    $placed = $receipt.placedReusableCases[0]
    $draft = Api "/design/workflows/drafts/$($placed.draftId)"
    $call = @($draft.state.rootActivity.structure.payload.activities | Where-Object nodeId -CEQ 'call-c')[0]
    $call.activityVersionId = $badLeaf.VersionId
    $null = Api "/design/workflows/drafts/$($draft.id)" 'Put' @{state=$draft.state;layout=$draft.layout;activityPresentation=$draft.activityPresentation}
    $badVersion = Api "/design/workflows/drafts/$($draft.id)/promote" 'Post' @{}
    $before = Read-Snapshot $placed.definitionId $placed.rootNodeId
    Save-Json 'placed-publication-before' $before
    $response = Invoke-WebRequest "$BaseUrl/publishing/workflows/$($badVersion.id)/publish" -Method Post -WebSession $ctx.Session -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck
    $problemText = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { $response.Content }
    $problem = $problemText | ConvertFrom-Json -AsHashtable
    Assert-That ($response.StatusCode -eq 400 -and $problem.errorCode -ceq 'VF-COER-001') 'Placed known Customer object to String must be refused by the actual compiler'
    $after = Read-Snapshot $placed.definitionId $placed.rootNodeId
    Save-Json 'placed-publication-after' $after
    Assert-PublicationUnchanged $before $after
    $goodAfter = Api "/design/activities/versions/$($leaf.VersionId)"
    $badAfter = Api "/design/activities/versions/$($badLeaf.VersionId)"
    Assert-That ($goodAfter.template.templateHash -ceq $leaf.TemplateHash -and $badAfter.template.templateHash -ceq $badLeaf.TemplateHash -and
        (Canonical $goodAfter.provider.payload) -ceq (Canonical $leaf.providerPayload) -and (Canonical $badAfter.provider.payload) -ceq (Canonical $badLeaf.providerPayload)) 'Refused placement changed a stored reusable template/provider'
    $receipt.placedBodyPublicationRefusal = @{status='passed';statusCode=[int]$response.StatusCode;code=$problem.errorCode;problem=$problem;
        fingerprintsUnchanged=$true;before=$before;after=$after;badLeaf=$badLeaf;previousPublication=$placed.publication}
    Save-Json 'placed-publication-refusal' $receipt.placedBodyPublicationRefusal
    Save-Json 'receipt' $receipt

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
    foreach ($case in $receipt.placedReusableCases) { Run-PlacedCase $case; Save-Json 'receipt' $receipt }
    $binaryAfter = @(Get-ChildItem $binaryDirectory -Filter '*.dll' | Sort-Object Name | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash} })
    Assert-That ((Canonical $binaryBefore) -ceq (Canonical $binaryAfter)) 'Tested binaries changed while host was running'
    $receipt.status = 'passed'; $success = $true
} catch {
    $receipt.status = 'failed'; $receipt.failures += @{message=$_.Exception.Message;detail=$_.ErrorDetails.Message;stack=$_.ScriptStackTrace}
    throw
} finally {
    $receipt.sourceAfter = (& git -c core.fsmonitor=false rev-parse HEAD).Trim()
    $receipt.sourceStatusAfter = @(& git -c core.fsmonitor=false status --porcelain)
    $receipt.sourceFilesAfter = Source-Fingerprints $sourceHashPaths
    if ($receipt.sourceAfter -cne $sourceBefore -or (Canonical $receipt.sourceStatusAfter) -cne (Canonical $sourceStatusBefore) -or
        (Canonical $receipt.sourceFilesAfter) -cne (Canonical $receipt.sourceFilesBefore)) { $receipt.status='failed'; $success=$false; $receipt.failures += @{message='Source changed during owned proof'} }
    if ($root) {
        Stop-OwnedElsaServer
        foreach ($log in Get-ChildItem $root -Filter 'server-*.log') { Copy-Item $log.FullName $EvidenceDirectory }
        Remove-OwnedElsaServer -ContentRoot $root -KeepContentRoot:(-not $success)
        $receipt.cleanup = @{ownedProcessesExited=(@($receipt.ownedProcessIds | Where-Object {Get-Process -Id $_ -ErrorAction SilentlyContinue}).Count -eq 0);contentRootRetained=(Test-Path $root)}
    }
    Save-Json 'receipt' $receipt
}

Assert-ProofSucceeded -Succeeded $success
