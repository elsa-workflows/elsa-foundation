# Implementation Plan: Collapse the per-activity hop chain under per-hop persistence

**Branch**: `runtime-throughput/2584-hop-chain-plan` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/234-hop-chain-collapse/spec.md`

**Program**: Runtime Efficiency (#2531), epic #2564, issue #2584. **Owner decisions recorded here**: the ADR 0020
reading is confirmed (2026-10-11, [#2584](https://github.com/elsa-workflows/elsa-foundation/issues/2584#issuecomment-6104682664),
now a dated clarification in `docs/adr/0020-runtime-checkpoint-commit-post-commit-work.md`); one switch controls all
three levers, default on, off reproduces today exactly; Balanced/Ephemeral modes (#2585), the root-write lease skip
(#2586) and diagnostics stores are out of scope. **Owner decisions of 2026-10-11 on the planning open questions**
(after review of PR #2639): the workflow tail is routed in-process and SC-001 becomes ≤ 52 (≤ 45 once #2586 lands);
SC-003's crash suite is `EfHopCollapseCrashConvergenceTests`; an existing item under the same `WorkItemId` is kept
as-is; a two-commit dispatch deletes its item in its first non-attempt-claim commit. All four are in the spec now; see
"Resolved questions".

## Summary

Under per-hop persistence (`Immediate`, later `Durable`), most of the ~106 runtime commits per request of the
4-activity HTTP workflow are queue and outbox plumbing between checkpoints ([research R0](research.md#r0--where-the-106-commits-come-from-verified-against-the-code)).
Three levers, behind one switch, remove that plumbing without removing or moving any checkpoint:

1. **Continuations in the commit (US1, FR-001–FR-003).** When the scheduler queue shares the checkpoint store's
   `RuntimeDbContext`, the EF checkpoint store writes each `EnqueueSchedulerWork` continuation as a scheduler work item
   inside the checkpoint transaction instead of an outbox row, so the later enqueue and `Delivered` commits disappear.
   The committer still builds the outbox entries, so the replay fingerprint is transport-neutral
   ([R1](research.md#r1--fr-001-where-the-continuation-is-written), [R2](research.md#r2--replay-fingerprint-and-the-replay-result-stay-transport-neutral)).
2. **In-process routing (US2, FR-004–FR-006).** The drainer runs a *routing pass* inside the dispatch of a claimed item:
   the non-committing next hops (`Start` → `WorkflowStarted` checkpoint item; leaf `ActivityCompleted` →
   `ParentCompletionEvaluation`; the workflow tail, root `ActivityCompleted` → `ContinuationScheduling` → tail
   `Checkpoint`) are dispatched inline instead of being enqueued, claimed and acknowledged. The claimed
   item stays claimed until the pass's first durable boundary commit, which deletes it; a crash re-derives the pass from
   it ([R5](research.md#r5--fr-004-the-routing-pass), [R7](research.md#r7--routable-in-process-fr-005-reconciled-with-spec-228)).
3. **Acknowledgement in the dispatch's commit (US3, FR-008).** The drain's consume frame is staged on the pipeline
   workspace and linked into the scope the Activities handlers commit from, so spec 105's fold works for
   `InvokeActivity` and `ParentCompletionEvaluation`, whose committers live in another DI scope today. A dispatch that
   commits twice deletes its item in its first commit that is not an attempt claim
   ([R6](research.md#r6--fr-008-the-di-scope-split-confirmed-by-reading-the-code)).

The projected result is ~52 commits per request with the root-write lease unchanged (research [R11](research.md#r11--projected-commit-count-and-the-sc-001-target)),
which is SC-001's target as restated by the owner on 2026-10-11; ~44 once #2586 lands, under the second bound of 45.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK pinned by `global.json`)

**Primary Dependencies**: EF Core 10 (SQLite, Npgsql, SQL Server, MySQL providers) through the Runtime EF module; the
existing runtime scheduler, checkpoint and coalescing services; CShells feature settings.

**Storage**: Runtime EF context (`RuntimeDbContext`). Tables used: `SchedulerWorkItems`, `RuntimePostCommitOutbox`,
`RuntimeCheckpointCommits`. No migration, no new column, no schema-version bump (research R1, R2).

**Testing**: xUnit. `Elsa.Workflows.Runtime.Tests`, `Elsa.Activities.Runtime.Tests`,
`Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests` (SQLite) and `.ProviderTests` (PostgreSQL, SQL Server,
MySQL in CI), `Elsa.Activities.Http.IntegrationTests` (the #2569 budget gate), `Elsa.Architecture.Tests`, and the
owner-run e2e A1 matrix (`e2e-tests/http/Test-RuntimeConcurrencyCorrectness.ps1`).

**Target Platform**: Server hosts (Workbench, product host later). Linux and Windows.

**Project Type**: Library: `Elsa.Workflows.Runtime.Core`, `Elsa.Workflows.Runtime`, `Elsa.Workflows.Runtime.Api`,
`Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore`, `Elsa.Activities.Runtime` (no change to its logic; its
handlers inherit US3 through the Runtime handler base).

**Performance Goals**: None as gates (ADR 0073 D7). Deterministic counts only: SC-001 durable commits per request
(counted by a new interceptor, research R10) and SC-002 EF command budgets (#2569, ADR 0080 D2).

**Constraints**:
- FR-007: every checkpoint that commits today still commits, with the same content, before the work it guards.
- FR-009: off restores today exactly, including command and commit counts (asserted with equality, not `≤`).
- No new project, package, table or public contract signature change; one new default interface member on
  `IRuntimeCheckpointCommitStore` (inherited `false`).
- New implementation types are `internal sealed` (§2.23.3, ADR 0080 internal-by-default).
- Coalesced runs unchanged (SC-005); in-memory compositions unchanged (spec edge case).

**Scale/Scope**: about 18 production files touched, 3 new internal types, 1 new options type; about 12 test files
touched or added; budget JSON files; documentation in 5 places.

## Constitution Check

*Gate before Phase 0; re-checked after Phase 1 (below).* Both ratified constitutions were loaded
(`.specify/memory/constitution.md` v5.0.0, `.specify/memory/constitution-framework.md` v5.0.1). The plan relies on no
draft section: framework §2.24 (draft) and Elsa §E2.9 (provisional) are not used.

| Gate | Status |
|---|---|
| **Framework §2.6 / §2.6.2 composition** | Pass. No new cross-feature coupling. The capability is a default member on the existing replacement contract `IRuntimeCheckpointCommitStore`; third-party stores inherit `false` and keep the outbox. Linking the consume frame threads the drain's accessor explicitly through the pipeline workspace, not an AsyncLocal (spec 105 FR-003). |
| **§2.7 bridge default** | Pass. The capability is decided by the provider that owns both stores (EF), not by the core reaching into EF. |
| **§2.16 / §2.20 project granularity** | Pass. No new project or package. |
| **§2.21.1 / §2.23.4 golden rule** | Pass, with a duty. Existing tests that assert today's separate enqueue, `Delivered` mark, routing-hop dispatches or separate acknowledgements on EF Immediate compositions describe the switch-off path, which must stay exact (FR-009). T005 inventories them; each is pinned to `CollapseHopChain = false` with its subject and objective unchanged, and a switch-on variant is added where the behaviour changes. No test is deleted. Spec 228's fusion guardrails must pass unchanged after the classifier extraction (T008). |
| **§2.23.1 composition / registration test** | Pass, with a duty. `WorkflowsRuntimeCheckpointPersistenceFeature` gains conditional wiring (the setting replaces the options before the Immediate early return), so it gets a registration test (T013). |
| **§2.23.2 behaviour tests** | Pass, with a duty. Risk paths get tests: committer partition and guard, store staging and replay, routing pass engagement/fallback/boundary, consume link, attempt-claim rule, crash convergence at nine kill points (K1–K9), ON/OFF guardrail, budgets. |
| **§2.23.3 visibility** | Pass. `RuntimeRoutingPass`, `HopRoutingClassifier` and the link are `internal sealed`; `InternalsVisibleTo` is added for the owning test assemblies (T001, T002). `RuntimeHopCollapseOptions` is public (options are public surface). |
| **§2.23.5 exception boundaries** | Pass. In-commit staging failures surface through the store's existing paths: rollback plus marker reconciliation, `RuntimeSchedulerWorkConsumeConflictException`, and `InvalidDataException` from `ReadChecked`. No new infrastructure exception escapes. |
| **§2.21.2 greenfield cadence** | **Gap recorded, not a failure.** The Elsa derived constitution declares no test-creation cadence (TDD, test-after or none). As in spec 200, tasks use red-first ordering where the base code can fail the test (the commit-count, crash and consume-link tests) and say so where a test is a non-regression guard. The owner may declare a cadence; see "Resolved questions". |
| **§2.22 / §2.22.1 / §2.22.2 documentation and extension-point catalog** | Pass, with a duty. Update the Runtime catalog (`IRuntimeCheckpointCommitStore` entry with the capability; `WorkflowsRuntimeCheckpointPersistenceFeature` entry with `CollapseHopChain`), the Runtime API README, `docs/runtime-durable-resumption.md` crash windows, and the glossary (routing pass, §E6 R5) (T058–T061). A test never asserts on catalog text. |
| **Elsa §E2.2 / §E2.6** | Pass. Runtime stays artifact-only, no Design dependency. |
| **Elsa §E6 naming** | Pass. `RuntimeHopCollapseOptions` (4 components), `RuntimeRoutingPass` (3), `HopRoutingClassifier` (3); no banned words; the EF staging method is added to the existing `EfRuntimeCheckpointParticipantStaging` rather than a new over-budget type name. |
| **ADR 0020 (with 2026-10-11 clarification)** | Pass. The commit records the continuation and dispatches nothing. |
| **ADR 0073 D5 / D7, ADR 0080 D1 / D2** | Pass. No timing, no performance gate; deterministic command and commit counts are correctness gates. Risk-based tests only. Mixed-version clusters are unsupported (ADR 0073 D5); the transport-neutral fingerprint still keeps a switch flip across a crash safe (research R2). |
| **AGENTS.md concurrent work claims / program bookkeeping** | Not applicable to this planning commit (no code). Before T001 the implementer claims #2584 with the worktree and scope, and comments at each state transition. |

**Post-design re-check (after Phase 1)**: unchanged; all gates pass with the duties above. No violation needs the
Complexity Tracking table.

## Project Structure

### Documentation (this feature)

```text
specs/234-hop-chain-collapse/
├── spec.md                 # Assumption on ADR 0020 updated (confirmed 2026-10-11)
├── plan.md                 # this file
├── research.md             # R0–R13
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── checkpoint-commit-continuations.md
│   └── routing-pass-and-acknowledgement.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/essentials/Workflows/Runtime/
├── Core/
│   ├── Contracts/IRuntimeCheckpointCommitStore.cs          # + CanStageSchedulerWork default member (FR-001)
│   ├── Models/RuntimeCheckpointCommit.cs                   # + SchedulerWorkInCommit, WithSchedulerWorkInCommit
│   ├── Models/RuntimeCheckpointCommitResult.cs             # + ScheduledSchedulerWorkItemIds
│   ├── Models/RuntimeHopCollapseOptions.cs                 # new switch (FR-009)
│   ├── Models/RuntimePipelineWorkspace.cs                  # + ConsumedWorkClaim (FR-008)
│   └── Diagnostics/RuntimeSchedulerDispatchDiagnostics.cs  # + routing counters (FR-010)
├── Elsa.Workflows.Runtime/
│   ├── Elsa.Workflows.Runtime.csproj                       # + InternalsVisibleTo test assemblies
│   ├── Extensions/RuntimeCoreServiceCollectionExtensions.cs  # register options, pass, classifier; committer factory args
│   ├── Services/Checkpoints/RuntimeCheckpointCommitter.cs    # partition, guard, attempt-claim exclusion, fast-path skip
│   ├── Services/Checkpoints/RuntimeCheckpointCommitValidator.cs  # SchedulerWorkInCommit rules
│   ├── Services/Checkpoints/RuntimeSchedulerPostCommitIntentDispatcher.cs  # share the work-item reader
│   ├── Services/Checkpoints/InMemoryRuntimeCheckpointCommitStore.cs        # refuse marked commits (C16)
│   ├── Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs       # forward capability when no session
│   ├── Services/Coalescing/CoalescingWorkflowSchedulerWorkQueue.cs         # expose inner queue for C3
│   ├── Services/Coalescing/RuntimeCheckpointFold.cs                        # guard: marked commits never folded
│   ├── Services/Scheduler/RuntimeRoutingPass.cs                            # new (FR-004)
│   ├── Services/Scheduler/WorkflowSchedulerDrainer.cs                      # open/run/close the pass
│   ├── Services/Scheduler/RuntimeConsumedSchedulerWorkClaimAccessor.cs     # LinkTo (FR-008)
│   ├── Services/Pipelines/RuntimeExecutionPipelineDispatcher.cs            # stage ConsumedWorkClaim
│   ├── Services/WorkHandlers/HopRoutingClassifier.cs                       # new, extracted from the fusion driver
│   ├── Services/WorkHandlers/ReplaySafeFusionDriver.cs                     # use the classifier
│   ├── Services/WorkHandlers/RuntimeSchedulerWorkHandlerBase.cs            # link the consume frame
│   ├── Services/WorkHandlers/WorkflowStartActivitySchedulerWorkHandler.cs  # link the consume frame
│   ├── Services/WorkHandlers/WorkflowStartSchedulerWorkHandler.cs          # RouteAsync for the WorkflowStarted item
│   ├── Services/WorkHandlers/WorkflowCompleteActivitySchedulerWorkHandler.cs  # RouteAsync for routing hops
│   └── EXTENSION_POINTS.md
├── Api/
│   ├── Coalescing/WorkflowsRuntimeCheckpointPersistenceFeature.cs  # CollapseHopChain setting
│   └── README.md
└── Persistence/EntityFrameworkCore/
    ├── Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.csproj  # + InternalsVisibleTo EF test assembly
    ├── Stores/EfRuntimeCheckpointCommitStore.cs              # capability, staging call, replay result
    ├── Stores/EfRuntimeCheckpointParticipantStaging.cs       # StageScheduledSchedulerWorkAsync
    ├── Stores/EfRuntimeCheckpointOutboxParticipantStaging.cs # skip marked entries
    └── Stores/EfSchedulerWorkQueueStore.cs                   # ToEntity internal, SharesContextWith
