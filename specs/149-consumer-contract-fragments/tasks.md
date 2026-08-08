# Tasks: Consumer Contract Fragments as Build Output

**Input**: Design documents from `/specs/149-consumer-contract-fragments/`

**Prerequisites**: plan.md, spec.md, research.md (decisions R1–R12), data-model.md, contracts/contract-fragment-schema.md

**Tests**: included — the repo constitution mandates them (§2.23.2 branch-covered implementation tests; §2.21.1 golden rule). xunit only, no FluentAssertions.

**Organization**: by user story. Note on ordering: US1 and US2 are both P1; US2 (gates G1/G2) is sequenced first because fragments must carry truthful defaults/requiredness — doing gates first means `docs/contracts/` is generated once, correct from the start.

## Phase 1: Setup

- [ ] T001 Create `tools/contracts/Elsa.Contracts.Generator/Elsa.Contracts.Generator.csproj` (net10.0 exe, mirrors `tools/maps/Elsa.Maps.Generator` conventions) with `Program.cs` command routing (`emit` | `merge` | `check`, exit codes 0/1/2 per contracts/contract-fragment-schema.md); add project to `Elsa.Server.slnx`; ProjectReferences to `Elsa.Activities.Design.Reconciliation.Clr`, `Elsa.Activities.Design.Api`, `Elsa.Workflows.Design.Api`, `Elsa.Expressions.Core`, `Elsa.Modularity.Nuplane`
- [ ] T002 [P] Add `Mono.Cecil` to `Directory.Packages.props` (central package management) and reference it from the generator csproj only

## Phase 2: Foundational (blocking prerequisites)

- [ ] T003 Implement `tools/contracts/Elsa.Contracts.Generator/DeterministicJson.cs` — ordinal-sorted keys, 2-space indent, LF, UTF-8 no BOM, invariant culture, wire-form value serialization (same options family as `AuthoringSchemaExporter`), `sha256:<lowercase hex>` file fingerprints (research R7)
- [ ] T004 [P] Promote `AuthoringSchemaExporter` `internal static` → `public static` in `src/Elsa/Workflows/Design/Api/Services/AuthoringSchemaExporter.cs` with XML doc stating it is the single wire-coupled schema exporter shared by endpoints and the contracts generator (research R1); verify existing tests still compile/pass
- [ ] T005 [P] Implement `tools/contracts/Elsa.Contracts.Generator/FragmentModels.cs` — `ContractFragment`, `FeatureContract`, `FeatureOptionContract`, `ActivityContract`, `InputContract`, `OutputContract`, `StructureContract`, `ExpressionSurface`, `IntrinsicContract`, `ContractsManifest` per data-model.md (schemaVersion `"1.0.0"`)
- [ ] T006 Unit tests for DeterministicJson (key ordering, LF/no-BOM bytes, culture invariance under a non-invariant thread culture, fingerprint format) in `tests/Elsa/Contracts/Tests/DeterministicJsonTests.cs` (create `tests/Elsa/Contracts/Tests/Elsa.Contracts.Tests.csproj`, xunit, ProjectReference to the generator; add to slnx)

**Checkpoint**: tool skeleton builds; deterministic writer proven.

## Phase 3: User Story 2 — Trust defaults and output requiredness (Priority: P1, gates G1/G2) 🎯 first increment

**Goal**: every input with a statically representable CLR default emits `defaultValue`; every output descriptor emits `isRequired` — in the scanner-minted descriptors (persisted catalog) and therefore later in fragments.

**Independent Test**: project `HttpEndpoint` through reconciliation and the catalog handler — `ResponseMode.defaultValue == "Async"`, `Request`/`RouteData.isRequired == true`, `ParsedContent.isRequired == false`.

