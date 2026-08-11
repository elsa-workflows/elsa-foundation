# Implementation Plan: File-based workflow deployment at startup

**Branch**: `780-file-workflow-deployment` | **Date**: 2026-08-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/147-file-workflow-deployment/spec.md` · Issue: [elsa-workflows/elsa-foundation#1157](https://github.com/elsa-workflows/elsa-foundation/issues/1157)

## Summary

Complete the file-based (GitOps) workflow deployment story: package `JsonWorkflowReconciliation` into `Elsa.Workbench` (it exists but is uncatalogued and silently skipped), add a `FolderPath` option (exactly-one-of-three validation, deterministic non-recursive ordinal scan), and add opt-in `PublishOnReconcile`. The publish step is a Publishing-engine event subscriber (`PublishReconciledWorkflowVersions`) on a **new** Sequential completion event `WorkflowVersionsReconciled` (none exists today) whose payload carries per-source provenance claims — provenance is not persisted and survives only through the event pipeline. Sequential delivery inside the reconciler's `[SingleNodeTask]` startup task yields single-node execution and publish-before-`/health/ready` for free; the subscriber never throws (per-definition failure isolation) and pre-checks the publication slot for restart idempotency, backed by `PublishWorkflow`'s built-in `WasCreated=false` replay short-circuit. Full decisions in [research.md](research.md); shapes in [data-model.md](data-model.md); contracts in [contracts/](contracts/).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, repo-wide via `Directory.Build.props`)

**Primary Dependencies**: CShells (shell/feature activation), Elsa.Events.Core (`IEvent`/`IEventHandler<T>`/`IInlineEventPublisher`), Elsa.Mediator.Core (`IRequestSender` → `PublishWorkflow`), Elsa.Tasks.Core (`IStartupTask`, `[SingleNodeTask]`, `[Order]`), Elsa.Locking.Core, Elsa.Serialization.Core (`IPayloadSerializer`)

**Storage**: none new — existing design catalog stores (`IWorkflowDefinition[Version]Store`) and publishing authority stores (`IPublicationSlotStore`/`IPublicationRecordStore`); definition files are read-only input

**Testing**: xunit only (no FluentAssertions/Moq — hand-written stubs, house rule); existing patterns in `tests/Elsa/Workflows/Design/Tests/Unit/Reconciliation/`; architecture guards in `tests/Elsa/Architecture/`; PowerShell e2e in `e2e-tests/`

**Target Platform**: Linux/Windows server host (`Elsa.Workbench`), docker image

**Project Type**: modular server framework — feature packages + composition-root host

**Performance Goals**: no regression to time-to-ready beyond the file scan + per-definition publish (publish already runs a compile per definition; slot pre-check avoids recompiles on unchanged restarts)

**Constraints**: Design↔Publishing seam (§E2.2/E2.6 — Publishing→Design contracts allowed, reverse forbidden); MINOR-only package changes (§4.2); existing tests pass unmodified (§2.21.1); `Elsa.Workflows.Publishing.Persistence.Groundwork.csproj` reference list is pinned by exact equality — do not touch

**Scale/Scope**: ~6 modified projects, 2 new event/model types, 1 new subscriber, 1 new e2e suite, docs + maps regeneration; no persisted schema change

## Constitution Check

*GATE: evaluated pre-Phase-0 and re-evaluated post-Phase-1 — PASS (no violations; two notes recorded).*

| Gate | Status | How this plan satisfies it |
|---|---|---|
| §E2.2 / §E2.6 seam | ✅ | Publish trigger is a Publishing-side subscriber; new reference `Elsa.Workflows.Publishing → Elsa.Workflows.Design.Reconciliation.Core` matches the allowed direction (engine already references the Activities equivalent). No Runtime edges change; guard tests stay green. |
| §E2.8 / §E2.9.5 Model X | ✅ | Reconciliation policy untouched: existence-check idempotency, mismatch tripwire, duplicate policy all unchanged; no per-pass mutating fields added; provenance travels in events, not entities. |
| §2.5 / §2.23.3 feature shape | ✅ | `JsonWorkflowReconciliationFeature` stays public non-sealed with `override ConfigureServices`; `WorkflowsPublishingFeature` stays public non-sealed `virtual`; new collaborators (`PublishReconciledWorkflowVersions`) are `public sealed`, registered and injected via contracts. |
| §2.6.1 / §2.6.6 events | ✅ | New event = independent-subscriber notification (sanctioned); **Sequential** because the subscriber is business-critical (publish-before-ready) — Background is constitutionally forbidden for it; subscriber-must-not-throw is documented as the event's failure policy. Fan-in payload rule respected on the extended `WorkflowVersionsReconciling` (get-only `ICollection`). |
| §2.11 DependsOn | ✅ | No new feature ids; existing `DependsOn` chains unchanged. `PublishOnReconcile` without `WorkflowsPublishing` in the shell = no subscriber = import-only (documented in contracts/shells-configuration.md). |
| §2.21.1 / §2.23.4 golden rule | ✅ | All existing Json/reconciliation/publishing tests pass with unmodified assertions; additive changes only (count-validation preserves every existing accept/reject case). |
| §2.22 / §2.22.1 / §2.22.2 docs | ✅ | Same-unit updates: Reconciliation `EXTENSION_POINTS.md` (new event + `RequestsPublication` + claims), Publishing `README.md` Cross-domain contributions section, new `Json/README.md`; no new catalog file ⇒ root index unchanged; `CatalogParityTests` enforces the event entry. |
| §2.23.1 / §2.23.2 tests | ✅ | Registration tests extended (Json feature both-option-states; Publishing feature resolves the new handler); branch-covered implementation tests for folder scan, claims population, and the subscriber (skip/deleted/missing-row/pre-check/publish/failure-isolation branches). |
| §2.23.5 exception boundaries | ✅ | Folder-scan `IOException`/`UnauthorizedAccessException`/`JsonException` wrap into the existing domain exception `InvalidWorkflowCatalogJsonException` (same boundary the reader already uses). |
| §2.24 sanctioned patterns | ✅ | Event subscriber (pattern 3) + delivery strategy (4) + existing Source contract (3b). **Note 1**: no new startup task is introduced (the non-catalogued-pattern concern from research is avoided by design — publish runs inside the existing sanctioned reconcile task). |
| §4.2 versioning | ✅ | All MINOR: additive options defaulting to current behaviour, additive default interface member, additive event, additive registration. No wire identifiers change (**Note 2**: `SourceKind` `"Json"`/`"git"` casing inconsistency is pre-existing and rename-exempt; left alone). |
| §E6 naming | ✅ | `WorkflowVersionsReconciled` (event pair grammar), `WorkflowVersionSourceClaim` (concrete noun), `PublishReconciledWorkflowVersions` (verb-named handler per Publishing's own style), `FolderPath`/`PublishOnReconcile` (option nouns); ≤4 components each. |
| §2.16.1 project size | ✅ | No new projects — subscriber lives in the existing Publishing engine; event/claim in existing `Reconciliation.Core`. |

## Project Structure

### Documentation (this feature)

```text
specs/147-file-workflow-deployment/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0 — decisions D1–D10
├── data-model.md        # Phase 1 — options/event/subscriber shapes
├── quickstart.md        # Phase 1 — validation walkthrough (source + docker)
├── contracts/
│   ├── shells-configuration.md
│   ├── definition-file-format.md
│   └── workflow-versions-reconciled.md
├── checklists/requirements.md
└── tasks.md             # Phase 2 — /speckit-tasks output (not created by plan)
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Design/Reconciliation/
├── Core/
│   ├── WorkflowVersionsReconciling.cs        # + Claims collection (additive)
│   ├── WorkflowVersionsReconciled.cs         # NEW event
│   └── WorkflowVersionSourceClaim.cs           # NEW payload record
├── Contracts/IWorkflowReconciliationSource.cs  # + RequestsPublication default member
├── Handlers/WorkflowVersionsReconcilingHandler.cs  # populate Claims per source entry
├── Services/WorkflowsVersionReconciler.cs      # publish Reconciled event on pass success
├── EXTENSION_POINTS.md                         # + event entry, + source-member docs
└── Json/
    ├── JsonWorkflowReconciliationFeature.cs    # count-based exactly-one validation
    ├── Options/JsonWorkflowReconciliationOptions.cs  # + FolderPath, + PublishOnReconcile
    ├── Services/JsonWorkflowReconciliationSource.cs  # folder scan, RequestsPublication, defId warning
    └── README.md                               # NEW feature doc (authoring guidance)

