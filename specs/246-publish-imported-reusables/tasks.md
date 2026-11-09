# Tasks: Publish imported Elsa 3 reusable activities so they run as graph activities

**Input**: Design documents from `specs/246-publish-imported-reusables/`: spec.md (D-1 to D-6, FR-001 to FR-020),
plan.md, research.md (R-1 to R-15), data-model.md, contracts/import-publisher-port.md, contracts/diagnostic-codes.md,
quickstart.md.

**Worktree**: `C:\Users\JoeyBarten\source\repos\w2796`, branch `2796-publish-imported-reusables`, reset onto `main`
at 9436d2193. Never the main checkout. Never merge `main` in; rebase if #2717 (spec 247) merges first and resolve the
shared files (R-15).

**Tests**:
- Required by framework §2.23: behaviour tests for every logic-bearing change. That covers the requested version id,
  the contract builder, publish order, conversion order and outcome source, the analyzer rule, the commit shape, the
  publish phase decisions, warnings persistence, the bridge mapping and the permissions.
- One feature-registration test: `Elsa3ImportActivitiesPublishing` registers a replacement contract whose absence
  changes apply behaviour (R-1). Every other registration is covered by the §2.23.1 composition test.
- xunit only, no FluentAssertions. `RealActivityCatalog` for every conversion and compile test.
- Elsa 3 input is test-owned export JSON only. No Elsa 3 instance, no external corpus.
- **Cadence**: test-after, as specs 236 and 239. The derived constitution sets no Elsa test-cadence gate (D9). Each
  story's tests follow its implementation within the story.
- Build and test only the projects listed per task; leave the whole solution to CI.

**Paths**:
- `PUB/` = `Essentials/src/Workflows/Publishing/`
- `PUBT/` = `Essentials/tests/Workflows/Publishing/Api/Tests/`
- `ID/` = `Essentials/src/Foundation/Identity/Elsa.Foundation.Identity/`
- `ARCH/` = `Essentials/tests/Architecture/Tests/`
- `M/` = `Extensions/Elsa3/src/Mapping/`
- `MT/` = `Extensions/Elsa3/tests/Mapping/Tests/`
- `IMP/` = `Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/`
- `IEF/` = `Extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore/`
- `IEFT/` = `Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests/`
- `BR/` = `Extensions/Elsa3/src/Activities/Design/Import/Publishing/` (new)
- `IT/` = `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/` (new)

**Format**: `[ID] [P?] [Story] Description`. `[P]` means a different file with no dependency on an incomplete task.

---

## Phase 1: Setup

- [X] T001 Create the bridge project `BR/Elsa3.Activities.Design.Import.Publishing.csproj`, modelled on
  `IMP/Elsa3.Activities.Design.Import.csproj`: same SDK, properties and packaging, no literal version. It references
  `IMP/Elsa3.Activities.Design.Import.csproj` and `PUB/Api/Elsa.Workflows.Publishing.Api.csproj`. Add it to
  `Elsa.Server.slnx` beside the other Elsa3 import projects. Add `BR/README.md` with one sentence on why it is its own
  project: "Adapter at the Elsa 3 migration boundary; merging it into the import would make the import reference
  Publishing's API implementation (framework §2.1, §2.16.1 classes 4–5)."
- [X] T002 Create the test project `IT/Elsa3.Activities.Design.Import.Integration.Tests.csproj`, modelled on
  `Essentials/tests/Secrets/Workflows/Tests/*.csproj`: TestHost, `Microsoft.EntityFrameworkCore.Sqlite`, the EF
  adapters for activities design, workflows design, publishing and runtime, plus `IEF/` and `BR/`. Copy
  `Fixtures/**/*.json` to the output. Add it to `Elsa.Server.slnx` under the Elsa3 tests folder. Run
  `dotnet build IT/` once.
