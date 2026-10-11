# Tasks: Collapse the per-activity hop chain under per-hop persistence

**Input**: Design documents from `specs/234-hop-chain-collapse/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**:
- Framework §2.23 applies. `WorkflowsRuntimeCheckpointPersistenceFeature` gains conditional wiring, so it gets a
  registration test (T013, §2.23.1). Every logic-bearing change gets risk-based behaviour tests (§2.23.2): committer
  partition and guard, store staging and replay, routing pass, consume link, crash convergence, ON/OFF guardrail and
  budgets.
- Framework §2.21.1 applies to existing tests: those that describe today's separate enqueue, `Delivered` mark,
  routing-hop dispatches or acknowledgements on EF Immediate compositions keep their subject and objective, pinned to
  the switch-off path (T005, T012). None is deleted.
- **Cadence gap (§2.21.2)**: the Elsa derived constitution declares no test cadence. Recorded for the owner (plan,
  "Resolved questions", last item). Following the spec 200 precedent, each story lists its tests before its implementation and
  requires a failing run ("red") where the base code can fail the test; a task that cannot fail on the base code says
  it is a non-regression guard.
- Build and test only the projects a task touches; check `uptime` before trusting a timing-shaped failure (AGENTS.md).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1, US2, US3 from spec.md

---

## Phase 1: Setup

**Purpose**: test access to new internal types, the deterministic commit counter, and the baseline.

- [ ] T001 Add `<InternalsVisibleTo Include="Elsa.Workflows.Runtime.Tests" />` and `<InternalsVisibleTo Include="Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests" />` to `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Elsa.Workflows.Runtime.csproj` (pattern: `src/essentials/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj`), for `RuntimeRoutingPass`, `HopRoutingClassifier` and `RuntimeConsumedSchedulerWorkClaimAccessor.LinkTo`.
- [ ] T002 [P] Add `<InternalsVisibleTo Include="Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests" />` and `<InternalsVisibleTo Include="Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests" />` to `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.csproj`, for `EfSchedulerWorkQueueStore.SharesContextWith` and the staging method.
- [ ] T003 [P] Create `tests/essentials/Persistence/EntityFramework/Tests/CommitCaptureInterceptor.cs` (research R10): a `DbTransactionInterceptor` + `DbCommandInterceptor` that counts committed transactions (sync and async) and write commands (non-query, or reader text starting `INSERT`/`UPDATE`/`DELETE`) run with no transaction, and classifies each commit by the table of its first write using `RuntimeOperationalStateEfModule.SchedulerWorkTableName`, `RuntimeOperationalStateEfModule.CheckpointCommitTableName` and `RuntimePostCommitOutboxEfModule.TableName` (kinds: checkpoint transaction, work-item enqueue, claim, acknowledgement, outbox delivery, other). Add its path to the reviewed source inventory in `tests/essentials/Architecture/Tests/SecretsEfPersistencePilotArchitectureTests.cs` and a `<Compile Include … Link="CommitCaptureInterceptor.cs" />` to `tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj`.
- [ ] T004 Record the baseline before any lever lands: in `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/EfCommandBudgetTests.cs` add the `CommitCaptureInterceptor` beside the command interceptor (same `ConfigureDbContext<RuntimeSqliteDbContext>` call), print `Durable commits: …` and the per-kind breakdown per run, and check the total against a new `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/ef-commit-budgets.json` (same shape and rules as `ef-command-budgets.json`; add `<None Update="CommandBudgets/ef-commit-budgets.json" CopyToOutputDirectory="PreserveNewest" />` to the csproj). Fill it with the measured counts for all three workflows under both modes, document it in `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/README.md`, and post the per-kind table on #2584 with the implementation claim. Non-regression guard (passes on base code by construction).
- [ ] T005 Inventory the existing tests whose subject is today's EF Immediate transport (outbox continuation rows and their `Delivered` mark, routing-hop dispatches, separate acknowledgements, dispatch counts). Start from the files already found to read that transport through a full runtime: `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/LiveDrainSweepContentionContract.cs` (and its runners `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/RuntimeEntityFrameworkCoreLiveDrainSweepContentionTests.cs`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/LiveDrainSweepContentionProviderSmokeTests.cs`), `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/PoisonedFirstCheckpointContractTests.cs`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/ClaimLeaseOverrunContract.cs`, `tests/essentials/Activities/Http/IntegrationTests/ResponseReplaySafetyProcessTests.cs`, then search `tests/` for further hits. For each, record its subject and objective and whether it pins the switch off or gains an ON variant, in the Notes section of `specs/234-hop-chain-collapse/tasks.md`.

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: the switch, the counters, the shared classifier, the operator setting and the switch-off budget cases.

- [ ] T006 Create `src/essentials/Workflows/Runtime/Core/Models/RuntimeHopCollapseOptions.cs` (`public sealed`, `bool Enabled { get; set; } = true;`, doc comment naming FR-001/FR-004/FR-008 and "off restores today exactly") and register it with `services.TryAddSingleton<RuntimeHopCollapseOptions>()` next to `RuntimeReplaySafeFusionOptions` in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`.
- [ ] T007 [P] Add `InProcessRoutingSteps`/`RecordInProcessRoutingStep()` and `InProcessRoutingFallbacks`/`RecordInProcessRoutingFallback()` (Interlocked, included in `Reset()`) to `src/essentials/Workflows/Runtime/Core/Diagnostics/RuntimeSchedulerDispatchDiagnostics.cs` (FR-010).
- [ ] T008 Extract the completion classification of `ReplaySafeFusionDriver.ClassifyCompletionAsync` (child-faulted metadata, parent `ReplaySafe` profile via the burst-cached `PinnedExecutableRead`) into `internal sealed class HopRoutingClassifier` in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/HopRoutingClassifier.cs`, add `IsStartCheckpoint(RuntimeSchedulerWorkItem)` (a `Checkpoint` item whose payload names `RuntimeCheckpointNames.WorkflowStarted`), register it scoped in `RuntimeCoreServiceCollectionExtensions.cs`, and make `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/ReplaySafeFusionDriver.cs` call it. Golden rule: `tests/essentials/Activities/Runtime/Tests/ReplaySafeFusionGuardrailTests.cs` and `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfReplaySafeFusionCapRecoveryTests.cs` pass unchanged.
- [ ] T009 Add `[ManifestSetting(DisplayName = "Collapse hop chain", Description = "Under per-hop persistence, write continuations in the checkpoint commit, route non-committing hops in-process and acknowledge claimed work in the dispatch's commit. Off restores the separate hops exactly.", Category = "Runtime", DefaultValue = "true")] public bool CollapseHopChain { get; set; } = true;` to `src/essentials/Workflows/Runtime/Api/Coalescing/WorkflowsRuntimeCheckpointPersistenceFeature.cs`; in `PostConfigureServices`, before the `Immediate` early return, `services.Replace(ServiceDescriptor.Singleton(new RuntimeHopCollapseOptions { Enabled = CollapseHopChain }))`.
- [ ] T010 [P] Add a `bool collapseHopChain = true` parameter to `StartDurableSqliteAsync` and `AddDurableSqlite` in `tests/essentials/Activities/Http/IntegrationTests/HttpEndpointHostFixture.cs`, passed to the feature's `CollapseHopChain`.
- [ ] T011 In `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/EfCommandBudgetTests.cs` add an `Immediate.CollapseOff` case per workflow (fixture started with `collapseHopChain: false`; the test resolves `RuntimeHopCollapseOptions` and asserts `Enabled == false` so the case cannot pass vacuously), asserting **equality** with its budget for both commands and commits (SC-004); extend `Every_budget_names_a_reference_workflow_and_a_persistence_mode` to accept that key. Set `Immediate.CollapseOff` in `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/ef-command-budgets.json` (1482 / 569 / 507) and `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/ef-commit-budgets.json` (T004 values), and describe the equality rule in `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/README.md`.
- [ ] T012 Pin each test recorded by T005 to the switch-off path (register `new RuntimeHopCollapseOptions { Enabled = false }` or set `CollapseHopChain = false` in its host composition), keeping its subject and objective, in the files listed in T005's Notes (at least `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/LiveDrainSweepContentionContract.cs`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/PoisonedFirstCheckpointContractTests.cs`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/ClaimLeaseOverrunContract.cs`, `tests/essentials/Activities/Http/IntegrationTests/ResponseReplaySafetyProcessTests.cs`). Name the reason in a one-line comment at each pin.
- [ ] T013 [P] Add a registration test to `tests/essentials/Workflows/Runtime/Tests/WorkflowsRuntimeCheckpointPersistenceFeatureTests.cs`: the setting is declared with its metadata; default resolves `RuntimeHopCollapseOptions.Enabled == true`; `CollapseHopChain = false` resolves `false` under both `Immediate` and `Coalesced`; the existing assertions stay unchanged (§2.23.1, conditional wiring).

**Checkpoint**: switch, counters, classifier and setting in place; budgets ON == OFF == baseline; all existing suites green.

---

## Phase 3: User Story 1 — A continuation is recorded in the commit that produces it (Priority: P1) 🎯 MVP

**Goal**: on a composition whose queue shares the checkpoint store's `RuntimeDbContext`, every `EnqueueSchedulerWork`
continuation is a scheduler work item written in the checkpoint transaction; no separate enqueue or `Delivered`
commit follows (FR-001–FR-003).

**Independent Test**: `http-reference` under `Immediate` on SQLite drops by the continuation commits (~30 per request
before US2/US3); K1 and K2 converge; a split composition and non-queue intents keep the outbox.

### Model members (needed for the tests to compile)

- [ ] T014 [US1] Add `IReadOnlyCollection<string> SchedulerWorkInCommit` (default empty, normalized like `ConsumedSchedulerWorkItems`) and `WithSchedulerWorkInCommit(...)` to `RuntimeCheckpointStateChangeSet` in `src/essentials/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs`, carried through every `With…` method and constructor path. Do not add it to `src/essentials/Workflows/Runtime/Core/Models/RuntimeCheckpointCommitFingerprint.cs` (research R2).
- [ ] T015 [P] [US1] Add `public IReadOnlyCollection<string> ScheduledSchedulerWorkItemIds { get; init; } = [];` with its doc comment to `RuntimeCheckpointCommitStoreResult` in `src/essentials/Workflows/Runtime/Core/Models/RuntimeCheckpointCommitResult.cs`.
- [ ] T016 [P] [US1] Add `bool CanStageSchedulerWork(IWorkflowSchedulerWorkQueue schedulerWorkQueue) => false;` with the C1–C4 doc comment to `src/essentials/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs`.

### Tests for User Story 1 (red before T024–T032)

- [ ] T017 [P] [US1] Validator cases in `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCommitValidatorTests.cs`: a marked id that is not in `PostCommitOutbox`, that names a non-`EnqueueSchedulerWork` intent, whose work item targets another execution, or that repeats, is refused; a valid mark passes.
- [ ] T018 [P] [US1] Create `tests/essentials/Workflows/Runtime/Tests/RuntimeHopCollapseCommitterTests.cs` (stub store and queue): marks every `EnqueueSchedulerWork` entry only when switch on + no session + `CanStageSchedulerWork` true (each false case leaves the commit unmarked and byte-identical to today); other intent kinds never marked; guard fails when pending ∪ scheduled ≠ folded ids or they overlap; `PublishInProcessHopWorkItems` publishes only unmarked intents; `RuntimeCheckpointCommitFingerprint.Compute` is equal for the marked and unmarked form of one commit.
- [ ] T019 [P] [US1] Add the contract cases of `specs/234-hop-chain-collapse/contracts/checkpoint-commit-continuations.md` (marked continuation queued with no outbox row; two continuations in outbox order; rollback leaves nothing; an item already present under the same `WorkItemId` is kept as-is and returned, not overwritten, even when the staged item's content differs, exactly as `EfSchedulerWorkQueueStore.EnqueueAsync`; replay reports scheduled ids and writes nothing; replay after the entry is no longer marked resolves without a replay-conflict exception) to `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/RuntimeCheckpointCommitContract.cs`, run on SQLite by `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfRuntimeCheckpointSchedulerWorkParticipantTests.cs`.
- [ ] T020 [P] [US1] Run the same contract cases on PostgreSQL, SQL Server and MySQL from `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/EfRuntimeCheckpointSchedulerWorkParticipantProviderSmokeTests.cs`.
- [ ] T021 [P] [US1] Refusal cases: a marked commit sent to `InMemoryRuntimeCheckpointCommitStore` throws `NotSupportedException` (new case in `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCommitTests.cs`), and `RuntimeCheckpointFold` refuses to fold a marked commit (`tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointFoldTests.cs`).
- [ ] T022 [P] [US1] Create `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfHopCollapseCrashConvergenceTests.cs` on the two-generation cut-and-recover harness of `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfReplaySafeFusionCapRecoveryTests.cs` (`StartGenerationAsync`, a `CheckpointCommitGate`-style gate, `SweepUntilQuietAsync`, retained-database diagnostics on failure) with kill points K1 (after the commit that wrote a continuation, before its claim) and K2 (inside the transaction after the continuation row is staged, before the marker; inject the failure through a decorated `RuntimeDbContext` save), each with a switch-off control. Assert: same terminal state as an uninterrupted run, the continuation claimed and run once with its committed `WorkItemId`, no duplicate activity attempt or inspection row, queue and outbox settled (research R12).
- [ ] T023 [P] [US1] Create `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfHopCollapseGuardrailTests.cs`: drive each shape on file SQLite with the switch on and off — Sequence of three ReplaySafe leaves, Flowchart multi-outcome branch, Parallel fork-join with a suspending branch resumed by a bookmark, a faulting child under a Sequence, a non-ReplaySafe (External) parent — and assert equal terminal workflow/activity state fingerprints and an equal set of checkpoint commit fingerprints (equal sequence for the straight-line shapes; set equality where parallel interleaving may differ, research R9), plus a deterministic-fingerprint self-check across two OFF runs.

### Implementation for User Story 1

- [ ] T024 [US1] Enforce the data-model rules for `SchedulerWorkInCommit` in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeCheckpointCommitValidator.cs`.
- [ ] T025 [US1] Move the intent-to-work-item reader out of `RuntimeSchedulerPostCommitIntentDispatcher.DeserializeWorkItem` into `public static RuntimeSchedulerWorkItem ReadSchedulerWorkItem(RuntimePostCommitIntent intent)` on `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimePostCommitOutboxItems.cs` (materialized item first, else payload, same exceptions and messages), and call it from `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeSchedulerPostCommitIntentDispatcher.cs`.
- [ ] T026 [US1] In `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfSchedulerWorkQueueStore.cs` make `ToEntity` `internal` and add `internal bool SharesContextWith(RuntimeDbContext other) => ReferenceEquals(context, other);`.
- [ ] T027 [US1] Add `StageScheduledSchedulerWorkAsync(RuntimeDbContext, RuntimeSchedulerWorkItem, string scope, CancellationToken)` to `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointParticipantStaging.cs` next to `StageConsumedSchedulerWorkAsync`: require a caller-owned transaction; validate the item as `EfSchedulerWorkQueueStore` does; keep an existing row read through `EfSchedulerWorkQueueStore.ReadChecked`; otherwise add `EfSchedulerWorkQueueStore.ToEntity(item, scope, id, 1)` (contract C10–C12).
- [ ] T028 [US1] Skip marked entries in `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointOutboxParticipantStaging.cs`.
- [ ] T029 [US1] In `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs`: implement `CanStageSchedulerWork` (`queue is EfSchedulerWorkQueueStore ef && ef.SharesContextWith(context)`); in `CommitNewAsync` stage each marked entry (via `RuntimePostCommitOutboxItems.ReadSchedulerWorkItem`) after the outbox staging and before the consumed-work delete, keeping the marker as the last write; make `PendingOutboxIds` exclude marked ids; make `ResolveReplay`/`ResultFor` report `ScheduledSchedulerWorkItemIds` as the commit's `EnqueueSchedulerWork` outbox ids absent from the marker's pending set (contract C13–C15).
- [ ] T030 [P] [US1] Refuse non-empty `SchedulerWorkInCommit` with `NotSupportedException` in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/InMemoryRuntimeCheckpointCommitStore.cs`, and guard `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Coalescing/RuntimeCheckpointFold.cs` against folding a marked commit (contract C16).
- [ ] T031 [US1] Forward the capability in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs` only when no session applies (to the inner store, with the inner queue), exposing the wrapped queue as `internal IWorkflowSchedulerWorkQueue Inner` on `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Coalescing/CoalescingWorkflowSchedulerWorkQueue.cs`; a marked commit while a session applies throws (contract C3, C16).
- [ ] T032 [US1] In `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeCheckpointCommitter.cs` add optional constructor arguments `IWorkflowSchedulerWorkQueue? schedulerWorkQueue` and `RuntimeHopCollapseOptions? hopCollapseOptions`; after building `postCommitOutbox`, mark entries per contract C5; replace the pending-count guard with the union/disjoint guard (C8); skip marked intents in `PublishInProcessHopWorkItems` (C9). Pass both from the explicit factory in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs` with `GetService`.
- [ ] T033 [US1] Run T017–T023 (green; K1/K2 controls green), `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj` and `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/` in `tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj`. Lower the `Immediate` budgets in `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/ef-command-budgets.json` and `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/ef-commit-budgets.json` to the new counts; `Immediate.CollapseOff` and `Coalesced` stay exactly as before.

**Checkpoint**: US1 is independently shippable behind the switch.

---

## Phase 4: User Story 2 — Routing hops run in-process without their own queue round trips (Priority: P1)

**Goal**: under per-hop persistence the `Start` hop's `WorkflowStarted` item, each leaf's `ParentCompletionEvaluation`
and the workflow tail (the root's `ActivityCompleted` → `ContinuationScheduling` → tail `Checkpoint`, owner decision
2026-10-11) run inside the drain that holds the claimed item, with no queue item, claim or acknowledgement of their
own; everything FR-005 still excludes falls back (FR-004–FR-006).

**Independent Test**: routing hops, tail included, no longer appear as queue rows or claims; `InProcessRoutingSteps`
> 0 with the switch on and 0 off; K3–K6 and K9 converge.

### Tests for User Story 2 (red before T039–T043)

- [ ] T034 [P] [US2] Create `tests/essentials/Workflows/Runtime/Tests/RuntimeRoutingPassTests.cs` covering contract P1–P8 and D1–D3 of `specs/234-hop-chain-collapse/contracts/routing-pass-and-acknowledgement.md`: the gate matrix (switch off, no live-drain scope, session applies, non-claim queue, process-local scheduler evidence → no pass, handlers enqueue); capture of the `WorkflowStarted` item and of a `ParentCompletionEvaluation`; fallback of a child-faulted evaluation and a non-ReplaySafe parent; capture after the boundary enqueues; pause gate closed, terminal status, no accepting handler and a throwing routed step each enqueue the routed item before the claimed item is acknowledged; cancellation and claim loss propagate; the claimed item is neither acknowledged nor released while the pass is open; counters.
- [ ] T035 [US2] Add tail-pass cases to `tests/essentials/Workflows/Runtime/Tests/RuntimeRoutingPassTests.cs` (after T034, same file): a claimed root `ActivityCompleted` captures `ContinuationScheduling`, which captures the tail `Checkpoint`; the tail checkpoint commit is the pass's boundary and consumes the root `ActivityCompleted` item; a terminal status after `WorkflowCompleted` ends the pass with nothing enqueued; with the switch off both tail items are enqueued exactly as today; the fusion driver's classification still treats the tail as a fallback.
- [ ] T036 [P] [US2] Add kill points K3 (`Start` dispatched, `WorkflowStarted` item captured, before its commit), K4 (leaf `ActivityCompleted` dispatched, `ParentCompletionEvaluation` captured, before the parent's first commit), K5 (inside the routed evaluation after its `ActivityAttemptClaimed` commit, before its scheduling commit) and K6 (after the pass's boundary commit, before the drainer returns) to `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfHopCollapseCrashConvergenceTests.cs`, each with a switch-off control and the R12 assertions.
- [ ] T037 [US2] Add kill point K9 to `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfHopCollapseCrashConvergenceTests.cs` (after T036, same file): the root's `ActivityCompleted` dispatched, `ContinuationScheduling` run inline and the tail `Checkpoint` captured, before the tail checkpoint commit. Expected: the root's `ActivityCompleted` is redelivered, the tail is re-derived, `WorkflowCompleted` commits once and the run completes once; run with a switch-off control and the R12 assertions.
- [ ] T038 [P] [US2] Extend `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfHopCollapseGuardrailTests.cs` to read `RuntimeSchedulerDispatchDiagnostics`: ON shows `InProcessRoutingSteps` > 0 for every shape (tail included), no fallback for the tail, and fallbacks for the faulting child and the External parent; OFF shows both counters at 0 and unchanged `Dispatches`.

### Implementation for User Story 2

- [ ] T039 [US2] Add the workflow-tail rule to `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/HopRoutingClassifier.cs`: `IsTailStep(item)` is true for a `CompleteActivity` item whose completion kind is `ContinuationScheduling` and for the `Checkpoint` item `WorkflowCompleteActivitySchedulerWorkHandler.EnqueueCheckpointAsync` derives (reason `RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason`). `RuntimeRoutingPass` uses it; `ReplaySafeFusionDriver` does not (its tail fallback stays, because coalesced tail commits are buffered), so `tests/essentials/Activities/Runtime/Tests/ReplaySafeFusionGuardrailTests.cs` passes unchanged.
- [ ] T040 [US2] Create `internal sealed class RuntimeRoutingPass` in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/RuntimeRoutingPass.cs` (data model: `Open`, `RouteAsync`, `TryTakeNext`, `FallBackAsync`, `Close`; uses `HopRoutingClassifier`, `IWorkflowSchedulerWorkQueue` and `RuntimeSchedulerDispatchDiagnostics`) and register it scoped in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`.
- [ ] T041 [P] [US2] In `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/WorkflowStartSchedulerWorkHandler.cs` take an optional `RuntimeRoutingPass? routingPass` constructor argument and send the `WorkflowStarted` checkpoint item through `RouteAsync` (enqueue when absent).
- [ ] T042 [P] [US2] In `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/WorkflowCompleteActivitySchedulerWorkHandler.cs` take the same optional argument on both public constructors and send the items of `EnqueueParentCompletionEvaluationAsync`, `EnqueueContinuationSchedulingAsync` and `EnqueueCheckpointAsync` through `RouteAsync` (the classifier decides; under per-hop persistence the tail is routed).
- [ ] T043 [US2] In `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/WorkflowSchedulerDrainer.cs` add optional constructor arguments (`RuntimeHopCollapseOptions?`, `IRuntimeLiveDrainDeliveryAccessor?`, `IRuntimeCoalescingSessionAccessor?`, `IEnumerable<IWorkflowDispatchDurabilityEvidence>?`, `RuntimeRoutingPass?`), evaluate the FR-004 gate per dispatch (research R4), and inside the `DispatchWithClaimRenewalAsync` delegate open the pass, run the handler, then dispatch captured items inline (pause gate, `FindHandler`, pipeline dispatcher with the same ambient services, terminal re-check), close at the boundary (`WasConsumedDurably`) and fall back on any stop (contract P3–P5). Leave `RecordDispatch`, the admission charge, the ack decision and the fault arm unchanged. Pass the new arguments from the drainer's explicit factory in `RuntimeCoreServiceCollectionExtensions.cs`.
- [ ] T044 [US2] Run T034–T038 (green, K9 included), `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, `tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`, `tests/essentials/Activities/Sequence/Tests/Elsa.Activities.Sequence.Tests.csproj`, `tests/essentials/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj` and the budget tests in `tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj`; lower the `Immediate` budgets again; `Immediate.CollapseOff` and `Coalesced` unchanged.