src/Elsa/Workflows/Publishing/
├── Elsa.Workflows.Publishing.csproj            # + ProjectReference Reconciliation.Core
├── WorkflowsPublishingFeature.cs               # + AddEventHandler<WorkflowVersionsReconciled, …>
├── Handlers/PublishReconciledWorkflowVersions.cs   # NEW subscriber
├── README.md                                   # + Cross-domain contributions section
└── EXTENSION_POINTS.md                         # + cross-domain seam note

src/Apps/Elsa.Workbench/
├── Elsa.Workbench.csproj                       # + 2 ProjectReferences (with why-comment)
└── Program.cs                                  # + usings + 2 typeof(...).Assembly lines

docs/
├── docker-hub-quickstart.md                    # + file-based deployment section
├── docker.md                                   # + mounts-table row
└── maps/*                                      # regenerated

tests/Elsa/Workflows/Design/Tests/Unit/Reconciliation/
├── Json/JsonWorkflowReconciliationFeatureTests.cs      # extended validation matrix
├── Json/JsonWorkflowReconciliationSourceTests.cs       # folder scan branches (temp-dir IO pattern)
├── WorkflowVersionsReconcilingHandlerTests.cs          # claims population branches
└── WorkflowsVersionReconcilerTests.cs                  # Reconciled published on success, not on failure

tests/Elsa/Workflows/Publishing/Tests/                  # (project per repo mirror convention)
├── PublishReconciledWorkflowVersionsTests.cs           # NEW — subscriber branch coverage
└── (feature registration test extended)

tests/Elsa/Architecture/
└── ArchitectureGuardTests.cs                   # NEW Server_catalogs_workflow_reconciliation… fact

e2e-tests/file-deployment/                      # NEW suite (durability-style lifecycle)
├── README.md
├── _FileDeploymentCommon.ps1
└── Test-FileBasedDeployment.ps1
```

**Structure Decision**: no new projects. The event + claim land in the existing `Elsa.Workflows.Design.Reconciliation.Core` (contracts project already referenced by both sides); the subscriber lands in the existing `Elsa.Workflows.Publishing` engine (it is publishing logic, activated with the engine — a separate sub-package would be a premature split per the §2.16.1 guidance and the provider-module memory rule). The host is the composition root and takes the two new references (§2.1).

## Complexity Tracking

No constitution violations to justify. Two recorded notes (also in the Constitution Check): (1) no new startup task — deliberately avoids the uncatalogued-pattern gate; (2) `SourceKind` casing inconsistency is pre-existing, wire-value-exempt, untouched. Pre-existing hazards documented for follow-up, not fixed here (research D10): Json+Git double-registration of the reconcile startup task; single-instance-per-shell limit for the Json feature; silent skip of unknown feature names in the runtime catalog.