- [X] T003 Create `IT/Support/Elsa3ImportHost.cs`, trimmed from
  `Essentials/tests/Secrets/Workflows/Tests/Support/SecretsCanaryWorkflowHost.cs`.
  - `WebApplication` with `UseTestServer()`, a CShells shell, SQLite for activities design, workflows design,
    publishing, runtime and `Elsa3ImportDbContext`, and the fake auth scheme with configurable permissions and tenant
    claims.
  - Features: CLR activity reconciliation, Sequence, ControlFlow, Flowchart, Graph design and runtime, `Elsa3Mapping`,
    `Elsa3ImportJsonActivities`, `Elsa3ImportActivitiesEntityFrameworkCore`, `WorkflowsPublishingApi`, the runtime
    API, and (switchable) `Elsa3ImportActivitiesPublishing`.
  - Helpers: `UploadCollectionAsync(fixtureNames)`, `SelectAsync`, `ApplyAsync`, `GetImportStatusAsync`,
    `PublishWorkflowAsync`, `RunWorkflowAsync` (execute, poll the instance, return its outputs and the executed node
    ids).
  - Fail fast if built-in reconciliation has not published Sequence, Flowchart and graph `Complete`.
- [X] T004 [P] Add the test-owned Elsa 3 exports under `IT/Fixtures/`, in the JSON shape of
  `MT/Fixtures/Flowcharts/*.json`:
  - `reusable-two-outcomes.json`: reusable flowchart body that completes with `Approved` or `Rejected` based on an
    input (no intrinsic: D-7).
  - `reusable-calls-reusable.json`: reusable `outer` calling reusable `inner`; `inner` has two selected versions
    (v1, v2).
  - `caller-routes-outcome.json`: non-reusable flowchart calling `reusable-two-outcomes`, with one branch per outcome
    that each set a distinct workflow output.
  - `reusable-refused-body.json`: reusable whose body the graph provider refuses at publish (e.g. an unpublished
    custom activity type), plus `reusable-depends-on-refused.json`.
  - `reusable-with-behaviour-warning.json`: reusable that converts with one `ELS3-BEHAVIOUR-*` warning.
  - `reusable-sets-variable.json`: reusable whose body uses SetVariable (refused, D-7).
  - `reusable-leaf-start-trigger.json`: reusable with a leaf `HttpEndpoint` marked `canStartWorkflow`.

---

## Phase 2: Foundational (blocks every story)

### Publisher: requested version id (R-2, FR-006, SC-006)

- [X] T005 In `PUB/Api/Services/ActivityDefinitionPublisher.cs`:
  - Add the trailing `string? RequestedVersionId = null` to `PublishActivityDefinitionRequest` (:27-33).
  - Validate it in the request validation (:926): when present it must be non-blank and fit the version id column
    length (reuse the existing length constant), else `RequestInvalid`.
  - At :298, use `request.RequestedVersionId ?? NewId("activity-ver")`.
  - Leave preflight unchanged.
- [X] T006 In `PUB/Core/Services/ActivityPublicationRequestFingerprint.cs`, include `RequestedVersionId` (null and
  absent hash the same, so existing receipts keep their fingerprint). Update the receipt record if it carries the
  request fields, and keep `EfActivityPublicationCommand.ValidateCommit` (:151-156) consistent.
- [X] T007 [P] Add `PUBT/ActivityDefinitionPublisherRequestedVersionTests.cs`, built like
  `ActivityPublicationPreflightParityTests.cs:139-155` (same fakes). Cases:
  - a requested id is used for the catalog version, publication, layout, edges and source reference;
  - absent → a generated `activity-ver-*` id (unchanged);
  - blank → `RequestInvalid`;
  - an id already used → `PublicationConflict`;
  - a replay with the same idempotency key and a different requested id → `activity.publication.idempotency-conflict`.

  Run `dotnet test PUBT` and
  `dotnet test Essentials/tests/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests`. Every existing test passes
  unchanged.

### Import models, codes and port

- [X] T008 [P] In `IMP/Models/ReusableActivityImportPlan.cs` (`ReusableActivityImportDiagnosticCodes`, append at the
  end): add `UnsupportedStartTrigger = "ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER"` and
  `PublishingUnavailable = "ELS3-REUSABLE-PUBLISHING-UNAVAILABLE"`, with XML docs per
  `contracts/diagnostic-codes.md`.
- [X] T009 [P] Create `IMP/Models/ReusableActivityPublicationState.cs` with the record
  `ReusableActivityPublicationState` and the enum `ReusableActivityPublicationStatus` (`Published`,
  `AlreadyPublished`, `Refused`, `NotPublishedDependency`, `NotPublished`) per data-model.md. Add the never-stored
  `Publications` property to `ReusableActivityImportReceipt` (`JsonIgnore(WhenWritingNull)`, as `Warnings`).
