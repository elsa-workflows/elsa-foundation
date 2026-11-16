# Shared behavioral oracles used by the actual journey and its retained-receipt controls.
function Assert-CommittedText($Detail, [string] $Expected) {
    if ($Detail.payload.preview -cne $Expected) {
        throw "Committed consumer input differs: expected '$Expected', got '$($Detail.payload.preview)'"
    }
}
function Assert-PublicationUnchanged($Before, $After) {
    $beforeJson = ConvertTo-Json -InputObject $Before -Depth 100 -Compress
    $afterJson = ConvertTo-Json -InputObject $After -Depth 100 -Compress
    if ($beforeJson -cne $afterJson) { throw 'Refused publication changed owned artifact/reference/publication/activation rows' }
}

function Assert-PlacedMemberBinding($Binding, [string] $ProducerId) {
    if ($null -eq $Binding -or $Binding.source -cne 'ActivityResultMember' -or $null -eq $Binding.selection -or
        $Binding.selection.mode -ne 'Known' -or $Binding.selection.selectedType.alias -cne 'String' -or
        $Binding.selection.selectedType.collectionKind -ne 'Single' -or
        (ConvertTo-Json -InputObject @($Binding.selection.segments) -Compress) -cne '["customerId"]' -or
        $Binding.selection.profileRevision -cne 'elsa.activity-value-json/1' -or
        $Binding.activityResult.producerExecutableNodeId -cne $ProducerId -or $Binding.activityResult.projectionKey -cne 'Customer' -or
        $null -ne $Binding.activityResult.producerScopeId -or $Binding.activityResult.isOptional -ne $false -or
        $null -eq $Binding.conversionPlan -or $Binding.conversionPlan.operation -ne 'Identity') {
        throw 'Placed member binding lost its exact known contract, conversion or producer identity'
    }
}

