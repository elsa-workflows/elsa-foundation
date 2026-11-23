# Serialization rule

Every ordinary JSON option set derives from the read-only `JsonBaseline.Options` in
[`Elsa.Serialization.Core`](../Essentials/src/Serialization/Core/JsonBaseline.cs). Use `JsonBaseline.CreateCopy()`
when an owner needs its own resolver or a registered wire-format converter. Modules contribute resolvers for their
owned contracts; they do not change host-wide HTTP JSON settings.

The baseline writes camelCase CLR member names, reads those names exactly, preserves dictionary keys and explicit
nulls, and uses strict JSON numbers. Carried `JsonElement` data keeps its original spelling, whitespace, number tokens
and escapes through writes and reads. Changed data uses the relaxed encoder. These rules also apply to HTTP and
persistence owners; crossing a boundary does not justify a different data contract.

```csharp
var options = JsonBaseline.CreateCopy();
var json = JsonSerializer.Serialize(ownedRecord, options);
var record = JsonSerializer.Deserialize<OwnedRecord>(json, options);
```

`IPayloadSerializer` remains the injected owner serializer where a component already uses it. Its options derive
from the same baseline. Its configured ordering and alias converters are recorded below, rather than granting every
caller an unrestricted option set.

## Typed user values, equality and hashes

A conversion to or from a user CLR type uses `JsonConversionContract`, selected on the type or input member with
`JsonConversionAttribute`. The default is camelCase, exact matching and unmatched-member warnings. API wire dates
require an explicit offset; user conversion and API write modes have distinct rules for `DateTimeKind.Unspecified`.
The [conversion contract](../specs/1207-json-data-integrity-baseline/contracts/json-baseline.md) defines diagnostics,
date handling and the scoped override surface.

Data-carrying API, store and message members use `JsonElement` or `JsonNode`. Typed owner metadata stays typed.
Do not deserialize raw data into `object`, a dictionary or a convenient POCO merely to retain or forward it.
An explicit conversion validates the requested type while the retained data keeps its original representation.

Use `JsonCanonicalForm.Equals` to compare JSON values. Every JSON hash or fingerprint goes through `JsonHashInput`,
including JSON components of framed and keyed protocols. Its independent fixed option set is the sole hash-profile
entry below. Canonical bytes belong to equality and hashing; they do not replace the bytes of stored or forwarded
data. See [ADR 0091](adr/0091-json-data-passes-through-unchanged-and-every-serializer-derives-from-one-baseline.md).

Physical checksums remain byte checks: `EfMigrationPlan.ManifestSha256`, captured closure-file digests, exact transport copies and the activity authority row witness
detect changes to stored bytes even when JSON values are equal. `ActivityAuthorityIntegrity` binds raw and auxiliary
JSON spellings, token columns and decoded scalars using the length-framed UTF-16 recipe verified by SQL Server's
`HASHBYTES` predicate, including scalars above 4,000 characters. It is not a content hash or JSON value identity.
The paired authority columns are compared with the provider's ordinal collation before paging and ordinally after
hydration. Replacing this witness with canonical JSON would hide physical drift and invalidate the SQL verifier.
Semantic package-manifest catalog hashes still use `JsonHashInput`; a file's JSON extension alone grants no exemption.

## Boundary contract review

`JsonDataBoundaryArchitectureTests` discovers generated contexts across every shipping C# source root, then inspects
their actual constructed metadata. The [wire pins](../Essentials/tests/Architecture/Tests/Baselines/json-wire-contracts.json)
make a new root, member, wire name, converter, nullability rule or polymorphic alternative require review. A mismatch
prints each changed shape and retains the full current graph under the temporary evidence directory named in the
failure. Updating a pin is a reviewed contract change, not permission to call the new member metadata.

The guard also pins these semantic roles separately:

| Member or contract | Role |
|---|---|
| `ArgumentValue.Value`, `ActivityArgumentValue.Value` | Carried literal/default data, held as JSON; CLR producers explicitly apply their scoped conversion contract. |
| `ValueEnvelope.InlineValue`, `WorkflowExecutionCommand.Payload` | Carried runtime data, held as JSON. Envelope admission and null/missing policies remain S-2-owned. |
| `WorkflowInstanceDetailsView.Outputs`, `WorkflowOutputView.Value` | A named map of owned output records; the member converter preserves authored output names and each record's data remains JSON. `WorkflowOutputView` is also an explicit owner-context root. |
| `WeaverContextAttachment.Content`, `WeaverActionProposal.Operations[*]` | Detached JSON data. Proposal operation roots are objects. |
| `WeaverStreamEvent.Payload` | Owned event protocol alternatives described by `WeaverSseJsonContext`; their nested carried data remains JSON. It is not an arbitrary workflow-data slot. |
| `WeaverToolCallOutcome.Result` | The owned `WeaverToolResult` protocol projection; its payload data remains JSON. |
| `RuntimePreflightProblemDetails.Diagnostics` | An owned diagnostic projection; its three current producers supply empty lists. No arbitrary data producer is admitted by this role. |
| `ActivityLayoutRecordsPayload`, `ActivityNodePresentationPayload` | Owner converters for layout/presentation request alternatives, including named node metadata. |
| `EtlPipelineV2` | Its owner converter writes the explicit pipeline protocol; carried connector/configuration values remain JSON. |

These are exact member/contract classifications. They grant no subtree or dictionary exemption. The generated-wire
graph is one part of enforcement; persistence document bindings, message codecs and producer/converter source
closures must also be checked before spec 1207 is complete. Retain the real API/store round trips alongside the
structural controls; shape inspection alone does not prove data passes through unchanged.

## Types are stored by alias

Type identity is alias-based, everywhere. A workflow Variable/Input/Output persists its type as a
`TypeReference { Alias, CollectionKind }` (plain data, serialized natively); the compiled-Type path
(`TypeJsonConverter`) is alias-only too; and a CLR activity's construction descriptor is a
`ClrActivityDescriptor { TypeAlias }`. Every alias resolves to a CLR type via `IWellKnownTypeRegistry`
under the shared `TypeAliasConvention` (a reserved bare alias for BCL primitives, otherwise the dotted
`Type.FullName`). No persisted shape carries an assembly name or version — the former decomposed
`TypeInformation` (namespace/assembly/version) has been removed, so a package bump never breaks
resolution or construction.

## Owner composition

An owner may add a resolver for its own contracts or a converter required by its established metadata format.
Specific deviations are recorded below with their reason and decision authority. HTTP, EF value converters,
expression helpers and runtime persistence all follow the baseline; none has a blanket exemption.

The Runtime EF persistence owner keeps a baseline-derived durability format independent of startup-contributed
`IPayloadSerializer` converters. Its retained JSON data still passes through raw. Provider column normalization and
wire-format differences need the specific register entries and round-trip evidence described by spec 1207.

## Baseline deviations

The retained activity workflow-state receipt metadata uses a private, baseline-derived PascalCase profile.
Its original writer and reader remain bidirectionally compatible, including null attempt IDs and date offsets;
append-on-write preserves prior receipts. This closed, value-free durability protocol does not configure API JSON.