- [ ] T007 [US2] Implement `src/Elsa/Activities/Design/Reconciliation/Clr/Services/InitializerDefaultReader.cs` — System.Reflection.Metadata ctor-IL analysis of the parameterless-constructor chain, recognizing `ldarg.0; <ldc.i4.*|ldc.i8|ldc.r4|ldc.r8|ldstr|ldnull> [conv.*]; stfld <backing field>`; returns per-property: recognized constant, or not-statically-representable (research R2)
- [ ] T008 [US2] Add `HasStaticDefault` (bool, default false) to `InputDefinition` in `src/Elsa/Activities/Design/Core/Models/InputDefinition.cs` (additive tail parameter; old persisted rows deserialize with false)
- [ ] T009 [US2] Wire the G1 default ladder into `ClrAssemblyScanner.BuildModel` (`src/Elsa/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`): attribute `DefaultValue` wins → IL initializer constant → `default(T)` for non-nullable value types → explicit null for nullable/reference types → not-static (HasStaticDefault=false, DefaultValue=null); serialize derived defaults to wire form (camelCase/string enums)
- [ ] T010 [P] [US2] Branch-covered unit tests for InitializerDefaultReader in `tests/Elsa/Activities/Design/Reconciliation/Clr/Tests/` (locate the existing Reconciliation.Clr test project; add `InitializerDefaultReaderTests.cs`): enum initializer, int/long/float/double/string/bool, `ldnull`, computed initializer → not-static, no initializer on non-nullable value type → default(T), nullable without initializer → explicit null, base-class initializer chain, attribute precedence over initializer
- [ ] T011 [US2] Scanner-level G1 tests incl. the HttpEndpoint repro (`ResponseMode` → `"Async"`, `HasStaticDefault` true) in the same test project (`ClrAssemblyScannerDefaultValueTests.cs`)
- [ ] T012 [US2] G2: add `ReferenceKey` and `IsRequired` to `ActivityOutputDescriptorView` in `src/Elsa/Activities/Design/Api/Models/ActivityAuthoringCatalogView.cs`; add `HasStaticDefault` to `ActivityInputDescriptorView`; map all three in `ListActivityAuthoringCatalogRequestHandler.ToView` (`src/Elsa/Activities/Design/Api/Handlers/ListActivityAuthoringCatalogRequestHandler.cs`)
- [ ] T013 [P] [US2] G2 catalog tests in `tests/Elsa/Activities/Design/Api/Tests/` — extend `ActivityAuthoringCatalogTests.cs` (or add `ActivityAuthoringCatalogOutputRequirednessTests.cs`): outputs carry referenceKey/isRequired; HttpEndpoint repro (Request/RouteData required, ParsedContent not)
- [ ] T014 [US2] Golden-rule audit (§2.21.1): run `tests/Elsa/Activities/Design/**` and `tests/Elsa/Workflows/Design/**` suites; any pre-existing test asserting `DefaultValue == null` for initializer-defaulted inputs is updated as a *deliberate behavior change* with a note in the PR description — no test deletions

**Checkpoint**: catalog endpoint is truthful (G1/G2) with zero fragment machinery — independently shippable and verifiable.

## Phase 4: User Story 1 — Read the authoring contract at a pinned commit (Priority: P1)

**Goal**: per-assembly fragments emitted at build, embedded as resources, merged into committed `docs/contracts/` with fingerprint manifest.

**Independent Test**: at any commit, `docs/contracts/fragments/Elsa.Activities.Http.json` fully describes HttpEndpoint (inputs/defaults/outputs/requiredness) with no server; manifest fingerprints string-compare against fragment bytes.