function Get-PlacedExecutableNodes($Node) {
    $Node
    foreach ($slot in $Node.childSlots) { foreach ($child in $slot.activities) { Get-PlacedExecutableNodes $child } }
}
function Get-PlacedBoundaryOwner($Detail, $Details, [string] $VersionId) {
    $parent = $Detail.provenance.parentActivityExecutionId; $seen = @{}
    while ($parent) {
        if ($seen.ContainsKey($parent) -or -not $Details.ContainsKey($parent)) { throw 'Nested placed wrapper has missing or cyclic runtime ancestry' }
        $seen[$parent] = $true; $ancestor = $Details[$parent]
        if ($ancestor.boundary.definitionVersionId -ceq $VersionId) { return $ancestor.activityExecutionId }
        $parent = $ancestor.provenance.parentActivityExecutionId
    }
    return $null
}
function Assert-PlacedWrapperTopology($Case) {
    if ($null -eq $Case.wrapper -or $Case.wrapperView.versionId -cne $Case.wrapper.VersionId -or
        $Case.wrapperView.definition.definitionId -cne $Case.wrapper.DefinitionId -or
        $Case.wrapperView.definition.activityTypeKey -cne $Case.wrapper.ActivityTypeKey -or
        $Case.wrapperView.template.templateHash -cne $Case.wrapper.TemplateHash -or
        $Case.wrapperView.template.sourceReferenceId -cne $Case.wrapper.SourceReferenceId) { throw 'Nested placed wrapper publication identity changed' }
    $all = @(Get-PlacedExecutableNodes $Case.executable.rootActivity)
    $wrappers = @($all | Where-Object activityType -CEQ $Case.wrapper.ActivityTypeKey)
    if ($wrappers.Count -ne 2 -or @($Case.wrappers).Count -ne 2 -or
        @($wrappers.executableNodeId | Sort-Object -Unique).Count -ne 2) { throw 'Nested placed wrapper topology must retain exactly two B boundaries' }
    if (@($Case.runtime.details.Values | Where-Object {$_.boundary.definitionVersionId -ceq $Case.wrapper.VersionId}).Count -ne 2) {
        throw 'Nested placed wrapper runtime must retain exactly two B boundaries'
    }
    $scopes = @(); $executions = @(); $children = @()
    foreach ($wrapper in $wrappers) {
        $subtree = @(Get-PlacedExecutableNodes $wrapper)
        $leaves = @($subtree | Where-Object activityType -CEQ $Case.leaf.ActivityTypeKey)
        $saved = @($Case.wrappers | Where-Object boundaryNodeId -CEQ $wrapper.executableNodeId)
        if ($wrapper.activityTypeVersion -cne $Case.wrapper.Version -or $leaves.Count -ne 2 -or $saved.Count -ne 1 -or
            @($subtree | Where-Object activityType -CEQ $Case.wrapper.ActivityTypeKey).Count -ne 1) { throw 'Nested placed wrapper must own exactly two C boundaries at its published version' }
        $detail = $saved[0].runtime
        $entries = @($Case.runtime.executionEntries | Where-Object {$_.execution.executableNodeId -ceq $wrapper.executableNodeId})
        if ($entries.Count -ne 1 -or $entries[0].execution.status -ne 'Completed' -or
            $detail.activityExecutionId -cne $entries[0].execution.activityExecutionId -or $detail.executableNodeId -cne $wrapper.executableNodeId -or
            $detail.status -ne 'Completed' -or [string]::IsNullOrWhiteSpace($detail.lastCheckpointId) -or
            $detail.boundary.definitionId -cne $Case.wrapper.DefinitionId -or $detail.boundary.definitionVersionId -cne $Case.wrapper.VersionId -or
            $detail.boundary.templateHash -cne $Case.wrapper.TemplateHash -or [string]::IsNullOrWhiteSpace($detail.boundary.executionScopeId) -or
            $null -ne (Get-PlacedBoundaryOwner $detail $Case.runtime.details $Case.wrapper.VersionId)) { throw 'Nested placed wrapper runtime identity, completion or ancestry changed' }
        $scopes += $detail.boundary.executionScopeId; $executions += $detail.activityExecutionId
        foreach ($leaf in $leaves) {
            $frames = @($Case.frames | Where-Object boundaryNodeId -CEQ $leaf.executableNodeId)
            if ($frames.Count -ne 1) { throw 'Nested placed wrapper lost its exact C frame' }
            $frame = $frames[0]; $children += $leaf.executableNodeId
            if ($frame.wrapperNodeId -cne $wrapper.executableNodeId -or $subtree.executableNodeId -cnotcontains $frame.callerNodeId -or
                (Get-PlacedBoundaryOwner $frame.runtime.boundary $Case.runtime.details $Case.wrapper.VersionId) -cne $detail.activityExecutionId -or
                (Get-PlacedBoundaryOwner $frame.runtime.caller $Case.runtime.details $Case.wrapper.VersionId) -cne $detail.activityExecutionId -or
                $null -ne (Get-PlacedBoundaryOwner $frame.runtime.caller $Case.runtime.details $Case.leaf.VersionId)) {
                throw 'Nested placed wrapper owns the wrong C boundary or outside caller'
            }
        }
    }
    if (@($children | Sort-Object -Unique).Count -ne 4 -or @($scopes | Sort-Object -Unique).Count -ne 2 -or
        @($executions | Sort-Object -Unique).Count -ne 2 -or
        @($Case.frames | Where-Object {$scopes -ccontains $_.runtime.boundary.boundary.executionScopeId}).Count -gt 0) {
        throw 'Nested placed wrapper reused a child identity, execution or boundary scope'
    }
}

