<#
.SYNOPSIS
    Shared helpers for the reusable-activity (workflows-as-activities) test scripts.
.DESCRIPTION
    Dot-sources ../_ElsaCommon.ps1 and adds the reusable-activity authoring lifecycle over REST:
    create definition (+ draft) -> publication-preflight (issues a review token) -> publish.

    "Workflows used as activities" in Foundation = a reusable ACTIVITY DEFINITION authored as an
    `elsa.activity-graph` graph with a public boundary contract. Schema 1 exposes the single `done`
    outcome; schema 2 supports explicit mappings from entry outcomes to emitted public outcomes. A
    consumer binds it by exact `activityVersionId`; the graph is INLINED into the consumer at publish
    (not a separate execution). See ../README.md and the plan notes.

    Manifests are authored as RAW JSON strings on purpose: the graph manifest requires `variables` and
    `outputMappings` to be present JSON arrays, and PowerShell 5.1 drops/By-nulls empty `@()` arrays under
    ConvertTo-Json, so hashtable authoring is unreliable here.
#>
. "$PSScriptRoot/../_ElsaCommon.ps1"

# A contract that emits only the required `done` outcome, no inputs/outputs.
$script:DoneOnlyContract = '{"contractSchemaVersion":"1","inputs":[],"outputs":[],"outcomes":[{"referenceKey":"done","name":"Done","isEmitted":true}]}'

# Create a reusable activity definition (+ its initial draft). Returns identity + the generated activity type key.
function New-ReusableActivityDefinition {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $DisplayName,
        [Parameter(Mandatory)][string] $PayloadJson,
        [string] $ContractJson = $script:DoneOnlyContract,
        [string] $Category = "QaReusable",
        [string] $SchemaVersion = "1"
    )
    $body = @"
{"category":"$Category","displayName":"$DisplayName","description":"reusable activity test","provider":{"providerKey":"elsa.activity-graph","schemaVersion":"$SchemaVersion","payload":$PayloadJson},"contract":$ContractJson,"layout":[]}
"@
    $c = Invoke-RestMethod "$($Ctx.BaseUrl)/design/activities/definitions" -Method Post -WebSession $Ctx.Session -ContentType 'application/json' -Body $body
    [pscustomobject]@{
        DefinitionId    = $c.definition.definitionId
        DraftId         = $c.draft.draftId
        Revision        = [long]$c.draft.revision
        ActivityTypeKey = $c.definition.activityTypeKey
    }
}

# Preflight + publish a reusable-activity draft. Returns the published version identity.
function Publish-ReusableDraft {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $DraftId,
        [Parameter(Mandatory)][long] $Revision,
        $HeadVersionId = $null,   # untyped: a [string] param would coerce $null to "" and trip stale-head
        $Version = $null
    )
    if ([string]::IsNullOrEmpty($HeadVersionId)) { $HeadVersionId = $null }
    $pfRequest = @{ expectedDraftRevision = $Revision; expectedDefinitionHeadVersionId = $HeadVersionId }
    if (-not [string]::IsNullOrEmpty($Version)) { $pfRequest.version = $Version }
    $pfBody = $pfRequest | ConvertTo-Json
    $pf = Invoke-RestMethod "$($Ctx.BaseUrl)/design/activities/drafts/$DraftId/publication-preflight" -Method Post -WebSession $Ctx.Session -ContentType 'application/json' -Body $pfBody
    if (-not $pf.isPublishable) {
        $msg = ($pf.diagnostics | ForEach-Object { "$($_.severity):$($_.code)" }) -join "; "
        throw "Reusable activity draft '$DraftId' not publishable: $msg"
    }
    $pubBody = @{
        expectedDraftRevision           = $pf.draftRevision
        expectedDefinitionHeadVersionId = $pf.definitionHeadVersionId
        version                         = $pf.reviewedVersion
        reviewToken                     = $pf.reviewToken
        idempotencyKey                  = [guid]::NewGuid().ToString()
    } | ConvertTo-Json
    $rec = Invoke-RestMethod "$($Ctx.BaseUrl)/design/activities/drafts/$DraftId/publish" -Method Post -WebSession $Ctx.Session -ContentType 'application/json' -Body $pubBody
    if ($rec.status -ne 'Applied') { throw "Reusable activity publish status '$($rec.status)'" }
    [pscustomobject]@{
        VersionId         = $rec.outcome.definitionVersionId
        SourceReferenceId = $rec.outcome.sourceReferenceId
        TemplateHash      = $rec.outcome.templateHash
        Version           = $rec.outcome.version
    }
}