- [ ] T015 [US1] Implement `tools/contracts/Elsa.Contracts.Generator/FeatureMetadataProjector.cs` — `[ShellFeature]` id/displayName/description/DependsOn via MetadataLoadContext; options by instantiating the feature in an execution ALC and projecting manifest-hint metadata identically to `ManifestHintReader` (`src/Elsa/Modularity/Nuplane/Services/ManifestHintReader.cs` — reference it if public, else reproduce its documented mapping; record which in the code comment) (research R1)
- [ ] T016 [P] [US1] Implement `tools/contracts/Elsa.Contracts.Generator/StructureProjector.cs` — instantiate the assembly's `IActivityStructureHandler` implementations, read Kind/SchemaVersion/SupportsScopedVariables, export `AuthoredPayloadType` via `AuthoringSchemaExporter`; dependency-bearing constructor → canonical diagnostic error
- [ ] T017 [P] [US1] Implement `tools/contracts/Elsa.Contracts.Generator/ExpressionSurfaceProjector.cs` — instantiate `IExpressionDescriptorProvider`s (descriptor type/displayName/editingMode) and invoke `IJavaScriptDeclarationContributor`s against a fresh declarations context (research R10)
- [ ] T018 [US1] Add declarative Jint sandbox-surface catalog (public static readonly, entries: `getVariable` function, frozen `args`/`variables` objects, `perVariableAccessor` pattern) in `src/Elsa/Expressions/JavaScript/Jint/` (exact file per package layout, e.g. `SandboxSurfaceCatalog.cs`), plus a guard unit test in the Jint test project composing `IsolatedJintEngine` and asserting declared globals exist and no undeclared top-level globals appear
- [ ] T019 [P] [US1] Implement `tools/contracts/Elsa.Contracts.Generator/IntrinsicsProjector.cs` — drive `IntrinsicAuthoringDescriptorProvider` (`src/Elsa/Activities/Design/Api/Services/`) into `IntrinsicContract` entries (engine fragment only)
- [ ] T020 [US1] Implement `tools/contracts/Elsa.Contracts.Generator/FragmentEmitter.cs` + `emit` command — drive `ClrAssemblyScanner` over the target assembly (references from `--references` rsp), attribute activities to features with the `ActivityFeatureAttributionResolver` rule (min-ordinal feature id), compose all projectors, per-activity `contentHash` via `DefaultActivityDefinitionHasher` canonical form, ordinal-sorted arrays; zero contributions → no output; diagnostics in canonical MSBuild format; infrastructure exceptions wrapped into tool diagnostics (§2.23.5)
- [ ] T021 [US1] Implement `tools/contracts/Elsa.Contracts.Generator/ResourceEmbedder.cs` — Mono.Cecil injection of `elsa.contract.json` into the intermediate assembly with symbol rewrite; idempotent (replaces existing resource) (research R5)
- [ ] T022 [US1] Create `tools/contracts/ContractFragments.targets` (AfterTargets CoreeCompile → run `dotnet exec` on the built CLI with `@(IntermediateAssembly)`/`@(ReferencePath)`; inject `ProjectReference` to the CLI with `ReferenceOutputAssembly=false`; gated on `$(EmitContractFragment)`) and import it conditionally from `src/Elsa/Directory.Build.targets` (research R4)
- [ ] T023 [US1] Set `<EmitContractFragment>true</EmitContractFragment>` in every contributing csproj: activity libraries (Http, ControlFlow, Sequence, Flowchart, Bpmn, Primitives, Testing, …), structure/expression features (Expressions, JavaScript.*, Jint, Http.JavaScript), `Elsa.Activities.Design.Api` (intrinsics) — enumerate by the same contribution markers the completeness guard uses; verify `dotnet build Elsa.Server.slnx` embeds resources
- [ ] T024 [US1] Implement `ContractsMerge.cs` + `merge` command — collect embedded fragments from built opted-in assemblies → `docs/contracts/fragments/*.json` + `submit-schema.json` (via `AuthoringSchemaExporter.ExportSchemaNode(typeof(SubmitDefinition))`) + `manifest.json`; fail loudly on unreadable/duplicate fragment (research R8)
- [ ] T025 [US1] Generate and commit the initial `docs/contracts/` (fragments, submit-schema, manifest) + write `docs/contracts/README.md` (convention, regeneration command, fingerprint verification, consumer filtering by featureId, "surface contributors must be dependency-free" note)
- [ ] T026 [P] [US1] Branch-covered generator unit tests in `tests/Elsa/Contracts/Tests/FragmentEmitterTests.cs` — per-surface projection against a purpose-built sample assembly (activity with initializer default, required/optional outputs, structure handler, expression provider, feature with DependsOn/options); merge failure branches; double-emit byte identity (`FragmentDeterminismTests.cs`)
- [ ] T027 [US1] Completeness guard test `tests/Elsa/Contracts/Tests/CompletenessGuardTests.cs` — scan built src assemblies for contribution markers (non-abstract `IActivity`, `IActivityStructureHandler`, `IExpressionDescriptorProvider`, `IJavaScriptDeclarationContributor`, `[ShellFeature]`) and fail when a contributing assembly lacks the `elsa.contract.json` resource (spec FR-001 completeness rule)

**Checkpoint**: contracts readable at the pinned commit, fingerprint-verifiable, resource-embedded.

## Phase 5: User Story 3 — Contract changes surface in the causing PR (Priority: P2)

**Goal**: stale committed contracts fail CI; contract diffs ride the causing PR.

**Independent Test**: mutate a contract-visible property without regenerating → `check` exits 1 naming the stale file; regenerate → exit 0.

- [ ] T028 [US3] Implement `ContractsFreshness.cs` + `check` command — regenerate to `%TEMP%/elsa-contracts-check-{pid}`, byte-compare every file **including manifest.json** against committed `docs/contracts/`, exit 1 with stale list + regenerate remediation text (`MapFreshness` conventions, deliberate manifest-inclusion difference documented in code comment) (research R8)
- [ ] T029 [US3] Add contracts freshness step to `.github/workflows/ci.yml` `build-and-test` job after `dotnet build`: `dotnet run --project tools/contracts/Elsa.Contracts.Generator -c Release -- check`
- [ ] T030 [P] [US3] Check-mode tests in `tests/Elsa/Contracts/Tests/ContractsFreshnessTests.cs` — fresh tree passes; tampered committed fragment fails naming the file; tampered manifest fails