- [X] T010 [P] Create `IMP/Contracts/IReusableActivityImportPublisher.cs` with the port,
  `ReusableActivityImportPublishRequest` and `ReusableActivityImportPublishOutcome`, exactly as in
  `contracts/import-publisher-port.md`. Use the `ActivityDiagnostic` type Imp already reaches through
  `Elsa.Activities.Design.Core`; if it is not reachable, stop and report rather than add a reference.

### Mapping: contract builder, conversion order, outcome source

- [X] T011 Create `M/Services/Elsa3ImportedActivityContract.cs` (internal static `Build(Elsa3ConvertedDefinition)`):
  move the body of `MT/Support/GraphProviderProof.cs` `Contract(converted)` (:50-58) there. Make
  `GraphProviderProof.ValidateAsync` call it and delete the helper. Mapping tests reach it through the existing
  `InternalsVisibleTo` to its own test assembly; check it exists.
- [X] T012 [P] Add `MT/Elsa3ImportedActivityContractTests.cs` covering:
  - inputs and outputs keep reference key, name and type, with `elsa.json` as the default storage driver;
  - outcomes equal `BoundaryOutcomes` (`done` first, then the converted names);
  - a body with no Complete gives only `done`.
- [X] T013 Create `M/Services/Elsa3ReusableImportOrder.cs` (internal static, in Mapping, because Mapping references
  Imp and Imp has no `InternalsVisibleTo` to it): given the selected reusable
  items and the plan's `ReusableActivityDependency` edges, return the versions in dependency order. Within one
  definition, ascending `Version`; ties broken by `SourceVersionId` ordinal. A cycle throws
  `InvalidOperationException`; the analyzer has already refused cycles.
- [X] T014 [P] Add `MT/Elsa3ReusableImportOrderTests.cs`:
  chain, diamond, two versions of one definition with a dependant, independent items in stable order, and a cycle
  throwing.
- [X] T015 In `M/Services/Elsa3ReusableActivityImportMaterializer.cs` (R-9):
  - Convert the selected reusable items in `Elsa3ReusableImportOrder` order, then the non-reusable ones in their
    current order. The mutation's imported activities keep that order: it is the publish order Imp consumes, so Imp
    needs no ordering code of its own.
  - Build each rewrite's `Elsa3ActivityExactReplacement.OutcomeNames` from the already converted target's
    `BoundaryOutcomes` names (:116-136). A target that failed conversion leaves its caller with the existing
    dependency diagnostic.
  - Delete `CompletionOutcomes` (:144-177).
- [X] T016 Adapt the spec 239 outcome tests in `MT/` that asserted the JSON-scan behaviour (grep `OutcomeNames` and
  `CompletionOutcomes`). Add cases in `MT/Elsa3ReusableOutcomeSourceTests.cs`:
  - a PascalCase `Outcomes` key now routes;
  - a Complete-shaped object nested in a property is no longer counted;
  - an unreachable Complete is not offered as a port;
  - caller `OutcomeNames` equal the target's `BoundaryOutcomes` names for every reusable in
    `MT/Fixtures/Flowcharts/reusable-two-completes.json`.

  Run `dotnet test MT`.

### Analyzer: leaf start triggers refused (D-6, FR-019)

