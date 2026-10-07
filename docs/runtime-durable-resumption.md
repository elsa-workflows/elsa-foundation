# Durable resumption of workflow execution (PS-2 / RT-3)

> **Audience:** engineers and architects working in `elsa-foundation`.
> **Purpose:** make the difference between *durable storage* and *durable resumption* explicit, and
> document exactly which crash windows the runtime recovers from today, which one it does not, and
> why. This is the worked reference behind roadmap unit **W2** of the Elsa 4 review remediation
> program (findings **PS-2** and **RT-3**).
> **Knowledge role:** worked reference. Canonical short definitions live in
> [`docs/glossary/elsa.md`](glossary/elsa.md); the extension-point contracts live in
> [`src/essentials/Workflows/Runtime/EXTENSION_POINTS.md`](../src/essentials/Workflows/Runtime/EXTENSION_POINTS.md)
> and [`src/essentials/Workflows/Runtime/Resumption/EXTENSION_POINTS.md`](../src/essentials/Workflows/Runtime/Resumption/EXTENSION_POINTS.md).

## Durable storage is not durable resumption

A workflow makes progress through a repeating cycle:

1. An execution agent accepts a command and **commits a checkpoint** — the durability boundary. The
   checkpoint records new state *and* the follow-up work the commit implies as **post-commit outbox**
   items. Scheduler continuation uses `EnqueueSchedulerWork`; modules may contribute other stable kinds.
2. The post-commit outbox is **delivered**: the ordinal keyed runtime dispatcher selects the
   contributed handler for each item. The built-in `EnqueueSchedulerWork` handler enqueues a
   `RuntimeSchedulerWorkItem` into the **scheduler work queue**; other cross-execution handlers run
   through the same global delivery path outside workflow execution actor mailboxes.
3. The scheduler **drains** the queued work — running activities, scheduling children, and producing
   the next checkpoint — and the cycle repeats.

Before W2, every store that backs step 1 could be durable and survive a crash, yet the
runtime still lost work: the durable registration swapped eleven state contracts but **not**
`IWorkflowSchedulerWorkQueue`, so delivered work landed in a process-local in-memory queue that died
with the process (**PS-2**). And nothing ever *ran* a recovery pass: the system-wide outbox sweep and
`IRuntimeRecoveryScanner` were registered but never invoked, so an item stranded between commit and
delivery — or a `FailedRetryable` outbox item with a future `AvailableAt` — waited forever for an
unrelated command to arrive (**RT-3**).

**Durable storage** means the state survives the crash. **Durable resumption** means something
*re-drives* that state to completion after the crash. W2 adds the second half.

## What W2 adds

- **A durable scheduler work queue** — `EfSchedulerWorkQueueStore`, an `IDocumentStore`-backed
  bridge (document kind `schedulerWorkItem`) swapped in by `AddRuntimeEntityFrameworkCore`. Enqueue is
  idempotent by `(WorkflowExecutionId, WorkItemId)`; listing/dequeue are FIFO by
  `(RecordedAt, Sequence, WorkItemId)`; dequeue is load-first-then-delete.
