# Implementation Plan: Publish imported Elsa 3 reusable activities so they run as graph activities

**Branch**: `2796-publish-imported-reusables` (reset onto `main` at 9436d2193) | **Date**: 2026-10-19 |
**Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/246-publish-imported-reusables/spec.md`; research in
[research.md](research.md).

## Summary

**What.** Applying an Elsa 3 collection import commits definitions and workflows atomically. Then it publishes every
selected reusable version through a draft and `ActivityDefinitionPublisher`, dependencies first, under the plan's
deterministic version ids. Imported reusable activities get a publication, a contract with outcomes, an
`elsa.outcomes` facet and a compiled template, so callers and wrappers run them as graph activities. Warnings persist
on the receipt. Apply needs the publish permission. Reusables with a leaf start trigger are refused at analysis.

**How.**
- The publisher gains an optional `RequestedVersionId` (R-2).
- Imp gains a publish phase (R-5) behind an Import-owned port, `IReusableActivityImportPublisher`. A new bridge project
  implements the port over the publisher (R-1).
- The commit stops writing catalog versions. It creates a new definition with a null head and its first draft, through
  the Design create path (R-4).
- The materializer converts in dependency order, so caller outcomes come from the target's `BoundaryOutcomes` (R-9).
  The contract builder moves into production (R-12).
- Publication state is read live, not stored (R-6). Warnings stop being stripped (R-7).
- The apply endpoint requires both permissions through a new `RequireAllPermissionsAttribute` (R-8).
- Compile proof at the provider level in the Mapping tests (R-11). A new TestServer integration test project for
  import → publish → run (R-13).

**Implementation finding (2026-10-19, D-7):** graph activities cannot hold engine intrinsics, so a reusable body using
SetVariable, SetOutput, Correlate or SetName is refused at conversion, and spec 236's SC-006 output test moves to a
follow-up spec. The compile proof also found the root natural-outcome key spec 236 wrote (`done`), which a built-in root
does not emit; conversion now maps from `Done`.

**Implementation finding (2026-10-19, D-8):** graph body nodes cannot read the activity's inputs, its variables or other
body results (R-18, #3027), so a reusable body that does is refused at conversion. 246 publishes and runs reusables whose
bodies route on literal logic; Essentials gets one side fix (out-of-order type discriminators in descriptor payloads).

**Review points** (agreed 2026-10-19; they change the spec; listed under [Spec amendments](#spec-amendments)): R-1
bridge project, R-3 lower-version refusal, R-6 live publication state, R-13 new test project.

## Technical Context

**Language/Version**: C# / .NET (repo SDK, `global.json`), nullable enabled

**Primary Dependencies**:
- Existing: `Elsa.Activities.Design.Core` and `Elsa.Activities.Design.Persistence.Core` (drafts, contract, create
  commands), `Elsa.Activities.Graph.Design` (provider), `System.Text.Json`.
- New project reference, bridge only: `Elsa.Workflows.Publishing.Api`. Imp, ImpEF and Mapping gain no project
  reference.

**Storage**: no new table, no migration. `Elsa3ImportReceipt.ContentJson` gains the `Warnings` field (additive,
schema stays `1.0.0`). Drafts, publications, templates and source references are written by the existing Design and
Publishing stores.

**Testing**:
- xunit only (no FluentAssertions).
- `Extensions/Elsa3/tests/Mapping/Tests`: compile proof, outcome source, start-trigger refusal, contract builder.
  `RealActivityCatalog`, no new reference.
- `Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests`: commit shape, warnings
  round-trip.
- `Essentials/tests/Workflows/Publishing/Api/Tests` and `.../Persistence/EntityFrameworkCore/Tests`: requested id.
- New `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`: TestServer CShells host on SQLite, end to
  end (R-13), plus the bridge's behaviour and its permission-constant test.
- `Essentials/tests/Architecture/Tests`: guards; `EndpointSecurityTests` update.

**Target Platform**: server library (Design side) plus one bridge feature. No Runtime project changes.

**Project Type**: library inside the modular solution.

**Performance Goals**: the publish phase runs in the apply request. That is one preflight and one publish per
selected reusable version, each a provider compile. Collections are small: the corpus's largest reusable set is a
handful of definitions. A background job is out of scope (spec Assumptions).

**Constraints**:
- Faithful or refuse. No partial silent state: every selected version reports a state (FR-010).
- Deterministic draft ids, idempotency keys and publish order. A resumed apply never publishes twice (R-5).
- §E2.7: no Elsa 3 knowledge outside `Elsa3.*`; the Publishing change is generic (an optional id).
- §E2.2: no Runtime → Design reference (FR-017).

**Scale/Scope**:
- 1 new production project (bridge, ~120 lines), 1 new test project.
- 1 optional request field, 1 attribute, 2 diagnostic codes, 1 port.
- The commit drops version writes. The materializer is reordered. Analyzer: 1 rule.

## Constitution Check

*Loaded: `.specify/memory/constitution.md` (v6.0.2), `.specify/memory/constitution-framework.md` (v6.1.0),
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.2 Runtime takes no Design reference | PASS | No Runtime project touched. The bridge is a Design-side Elsa 3 project. |
| §E2.6.1 executable always runs | PASS, improves | Imported reusable activities become published artifacts with templates. Today callers silently fall back to plain authorable rows that cannot run as graphs. |
| §E2.7 Elsa 3 behind the import boundary | PASS | Elsa 3 code stays in `Extensions/Elsa3`. The Essentials changes (an optional `RequestedVersionId`, `RequireAllPermissionsAttribute`) carry no Elsa 3 knowledge. `Elsa3MigrationBoundaryTests` unchanged. |
| §E2.8 catalog as source of truth | PASS | Catalog versions are written only by the publisher, as for Studio. |
| §E5 versioning | PASS | Additive members only: an optional trailing record parameter, new attribute, new codes, new receipt field. No literal versions. |
| §E6 naming R1–R8 | PASS | `IReusableActivityImportPublisher` (port), `ReusableActivityImportPublishPhase`, `Elsa3ImportedActivityContract`, project `Elsa3.Activities.Design.Import.Publishing`. |
| §2.1 three-layer, no impl→impl across sub-domains | PASS via R-1 | Imp never references Publishing.Api. The bridge is the one sanctioned adapter (§2.7). |
| §2.5 / §2.5.1 features | ACTION | New feature `Elsa3ImportActivitiesPublishing` (public, not sealed, virtual `ConfigureServices`). Registration test in the integration test project. `FeatureCompositionTests` covers it. |
| §2.6.2 replacement contracts | ACTION | `IReusableActivityImportPublisher`: exactly one, no default. Declared in Imp `EXTENSION_POINTS.md`. Absence is a refusal (`PUBLISHING-UNAVAILABLE`), not a crash. |
| §2.7 / §2.7.1 adapter | PASS | The bridge isolates the Publishing dependency. |
| §2.16.1 minimum-viable project | PASS | The bridge is under ~100 lines plus its feature. Class (4)/(5): migration-boundary adapter; merging it into Imp would violate §2.1. One sentence in its README. |
| §2.17 duplication beats dependency | PASS | The permission string is duplicated in Imp, with a test pinning it (R-8). The test copy of `ToContract` is duplicated (R-11). |
| §2.20 EF packages | PASS | No new EF package anywhere. The integration test project uses SQLite like the Secrets canary; `EfCoreDependencyGuardTests` is checked. |
| §2.21.1 refactor preserves behaviour | ACTION | Behaviour changes on purpose, and the PR names the tests: commit no longer writes versions or heads (EF import tests); `CompletionOutcomes` removed (239 outcome tests now read `BoundaryOutcomes`); `ValidCollection()` loses its leaf trigger (D-6). |
| §2.22 / §2.22.1 docs and extension points | ACTION | Mapping `README.md`; Imp `EXTENSION_POINTS.md` (port); bridge `README.md` + `EXTENSION_POINTS.md`; Publishing.Api `EXTENSION_POINTS.md` if it lists the request; `docs/maps` regenerated. |
| §2.23.1–§2.23.3 tests and visibility | PASS | Behaviour tests per story. New types internal sealed except the port, its records and the feature. No cross-domain `InternalsVisibleTo`; the integration tests use public HTTP only. |
| §2.23.5 exception boundaries | PASS | Publisher refusals become states, never exceptions to the caller. Infrastructure failures are recorded as `Refused(activity.publication.failed)` per version. The commit is unchanged in its failure semantics. |
| §2.24 (draft) sanctioned patterns | PASS | Replacement contract, adapter/bridge, CQS. No new pattern. |
| ADR 0080 | PASS | Internal by default. Pre-GA: stores imported before this change are re-imported (spec Assumptions). |

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/246-publish-imported-reusables/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── import-publisher-port.md
│   └── diagnostic-codes.md
├── checklists/requirements.md
└── tasks.md                 # /speckit-tasks
```

