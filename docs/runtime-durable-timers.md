# Durable timers and the `Delay` activity (E3-2)

> **Audience:** engineers and architects working in `elsa-foundation`.
> **Purpose:** document how a workflow suspends on a relative delay and resumes durably after a
> process restart — the worked reference behind roadmap unit **W8** of the Elsa 4 review-remediation
> program (finding **E3-2**).
> **Knowledge role:** worked reference. Canonical short definitions live in
> [`docs/glossary/elsa.md`](glossary/elsa.md); the extension-point contracts live in
> [`src/essentials/Workflows/Runtime/EXTENSION_POINTS.md`](../src/essentials/Workflows/Runtime/EXTENSION_POINTS.md).
> **See also:** [`docs/runtime-durable-resumption.md`](runtime-durable-resumption.md) — the durable
> resumption spine (W2) this unit piggybacks on; and
> [`docs/bookmark-expiration.md`](bookmark-expiration.md) — the bookmark admissibility cutoff,
> non-timeout semantics, and worked use cases.

## The problem

A `Delay(5s)` must suspend the workflow, survive a process restart, and resume on schedule. "Survive a
restart" is the hard part: an in-memory timer dies with the process. Durable timers add a persisted,
due-time-indexed record plus a hosted pump that fires due timers through the **existing** resume path.

## The three pieces

1. **Durable timer store** (`IDurableTimerStore`, document kind `durableTimer`). Persists a
   `DurableTimer { TimerId, WorkflowExecutionId, StimulusType, StimulusHash, DueTime, CreatedAt, … }`
   keyed by `(WorkflowExecutionId, TimerId)`. `EfDurableTimerStore` is the durable bridge
   (`AddRuntimeEntityFrameworkCore`); `InMemoryDurableTimerStore` is the non-durable default. Routes
   through the runtime document serializer + `ElsaRuntimeDocumentVersions` with a v1 golden
   fixture, following the `schedulerWorkItem` pattern (W3).

2. **Hosted timer pump** (`DurableTimerPumpTask : IRecurringTask`, `Elsa.Workflows.Runtime.Scheduling`).
   Modeled on `RuntimeResumptionPumpTask`: one bounded sweep per tick (`MaxTimersPerTick`), geometric
   whole-sweep and per-timer backoff, never throws out of a tick. Each due timer is fired as a bookmark
   resume through `IBookmarkResumeDispatcher` — the same single-writer mailbox path every other resume
   uses (W5).

3. **`Delay` activity** (`Elsa.Activities.Scheduling`). Writes the timer, then creates a matching
   bookmark, then suspends.

## The suspend/resume cycle

```
Delay.ExecuteAsync                  DurableTimerPumpTask (per tick)         resume spine
------------------                  -------------------------------        ------------
dueTime = clock.now + Duration
write DurableTimer  ───────────►    (persisted, survives restart)
CreateBookmark(ExpiresAt = null)    ───────────►  (persisted bookmark)
suspend
                                    ListDueAsync(now) → due timer
                                    DispatchAsync(ResumeBookmark) ──────►  agent mailbox
                                                                           ProcessAsync enqueues
                                                                           work durably, then drains
                                    on Dispatched/Duplicate → delete timer
```

## Three correctness cruxes

### 1. Delete-on-`Dispatched` is safe

The pump deletes a timer as soon as the dispatcher returns `Dispatched`. That is only safe because the
resume is **durably enqueued before the dispatcher returns**:
`WorkflowSchedulerCommandRouter.ProcessAsync` calls `_schedulerWorkQueue.EnqueueAsync(workItem)`
(durable when EF Core-backed) **before** the drain and before the agent returns `Accepted`. So if the
process crashes after the timer delete but before the resume commits, W2's resumption sweep
(`IRuntimeResumptionService.SweepAsync`) discovers the durable backlog
(`ListClaimableWorkflowExecutionIdsAsync`) and re-drives the workflow to completion. The timer being gone
is harmless — it already did its one job.
*Covered by `DurableTimerRestartCrashTests.DeleteOnDispatched_IsSafe_ResumeSurvivesCrashBeforeDrain_AndConverges`.*

### 2. The bookmark does not own the deadline

The bookmark is created with `ExpiresAt = null`. The **timer** owns the deadline. Setting
`ExpiresAt = dueTime` would make the bookmark unmatchable exactly at fire time — the stimulus lookup
filters out bookmarks whose expiry is at/behind the evaluation instant — producing `NotFound` forever
and a permanent hang. See [bookmark expiration](bookmark-expiration.md) for the general contract and
examples of finite expiration.

### 3. Idempotency under at-least-once delivery

The pump fires with `idempotencyKey = "timer:{TimerId}"`. A duplicate/late fire finds the single-use
bookmark already consumed and returns `NotFound`; past the `NotFoundGrace` window the pump deletes the
timer. Within grace, `NotFound` is treated as "a very short delay is still committing its bookmark" and
retried. So a duplicate fire can never double-resume.
*Covered by `DurableTimerRestartCrashTests.TimerFire_IsIdempotent_UnderAtLeastOnceDuplicateDelivery`.*

## Durability caveat

`Delay` is restart-durable **only** in a shell with a durable timer store (EF Core). With the
in-memory default store it still suspends and resumes within the process but does not survive a restart.

## Timer and Cron start triggers: at least once per occurrence