**Checkpoint**: US1 + US2 green; routing hops, tail included, gone from the queue for the reference workflows.

---

## Phase 5: User Story 3 — Every acknowledgement rides a commit when one exists (Priority: P2)

**Goal**: on a claim-capable provider, a claimed item whose dispatch commits is deleted in that dispatch's commit
whichever DI scope the committer comes from; a dispatch that commits twice (an `ActivityAttemptClaimed` commit, then
the outcome commit: `InvokeActivity`, parent completion evaluation) deletes its item in the first commit that is not an
attempt claim, so recovery after a crash mid-attempt is unchanged; dispatches without a commit, non-atomic and legacy
providers and the fault arm keep today's separate acknowledgement (FR-008).

**Independent Test**: a synchronous HTTP run of `http-reference` shows separate acknowledgements only for dispatches
that made no commit; K7/K8 converge; non-atomic providers unchanged.

### Tests for User Story 3 (red before T051–T055)

- [ ] T045 [P] [US3] In `tests/essentials/Workflows/Runtime/Tests/RuntimeDrainStepSingleTransactionTests.cs` add: a handler committing through a committer resolved from (a) explicit ambient services and (b) a fresh scope folds the consume and the drainer skips `CompleteClaimAsync`; a dispatch with no commit is acknowledged; a legacy (non-claim) provider is acknowledged; a handler fault after a cross-scope commit records poison without a second acknowledgement; a replaced accessor implementation is never linked; with the switch off every case equals today.
- [ ] T046 [P] [US3] In `tests/essentials/Workflows/Runtime/Tests/RuntimeSchedulerWorkHandlerBaseTests.cs` add: the body's accessor is linked to the staged one when it is a different `RuntimeConsumedSchedulerWorkClaimAccessor`; not linked when the same instance, when the switch is off, or when nothing is staged; the link is removed when the body ends or throws.
- [ ] T047 [P] [US3] In `tests/essentials/Workflows/Runtime/Tests/RuntimeExecutionPipelineDispatchTests.cs` assert `RuntimePipelineWorkspace.ConsumedWorkClaim` is the dispatcher's accessor for both the workflow and the activity pipeline.
- [ ] T048 [P] [US3] In `tests/essentials/Workflows/Runtime/Tests/RuntimeHopCollapseCommitterTests.cs` add: with the switch on an `ActivityAttemptClaimed` commit carries no consume and the dispatch's next commit does; with it off folding is today's (FR-008, research R6).
- [ ] T049 [P] [US3] Add kill points K7 (synchronous HTTP path, inside `InvokeActivity` after `ActivityAttemptClaimed`, before the outcome commit) and K8 (after an outcome commit that consumed its item cross-scope, before the next claim) to `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfHopCollapseCrashConvergenceTests.cs`, each with a switch-off control.
- [ ] T050 [P] [US3] Add `Http_reference_acknowledges_separately_only_dispatches_that_made_no_commit` to `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/EfCommandBudgetTests.cs`: from the `CommitCaptureInterceptor` breakdown of one synchronous request, the separate acknowledgements equal the dispatches that made no checkpoint commit, which is zero once US2's tail routing is in place (every routing pass, the tail included, ends in a commit that consumes its claimed item).

