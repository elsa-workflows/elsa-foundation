# Research: Publish imported Elsa 3 reusable activities

**Spec**: [spec.md](spec.md) · **Date**: 2026-10-19 · **Base**: main at 9436d2193 (line numbers drift)

Abbreviations: **Imp** = `Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import`, **ImpEF** =
`Extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore`, **Pub** =
`Essentials/src/Workflows/Publishing/Api/Services/ActivityDefinitionPublisher.cs`.

Items marked **(agreed 2026-10-19)** change or narrow the spec; the maintainer agreed them on 2026-10-19.

## R-1 Where the publish call lives: an Import-owned port and a bridge project (agreed 2026-10-19)

**Decision.** Imp declares one port, `IReusableActivityImportPublisher`, and calls it after the commit. A new Layer-3
bridge project `Elsa3.Activities.Design.Import.Publishing` (feature `Elsa3ImportActivitiesPublishing`, depends on
`Elsa3ImportJsonActivities` and `WorkflowsPublishingApi`) implements it over `IActivityDefinitionPublisher`. It is a
replacement contract with no default implementation. If no implementation is registered, an apply whose selection
contains a reusable version is refused before anything is written, with the new code `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE`.

**Rationale.**
- `IActivityDefinitionPublisher` and its request and response types live in `Elsa.Workflows.Publishing.Api`, a feature
  project that pulls in Runtime and Validations implementations (its csproj :19-35). Only `Elsa.Workbench` references
  it today. A direct reference from Imp would be an implementation-to-implementation reference between unrelated
  sub-domains (framework §2.1).
- Framework §2.7 / §2.7.1 sanctions an adapter for this. The bridge is the import boundary's compatibility seam
  (§2.16.1 class 4 or 5), so it earns its own project even under 100 lines.
- The bridge keeps Imp free of Publishing, and keeps the ImpEF reference list pinned by
  `ReusableActivityArchitectureTests.Elsa3_import_ef_bridge_references_design_ef_lanes_and_runtime_core_contracts_only`
  unchanged.

**Alternatives.**
- Imp references `Elsa.Workflows.Publishing.Api` directly: rejected (§2.1).
- Move `IActivityDefinitionPublisher`, its requests, `ActivityPublicationPreflightView` and
  `ActivityPublicationRejectedException` into `Elsa.Workflows.Publishing.Core`: a cross-lane change to Essentials' public
  surface for one consumer. Rejected (scope narrowly; ADR 0080 internal-by-default).

## R-2 Requested version id on the publisher (D-2, FR-006)

**Decision.** `PublishActivityDefinitionRequest` gains an optional trailing `string? RequestedVersionId = null`. In
`PublishCoreAsync`, `versionId = request.RequestedVersionId ?? NewId("activity-ver")` (Pub:298). The id is validated as
non-blank, at most the column length, when present (Pub:926). It is added to `ActivityPublicationRequestFingerprint`,
so an idempotency replay with a different requested id is an `idempotency-conflict`.

**Why this is small.** The review token does not include the version id; preflight compiles under
`StableCandidateVersionId`, and publish recompiles under the real id (ReviewPolicy:148-221). From Pub:298 the id
already flows into the catalog version, publication, version layout, edges, source reference and layout sidecar
(Pub:327-372, 864-896). `EfActivityPublicationDesignCommit.EnsureNewVersionAsync` (:383-392) already refuses an id or
SemVer in use, and `EnsurePublicationAbsentAsync` (:394-404) refuses a second publication. Both surface as
`PublicationConflict` (Pub:414-421), which is the existing version-conflict error FR-006 asks for. The template id is
content-addressed and independent of the version id. No production code parses `activity-ver-*`.

**Preflight request** does not need the field: it is id-independent.

**Alternatives.** A separate import-only publisher: rejected, it would fork the publish rules (FR-001).

## R-3 First and subsequent publishes, SemVer and head