function Assert-PlacedReusableCase($Case) {
    $expected = switch ($Case.name) {
        'placed-member-single' { ,@('cust-1042') }
        'placed-member-nested' { ,@('cust-A','cust-B','cust-C','cust-D') }
        default { throw 'Unknown placed reusable proof case' }
    }
    if ((ConvertTo-Json -InputObject @($Case.expectedValues) -Compress) -cne (ConvertTo-Json -InputObject $expected -Compress)) {
        throw 'Placed proof must retain the authored per-occurrence sentinel values'
    }
    if ($Case.status -ne 'passed' -or $Case.publication.artifactId -cne $Case.executable.artifactId -or
        $Case.inputSources.artifactId -cne $Case.publication.artifactId -or
        $Case.inputSources.sourceReferenceId -cne $Case.publication.sourceReferenceId -or
        $Case.inputSources.accessState -ne 'allowed' -or
        $Case.versionView.template.templateHash -cne $Case.leaf.TemplateHash -or
        $Case.versionView.template.sourceReferenceId -cne $Case.leaf.SourceReferenceId -or
        $Case.runtime.instance.instance.status -ne 'Completed' -or $Case.runtime.instance.instance.incidentCount -ne 0 -or
        @($Case.frames).Count -ne @($Case.expectedValues).Count) { throw 'Incomplete real placed reusable publication/execution receipt' }
    $outputs = @($Case.versionView.contract.outputs | Where-Object referenceKey -CEQ 'selected-id')
    if ($outputs.Count -ne 1 -or $outputs[0].name -cne 'SelectedId' -or $outputs[0].type.alias -cne 'String' -or
        $outputs[0].isNullable -ne $false -or $outputs[0].isRequired -ne $true) { throw 'Placed public output contract changed' }
    foreach ($requirement in @(@{key='elsa.runtime.input-member-selection';version='1'}, @{key='intrinsic';version='2'})) {
        if (@($Case.versionView.template.runtimeRequirements | Where-Object { $_.consumerKey -ceq $requirement.key -and $_.schemaVersion -ceq $requirement.version }).Count -ne 1) {
            throw 'Published reusable omitted its member-selection or scoped-output requirement'
        }
    }
    if ($Case.name -ceq 'placed-member-nested') { Assert-PlacedWrapperTopology $Case }
    $identities = @(); $scopes = @(); $index = 0
    foreach ($frame in $Case.frames) {
        if ($frame.expected -cne $Case.expectedValues[$index++]) { throw 'Placed frame expectation moved to another occurrence' }
        Assert-PlacedMemberBinding $frame.bodyBinding $frame.producerNodeId
        Assert-PlacedMemberBinding $frame.returnBinding $frame.producerNodeId
        if ($frame.callerBinding.source -cne 'ActivityResult' -or
            $frame.callerBinding.activityResult.producerExecutableNodeId -cne $frame.boundaryNodeId -or
            $frame.callerBinding.activityResult.projectionKey -cne 'selected-id' -or
            $null -ne $frame.callerBinding.activityResult.producerScopeId -or $frame.callerBinding.activityResult.isOptional -ne $false -or
            $null -ne $frame.callerBinding.selection) {
            throw 'Caller does not consume this exact boundary public output'
        }
        $boundary = $frame.runtime.boundary
        if ($boundary.boundary.definitionVersionId -cne $Case.leaf.VersionId -or
            $boundary.boundary.templateHash -cne $Case.leaf.TemplateHash -or
            [string]::IsNullOrWhiteSpace($boundary.boundary.executionScopeId) -or
            $frame.runtime.producerOwnerId -cne $boundary.activityExecutionId -or
            $frame.runtime.bodyOwnerId -cne $boundary.activityExecutionId -or
            $frame.runtime.returnOwnerId -cne $boundary.activityExecutionId -or
            $frame.runtime.callerOwnerId -ceq $boundary.activityExecutionId) { throw 'Placed runtime occurrence crossed its owning boundary scope' }
        $scopes += $boundary.boundary.executionScopeId
        foreach ($role in @('boundary','producer','body','return','caller')) {
            $detail = $frame.runtime[$role]
            $id = $frame[(@{boundary='boundaryNodeId';producer='producerNodeId';body='bodyNodeId';return='returnNodeId';caller='callerNodeId'})[$role]]
            $executions = @($Case.runtime.executionEntries | Where-Object { $_.execution.executableNodeId -ceq $id })
            if ($executions.Count -ne 1 -or $executions[0].execution.status -ne 'Completed' -or
                $detail.activityExecutionId -cne $executions[0].execution.activityExecutionId -or
                $detail.executableNodeId -cne $id -or $detail.status -ne 'Completed' -or
                [string]::IsNullOrWhiteSpace($detail.lastCheckpointId)) { throw "Expected one committed Completed placed $role" }
            $identities += $id
        }
        Assert-CommittedText $frame.runtime.producerInput $frame.expected
        Assert-CommittedText $frame.runtime.bodyInput $frame.expected
        Assert-CommittedText $frame.runtime.callerInput $frame.expected
    }
    if (@($identities | Sort-Object -Unique).Count -ne $identities.Count -or @($scopes | Sort-Object -Unique).Count -ne $scopes.Count) {
        throw 'Repeated placed occurrences reused an executable identity or boundary execution scope'
    }
}

# Called after cleanup/receipt retention, including a final source-integrity refusal.
function Assert-ProofSucceeded([Parameter(Mandatory)][bool] $Succeeded) {
    if (-not $Succeeded) { throw 'Activity data proof failed; inspect the retained receipt.' }
}