### Implementation for User Story 3

- [ ] T051 [US3] Add `IRuntimeConsumedSchedulerWorkClaimAccessor? ConsumedWorkClaim { get; set; }` with its doc comment to `src/essentials/Workflows/Runtime/Core/Models/RuntimePipelineWorkspace.cs`, and stage it from an optional `IRuntimeConsumedSchedulerWorkClaimAccessor` constructor argument in both branches of `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Pipelines/RuntimeExecutionPipelineDispatcher.cs` (update its registration in `RuntimeCoreServiceCollectionExtensions.cs` if it uses a factory).
- [ ] T052 [US3] Add `internal IDisposable LinkTo(IRuntimeConsumedSchedulerWorkClaimAccessor target)` to `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/RuntimeConsumedSchedulerWorkClaimAccessor.cs`: while linked every member delegates to `target`; self-link is a no-op; linking while this instance has its own open frame throws; disposing unlinks (data model).
- [ ] T053 [US3] In `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/RuntimeSchedulerWorkHandlerBase.cs` make `ExecuteAsync` receive the workspace's `ConsumedWorkClaim` (pipeline path; `null` on the direct path) and, when `RuntimeHopCollapseOptions.Enabled` (resolved from the body's provider) and the body's accessor is a different `RuntimeConsumedSchedulerWorkClaimAccessor`, hold `LinkTo` for the body.
- [ ] T054 [US3] Apply the same link in `ExecuteWithServicesAsync` of `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/WorkflowStartActivitySchedulerWorkHandler.cs`.
- [ ] T055 [US3] In `RuntimeCheckpointCommitter.ResolveConsumedWorkItems` (`src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeCheckpointCommitter.cs`) return no consume for a `RuntimeCheckpointNames.ActivityAttemptClaimed` checkpoint while the switch is on; off, unchanged.
- [ ] T056 [US3] Run T045–T050 (green), `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, `tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`, `tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj` and `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj`; lower the `Immediate` budgets again; `Immediate.CollapseOff` and `Coalesced` unchanged.

**Checkpoint**: all three levers in place behind one switch.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T057 Measure SC-001: record the `http-reference` / `Immediate` durable-commit total and its per-kind breakdown from `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/EfCommandBudgetTests.cs` output, compare with research R11, and assert SC-001: the total is at most 52 with #2586 unchanged (add it as the `http-reference` / `Immediate` entry of `tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/ef-commit-budgets.json`, which the gate enforces). Post the figure and breakdown on #2584; a total above 52 is a failing gate to fix, not a decision to defer.
- [ ] T058 [P] Update `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md`: add an `IRuntimeCheckpointCommitStore` entry (replacing the stale `IRuntimeCheckpointWriter` one) with `CanStageSchedulerWork`, `SchedulerWorkInCommit` and the replay rule; add `CollapseHopChain` to the `WorkflowsRuntimeCheckpointPersistenceFeature` entry; mention the routing counters on the diagnostics entry (§2.22.1 (a), (d)).
- [ ] T059 [P] Document the `CollapseHopChain` setting, its default and the rollback in `src/essentials/Workflows/Runtime/Api/README.md` and, if it lists feature settings, `src/essentials/Workflows/Runtime/Api/EXTENSION_POINTS.md` (§2.22).
- [ ] T060 [P] In `docs/runtime-durable-resumption.md` add crash window A′ (continuation written in the checkpoint transaction: recovered by backlog discovery, covered by K1/K2) and C′ (inside a routing pass, the workflow tail included: the claimed item stays claimed until the boundary, covered by K3–K6 and K9), and the FR-008 attempt-claim rule (K5, K7).
- [ ] T061 [P] Add "Routing pass" (and "In-commit continuation") to `docs/glossary/elsa.md` next to "Workflow drain" (§E6 R5).
- [ ] T062 Bite-proofs with temporary working-tree mutations, each reverted after the run: move the marker write before the scheduler staging in `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs` (K2 must fail); fold the consume into `ActivityAttemptClaimed` in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeCheckpointCommitter.cs` (K5 or K7 must fail); skip the fallback enqueue on a closed pause gate in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/WorkflowSchedulerDrainer.cs` (the T034 pause case must fail); add `SchedulerWorkInCommit` to `src/essentials/Workflows/Runtime/Core/Models/RuntimeCheckpointCommitFingerprint.cs` (the T019 switch-flip replay case must fail); make `RuntimeRoutingPass` treat the tail as a fallback in `src/essentials/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/HopRoutingClassifier.cs` (the T035 tail cases must fail). Record outcomes in the PR evidence comment.
- [ ] T063 Run the affected full projects once: `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, `tests/essentials/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj`, `tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj`, `tests/essentials/Activities/Sequence/Tests/Elsa.Activities.Sequence.Tests.csproj`, `tests/essentials/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj`, `tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj` and `tests/essentials/Architecture/Tests/Elsa.Architecture.Tests.csproj`.
- [ ] T064 Confirm the native-provider CI suites ("EF container suites (runtime)") pass for `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj`, including T020.
- [ ] T065 Run the backend e2e `http/` and runtime suites against a rebuilt Workbench with a fresh database (`e2e-tests/README.md`), switch on (default) and once with `CollapseHopChain: false` in `src/apps/Elsa.Workbench/shells.json` (revert after).
- [ ] T066 Owner-run: the A1 matrix with `e2e-tests/http/Test-RuntimeConcurrencyCorrectness.ps1` for SQLite and PostgreSQL × `-Cadence Immediate`/`Coalesced` × `-Clients 16`/`32` (8 cases, SC-003); post results on #2532 and #2584.
- [ ] T067 Regenerate and verify maps: `dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -p:RestoreLockedMode=true -- all`, then `-- check`; stage every changed file under `docs/maps/` by explicit path, `docs/maps/manifest.json` included.
- [ ] T068 Bookkeeping: comment on #2584 at each state transition (AGENTS.md "Program bookkeeping"); keep `specs/234-hop-chain-collapse/spec.md` in line with what ships (the owner's 2026-10-11 decisions are already in it); set the spec status per `docs/reference/spec-lifecycle.md` when the evidence comment is posted.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (T001–T005)**: none. T004 depends on T003.
- **Foundational (T006–T013)**: after Setup. T011 depends on T004 and T010; T012 on T005 and T006; T013 on T009.
- **US1 (T014–T033)**: after Foundational. T014–T016 first (compile), then tests T017–T023, then T024–T032, then T033.
- **US2 (T034–T044)**: after Foundational; independent of US1 in code. Its full saving needs US3 (the routed
  `ParentCompletionEvaluation` commits from another scope); without US3 it is still correct (contract P6).
