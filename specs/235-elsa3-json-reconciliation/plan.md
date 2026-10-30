# Implementation Plan: Elsa3JsonReconciliation — import Elsa 3 workflow JSON from a folder at engine start

**Branch**: `235-elsa3-json-reconciliation` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/235-elsa3-json-reconciliation/spec.md`

## Summary

A new shell feature, `Elsa3JsonReconciliation`, adds one startup task. On every start, after the reconciliation
passes, it reads the configured Elsa 3 JSON files and, in each persistence scope, runs the existing collection import
in-process (R-1):

- **Select.** Leave out duplicate or blank source ids and items with blocking analysis diagnostics (a document with
  no root among them), with their dependents (R-5).
- **Apply.** Apply the selection without an access scope, with the scope's tenant and a per-start operation key
  (R-4). When the apply refuses items, leave them and their dependents out and apply again. The import publishes the
  reusables, dependencies first (spec 246).
- **Publish workflows.** Publish the newest version in the input of each workflow and wrapper with `PublishWorkflow`,
  unless its slot already published it once or a reusable it uses is unpublished (R-6).
- **Report.** Log one warning per skipped item and one summary line per scope; strict mode fails the start.

The import gains two additive request members (the tenant to stamp and the publish operation key, for an unscoped
apply) and two additive result
members (the publications and the committed workflow versions); data-model.md. A scoped startup grant lets the activity
publisher accept the persistence scope's tenant while the task publishes, and nowhere else (R-3).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: none added. Existing: CShells, the Elsa 3 import and mapping, Workflows Publishing, Tasks,
Mediator.

**Storage**: no schema change; writes go through the import's EF commit and the publishers.

**Testing**:
- xunit only (no FluentAssertions).
- Feature, options and selection logic: unit tests in `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`
  (the project already holds the import's unit-level tests next to its host tests).
- End to end: hosts in the same project that compose SQLite persistence, publishing and the runtime, as spec 246's
  tests do.
- Feature composition: `FeatureCompositionTests` picks the new feature up.

**Target Platform**: Elsa 4 server hosts

**Project Type**: one new library project in the Elsa 3 extension, small additive change to the import

**Performance Goals**: the startup cost is conversion of the input plus one commit; a restart with unchanged input
converts and finds nothing to write. No target beyond "the JTest corpus (36 workflows) starts in seconds".

**Constraints**:
- Faithful or refuse: the feature adds no conversion rule.
- The collection import's observable behaviour is unchanged (FR-010).
- Build and test only the touched projects, through the build-slot wrapper (shared machine).

**Scale/Scope**:
- 1 new project with about 8 types (feature, options, exception, file reader, startup task, selection, workflow
  publisher, startup publishing grant).
- 2 edited import files (apply result, importer).
- Fixtures: 4 Elsa 3 definitions plus 2 for refusals.

## Constitution Check

*Loaded: `.specify/memory/constitution.md` (v6.3.0), `.specify/memory/constitution-framework.md` (v6.1.0) and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | The feature is part of the import adapter (`Elsa3.Activities.Design.Import.*`). It is one-way: Elsa 3 definitions become Elsa 4 entities that run natively; instance state is never imported. "One-time" holds per Elsa 3 version: each is imported once, and a later start finds it and writes nothing (R-9). No Runtime project references Elsa 3. |
| §E2.2 Runtime takes no Design reference | PASS | No Runtime project changes. |
| §E2.8 / Model X (provisional) | PASS | The feature writes through the import's commit, not the catalog reconcilers, and adds no per-pass mutating field. Versions are never deleted. |
| §E2.6.1 executable-always-runs | PASS | Imported workflows publish through the normal publisher; a reusable that fails to publish keeps its callers unpublished rather than publishing a broken artifact (FR-009). |
| §2.1 `.Core` purity / heavy packages; implementation references | PASS, one recorded deviation | No `.Core` project changes. The new project references `Elsa.Workflows.Publishing.Api` (an implementation project) for the authorization decorator, as its sibling adapter does; recorded for the architects as A-9. |
| §2.2 paths follow the domain tree | PASS | `Extensions/Elsa3/src/Activities/Design/Import/Reconciliation/`, next to `Publishing/` and `Persistence/`. |
| §2.5, §2.23.1 feature composes, services resolve | ACTION | A registration test for the feature; `FeatureCompositionTests` covers it. |
| §2.6.2 replacement contracts | ACTION | One decoration of an existing single-implementation contract: `Elsa3StartupPublishingAuthorization` decorates `IActivityPublishingAuthorizationContext` (R-3), reading the scoped `Elsa3StartupPublishingGrant`. A replacement registered after this feature removes the decorator, and startup reusable publications are then refused and reported as skips (A-9). It delegates to the inner context unless the startup task set the scoped grant in its own operation scope. Registration test: the inner context is kept and still answers outside the grant. Reviewers: this touches publishing authorization, so it needs an architect's look. |
| §2.17 duplication beats dependency | PASS | The slot check (`HasActivePublicationOf`, `ResolveTargetSlotName`, about thirty lines) is copied from `PublishReconciledWorkflowVersions` (R-6). The options shape mirrors `JsonWorkflowReconciliationOptions` without referencing it. |
| §2.22 / §2.22.1 docs and extension points | ACTION | README and `EXTENSION_POINTS.md` for the new project (it consumes the import's contracts and adds a startup task); the Elsa 3 Mapping README and `docs/reference/elsa3-migration-readiness.md` gain the folder entry point. |
| §2.23.3 visibility | PASS | Public: the feature class (not sealed), `Elsa3JsonReconciliationOptions` and its file option (the feature's `Options` property exposes them), `Elsa3ReconciliationFailedException`. Everything else internal. Additive public members on `ReusableActivityImportApplyRequest` and `ReusableActivityImportApplyResult`. |
| §2.23.5 exception boundaries | PASS | Configuration errors at registration; file errors as a domain exception naming the path; strict mode and unattributed refusals as `Elsa3ReconciliationFailedException`. Per-item failures are diagnostics, not exceptions. |
| §E6 naming | PASS | Each new type name has at most four components: `Elsa3JsonReconciliationFeature`, `Elsa3JsonReconciliationOptions`, `Elsa3FolderImportTask`, `Elsa3JsonFileReader` (reads; not a `…Source`, which R4 reserves for return-on-request contracts), `Elsa3ImportSelection`, `ImportedWorkflowPublisher`, `Elsa3StartupPublishingGrant`. The feature id keeps "Reconciliation" for continuity; the types say "Import", which is what they do (R-9). |
| §E7 CI tiers | PASS | No new test project. |
| Epic #3052 working agreement | PASS | This spec is defined (spec, plan, tasks) before implementation; it depends on no open D/A/S decision. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | Import engine at start | R-1. No reconciliation source, no core change. |
| D-2 | `[Order(4)]` startup task | R-2. After both reconciliation passes and the Git export. |
| D-3 | One run per persistence scope, with a startup grant | R-3. `IPersistenceScopeRunner`; every scoped service from the operation scope; the scope's tenant stamped on the unscoped apply; `Elsa3StartupPublishingGrant` lets the activity publisher accept that tenant inside that scope only. |
| D-4 | Unscoped apply | R-4. No receipt; the commit is a no-op on unchanged input; a per-start operation key so a refused reusable is retried on the next start. |
| D-5 | Narrowing selection | R-5. Input, analysis, then apply refusals located by `SourceVersionId` metadata; dependents removed transitively; bounded by the item count. Credential and identity-collision refusals are not attributable and fail the start. |
| D-6 | Workflow publishing | R-6. Newest version in the input per definition, published at most once: skip when its slot's journal already holds it (any status but Failed), or when a reusable is unpublished. |
| D-7 | New project | R-7. `Elsa3.Activities.Design.Import.Reconciliation`; tests in the existing integration test project. |
| D-8 | Versions `{n}.0.0` | R-8. Converter revision deferred. |

## Amendments during implementation (2026-10-30, US1)

| # | Change | Why |
|---|---|---|
| A-1 | US1 already holds part of US3: the input rule (duplicate and blank ids), the analysis rule with dependents, the apply loop and the blocking of callers whose reusable was not published (T020–T022 in part). | One unconvertible workflow must not stop the demo folder from importing; US3 still adds its tests and strict-mode tests. |
| A-2 | Fixtures are copied from `Fixtures/` into the test's folder at start instead of duplicated under `Fixtures/Reconciliation/`. There is no separate plain fixture: `caller` and `http-hello` are plain workflows. | No duplicate fixture files. |
| A-3 | `http-endpoint.json` sets the WriteHttpResponse content type. An imported WriteHttpResponse without one faults at run time with `VF-ACT-006: Required result projection 'ContentType' was null`, although the converter README says an unset content type is inferred as Elsa 3 did. | A converter or runtime defect outside this spec; reported separately. |
| A-4 | *(Superseded by A-8.)* The project also references `Elsa.Workflows.Publishing`. | `PublicationActivationException`, which the publisher catches by code for a concurrent publication (R-6), lives there. |
| A-5 | The US3 refusal fixtures are generated in the test from the existing fixtures (a reusable with `Elsa.StateMachine`, its caller, a document with no root) instead of files under `Fixtures/Reconciliation/Refusals/`; `reusable-sets-variable.json` is used as it is. | The change from the base fixture stays visible next to the assertion. |
| A-6 | A plan error fails the start only when it names no plan item. The plan's diagnostics repeat every item's errors, and those are skipped per item. | Found by the US3 test: a document with no root is an item error (`ELS3-REUSABLE-INVALID-SOURCE`), not a plan-level one. |
| A-7 | **Review round 1.** A failed workflow publication is a skip with `ELS3-RECONCILE-PUBLISH-FAILED`, so strict mode fails on it. The summary line reports what is in place and what this start published. An unattributed validation refusal carries every diagnostic's code and guidance. A null entry in an array fails the read, naming the file. | FR-014, FR-015; a refused reusable's dependents are reported as "not published", not "not imported". |
| A-8 | **No reference to `Elsa.Workflows.Publishing`** (framework §2.1). When the slot already serves the artifact, `PublishWorkflow` succeeds with the existing publication (`WasCreated` false), which counts as already published, not as published by this start. A concurrent node's publication is recognised by reading the slot journal again after any failed `PublishWorkflow`, not by the exception's type; a failure of that second read is a `Failed` outcome, never an escape. | Replaces A-4. One mechanism covers every refusal shape, and the implementation reference goes. |
| A-9 | **Kept, for the architects:** the decorator lives in this project, which references `Elsa.Workflows.Publishing.Api` as the sibling `Import.Publishing` adapter does; and the default-slot resolution is copied from `PublishReconciledWorkflowVersions`. Both are listed under "Cross-domain contributions" in the README. Moving the contract to `Publishing.Core`, or adding a shared "default target slot" query there, changes another domain and is a follow-up. | Standards review findings 2–4; decided against a restructure in this unit. |
| A-10 | Reader and selection are scoped, not singleton (framework §2.5.1). The project references `Elsa3.Activities.Design.Import.Publishing`, the package its `DependsOn` names. Type names: `Elsa3JsonFileOption`, `WorkflowPublicationStatus`. | Standards review findings 5, 7–9. |

## Project Structure

### Documentation (this feature)

```text
specs/235-elsa3-json-reconciliation/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/feature-configuration.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
Extensions/Elsa3/src/Activities/Design/Import/
├── Elsa3.Activities.Design.Import/
│   ├── Models/ReusableActivityImportPlan.cs              # ApplyRequest: TenantId, OperationKey; ApplyResult: Publications, Workflows (additive)
│   └── Services/ReusableActivityCollectionImporter.cs    # unscoped: stamp TenantId, pass OperationKey to the phase; fill the result members
└── Reconciliation/                                       # new project Elsa3.Activities.Design.Import.Reconciliation
    ├── Elsa3.Activities.Design.Import.Reconciliation.csproj
    ├── Elsa3JsonReconciliationFeature.cs                 # feature, option validation, registrations
    ├── Options/Elsa3JsonReconciliationOptions.cs
    ├── Exceptions/Elsa3ReconciliationFailedException.cs  # strict mode; invalid input file
    ├── Services/Elsa3JsonFileReader.cs                   # files → input items
    ├── Services/Elsa3ImportSelection.cs                  # D-5 narrowing, skipped items
    ├── Services/ImportedWorkflowPublisher.cs             # D-6, A-8
    ├── Services/Elsa3FolderImportTask.cs                 # D-2, D-3, D-5 apply loop, logging
    ├── Services/Elsa3StartupPublishingGrant.cs           # D-3 decorator and scoped grant holder
    ├── Models/Elsa3ReconciliationDiagnosticCodes.cs      # the two new codes
    ├── README.md
    └── EXTENSION_POINTS.md
Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/
├── Elsa3JsonReconciliationTests.cs                       # US1 host tests
├── Elsa3JsonReconciliationRestartTests.cs                # US2 host tests
├── Elsa3JsonReconciliationRefusalTests.cs                # US3 host tests
├── Elsa3ImportSelectionTests.cs                          # D-5 unit tests
├── Elsa3JsonFileReaderTests.cs                           # reader unit tests
├── Elsa3StartupPublishingGrantTests.cs                   # grant and decorator
├── Elsa3JsonReconciliationFeatureTests.cs                # registration, options
└── Fixtures/http-endpoint.json                           # the only new fixture (A-2, A-5)
Extensions/Elsa3/src/Mapping/README.md                    # folder entry point
docs/reference/elsa3-migration-readiness.md               # folder entry point
Elsa.Server.slnx                                          # add the project
```

**Structure Decision**: one new project beside the import's Publishing and Persistence projects; no new test project.

## Delivery order

1. **Foundation**: the apply result members; the project, feature, options and file reader.
2. **US1**: selection without refusals, apply, workflow publishing, logging; host test on the fixture folder.
3. **US2**: restart tests (unchanged input, new version, resume publication).
4. **US3**: refusal narrowing, dependents, strict mode.
5. **Docs**: README, extension points, Mapping README, migration readiness guide, spec status.

## Complexity Tracking

| Item | Why needed | Simpler alternative rejected because |
|---|---|---|
| Re-applying after a refused apply (D-5) | The apply is all or nothing; conversion and credential failures are found only inside it | A dry-run materialize would need a second, public engine path in the import |
| Copied slot resolution (D-6) | FR-011: a restart writes nothing; FR-008: never take back an operator's slot | Sharing it means making a Publishing handler's internals public for one caller |
| Startup publishing grant (D-3) | The activity publisher authorizes by HTTP tenant claim only; at startup there is none | Widening the HTTP context for every non-request caller changes publishing authorization host-wide |