Every ordinary option set derives from `JsonBaseline`. Scoped user conversion is selected by code on the type or
input member through `JsonConversionContract`; it is not a baseline deviation or an executable stamp. See
[spec 1207](../specs/1207-json-data-integrity-baseline/contracts/json-baseline.md) for its diagnostics and date rules.
A carried `JsonElement` is written raw. Canonical bytes are used only for equality and hashes.

The source and reflection architecture guards read this table. Each row identifies one file and a specific setting;
a row for one owner cannot exempt another. Each setting receipt ends in a digest of its declaration, expression and occurrence, so an added or changed call cannot borrow an existing row; stale receipts fail the guard. New internal own-contract deviations need a reason here; API data changes
follow ADR 0091's decision route. The separate `Baselines/json-optionless-calls.txt` is a shrinking legacy inventory
for Elsa-owned contracts only. Workflow-data conversions never enter that list.

| File | Setting | Reason | Approved by |
|---|---|---|---|
| `Essentials/src/Workflows/Runtime/Core/Models/StepWorkflowStateEffects.cs` | `PropertyNamingPolicy@3619e834e6e8` | The closed, value-free retained receipt protocol keeps its PascalCase field names. Bidirectional original/current reader and writer controls and append controls cover null attribution, escaped text and explicit offsets; API contexts retain camelCase. | ADR 0091 decision 7; spec 1207 FR-023 |
| `Essentials/apps/Elsa.Workbench/ElsaModuleManagementApi.cs` | `WriteIndented@bae9e4e922a3, json-method:ToJson@4f6ff7edd851, json-method:ToJson@ddc5d46f705a` | The exact local ToJson calls construct feed configuration JsonObject values, not EF owned-JSON mappings. The owner retains indentation for operator configuration and raw JsonElement subtrees remain unchanged. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Bpmn/Core/Internal/BpmnEmbeddingJson.cs` | `UnmappedMemberHandling@a4fc9453ddd9, converter:TextConverter@30ec2f1086d9` | The closed BPMN embedding union retains its text converter and refuses unknown structural fields; embedded data values remain raw JSON. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Bpmn/Design/Internal/BpmnStructureHandler.cs` | `converter:JsonStringEnumConverter@aca668889826` | The BpmnStructureHandler owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDirectExecutionDriver.cs` | `UnmappedMemberHandling@05a500a05ad7` | Direct BPMN private state refuses undeclared state fields; the rule does not reinterpret workflow values. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Bpmn/Interchange/Services/BpmnAuthoredDirectProjection.cs` | `UnmappedMemberHandling@1493343a7282, converter:JsonStringEnumConverter@996da68af17c` | The BpmnAuthoredDirectProjection owner retains a closed control/request shape and refuses undeclared fields before using its authority; raw JSON data slots remain unchanged. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Bpmn/Interchange/Services/BpmnCatalogBindingSession.cs` | `converter:JsonStringEnumConverter@0ccaa152e3ad` | The BpmnCatalogBindingSession owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Bpmn/Interchange/Services/BpmnDocumentExporter.cs` | `converter:JsonStringEnumConverter@684f90677196` | The BpmnDocumentExporter owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Compensation/Internal/CompensationScopeAuthoredStructureReader.cs` | `converter:JsonStringEnumConverter@023bab3087e9` | The CompensationScopeAuthoredStructureReader owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/ControlFlow/If/Internal/IfStructureHandler.cs` | `converter:JsonStringEnumConverter@e5c2363f25f9` | The IfStructureHandler owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/ControlFlow/Loops/LoopStructureHandler.cs` | `converter:JsonStringEnumConverter@b015c8acde41` | The LoopStructureHandler owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/ControlFlow/Parallel/Internal/ParallelStructureHandler.cs` | `converter:JsonStringEnumConverter@71a900fe5aba` | The ParallelStructureHandler owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/ControlFlow/Switch/Internal/SwitchAuthoredStructureReader.cs` | `converter:JsonStringEnumConverter@4fcdf35c07ea` | The SwitchAuthoredStructureReader owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/ControlFlow/Switch/Internal/SwitchStructureHandler.cs` | `converter:JsonStringEnumConverter@7af86c5a0548` | The SwitchStructureHandler owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Design/Api/ActivitiesDesignJsonContext.cs` | `converter:ActivitiesDesignCamelCaseEnumConverter@c849e55192b2` | The ActivitiesDesignJsonContext owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Design/Api/Services/ActivityDataSelectionSnapshot.cs` | `UnmappedMemberHandling@f10486014fd2` | The data-selection snapshot has a closed owned shape and refuses undeclared authority fields. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Design/Core/Services/ActivityVersionDiffer.cs` | `converter:JsonStringEnumConverter@8827d461c54d` | Version-diff metadata retains enum labels for its existing readers; default and hint values remain raw JSON. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Flowchart/Internal/FlowchartStructureHandler.cs` | `converter:JsonStringEnumConverter@0f17d1600c80` | The FlowchartStructureHandler owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Graph/Design/Services/GraphBodyReferenceValidator.cs` | `converter:JsonStringEnumConverter@2125c8dc3d56` | The GraphBodyReferenceValidator owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Activities/Runtime/Core/Serialization/ActivityValueJsonProfile.cs` | `construction@dd81fd6dae8a` | Returns a mutable copy of the default scoped conversion contract for the existing typed boundary; it does not create an independent wire profile. | spec 1207 FR-004a/FR-016 |
| `Essentials/src/Activities/Sequence/Internal/SequenceStructureHandler.cs` | `converter:JsonStringEnumConverter@e0f2b2f3491c` | The SequenceStructureHandler owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Cli/Elsa.Cli/CompositionAcceptCommand.cs` | `WriteIndented@e16b4399f746` | The CompositionAcceptCommand owner retains human-readable generated composition documents and its established enum vocabulary; embedded JSON values stay raw. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Cli/Elsa.Cli/CompositionGenerateCommand.cs` | `WriteIndented@1fa3f3501c48, converter:JsonStringEnumConverter@68c673122bdf` | The CompositionGenerateCommand owner retains human-readable generated composition documents and its established enum vocabulary; embedded JSON values stay raw. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Cli/Elsa.Cli/CompositionImportCommand.cs` | `WriteIndented@dbfd8b77c822, converter:JsonStringEnumConverter@d63550e9acd2` | The CompositionImportCommand owner retains human-readable generated composition documents and its established enum vocabulary; embedded JSON values stay raw. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Cli/Elsa.Cli/CompositionInitCommand.cs` | `WriteIndented@d2049ea2d75e` | The CompositionInitCommand owner retains human-readable generated composition documents and its established enum vocabulary; embedded JSON values stay raw. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Cli/Elsa.Cli/CompositionPlanCommand.cs` | `WriteIndented@16a1d6e4598a` | The CompositionPlanCommand owner retains human-readable generated composition documents and its established enum vocabulary; embedded JSON values stay raw. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Cli/Worker/ToolingEntryPoint.cs` | `DefaultIgnoreCondition@973fba781b36` | The CLI tooling receipt omits absent optional control fields, retaining its existing management protocol. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Cli/Worker/WorkerContract.cs` | `DefaultIgnoreCondition@61acc44b4daa, UnmappedMemberHandling@858a5124e830` | Worker management control messages retain their closed shape and omission of absent optional control fields; they carry no workflow value payload. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Cluster/EntityFrameworkCore/Stores/MemberReportJson.cs` | `UnmappedMemberHandling@8bb34dd70ec5` | Cluster membership reports refuse undeclared coordination fields; the profile does not carry workflow data. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Connections/Api/ConnectionsApi.cs` | `converter:JsonStringEnumConverter@6729d2f12438` | The ConnectionsApi owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Connections/Api/ConnectionsJsonContext.cs` | `converter:JsonStringEnumConverter@34b0e82e390a, converter:JsonStringEnumConverter@e76e6bfa4d7c` | The ConnectionsJsonContext owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/OpenTelemetryJsonContext.cs` | `converter:JsonStringEnumConverter@459ff70c8697, converter:JsonStringEnumConverter@501709c8ba25` | The OpenTelemetryJsonContext owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Stores/EfOpenTelemetryStore.cs` | `converter:JsonStringEnumConverter@105359cfe148` | The EfOpenTelemetryStore owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Diagnostics/StructuredLogs/Elsa.Diagnostics.StructuredLogs/Endpoints/StructuredLogsJsonContext.cs` | `converter:JsonStringEnumConverter@6211f2d5c9f7, converter:JsonStringEnumConverter@6751426a5d7d` | The StructuredLogsJsonContext owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Stores/EfStructuredLogStore.cs` | `converter:JsonStringEnumConverter@288460014af9` | The EfStructuredLogStore owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs` | `WriteIndented@6a90bd7cce1d` | The CompositionCandidateBuilder owner retains human-readable generated composition documents and its established enum vocabulary; embedded JSON values stay raw. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationStore.cs` | `converter:JsonStringEnumConverter@fcaae0fb886b` | The EfSchemaFinalizationStore owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs` | `UnmappedMemberHandling@ed3cebc69e70` | EF tooling configuration refuses unknown operator configuration fields before selecting a context. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Persistence/EntityFramework/Tooling/EfToolingContextContract.cs` | `DefaultIgnoreCondition@19fca22f51e4, UnmappedMemberHandling@04fcbb62bafb` | EF process context receipts retain their closed shape and omit absent optional management fields. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Persistence/EntityFramework/Tooling/EfToolingContract.cs` | `UnmappedMemberHandling@d60d89af71e0` | EF tooling control messages refuse unknown fields before running schema operations. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Secrets/Api/SecretsApi.cs` | `converter:JsonStringEnumConverter@510b42c0a745` | The SecretsApi owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Secrets/Api/SecretsJsonContext.cs` | `converter:JsonStringEnumConverter@14abedc6398a, converter:JsonStringEnumConverter@6829bbc709f9` | The SecretsJsonContext owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Serialization/SecretsEfJson.cs` | `converter:JsonStringEnumConverter@44cabe7a7f2e` | The SecretsEfJson owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Serialization/Core/JsonBaseline.cs` | `Encoder@0df70baec4ab, PropertyNamingPolicy@4a320e312ef8, construction@7056f3395e5d, construction@df9718bad27b, converter:RawJsonElementConverter@bc7eb074a8d9, converter:RawJsonElementConverter@c82b715890ea` | Defines and copies the immutable baseline, including its raw JsonElement converter; these settings establish the contract itself. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Serialization/Core/JsonConversionContract.cs` | `PropertyNameCaseInsensitive@782c71b6f0b7, PropertyNamingPolicy@ef8789e6b1e6, converter:ScopedConverterFactory@3f1ae03ee653, modifier:lambda@3f99f5458acc` | Implements the supported scoped naming/matching selection and member-boundary converter, with operation-local metadata. This is the FR-004a conversion contract, not a host-wide deviation. | spec 1207 FR-004a/FR-016 |
| `Essentials/src/Serialization/Core/JsonHashInput.cs` | `Encoder@85b912a3b421, MaxDepth@abfa0c61e71b, construction@803a4f23b6a7, construction@9dcc7a88d831, converter:JsonStringEnumConverter@403922a6a159` | Sole independent hash profile: fixed default encoder, enum names and depth 1000. Only exact canonical bytes are hashed; this profile never writes stores or responses. | ADR 0091 hash amendment; spec 1207 Q11 |
| `Essentials/src/Serialization/Core/JsonPayloadConverterRegistry.cs` | `converter:converter@4043cd246548` | Collects the payload serializer owner's explicit startup converter contributions. The serializer keeps the baseline raw JsonElement converter ahead of these contributions and never mutates host HTTP options. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Serialization/Core/StrictDateTimeConverters.cs` | `converter:DateConverter@dee613cdf73a, converter:DurationConverter@3c1c4719cfed, converter:InstantConverter@6d01687008dd, converter:OffsetConverter@89761b071601, converter:TimeConverter@654a26fd3d85` | Implements explicit-offset API and scoped conversions, date-only/time-only rules and ISO durations; it is deliberately absent from the ordinary data baseline. | spec 1207 FR-004a/FR-016 |
| `Essentials/src/Serialization/SystemText/Extensions/JsonSerializerOptionsExtensions.cs` | `construction@07e33a150afa, converter:converter@2f78b24fc138` | Copies a caller-owned baseline-derived profile to add its explicit converters without changing the original options. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Serialization/SystemText/Handlers/RegisterJsonConverters.cs` | `converter:converter@3a164a9a855f` | Collects startup contributions to the payload serializer owner; the raw JsonElement converter retains precedence for carried JSON. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Serialization/SystemText/Services/JsonPayloadSerializer.cs` | `converter:DeterministicDictionaryConverterFactory@cd0975542694, converter:converter@b01ab305dda1, modifier:DeterministicOrderTypeInfoModifier.SortObjectMembers@78e87e70f9a0` | Retains its existing deterministic metadata ordering and explicit startup converter contributions; carried JsonElement values stay raw. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Dashboard/Persistence/EntityFrameworkCore/Stores/EfWorkflowPortfolioDataSource.cs` | `converter:JsonStringEnumConverter@b65ea1f1bc4b, converter:LosslessUtf16StringConverter@ce07163419ee` | The EfWorkflowPortfolioDataSource owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Dashboard/Persistence/EntityFrameworkCore/Stores/EfWorkflowRunHealthDataSource.cs` | `converter:JsonStringEnumConverter@439812373ba1` | The EfWorkflowRunHealthDataSource owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Design/Api/Services/AuthoringSchemaExporter.cs` | `converter:JsonStringEnumConverter@b26f22b64b2b` | The AuthoringSchemaExporter owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Design/Api/WorkflowsDesignJsonContext.cs` | `converter:JsonStringEnumConverter@11b58a2a692d, converter:JsonStringEnumConverter@2106ae1b3727, converter:JsonStringEnumConverter@5da54ee80b11, converter:JsonStringEnumConverter@c9473e2f9df1, converter:JsonStringEnumConverter@eadbac31025e, converter:JsonStringEnumConverter@ef2dbd06f216` | The WorkflowsDesignJsonContext owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Design/Reconciliation/Git/Services/GitDefinitionMetadata.cs` | `WriteIndented@c97be5bbd8f5` | The generated Git metadata envelope remains human-readable; raw carried values are not reformatted. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Design/Reconciliation/Git/Services/GitWorkflowExporter.cs` | `json-method:ToJson@934064b50e75` | The exact ToJson call is the GitDefinitionMetadata text serializer, not an EF owned-JSON mapping. Its owner writes through the baseline and preserves raw carried values. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Publishing/Api/WorkflowsPublishingJsonContext.cs` | `converter:CamelCaseEnumConverter@f0f8bdd86b54` | The WorkflowsPublishingJsonContext owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/ActivityTemplatePlacer.cs` | `converter:JsonStringEnumConverter@bc90566e9b99` | The ActivityTemplatePlacer owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Publishing/Persistence/EntityFrameworkCore/Stores/PublishingEfJson.cs` | `converter:JsonStringEnumConverter@124a5bf37d9d, converter:LosslessUtf16StringConverter@a91c36c6f070` | The PublishingEfJson owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowArtifactClosureSerializer.cs` | `construction@1c861b0a236d, modifier:DropRecomputedProjections@aa829b7c3240` | Copies the existing owner profile and omits recomputed executable indexes from the closure projection; raw descriptor/default values remain unchanged. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/RuntimeGeneratorEmissionScheduler.cs` | `construction@6cb142efa6db` | Copies the existing payload profile for a private generated-event projection without mutating startup or host options. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfBookmarkStateStore.cs` | `converter:JsonStringEnumConverter@c0d5090ba7f6` | Owned durable bookmark state uses enum names. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfSchedulerStateStore.cs` | `converter:JsonStringEnumConverter@47a461d21539` | Owned durable scheduler state uses enum names. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/RuntimeArtifactJson.cs` | `converter:JsonStringEnumConverter@525351fffb23, converter:LosslessUtf16StringConverter@ea853a07b9ed, modifier:lambda@911210df0958` | Retains executable-owned projection metadata, enum names and lossless UTF-16 identity encoding. Raw data is excluded from that encoding and written unchanged. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Agent/src/Api/AgentApi.cs` | `UnmappedMemberHandling@197b72b9c14e, UnmappedMemberHandling@c42e84dbfb8e` | The owner API refuses undeclared model credentials and forged request authority. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Agent/src/Core/Models/AgentJson.cs` | `MaxDepth@17995556a7e5, UnmappedMemberHandling@3945b5fe112d, construction@7320acd651c3` | Closed agent journal/control documents retain depth bounds and reject undeclared members; business payloads use protected strings or raw JSON slots. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Agent/src/Core/Models/AgentRetention.cs` | `MaxDepth@d228c7c0da6e` | Retention snapshots retain their explicit bounded owner document depth. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Agent/src/Elsa.Agent/Services/AgentSessionCodec.cs` | `UnmappedMemberHandling@782f75a705a6, construction@d661958515ea` | Copies the baseline-derived agent profile for the closed protected session envelope, refusing unknown envelope fields before consuming it. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Agent/src/Elsa.Agent/Services/AgentSettlementBoundary.cs` | `UnmappedMemberHandling@70ea9d2f3ce1` | Settlement handles are a closed authority-bearing shape; undeclared fields are refused. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Agent/src/Inspection/Api/AgentInspectionApi.cs` | `UnmappedMemberHandling@cb004b773030, UnmappedMemberHandling@f2b9c00194f8, converter:JsonStringEnumConverter@bfdff349114b, converter:JsonStringEnumConverter@edbf6942431b` | The owner inspection request contract refuses undeclared fields. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/Stores/EfAgentTurnStore.cs` | `MaxDepth@2899d76da16b` | The persisted agent journal retains its established bounded metadata depth; protected business content is opaque to the store. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Agent/src/Review/Api/HumanReviewApi.cs` | `UnmappedMemberHandling@d787de357a92, UnmappedMemberHandling@f8455b0f8379, converter:JsonStringEnumConverter@531d4c9c8aaa, converter:JsonStringEnumConverter@5440fdd2822c` | Decision commands refuse caller-authored actor fields; authenticated authority remains server-owned. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Decisions/src/Workflows/Runtime/DecisionWorkflowPin.cs` | `MaxDepth@0610f78e3dad, UnmappedMemberHandling@1bcfb670aaba` | A native decision pin retains its bounded closed versioned shape before artifact checks. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/Models/Elsa3ImportedActivityNodes.cs` | `converter:JsonStringEnumConverter@201afa9664c1` | The Elsa3ImportedActivityNodes owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Elsa3/src/Mapping/Conversion/Elsa3NativeStructureBuilder.cs` | `converter:JsonStringEnumConverter@64d0bc00b57b` | The Elsa3NativeStructureBuilder owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Api/EtlCursorCodec.cs` | `UnmappedMemberHandling@78adfc2a6564, UnmappedMemberHandling@a0db30581b8d` | The authenticated continuation envelope refuses undeclared cursor fields. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Core/EtlArtifactJson.cs` | `UnmappedMemberHandling@2cbd3bb5374c, converter:JsonStringEnumConverter@65bf833a3f94` | The ETL artifact contract retains enum names and refuses unknown metadata; JSON literal members remain raw. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Core/EtlArtifactV2Json.cs` | `MaxDepth@404adaba18c1, UnmappedMemberHandling@b254e32a65ab, converter:StrictEtlFilterConverter@0d3e90bae237, converter:StrictJsonStringEnumConverter@06a17e720897, converter:StrictJsonStringEnumConverter@08dd5f1cdae7, converter:StrictJsonStringEnumConverter@14fbad34ebc6, modifier:MarkSharedModelPropertiesRequired@3b2139028085` | The versioned ETL document retains its bounded closed shape, exact enum vocabulary and strict filter-union converter; literal values remain raw JSON. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Core/Runtime/EtlRuntimeJson.cs` | `UnmappedMemberHandling@901df2bd5ace, converter:JsonStringEnumConverter@7138dd198f63, converter:LosslessStringJsonConverter@a6f1bf9c734f` | ETL runtime metadata retains enum names and lossless UTF-16 identity strings; JSON values stay raw. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Design/Api/EtlDesignJsonContext.cs` | `UnmappedMemberHandling@151f4c957e17, UnmappedMemberHandling@9d8d226ff016, converter:EtlCamelCaseEnumConverter@86f847a444ef, converter:EtlCamelCaseEnumConverter@e9e1b44bf450, converter:EtlPipelineV2JsonConverter@4d14ed6dec65, converter:EtlPipelineV2JsonConverter@65bcd1a932f5` | The owner ETL design request contract refuses undeclared fields, keeps its enum spelling/integer refusal and bounds/validates its canonical pipeline document. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Persistence/EntityFrameworkCore/Stores/EtlDefinitionRecordCodec.cs` | `UnmappedMemberHandling@2e13ca89e674` | Persisted ETL definition metadata refuses undeclared fields before semantic projection validation. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Runtime/Api/EtlRuntimeApiJsonContext.cs` | `UnmappedMemberHandling@13bc7eb27f4f, UnmappedMemberHandling@f8e95ca09577` | The owner ETL runtime request contract refuses undeclared fields. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Workflows/Design/Services/EtlPublishedArtifactPinSource.cs` | `UnmappedMemberHandling@92271859fdad` | A published ETL pin has a closed versioned shape; extra fields are refused before artifact/hash checks. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Etl/src/Workflows/Runtime/Services/EtlNativeBoundary.cs` | `UnmappedMemberHandling@9cc2671c5d82` | A native ETL pin has a closed versioned shape; extra fields are refused before artifact/hash checks. | ADR 0091 decision 7; spec 1207 |
| `Extensions/ExtensionBuilder/src/Api/ExtensionBuilderStorage.cs` | `WriteIndented@ebe42809a821, converter:JsonStringEnumConverter@0ff4d4a50ea2` | The ExtensionBuilderStorage owner retains indentation for its generated human-readable metadata; carried JsonElement values remain raw. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Integrations/src/Connectors/OAuth2/OAuth2TokenService.cs` | `PropertyNameCaseInsensitive@1e03b4f31a43, PropertyNamingPolicy@a8330bf9a080` | External OAuth provider responses retain their defined snake_case vocabulary and tolerant property matching, scoped to the provider protocol rather than user values. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Integrations/src/Connectors/Slack/SlackJson.cs` | `PropertyNameCaseInsensitive@e2de6f8fe043` | External Slack response DTOs retain their provider-specific tolerant member matching; arbitrary dictionary keys remain verbatim. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Integrations/src/Sdk/Manifest/ConnectorManifest.cs` | `DefaultIgnoreCondition@97beaae3cad9, WriteIndented@b11c39862059, json-method:ToJson@4177f7993b2d` | Human-readable generated connector manifests retain indentation and omit absent optional metadata; this does not select the semantic manifest hash profile. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Rules/src/Workflows/Runtime/Models/RuleWorkflowProgramPin.cs` | `MaxDepth@bb0d82078ee9, UnmappedMemberHandling@e7550b6d6f16` | A native Rules pin retains its bounded closed versioned shape before artifact checks. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Rules/src/Workflows/Runtime/Services/RulesNativeBoundary.cs` | `MaxDepth@1bd28ad0b66f, UnmappedMemberHandling@fb418e820cbb` | Native Rules invocations retain bounded closed pin/control shapes; user fact payloads retain their established protocol. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Rules/src/Workflows/Runtime/Services/RulesNativeDeliveryInspector.cs` | `MaxDepth@3753447d952d, UnmappedMemberHandling@9761aaaaee98` | The native delivery proof has a bounded closed authority shape and refuses extra fields. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Weaver/src/Api/WeaverJsonContext.cs` | `converter:WeaverCamelCaseEnumConverter@5585263742ff, converter:WeaverCamelCaseEnumConverter@955315b4c11b, converter:WeaverCamelCaseEnumConverter@9e7ca51a8cd0, converter:WeaverCamelCaseEnumConverter@f8458ebc697d` | Both owner API and SSE contexts retain the module's camelCase enum labels. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Weaver/src/Elsa.Weaver/Services/WeaverInvocationIntegrity.cs` | `converter:JsonStringEnumConverter@9db4c2fee8f8` | Retained disclosure snapshots use enum labels with matching readers; raw JSON and ordinary numeric tokens remain data. Their digest alone uses JsonHashInput. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Weaver/src/GitHubCopilot/Services/WeaverGitHubCopilotProvider.cs` | `converter:JsonStringEnumConverter@32aa83e1cbd6` | The WeaverGitHubCopilotProvider owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Extensions/Weaver/src/Persistence/EntityFrameworkCore/Services/WeaverRecordCodec.cs` | `MaxDepth@1ffba1bbb3de` | Retained Weaver records keep their explicit owner depth bound, while raw JSON members preserve their text. | ADR 0091 decision 7; spec 1207 |
| `Studio/src/essentials/Elsa.Studio.Backends/StudioBackendProfiles.cs` | `MaxDepth@2736a7f581a0, UnmappedMemberHandling@026877a334d3` | Backend credential/profile configuration retains its bounded closed shape; arbitrary user workflow values are not admitted there. | ADR 0091 decision 7; spec 1207 |
| `Studio/src/essentials/Elsa.Studio.Hosting.Nuplane/ManagementConfigurationStore.cs` | `WriteIndented@9d8a7691b78b` | The ManagementConfigurationStore owner retains human-readable generated configuration. Unchanged JsonElement subtrees keep their original text. | ADR 0091 decision 7; spec 1207 |
| `Studio/src/essentials/Elsa.Studio.Hosting.Nuplane/PackageMaintenanceEndpoints.cs` | `converter:JsonStringEnumConverter@b089d75f13ed` | The PackageMaintenanceEndpoints owner retains its established enum vocabulary for matching typed readers; raw JsonElement members and dictionary keys retain the baseline behavior. | ADR 0091 decision 7; spec 1207 |
| `Studio/src/essentials/Elsa.Studio.Hosting.Themes/ThemeDirectorySource.cs` | `WriteIndented@7989d7d56348` | The ThemeDirectorySource owner retains human-readable generated configuration. Unchanged JsonElement subtrees keep their original text. | ADR 0091 decision 7; spec 1207 |
| `Studio/src/essentials/Elsa.Studio.Hosting.Themes/ThemeStoreApi.cs` | `WriteIndented@d2f10bc8b677` | The ThemeStoreApi owner retains human-readable generated configuration. Unchanged JsonElement subtrees keep their original text. | ADR 0091 decision 7; spec 1207 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/ConnectionsModelBuilderExtensions.cs` | `storage:json@9ac3496fca48, storage:jsonb@9ac3496fca48` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/ConnectionsMySqlDbContext.cs` | `storage:json@d5fab2997bc2` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/ConnectionsPostgreSqlDbContext.cs` | `storage:jsonb@69b2d06227d8` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/Migrations/Connections/MySql/20261031232322_Initial.Designer.cs` | `storage:json@b64bc9cdff5b` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/Migrations/Connections/MySql/20261031232322_Initial.cs` | `storage:json@fab00e8c0e6d` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/Migrations/Connections/MySql/ConnectionsMySqlDbContextModelSnapshot.cs` | `storage:json@3796ba002ae4` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/Migrations/Connections/PostgreSql/20261031232323_Initial.Designer.cs` | `storage:jsonb@2df734f30113` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/Migrations/Connections/PostgreSql/20261031232323_Initial.cs` | `storage:jsonb@bad35550a325` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Connections/Persistence/EntityFrameworkCore/Migrations/Connections/PostgreSql/ConnectionsPostgreSqlDbContextModelSnapshot.cs` | `storage:jsonb@65312cc8358c` | ConnectionRecord.Payload contains closed ConnectionDocument metadata: string settings, tags, credential references and typed state. It carries no raw JsonElement business value; the same classification applies to these exact generated historical sites. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.Designer.cs` | `storage:json@d0308b42a1cf` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260915205200_Initial.cs` | `storage:json@2a4a0fc884c0` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260918232200_OrdinalCollation.Designer.cs` | `storage:json@9b388ecea8da` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928002150_SchemaVersionStamp.Designer.cs` | `storage:json@866a293b0ddf` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20260928040738_SchemaFinalization.Designer.cs` | `storage:json@168866634277` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/20261020202445_FreeNameOnDelete.Designer.cs` | `storage:json@6bb7fb4e7c0e` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/MySql/SecretsMySqlDbContextModelSnapshot.cs` | `storage:json@9da4cbc4898a` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.Designer.cs` | `storage:jsonb@3ba9aeaf5bb3` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260910210216_Initial.cs` | `storage:jsonb@ec2153500557` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260911011058_WidenLookupKeys.Designer.cs` | `storage:jsonb@178f9816ebd2` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260918221655_OrdinalCollation.Designer.cs` | `storage:jsonb@f80a2d9db219` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928002110_SchemaVersionStamp.Designer.cs` | `storage:jsonb@c47a5596fdf2` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20260928040734_SchemaFinalization.Designer.cs` | `storage:jsonb@f4d365d71377` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/20261020202251_FreeNameOnDelete.Designer.cs` | `storage:jsonb@bdbd1f4c29b8` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/Migrations/PostgreSql/SecretsPostgreSqlDbContextModelSnapshot.cs` | `storage:jsonb@5f86d98ab9fb` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsMySqlDbContext.cs` | `storage:json@c9b2be838df5` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Essentials/src/Secrets/Persistence/EntityFrameworkCore/SecretsPostgreSqlDbContext.cs` | `storage:jsonb@a9f5b92e1516` | SecretRecord.Payload contains closed secret/version metadata and string values, including protected ciphertext. These exact current and historical columns fall under the ADR 0091 exclusion for protected or string content. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/AgentModelBuilderExtensions.cs` | `storage:json@7b55395d202f, storage:json@db94a170c3d2, storage:json@fceee32d67d0, storage:jsonb@7b55395d202f, storage:jsonb@db94a170c3d2, storage:jsonb@fceee32d67d0` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/AgentMySqlDbContext.cs` | `storage:json@2b56f93d0573` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/AgentPostgreSqlDbContext.cs` | `storage:jsonb@db5951b55bf8` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/Migrations/Agent/MySql/20261110122309_Initial.Designer.cs` | `storage:json@0489f81cece1, storage:json@35e9c3aeba28, storage:json@c17c3de53cb4` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/Migrations/Agent/MySql/20261110122309_Initial.cs` | `storage:json@19dac4445898, storage:json@29db941c00af, storage:json@3e6ab0668375` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/Migrations/Agent/MySql/AgentMySqlDbContextModelSnapshot.cs` | `storage:json@2a098d5702f7, storage:json@77a0a5d0f926, storage:json@8a1102c4785d` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/Migrations/Agent/PostgreSql/20261110122312_Initial.Designer.cs` | `storage:jsonb@157a773ccf46, storage:jsonb@4cfd2ccdd613, storage:jsonb@c17be21ea0de` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/Migrations/Agent/PostgreSql/20261110122312_Initial.cs` | `storage:jsonb@84e5bc2a8249, storage:jsonb@d809a51e2f3b, storage:jsonb@e602b21fe722` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Agent/src/Persistence/EntityFrameworkCore/Migrations/Agent/PostgreSql/AgentPostgreSqlDbContextModelSnapshot.cs` | `storage:jsonb@430810b875cb, storage:jsonb@6fe9be048573, storage:jsonb@f8a43a937a95` | These exact Agent payload columns contain closed lifecycle, ownership, provenance, revision and delivery metadata. Business request/result JSON is protected before persistence and stored as opaque strings, not raw JSON. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Decisions/src/Persistence/EntityFrameworkCore/Migrations/Decisions/MySql/20261105223018_Initial.Designer.cs` | `storage:json@e92e59bb0916` | Retained historical Initial schema only, subsequently widened to text by PreserveJsonText. This receipt does not permit native JSON in the current Decisions model or snapshot. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Decisions/src/Persistence/EntityFrameworkCore/Migrations/Decisions/MySql/20261105223018_Initial.cs` | `storage:json@fc0131797fb2` | Retained historical Initial schema only, subsequently widened to text by PreserveJsonText. This receipt does not permit native JSON in the current Decisions model or snapshot. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Decisions/src/Persistence/EntityFrameworkCore/Migrations/Decisions/MySql/20261123021759_PreserveJsonText.cs` | `storage:json@32adcd480895` | Only the explicit Down rollback restores the historical native JSON schema. The active Up target and current model use text; rollback does not promise raw-text preservation. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Decisions/src/Persistence/EntityFrameworkCore/Migrations/Decisions/PostgreSql/20261105223015_Initial.Designer.cs` | `storage:jsonb@5bc22ac0b4da` | Retained historical Initial schema only, subsequently widened to text by PreserveJsonText. This receipt does not permit native JSON in the current Decisions model or snapshot. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Decisions/src/Persistence/EntityFrameworkCore/Migrations/Decisions/PostgreSql/20261105223015_Initial.cs` | `storage:jsonb@7adf71fc5fca` | Retained historical Initial schema only, subsequently widened to text by PreserveJsonText. This receipt does not permit native JSON in the current Decisions model or snapshot. | ADR 0091 decision 7; spec 1207 FR-008 |
| `Extensions/Decisions/src/Persistence/EntityFrameworkCore/Migrations/Decisions/PostgreSql/20261123021754_PreserveJsonText.cs` | `storage:jsonb@30b319bf1ebe` | Only the explicit Down rollback restores the historical native JSON schema. The active Up target and current model use text; rollback does not promise raw-text preservation. | ADR 0091 decision 7; spec 1207 FR-008 |