- **US3 (T045–T056)**: after Foundational; independent of US1 and US2 in code.
- **Polish (T057–T068)**: after the stories that are in the delivery. T057 needs US1–US3.

### Story order

US1 (P1, MVP) → US2 (P1) → US3 (P2) is the recommended merge order because the budget files are lowered at each
story's checkpoint. US2 and US3 can be built in parallel by two sessions if each rebases its budget changes.

### Within each story

Model members → tests (red) → store/provider → service → wiring → run and lower budgets.

## Parallel Examples

```text
# Setup
T002, T003 in parallel (different projects).

# Foundational
T007, T010, T013 in parallel after T006/T009.

# US1 tests, all in different files
T017 RuntimeCheckpointCommitValidatorTests.cs
T018 RuntimeHopCollapseCommitterTests.cs
T019 RuntimeCheckpointCommitContract.cs + EfRuntimeCheckpointSchedulerWorkParticipantTests.cs
T020 EfRuntimeCheckpointSchedulerWorkParticipantProviderSmokeTests.cs
T021 RuntimeCheckpointCommitTests.cs + RuntimeCheckpointFoldTests.cs
T022 EfHopCollapseCrashConvergenceTests.cs
T023 EfHopCollapseGuardrailTests.cs

# US2
T041 WorkflowStartSchedulerWorkHandler.cs and T042 WorkflowCompleteActivitySchedulerWorkHandler.cs in parallel after T040.

# US3 tests
T045–T050 in parallel (six different files).
```

## Implementation Strategy

1. **MVP**: Setup + Foundational + US1. Ship behind the default-on switch; it removes the continuation enqueue and
   `Delivered` commits (~30 per request) with no change to what is durable or when.
2. **Increment 2**: US2 (routing pass). Re-run the crash suite and the guardrail.
3. **Increment 3**: US3 (acknowledgement link). Then T057 checks SC-001 (≤ 52 with #2586 unchanged).
4. Every increment keeps `Immediate.CollapseOff` equal to the pre-feature counts, which is the rollback guarantee.

## Notes

- T005 inventory (filled by the implementer): file · subject · objective · pinned off / ON variant added.
- Do not commit `.agent-prefs/*.md`, credentials, connection strings or lock-file churn from local restores.
- Task counts: Setup 5, Foundational 8, US1 20, US2 11, US3 12, Polish 12 (68 total).
- Owner decisions of 2026-10-11 (#2584, PR #2639) are applied: tail routing (T035, T037, T039) and SC-001 ≤ 52 (T057);
  SC-003's crash suite is `EfHopCollapseCrashConvergenceTests` with K1–K9; an existing item under the same `WorkItemId`
  is kept as-is (T019, T027; no task adds conflict behaviour); FR-008's attempt-claim rule (T048, T055).