**Checkpoint**: silent contract drift impossible on PRs to main.

## Phase 6: User Story 4 — Runtime catalog cannot drift from published contracts (Priority: P2)

**Goal**: equivalence proof — endpoint output == merged fragments of enabled features + overlay + dynamic union.

**Independent Test**: `dotnet test tests/Elsa/Contracts/Tests --filter Equivalence` green on a representative composed host.

- [ ] T031 [US4] Representative-host fixture in `tests/Elsa/Contracts/Tests/EquivalenceHostFixture.cs` — compose Activities Design API + Reconciliation.Clr + Sequence/Flowchart/ControlFlow/Http + Expressions/JavaScript + Groundwork SQLite (patterns: `WorkflowsDesignTestHost`, feature-composition tests); run CLR reconciliation at startup
- [ ] T032 [US4] Equivalence tests in `tests/Elsa/Contracts/Tests/EquivalenceTests.cs` (research R9): dispatch `ListActivityAuthoringCatalog(All)` + `ListActivityStructures` via mediator; assert CLR-activity items equal embedded-fragment entries of composed features after stripping overlay (`ActivityVersionId`, `Available`, `AvailabilityReason`, `Provenance`) and normalizing template boilerplate; intrinsics match the engine fragment; structures match fragment entries incl. payload schemas; a test-registered store-fed activity appears additively (union, never re-projection)

**Checkpoint**: one-projection rule phase 1 proven by test.

## Phase 7: Polish & Cross-Cutting

- [ ] T033 Refresh generated maps (`dotnet run --project tools/maps/Elsa.Maps.Generator -- all`) — new projects (`Elsa.Contracts.Generator`, `Elsa.Contracts.Tests`) make committed maps stale and would fail the `map-freshness` CI job; review the generated findings report (authorized as part of this unit)
- [ ] T034 [P] Add a `docs/contracts/` row to the AGENTS.md source-of-truth table ("Consumer authoring contracts (generated, committed)") and a link in `docs/maps/README.md`-adjacent docs where the maps convention is described
- [ ] T035 Full verification run per quickstart.md: `dotnet build Elsa.Server.slnx -c Release` → `merge` (no diff) → `check` (exit 0) → `dotnet test` on new/changed test projects → relevant `e2e-tests/` suite for design/publishing surface if touched behavior warrants (per AGENTS.md backend-e2e guidance)
- [ ] T036 Deployment-note propagation: verify quickstart's Model X hash-impact note (research R11) is reflected in `docs/contracts/README.md` and queued for the PR description / RFC comment

## Dependencies & Execution Order

- **Phase 1 → 2**: T001 blocks everything; T003/T005 block emitter work; T004 blocks T016/T024.
- **US2 (Phase 3)**: independent of the tool — only needs Phase 1–2 for nothing; can start immediately after Setup (T007–T014 touch product code only). Blocks *content correctness* of US1's committed contracts (T025).
- **US1 (Phase 4)**: needs Phases 1–2; T025 (commit contracts) must follow T009/T012 (G1/G2) so contracts are generated once.
- **US3 (Phase 5)**: needs T024 (merge) and T025 (committed baseline).
- **US4 (Phase 6)**: needs T023 (embedded resources) + T012 (view fields); independent of US3.
- **Polish**: after all stories.

### Parallel opportunities

- T002 ∥ T001; T004 ∥ T005 ∥ T003 (after T001).
- All of Phase 3 (product code) ∥ Phase 4 tool-side projectors T015–T019 (different files).
- T010 ∥ T013 (different test projects); T016 ∥ T017 ∥ T019.
- T026 ∥ T027; T030 ∥ T032 prep.

## Implementation Strategy

1. **First increment (US2)**: gates G1/G2 in product code — smallest diff, immediately verifiable against the served catalog, dissolves the two known repros.
2. **Second increment (US1)**: emitter → embed → merge → commit `docs/contracts/`.
3. **Third (US3)**: check + CI wiring (cheap once merge exists).
4. **Fourth (US4)**: equivalence test sealing the one-projection rule.
5. Polish: maps refresh, docs, full verification, hand-off notes.
