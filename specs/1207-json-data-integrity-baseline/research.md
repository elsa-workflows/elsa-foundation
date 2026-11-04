# Research: JSON data integrity baseline

**Spec**: [spec.md](spec.md) · **Date**: 2026-11-03 · Line numbers are from `origin/main` at `86bda483c`.
Paths are under `Essentials/src` unless they start with another root. "Inferred" means traced in code, not run.

## R-0 S-9 intake checks

The board lists nine checks for this spec (#3052, *S-9 intake*). Each is settled here, or marked open.

| # | Check | Result | Where |
|---|---|---|---|
| 1 | Hashes, digests and byte comparisons over serializer output, rows or artifacts | **Settled, with findings.** Most content hashes already hash a canonical re-serialization and are unaffected. Some hash serialized text directly, and several stores compare serialized text with `Ordinal`. Some hash preimages use options that would move every hash if they derived from the baseline. | R-1 |
| 2 | Producers of `Metadata`, `StructuralMetadata` and `Errors` keys that start with a capital | **Settled.** Capitalized keys exist only in scheduler command metadata and Elsa 3 diagnostics, and both are already written verbatim. Runtime metadata keys are lowercase. | R-2 |
| 3 | Studio mapping validation-error keys back to field paths | **Settled: no mapping.** `extractValidationErrors` copies keys verbatim, and its callers only join the messages. Studio's own tests already expect PascalCase keys. | R-3 |
| 4 | External clients and samples relying on camelCased output names | **Settled: none in the repository.** No samples folder exists, and no e2e script reads output keys. Two contract tests and their helpers pin camelCased keys. Clients outside the repository cannot be checked from here (Assumptions). | R-4 |
| 5 | Whether a NativeEndpoints group context governs binding and writing | **Settled: yes**, from the package's XML docs and its decompiled source. | R-5 |
| 6 | Studio preferences and structured-log responses once modules stop changing host options | **Settled: unchanged** (inferred from the types). Other host-option endpoints lose camelCased dictionary keys, which is the intended fix. | R-6 |
| 7 | `GitCanonicalJson` and `EfDesignSupport.Fingerprint` moving to a canonical preimage | **Settled; a smaller task than expected.** Neither hash moves when raw passthrough or the relaxed encoder lands. The Git hash is computed from file text only and is not compared today. The design fingerprint is already canonical. Raw passthrough does not reach Git files, though, because the exporter re-emits them through `JsonNode`. | R-7 |
| 8 | Which encoder the composed host HTTP options use | **Settled: relaxed** (the ASP.NET Core 10 default). `IPayloadSerializer` and every source-generated context use the default encoder. | R-8 |
| 9 | Stamped migration of the runtime EF store | **Settled as a task, with a finding.** No schema change, no migration to regenerate and no runtime-row golden fixture exist today. The change is in the writer only, and the stored format stays readable both ways. | R-9 |

## R-1 Hashes, digests and byte comparisons (check 1)

**Canonical re-serializations** (re-parse, sort, re-write) are unaffected by raw passthrough and by the encoder
change:

- `WorkflowExecutableHasher` (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowExecutableHasher.cs`
  L76–88, L457–489), with its own context `WorkflowExecutableHashJsonContext`. That context is camelCase, with string
  enums and `MaxDepth` 1000.
- `RuntimeExecutionMaterial.ComputeContentHash` (`Workflows/Runtime/Core/Models/RuntimeExecutionMaterial.cs`
  L102–125).
- `ExpressionDefinition` (`Expressions/Core/Models/ExpressionDefinition.cs` L87–93).
- `RuntimeCheckpointCommitFingerprint` (`Workflows/Runtime/Core/Models/RuntimeCheckpointCommitFingerprint.cs` L13–61).
- `ValueConversionPlan`, `ActivityProviderManifestFingerprint` and `ActivityLayoutHasher`, which use hand-written
  writers.
- `EfDesignSupport.FingerprintJson` (R-7).

**Preimages that would move if they derived from the baseline.** These serialize with no options, or with options
that differ from the baseline, and their hashes are stored or pinned:

| Site | Options today | Would move under the baseline because |
|---|---|---|
| `ActivityContract` schema fingerprint (`Activities/Runtime/Core/Models/ActivityContract.cs` L147, L161) | none: PascalCase, nulls kept | camelCase member names. It feeds the ADR 0038 artifact hash (`WorkflowExecutableHasher.cs` L243), so every executable's hash would move |
| `ExecutableActivityTemplateBehaviorHasher` (`Workflows/Publishing/Core/Services/ExecutableActivityTemplateBehaviorHasher.cs` L21–46) | none | camelCase |
| `DefaultActivityDefinitionHasher` (`Activities/Design/Persistence/Core/Services/DefaultActivityDefinitionHasher.cs` L33–53) | own: `WhenWritingNull`, PascalCase | nulls kept and camelCase |
| `WorkflowExecutableHashJsonContext` (see above) | own | the baseline has no string-enum converter or depth setting |

`docs/serialization.md` already says an executable's identity-hashed format never changes in place (spec 180,
FR-028). ADR 0091 decision 1 says *every* serializer derives from the baseline. Decision 7 lists only three hashes as
register entries. → **Finding F-2**, **Q4**.

**Hashes taken directly over serialized text.** None is canonical. Raw passthrough or the encoder changes them only
when their options change, or when the text they read was written by a changed writer:

- `EtlArtifactJson.Hash` (`Extensions/Etl/src/Core/EtlArtifactJson.cs` L38–39) serializes a `JsonElement` and hashes
  the bytes. Raw passthrough would make it hash the stored text instead of re-escaped text. Several ETL codecs and
  stores compare `GetRawText` with `Ordinal`: `EtlDefinitionRecordCodec.cs` L84, L90, L234, L254, L262 and
  `EtlRuntimeRecordCodec.cs` L125, L181.
- `ActivityForkMaterialFingerprint` (`Activities/Design/Core/Services/ActivityForkMaterialFingerprint.cs` L9–15).
- `AuthoringSchemaExporter.ComputeFingerprint` (`Workflows/Design/Api/Services/AuthoringSchemaExporter.cs` L91–95).
- `RuntimeActivityInputMaterializer.ComputeBindingFingerprint`
  (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeActivityInputMaterializer.cs` L620–646). It uses
  no options and is held in memory only (inferred).
- `ExecutionEvidenceCheckpointEnricher` (`Workflows/ExecutionEvidence/Services/ExecutionEvidenceCheckpointEnricher.cs`
  L201–221) hashes the stored `InlineValue` text. Raw passthrough changes the digests of values written after it,
  which is correct: the digest is of the value as stored.
- `WorkflowDraftStateHash` and `StructuredLogAppendFingerprint` hash stored text. They change only across a rewrite.

**Ordinal comparison of a re-serialization with stored text.** Each compares a value read back from storage with a
fresh serialization of the incoming value. With raw passthrough, a `JsonElement` read back keeps its stored text,
while the incoming value keeps the text it arrived with. Equal values can then compare unequal, which causes a
spurious write or a spurious concurrency fault (inferred):

- `Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowTriggerBindingStore.cs` L44, L68.
- `EfDesignAtomicWrite.cs` L257.
- `ActivitiesDesignDbContext.cs` L548 (value comparer).
- `EfClusterMembershipStore.cs` L182.
- `EfEtlRuntimeStore.cs` L45, L646, L841, L1004.
- `EfRuleRunStore.Delivery.cs` L183.
- `Elsa3ImportRecordCodec.cs` L199.

The `DeepEquals` comparisons ignore escaping and whitespace, but they do see an explicit null versus a missing member:

- `DispatchWorkflowResumeGuard.cs` L49, L88.
- `WorkflowControlTransport.cs` L26.
- `BpmnNativeWorkBoundary.cs` L128, L183, L210.
- `PublishingEfJson.cs` L34.
- `EfExecutableActivityTemplateStore.cs` L392–402.
- `EfDesignAtomicWriter.cs` L432–434.

Under FR-018 both sides are written by the same serializer, so they agree (inferred).

- **Decision**:
  - Every hash preimage keeps its own frozen options and is a register entry. It never derives from the baseline (Q4).
  - Every direct text hash and every `Ordinal` comparison of serialized values moves to the canonical form of FR-021,
    or gets a register entry explaining why text equality is meant.
- **Rationale**: ADR 0091 decision 3 already says nothing that decides whether a value changed may compare stored
  text. These sites are where that rule bites.
- **Alternatives**:
  - Let the preimages derive from the baseline and recompute every stored hash. Rejected: spec 180 FR-028 forbids
    changing an identity-hashed format in place, and ADR 0038 hashes are pinned in executables and fixtures.
  - Leave the `Ordinal` comparisons as they are. Rejected: they turn equal values into spurious writes or faults.

## R-2 Dictionary keys (check 2)

**`DictionaryKeyPolicy` is set** in:

- `Workflows/Runtime/Api/WorkflowsRuntimeJsonContext.cs` L32;
- `Workflows/Publishing/Api/WorkflowsPublishingJsonContext.cs` L39, L60;
- `Activities/Design/Api/ActivitiesDesignJsonContext.cs` L34, L58;
- `Connections/Api/ConnectionsJsonContext.cs` L9 and `ConnectionsApi.cs` L456;
- `Secrets/Api/SecretsJsonContext.cs` L14 and `SecretsApi.cs` L379;
- `Workflows/Design/Api/Services/AuthoringSchemaExporter.cs` L34;
- `Essentials/apps/Elsa.Workbench/Constants/JsonSerializerOptionsCache.cs` L15 (unused, R-10).

It is not set in Extensions or Studio. `IPayloadSerializer` already writes keys verbatim (`DeterministicDictionaryConverter`).

**Capitalized keys**:

- **Scheduler command metadata**: `RuntimeGeneratorEmissionScheduler.cs` L88–98 (`GeneratedEventId`, `BranchId`, …).
  Not confirmed to reach a Runtime API view.
- **Elsa 3 conversion diagnostics**: `Elsa3ConversionDiagnostics.cs` L58–71 and others. They are written through a
  Web context without a key policy, so they are already verbatim.
- **Keys that come from input**: user-task form fields (`WorkflowsRuntimeFaultRenderer.cs` L31, no options, already
  verbatim), Secrets `[error.Name]` (`SecretsApi.cs` L337) and Agent `[field]` (`AgentApi.cs` L871).
- **Runtime metadata constants** are lowercase `runtime.*` (`Workflows/Runtime/Core/Constants/RuntimeMetadataKeys.cs`).

So removing the policy changes only authored names (instance outputs, expression parameters, input names) and the
Secrets error keys built from input names.

## R-3 Studio (check 3)

- `extractValidationErrors` (`Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts` L2583–2598) copies
  `errors` keys verbatim.
- Its callers only join the messages (`getValidationErrorMessage` L2600–2606, `getProblemDetailsMessage`
  L2567–2581). No key-to-field-path mapping exists. The search covered `tryExtractValidationErrors`,
  `StudioValidationErrors`, `errors[key|field|name]` and `payload.errors`.
- Studio's tests already expect PascalCase keys kept: `auth-transport.test.ts` L393 and `registry.test.ts` L594.
- **No Studio change is needed.**

## R-4 Consumers of camelCased keys (check 4)

- **Tests that assert camelCased keys**:
  - `Essentials/tests/Activities/Design/Tests/Api/ActivitiesDesignSerializationContractTests.cs` L277–302;
  - `Essentials/tests/Workflows/Publishing/Api/Tests/PublishingSerializationContractTests.cs` L236–255.
- **Tests that pin the policy**:
  - `ActivitiesDesign…` L221–232, L249–255 and L345;
  - `Publishing…` L160–167, L194–215 and L320;
  - `Essentials/tests/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs` L17;
  - `AddDefinitionDeserializationTests.cs` L20.
- **Not found**:
  - no samples folder;
  - no e2e script that reads output keys;
  - `orderTotal` nowhere in `.cs`, `.ts`, `.tsx`, `.json` or `.http` files.
- **Decision**: invert the two contract tests and rename them `…dictionary_keys_are_verbatim`; drop the pinning
  assertions. The FastEndpoints-era wire shape is not kept.

## R-5 NativeEndpoints (check 5)

- **Package**: NativeEndpoints `1.0.0-preview.6` (`Directory.Packages.props` L120–122).
- **XML docs**: the `MapEndpointGroup` `jsonContext` parameter is "a source-generated serializer context governing
  both binding and writing" (`lib/net10.0/NativeEndpoints.xml` L740–744).
- **Decompiled source**: the group's options are `jsonContext?.Options ?? host JsonOptions ?? JsonSerializerOptions.Web`.
  Binding uses those options; writing uses `jsonContext.GetTypeInfo`. Responses default to
  `application/json; charset=utf-8`.
- **Groups in `src`**:
  - Every `MapEndpointGroup` passes a context.
  - Activities Design, Publishing and Workflows Design pass an owner `WireContext` built from their options.
  - Most others pass a generated `.Default`.
- **Exceptions inside groups** that write with the host options:
  - `StudioPreferencesApi.cs` L68, L116, L161, L198;
  - `StudioHomeLayoutApi.cs` L138–231;
  - `StructuredLogsApi.cs` L85.
- **Decision**: the group context is where each API's options come from (FR-002), and the architecture test reflects
  over every group's context.

## R-6 Host HTTP options (check 6)

| Call | Sets |
|---|---|
| `Activities/Design/Api/ActivitiesDesignApiFeature.cs` L146 → `ActivitiesDesignJsonContext.cs` L30–43 | case-insensitive reads, camelCase, **key policy**, **relaxed encoder**, resolver at index 0, enum converter |
| `Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs` L91 → `WorkflowsPublishingJsonContext.cs` L33–48 | the same |
| `Workflows/Design/Api/WorkflowsDesignApiFeature.cs` L100–104 | resolver at index 0 only |
| `Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs` L77–81 | resolver at index 0 only |

- **Studio preference and structured-log types**:
  - `StudioPreferenceDocument` (`Studio/Preferences/Core/Models/StudioPreferenceModels.cs` L11–16) holds strings, an
    int, a `JsonElement` and a `DateTimeOffset`;
  - `LogSource` holds strings and an int.
  - Neither has dictionaries or enums.
- **Effect of removing the two `Configure` calls**: the framework default (Web plus the relaxed encoder) is left, so
  these responses are unchanged (inferred). `/recent` and SSE use their own context.
- **Decision**: replace both `Configure` calls with resolver-only insertion, like Design and Runtime (FR-017). Pin the
  Studio preference response in a test.

## R-7 Git and design-store hashes (check 7)

**`GitCanonicalJson`** (`Workflows/Design/Reconciliation/Git/Services/GitCanonicalJson.cs` L22–41):

- **Write side**: the exporter writes `Indent(ToCompact(state))` (`GitWorkflowExporter.cs` L153). That is the
  `IPayloadSerializer` output re-parsed into a `JsonNode` and re-emitted, indented, with the default encoder.
- **Read side**: `HashFile(fileText)` is a `JsonNode` re-emit of the file, also with the default encoder
  (`GitWorkflowReconciliationSource.cs` L80).
- Both sides go through the default `JsonNode` writer, so the hash does not depend on the payload serializer's encoder
  or on raw passthrough.
- **Nothing compares it today.** `ContentHash` is "forward-compat metadata". The reconciler recomputes from `State`
  (`Workflows/Design/Reconciliation/Elsa.Workflows.Design.Reconciliation/Models/WorkflowVersionReconciliationModel.cs`
  L10–14).
- **Only a test relies on serializer output equalling the re-emit**: `GitCanonicalJsonTests.cs` L14–24 asserts
  `Sha256Hex(compact) == HashFile(Indent(compact))`.
- Committed versions are immutable (`GitWorkflowExporter.cs` L142–143).

**`EfDesignSupport.Fingerprint`** (`Workflows/Design/Persistence/EntityFrameworkCore/Stores/EfDesignSupport.cs`
L207–279):

- It is already canonical: `JsonDocument.Parse`, members sorted ordinally, a default writer, and a framed preimage
  `elsa-design-material:v1|kind|1|canonical`.
- Only `LegacyFingerprint` (L217–218, L245–246), kept to replay markers written by the first EF implementation,
  hashes raw Web text.
- `EfDesignSupport.Json` uses `JsonSerializerOptions.Web` (L198–199).

**What this means for ADR 0091.** Its Consequences say decisions 3 and 4 break the Git hash and the design
fingerprint, so every definition read back from Git would hash as changed. In the code, neither moves.

The code does show a different gap. Because the exporter re-emits through `JsonNode`, a Git file never holds data as
it was received: `René` is written as `René` and inner whitespace is lost. Decision 3 promises byte-identical data
in every store. → **Finding F-1**, **Q5**.

- **Decision** (proposal Q5):
  - Git files keep the data text. The exporter indents with a writer that writes `JsonElement` values raw, as the
    baseline does.
  - `GitCanonicalJson.HashFile` becomes an explicit canonical preimage: sorted members and a fixed encoder, in the one
    canonical helper of FR-021.
  - The test invariant is replaced by "a Git round trip keeps every hash".
  - `EfDesignSupport` keeps its canonical fingerprint, registered as a hash preimage. `EfDesignSupport.Json` derives
    from the baseline.
- **Alternatives**: register Git files as a normalized rendering. That is cheaper, but Git is where users diff and
  review definitions, so a changed escape there is visible.

## R-8 Encoder (check 8)

- **The host default is relaxed.** SDK `10.0.300` (`global.json`), `net10.0`. In the decompiled
  `Microsoft.AspNetCore.Http.Json.JsonOptions`, `DefaultSerializerOptions` is
  `new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }`. The host options in the
  composed host are therefore already relaxed.
  - The comment at `ActivitiesDesignJsonContext.cs` L37 says the same.
- **`IPayloadSerializer` uses the default encoder.** It sets no encoder (`Serialization/SystemText/Services/JsonPayloadSerializer.cs`
  L105–126).
- **Source-generated contexts use the default encoder too.** The `[JsonSourceGenerationOptions]` attribute cannot set
  one. So the Runtime and Design APIs, which pass `.Default` or an owner context, escape `é`, `<` and `+` today.
- **Encoders set in `src`**:
  - relaxed: `ActivitiesDesignJsonContext.cs` L37, `WorkflowsPublishingJsonContext.cs` L42,
    `HttpResponseInstructionDelivery.cs` L25, `IsolatedJintEngine.cs` L250 and Agent `SpecialistSessionCodec.cs` L35;
  - `UnicodeRanges.All`: `ObjectConverter.cs` L36, L41;
  - default: ETL `EtlConnectorInputBatchBuilder.cs` L928 and `EtlInputBatchBuilder.cs` L476.
- **Consequence**: under decision 4 the stored design text changes for new writes too (`IPayloadSerializer` moves from
  the default encoder to relaxed). The text changes; the value does not. R-7 shows the content hashes are unaffected.

## R-9 Runtime durable state (check 9)

**How the runtime store writes values today**:

- `RuntimeArtifactJson` (`Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/RuntimeArtifactJson.cs` L11–29)
  uses Web options, string enums, `LosslessUtf16StringConverter` and the default encoder, and keeps nulls. 27 files use
  it.
- Bookmarks use their own options (`EfBookmarkStateStore.cs` L518–523). So do five other runtime stores.
- `JsonElement` values are written with STJ's converter, so they are re-escaped (inferred).

**What is pinned**:

- Migrations are per provider, under `Workflows/Runtime/Persistence/EntityFrameworkCore/Migrations/Runtime/{MySql,PostgreSql,SqlServer,Sqlite}`.
- Each family carries a stamp (`[EfSchemaFamily]`; `SchemaVersion` constants per module, mostly `1.0.0`).
- The only golden fixtures for the runtime are the Distributed wire payloads
  (`Essentials/tests/Workflows/Runtime/Distributed/Tests/Fixtures/v1/*.json`), and they are compared semantically.
- `docs/serialization.md` L116–124 states that the rest of the Runtime module has no fixture.

**Why no migration and no stamp bump is needed**:

- Raw passthrough and keeping nulls change only the text a writer produces. Old rows keep reading the same, and new
  rows stay readable by older code: the JSON is equivalent, and a missing member still reads as null.
- No column or model changes, so there is no migration to regenerate.
- Before 4.0, `docs/serialization.md` L126–132 keeps every family at its baseline version (reset and republish).
- From 4.0 on, a change that keeps every stored row readable by both versions is not a new family version
  (spec 180 versions a change to what a family stores, not its escaping), so it needs no upcaster.

**What this means for ADR 0091.** Decision 7 and question 6 say "regenerated migrations and updated golden fixtures".
In the code, neither exists for this change. → **Finding F-4**, **Q7**.

- **Decision** (proposal Q7):
  - S-9 changes the runtime writers (`RuntimeArtifactJson` and the five store option sets) to derive from the
    baseline. Their frozen settings (string enums, `LosslessUtf16StringConverter`) are register entries.
  - S-9 adds the runtime store's first **byte-level** golden fixture for a stored data value, so durable data text is
    pinned from now on.
  - S-9 states the path from 4.0 on: a writer change that leaves every stored row readable by both versions needs no
    version bump; a change that alters what a row means is a new family version under spec 180.
- **Alternatives**:
  - Bump every runtime family's version now. Rejected: it adds upcasters with nothing to do.
  - Keep re-escaping as an exception. Rejected by question 6.

## R-10 Value path (re-verified) and scale

**Value-path sites** from the [29 Oct search](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6491919492),
checked again on `86bda483c`.

Unchanged:

- `RuntimeInputBindingCompiler.cs` L719, L788–806, L1019, L1042;
- `WorkflowExecutableInputValidator.cs` L119;
- `StatefulActivity.cs` L87, L89, L214, L399, L415;
- `BackgroundActivityCompletionCodec.cs` L15, L38, L48;
- `RuntimeDurableValueStorageDrivers.cs` L48, L64;
- `GraphActivity.cs` L58, L388, L410;
- `WorkflowBuilder.cs` L105, L334;
- `DescriptorPayloadSerializer.cs` L18;
- `ObjectConverter.cs` L27–42;
- `HttpResponseInstructionDelivery.cs` L22–25.

Moved:

- `ActivityCompletionProjector.cs` (L61) and `ActivityInputHydrator.cs` (L109) now share `ActivityValueJson.Options`
  (#3801, see below).
- `RuntimeActivityInputMaterializer.cs` JSON sites are now L316, L618, L679 and L702.
- `WorkflowExecutionStartDispatch.cs` serializes at L713.
- **ForEach no longer narrows numbers.** `ForEach.cs` keeps the collection as `JsonElement` (`ResolveJson` L261), and
  items become envelopes (L182–189). This was fixed in #3424. ADR 0091's Context still states the old behaviour.
  → **Finding F-6**.

**The highest-risk site was confirmed in code, and is now fixed.** An activity result was written with Web options
(camelCase, `ActivityCompletionProjector.cs` L16, L63) and read back with the defaults (case-sensitive,
`ActivityInputHydrator.cs` L109). A .NET-typed result bound to the same .NET type therefore lost every property whose
camelCase name differs from its C# name. This defect does not depend on any #3052 decision, so it was filed as general
defect **G-1**, [#3799](https://github.com/elsa-workflows/elsa-foundation/issues/3799).

[#3801](https://github.com/elsa-workflows/elsa-foundation/pull/3801) fixed it on 2026-11-04 (`4a81500f8`). Both sides
now use `ActivityValueJson.Options` (`Activities/Runtime/Elsa.Activities.Runtime/Services/ActivityValueJson.cs`: Web,
camelCase, strict numbers), at `ActivityCompletionProjector.cs` L61 and `ActivityInputHydrator.cs` L109. Its tests are
`ActivityResultRoundTripTests` and `ActivityValueJsonTests`. S-9 replaces `ActivityValueJson.Options` with
`JsonBaseline.DataBoundary` (T016). The reads then become exact (decision 2), where they are case-insensitive today.

**Admission now runs on every start.** [#3731](https://github.com/elsa-workflows/elsa-foundation/pull/3731)
(`4c002dadb`, #3728) makes `WorkflowStartDispatcher` apply the input contract to every new start through
`IWorkflowExecutableInputValidator`. The validator's type check (`WorkflowExecutableInputValidator.cs` L119,
`input.Value.Deserialize(targetType)` with the defaults) therefore governs the API, trigger, BPMN and child starts. On
the data-boundary copy it agrees with the activity's own read, so a start is never admitted with a value the activity
then refuses, or the reverse (FR-012, US3 scenario 6).

**Scale** (all source roots: Essentials, Extensions, Studio; excluding migrations, `obj` and `bin`; approximate):

| What | Count | ADR 0091 says |
|---|---|---|
| `new JsonSerializerOptions` constructions | 187 lines in 175 files (Essentials 129, Agent 20, ETL 9, Elsa3 8, Studio 9, others 12); 146 use `JsonSerializerDefaults.Web` | about 32 in 29 files |
| `[JsonSourceGenerationOptions]` | 32 | 28 |
| `JsonSerializerOptions.Web` / `.Default` | 4 / 1 | "several" |
| `JsonSerializer` calls with neither options nor type info | about 267 in 138 files | not counted |

The architecture test of decision 6 scans for all of these. Listing each one in the register would make the register a
census rather than a list of reasons. → **Finding F-5**, **Q2**, **Q3**.

## R-11 Elsa.Serialization.Core and existing guards

- **`Elsa.Serialization.Core`** exists (`Serialization/Core/Elsa.Serialization.Core.csproj`). It references only
  `Elsa.Primitives` and `Elsa.Events.Core`, and 30 projects reference it. It holds no options today
  (`IPayloadSerializer.GetOptions()` only, `IPayloadSerializer.cs` L63). The baseline fits there without new
  dependencies.
- **No architecture test checks JSON options.** The architecture suite is `Essentials/tests/Architecture/Tests`, with
  `ArchitectureGuardTests.*`, `EfSchemaVersionStampGuardTests` and frozen baselines under `Baselines/`. The new guard
  follows the baseline-file pattern those tests use.
- **`docs/serialization.md`**:
  - "Approved exceptions" (L52–75) still names FastEndpoints for the HTTP boundary (L59–60).
  - "Why it matters" (L21–26) says camelCase and case-insensitive.
  - There is no "Baseline deviations" section.
- **The Workbench `JsonSerializerOptionsCache`** is unused: its only reference is its own declaration.

## R-12 Content type and `nosniff` (decision 4)

- `X-Content-Type-Options` is set nowhere in Essentials, Extensions or Studio.
- Elsa's own APIs answer `application/json; charset=utf-8` (R-5). Error bodies are `text/plain`:
  - `AttentionApi.cs` L61;
  - `OpenTelemetryApi.cs` L178;
  - `StructuredLogsApi.cs` L327;
  - `WorkflowsDashboardApi.cs` L53, L98.
- SSE streams use `text/event-stream`.
- **Workflow HTTP responses** (`WriteHttpResponse`, delivered by `HttpResponseInstructionDelivery.cs` L69–104) use
  the content type the workflow chose, or one inferred from the body (`text/plain` for a string). A JSON body is
  written with the relaxed encoder. So relaxed-encoded data can leave as `text/html` when a workflow sets that.
- ADR 0091 decision 4 says "every Elsa response is `application/json`". That holds for Elsa's own APIs, not for
  responses a workflow composes. → **Finding F-3**, **Q6**.

## R-13 Unmatched-key warning (decision 2 leaves the mechanism to S-9)

System.Text.Json has no "warn on unmapped member" mode. It ignores unmapped members (the default), refuses them
(`JsonUnmappedMemberHandling.Disallow`), or collects them into an extension-data member.

- **Decision** (proposal Q1): before a data-to-.NET conversion, compare the JSON object's keys with the target's
  `JsonTypeInfo.Properties` (recursively for nested object members, case-sensitive) and record one warning per
  unmatched key, naming the key path and the target type. Then deserialize normally.
  - It lives in one helper in `Elsa.Serialization.Core` that every FR-012 conversion calls.
  - Its warnings reach the activity's invocation record through the diagnostics the conversion site already has.
- **Rationale**: no change to what deserialization does, no extension-data members on user types, and the same rule
  at every conversion site.
- **Alternatives**:
  - A type-info modifier that adds a synthetic extension-data property per type. It is invasive, and it changes how
    user types serialize.
  - Deserialize twice, once with `Disallow`, catching the exception. That costs two passes and finds only the first
    key.