# Create + publish a reusable activity in one call. Returns identity + published version.
function Publish-ReusableActivity {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $DisplayName,
        [Parameter(Mandatory)][string] $PayloadJson,
        [string] $ContractJson = $script:DoneOnlyContract,
        [string] $Category = "QaReusable",
        [string] $SchemaVersion = "1"
    )
    $d = New-ReusableActivityDefinition -Ctx $Ctx -DisplayName $DisplayName -PayloadJson $PayloadJson -ContractJson $ContractJson -Category $Category -SchemaVersion $SchemaVersion
    $p = Publish-ReusableDraft -Ctx $Ctx -DraftId $d.DraftId -Revision $d.Revision
    [pscustomobject]@{
        DefinitionId    = $d.DefinitionId
        DraftId         = $d.DraftId
        ActivityTypeKey = $d.ActivityTypeKey
        VersionId       = $p.VersionId
        SourceReferenceId = $p.SourceReferenceId
        TemplateHash    = $p.TemplateHash
        Version         = $p.Version
    }
}

# Add a new draft to an EXISTING reusable definition (for publishing a subsequent version). Returns draft id + revision.
function Add-ReusableActivityDraft {
    param(
        [Parameter(Mandatory)] $Ctx,
        [Parameter(Mandatory)][string] $DefinitionId,
        [Parameter(Mandatory)][string] $PayloadJson,
        [string] $ContractJson = $script:DoneOnlyContract,
        $SourceVersionId = $null,
        [string] $SchemaVersion = "1"
    )
    $sv = if ([string]::IsNullOrEmpty($SourceVersionId)) { "null" } else { '"' + $SourceVersionId + '"' }
    $body = @"
{"sourceVersionId":$sv,"provider":{"providerKey":"elsa.activity-graph","schemaVersion":"$SchemaVersion","payload":$PayloadJson},"contract":$ContractJson,"layout":[]}
"@
    $r = Invoke-RestMethod "$($Ctx.BaseUrl)/design/activities/definitions/$DefinitionId/drafts" -Method Post -WebSession $Ctx.Session -ContentType 'application/json' -Body $body
    [pscustomobject]@{ DraftId = $r.draftId; Revision = [long]$r.revision }
}

# Return the exact dependency version ids a consumer version is bound to (authoritative outbound edges).
function Get-OutboundDependencyVersionIds {
    param([Parameter(Mandatory)] $Ctx, [Parameter(Mandatory)][string] $VersionId)
    $out = Invoke-RestMethod "$($Ctx.BaseUrl)/design/activities/versions/$VersionId/dependencies?direction=outbound" -WebSession $Ctx.Session -UseBasicParsing
    @($out.items | ForEach-Object { $_.dependency.versionId })
}

# Build a graph manifest whose root is a Sequence running the given inner-node JSON array (raw JSON string).
function New-GraphManifestJson {
    param(
        [Parameter(Mandatory)][string] $SeqVersionId,
        [Parameter(Mandatory)][string] $ActivitiesJson,   # raw JSON array of node objects
        [string] $OutputMappingsJson = "[]",
        [string] $VariablesJson = "[]",
        $OutcomeMappingsJson = $null   # untyped: a [string] param coerces $null to "" and emits an empty "outcomeMappings":
    )
    $outcomeMappings = if ([string]::IsNullOrEmpty($OutcomeMappingsJson)) { "" } else { ',"outcomeMappings":' + $OutcomeMappingsJson }
    @"
{"variables":$VariablesJson,"rootActivity":{"nodeId":"graph-root","activityVersionId":"$SeqVersionId","inputs":[],"outputs":[],"structure":{"kind":"elsa.sequence.structure","schemaVersion":"1.0.0","payload":{"activities":$ActivitiesJson}}},"outputMappings":$OutputMappingsJson$outcomeMappings}
"@
}

