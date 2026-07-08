# Implementation Plan: Workflow-Definition GitOps — Git Reconciliation Source + Export Sink

**Branch**: `085-workflow-definition-gitops` | **Date**: 2026-07-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/085-workflow-definition-gitops/spec.md` +
[ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md) (D1–D11).

## Summary

Add a concrete git-backed reconciliation source + a Writer-only export reconciler that layer GitOps
over the existing operational catalog — git as content authority per immutable version, the catalog as
retention authority, no merge ever (D1). Deliver in one new leaf project
(`Elsa.Workflows.Design.Reconciliation.Git`) plus small additive changes to the shared reconciler seam:
a `GitWorkflowReconciliationSource`, an `IGitWorkflowExporter` set-diff sweep, an `IGitWorkspace` with
role-driven clone modes (Writer = persistent ff-only working copy; Consumer = disposable
`reset --hard` mirror), and a `WorkflowsDesignGitReconciliationFeature : WorkflowsDesignReconciliationFeature`.
Fold in the **FR-008a correction** (gate definition-metadata apply to the newest version) and the
additive `ContentHash` + soft-delete threading. Content identity is the deterministic payload
serialization (spec 086, merged); the on-disk file is its indented form. Full design rationale in
[research.md](research.md); model in [data-model.md](data-model.md); surface in
[contracts/contracts.md](contracts/contracts.md).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`).

