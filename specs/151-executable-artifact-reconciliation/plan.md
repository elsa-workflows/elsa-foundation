# Implementation Plan: Executable Artifact Reconciliation

**Branch**: `1304-executable-artifact-reconciliation` | **Date**: 2026-08-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/151-executable-artifact-reconciliation/spec.md` (issue #1304 rev 4; all 7 clarifications resolved 2026-08-14)

## Summary

Close the "runtime-only engine has nothing to execute" gap by (a) exporting a portable, self-contained **closure envelope** (artifact + transitive child artifacts + published references + trigger bindings) from a publish-capable engine through a pluggable export-target seam whose v1 target is an API download, and (b) importing/reconciling such envelopes into a design-free runtime's executable store behind a two-axis requirements gate (consumer capabilities + storage drivers, and per-node CLR type presence) that rejects at import, never at first activation. Two reviewed extractions carry the design: the requirements checker moves from `Publishing.Api` to the Runtime layer (`IRuntimeRequirementChecker`), and the definition-keyed activation authority (`IPublicationSlotStore`/`PublicationSlot`) moves from `Publishing.Core` to `Runtime.Core` so publish and import share **one activation ledger per engine**, with a namespace-attributed cross-authority guard preventing silent double activation. Triggering reuses the existing startup-task + shell-reload model (#1303 deferred).

## Technical Context

**Language/Version**: C# / .NET (repo-pinned SDK; `Directory.Packages.props` central versions)

**Primary Dependencies**: CShells (`IShellFeature`/`[ShellFeature]`), Elsa.Tasks (`IStartupTask`, `[SingleNodeTask]`, `[TaskDependency]`), Elsa.Locking.Core, Elsa.Serialization.Core (`IPayloadSerializer`, `IWellKnownTypeRegistry`), Elsa.Api.FastEndpoints + Elsa.Api.Capabilities, Groundwork persistence (manifest sources + physicalizers)

**Storage**: contract-first — in-memory defaults via `AddWorkflowRuntime()`; durable via Groundwork with **one physical activation ledger**: a new slot document kind in the runtime store family, while the publishing-family slot store is deleted (no consumers yet → nothing to migrate; historical-schema baselines updated as a named task). **Zero EF Core** (program-goal ratchet enforced).

**Testing**: xunit 2.9.3 only (FluentAssertions constitutionally absent); §2.23.1 registration tests + §2.23.2 branch-covered unit tests; architecture-guard and composition assertions for SC-B-001/005

**Target Platform**: cross-platform server (CShells-hosted shells; workbench host for demo)

**Project Type**: modular framework libraries (2 new projects + 1 persistence unit; modifications in Runtime.Core/Runtime/Publishing family)

**Performance Goals**: reconcile pass is startup-path work — must complete before readiness; no serving-path changes (dispatch/stimulus routing untouched)

**Constraints**: assembly-enforced design-freedom (SC-B-005: no Design/Publishing assembly enters the runtime closure); behavior-preserving relocations (§2.21.1 golden rule — existing publishing tests pass with wiring-only changes); slot storage moves families deliberately (no consumers → no migration; schema baselines updated, never silently churned)

**Scale/Scope**: ~12 new contracts/services, 2 relocations, 1 endpoint + capability rel, ~6 EXTENSION_POINTS/maps/docs touchpoints; folder-scale artifact sets (tens–hundreds of closures per reconcile)

## Constitution Check

*Gates evaluated against Elsa constitution v4.0.0 + framework v4.0.0. Re-checked post-Phase-1: PASS (no Complexity Tracking entries).*

| Gate | Verdict | Evidence |
|---|---|---|
| §E2.2 no Runtime→Design/Publishing dependency | PASS | New runtime projects reference only Runtime/Tasks/Locking/Serialization/Persistence cores; both extractions point Publishing→Runtime (already-legal direction). SC-B-005 adds an assembly assertion test. |
| §E2.2.3 deployment shapes preserved | PASS | The feature *creates* the runtime-only shape's missing populate path; design-only and combined shapes covered by US5 + composition tests (SC-B-006). |
| §E2.6.1/.2 executable-always-runs, artifact-only runtime | PASS | Import gate rejects unexecutable artifacts at import (US2) — activation failures stay bugs, not features; importer reads/writes only runtime stores; no design data crossed. |
| §E2.8 Model X reconciliation policy (generalized) | PASS | Creation-time-only, no per-pass mutating fields, content-addressed idempotency, hash-mismatch → broken-source diagnostic (research D5). |
| §2.24 sanctioned patterns only | PASS | #1 three-layer (Reconciliation.Core/feature), #2 feature inheritance (abstract base + Json concrete, mirroring `WorkflowsDesignReconciliationFeature`), #3b source contract (`I…ReconciliationSource`), #5 replacement contract (slot store, checker), #8 bridge (publishing preflight wrapper), #9 strategy (export targets), #10 factory (closure factory). No new pattern introduced. |
| §2.20 no premature provider decomposition | PASS | JSON source stays in the base project (one source kind); blob/OCI later triggers the split (research D1). |
| §2.22.1 extension-point catalogs | PLANNED | New catalog for Reconciliation feature project + updates to Runtime, Publishing(+Api) catalogs + root index link (research D9). |
| §2.23.1/.2 unit tests, §2.23.3 visibility, §2.23.5 exception wrapping | PLANNED | Registration + branch-covered tests enumerated in D9; `public sealed` impls; `InvalidWorkflowArtifactClosureException` wraps all infra failures. |
| §E6 naming R1–R8 | PASS w/ 1 flag | Naming table research D8; single flag: `…Target` suffix is not R4-codified — pinned domain term (FR-B-010a), explicitly surfaced for reviewer judgment. |
| §E5 computed versioning | PASS | No `<Version>` elements; new csprojs follow Line B; no `-p:Version` anywhere. |
| §2.21.1 golden rule | PASS | Relocations are pure moves (no rename/behavior change); existing publishing + runtime tests must pass unchanged — treated as a hard task-phase gate. |

## Project Structure

### Documentation (this feature)

```text
specs/151-executable-artifact-reconciliation/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D9, citation re-verification, risks
├── data-model.md        # Phase 1 — envelope, relocated authority, checker result, minted rows
├── quickstart.md        # Phase 1 — export → runtime-only import → v2 rollout walkthrough
├── contracts/
│   ├── export-endpoint.md    # route, capability rel, responses (pinned for studio#493)
│   ├── runtime-contracts.md  # service contracts + feature surface
│   └── closure-envelope.md   # wire format, invariants, versioning
└── tasks.md             # Phase 2 (/speckit.tasks — not created by /speckit.plan)
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/
├── Core/                                  # MODIFIED: +WorkflowArtifactClosure, +IRuntimeRequirementChecker,
│                                          #   +RuntimeRequirementCheckResult, +relocated IPublicationSlotStore/
│                                          #   PublicationSlot/PublicationSlotTransitionResult
├── Elsa.Workflows.Runtime.csproj          # MODIFIED: <Compile Remove> glob gains Reconciliation sibling;
│   └── (impl project)                     #   +RuntimeRequirementChecker default, +in-memory slot store,
│                                          #   AddWorkflowRuntime() registrations
├── Reconciliation/
│   ├── Core/Elsa.Workflows.Runtime.Reconciliation.Core.csproj   # NEW: source contract, options, exceptions, result
│   └── Elsa.Workflows.Runtime.Reconciliation.csproj             # NEW: abstract+Json features, reconciler,
│                                                                #   startup task, import gate
src/Elsa/Workflows/Publishing/
├── Core/                                  # MODIFIED: -relocated slot types; +IWorkflowArtifactExportTarget,
│                                          #   +WorkflowArtifactExportDelivery
├── (engine)                               # MODIFIED: +IWorkflowArtifactClosureFactory + impl; publish-side
│                                          #   cross-authority guard; recompile vs relocated contract
├── Api/                                   # MODIFIED: preflight → thin wrapper (+consumer diagnostics),
│                                          #   +export endpoint, +DownloadWorkflowArtifactExportTarget,
│                                          #   +capability rel workflow-executable-export, +permission, +route
└── Persistence/Groundwork/                # MODIFIED: slot store + manifest entry DELETED (ledger moves to runtime family)
src/Elsa/Persistence/Groundwork/           # MODIFIED: runtime-family Groundwork slot store; slot document kind in
                                           #   ElsaRuntimeStorageManifest + GroundworkRuntimeStoreRegistration;
                                           #   historical-schema/target baselines updated
