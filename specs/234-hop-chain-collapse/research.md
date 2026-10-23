# Research: Collapse the per-activity hop chain under per-hop persistence

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

Every path below was read on `origin/main` at `ea450fc83` (2026-10-11). Paths are repository-relative. "Runtime" means
`src/essentials/Workflows/Runtime/`.

## R0 — Where the 106 commits come from (verified against the code)

The #2553 table on [#2584](https://github.com/elsa-workflows/elsa-foundation/issues/2584#issuecomment-6104358279) maps
onto the code as follows. The `http-reference` workflow of the #2569 gate is a `Sequence` of `HttpEndpoint`, a
JavaScript `Set` and `WriteHttpResponse` (`tests/essentials/Activities/Http/IntegrationTests/CommandBudgets/ReferenceWorkflows.cs`).

| Hop (dispatched work item) | Produced by | Commits today |
|---|---|---|
| `Start` | `WorkflowSchedulerCommandRouter.ProcessAsync` (direct enqueue) | enqueue, claim, separate ack (the handler makes no checkpoint) |
| `Checkpoint` (`WorkflowStarted`) | `WorkflowStartSchedulerWorkHandler.HandleAsync` (direct enqueue) | enqueue, claim, checkpoint tx (consume folded, spec 105) |
| `ScheduleActivity` / `StartActivity` / `InvokeActivity` per activity | the previous checkpoint's `EnqueueSchedulerWork` intent (outbox) | outbox enqueue + `Delivered` mark, claim, checkpoint tx(s) |
| `CompleteActivity` (`ActivityCompleted`) | `InvokeActivity`'s completion checkpoint intent (outbox) | outbox enqueue + `Delivered`, claim, separate ack (no checkpoint; it direct-enqueues the next hop) |
| `CompleteActivity` (`ParentCompletionEvaluation`) | `WorkflowCompleteActivitySchedulerWorkHandler.EnqueueParentCompletionEvaluationAsync` (direct enqueue) | enqueue, claim, checkpoint tx(s), **separate ack** (see R6) |
| root `ActivityCompleted` → `ContinuationScheduling` → `Checkpoint` (`WorkflowCompleted`) | direct enqueues in `WorkflowCompleteActivitySchedulerWorkHandler` | the workflow tail: 3 enqueues, 3 claims, 2 separate acks, 1 checkpoint tx |

The 12 "deletes outside a transaction" are: `Start` (1), the three leaf `ActivityCompleted` hops (3), the two tail
routing hops (2), and the three `ParentCompletionEvaluation` plus three `InvokeActivity` dispatches (6), whose
handlers commit through a committer from another DI scope and so never see the drain's pending consume (R6).

## R1 — FR-001: where the continuation is written

**Decision**: The committer keeps building one pending outbox entry per post-commit intent exactly as today
(`RuntimePostCommitOutboxItems.CreatePendingChanges`). When the lever applies (R4), it additionally marks each
`EnqueueSchedulerWork` entry as *written in the commit* through a new change-set member,
`RuntimeCheckpointStateChangeSet.SchedulerWorkInCommit` (the outbox item ids so marked). The EF checkpoint store
stages a `SchedulerWorkItems` row for each marked entry instead of an outbox row, inside the transaction it already
opens in `EfRuntimeCheckpointCommitStore.CommitNewAsync`, through a new
`EfRuntimeCheckpointParticipantStaging.StageScheduledSchedulerWorkAsync` placed next to the existing
`StageConsumedSchedulerWorkAsync`. The work item is read from the intent itself: `RuntimePostCommitIntent.MaterializedSchedulerWorkItem`
when present (spec 109), else the intent payload, deserialized by the same code `RuntimeSchedulerPostCommitIntentDispatcher`
uses (moved to one shared helper). The row is built by `EfSchedulerWorkQueueStore.ToEntity` (made `internal`).