### Source Code

```text
Essentials/src/Workflows/Publishing/
├── Api/Services/ActivityDefinitionPublisher.cs          # + RequestedVersionId on the request; used at :298; validated
└── Core/Services/ActivityPublicationRequestFingerprint.cs  # + RequestedVersionId
Essentials/src/Foundation/Identity/Elsa.Foundation.Identity/Authorization/AuthorizationServices.cs
                                                         # + RequireAllPermissionsAttribute (mirror of RequireAny)

Extensions/Elsa3/src/Mapping/
├── Services/Elsa3ReusableActivityImportMaterializer.cs  # topological conversion; OutcomeNames from BoundaryOutcomes;
│                                                        #   no catalog version row; CompletionOutcomes removed
├── Services/Elsa3ImportedActivityContract.cs            # new: contract builder (from GraphProviderProof.Contract)
├── Services/Elsa3ReusableImportOrder.cs                 # new: topological + version order (internal static)
└── README.md                                            # publish phase; drop "not yet published … #2796"

Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/
├── Contracts/IReusableActivityImportPublisher.cs        # new port + request/outcome records
├── Models/ReusableActivityImportPlan.cs                 # + 2 codes; receipt Publications; apply-result doc
├── Models/ReusableActivityPublicationState.cs           # new
├── Services/ReusableActivityCollectionAnalyzer.cs       # D-6 refusal
├── Services/ReusableActivityImportPublishPhase.cs       # new: R-5
├── Services/ReusableActivityCollectionImporter.cs       # PUBLISHING-UNAVAILABLE check; runs the phase after commit
├── Services/ReusableActivityImportOperationService.cs   # replay runs the phase; stored warnings; status fills Publications
├── Endpoints/Collections/Apply/Endpoint.cs              # RequireAllPermissions(import manage, publishing manage)
├── Endpoints/ReusableActivityImportApi.cs               # + publishing-manage constant (duplicated string)
└── EXTENSION_POINTS.md                                  # port

Extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore/Stores/
├── EfReusableActivityImportCommand.cs                   # no version writes; create definition + first draft; preflight rules
└── Elsa3ImportRecordCodec.cs                            # keep Warnings; strip Publications

Extensions/Elsa3/src/Activities/Design/Import/Publishing/          # new project
├── Elsa3.Activities.Design.Import.Publishing.csproj     # → Imp, Elsa.Workflows.Publishing.Api
├── Elsa3ImportActivitiesPublishingFeature.cs            # [ShellFeature("Elsa3ImportActivitiesPublishing", DependsOn = Elsa3ImportJsonActivities, WorkflowsPublishingApi)]
├── Services/PublisherReusableActivityImportPublisher.cs # contract behaviour
├── README.md, EXTENSION_POINTS.md
Elsa.Server.slnx                                         # + 2 projects

Extensions/Elsa3/tests/Mapping/Tests/
├── Support/GraphProviderProof.cs                        # + CompileAsync; Contract → production builder
├── Support/CatalogResolvedDependencies.cs               # new: catalog row → ActivityResolvedDependency (test ToContract copy)
├── Elsa3ReusableTerminalTests.cs, Elsa3CollectionLoweringTests.cs, Elsa3FlowchartConversionTests.cs   # + compile
├── ReusableActivityCollectionImportTests.cs, ReusableActivityCollectionApplyTests.cs                 # D-6, publish order
└── Elsa3ImportedActivityContractTests.cs                # new

Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests/   # commit shape, warnings
Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/                        # new project (R-13)
├── Support/Elsa3ImportHost.cs                           # trimmed from SecretsCanaryWorkflowHost
├── ImportedReusableRunsTests.cs                         # US1 (output, outcome routing, wrapper, flowchart body)
├── ImportPublishOrderTests.cs                           # US2
├── ImportPublishResumeTests.cs                          # US3
├── ImportWarningsTests.cs                               # US4
├── ImportPublishPermissionTests.cs                      # US5
└── PublisherBridgeTests.cs                              # tenant mismatch, refusal mapping, permission constant

Essentials/tests/Workflows/Publishing/Api/Tests/ActivityDefinitionPublisherRequestedVersionTests.cs   # new
Essentials/tests/Architecture/Tests/EndpointSecurityTests.cs                                        # apply permissions
```