**Primary Dependencies**: `Elsa.Git` (`IGitClient`, leaf), `Elsa.Workflows.Design.Reconciliation(.Core)`,
`Elsa.Workflows.Design.Core`, `Elsa.Workflows.Design.Persistence.Core`, `Elsa.Serialization.Core`
(`IPayloadSerializer`, deterministic per #549/ADR 0035), `Elsa.Tasks.Core`, `Elsa.Locking.Core`,
`CShells.Abstractions`, `System.Text.Json` (`JsonNode` canonicalizer).

**Storage**: No new persistent store. Reads/writes JSON files in a local git working clone; upserts
through the existing catalog commands. Git is never an `IWorkflowDefinitionStore` (FR-015).

**Testing**: xUnit. Extend `Elsa.Workflows.Design.Tests` (reconciler FR-008a/R10/R13); new git test
project for source/exporter/canonicalizer/feature-registration using temp local repos.

**Target Platform**: Server host (`Elsa.Server`), `[ManifestRuntimeKind(Server)]`.

**Project Type**: Single backend/library feature (modular monolith, CShells feature).

**Performance Goals**: Off the hot path — runs at startup under a single-node lock. Cost bounded by
`--single-branch` fetch + O(versions) file writes on export.

**Constraints**: Dependency-envelope clean (no Design→app/runtime dep, SC-006); single-writer; git off
the runtime read path; no interactive git prompt (`GIT_TERMINAL_PROMPT=0`); unreleased → no back-compat.

**Scale/Scope**: v1 single-writer, single/default tenant. ~1 new project, ~12 new types, ~4 additive
edits to shared seams, ~9 test obligations.

## Constitution Check

*GATE: re-checked after Phase 1 design — PASS.*

| Gate | Assessment |
|---|---|
| **§E2.2 Design↔Runtime split** | Feature is Design-only; references Design + `Elsa.Git` (leaf) + serialization/tasks/locking cores. **No** Design→Runtime or Design→app dep. Arch-guard dependency-envelope stays green (SC-006). ✅ |
| **§E2.6 artifact-only runtime** | Untouched — git never read at execution; catalog remains runtime read path. ✅ |
| **§E2.8 / §E2.9.5 Model X** | Import obeys `(id,version)` lookup → create-if-absent / skip-or-throw; mismatch surfaced (R13). Versions never deleted. ✅ |
| **§E2.9 State scope** | `versions/*.json` carries only `WorkflowDefinitionState` (authored content). Soft-delete is a definition-level `DeletedAt` (lifecycle metadata, peer of Name/Description) — **not** added to State. ✅ |
| **§E6 naming (R4)** | `…Source` = pull (`GitWorkflowReconciliationSource`); `…Exporter`/`Export…` = does work; `IGitWorkspace` concrete noun; `Default`-free feature name follows `[ShellFeature]` precedent. "Reconciler" reused as the established domain suffix (`WorkflowsVersionReconciler`). ✅ |
| **§2.16.1 min project size** | New git project fits the "independently-composable `[ShellFeature]` unit / source-variant seam" exemption class (peer of `Elsa.Activities.Design.Reconciliation.Clr`). ✅ |
| **Golden-rule refactoring (§E1/§2.21.1)** | FR-008a relocation + reconciler additions preserve existing reconciler tests (the newest-version rename case stays green); only additive behavior + one new older-entry test. ✅ |

**One flagged composition change (not a violation):** registering the import startup task in the base
feature flips the reconcile lifecycle from dormant→active when *any* concrete reconciliation feature is
enabled (R3). Intended per ADR Consequence 4; validated at the QA gate. No Complexity-Tracking entry
required.

## Project Structure

### Documentation (this feature)
```text
specs/085-workflow-definition-gitops/
├── plan.md              # this file
├── research.md          # Phase 0 — decisions R1–R14
├── data-model.md        # Phase 1 — model + config + layout
├── contracts/
│   └── contracts.md     # Phase 1 — C# surface + test obligations
├── quickstart.md        # Phase 1 — operator config + verify
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)
```text
src/Elsa/Workflows/Design/Reconciliation/
├── WorkflowsDesignReconciliationFeature.cs        # EDIT: register import IStartupTask (R3)
├── Models/WorkflowVersionReconciliationModel.cs   # EDIT: + ContentHash, + Deleted (R10/FR-007)
├── Services/WorkflowsVersionReconciler.cs         # EDIT: FR-008a gate (R9), DeletedAt diff (R10), hash tripwire (R13)
├── Handlers/WorkflowVersionsReconcilingHandler.cs # EDIT: pass entry.Deleted to factory
└── Git/                                            # NEW project Elsa.Workflows.Design.Reconciliation.Git
    ├── Elsa.Workflows.Design.Reconciliation.Git.csproj
    ├── WorkflowsDesignGitReconciliationFeature.cs
    ├── Options/{GitReconciliationOptions,GitExportOptions}.cs
    ├── Options/{GitReconciliationRole,GitCredentialsMode,GitPushMode}.cs
    ├── Contracts/{IGitWorkspace,IGitWorkflowExporter}.cs
    ├── Services/{GitWorkspace,GitWorkflowExporter,GitWorkflowReconciliationSource}.cs
    ├── Services/GitCanonicalJson.cs
    └── Startup/GitWorkflowExportStartupTask.cs

src/Elsa/Workflows/Design/Core/Contracts/
├── IWorkflowDefinition.cs                          # EDIT: + DateTimeOffset? DeletedAt
└── IWorkflowDefinitionFactory.cs                   # EDIT: + bool deleted param
# + the IWorkflowDefinition read-model impl + WorkflowDefinition.From (honor DeletedAt)

tests/Elsa/Workflows/Design/
├── Tests/Unit/Reconciliation/WorkflowsVersionReconcilerTests.cs  # EDIT: + FR-008a/R10/R13 cases
└── Reconciliation/Git/Tests/...                    # NEW: source, exporter, canonicalizer, feature-reg
```

**Structure Decision.** Nest the git source-variant under the workflow reconciliation domain root
(`.../Reconciliation/Git/`), exactly mirroring `Elsa.Activities.Design.Reconciliation.Clr`, and add the
project's `Compile Remove="Git/**"` glob to the base project so the nested folder compiles as its own
assembly. Shared-seam edits stay minimal and additive.

## Phases

- **Phase A — Reconciler seam (no git):** FR-008a relocation (R9) + additive `ContentHash`/`Deleted` on
  the model + `DeletedAt` on the facade/factory + `DeletedAt` diff (R10) + hash tripwire (R13) + base
  feature registers the import startup task (R3). Land with reconciler unit tests (incl. the older-entry
  FR-008a test). Independently valuable; unblocks US2.
- **Phase B — Inbound source (US2):** new project skeleton, `GitCanonicalJson`, `IGitWorkspace` +
  `GitWorkspace` (clone modes/credentials), `GitWorkflowReconciliationSource`, and the
  `WorkflowsDesignGitReconciliationFeature` wiring the source + Consumer path. Tests with temp repos.
- **Phase C — Export reconciler (US3):** `IGitWorkflowExporter` + `GitWorkflowExporter`,
  `GitWorkflowExportStartupTask`, Writer-only registration + push modes. Tests.
- **Phase D — Coherence (US4):** round-trip no-op tests, ff-only-refusal test, hash-tripwire assertion;
  QA gate (arch guard 49/49 + full Design/reconciliation suites).

## Complexity Tracking

> No constitution violations requiring justification. Table intentionally empty.