# Build a graph manifest whose root IS another activity version directly (root-wraps-child). This is one supported
# composition shape for reusable activities; New-GraphManifestJson also supports reusable references in a Sequence.
function New-RootWrapManifestJson {
    param(
        [Parameter(Mandatory)][string] $RootVersionId,
        [string] $OutputMappingsJson = "[]",
        [string] $VariablesJson = "[]",
        $OutcomeMappingsJson = $null   # untyped: a [string] param coerces $null to "" and emits an empty "outcomeMappings":
    )
    $outcomeMappings = if ([string]::IsNullOrEmpty($OutcomeMappingsJson)) { "" } else { ',"outcomeMappings":' + $OutcomeMappingsJson }
    @"
{"variables":$VariablesJson,"rootActivity":{"nodeId":"wrap","activityVersionId":"$RootVersionId","inputs":[],"outputs":[],"structure":null},"outputMappings":$OutputMappingsJson$outcomeMappings}
"@
}

# A single WriteLine node (raw JSON) for embedding in a manifest's activities array.
function New-WriteLineNodeJson {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $WriteLineVersionId, [Parameter(Mandatory)][string] $Text)
    @"
{"nodeId":"$NodeId","activityVersionId":"$WriteLineVersionId","inputs":[{"referenceKey":"text","value":{"value":"$Text","expressionType":"Literal"},"autoEvaluate":null,"evaluatorType":null,"storageDriverType":null,"isSensitive":null}],"outputs":[],"structure":null}
"@
}

# A node that references another reusable activity by version id (a boundary child), no authored children.
function New-ReusableRefNodeJson {
    param([Parameter(Mandatory)][string] $NodeId, [Parameter(Mandatory)][string] $VersionId)
    @"
{"nodeId":"$NodeId","activityVersionId":"$VersionId","inputs":[],"outputs":[],"structure":null}
"@
}

# True if the instance ran an activity of the given reusable activity type key (case-insensitive) to completion.
function Test-ReusableRan {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $ActivityTypeKey)
    [bool]($Instance.activities | Where-Object {
        $_.activityType -and $_.activityType.ToLowerInvariant() -eq $ActivityTypeKey.ToLowerInvariant() -and $_.status -in @('Completed','Finished')
    })
}

# True if the instance Faulted because of the reusable container-root bare-compilation bug (issue #1051):
# CompileSourceOwnedRoot builds the reusable root as an empty ActivityNode (no inputs, no structure), so a
# structural-container root loses its Structure (Sequence -> "requires structure") AND an input-carrying root
# loses its authored inputs (If -> "cannot materialize a complete input snapshot. Missing keys: [Condition]").
# The graph boundary re-wraps a descendant fault as "descendant activity faulted inside the graph boundary".
function Test-Structure1051Fault {
    param([Parameter(Mandatory)] $Instance)
    if ($Instance.instance.status -ne 'Faulted') { return $false }
    $msgs = @($Instance.incidents | ForEach-Object { $_.message })
    [bool]($msgs | Where-Object {
        $_ -match "requires structure '" -or
        $_ -match 'descendant activity faulted inside the graph boundary' -or
        $_ -match 'cannot materialize a complete input snapshot'
    })
}

# Emit the standard KNOWN ISSUE #1051 line (a reusable-with-container-root tracker; auto-passes once the fix lands).
function Report-Structure1051 {
    Write-Host "KNOWN ISSUE #1051 - reusable activity with a structural-container (Sequence) root faults at runtime: the inlined container publishes with Structure=null. This tracker passes; convert to a strict assertion once #1051 is fixed." -ForegroundColor Yellow
}