**Rationale**:
- One source of truth for identity, payload and ordering: the bytes the outbox path would have delivered (FR-002).
  The queue's `WorkOrderKey` is derived from `RecordedAt`, `Sequence` and `WorkItemId` (`EfSchedulerWorkQueueStore.WorkOrderKey`),
  not from insertion time, so the order of several continuations is the same as through the outbox by construction.
- The checkpoint store already stages scheduler-queue rows on the shared context (`StageConsumedSchedulerWorkAsync`
  deletes the claimed row in the same transaction, spec 105), and the `SchedulerWorkItemEntity` belongs to the same
  `RuntimeOperationalState` schema family as the checkpoint marker (`Persistence/EntityFrameworkCore/AssemblyInfo.cs`).
  No migration, no new column and no schema-version bump.
- The marker is the final write of the transaction, so the continuation exists if and only if the checkpoint
  committed (US1 AS1/AS2).

**Alternatives considered**:
- *Carry the work items themselves in a new change-set slot.* Rejected: a second copy of the payload has to be proven
  equal to the intent, and the fingerprint question (R2) gets harder.
- *Enqueue through `IWorkflowSchedulerWorkQueue.EnqueueAsync` inside the open transaction.* Rejected: `EnqueueAsync`
  calls `SaveChangesAsync` and detaches, and its unique-key retry assumes an autocommit statement.
- *Insert-then-delete the outbox row in the same transaction.* Rejected: two writes for nothing.

## R2 — Replay fingerprint and the replay result stay transport-neutral

**Decision**: `SchedulerWorkInCommit` is deliberately left out of `RuntimeCheckpointCommitFingerprint.Compute` (which
lists its members explicitly). The `PostCommitOutbox` slot still carries every intent's pending entry, so the
fingerprint of a commit is byte-identical whichever transport carried its continuations, and byte-identical to
today's fingerprint. The marker's `PendingPostCommitWorkIds` records only the outbox rows actually written. On a
replay, `EfRuntimeCheckpointCommitStore.ResolveReplay` reports
`ScheduledSchedulerWorkItemIds` = the commit's `EnqueueSchedulerWork` outbox ids that are absent from the marker's
pending set. The committer's guard becomes: pending ids ∪ scheduled ids = the folded outbox ids, disjoint.