Before Elsa 4's first stable release, the approved S-9 migration regenerates changed stored fixtures, migrations and
stored executables in place. It adds no old-hash verifier, format upcaster, family bump or runtime compatibility check.
From the first stable release, `JsonHashInput` and every hashed contract freeze; a change needs an explicit decision
covering identity, versioning, migration and verification. Runtime wire fixtures freeze at that release too. This
pre-stable rule applies beside the Distributed gate below, rather than claiming that today's fixtures are immutable.

## Changing the format of saved runtime state

Runtime state (bookmarks, executables, execution/scheduler/operational/control-plane/incident/durable-value
state, checkpoint commits, the post-commit outbox, the durable scheduler work queue, workflow trigger
bindings) must be able to evolve without silently breaking already-suspended workflows. The contract:

- **EF Core migrations are the mechanism.** Each module owns its own migrations set and its own
  `__EFMigrationsHistory_*` table, applied or validated on shell activation according to the module's
  `EfMigratePolicy`. See [`Essentials/src/Persistence/EntityFramework/README.md`](../Essentials/src/Persistence/EntityFramework/README.md)
  for the shared policy.
- **Loud enforcement on read.** A pending model change, a missing migration, or a provider mismatch fails
  shell activation rather than serving a partially readable store.
- **Every row carries its schema family's stamp, and reads go through the family's upcaster chain**
  ([spec 180](../specs/180-schema-upcaster-chain/spec.md)). A family declares, in its module's assembly, its current
  version and an ordered chain of upcasters (`[EfSchemaFamily(..., Upcasters = ...)]`, each upcaster an
  `IEfSchemaUpcaster` carrying `[EfSchemaUpcaster(from, to)]`). Its readable set is the current version plus every
  predecessor the chain reaches without a gap; a host's readability report credits exactly that set, and a store's
  check accepts exactly that set, because both are computed from the one declaration. A store checks the stamp first
  (`EfSchemaVersion.NotReadable(<Module>.Chain, row.SchemaVersion)`), evaluates its integrity clauses for the stamped
  version, then upcasts the row's decoded content columns together, one step at a time
  (`<Module>.Chain.Upcast<TEntity>(row.SchemaVersion, (nameof(row.A), row.A), ...)`, every declared content column of
  the table), before deserializing any of them. An upcaster works on the whole row, so one step can move data from one
  column to another, and the chain refuses a read that passes other columns than the table's declared ones, and a step
  that adds or drops one: a row is either at its stamp or wholly upcast (#2144). A stamp outside the readable set,
  below a gap included, is `EfSchemaVersionSkewException`; an upcaster that fails on a readable row, or returns other
  columns than it received, is corruption. A write stamps the current version on content it writes in the current format, so a
  row moves forward the next time it is written; a read never rewrites anything. A chain with a gap, a duplicate, a
  branch or a cycle, or one that does not end at the current version, fails the module's registration at startup
  (the build-time check is pending #2606). The two design contexts whose
  content EF deserializes in value converters read their current version alone and declare no chain.
- **A family declares which of its columns are content.** Beside `[EfSchemaFamily]`, `[EfSchemaContent(family,
  typeof(Entity), columns...)]` names the columns a read upcasts before it deserializes them and a write that changes
  them restamps; `[EfSchemaIntegrity(family, typeof(Entity), column, reason)]` names a document column compared as the
  bytes it was stored with, never upcast, and records why. Every document column of a stamped table is one or the other
  (`EfSchemaContentDeclarationTests`, against every module's model on every provider). Every read of a content column
  goes through the chain or deserializes nothing, and every write that changes one, bulk updates included, restamps
  the row; both hold by review until #2606 restores compiled checks. A column that restates
  part of a row's content and is compared with it, such as a bookmark's payload or a checkpoint marker's id sets, is
  content, so both sides of the comparison are in one format.
- **A golden-fixture gate freezes the wire format for the Distributed leaf only.** The
  `Elsa.Workflows.Runtime.Distributed.Tests` suite re-serializes a canonical instance of the placement and
  transport payloads and compares each semantically against a committed `Fixtures/v1/*.json` expectation,
  so a field add/rename/remove/retype on those two payloads cannot land without an explicit, versioned
  decision (see [`GoldenFixtureTestSupport`](../Essentials/tests/Workflows/Runtime/Distributed/Tests/GoldenFixtureTestSupport.cs)).
  The rest of the Runtime EF Core module (bookmarks, executables, execution/scheduler/operational/
  control-plane/incident/durable-value state, checkpoint commits, the post-commit outbox, the durable
  scheduler work queue, workflow trigger bindings) has no such fixture today; its state-record shapes are
  covered by the migration and read-enforcement rules above, not by a round-trip payload comparison.

### How to change a persisted runtime state record

Until 4.0 ships as a stable release, migrations are regenerated and every family stays at its baseline version
([ADR 0078](adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md),
Consequences): alter the entity and its model configuration, regenerate the module's migration for every provider
it ships, update the payload expectation, and treat installations carrying the older generation as
reset-and-republish upgrades.

From 4.0 on, a change to what a family stores is a new version of that family ([spec 180](../specs/180-schema-upcaster-chain/spec.md)):

1. Bump the family's `SchemaVersion` constant, and add one upcaster from the previous version to the end of its
   declared chain, beside the family's store code. It is a pure function of the row it receives (FR-019) and preserves
   every identity the row carries (FR-020). It receives every content column the family declares for the row's table
   and returns the same columns, changing the ones it has a change for and returning the rest as it received them,
   including a column a later version added, which is null on the rows it reads. A new document column is declared with
   `[EfSchemaContent]`, or `[EfSchemaIntegrity]` with its reason, and every store read of that table names it.
2. Keep the change expand-only, for content as for migrations: a renamed, retyped or restructured member is split
   across two versions (FR-027), and an executable's identity-hashed format never changes in place (FR-028).
3. Gate every integrity clause for a projection the version introduces on the row's stamp
   (`<Module>.Chain.IsAtOrAfter(row.SchemaVersion, "<version>")`), since an older writer left it unset (FR-008).
4. Commit the upcaster's fixture pair under `Fixtures/SchemaUpcasters/<family>/<from>-to-<to>/` in the module's
   test project - one row per table, `<table>.source.json` and `<table>.expected.json`, each a JSON object of the row's
   content columns, the table named by its entity type - record it in
   `Essentials/tests/Architecture/Tests/Baselines/schema-upcaster-fixtures.sha256`, and prove the
   upcast, the old-format round trip and the store's read of the source fixture (FR-022) with a test class deriving
   directly from `EfSchemaUpcasterProof<TUpcaster, TValue>(family, store)`, compiled in from
   `EfSchemaUpcasterFixtureSupport.cs`. The base class holds all three proofs; the module supplies only the store's
   half, as `SyntheticOrders.cs` does for the synthetic family. Every upcaster needs a pair and such a class, every
   committed pair needs a class that proves it, and a recorded fixture is never edited or deleted; these hold by
   review until #2606 restores a compiled check.
5. When the upcaster reaches a column the stores edit in place, as Identity's coordinators edit a user's registries,
   the write upgrades the whole row first, upcasting every content column and restamping it, so a stamped row is never
   partly in an older format (FR-014). An in-place content write restamps its row, directly or through a helper that
   does, and a restamp assigns every declared content column of its row from upcast values (#2144); both hold by
   review until #2606 restores a compiled check.

## Related runtime formats

The sections below are not about the serializer rule itself. They document persisted runtime formats that must evolve under the same rules, and are kept here until they move to the runtime docs.

### Stimulus routing indexes

The trigger + stimulus-routing feature (`WorkflowsRuntimeTriggersFeature`) adds one new persisted document
kind and two cross-cutting (across-execution / across-artifact) indexes. Both route through the same bridge
serializer and versioning as every other runtime kind.

- **Document kind `workflowTriggerBinding` (current/minimum version 2).** A durable index entry written at **publish
  time** mapping an external stimulus identity `(stimulusType, stimulusHash)` to a start-trigger activity
  inside a *pinned, published* executable — the piece Elsa 4 was missing that made "start a workflow from
  an external event" impossible. It is indexed over the published artifact, never the mutable authored
  definition. The binding also carries its publication, slot, provider-cardinality, and prepared/active authority.
  Two indexes back it: one over `stimulusHash`, the cross-artifact fan-out used by the router to start every
  workflow waiting on a stimulus, and one over `artifactId`, used to replace an artifact's bindings on
  republish. Writing an unroutable published trigger (a trigger node whose stimulus
  cannot be derived) **fails the publish** rather than persisting a trigger that can never fire.
- **New `by-stimulus` index on the existing `bookmarkState` kind.** Added additively so a single stimulus
  can resume *waiting instances across executions* (fan-in), not only within one `workflowExecutionId`.
  No version bump or upcaster is needed: the state record shape is unchanged; only a new index was declared.
- **New `by-parent-activity-execution` index on the existing `activityExecutionState` kind (#514 / #413 item 3).**
  Added additively so a composite (e.g. a `Parallel` fork/join) can read only the activity-execution states
  *directly parented by it* via `IActivityExecutionStateStore.ListByParentAsync`, instead of loading every
  activity-execution state in the workflow and filtering in memory — the join fires once per branch completion,
  so the whole-workflow read made it O(branches × workflow states). The index is a keyword over the **already
  persisted** parent identity. No payload change is needed: the state record shape is unchanged; only a new
  index was declared, so there is no payload to re-freeze — an unchanged shape is the wire-safety proof.
  The store queries the parent index and then applies a defensive
  in-memory `workflowExecutionId` filter, so the full `(workflowExecutionId, parentActivityExecutionId)` semantics
  hold identically across providers without relying on parent activity-execution ids being globally unique.

### Stimulus START idempotency is at-least-once

Stimulus delivery is an at-least-once world (a stimulus can be delivered more than once). The router's START
path dedups **only when an `idempotencyKey` is supplied**. Each matching start is then a keyed start
(`KeyedWorkflowStartIdentity`, #2195): the workflow execution id, and the start command and envelope ids, derive
from the key and the matched artifact, so a repeated delivery names the same execution and the start dispatcher,
which reads durable execution state, answers it as a duplicate. The guarantee is durable and cross-node: it holds
when a peer redelivers a PublishStimulus intent whose claim lapsed, and after a restart. When **no**
`idempotencyKey` is supplied the router makes **no** dedup guarantee — a duplicate delivery **may double-start**.
Callers that require start-once semantics must supply a stable `idempotencyKey`.

This replaced the earlier in-process `IStimulusStartDeduplicator`. Besides being scoped to one process, it
recorded a key before the start was dispatched, so the retry of a start whose first attempt failed was skipped as
a duplicate and acknowledged although nothing had started.

Two consequences follow from the dedupe being durable. A reused idempotency key on the stimulus API, for the same
artifact, is answered `SkippedDuplicate` permanently, on every node and after any restart; a caller wanting a second
start sends a new key. And a recurring-trigger occurrence starts its workflow at most once however often that
occurrence fires. The recurring-trigger pump relies on that: it fires each occurrence at least once, again after a
crash or a failed start (#2198), and the repeats converge on the first start.

A start that runtime admission sheds at capacity is the exception to "dispatched means started". It writes nothing
and leaves its key unconsumed, so the router reports it `Shed`, never `Started`, and a retry under the same key is a
fresh start (#2548). Each caller retries it instead of acknowledging it: the HTTP endpoint middleware and
`POST runtime/workflows/stimuli` answer `429` with `Retry-After` when every matched start was shed and nothing
resumed, the recurring-trigger pump releases the occurrence with backoff, and a `PublishEvent` delivery fails as
transient so the outbox redelivers it.

A start that its workflow's input contract refuses (#3728) also writes nothing, but a retry would be refused again, so
the router reports it `Refused` with the reason code and every caller settles instead of retrying. The other starts
and resumes of the same stimulus still run. The HTTP endpoint middleware and `POST runtime/workflows/stimuli` answer
`400` when every matched start was refused and nothing resumed, the recurring-trigger pump settles the occurrence, and
a `PublishEvent` delivery completes.

The keyed start identity is a frozen, persisted format with two versions, side by side. Its ids are
`wfexec:start:{version}:{digest}`, `command:start:{version}:{digest}` and `envelope:start:{version}:{digest}`; the
digest is the lowercase hex SHA-256 of the values `"elsa.workflow-start"`, the version and the start key (each
value's UTF-8 bytes prefixed by its 4-byte big-endian length).

- `v1` (#2195, `KeyedWorkflowStartIdentity.For`) is every keyed start except a recurring occurrence of a publication
  slot. The router builds its start key as `{idempotencyKey}:start:{artifactId}`, so one keyed stimulus that matches
  two workflows starts each of them once.
- `v2` (#2198, `KeyedWorkflowStartIdentity.ForOccurrence`) is a recurring-trigger occurrence of a schedule that
  serves a publication slot, routed with `StimulusStartKeyScope.Occurrence`. Its start key is
  `{occurrenceKey}:start`, with no artifact, and the occurrence key is
  `recurring:{slotId}:{executableNodeId}:{stimulusHash}:{occurrenceTicks}` with each id escaped (`%` as `%25`, `:` as
  `%3A`), as `RecurringTriggerSchedule.BuildOccurrenceKey` builds it. The key names the trigger, not the publication, so
  when a republish hands a due occurrence to the new publication (which serves another artifact), the new publication's
  fire and any fire the replaced publication already made converge on one start; the start dispatcher answers an
  existing `v2` execution as the duplicate whichever artifact it is pinned to. A router refuses an occurrence-keyed
  request that matches more than one binding, since the second would silently never start.
- A recurring schedule without a slot (legacy artifact-scoped indexing) keeps the key
  `recurring:{scheduleId}:{occurrenceTicks}` under `v1`.

A redelivery is recognized only by deriving the same id again, so any change to a derivation, or to the occurrence
key, silently starts every redelivered occurrence a second time. Change one by adding a new version beside `v1` and
`v2`, never by editing either; `KeyedWorkflowStartIdentityTests` pins the literals of both and of the occurrence key.

### Published executables are saved durably

A published workflow compiles to a `WorkflowExecutable` artifact that persists through the same Runtime
EF Core module as every other runtime state, written by
[`EfWorkflowExecutableStore`](../Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowExecutableStore.cs)
over the `IWorkflowExecutableStore` seam. Its payload carries the compiled workflow-scope variable
declarations (`workflowVariables`, #972), the pinned activity contract, and the explicit input-nullability
data required by typed value flow. The
`InMemory` executable store registered by `WorkflowsPublishingFeature` is a `TryAdd` default that the
Runtime EF Core persistence feature overrides; when durable persistence is composed, publishing is durable
by construction. The `PublishWorkflowRequestHandler` saves the compiled artifact and then builds its trigger
index in the same publish flow, so a published artifact **and** its start-triggers survive a host restart.
`RuntimeEntityFrameworkCoreEndToEndTests` proves this against a file-backed SQLite database reopened with a
fresh service provider. There is intentionally **no**
Publishing-owned executable store or manifest — a second store writing the same kind under different options
would be a wire-level format divergence, exactly the hazard the single owning module exists to prevent.

## Background

Programme codes previously in headings: the stimulus routing indexes were roadmap unit **W7** (findings **E3-1** and **E3-5**, the cross-execution fan-in); durable published executables were **DS-2** / **W17**.

Related: [Recovering after a crash](runtime-durable-resumption.md) · [Elsa glossary](glossary/elsa.md)