- **First publish.** With `HeadVersionId == null`, `ResolvePublicationContextAsync` accepts the definition (Pub:481), and
  `ExpectedDefinitionHeadVersionId` must be null (Pub:631). The minimum version is `1.0.0`; an explicit higher version is
  accepted (Pub:592-619). The import's version string is `"{source.Version}.0.0"`, and Elsa 3 versions start at 1, so
  every first publish is accepted.
- **Next versions.** Each draft publish sets the head to the new version (DesignCommit:125). The minimum is head plus
  the bump the diff requires. Consecutive imported versions differ by at least one major, which satisfies any required
  bump. So ascending order always passes. The recommended version is set only on the first publish (DesignCommit:121).
- **A version lower than the current head is refused** (`activity.version.bump-insufficient`). There is no "advance
  only if higher" for draft publishes. This contradicts the spec's edge case "the lower version is still published;
  the head stays on the highest". **(agreed 2026-10-19)** Proposed: such a version is reported as not published with the
  publisher's code. It can arise only when a later apply selects an older version than one already published.
  Faithful or refuse.

## R-4 Drafts: the commit creates the definition with its first draft; the publish phase creates the rest

**Decision.**
- For a **new definition**, the commit writes the definition, its authoring state (`HeadVersionId = null`,
  `ContentAuthority = (Design, "elsa3.collection-import")`), the draft for the lowest selected version and its draft
  layout. All of these go through `EfActivityDesignStores.ExecuteAsync(CreateActivityDefinitionRequest)`, the same
  create path Studio uses (EfActivityDesignStores.cs:563-613). It runs inside the import's shared transaction through
  the atomic writer the command already builds (ImpEF EfReusableActivityImportCommand.cs:339-341). That path also
  writes the management projection.
- For **every other version** (later versions of a new definition, or new versions of an existing definition), the
  publish phase creates a draft just before publishing it, through `ICreateActivityDraftCommand(Draft, Layout,
  ExpectedDefinitionHeadVersionId)` (Activities Design Persistence Core, already referenced by Imp).
- There are many drafts per definition (`ActivitiesDesignDbContext.cs:103` is non-unique), and a published draft
  becomes `Published` (DesignCommit:122-124).
- **Draft content.**
  - `ActivityDefinitionDraftState(Contract, Provider(GraphActivityProvider.Key, "2", BuildGraphManifest(converted)),
    Options)`.
  - The draft layout has one record per graph node with empty data. The plan confirms the minimal shape the publisher
    accepts (Pub:471: the layout revision equals the draft revision).
  - `SourceVersionId` = the previous head, or null.
  - `PresentationLabel` = the Elsa 3 source version.
- **Draft ids** are deterministic, `ReusableActivityImportIdentity.Create("activity-draft", sourceVersionId)`. A resumed
  apply then finds the draft it created earlier instead of creating another.

**Alternatives.**
- Create every draft at commit time: rejected. Drafts created against a null head would carry a stale base for later
  versions.
- Keep writing catalog versions at commit time: rejected. The publisher refuses an existing catalog version (R-2), and
  the version it writes differs anyway: SourceKind, Hash and CreatedAt (Pub:750-777).

**Commit-side changes this implies.**
- In preflight, drop `plan.Versions`, the orphan rule, the `ActivityVersionKind` dispositions and the
  `ResolveAuthoringUpdate` head comparison (ImpEF :167-297, :246-265, :469).
- A new definition is created with its initial draft. An existing compatible definition gets no commit write.
- Remove the materializer's `BuildActivity` version row; it keeps the definition row and the graph manifest.

## R-5 Publish phase orchestration (FR-005, FR-008, FR-009, FR-011)