tests/essentials/
├── Persistence/EntityFramework/Tests/CommitCaptureInterceptor.cs           # new (SC-001 counter)
├── Architecture/Tests/SecretsEfPersistencePilotArchitectureTests.cs        # reviewed-source inventory entry
├── Activities/Http/IntegrationTests/
│   ├── CommandBudgets/EfCommandBudgetTests.cs, ef-command-budgets.json, ef-commit-budgets.json, README.md
│   ├── HttpEndpointHostFixture.cs                                          # collapse switch parameter
│   └── Elsa.Activities.Http.IntegrationTests.csproj
├── Workflows/Runtime/Tests/
│   ├── RuntimeHopCollapseCommitterTests.cs (new), RuntimeRoutingPassTests.cs (new)
│   ├── RuntimeDrainStepSingleTransactionTests.cs, RuntimeSchedulerWorkHandlerBaseTests.cs
│   ├── RuntimeCheckpointCommitValidatorTests.cs, RuntimeCheckpointFoldTests.cs, RuntimeExecutionPipelineDispatchTests.cs
│   └── WorkflowsRuntimeCheckpointPersistenceFeatureTests.cs
├── Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/
│   ├── RuntimeCheckpointCommitContract.cs, EfRuntimeCheckpointSchedulerWorkParticipantTests.cs
│   ├── EfHopCollapseGuardrailTests.cs (new), EfHopCollapseCrashConvergenceTests.cs (new)
└── Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/EfRuntimeCheckpointSchedulerWorkParticipantProviderSmokeTests.cs
docs/
├── adr/0020-runtime-checkpoint-commit-post-commit-work.md   # dated clarification (this commit)
├── runtime-durable-resumption.md                            # crash windows A′ and C′
└── glossary/elsa.md                                         # routing pass
```

**Structure Decision**: every change sits at the seam that owns the behaviour: the committer decides the transport,
the EF store writes it, the drainer owns the routing pass, the pipeline dispatcher and handler base own the scope
link, and the existing checkpoint-persistence feature owns the operator setting. No new project or abstraction layer.

## Design notes per lever

### US1 — continuations in the commit

1. `RuntimeCheckpointCommitter.CommitAsync` builds `postCommitOutbox` as today, then, when the FR-001 gate holds
   (research R4), marks every `EnqueueSchedulerWork` entry in `SchedulerWorkInCommit`.
2. `RuntimeCheckpointCommitValidator` checks the marks (data-model rules).
3. `EfRuntimeCheckpointCommitStore.CommitNewAsync` stages marked entries with `StageScheduledSchedulerWorkAsync` after
   the outbox staging and before the consumed-work delete, inside the same transaction; `EfRuntimeCheckpointOutboxParticipantStaging`
   skips them; the marker records only written outbox ids.
4. `ResolveReplay` derives `ScheduledSchedulerWorkItemIds` (R2). The committer's guard checks the union (C8).
5. `PublishInProcessHopWorkItems` skips marked intents.

### US2 — routing pass

1. `WorkflowSchedulerDrainer.DispatchAsync`: when the FR-004 gate holds, open `RuntimeRoutingPass` for the claimed item
   inside the `DispatchWithClaimRenewalAsync` delegate; after the handler, run captured items inline (pause gate,
   terminal check, `FindHandler`, pipeline dispatcher); close at the boundary; fall back on any stop (contract P1–P8).
2. `WorkflowStartSchedulerWorkHandler` and `WorkflowCompleteActivitySchedulerWorkHandler` route through
   `RuntimeRoutingPass.RouteAsync` (injected as an optional constructor argument; absent means enqueue).
3. `HopRoutingClassifier` holds the spec-228 rules, the Start-hop rule and the workflow-tail rule (`IsTailStep`).
   `RuntimeRoutingPass` uses all of them; `ReplaySafeFusionDriver` uses only the spec-228 rules, so its tail fallback
   under coalescing is unchanged.
4. The tail pass starts at the claimed root `ActivityCompleted`, runs `ContinuationScheduling` and the tail
   `Checkpoint` inline, and ends at the tail checkpoint commit (`WorkflowCompleted`, or the root's `ActivityCompleted`
   when the root has no outcomes), which the `Checkpoint` handler makes through its drain-scope committer and which
   consumes the claimed item. Kill point K9 covers it.

### US3 — acknowledgement in the dispatch's commit

1. `RuntimeExecutionPipelineDispatcher` stages its accessor on `RuntimePipelineWorkspace.ConsumedWorkClaim`.
2. `RuntimeSchedulerWorkHandlerBase.ExecuteAsync` and `WorkflowStartActivitySchedulerWorkHandler.ExecuteWithServicesAsync`
   link the body's accessor to it while the switch is on.
3. `RuntimeCheckpointCommitter.ResolveConsumedWorkItems` skips `ActivityAttemptClaimed` checkpoints while the switch is
   on, so a two-commit dispatch deletes its item in the outcome commit (FR-008).

US2's boundary commit for an `ActivityCompleted` pass is the routed `ParentCompletionEvaluation`'s outcome commit, made
through a committer in another scope, so US2 reaches its full saving only with US3. Without US3 it is still correct:
the drainer acknowledges the claimed item separately after the pass (contract P6).

## Validation design

1. **Baseline first (T004).** Add the commit counter and record the per-kind baseline for all three reference
   workflows under both modes before any lever lands; the switch-off budget cases are created from it.
2. **Red-first where the base code can fail** (cadence gap, see above): the cross-scope consume test, the in-commit
   contract cases, the routing pass tests and the crash kill points are each shown failing (or recorded as
   non-regression guards) before their implementation task.
3. **Per story:** implement, run the story's tests and the affected full projects, then the ON/OFF guardrail.
4. **Bite-proofs** (temporary working-tree mutations, reverted): remove the marker-last ordering (K2 must fail); fold
   the consume into `ActivityAttemptClaimed` (K5/K7 must fail); skip the fallback enqueue on a pause (the pause case
   must fail); make the fingerprint include `SchedulerWorkInCommit` (the switch-flip replay case must fail); treat the
   tail as a fallback in the routing pass (the tail-pass cases must fail).
5. **SC-001 (T057):** the `http-reference` / `Immediate` durable-commit total must be ≤ 52, enforced by
   `ef-commit-budgets.json`.
6. **Gates:** budgets lowered in the same change (SC-002), switch-off equality (SC-004), Coalesced unchanged (SC-005),
   architecture tests, maps check, native providers in CI, owner-run A1 matrix (SC-003) and backend e2e.

## Resolved questions

The planning draft raised four open questions. The owner decided all four on 2026-10-11 after reviewing PR #2639;
each decision is now in the spec and in the artifacts below.

- **OQ-1 — SC-001 (≤ 45) was not reachable with the lease unchanged.** The draft projected ~58, ~52 with the workflow
  tail routed, ~44 with tail routing plus #2586. **Decided (owner, 2026-10-11): add tail routing.** Under per-hop
  persistence the workflow tail (root `ActivityCompleted` → `ContinuationScheduling` → tail `Checkpoint`) is routed
  in-process; spec 228 excluded it only because coalesced commits were buffered. SC-001 is restated as "at most 52
  durable commits per request with the root-write lease (#2586) unchanged; at most 45 once #2586 lands", measured by the
  deterministic commit counter. Applied to FR-004, FR-005, US2 acceptance scenario 3, the edge cases, research R7/R11,
  the routing contract (P2a) and tasks T035, T037, T039 and T057. Writing in-commit continuations already claimed by
  the committing drain (up to 15 fewer claims) stays a candidate for a follow-up spec ([R11](research.md#r11--projected-commit-count-and-the-sc-001-target)).
- **OQ-2 — The Groundwork crash suite named in SC-003 no longer exists** (ADR 0073). **Decided (owner, 2026-10-11):**
  SC-003 names the new EF suite `EfHopCollapseCrashConvergenceTests` (the two-generation cut-and-recover harness, kill
  points K1–K9, [R12](research.md#r12--the-crash-suite-named-in-sc-003)), citing ADR 0073.
- **OQ-3 — The existing-item edge case.** The draft spec called a different item under the same identity a conflict,
  but enqueue keeps and returns the existing item. **Decided (owner, 2026-10-11):** the spec matches the code. An item
  already present under the same `WorkItemId` is kept as-is (returned, not overwritten), and in-commit writes behave
  the same way; there is no conflict outcome, and no task adds one
  ([R3](research.md#r3--idempotency-of-an-in-commit-continuation-spec-edge-case), contract C12).
- **OQ-4 — FR-008 for dispatches that commit twice.** **Decided (owner, 2026-10-11):** a dispatch that makes an
  attempt-claim commit and then the outcome commit (`InvokeActivity`, parent completion evaluation) deletes its claimed
  work item in the first commit that is not an attempt claim, so recovery after a crash mid-attempt is unchanged.
  Applied to FR-008, US3 (acceptance scenario 2), contract A4 and tasks T048 and T055
  ([R6](research.md#r6--fr-008-the-di-scope-split-confirmed-by-reading-the-code)).
- **Cadence gap (§2.21.2), still open.** The Elsa constitution declares no test cadence. Recommended: keep the spec 200
  precedent (red-first for defect-shaped and crash tests) until the owner declares one.

## Complexity Tracking

No violations.

**Rejected alternatives** (details in research): a separate change-set slot carrying the work items (R1); recording
scheduled ids in the marker document (R2, needs a schema bump); a queue decorator that swallows enqueues (R5);
reusing the spec-228 pump under Immediate (R5); an AsyncLocal consume accessor (R6, violates spec 105 FR-003); folding
the consume into `ActivityAttemptClaimed` (R6).