src/Elsa/Activities/
├── Runtime/Core/                          # MODIFIED: UnknownActivityTypeException re-parented to
│                                          #   ActivityResolutionException + new failure kind (research D2)
src/Apps/Elsa.Workbench/Program.cs         # MODIFIED: register new feature assemblies

tests/Elsa/Workflows/Runtime/Reconciliation/Tests/   # NEW test project (mirrors src)
tests/Elsa/Workflows/{Runtime,Publishing,Publishing/Api}/Tests/, tests/Elsa/Architecture/  # MODIFIED
docs/maps/* (regenerated), EXTENSION_POINTS.md files per research D9
```

**Structure Decision**: sibling-project-per-feature under `Workflows/Runtime/` (precedent: `ReferenceGarbageCollection`), reconciliation family shape mirrored from `Workflows/Design/Reconciliation` at 2-project scale (research D1). Shared contracts ride `Runtime.Core` because Publishing (the bridge) already references it — the same direction both reviewed extractions use.

## Phase 0 / Phase 1 outputs

- research.md — citation re-verification (zero drift; dispatcher-wiring correction), decisions D1–D9, risks R1–R5.
- data-model.md, contracts/ (3 files), quickstart.md — complete.
- Agent context: intentionally not updated (repo keeps shared agent instructions feature-neutral; agent-context extension disabled).

## Plan-phase notes carried from clarify (in scope)

1. **Preflight diagnostics asymmetry** — publishing wrapper gains activity-consumer diagnostics from the shared checker result (research D2).
2. **`UnknownActivityTypeException` classification** — re-parent + new `ActivityActivationFailureKind` member so the defense-in-depth path classifies as a non-retryable deployment incident (research D2).

## Next step

`/speckit.tasks` — generate dependency-ordered tasks from this plan. Suggested task clusters: (1) checker extraction + wrapper + classification fix; (2) slot-contract relocation + single runtime-family ledger (publishing slot store deleted, baselines updated) + cross-authority guard; (3) envelope + closure factory + Published-scope enforcement; (4) reconciliation projects + import pipeline + startup task; (5) export target seam + endpoint + capability; (6) composition/architecture tests + EXTENSION_POINTS + maps + workbench wiring.