**Decision.** `ReusableActivityImportPublishPhase` lives in Imp and runs after a successful commit, and on an
`AlreadyImported` replay. The order is computed in Mapping (`Elsa3ReusableImportOrder`), which converts in that order
anyway (R-9). The phase consumes the mutation's order; Imp has no `InternalsVisibleTo` to Mapping.
- **Order.**
  1. Topological order over `ReusableActivityDependency` edges between selected reusable versions; the analyzer has
     already refused cycles.
  2. Within one definition, ascending `Version`.
  3. Ties broken by `SourceVersionId`, ordinal.

  No order is computed today; the analyzer only runs `FindCycles` (analyzer :303+).
- **Per version.**
  1. If an active publication exists for the plan's version id, it is `AlreadyPublished`, but only when its version
     matches the plan's version string. Otherwise it is `Refused` with `IDENTITY-COLLISION`. This mirrors
     `SourceOwnedActivityVersionPublisher.IsPublishedIdenticallyAsync` (:168-210).
  2. If a dependency of the version is not published, it is `NotPublishedDependency` and names that dependency.
  3. Otherwise: find or create the draft (R-4), then call the port, which runs preflight and then reviewed publish
     with `RequestedVersionId` = the plan's id, `Version` = the plan's version string,
     `ExpectedDefinitionHeadVersionId` = the current head and `IdempotencyKey` = `import:{receiptId}:{versionId}`.
     The result is `Published` or `Refused(code, diagnostics)`.
- A refusal does not stop versions that do not depend on the refused one (FR-011).
- **Idempotency key.** Deterministic, so a crash after the publisher commits but before the phase records the result
  replays to the same receipt instead of publishing twice.

## R-6 Publication state is read live, not stored on the receipt (agreed 2026-10-19)

**Finding.** Import receipt rows are immutable: "A stored row is never rewritten"
(Imp/Contracts/IReusableActivityCollectionImporter.cs:62). Recording a per-version publish state after the commit would
need either a rewrite rule or a new table with migrations in four providers.