- [X] T017 In `IMP/Services/ReusableActivityCollectionAnalyzer.cs` (:113-127), for a reusable source whose
  `CanStartWorkflow` node owns no nested behaviour, add a blocking `UnsupportedStartTrigger` diagnostic (message,
  guidance and metadata `nodeId`, `activityType` per `contracts/diagnostic-codes.md`, at the node's path). Keep
  recording `DirectStarts`. A node that owns nested behaviour keeps only `UnsupportedTrigger`.
- [X] T018 Update the tests:
  - In `MT/ReusableActivityCollectionApplyTests.cs`, make `ValidCollection()` (:166) use `directStart: false` for
    `a-root`, so the five apply tests keep testing materialization.
  - In `MT/ReusableActivityCollectionImportTests.cs`,
    `Analysis_reports_deterministic_rewrites_direct_starts_missing_unsupported_and_complete_cycles` now also expects
    `UnsupportedStartTrigger` on `a`.
  - Add `Analysis_refuses_a_reusable_with_a_leaf_start_trigger` and
    `Analysis_reports_only_unsupported_trigger_for_a_trigger_with_nested_behaviour`.

  Run `dotnet test MT`.

### Commit: no catalog versions, null head, first draft (R-4, FR-004)

- [X] T019 In `M/Services/Elsa3ReusableActivityImportMaterializer.cs`, stop building the `ActivityDefinitionVersion`
  in `BuildActivity` (:179-229). Instead the `ImportedActivity` carries:
  - `ActivityDefinitionVersionId`, `Version` (`"{n}.0.0"`), `SourceVersionId`;
  - `Contract` (T011), `GraphManifest` (`BuildGraphManifest`) and `DependsOn` (version ids).

  The authoring row for a new definition gets `HeadVersionId = null`. Adjust `ReusableActivityImportMutation` and
  every consumer the compiler flags.
- [X] T020 In `IEF/Stores/EfReusableActivityImportCommand.cs`:
  - Remove the version preflight, the orphan rule, the `ActivityVersionKind` dispositions and the
    `ResolveAuthoringUpdate` head comparison (:167-297, :246-265, :469).
  - Create each new definition through `EfActivityDesignStores.ExecuteAsync(CreateActivityDefinitionRequest)` with:
    - authoring (`HeadVersionId = null`, authority `(Design, "elsa3.collection-import")`);
    - the initial draft for its lowest selected version (id `ReusableActivityImportIdentity.Create("activity-draft",
      sourceVersionId)`, revision 1, state `(contract, Provider("elsa.activity-graph", "2", manifest))`, label
      `Elsa 3 v{n}`, the import's tenant);
    - an initial layout with one empty record per graph node.

    It runs inside the shared transaction through the atomic writer already built at :339-341.
  - An existing compatible definition gets no activity write.
  - Keep the binding, receipt and workflow writes as they are.
  - If `CreateActivityDefinitionRequest`'s operation key (`create:{id}`) is not replay-safe under the command's
    retry, pass the command's commit-attempt key the way `Execute(key, …)` does, and say so in the PR.
- [X] T021 Update `IEFT/` tests (grep `ActivityVersionKind`, `HeadVersionId`, `NewVersions`). Assert:
  - after commit there is no catalog version for a reusable item;
  - a new definition has a null head and one active draft with its layout;
  - a reapply of the same plan writes no activity row;
  - a definition-key collision is still refused.

  Run `dotnet test IEFT`.

**Checkpoint**: the publisher accepts a requested id, conversion has one outcome source, the commit leaves drafts and
no versions. Nothing publishes yet.

---

## Phase 3: User Story 1 — An imported reusable activity runs in an Elsa 4 workflow (P1) 🎯 MVP

**Goal**: after apply, each imported reusable version is published, and a caller and the wrapper run it.

**Independent test**: `IT/ImportedReusableRunsTests.cs` passes against `reusable-two-outcomes` and
`caller-routes-outcome`.

- [X] T021b [US1] D-8 (added 2026-10-19): in `M/Services/Elsa3ValueFlowLowerer.cs`, refuse input, variable and
  result reads and memory references in a reusable body with `ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED`; Mapping *(Lifted by spec 265.)*
  tests in `MT/Elsa3ReusableValueReadTests.cs`; Essentials side fix `DescriptorPayloadSerializer.Options`
  (`AllowOutOfOrderMetadataProperties`). Integration fixtures route on literal logic.
- [X] T021a [US1] D-7 (added 2026-10-19): in `M/Conversion/Elsa3DefinitionConverter.cs`, refuse every node of a
  reusable body that resolves to an engine intrinsic with the new `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`
  (`MO/Elsa3ActivityDiagnosticCodes.cs`). Fix the root natural-outcome key the compile proof found (`Done` for a
  built-in root, `done` for an imported one). Update the 236 SetOutput/SetVariable reusable tests to expect the refusal.

- [X] T022 [US1] Create `BR/Services/PublisherReusableActivityImportPublisher.cs` (internal sealed), implementing
  `IReusableActivityImportPublisher` per `contracts/import-publisher-port.md`:
  - tenant check against `IActivityPublishingAuthorizationContext`;
  - `PreflightAsync`, then `PublishReviewedAsync` with `RequestedVersionId`;
  - map `ActivityPublicationRejectedException` and a non-`Applied` receipt to an outcome;
  - let infrastructure exceptions propagate.
- [X] T023 [US1] Create `BR/Elsa3ImportActivitiesPublishingFeature.cs`:
  `[ShellFeature("Elsa3ImportActivitiesPublishing", DisplayName = …, Description = …, DependsOn = new object[] {
  "Elsa3ImportJsonActivities", "WorkflowsPublishingApi" })]`, public, not sealed, virtual `ConfigureServices`
  registering the port as scoped (replacement contract: `Replace`, not `TryAdd`). Model it on
  `IEF/Elsa3ImportActivitiesEntityFrameworkCoreFeature.cs`.
- [X] T024 [US1] Create `IMP/Services/ReusableActivityImportPublishPhase.cs` (internal sealed, scoped, registered in
  `IMP/Elsa3ImportActivitiesFeature.cs`). For each imported activity in the mutation's order (R-5, T015):
  1. An active publication for the plan's version id with the plan's version → `AlreadyPublished`. With another
     version → `Refused(ELS3-REUSABLE-IDENTITY-COLLISION)`.
  2. Find the draft by its deterministic id. If it is missing, create it through `ICreateActivityDraftCommand`, with
     the expected head = the current head and `SourceVersionId` = the current head.
  3. Call the port with version, requested id, expected head, the draft revision and the idempotency key
     `import:{receiptId}:{versionId}`.
  4. Map the result to `Published` or `Refused`.

  It returns the list of states. The dependency gate is added in US3 (T033); until then, a version follows its
  predecessors.
- [X] T025 [US1] In `IMP/Services/ReusableActivityCollectionImporter.cs`:
  - Before materializing, if the selection holds a reusable item and no `IReusableActivityImportPublisher` is
    registered (resolve it optionally), throw the import's blocking diagnostic exception with
    `PublishingUnavailable`. Nothing is written.
  - After `command.CommitAsync` (:147), run the publish phase.
  - Put the states on the returned receipt's `Publications`.
- [X] T026 [US1] In `IEF/Stores/Elsa3ImportRecordCodec.cs` (:81-82), strip `Publications` when writing a receipt.
  Never store it. (`Warnings` is handled in US4.)
- [X] T027 [US1] Add the compile proof (FR-015, SC-003):
  - `MT/Support/CatalogResolvedDependencies.cs` maps each occurrence from `GraphActivityProvider.DiscoverDependenciesAsync`
    to `ActivityResolvedDependency` via `RealActivityCatalog.FindVersion`, with a test-local copy of
    `SourceOwnedActivityVersionPublisher.ToContract` (:231-293; outcomes from `elsa.outcomes`, default `Done`) and
    `DeclaredStructure` from `ActivityStructureDesignFacetReader`. Imported siblings use their converted contract.
  - `MT/Support/GraphProviderProof.cs` gets `CompileAsync(catalog, converted, siblings?)`, which builds an
    `ActivityTemplateCompilationRequest` and asserts zero error diagnostics, a non-null executable root and outcome
    mappings. Remove the "compile waits for #2796" remark (:23-26).
- [X] T028 [US1] Call `GraphProviderProof.CompileAsync` wherever `ValidateAsync` is called for a reusable conversion
  in `MT/Elsa3ReusableTerminalTests.cs`, `MT/Elsa3CollectionLoweringTests.cs` and the reusable cases of
  `MT/Elsa3FlowchartConversionTests.cs`. Run `dotnet test MT`. Every reusable fixture validated today compiles.
- [X] T029 [US1] Add `IT/ImportedReusableRunsTests.cs`:
  - `Applied_reusable_has_an_active_publication_contract_outcomes_facet_and_template` (scenario 1, SC-001);
  - `Caller_runs_the_published_activity_with_its_input` (scenario 2, SC-002);
  - `Direct_start_wrapper_runs_the_published_activity` (scenario 3);
  - `Reusable_using_an_intrinsic_is_refused_and_nothing_is_written` (scenario 7, D-7);
  - `Contract_and_facet_carry_done_and_each_converted_outcome` (scenario 4);
  - `Flowchart_bodied_reusable_publishes_and_runs` (scenario 5);
  - `Flowchart_caller_follows_the_non_done_outcome` (scenario 6: run once per outcome and assert the branch output).
- [X] T030 [P] [US1] Add `IT/PublisherBridgeTests.cs`:
  - a tenant mismatch → refused, the publisher is not called;
  - a preflight that is not publishable → refused with its diagnostics;
  - a rejected publish → refused with its code;
  - `Elsa3ImportActivitiesPublishing` registers exactly one port. Without the feature, an apply with a reusable item
    is refused with `PublishingUnavailable` and writes no row.

  Run `dotnet test IT`.

**Checkpoint (MVP)**: an imported reusable activity runs in an Elsa 4 workflow.

---

## Phase 4: User Story 2 — Reusable activities that use each other are published in dependency order (P1)

**Goal**: dependencies first, versions ascending, plan ids kept.

**Independent test**: `IT/ImportPublishOrderTests.cs` against `reusable-calls-reusable`.

- [X] T031 [US2] In `IMP/Services/ReusableActivityImportPublishPhase.cs`, make sure each subsequent version of one
  definition creates its draft only after the previous version is published, with the expected head = that version,
  so each step is an ascending major (R-3). A version the publisher refuses as lower than the head
  (`activity.version.bump-insufficient`) is reported `Refused` with that code (amended edge case).
- [X] T032 [US2] Add `IT/ImportPublishOrderTests.cs`:
  - `Dependency_is_published_before_its_caller` (compare publication timestamps or the receipt order);
  - `Versions_of_one_definition_publish_ascending_and_head_ends_on_highest`;
  - `Published_versions_keep_the_plan_ids_so_wrappers_and_callers_resolve`;
  - `Older_version_selected_after_a_newer_one_is_published_is_refused_with_bump_insufficient` (two applies).

  Run `dotnet test IT`.

---

## Phase 5: User Story 3 — A failed or interrupted publish is visible and resumable (P2)

**Goal**: every version reports a state; a refusal doesn't stop independent versions; a reapply resumes.

**Independent test**: `IT/ImportPublishResumeTests.cs` against `reusable-refused-body` and
`reusable-depends-on-refused`.

- [X] T033 [US3] In `IMP/Services/ReusableActivityImportPublishPhase.cs`, add the dependency gate: a version whose
  dependency is neither `Published` nor `AlreadyPublished` is `NotPublishedDependency` with `BlockedBy` set, and the
  port is not called. An infrastructure exception from the port is recorded as
  `Refused("activity.publication.failed")` with one diagnostic, and the phase moves on (FR-011).
- [X] T034 *(Done differently, 2026-10-19: the upload is deleted after a successful apply, so a same-key replay cannot
  re-convert. A same-key replay returns the stored receipt with live `Publications`; finishing a partly published import
  is a new apply of the re-uploaded export under a new idempotency key, whose commit reuses the rows and whose publish
  phase publishes only what is missing. FR-009 amended; accepted by the maintainer 2026-10-19.)* [US3] In `IMP/Services/ReusableActivityImportOperationService.cs` (:243-254), when the stored receipt
  matches (`AlreadyImported`), run the publish phase again for the plan's selection before returning, and return its
  states (FR-009). This needs the plan's selection and the materialized drafts' inputs, so check that the replay has
  what the phase needs (version ids, versions, dependencies, draft ids). If the stored receipt lacks any of it, derive
  it from the stored plan via the importer and note the choice in the PR.
- [X] T035 [US3] In `GetStatusAsync` (:280-289), fill `Publications` live: `AlreadyPublished` when an active
  publication exists for the plan's version id, `NotPublished` otherwise (FR-010 as amended).
- [X] T036 *(All but `Crash_after_publisher_commit_replays_to_the_same_publication`: its idempotency key is per receipt, and a resume is a new receipt, so the deterministic key covers a retry inside one apply only; left for review.)* [US3] Add `IT/ImportPublishResumeTests.cs`:
  - `Refused_version_reports_publisher_code_and_diagnostics_and_independent_versions_still_publish` (scenario 1);
  - `Dependant_of_a_refused_version_reports_NotPublishedDependency` (scenario 1);
  - `Reapply_after_fix_publishes_only_the_missing_versions` (scenario 2, SC-004): fix by making the missing activity
    available in the host, then reapply;
  - `Reapply_of_a_fully_published_plan_publishes_nothing_and_returns_AlreadyImported` (scenario 3);
  - `Status_read_reports_published_state_live`;
  - `Crash_after_publisher_commit_replays_to_the_same_publication` (simulate by publishing one version with the
    phase's idempotency key before the apply, then apply).

  Run `dotnet test IT`.

---

## Phase 6: User Story 4 — Conversion warnings survive replay and status reads (P2)

**Goal**: warnings are stored on the receipt and never reach the publication.

**Independent test**: `IT/ImportWarningsTests.cs` and the codec tests in `IEFT/`.

- [X] T037 [US4] In `IEF/Stores/Elsa3ImportRecordCodec.cs` (:81-82), stop setting `Warnings = null`, and remove the
  "Warnings belong to the apply response only" comment. Update the doc comments on `ReusableActivityImportReceipt.Warnings`
  and `ReusableActivityImportApplyResult` (`IMP/Models/ReusableActivityImportPlan.cs`). In
  `IMP/Services/ReusableActivityImportOperationService.cs` (:276), return the stored warnings on replay and in the
  status read.
- [X] T038 [P] [US4] In `IEFT/`, add codec tests: warnings round-trip with the content hash intact; a schema-`1.0.0`
  row without `Warnings` still reads (null); `Publications` is never written. Run `dotnet test IEFT`.
- [X] T039 [US4] Add `IT/ImportWarningsTests.cs`:
  - `Status_and_replay_return_the_warnings_of_the_committing_apply` (scenario 1);
  - `Publication_contract_and_facets_carry_no_conversion_warning` (scenario 2, FR-013).

  Run `dotnet test IT`.

---

## Phase 7: User Story 5 — Applying an import needs permission to publish (P2)

**Goal**: apply needs both permissions, checked before any write; publishing runs in the caller's tenant.

**Independent test**: `IT/ImportPublishPermissionTests.cs` and `ARCH/EndpointSecurityTests.cs`.

- [X] T040 [US5] In `ID/Authorization/AuthorizationServices.cs`, add `RequireAllPermissionsAttribute` beside
  `RequireAnyPermissionAttribute` (:270-283), as its mirror: `PermissionPolicyDescriptor.All`, and `Apply` calling
  `builder.RequireAllPermissions(...)`. Add one test next to the existing `RequireAnyPermissionAttribute` tests
  (grep its test file) asserting the policy name and the applied convention.
- [X] T041 [US5] In `IMP/Endpoints/ReusableActivityImportApi.cs` (:41-45), add the internal constant
  `PublishingManage = "workflows/publishing:manage"`, with a comment on the duplication (§2.17) and the test that pins
  it. In `IMP/Endpoints/Collections/Apply/Endpoint.cs` (:11), replace `[RequirePermission(Manage)]` with
  `[RequireAllPermissions(Elsa3ImportPermissions.Manage, PublishingManage)]`.
- [X] T042 [US5] Update `ARCH/EndpointSecurityTests.cs` (:91-92) for the apply endpoint's two permissions. Run
  `dotnet test ARCH --filter EndpointSecurity`.
- [X] T043 [US5] Add `IT/ImportPublishPermissionTests.cs`:
  - `Apply_without_publish_permission_is_forbidden_and_writes_nothing` (scenario 1, SC-005: 403, no receipt, no
    definition, no draft);
  - `Apply_with_both_permissions_publishes_in_the_callers_tenant` (scenario 2);
  - `Imp_publishing_permission_constant_equals_WorkflowPublishingPermissions_Manage`.

  Run `dotnet test IT`.

---

## Phase 8: Polish and cross-cutting

- [X] T044 [P] `M/README.md`: drop "Imported reusable activities are not yet published … until #2796" (:105). Add a
  short "Publishing imported reusable activities" section: commit then publish, dependency order, plan ids, states,
  bridge feature, and the start-trigger refusal. Shared with #2717: keep edits local to these lines.
- [X] T045 [P] `IMP/EXTENSION_POINTS.md`: add `IReusableActivityImportPublisher` (replacement contract, no default,
  implemented by `Elsa3ImportActivitiesPublishing`, absence → `PublishingUnavailable`). Add `BR/EXTENSION_POINTS.md`
  listing the implementation and the feature. Check `PUB/Api/EXTENSION_POINTS.md` and
  `M/EXTENSION_POINTS.md` for anything that names the request record or `CompletionOutcomes`, and update it.
- [X] T046 [P] Grep the repo for remaining "#2796" deferral notes (`specs/236*/`, `specs/239*/spec.md:25`,
  `M/README.md`, `MT/Support/GraphProviderProof.cs`, `docs/reports/`). Leave merged specs as historical records;
  update live docs and reports only.
- [X] T047 Run `dotnet test ARCH` (feature composition, InternalsVisibleTo, EF dependency guard, Elsa3 boundary,
  `ReusableActivityArchitectureTests`). If a guard pins project references for the new projects, add the narrowest
  allow-list entry and say so in the PR. Do not restructure to satisfy a guard; ask first.
- [X] T048 Refresh the generated maps: `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, then
  `git diff --stat docs/`. Stage every changed map by explicit path, `docs/maps/manifest.json` included. On Windows,
  `-- check` always says stale (CRLF), so rely on the diff.
- [X] T049 Run [quickstart.md](quickstart.md) steps 1–5 and record the results for the PR evidence comment.
- [X] T050 Re-check #2796 and open PRs for competing work before committing (AGENTS.md claims). Ask the maintainer
  before pushing or opening the draft PR against `main`.
- [X] T051 After spec 248 merged: refuse only real start triggers (D-6 amendment, 2026-10-20). Add
  `IReusableActivityTriggerClassifier` (Import) and its Elsa3Mapping implementation, prove it with
  `ReusableActivityStartTriggerTests` and `ReusableFlaggedStartTests`, and update the readiness guide.
- [X] T052 Guidance review against Elsa 3 semantics (R-19): per-type intrinsic advice including Finish, HTTP projection
  refusals on the endpoint, capture vs read, the regular-workflow route's outputs, `done` callers, and the Delay proof
  (`Elsa3ReusableGuidanceTests`, `ReusableBodyWaitTests`); readiness guide updated.
- [X] T053 Prove Event and mid-flow HTTP Endpoint waits in a reusable body (`ReusableBodyWaitTests`); file the
  Publish Event resume fault found on the way (#3107) and name it in the readiness guide.
- [X] T054 Set the spec's `**Status**:` line to `Implemented` in this PR, before it merges, and regenerate the
  spec-status map (docs/reference/spec-lifecycle.md transition rule).

---

## Dependencies and execution order

- **Phase 1** (T001–T004): T001 and T002 first; T003 after T002; T004 any time.
- **Phase 2**:
  - T005–T007 (publisher) are independent of the rest.
  - T008–T010 are parallel.
  - T011 → T012.
  - T013 → T014 and T015.
  - T015 → T016.
  - T017 → T018.
  - T019 needs T011 and T015. T020 needs T019. T021 needs T020.
- **US1** (T022–T030) needs all of Phase 2 and T001–T004. T022/T023 → T024 → T025 → T026. T027 → T028 can run
  alongside T022–T026. T029 needs T025 and T003.
- **US2** needs US1's publish phase. **US3** needs US1. **US4** needs only T020 and T026, and can run alongside
  US2/US3. **US5** needs only Phase 1 and T025 for its integration test; T040–T042 can run any time.
- **Polish** last.

## Parallel opportunities

- Phase 2: T005–T007, T008–T010, T011–T012, T013–T014 and T017–T018 are five independent strands.
- US1: the compile proof (T027–T028) runs alongside the publish-phase work (T022–T026).
- After US1: US4 (T037–T039) and US5 (T040–T043) alongside US2 and US3.
- Polish: T044–T046 in parallel.

## Implementation strategy

1. **MVP = Phase 1, Phase 2 and US1.** The defect is fixed: imported reusable activities publish and run (spec 236
   SC-006 proven).
2. Add US2 (real collections with dependencies and several versions), then US3 (visibility and resume).
3. US4 and US5 complete the decisions D-3 and D-4.
4. Polish, then the PR as a draft against `main`, after the maintainer says go.