- **Backlog discovery** — an additive contract method
  `IWorkflowSchedulerWorkQueue.ListClaimableWorkflowExecutionIdsAsync(RuntimeSchedulerClaimableBacklogQuery)`
  returns, in ordinal order after an exclusive bound, the distinct execution ids whose queued work a claim
  would serve right now (#2188). After a restart, nothing else knows which executions were interrupted;
  this is how the sweep finds them. Both the in-memory and EF Core queues implement it and are held to one
  contract. (`ListPendingWorkflowExecutionIdsAsync(int limit)`, the original method, still lists every
  execution with any queued work; the sweep falls back to it only for a queue that does not implement
  claimable discovery.)
- **A resumption sweep service** — `IRuntimeResumptionService` (`RuntimeResumptionService`). One
  `SweepAsync` pass:
  1. **Re-delivers** stranded post-commit outbox items **system-wide**
     (`ProcessAsync(workflowExecutionId: null, intentKind: null)`) across every contributed intent
     kind, including due `FailedRetryable` retries — this closes RT-3. Normal per-execution draining
     remains intentionally filtered to `EnqueueSchedulerWork`, so non-local cross-execution work is
     never executed inside that workflow's actor mailbox. The claim takes every claimable item, a live
     drain's own continuations included; the drain accounts for that before it reports quiescence (see
     [A live drain's continuation in the sweep's hands](#a-live-drains-continuation-in-the-sweeps-hands-2225)).
  2. **Discovers** the interrupted executions: the union of the durable queue backlog
     (`ListClaimableWorkflowExecutionIdsAsync`) and `IRuntimeRecoveryScanner` candidates.
  3. **Re-drives** each execution by enqueueing a `RunSchedulerWork` command envelope **through the
     agent mailbox** — *not* by draining from the sweep. Re-driving through the mailbox preserves the
     single-writer discipline (RT-2): the agent remains the only writer for its execution.
- **A feature-gated pump** — `Elsa.Workflows.Runtime.Resumption` is a separate package whose
  `WorkflowsRuntimeResumptionFeature` registers the service and a `RuntimeResumptionPumpTask`
  (`IRecurringTask`, scheduled by the Tasks domain). The durable persistence features declare
  `DependsOn = ["WorkflowsRuntimeResumption"]`, so **selecting durable stores pulls the pump into the
  shell** — the "durable stores ⇒ pump available" invariant is machine-visible in the feature catalog.
  The runtime API feature is deliberately untouched (the pump is opt-in with durable storage).

## The idempotency / durability contract — read this carefully

The durability guarantee is **at-least-once on both sides**, and the redrive-safe drain (#412 item 3)
is what made the dequeue side match the enqueue side:

- **Enqueue side — at-least-once.** The post-commit outbox can be redelivered (by the drain
  coordinator during normal execution, or by the resumption sweep after a crash). Redelivery can
  enqueue the same work twice, so the queue **absorbs duplicates**: enqueue is idempotent by
  `(WorkflowExecutionId, WorkItemId)`, and re-drive envelopes carry a fresh per-sweep idempotency key
  while the underlying work items stay single-instance. An execution whose backlog remains (e.g. a
  dispatch that raced a crash) is simply re-driven on the next sweep. This is why re-running the sweep
  is always safe.
- **Dequeue side — at-least-once (redrive-safe ack).** The drainer no longer destructively dequeues
  before dispatch. `WorkflowSchedulerDrainer` peeks the head, dispatches it in place, and only then
  ack-deletes it from the durable queue — **after** the consuming handler's effect is durable (a
  successful commit, or, on a *handler fault*, before the poison record / RetryNow re-enqueue). A
  process crash before the ack leaves the source item durably queued, so backlog discovery re-drives
  it and the handler re-runs idempotently (activity-execution status guards + deterministic follow-up
  work-item ids the idempotent queue absorbs). The underlying `IWorkflowSchedulerWorkQueue.DequeueAsync`
  contract is unchanged (still load-first-then-delete); the ack is simply moved to *after* the durable
  effect. (`EfSchedulerWorkQueueStore`'s own dequeue is likewise still crash-safe by
  redelivery.)

With both sides at-least-once and every consumer idempotent, re-running the sweep is always safe and
no crash window strands an activity.

## Claims that outlive their lease (#2195)

The outbox and the durable timer pump each claim a batch of rows under one visibility timeout (one minute by
default) and then work through the batch one row at a time. The claims at the end of a long batch can lapse, and a
peer re-claims them. Both deliverers therefore follow one shared rule, `FencedClaimLease`: **a claim is renewed
immediately before its side effect, and a claim that cannot be renewed is never acted on.** The renewal is a
compare-and-set on the claim's owner and fence, so a re-claimed row is skipped rather than delivered a second time.

- **Outbox.** `RuntimePostCommitOutboxProcessor` renews each item's claim just before dispatching it. It does not
  renew during the dispatch, because intent handlers share the sweep's persistence context; a single dispatch that
  alone outlives the timeout can still be repeated by a peer. Each intent kind converges under that repeat by its own
  mechanism: scheduler work by work-item id while the item is still queued, a child start by its deterministic child
  id, a resume by consuming its bookmark once, a cancel by being idempotent, and a PublishStimulus start by its keyed
  execution id (see [Stimulus START idempotency](serialization.md#stimulus-start-idempotency-is-at-least-once)). The
  per-kind table under `IRuntimePostCommitOutboxStore` in the Runtime `EXTENSION_POINTS.md` records each mechanism and
  its limit: a scheduler-work repeat that arrives after its whole chain was drained can re-run a checkpoint whose
  replay conflicts, the same exposure redelivery after a crash already had.
- **Durable timers.** `DurableTimerPumpTask` renews each timer's claim just before firing it and keeps it renewed
  while the fire runs; a renewal that fails cancels the fire.
- **One lost claim never ends a sweep.** A row whose claim was lost before its side effect, during it, or at its
  fenced completion is logged (`RuntimePostCommitClaimLost` for the outbox) and skipped, and the sweep moves on to
  its next row, then to backlog discovery and re-drive. The claimant that holds the row records its outcome.
- **Test-scope cleanup is isolated from the sweep.** It runs first on every resumption tick, in an operation scope of
  its own. A failure there, such as a cleanup race or a scope row that cannot be read, is logged
  (`WorkflowTestScopeCleanupFailed`) and retried next tick; it no longer stops outbox delivery or re-drive.

The same claim, renew and fenced-complete shape appears in other loops that were not moved onto the helper:
scheduler work claims one item at a time and dispatches it at once, and its renewal carries drain-specific rules (a
dispatch deadline, and a claim its own checkpoint consumes); alteration jobs and transport items are fenced inside
the checkpoint commit itself.

## A live drain's continuation in the sweep's hands (#2225)

The sweep claims across every execution, so it can claim a continuation a live drain has just committed,
before the drain's own delivery step reaches it. The drain's step then finds nothing deliverable, or reports
the item superseded (#1798). The drain used to treat that as quiescence and return. Nothing was lost, since
the sweep delivered the item and re-drove the execution, but the command answered before its own next step
had run: a start returned Accepted before its first bookmark existed, a resume before its workflow completed.
The fixture-host evidence tests hit this in CI.

The drain now treats such a continuation as still its own. `WorkflowDrainOrchestrator` reports `Quiesced`
only once no other deliverer holds one of the execution's `EnqueueSchedulerWork` items and no work they
queued is left undrained:

- **Held by another deliverer.** The drain lists the execution's claimed continuations
  (`IRuntimePostCommitOutboxClaimStore.ListClaimedAsync`) and waits for those deliveries to finish, polling
  with backoff from 10 ms to a 500 ms cap. Then it drains the work they queued: it still holds the execution's
  ownership lease, so nothing else would drain that work before the command returned.
- **The other deliverer died.** Its claim lapses after the processor's visibility timeout
  (`RuntimePostCommitOutboxProcessing.ClaimVisibilityTimeout`, one minute). The drain never sleeps past the
  earliest lapse; it then claims the item through the durable claim path and delivers it itself, and a
  claimant that was only slow finds its renewal refused and skips the item.
- **Already delivered.** A sweep that finished the whole delivery before the drain's read leaves only queued
  work, so after a scheduler drain that ran items and stopped neither on a terminal status nor at its
  work-item budget the drain also checks its execution's queue.
- **The other deliverer failed it.** The listing also returns the execution's `FailedRetryable`
  continuations, so an attempt that failed and awaits a retry is seen even when it failed before the drain's
  first read. An awaited item that ended failed is found by looking it up once it leaves the listing. A failed
  attempt is not known to have queued anything, so the drain stops with `OutboxDeliveryFailed`, the status a
  failed delivery of its own produces, and the command answers `AcceptedButFaulted` rather than `Accepted`. It
  does not wait for the retry, which stays with the sweep (see *Retried continuations* below).
- **Bounded.** `WorkflowDrainOrchestratorOptions.ContinuationClaimWaitLimit` bounds all of a drain request's
  waiting: one deadline, set at the first wait and shared by every later one, so the drain's 64 cycles cannot
  multiply it. It defaults to the claim visibility timeout plus `ContinuationClaimWaitMargin`, 90 seconds.
  When it passes with a continuation still held, the drain stops with `OutboxDeliveryFailed` as above. The
  item stays with its claimant and then the sweep. Cancellation and a lost lease end the wait like any other
  drain step.

**Continuations go first in a sweep batch.** `RuntimePostCommitOutboxProcessor` dispatches a claimed batch's
`EnqueueSchedulerWork` items before every other kind, each part in claim order. A continuation only enqueues
work, but another kind can need an execution's mailbox: a `PublishStimulus` start or resume, or a
DispatchWorkflow parent resume, both reach `agent.EnqueueAsync`. A drain waiting for its continuation holds
its execution's mailbox, so in claim order a batch holding such an item ahead of that continuation would wait
on the drain while the drain waited on it, until the claim lapsed. Each item is still renewed immediately
before its own dispatch (#2195).

**Retried continuations.** `EnqueueSchedulerWork` carries a bounded retry policy,
`RuntimeSchedulerPostCommitIntentDispatcher.RetryPolicy`: four attempts one second apart, the shape and numbers
of a `PublishStimulus` send and of a DispatchWorkflow child start at its defaults. It used to carry none, so one
transient enqueue failure, a database blip, made the continuation `FailedFinal` and left the workflow stuck.

- **A failed attempt waits for its retry.** Every failed attempt but the last is recorded `FailedRetryable`,
  available again once the delay has passed. The drain that failed its own delivery stops with
  `OutboxDeliveryFailed`, as before. A sweep claims the item once it is due, delivers it, and re-drives the
  execution in the same pass, so the workflow goes on at the first sweep after the error clears, provided an
  attempt is left by then. One sweeper re-attempts the item at most once per pass, so on a single node at the
  default ten-second interval the error has to clear within about three passes.
- **The drain sees a failure on another deliverer.** Because the failure is `FailedRetryable`, the drain's
  listing returns it whenever it happened, before the drain's first read included, and the command answers
  `AcceptedButFaulted` rather than a false `Accepted`.
- **Retries converge.** The enqueue is one create-only write keyed by the work item's id. An attempt that
  failed before that write committed queued nothing, so its retry cannot repeat drained work and stays clear
  of the late-repeat window tracked in elsa-workflows/elsa-foundation#2232. A write that committed but whose
  acknowledgement was lost did queue it; its retry dedupes while the item is queued and falls in that window
  once it was drained, as redelivery after a crash between dispatch and completion already could.
- **Exhausted.** The last attempt the policy allows makes the item `FailedFinal`, logged as
  `RuntimePostCommitDeliveryFailedFinal` (68103) and counted in the sweep's failed outbox deliveries. Nothing
  claims it again. This kind projects no incident and no poison record on a final failure, before this change
  or after it.

**One case stays open.** `FailedFinal` items are not listed. A continuation whose attempts were all made, and
all failed, by other deliverers before the drain's first read is therefore not seen, and the drain still
reports `Quiesced`. The attempts are at least the retry delay apart, so that needs a drain whose first read
comes more than three retry delays (three seconds) after the commit that recorded the continuation, with
another deliverer re-attempting the item as each delay passes. Listing terminal failures is not the fix: they
stay in the outbox, so every later drain of the execution would report `OutboxDeliveryFailed`, and skip
incident strategy resolution, for good. Telling this drain's failure from an older one needs either a clock
comparison (a continuation's recorded time is not always the drain's: a retry boundary records its source work
item's time) or a read of the execution's failed continuations at the start of every drain.

**Synchronous HTTP endpoints.** A synchronous `HttpEndpoint` dispatch drains inline, bounded by the endpoint's
`RequestTimeout`. Under contention the request can therefore wait for its continuation. With continuations
first in a sweep batch that wait is short: the sweep reaches the continuation right after the continuations
claimed before it, each an enqueue. It is long only when the claimant died or stalled. Then the drain waits
up to the claim lapse, and a `RequestTimeout` shorter than that ends the request with the endpoint's timeout
status (408 by default) while the workflow goes on through the sweep. An endpoint without a `RequestTimeout`
waits at most the drain's wait limit.

The sweep itself is unchanged, so crash recovery stays at the sweep interval. The cost is latency under
contention, and two reads for a drain that quiesces: `ListClaimedAsync`, and the one-item queue read after a
scheduler drain that ran items.

The interleavings are pinned on the in-memory stores, SQLite and PostgreSQL by
`LiveDrainSweepContentionContract`, which runs the real sweep at the drain's read, and a claimant that delivers
late, dies, or stays stuck. Its retry scenarios run under the registered retry policy: a transient failure of
the drain's own delivery, delivered by the sweep after the delay; one on the sweep before the drain's first
read, answered `AcceptedButFaulted` and then delivered; and every attempt failing, until `FailedFinal`. The
orchestrator-level outcomes are in `WorkflowDrainContinuationSettlementTests`: cancellation, failed attempts
before and during the wait, the queue-read skip rules, the backoff, the lapse and the per-request deadline on a
fake clock, and a sweep batch holding a mailbox-needing item ahead of the drain's continuation.
`RuntimePostCommitOutboxProcessorTests` pins the batch order and its renewals.

## Crash windows

Three recoverable windows. Each is covered by the tests named under it.

### Window A — after checkpoint commit, before outbox delivery *(recovered)*

The checkpoint is durable; the outbox row is durable and `Pending`; the scheduler work was never
enqueued. On restart, the sweep's **outbox re-delivery** step delivers the row, enqueues the work, and
re-drives. ✅ Nothing holds the row back for the dead drain: the sweep claims a continuation whether or not
its execution has a live drain, so recovery takes one sweep interval (10 seconds by default). Covered by
`RuntimeResumptionServiceTests.SweepAsync_DeliversContributedIntentCommittedThroughRealCheckpointAndOutbox`
and, for a crash between the live drain's enqueue and its `Delivered` mark,
`RuntimeLiveDrainDeliveryTests.LiveDrain_CrashWindow_UnmarkedIntent_RedrivesIdempotentlyToNoOp`.

### Window B — after outbox delivery, before drain *(recovered)*

The outbox row is `Delivered` and the scheduler work is durably queued, but it was never drained. On
restart, the sweep's **backlog discovery** (`ListClaimableWorkflowExecutionIdsAsync`) finds the
execution and re-drives, draining the queue. ✅ Covered by
`RuntimeResumptionServiceTests.SweepAsync_RedrivesBacklogThroughAgentWithRecoveryEnvelope`.

### Window C — after the drainer picks up an item, before its handler checkpoint commit *(recovered)*

The drainer picks up a work item and its handler begins, but the process crashes **before** the handler
commits the checkpoint that would record the resulting progress. Concretely this includes a crash between
the fallback handler's two independent writes (save activity-execution state, then enqueue the follow-up
work item) — the case #412 item 3 named.

**What closes it — the redrive-safe drain (#412 item 3).** `WorkflowSchedulerDrainer` no longer
destructively dequeues before dispatch. It peeks the head, dispatches it in place, and only **ack-deletes**
it from the durable queue *after* the handler's effect is durable (a successful commit; or, on a *handler
fault*, before the poison record / RetryNow re-enqueue). A process crash before the ack therefore leaves the
source item durably queued, so the sweep's **backlog discovery** (`ListClaimableWorkflowExecutionIdsAsync`)
finds the execution once the dead drainer's claim lapses (`RuntimeSchedulerWorkClaimOptions.VisibilityTimeout`,
1 minute by default; until then no claim could take the item either) and re-drives it, and the handler re-runs **idempotently** — the activity-execution
status guards (`existing.Status == Scheduled` / `state.Status == Running`) recognise the already-applied
first write, and the deterministic follow-up work-item ids (`…:start:…`, `…:invoke:…`) are absorbed by the
idempotent queue, so redelivery never double-applies. This is covered by
`RuntimeSchedulerDrainTests.DrainAsync_RedriveSafe_CrashBetweenFallbackWrites_LeavesSourceItemQueuedForRedelivery_AndConvergesOnRedrive`
(crash-between-writes injection over the real Schedule handler, then a redrive that converges) and the
poison-path bounding in
`WorkflowSchedulerPoisonDrainTests` (ack-on-fault means a poisoned item is delivered a bounded number of
times, never a hot-loop).

**Belt-and-braces detection (W5).** Independently of the queue backlog, W5 (single-writer ownership
fencing, RT-2) also makes an interrupted drain **detectable**: `WorkflowDrainOrchestrator` acquires a
`RuntimeExecutionLease` (writing `ExecutionLease` + `Heartbeat` to operational state) and releases it in a
`finally`. When a crash prevents the release, `IRuntimeRecoveryScanner` yields the execution as a
`LeaseLost`/`HeartbeatExpired` candidate and the sweep unions it with the durable backlog before re-driving
through the agent mailbox. The two mechanisms compose: the redrive-safe ack guarantees the *item* survives
for item-level replay, and the lease/heartbeat guarantees the *execution* is surfaced even when a crash
leaves no queued backlog. This is covered by
`RuntimeResumptionServiceTests.SweepAsync_DiscoversWindowCExecution_FromOwnershipLeaseLeftByCrash` plus
lease-detectability/no-false-positive tests in `RuntimeExecutionOwnershipTests`.

## Bounding the pump

So a single restart with a large backlog — or one poisoned execution — cannot overwhelm or starve the
sweep, the pump is bounded on two axes:

- **Per tick:** `RuntimeResumptionSweepRequest.MaxExecutionsPerSweep` (default 100) caps how many
  executions one sweep re-drives. The recovery scanner keeps half of the cap (at least one slot, at
  most its batch size) and the backlog the rest, so neither can stop the other running; either side
  may use the slots the other leaves, and a cap of one alternates between them (#2188).
- **Across ticks:** backlog discovery lists only executions whose work is claimable now, so work held
  by a live claim or a backoff cannot fill the page. Every candidate, from the backlog, the recovery
  scanner or a candidate source, is re-driven only when the drainer's own pause gate would let its next
  queued item advance: a paused head stays claimable (the drainer releases it at the closed gate), and
  re-driving it only left one more `RunSchedulerWork` row queued behind it on every pass. A held
  recovery or source candidate still counts as dealt with: the scanner's cursor moves on and the source
  settles it. That costs ownership nothing, because the next drain acquires a strictly greater fencing
  token whatever a stale lease says. A hold is lifted by saving hold state, with no event to react to,
  so a resume is noticed the same way: the next pass that reaches the execution finds the gate open and
  re-drives it. A held execution that has reached a terminal status is still purged and reaped, which
  re-drives nothing. When the gate cannot be consulted the execution is not re-driven either, since its
  drain would consult the same gate; its work stays queued, a recovery candidate keeps the scan cursor
  in place so the scanner offers it again next sweep, and the sweep logs one warning
  (`RuntimeResumptionPauseCheckFailed`) with the count. The sweep reads next items in one request per
  page of executions and asks the gate only about executions it can use. A pass reads on past held
  executions, up to ten pages. The sweep walks the backlog from a position it keeps between ticks
  (`RuntimeResumptionDiscoveryStateStore`, least recently used scopes evicted first), resuming after
  the last execution it visited and starting over after a short page, so a fixed set of executions that
  stay claimable without draining cannot hold the window either. The position moves on past a failed
  re-drive; the failed execution keeps its work and its per-execution backoff, and the next walk
  reaches it again. A queue that does not implement claimable discovery keeps the earlier first-page
  listing, and the sweep logs a warning (`RuntimeResumptionFirstPageBacklogDiscovery`) once per queue
  type per host.
- **Per execution:** the pump applies a geometric backoff to individual executions whose re-drive
  fails, passing them as `ExcludedWorkflowExecutionIds` so they are skipped until their backoff
  elapses. A separate whole-sweep geometric backoff (bounded by `MaxBackoffInterval`, default 5m)
  throttles after consecutive sweep failures. One poisoned execution therefore cannot monopolise the
  sweep or block healthy executions.

## Coalescing checkpoint persistence — the deferred-flush window (E3-6 / RT-10)

Everything above describes the **default** `ImmediateRuntimeCheckpointPersistencePolicy`: every named
checkpoint flushes to the durable store the moment it is decided. `AddCoalescingRuntimeCheckpointPersistence`
swaps in `CoalescingRuntimeCheckpointPersistencePolicy` — an **opt-in** durability/throughput trade,
selectable exactly like Elsa 3's commit strategies. It folds a *drain segment* of non-suspending intra-drain
checkpoints into **one atomic flush commit at quiescence**, matching Elsa 3's one-write-per-burst behaviour.
The default runtime keeps Immediate, so nothing below applies unless coalescing is explicitly enabled.

Hosts select the policy through the provider-neutral `WorkflowsRuntimeCheckpointPersistence` shell feature. It
runs after persistence-provider registration, so it decorates whichever runtime stores the shell selected:

```json
"WorkflowsRuntimeCheckpointPersistence": {
  "Mode": "Coalesced",
  "MaxSegmentCheckpoints": 50
}
```

`Mode` is `Immediate` by default and is the configuration-only rollback switch. `Coalesced` reduces physical
checkpoint writes and request latency for straight-line synchronous workflows, at the cost of replaying up to one
unflushed segment after a crash. `MaxSegmentCheckpoints` (default 50, positive values only) bounds that replay and
memory window; lower values flush more frequently, while higher values favor write reduction. The Elsa.Workbench
reference composition explicitly selects `Coalesced` with cap 50; other hosts remain `Immediate` unless configured.

**A workflow can author its own cadence.** The design draft carries it under
`state.strategyOptions.checkpointCadence`, which publication compiles into the immutable executable:

```json
"state": {
  "strategyOptions": {
    "checkpointCadence": {
      "mode": "Coalesced",
      "maxSegmentCheckpoints": 8
    }
  }
}
```

`mode` accepts `Immediate` or `Coalesced`; an absent or empty mode inherits the host default. Authored `Immediate`
overrides a Coalesced host. Authored `Coalesced` overrides the host cap when it supplies a positive
`maxSegmentCheckpoints`; when that cap is absent, the host cap applies. A Coalesced authoring on an Immediate host
resolves to Immediate because that host has no coalescing session. Mandatory boundaries still flush immediately.
Changing authored cadence requires publishing a new workflow version so the executable contains the change; editing
the draft alone does not alter a published executable.

At run start, the runtime stamps the effective cadence onto that execution's persisted state and includes the segment
cap for Coalesced runs; it carries the stamp forward as the state is rebuilt. Instance detail reports
`checkpointCadence`, `maxSegmentCheckpoints`, and `inspectionGranularity` for the cadence that run used. To verify
persistence across host reconfiguration, capture the
`workflowExecutionId` and these fields from a completed run, restart the host against the same database with a
different host mode or cap, then issue the authenticated
`GET /runtime/workflows/instances/{workflowExecutionId}` for that same ID. The three fields should still describe
the original run; a new run with no authored cadence should resolve from the reconfigured host. The existing
`e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1` fixture prints the execution ID and performs the initial
detail read. Its optional `-AuthoredCadence` and `-AuthoredMaxSegmentCheckpoints` inputs exercise the published
authoring contract, while `-ExpectedMaxSegmentCheckpoints` checks an explicit effective cap. When the expected cadence
is Coalesced and this parameter is omitted, an explicit authored cap becomes the expectation; otherwise it defaults
to 50. When testing host-cap fallback on a Coalesced host with a cap other than 50, pass that host cap explicitly
with `-ExpectedMaxSegmentCheckpoints`. Immediate expects a null cap. With no new parameters, the fixture keeps its existing Coalesced/50 expectation
and no authored override. This #2392 SetVariable intrinsic reference remains representative and is not equivalent to
the historical custom CLR transform.

The [T09 normal-host verification](reports/runtime-db-access/cadence-verification.md) records six cadence cases,
same-execution readback across host reconfiguration, and bookmark/incident persistence across a real process restart.

For SQL diagnostics, temporarily set `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information`, retain
structured logs with scopes and UTC timestamps, and inspect EF command event 20101. Keep sensitive-data logging disabled. This counts command executions;
it does not by itself count individual SQL statements. Keep diagnostic runs separate from timing runs, which use
`Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning`, then restore the host's normal logging configuration.

**Governing invariant — the durable scheduler queue never advances past the last flushed state.** Within a
coalesced segment, intra-drain checkpoints are buffered in an ambient in-memory working set (an overlay over
the real state stores, scheduler queue, and outbox). The segment-entry work item is **only** dequeued from the
durable queue as part of the atomic flush commit — never durably dequeued before the flush lands. So at every
instant the durable queue's frontier equals the last flushed checkpoint. A crash mid-segment discards the
in-memory buffer and leaves the segment-entry item exactly where the last flush left it, so the standard Window
B recovery (backlog discovery → re-drive) replays the entire segment from the last durable state.

**What is buffered.** Workflow-execution, activity-execution, durable-value and scheduler state writes, plus
continuation intents (`EnqueueSchedulerWork`) which are consumed **in-segment** from the overlay so a folded
segment does not durably re-record its own continuation. **What is never buffered** (condition E): W5's
lease/heartbeat/ownership operational writes and `EnsureOwnershipAsync` fencing go straight to the operational
store as today, and the single folded flush still goes through `RuntimeCheckpointCommitter.CommitAsync`, so
ownership fencing gates the coalesced flush exactly as it gates an immediate commit — a stale writer is rejected
before anything persists.

**Inspection reads are memoized, not overlaid** (spec 131). The per-hop inspection-projection build reads
`IActivityExecutionInspectionStore.FindAsync` to pick its merge branch; under coalescing the overlay serves a
per-drain memo of the **durable baseline** (never the buffered state, which would change the flushed projection
bytes), invalidated at every durable flush. `CoalescingRuntimeCheckpointPersistenceOptions.CoalesceInspectionReads`
(default on) disables it back to per-hop durable reads.

**Flush boundaries — mandatory checkpoints are never coalesced away.** The policy forces an immediate flush at
every durability-critical boundary: `WorkflowSuspended`, `WorkflowCompleted`, `WorkflowFaulted`,
`WorkflowCancelled`, `IncidentRecorded`, `ActivitySuspended`, `ActivityCancelled`, and `BookmarkCreated`. A
bookmark-suspend (including `Delay`/timer-style suspensions) flushes so an external stimulus always finds a
**durable** bookmark and can never race an in-memory-only one; a decided fault is durable at the moment it is
decided. End-of-drain quiescence also flushes even when the workflow is still `Running`/waiting. A
boundary-forced flush is **complete** (condition C): it atomically persists the folded state + the boundary
checkpoint itself + any remaining unconsumed in-memory queue items (durably re-enqueued) + undelivered outbox
intents. Nothing buffered is lost at a boundary.

**Segment cap.** `CoalescingRuntimeCheckpointPersistenceOptions.MaxSegmentCheckpoints` (default 50) counts buffered
checkpoint records, not activities, nodes, or activity executions. It bounds a segment: once the buffered checkpoint
count reaches the cap, an intermediate fold-and-flush is forced and a
**fresh segment starts** (like a durable attempt boundary), so a replayable hot loop longer than the cap keeps
coalescing at one durable commit per cap-sized window instead of degrading to per-checkpoint persistence for the
remainder of the drain (ADR 0032's segment-cap follow-up). The cap's purpose is unchanged: it bounds both replay
cost (a crash re-runs at most one cap-sized segment past the last folded flush) and memory (the working set
buffers at most one segment). Continuation outbox items persisted durably by a cap flush keep delivering from
the overlay for the next segment; the session writes their overlay outcome back to the durable outbox store at
the next flush, so no durable `Pending` residue survives the drain to be redelivered by a later sweep. See the
benchmark results doc for the replay-cost trade.

**The crash-replay window and at-least-once semantics.** A crash mid-segment loses the buffered-but-unflushed
checkpoints, but they are **replayable from the last flushed commit + durable queue redelivery** — the honest
recovery generation re-drives the segment-entry item and re-executes the whole buffered segment. This means
**in-segment activity re-execution after a crash is expected**: the crashed generation persisted no checkpoint,
so activities in the lost segment run again on recovery. This is the same at-least-once-after-persist guarantee
the Immediate path already gives on the enqueue side (§The idempotency / durability contract); coalescing simply
widens the replay window from one checkpoint to one segment. External-facing outbox intents are still delivered
**only post-flush**, so an activity's external effect is never delivered before its durable commit. Convergence
and the absence of duplicate *terminal* effects are proven by
`CoalescingCrashConvergenceTests.Coalescing_CrashMidSegment_QueueRetainsSegmentEntry_ThenHonestSweepConvergesWithoutDuplicateEffects`
(two generations over a shared store: gen-1 crashes mid-segment with the queue still holding the segment entry;
gen-2's honest sweep converges to the crash-free control snapshot) and the queue-retention half by
`RuntimeCheckpointCoalescingTests.CrashMidSegment_DurableQueueStillHoldsSegmentEntry_AndNoPartialCheckpointPersisted`.
That a bookmark-suspend flushes its bookmark **durably** at the boundary — so W8's durable Delay/timer pump (which
reads the durable bookmark store) can never race an in-memory-only bookmark — is proven by
`RuntimeCheckpointCoalescingTests.Coalescing_BookmarkSuspend_FlushesDurableBookmarkImmediately`, and that
coalescing never wraps W8's `IDurableTimerStore` or the `IBookmarkStateStore` (so a `Delay` suspension's timer
*and* bookmark both persist directly, never through the buffer) by
`RuntimeCheckpointCoalescingTests.Coalescing_DoesNotDecorateDurableTimerOrBookmarkStores_SoDelaySuspensionStaysDurable`.

## Runtime composition root and lifetimes (RT-4)

The hosting-agnostic runtime execution spine is registered by
`RuntimeCoreServiceCollectionExtensions.AddWorkflowRuntime(this IServiceCollection)` in the
`Elsa.Workflows.Runtime` engine package (moved out of `.Core` and renamed from `AddWorkflowRuntimeCore` by ADR 0033). The FastEndpoints `WorkflowsRuntimeApiFeature` no longer owns those
registrations — it composes the Core root and then adds only its HTTP request handlers. This makes the
runtime usable from a non-HTTP host (a worker, another module, a test harness) without pulling in the
API feature. The host-agnostic guard is `RuntimeCoreCompositionRootTests`: it composes
`AddWorkflowRuntime` into a bare `ServiceCollection` and drives a real Cancel drain end-to-end with
no API feature present.

**Lifetime story — deliberate, not incidental.** Reference in-memory state remains **singleton**
(process-global), while the operation graph over persistence ports — handlers, pipelines, dispatchers,
drainers, checkpoint collaborators, and lookups — is **scoped**. This preserves application-wide in-memory
state while allowing a durable provider to bind storage scope and access policy to one execution scope.
Two host-lifetime components cross that seam explicitly: in-process actor mailboxes open a fresh async
scope per command, and recurring pumps open a fresh async scope per tick.

- **Overridability is preserved.** Every Core registration uses `TryAdd*`, so a durable provider package
  can register its own store *before or after* `AddWorkflowRuntime` and win. Scoped durable stores no longer
  become captive dependencies of process-global runtime services.
- **W9 coalescing decorators still wrap.** The opt-in `AddCoalescingRuntimeCheckpointPersistence`
  decorates the commit store / queue / outbox / state stores registered here; the Core root does not
  bypass the scope boundary, so those decorators keep composing within the active operation scope.
- **Host-wide coordination state remains host-wide.** In-memory stores, actor mailboxes and their
  idempotency caches, clocks, options, and pure policies remain singletons. Only access-bound operation
  collaborators are recreated; each mailbox command or background tick awaits and disposes its scope.

## Drain-path ambient service location removed (RT-7)

The drain path no longer resolves collaborators through ambient service locators. Two AsyncLocal
smugglers were deleted: the `IWorkflowExecutionAmbientServicesAccessor` that the drainer used to reach a
request-scoped `IServiceProvider`/state store, and the pipeline-context accessor that carried the mutable
workspace to handlers. Both now flow **explicitly**: the drainer injects `IWorkflowExecutionStateStore`
directly and passes the drain request's `AmbientServices` into
`IRuntimeExecutionPipelineDispatcher.DispatchAsync`, which stages it on
`RuntimePipelineWorkspace.AmbientServices`; the migrated nested-invoke handlers (`InvokeActivity`,
`ParentActivityCompletion`) read it from that workspace member instead of an AsyncLocal `.Current`.

Two ambients remain **by deliberate design**, and neither is a drain-path service locator:

- `IRuntimeExecutionOwnershipContextAccessor` — a runtime-internal AsyncLocal lease scope (RT-2/W5
  fencing) that carries the active lease from the drain coordinator to the single commit funnel.
- `IRuntimeCoalescingSessionAccessor` (**W9**) — an **opt-in ambient session flag**, not service
  location: it marks that a coalescing session is active so the decorators buffer intra-drain checkpoints
  into the in-memory working set. It is registered only by `AddCoalescingRuntimeCheckpointPersistence`
  and its gating semantics are preserved exactly ("the durable scheduler queue never advances past the
  last flushed state"). It is a documented exception to "no AsyncLocal in the drain path", distinct in
  kind from the removed service locators.

## Slot-invoked handler model (ADR 0029 Move 2)

Scheduler work handlers no longer commit inline from a terminal step. A migrated handler additionally
implements `IRuntimePipelineWorkHandler`; the dispatcher stages it on the workspace, the pipeline's
`Invoke` slot runs it, and the `Checkpoint` slot drains the handler-staged commit **list in order, one
`RuntimeCheckpointCommitter.CommitAsync` call per staged entry** — byte-identical to the previous inline
sequence. The slot never batches or folds staged commits (folding is the W9 coalescing decorators' job;
batching would change W9 boundary detection and W5 fencing granularity). The two nested-invoke handlers
are the deliberate exception: their commits go through a dynamically-resolved provider, so they commit
**inline** in the `Invoke` slot and stage nothing — converting them to staged commits would not be
behavior-preserving.

## Out of scope (owned elsewhere)

- **Ack-based dequeue** that keeps a scheduler work item durably owned until the consuming handler's
  checkpoint commits — the remaining increment for item-level window-C replay, layered on W5's
  lease/heartbeat ownership primitive over W2's durable queue.
- Drainer/handler refactors — **W1**. Runtime-spine decomposition — specs/083. Serializer-policy
  remediation (PS-3) — **W3**. Multi-node outbox delivery-ownership fencing — the durable outbox
  store rejects `OwnerId` filters today, and the sweep passes none.

## Cross-references

- Program-goal bucket: [`docs/program-goals/elsa-4-review-remediation.md`](program-goals/elsa-4-review-remediation.md).
- Roadmap W2 brief: [`docs/reports/elsa-4-architecture-review-2026-07/roadmap.md`](reports/elsa-4-architecture-review-2026-07/roadmap.md).
- Runtime extension points: [`src/essentials/Workflows/Runtime/EXTENSION_POINTS.md`](../src/essentials/Workflows/Runtime/EXTENSION_POINTS.md).
- Resumption feature surface: [`src/essentials/Workflows/Runtime/Resumption/EXTENSION_POINTS.md`](../src/essentials/Workflows/Runtime/Resumption/EXTENSION_POINTS.md).