**Structure Decision**:
- Orchestration (order, states, drafts) lives in Imp, the import's domain.
- The bridge only adapts one publish.
- Conversion order and the contract builder live in Mapping, which owns conversion.
- The test projects follow the existing Extensions layout.

## Implementation order (for /speckit-tasks)

1. **Publisher: requested id** (FR-006, SC-006). Request field, fingerprint, validation, tests (used; absent
   unchanged; blank; in use → conflict; replay with a different id → idempotency conflict). Publishing tests stay
   green.
2. **Foundations in Imp and Mapping.**
   - Codes.
   - `ReusableActivityPublicationState`.
   - The port and its records.
   - `Elsa3ImportedActivityContract` (moved from the test helper, with tests).
   - `Elsa3ReusableImportOrder` (Mapping) with tests (chain, diamond, versions of one definition, tie-break).
3. **Conversion order and outcomes** (FR-018, D-5). The materializer converts topologically and reads `OutcomeNames`
   from the converted target. `CompletionOutcomes` is removed, and the 239 outcome tests are adapted.
4. **Compile proof** (FR-015, SC-003). `CatalogResolvedDependencies`, `GraphProviderProof.CompileAsync`, assertions
   in the three test classes. MVP check: every validated reusable fixture compiles.
5. **Analyzer D-6** (FR-019). Refusal, new test, `ValidCollection()` adjusted.
6. **Commit change** (FR-004, R-4). No version rows. A new definition gets authoring with a null head plus its first
   draft through `CreateActivityDefinitionRequest` in the shared transaction. Preflight rules trimmed. EF import tests
   updated; reapply over an existing compatible definition writes nothing.