**Rationale**: a redelivered dispatch on a non-atomic path can replay a commit that already committed. If the switch
was flipped between the two attempts (a crash, then a restart with FR-009 off), a transport-sensitive fingerprint would
raise `RuntimeCheckpointReplayConflictException` and poison the item. With the neutral fingerprint the replay resolves
and the continuation, already written by the first attempt, is not delivered twice. Deriving the scheduled set from
the marker needs no change to the marker document (spec 180's stored-shape rules do not apply).

**Alternatives considered**: record the scheduled ids in the marker content (needs a schema-version bump, an upcaster
and spec 182 dormancy until finalization); make the transport part of the fingerprint (replay conflict across a switch
flip).

## R3 — Idempotency of an in-commit continuation (spec edge case)

**Decision**: An existing row with the same `(scope, WorkflowExecutionId, WorkItemId)` identity is kept and nothing is
written, exactly as `EfSchedulerWorkQueueStore.EnqueueAsync` and `InMemoryWorkflowSchedulerWorkQueue.EnqueueAsync` do
today (create-only by identity, `IWorkflowSchedulerWorkQueue.EnqueueAsync` remarks). The row is still read through
`EfSchedulerWorkQueueStore.ReadChecked`, so a damaged row fails the commit.

**Resolved (owner decision, 2026-10-11, OQ-3)**: the planning draft of the spec called a different item under the same
identity "a conflict, exactly as enqueue does today", but enqueue does not compare content; it returns the existing
item. The spec now matches the code: an item already present under the same `WorkItemId` is kept as-is (returned, not
overwritten), and an in-commit write behaves the same way. No task adds conflict behaviour.

A unique-key race inside the transaction rolls the whole checkpoint back. The store's existing reconciliation then
reads the marker: present means replay, absent means the commit failed and the claimed item is redelivered. A race
needs two writers of one execution, which the execution fence staged first in the transaction already refuses.

## R4 — Composition gates (when each lever engages)

**Decision**: one switch, `RuntimeHopCollapseOptions.Enabled` (default `true`), read by every lever. Each lever also
has the composition precondition under which it saves commits; outside it the lever is inert and today's path runs.

| Lever | Engages when (all hold) |
|---|---|
| FR-001 continuations in the commit | switch on; no coalescing session applies to the execution; the commit store reports `CanStageSchedulerWork(queue)` for the queue resolved in the committer's scope (EF: the queue is `EfSchedulerWorkQueueStore` on the same `RuntimeDbContext` instance) |
| FR-004 in-process routing | switch on; a live-drain delivery scope applies to the execution (Immediate, spec 106); no coalescing session applies; the queue reports `SupportsClaimTransitions` and the composition contributes `IWorkflowDispatchDurabilityEvidence` for `Scheduler` at `Durable` level |
| FR-008 acknowledgement in the dispatch's commit | switch on; claim-capable dispatch (the drainer holds a claim); no coalescing session applies |

**Rationale**:
- "Shares the unit of work" (spec assumption) is decided per scope at commit time, not at composition time, so a
  host that replaces `IWorkflowSchedulerWorkQueue` with its own queue on the same database falls back (US1 AS4) and
  never gets rows its queue would not read.
- The coalescing decorators (`CoalescingRuntimeCheckpointCommitStore`, `CoalescingWorkflowSchedulerWorkQueue`) forward
  the capability to their inner store and queue only when no session applies to the execution. A run that authored
  Immediate on a coalescing host therefore gets the levers, and a coalesced run never does (SC-005).
- In-memory compositions register no durable `Scheduler` evidence and the in-memory checkpoint store reports no
  capability, so none of the levers engage there (spec edge case "in-memory runtime stores: behaviour unchanged").
- The operator-facing lever is a new `[ManifestSetting]` on `WorkflowsRuntimeCheckpointPersistenceFeature`
  (`CollapseHopChain`, default `true`), applied before the feature's early return for Immediate, so the rollback needs
  configuration only (FR-009).

## R5 — FR-004: the routing pass

**Decision**: The drainer runs a *routing pass* inside the dispatch of a claimed item when R4's FR-004 gate holds.

1. `WorkflowSchedulerDrainer.DispatchAsync` opens a pass on a new scoped `RuntimeRoutingPass` (drain scope) for the
   claimed item, inside the existing `DispatchWithClaimRenewalAsync` delegate, so the claim keeps renewing for the whole
   pass and the spec-105 consume frame stays open for it.
2. The two handlers that direct-enqueue a non-committing next hop call `RuntimeRoutingPass.RouteAsync(next)` instead of
   `IWorkflowSchedulerWorkQueue.EnqueueAsync(next)`: `WorkflowStartSchedulerWorkHandler.HandleAsync` (the
   `WorkflowStarted` checkpoint item) and the three `Enqueue…Async` methods of
   `WorkflowCompleteActivitySchedulerWorkHandler` (`ParentCompletionEvaluation`, `ContinuationScheduling` and the tail
   `Checkpoint`). `RouteAsync` captures the item when the pass is open and the item is
   routable (R7); otherwise it enqueues it, byte-identical to today.
3. After the handler returns, the drainer dispatches captured items one at a time through the same handler resolution
   (`FindHandler`, custom before fallback) and the same pipeline dispatcher with the same ambient services, checking
   the pause gate and the terminal status before each, as the drain loop does.
4. The pass closes at its first durable boundary: once a commit in the dispatch has consumed the claimed item
   (`IRuntimeConsumedSchedulerWorkClaimAccessor.WasConsumedDurably`), or after the last captured item ran. A
   `RouteAsync` after the pass closed enqueues durably.
5. Any stop other than success (pause gate closed, terminal status, a captured item no handler accepts, an exception
   from a routed step other than cancellation or claim loss) enqueues the routed item durably (create-only) before the
   originating dispatch is acknowledged, then lets the drain loop dispatch it discretely. The routed step's fault is then
   handled by today's fault arm against today's work item, so poison and incident outcomes do not change.

**Rationale**: the claimed item that started the pass stays in the queue, claimed and renewed, until the boundary
commit deletes it (claim-capable) or the drainer acknowledges it after that commit (otherwise). A crash anywhere before
the boundary leaves it claimable once the claim lapses; the sweep redelivers it, the handler re-derives the same routed
item (identities come from `RuntimeChainId.Derive` over the claimed item's identities), and the pass runs again
(FR-006). This is the spec-228 D2 pump's discipline ("a stop leaves the item queued"), applied without a coalescing
session: under per-hop persistence every commit the routed steps make is still durable as it happens (FR-007).

**Re-derivation determinism**: routed items get fresh `EnqueuedAt`/`RecordedAt` when re-derived. No routed handler
reads those two fields into durable content: the only readers of a work item's timestamps in the work handlers are
`WorkflowResumeBookmarkSchedulerWorkHandler` (bookmark resume, not routed), `WorkflowRetryActivityBoundarySchedulerWorkHandler`
(not routed) and the `Start` handler, whose `now` reaches only the `WorkflowStarted` commit that a pre-boundary crash
never committed. The tail's routed items (`ContinuationScheduling` and the tail `Checkpoint`, routed since the owner
decision of 2026-10-11) carry no timestamp into durable content either: `WorkflowCompleteActivitySchedulerWorkHandler`
builds the tail checkpoint payload from names, activity ids and the reason only, and `WorkflowCheckpointSchedulerWorkHandler`
reads neither timestamp. Kill points K4 and K9 in the crash suite prove convergence anyway.

**Alternatives considered**: a queue decorator that swallows enqueues (implicit, would capture work no rule names);
re-running the spec-228 pump under Immediate (its peek/consume pair belongs to the coalescing overlay queue); routing
`ScheduleActivity` too (commits, not a routing step, and its claim is out of scope per spec finding 6).

## R6 — FR-008: the DI-scope split (confirmed by reading the code)

**Finding**: `RuntimeConsumedSchedulerWorkClaimAccessor` is registered scoped
(`Elsa.Workflows.Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`, "spec 105: scoped transport shared by
the same-scope drainer and checkpoint committer"). `RuntimeSchedulerWorkHandlerBase<TPayload>.ExecuteAsync` runs the
handler body against `RuntimePipelineWorkspace.AmbientServices` when the drain carries them, else in a fresh scope
from `IServiceScopeFactory.CreateAsyncScopeAsync(persistenceScope)`. `WorkflowStartActivitySchedulerWorkHandler.ExecuteWithServicesAsync`
does the same. The only caller that sets ambient services is the synchronous HTTP endpoint
(`src/essentials/Activities/Http/Middleware/HttpEndpointMiddleware.cs`, `new WorkflowExecutionCommandDispatchOptions(context.RequestServices)`).
`WorkflowInvokeActivitySchedulerWorkHandler` and `WorkflowParentActivityCompletionSchedulerWorkHandler`
(`src/essentials/Activities/Runtime/Elsa.Activities.Runtime/Services/`) resolve `RuntimeCheckpointCommitter` from that
provider. Either way the committer's accessor is another instance with no pending consume, so spec 105's fold never
happens for these handlers and the drainer acknowledges separately. The finding on #2584 is confirmed for both the
HTTP and the non-HTTP case.

**Decision**: the pipeline dispatcher (`RuntimeExecutionPipelineDispatcher`, drain scope) stages the drain's accessor
on a new `RuntimePipelineWorkspace.ConsumedWorkClaim` property. When the FR-008 gate holds and the handler's provider
resolves a different `RuntimeConsumedSchedulerWorkClaimAccessor` instance, the handler base (and
`WorkflowStartActivitySchedulerWorkHandler`) links it to the drain's for the duration of the body
(`RuntimeConsumedSchedulerWorkClaimAccessor.LinkTo(IRuntimeConsumedSchedulerWorkClaimAccessor)`, internal, returns a
disposable). A linked accessor delegates every member to the drain's, so the committer stages the consume, the store
deletes the row in the handler's own transaction, and `MarkConsumedDurably` and `BeginConsumeAttempt` land where the
drainer and its renewal loop read them (#1254 keeps working). A replaced accessor implementation is not linked and keeps
today's separate acknowledgement. Claim identity still travels by explicit threading, not an AsyncLocal (spec 105 FR-003).

**Crash-safety refinement (decided by the owner, 2026-10-11, OQ-4; now part of FR-008)**: `InvokeActivity` and `ParentCompletionEvaluation` commit more than
once per dispatch. Their first commit is `ActivityAttemptClaimed` (`ActivityAttemptActivationClaimer`, checkpoint name
`RuntimeCheckpointNames.ActivityAttemptClaimed`), made before the activity or structural callback runs. Today their
work item survives that window, so a crash mid-attempt is recovered by redelivering the item. Folding the consume into
the `ActivityAttemptClaimed` commit would move recovery to the lease/interruption path instead, which is a durability
change FR-007 forbids. While the switch is on, `RuntimeCheckpointCommitter.ResolveConsumedWorkItems` therefore does
not fold into an `ActivityAttemptClaimed` commit; the consume rides the dispatch's next commit (its outcome commit).
Off, `ResolveConsumedWorkItems` is unchanged. No same-scope handler makes an `ActivityAttemptClaimed` commit today, so
the rule changes nothing that folds today.

## R7 — "Routable in-process" (FR-005, reconciled with spec 228)

**Decision**: the classification moves out of `ReplaySafeFusionDriver.ClassifyCompletionAsync` into a shared internal
`HopRoutingClassifier` (`Elsa.Workflows.Runtime/Services/WorkHandlers/HopRoutingClassifier.cs`) that both the fusion
driver and `RuntimeRoutingPass` call. No routing rule is new except the ones the spec names explicitly: the Start hop
and, under per-hop persistence only, the workflow tail (owner decision, 2026-10-11, OQ-1). The fusion driver keeps its
tail fallback, because under coalescing the tail's commits are buffered.

| Routed candidate | Rule | Source |
|---|---|---|
| `Checkpoint` item carrying `WorkflowStarted`, derived by the `Start` handler | routable | FR-004 "and the Start hop" |
| `CompleteActivity` / `ParentCompletionEvaluation` | routable unless the work item carries `RuntimeMetadataKeys.ChildFaulted = true`, or the parent node's `ActivityContract.SideEffectProfile` is not `ReplaySafe` | spec 228 `ClassifyCompletionAsync` |
| `CompleteActivity` / `ContinuationScheduling` (derived from the root's `ActivityCompleted`), and the tail `Checkpoint` it derives (`ActivityCompleted`/`WorkflowCompleted`) | routable by `RuntimeRoutingPass` (the workflow tail); still a fallback for the spec-228 fusion pump | FR-004, FR-005 as amended 2026-10-11 |
| any `ScheduleActivity` (join or not) | never captured: it is a committed continuation, not a routing step, so a multi-predecessor join edge always takes today's path | FR-005 "multi-predecessor join" |
| anything else | fallback | FR-005 "any case the existing fusion rules exclude" |

## R8 — Spec reconciliation

- **Spec 105** (FR-004/FR-006): the atomic consume path is reused unchanged; FR-008 widens *who* can reach the pending
  consume (R6). Non-atomic and legacy providers, dispatches with no commit, and the handler-fault arm keep the separate
  acknowledgement and the RT-2 tripwire, by the R4 gate and by leaving `DispatchAsync`'s fault arm untouched.
- **Spec 106** (live-drain delivery): an in-commit continuation never enters the outbox, so the orchestrator's
  in-memory delivery step finds nothing for it; the drainer's own loop claims it on its next `AcquireNextAsync`. The
  #2225 settlement logic still covers outbox kinds. The live-drain scope is the FR-004 engagement signal.
- **Spec 109** (in-process hop fast path): `PublishInProcessHopWorkItems` skips intents written in the commit (nothing
  will deliver them). Their materialized item is still used, by the store, to build the row (R1).
- **Spec 228** (fusion): unchanged under coalescing; the classifier is shared (R7); the levers never engage while a
  session applies (R4).
- **ADR 0020**: confirmed reading recorded as a dated clarification in `docs/adr/0020-runtime-checkpoint-commit-post-commit-work.md`.
- **ADR 0031 (a)**: queue idempotency by `WorkItemId` holds for in-commit rows (R3).

## R9 — Dispatch order with continuations in the commit

**Finding**: an in-commit continuation is claimable the moment the checkpoint commits, while an outbox continuation
entered the queue only after the drainer ran dry and the orchestrator delivered it. Claim order is by `WorkOrderKey`,
so with parallel branches queued the drainer can now claim a continuation before a sibling it would previously have
claimed first. Durable outcomes do not depend on that interleaving (each hop is idempotent and fenced), but the
interleaving itself can differ with the switch on. With the switch off the order is today's exactly (SC-004). The
`fork-join-resume` reference workflow and the guardrail's parallel shape cover it.

## R10 — Counting durable commits deterministically (SC-001)

**Decision**: add `CommitCaptureInterceptor` next to `CommandCaptureInterceptor` in
`tests/essentials/Persistence/EntityFramework/Tests/`. It counts (a) every committed transaction
(`DbTransactionInterceptor.TransactionCommitted`/`TransactionCommittedAsync`, which EF raises for its own
`SaveChanges` transactions too), and (b) every write command (non-query, or a reader whose text starts with `INSERT`,
`UPDATE` or `DELETE`) executed with no transaction (autocommit). The #2569 test records the count per run next to the
command count and checks it against `ef-commit-budgets.json`.

**Rationale**: it is the same unit #2553 counted with fsyncs (one WAL fsync per committed write unit on SQLite), but
deterministic, CI-runnable and timer-free (ADR 0073 D7 forbids timing; ADR 0080 D2 allows deterministic counts as
correctness gates). The fsync shim stays an optional manual cross-check in [quickstart.md](quickstart.md).

## R11 — Projected commit count and the SC-001 target

Applying FR-001, FR-004 (including the workflow tail, owner decision 2026-10-11) and FR-008 to R0, with the root-write
lease unchanged:

| Kind | Today | Target | Why |
|---|---:|---:|---|
| Checkpoint transactions | 22 | 22 | FR-007: every checkpoint still commits |
| Work-item enqueues | 22 | 1 | −15 in the commit (FR-001); −6 routed (Start→`WorkflowStarted`, three `ParentCompletionEvaluation`, `ContinuationScheduling`, the tail `Checkpoint`); the router's `Start` remains |
| Work-item claims | 22 | 16 | −6 routed items |
| Separate acknowledgements | 12 | 0 | `Start`, the three leaf `ActivityCompleted` and the root `ActivityCompleted` fold into their pass's boundary commit; three `ParentCompletionEvaluation` and `ContinuationScheduling` disappear; three `InvokeActivity` fold (FR-008) |
| Outbox `Delivered` | 15 | 0 | FR-001 |
| Root-write lease | 8 | 8 | #2586, out of scope |
| Liveness / placement | ~5 | ~5 | out of scope |
| **Total** | **~106** | **~52** | SC-001: ≤ 52 with #2586 unchanged |

Without the tail the figure would be ~58; with #2586 as well it is ~44, under SC-001's second bound of 45. The largest
remaining block is the 16 claims of committed continuations, left for a follow-up spec. The projection is arithmetic
over R0; T004 measures the real per-kind baseline before any lever lands, and T057 checks the result against ≤ 52.

## R12 — The crash suite named in SC-003

**Finding (resolved by the owner, 2026-10-11, OQ-2; SC-003 now names this suite)**: the Groundwork crash suite named in
the planning draft of SC-003 no longer exists. ADR 0073 made EF Core the only first-party
persistence family; Groundwork is "a migration source and historical evidence source" and
`docs/program-goals/groundwork-persistence-readiness.md` is a historical record. Spec 228's corrective plan already
states that "historical Groundwork paths ... are provenance, not commands to resurrect". The live equivalent is the EF
two-generation cut-and-recover harness in
`tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfReplaySafeFusionCapRecoveryTests.cs`
(`RunCutAndRecoverAsync`, `StartGenerationAsync`, `CheckpointCommitGate`, `SweepUntilQuietAsync`): a source host on a
file SQLite database is held at a gate and abandoned, and a fresh host on the same file recovers through the
resumption sweep.

**Decision**: the SC-003 crash suite is a new `EfHopCollapseCrashConvergenceTests` built on that harness, with these
kill points (each also run with the switch off as its control):

| Id | Kill point | Expected recovery |
|---|---|---|
| K1 | After a checkpoint transaction carrying an in-commit continuation commits, before the drain claims the continuation | sweep claims the continuation once; same `WorkItemId`; run completes once (US1 AS3) |
| K2 | Inside the checkpoint transaction after the continuation row is staged, before the marker write | rollback: no checkpoint, no continuation; the claimed source item is redelivered (US1 AS2) |
| K3 | `Start` dispatched and its `WorkflowStarted` checkpoint item captured, before the `WorkflowStarted` commit | `Start` redelivered, re-derived, completes once |
| K4 | Leaf `ActivityCompleted` dispatched and `ParentCompletionEvaluation` captured, before the parent's first commit | `ActivityCompleted` redelivered; parent evaluated once; next child scheduled once |
| K5 | Inside the routed `ParentCompletionEvaluation`, after its `ActivityAttemptClaimed` commit, before its scheduling commit | `ActivityCompleted` still claimed (R6 refinement), redelivered; parent attempt recovered exactly as today |
| K6 | After the routing pass's boundary commit (originating item consumed in it), before the drainer returns | nothing redelivered; the in-commit continuation (K1 shape) carries the run |
| K7 | Inside `InvokeActivity` after `ActivityAttemptClaimed`, before the outcome commit, on the synchronous HTTP path (FR-008 cross-scope) | the `InvokeActivity` item survives and is redelivered; outcome identical to today's redelivery |
| K8 | After an `InvokeActivity` outcome commit that consumed its item cross-scope, before the drainer's next claim | no separate acknowledgement was needed; no duplicate activity execution |
| K9 | Tail pass: the root's `ActivityCompleted` dispatched, `ContinuationScheduling` run inline and the tail `Checkpoint` captured, before the tail checkpoint commit | the root's `ActivityCompleted` redelivered; the tail re-derived; `WorkflowCompleted` committed once; the run completes once |

Every case asserts: the same terminal state as an uninterrupted run, no duplicated activity execution (attempt counts
and inspection rows), no lost continuation, and a settled queue and outbox afterwards.

## R13 — Where the SC-003 A1 cases run

`e2e-tests/http/Test-RuntimeConcurrencyCorrectness.ps1` (#2532): SQLite and PostgreSQL × Immediate and Coalesced ×
16 and 32 clients = 8 cases, run against an owner-started Workbench (spec 200's quickstart names the Azure runner). The
script needs no change; the switch defaults on. It is an owner-run step, recorded on #2532 and #2584.
