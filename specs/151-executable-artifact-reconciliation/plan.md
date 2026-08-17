# Implementation Plan: Executable Artifact Reconciliation

**Branch**: `1304-executable-artifact-reconciliation` | **Date**: 2026-08-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/151-executable-artifact-reconciliation/spec.md` (issue #1304 rev 4; all 7 clarifications resolved 2026-08-14)

## Summary

Close the "runtime-only engine has nothing to execute" gap by (a) exporting a portable, self-contained **closure envelope** (artifact + transitive child artifacts + published references + trigger bindings) from a publish-capable engine through a pluggable export-target seam whose v1 target is an API download, and (b) importing/reconciling such envelopes into a design-free runtime's executable store behind a two-axis requirements gate (consumer capabilities + storage drivers, and per-node CLR type presence) that rejects at import, never at first activation. Three reviewed extractions carry the design (all confirmed by the 2026-08-15 architect review, with refinements): the requirements checker moves from `Publishing.Api` to the Runtime layer (`IRuntimeRequirementChecker`, covering executables and templates); the activation authority becomes **new, neutrally named runtime contracts** (`IWorkflowActivationAuthority`/`WorkflowActivationSlot` with an explicit `WorkflowActivationSource` ownership field — superseding publishing's slot store, never inferring ownership from id prefixes) behind **one shared `WorkflowActivationCoordinator`** that owns the complete activation lifecycle for both publish and import; and the executable hasher moves to the runtime layer, byte-stable, so the importer recomputes each received artifact's content hash before persistence (content-addressing invariant guard; signing stays deferred). Triggering reuses the existing startup-task + shell-reload model (#1303 deferred).

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

*Gates evaluated against Elsa constitution v4.0.0 + framework v4.0.0. Re-checked post-Phase-1 and again post-`/speckit.analyze` (2026-08-15): PASS with one Complexity Tracking entry (below), recorded after the 2026-08-15 architect review turned the activation relocation into a supersession.*

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
| §E6 naming R1–R8 | PASS w/ 2 flags | Naming table research D8. Two names surfaced for reviewer judgment, both intended and spec-pinned: (1) the `…Target` suffix is not R4-codified — pinned domain term (FR-B-010a); (2) `WorkflowActivationSource` uses `…Source` as an *ownership descriptor record*, not in R4's codified sense (`…Source` = a pull contract that returns items). Carried into implementation by tasks T081 and T021 respectively. |
| §E5 computed versioning | PASS | No `<Version>` elements; new csprojs follow Line B; no `-p:Version` anywhere. |
| §2.21.1 golden rule | PASS w/ recorded removal | **Not pure moves** (superseded by the 2026-08-15 architect review): the checker and hasher extractions are behavior-preserving relocations, but the activation authority is *superseded* — publishing's slot contract/store/in-memory impl are deleted, `PublicationActivator` becomes a caller, and the runtime-side rename sweep is total. Behavior preservation holds **by construction** (the coordinator absorbs the existing sequence verbatim, incl. compensation), and existing publishing + runtime tests must pass with wiring/naming changes only — a hard task-phase gate (tasks T040, T112). Tests whose *subject* is deleted are recorded in Complexity Tracking below per §2.21.1. |

## Complexity Tracking

> **This section records a §2.21.1 test-removal approval, not a Constitution Check violation.** The plan template reserves Complexity Tracking for justifying violations; §2.21.1 independently names "the plan's *Complexity Tracking* section" as a sanctioned location for recording architect approval of a test removal. That is the use here — every Constitution Check gate above passes.

### Test removals — §2.21.1 recorded architect approval

§2.21.1 (restated by Elsa §E1) requires **explicit recorded architect approval** to remove a test, and states that a passing CI is not sufficient justification. The 2026-08-15 architect review (@sfmskywalker on PR [#1330](https://github.com/elsa-workflows/elsa-foundation/pull/1330)) resolved that publishing's slot store is **superseded and deleted** rather than relocated. That decision necessarily removes the *subject under test* of the publishing-family slot tests. Recording it here so the removal is approved in the sanctioned location rather than inferred from a green build.

| Subject removed | Approving architect / record | Disposition of its tests |
|---|---|---|
| `IPublicationSlotStore`, `PublicationSlot`, `PublicationSlotIdentity`, `PublicationSlotTransitionResult` (`Publishing.Core`) | @sfmskywalker, PR #1330 review 2026-08-15 — *"two reconciliation inputs are justified; two independent activation authorities are not"* (spec.md Clarifications, 2026-08-15) | Objective **migrates** to the neutral runtime contracts: every behavioral assertion is re-homed onto `IWorkflowActivationAuthority` / `WorkflowActivationCoordinator` (tasks T037–T039). Only tests asserting the *publishing-family location* of the ledger are removed outright. |
| `GroundworkPublicationSlotStore` + its publishing-family manifest entry | Same review, plus the "one physical ledger" resolution (spec.md Clarifications 2026-08-14 PR-review; research.md D3) | Objective migrates to the runtime-family Groundwork slot store (task T026). Groundwork historical-schema and target baselines update deliberately and by name (task T036). |
| The in-memory slot implementation in `InMemoryPublicationStores.cs` | Same review | Objective migrates to the in-memory `IWorkflowActivationAuthority` default (task T023). |

**Nothing else is removed.** `PublicationRecord`, publication policies, `IPublicationRecordStore` attempt history, and the publishing preflight views all stay in Publishing with their tests intact (spec.md Non-Goals). If any *other* test turns out to require deletion during implementation, §2.23.4 applies: flag it, do not delete it solo, and extend this table.

### Test removals (2) — §2.21.1, Phase 2D, recorded 2026-08-16

Two tests were **deleted** in T041, not migrated. Recording per §2.21.1: removing a test requires explicit architect approval and a passing CI is not sufficient justification.

**What was removed.** Two tests asserting that `WorkflowTriggerIndexer` **notifies `IWorkflowTriggerIndexObserver`**. Their subject was the observer notification that lived inside the artifact-scoped `IndexAsync` path.

**Why the subject no longer exists.** FR-B-006 makes observer notification part of the activation lifecycle, owned by `WorkflowActivationCoordinator` — the indexer never notifies now, and `WorkflowTriggerIndexer` lost its `IEnumerable<IWorkflowTriggerIndexObserver>` constructor parameter entirely. This is §2.23.4's *"has the subject moved?"* case, and it moved to a component with its own coverage: notification is asserted in `WorkflowActivationCoordinatorTests`. **The objective is preserved; only its home changed.**

**Everything else was migrated, not weakened.** T041's blast radius was nine test files — the artifact-scoped path was the standard publish shortcut in fixtures. All but these two were rewritten onto prepare→activate with assertions intact.

**If Sipke disagrees**, the remedy is to re-add equivalent coverage against the coordinator rather than restore the indexer path, which no longer exists.

### Behaviour change — §2.23.4 recorded decision: the coordinator does not reproduce publishing's projection leak

**Approved by Joey, 2026-08-15**, on the recommendation below. Recorded here because §2.23.4 treats "the refactor resolved a bug the tests silently relied on" as an architect decision, never a solo one.

**The defect.** On a pre-flip failure — projection-prepare throws, or the slot CAS conflicts — `PublicationActivator` marks the publication record failed and returns. Nothing deletes the bindings and recurring schedules it already prepared; `PublishWorkflowRequestHandler` only retires the reference. The prepared rows are inert (`IsActive = false`) but they accumulate, and research D3's writer census notes that restore/compensation *re-prepares from the store*, so stale prepared rows may be picked up on a later restore. Whether that makes it a correctness bug or only a hygiene bug is **not established** — it was not traced.

**The decision.** `WorkflowActivationCoordinator` removes the candidate's projections and retires its reference on **every** failure path, restoring the predecessor only when the slot actually flipped. It does not reproduce the leak.

**Why this is not deferred to a separate fix.** The coordinator is *new* code, not modified code — nothing calls it yet, so no existing behaviour changes at the moment it is written. Deliberately writing a known leak into a new component so a later commit can remove it is strictly worse than not writing it, and would require T029/T030 to reproduce the leak on purpose. It also contradicts the component's own charter: FR-B-006 makes the coordinator the **sole writer** of activation-relevant serving state, and a sole writer that leaves orphans behind is not one.

**The gate that still applies.** No test asserts the leak — it is a leak, untested by construction. If Publishing.Api still passes 473/0 after T029/T030 retarget publishing onto the coordinator, this record is sufficient and there is nothing further to settle. **If a publishing test fails because it relied on the leaked rows, that is the §2.23.4 conversation with Sipke**, and it must happen before the retarget lands. Related: [#1358](https://github.com/elsa-workflows/elsa-foundation/issues/1358) is the same class of defect — orphaned serving state with no cleanup path — and the single-writer rule is the answer to both.

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
│                                          #   +RuntimeRequirementCheckResult, +IWorkflowExecutableHasher,
│                                          #   +IWorkflowActivationAuthority/WorkflowActivationSlot/
│                                          #   WorkflowActivationSource, +IWorkflowActivationCoordinator
├── Elsa.Workflows.Runtime.csproj          # MODIFIED: <Compile Remove> glob gains Reconciliation sibling;
│   └── (impl project)                     #   +RuntimeRequirementChecker default, +in-memory slot store,
│                                          #   AddWorkflowRuntime() registrations
├── Reconciliation/
│   ├── Core/Elsa.Workflows.Runtime.Reconciliation.Core.csproj   # NEW: source contract, options, exceptions, result
│   └── Elsa.Workflows.Runtime.Reconciliation.csproj             # NEW: abstract+Json features, reconciler,
│                                                                #   startup task, import gate
src/Elsa/Workflows/Publishing/
├── Core/                                  # MODIFIED: -slot contract (superseded); +IWorkflowArtifactExportTarget,
│                                          #   +WorkflowArtifactExportDelivery
├── (engine)                               # MODIFIED: +IWorkflowArtifactClosureFactory + impl; PublicationActivator +
│                                          #   publish handler become callers of the shared activation coordinator
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

`/speckit.tasks` — generate dependency-ordered tasks from this plan. Suggested task clusters: (1) checker + hasher extractions + wrapper + classification fix; (2) neutral activation authority + shared coordinator (absorbing PublicationActivator's sequence; publishing slot store deleted, baselines updated) + explicit ownership conflict rules + closing the censused back doors (remove the IndexAsync fallback/path, route pump deletes, collapse the schedule double-write); (3) envelope + closure factory + Published-scope enforcement; (4) reconciliation projects + import pipeline + startup task; (5) export target seam + endpoint + capability; (6) composition/architecture tests + EXTENSION_POINTS + maps + workbench wiring.

### Behaviour change (2) — §2.23.4 recorded decision: one logical version may no longer be claimed by two payloads

**Approved by Joey, 2026-08-17**, after the finding was surfaced: *"Lets go for this safer model and
assume the prior logic was not safe enough. As long as we record this decision, a reviewer could
revise this choice."* Found by running, not by reading, during US4 (T094–T096). Recorded because
§2.23.4 treats "the refactor resolved a bug the tests silently relied on" as an architect decision,
never a solo one, and because a pre-existing test had to change to accommodate it.

**The choice, stated plainly so it can be reversed as a unit:** the prior permissiveness is treated as
*not safe enough*, not as a capability being withdrawn. A reviewer who disagrees should reverse the
rule itself (drop T094's two collision gates), not soften the diagnostic — a half-enforced version
identity is worse than either end, because latest-wins would order some collisions and not others.

**The behaviour before.** The importer had no version gate. A mount could re-point a definition at
**different content under an unchanged `ArtifactVersion`** and the runtime would happily activate it —
serving state moved, and nothing recorded that two distinct content-addressed artifacts had both
claimed to be version `1.0.0` of the same definition.

**Why FR-B-007 forbids it.** Latest-wins orders activations by SemVer sort key. If one logical version
can denote two payloads, the ordering is not a function: whether a mount supersedes or is skipped
becomes a question of arrival order, not of version. So the pin is not merely stricter, it is what
makes latest-wins well-defined. T094 rejects the collision at both sites — store-side (candidate
versus the active reference) and envelope-side (two members of one closure claiming the same
`(DefinitionId, sortkey)` with different artifact ids).

**The test this changed.** `ImportedRecurringTriggerEndToEndTests.A_superseding_import_re_projects_the_schedule_instead_of_leaving_the_old_one_firing`
(added by T071a) mounted two *different* timer payloads, both at the default `1.0.0`, and asserted the
second superseded the first. Under the new rule that is the broken-source case, and the suite went
85/1 — **the failure is how the behaviour change was discovered.** The fix gives the superseding build
`1.1.0`, which is what a real new build carries. **The test's assertions are unchanged**: it still
proves a superseding import re-projects the schedule instead of leaving the old one firing. Only the
fixture's illegal premise moved. This is a fixture correction, not a weakening, and no test was
removed.

**The operator-visible consequence, accepted knowingly.** Anyone relying on "overwrite the mount, keep
the version" as a deployment idiom now gets a rejection instead of a silent re-point. That is the
intended outcome — a content change without a version change is exactly what the broken-source
diagnostic is for — and it was weighed against the alternative before being accepted. It will show up
again in the real-host run against `Elsa.Foundation.Host`; that is expected, not a regression.