**Decision (proposed).**
- The stored receipt gains the conversion warnings (R-7) and nothing else.
- The receipt gets a `Publications` property that is never stored: `WhenWritingNull`, and the codec strips it the
  way it stripped `Warnings`. It holds one `ReusableActivityPublicationState` per selected reusable version.
  - The apply, and a replay (which runs the publish phase again, per R-5), fill it with `Published`,
    `AlreadyPublished`, `Refused` (code plus the publisher's diagnostics) or `NotPublishedDependency` (dependency id).
  - A status read fills it with `AlreadyPublished` or `NotPublished`, read live from the publication store by the
    plan's version ids.
  - The REST shape stays one receipt, with an additive field that Studio ignores.
- Refusal diagnostics come only from the apply or replay that tried. Retrying recomputes them.

**Rationale.** It is always truthful, because a later Studio publish or retirement is reflected. It needs no new
storage. Replay already re-attempts.

**Spec impact.** FR-010 changes from "the apply result and the stored receipt MUST report … diagnostics" to: "the
apply result MUST report …; a status read MUST report published or not for each version". SC-004 is unchanged: the
same plan reapplied yields the same states.

**Alternative.** Append-only `Elsa3ImportPublication` rows per attempt: a durable failure history, but it needs a
migration and an upcaster for a benefit nobody has asked for. Rejected for now.

## R-7 Warnings on the receipt (D-3, FR-012)

**Decision.** `ReusableActivityImportReceipt.Warnings` already serializes when not null
(`JsonIgnoreCondition.WhenWritingNull`). The codec is what drops it: `Elsa3ImportRecordCodec.ToRecord` (:81-82) writes
`receipt with { Warnings = null }`. That strip goes away.
- The field is additive and nullable, so 1.0.0 rows (Warnings absent) deserialize unchanged. The schema stays
  `1.0.0`; no upcaster is needed (`EfSchemaChain`). The content hash covers the new field, as it does every field.
- The "never persisted" doc comments on the receipt and on `ReusableActivityImportApplyResult` change.
- `ReusableActivityImportOperationService` (:276) returns the stored warnings on replay.

## R-8 Permissions and tenant (D-4, FR-014)

**Decision.**
- The apply endpoint's `[RequirePermission(Elsa3ImportPermissions.Manage)]` becomes
  `[RequireAllPermissions(Elsa3ImportPermissions.Manage, "workflows/publishing:manage")]`. The policy runs before the
  handler, so a missing grant gives a 403 with no write.
- `PermissionRequirementMode.All` and the `RequireAllPermissions` convention already exist
  (Foundation.Identity `PermissionEndpointConventionBuilderExtensions.cs:18`). Only the attribute form is missing.
  `RequireAllPermissionsAttribute` is added beside `RequireAnyPermissionAttribute`
  (`AuthorizationServices.cs:270-283`) as its mirror, about 12 lines.
- `EndpointSecurityTests` (:91-92) pins the apply endpoint's permission and is updated.
- The tenant: the import scope (`ReusableActivityImportHttp.Scope`) and the publisher
  (`HttpContextActivityPublishingAuthorizationContext`) read the same claims (`elsa.identity.tenant_id` or
  `tenant_id`). The bridge asserts they match and refuses otherwise.
- The publisher itself checks no permission (only the endpoints do), so the import's check is the only gate. FR-014
  requires exactly that.
- Imp has no Publishing reference, so it duplicates the string `"workflows/publishing:manage"` as an internal
  constant (framework §2.17, duplication over dependency). A test in the bridge project's tests asserts it equals
  `WorkflowPublishingPermissions.Manage`.

**Alternative.** An imperative check in the operation service through `IPermissionAuthorizationService`: it works,
but it hides the requirement from the endpoint inventory that `EndpointSecurityTests` asserts on.

## R-9 One source for caller outcomes (D-5, FR-018)

**Finding.** The materializer's `CompletionOutcomes` (:144-177) scans the raw JSON for `Elsa.Complete` with exactly
one lower-case `outcomes` name. `BoundaryOutcomes` comes from the terminal rewrite (case-insensitive, Complete and End,
reached nodes only) through `Elsa3OutcomeKey.Assemble`. They differ for:
- PascalCase `Outcomes`;
- an unreachable or unresolved Complete;
- a Complete-shaped object nested in a property;
- a `done` spelling.

**Decision.**
- The materializer converts selected reusable versions in the topological order of R-5.
- It builds each caller's `Elsa3ActivityExactReplacement.OutcomeNames` from the already converted target's
  `BoundaryOutcomes` (their `Name`s).
- `CompletionOutcomes` is deleted.
- A target that failed conversion leaves its callers unconverted with the existing dependency diagnostic.
- No new mismatch code is needed: a single source cannot disagree. FR-018's "or refuse" branch is unused, and the
  plan says so.

**Alternative.** Keep both sources and compare them: more code, plus a new code for a state the design can rule out.

## R-10 Leaf start triggers refused (D-6, FR-019)

**Decision.**
- In `ReusableActivityCollectionAnalyzer` (:113-127), a reusable source with any `CanStartWorkflow` node now gets a
  blocking diagnostic.
- The new code is `ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER`, with guidance: "Elsa 4 cannot start a reusable activity
  from a trigger; split the trigger into its own workflow that calls the activity."
- The existing `UNSUPPORTED-TRIGGER` stays for a trigger that owns nested behaviour, so codes stay stable.
- `DirectStarts` is still inventoried. Studio shows it (`Elsa3ReusableImportPage.tsx:703`), and Studio is not touched.

**Tests that change.**
- `ReusableActivityCollectionImportTests.Analysis_reports_deterministic_rewrites_direct_starts_missing_unsupported_and_complete_cycles`
  gains the new diagnostic.
- `ReusableActivityCollectionApplyTests.ValidCollection()` (:166) drops its leaf start trigger from reusable `a`. Five
  apply tests use it; they assert materialization, not the trigger. A new test asserts the refusal.

## R-11 Compile proof (FR-015, SC-003)

**Decision.**
- `GraphProviderProof.CompileAsync` calls `GraphActivityProvider.CompileAsync(ActivityTemplateCompilationRequest)`
  (GraphActivityProvider.cs:114-193), which works only from `ResolvedDirectDependencies` and needs no store.
  Dependencies come from `DiscoverDependenciesAsync` (:195-216), mapped to `ActivityResolvedDependency` from
  `RealActivityCatalog.FindVersion`.
- A test-local `ContractOf(version)` mapping copies the private `SourceOwnedActivityVersionPublisher.ToContract`
  (:231-293), with outcomes from `elsa.outcomes` and `Done` by default (§2.17 duplication).
- `DeclaredStructure` comes from `ActivityStructureDesignFacetReader`.
- Imported siblings come from their converted contract.
- No new reference: `Elsa.Workflows.Publishing.Core` already arrives through `Elsa.Activities.Graph.Design`.
- The proof asserts zero error diagnostics, a non-null executable root and the outcome mappings. It is added to
  `Elsa3ReusableTerminalTests`, `Elsa3CollectionLoweringTests` and the reusable cases in
  `Elsa3FlowchartConversionTests`.
- `GraphProviderProof.Contract` is replaced by the production contract builder (FR-002, R-12).

**Rejected.** `ActivityTemplateCompiler`: it needs fake publication and template stores and a Publishing reference,
and proves nothing more.

## R-12 Contract builder in production (FR-002)

**Decision.**
- Add `Elsa3ImportedActivityContract.Build(Elsa3ConvertedDefinition)` in `Elsa3.Mapping`
  (`Services/`, beside the materializer). It returns `ActivityContract("1", inputs, outputs, BoundaryOutcomes)`.
- It is the test helper's body moved: inputs `(ReferenceKey, Name, Type, IsRequired: false, IsNullable: true, Default:
  null, StorageDriverType ?? "elsa.json")`, outputs likewise.
- Mapping already references `Elsa.Activities.Design.Core` through Design Persistence Core; the plan confirms the
  transitive path.
- The materializer stores the contract on each `ImportedActivity` so the publish phase does not reconvert.

## R-13 Run-time test home (FR-016, SC-002) (agreed 2026-10-19)

**Finding.** Nothing composes Elsa 3 in a host. The Workbench does not, and the Elsa 3 import EF tests hand-wire DB
contexts with no publishing or runtime. The closest in-process host is
`Essentials/tests/Secrets/Workflows/Tests/Support/SecretsCanaryWorkflowHost.cs`: a TestServer CShells shell over SQLite
for the Design, Publishing and Runtime lanes with a fake auth scheme. Guards keep Elsa 3 tests out of `Essentials/tests`.

**Decision (proposed).**
- Add a new test project `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`
  (`Elsa3.Activities.Design.Import.Integration.Tests`).
- Its host is trimmed from the Secrets canary: SQLite for activities design, workflows design, publishing, runtime and
  Elsa3 import. Features: CLR reconciliation, Graph design and runtime, Flowchart, Sequence, Elsa 3 mapping, import,
  import EF, the new bridge, the publishing API and the runtime API.
- Tests drive the public HTTP API: upload → selection → apply → submit, publish and execute an Elsa 4 caller → read
  outputs from the runtime store.
- Cases:
  - output propagation (SC-006);
  - a flowchart caller routing on a non-`done` outcome;
  - the direct-start wrapper running the published activity;
  - apply refused without the publish permission (SC-005).

**Alternatives.**
- Extend the Elsa 3 import EF test project: it is persistence-scoped, and its EF closure is pinned
  (`EfCoreDependencyGuardTests` :1203-1221). Adding TestHost, CShells and runtime there muddies it.
- The process-based Workbench journey (PostgreSQL in Docker): the Workbench does not compose Elsa 3.

## R-14 Spec 238, #2845, #2857: no further work

- **Spec 238.** It does not apply to activity publishing (`ActivityDefinitionPublisher` never calls
  `WorkflowTriggerBindingExtractor`). The wrapper's root is the graph node, so it holds no trigger. After D-6, no
  reusable body holds one either.
- **#2845.** The import uses `IElsa3DefinitionConverter`, not `IElsa3NodeConverter`, and nothing changes there.
- **#2857.** Flowchart bodies compile with the Flowchart feature registered (R-11). At run time, the host needs the
  Flowchart, Sequence and graph `Complete` built-ins published by reconciliation (R-13 composes them).

## R-15 Shared files with #2717 (spec 247)

- **Shared files:**
  - `ReusableActivityImportDiagnosticCodes`: two new constants, appended at the end.
  - `Extensions/Elsa3/src/Mapping/README.md`: the "not yet published … until #2796" line, plus a new publish-phase
    section.
  - `Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/EXTENSION_POINTS.md`: the new port.
  - The generated maps: a new project and a new test project.
- Whichever unit merges second rebases and resolves these. No other coordination.

## R-16 Engine intrinsics cannot be published in a graph activity (D-7, found during implementation)

**Finding.** The compile proof (R-11) showed that a converted reusable body holding an engine intrinsic node
(`elsa.intrinsic.set@1` and the others SetVariable, SetOutput, Correlate and SetName map to) does not compile:
`GraphActivityProvider.ValidateDependencies` requires a resolved dependency for every node, the template compiler resolves
dependencies only from publications, and intrinsics have neither a catalog row nor a publication. Neither the graph
provider nor the graph runtime has intrinsic support.

**Decision (maintainer, 2026-10-19).** Conversion refuses such a reusable with
`ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`. Intrinsic support in graph activities, and spec 236's SC-006 output test
it enables, move to a follow-up spec ([#3023](https://github.com/elsa-workflows/elsa-foundation/issues/3023)).

## R-18 Graph body nodes cannot read graph-scoped values (D-8, found during implementation)

**Finding.** The integration tests showed that an imported reusable whose body uses `getInput("x")` reads the started
workflow's input `x`. A body node's portable parameters resolve through `RuntimeInputBindingResolutionContext`, built only
from the started workflow's request and variable frames. Graph inputs and variables live in `GraphActivityScope` durable
values, read only by the boundary's own expressions (input defaults, variable initial values, output mappings). A body
`ActivityResult` reference keeps its template-local producer id after placement and is refused at workflow publish
(`VF-COER-001`), verified with a probe fixture.

**Tried and dropped.** Lowering `getInput` to a graph variable initialized from the boundary input: body nodes cannot
read graph variables either (`references unavailable variable … in scope 'workflow'`).

**Decision (maintainer, 2026-10-19).** Refuse such reusable bodies at conversion with
`ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED`; the Essentials gap is
[#3027](https://github.com/elsa-workflows/elsa-foundation/issues/3027).

**Side fix kept.** `GraphActivityProvider.Canonicalize` sorts payload keys, so a `Variable` or `ActivityResult`
parameter's `kind` discriminator came after other properties, and `RuntimeInputBindingCompiler` failed to read it (a 500
on workflow publish). `DescriptorPayloadSerializer.Options` now sets `AllowOutOfOrderMetadataProperties`.

## R-17 The graph manifest's natural-outcome source key (found during implementation)

**Finding.** Spec 236 wrote the schema-2 manifest's outcome mapping as `done → done`. A built-in root (Sequence,
Flowchart, a leaf) emits `Done` (`ActivityOutcomes.Done`, no separate reference key), so the compile's
`ValidateOutcomeMappings` refused every imported reusable (`activity.graph.outcome-mapping-source-unknown`). Validation
alone cannot see it because it has no resolved dependencies.

**Decision.** The source key is the root's own: `Done` for a built-in root, `done` (the Elsa 3 import's reference key)
for a root that is itself an imported reusable. The boundary key stays `done`.