7. **Warnings** (FR-012, R-7). Codec, replay, 1.0.0 row compatibility test.
8. **Publish phase** (FR-001, FR-005, FR-007 to FR-011, R-5). Find or create the draft, the per-version decision,
   deterministic idempotency keys, run after commit and on replay, `Publications` on the receipt, status read live.
   `PUBLISHING-UNAVAILABLE` check before commit.
9. **Bridge project** (R-1). csproj, feature, implementation, README, `EXTENSION_POINTS.md`, slnx.
10. **Permissions** (FR-014, R-8). `RequireAllPermissionsAttribute`, apply endpoint, `EndpointSecurityTests`.
11. **Integration test project** (FR-016, SC-001 to SC-005, R-13). Host first, then US1, US2, US3, US4, US5 and the
    bridge tests.
12. **Docs and maps.** Mapping README, `EXTENSION_POINTS.md` files, `docs/maps` (`-- all`, explicit staging). Run
    the quickstart and record the results on the PR.

Steps 1, 2 to 5 and 7 are independent of each other. Step 6 needs 2. Step 8 needs 1, 2, 6 and 9's port shape. Step 11
needs everything before it.

**Coordination with #2717 (spec 247)**: the shared files are listed in R-15. Whichever unit merges second rebases
onto `main` and resolves them; `main` is never merged in.

## Spec amendments

Agreed by the maintainer on 2026-10-19 and applied to spec.md:

1. **FR-010** (R-6): "The apply result MUST report, for each reusable version, whether it is published, and for one
   that is not, the publisher's error code and diagnostics, or the dependency that blocked it. A status read MUST
   report whether each version is published, read live." The stored receipt carries only warnings.
2. **Edge case "higher published version"** (R-3): replace it with "A selected version lower than a version of that
   definition already published is reported as not published with the publisher's
   `activity.version.bump-insufficient`." Faithful or refuse.
3. **Assumptions** (R-1): a host that applies a reusable import composes `Elsa3ImportActivitiesPublishing`. Without
   it, the apply refuses with `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE`.
4. **FR-018** (R-9): the import satisfies it by a single source, so no mismatch code is added.
5. **FR-016** (R-13): the run-time tests live in a new integration test project.

## Complexity Tracking

None.