A `Timer` or `Cron` *start* trigger starts a new workflow run on a schedule. It does not use the durable
timer store: publishing records a recurring schedule (`IRecurringTriggerScheduleStore`) whose cursor holds
the next occurrence, and the recurring-trigger pump (`RecurringTriggerPumpTask`, feature
`WorkflowsRuntimeRecurringTriggers`) fires due occurrences through the stimulus router.

**The guarantee: each occurrence of an active schedule is fired at least once and starts one run (#2198).**

- **In flight before it is fired.** The pump claims a due occurrence under a fenced lease before it routes
  it, and moves the schedule past the occurrence only after the route returned. If the node crashes after
  claiming, before or after routing, a peer (or the same node after a restart) fires the occurrence again
  once the lease lapses. The lease is the feature setting *Occurrence claim visibility*, 60 seconds by
  default, so it bounds how late a crashed occurrence fires.
- **A failed fire is retried, not skipped.** If starting the run throws, or no trigger binding is found for
  the schedule (for example while a republish is replacing the index), the occurrence is released with a
  backoff that doubles from the sweep interval up to the maximum backoff, and then fired again. Each retry
  is logged, so a persistently failing schedule shows up in the log rather than going quiet.
- **Repeats start nothing new.** Every fire of one occurrence carries the same idempotency key, so the
  router starts it as a keyed start: a repeat finds the run the first fire started and reports it as a
  duplicate (see *Stimulus START idempotency is at-least-once* in [serialization](serialization.md)). The
  key names the trigger, not the publication: `recurring:{slotId}:{nodeId}:{stimulusHash}:{occurrenceTicks}`
  for a schedule of a publication slot, which every publication of the slot shares, and
  `recurring:{scheduleId}:{occurrenceTicks}` for a schedule without a slot. Two nodes that sweep at the same
  moment cannot both claim one occurrence.
- **Republishing does not skip a due occurrence.** Activating a new publication of a slot replaces the
  replaced publication's schedules in one write. A schedule of the new publication takes over the replaced
  schedule's next occurrence of the same trigger (same slot, trigger node and stimulus) when that occurrence
  is earlier than the new schedule's own and fell due no later than the activation, so the occurrence that
  was due during the republish, including one that fell due after the new publication was prepared and before
  it was activated, fires on the new publication. The replaced schedule changes
  in the same write, so a claim a node still holds on it is stale and cannot settle the occurrence. If the
  replaced publication had already routed the occurrence, the new publication's fire carries the same key
  and starts nothing new, even though it serves another artifact: an occurrence start of a slot is keyed
  without the artifact. The legacy re-index path, which replaces an artifact's schedules without a
  publication slot, likewise keeps a cursor whose occurrence is already due.
- **An exhausted Cron fires its last occurrence.** When a Cron expression has no occurrence after the one
  in the cursor, that last occurrence is still routed under its claim, and the schedule is deleted only
  after the route returned. If the route fails, the occurrence is released and retried like any other.

**What it does not do.**

- **No catch-up.** After downtime, or while a failing occurrence is being retried, the occurrence in the
  cursor fires once and the schedule then moves to the first occurrence after the current time.
  Occurrences that elapsed in between are not replayed. A republish hands over only the replaced
  schedule's next occurrence, not a backlog.
- **A trigger the new publication dropped is not fired.** An occurrence that was due on a replaced
  publication's trigger which the new publication no longer has (another trigger node or another
  stimulus, for example a changed Cron expression) is not fired on its behalf.
- **The in-memory store is not durable.** Without the EF Core runtime persistence, schedules and their
  claims live in memory and are lost on restart.

**Rolling deploys.** The claims and the start-once dedupe hold only among nodes that run this behaviour
(#2198), and keyed starts need #2195 or later. A node from before #2198 advances a schedule before it
routes the occurrence and ignores the claim columns; it keys the start with
`recurring:{scheduleId}:{occurrenceTicks}` under the per-artifact identity (from #2195 on); before #2195 it
sent that key too, but the router remembered it only in process memory, so a repeat after a restart, or on
another node, started again. So during a rolling deploy an occurrence that falls due can fire on an old and a
new node and start twice, and an old node that crashes mid-fire still loses its occurrence. Roll every node
that runs the recurring-trigger pump before relying on the guarantee, or stop the pump on old nodes first.

## Follow-ups (not in this wave)

- **Timer/Cron start triggers** (schedules that *start* a workflow) shipped through their own recurring
  schedule store and pump rather than a `start-trigger` variant of the `durableTimer` kind; see
  [Timer and Cron start triggers](#timer-and-cron-start-triggers-at-least-once-per-occurrence).
- **Native due-time range index.** Without a range index on `DueTime`, `ListDueAsync` loads the whole
  timer partition each tick and filters `DueTime` in memory. `MaxTimersPerTick` bounds the dispatch
  burst, not the load. A native range index is the scale follow-up.
- **Atomic timer registration (Option B).** Registering the timer via a post-commit
  `RegisterDurableTimer` intent would make the timer==bookmark lifecycle fully atomic, at the cost of
  editing the core create-bookmark handler and the single-implementation post-commit intent dispatcher.
- **Node-scoped resume targets.** The executable compiler keys `ResumeTargets` by the `[ResumeTarget]`
  attribute ID, so only one instance of a given resume-target activity is supported per workflow this
  wave. Node-scoped IDs (keyed by node + attribute) would lift the single-`Delay`-per-workflow limit.
