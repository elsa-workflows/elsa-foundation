# Durable timers and the `Delay` activity (E3-2)

> **Audience:** engineers and architects working in `elsa-foundation`.
> **Purpose:** document how a workflow suspends on a relative delay and resumes durably after a
> process restart — the worked reference behind roadmap unit **W8** of the Elsa 4 review-remediation
> program (finding **E3-2**).
> **Knowledge role:** worked reference. Canonical short definitions live in
> [`docs/glossary/elsa.md`](glossary/elsa.md); the extension-point contracts live in
> [`src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`](../src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md).
> **See also:** [`docs/runtime-durable-resumption.md`](runtime-durable-resumption.md) — the durable
> resumption spine (W2) this unit piggybacks on.

## The problem

A `Delay(5s)` must suspend the workflow, survive a process restart, and resume on schedule. "Survive a
restart" is the hard part: an in-memory timer dies with the process. Durable timers add a persisted,
due-time-indexed record plus a hosted pump that fires due timers through the **existing** resume path.

## The three pieces

1. **Durable timer store** (`IDurableTimerStore`, document kind `durableTimer`). Persists a
   `DurableTimer { TimerId, WorkflowExecutionId, StimulusType, StimulusHash, DueTime, CreatedAt, … }`
   keyed by `(WorkflowExecutionId, TimerId)`. `GroundworkDurableTimerStore` is the durable bridge
   (`AddGroundworkRuntimeStores`); `InMemoryDurableTimerStore` is the non-durable default. Routes
   through `IGroundworkRuntimeDocumentSerializer` + `ElsaRuntimeDocumentVersions` with a v1 golden
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
(durable when Groundwork-backed) **before** the drain and before the agent returns `Accepted`. So if the
process crashes after the timer delete but before the resume commits, W2's resumption sweep
(`IRuntimeResumptionService.SweepAsync`) discovers the durable backlog
(`ListPendingWorkflowExecutionIdsAsync`) and re-drives the workflow to completion. The timer being gone
is harmless — it already did its one job.
*Covered by `DurableTimerRestartCrashTests.DeleteOnDispatched_IsSafe_ResumeSurvivesCrashBeforeDrain_AndConverges`.*

### 2. The bookmark does not own the deadline

The bookmark is created with `ExpiresAt = null`. The **timer** owns the deadline. Setting
`ExpiresAt = dueTime` would make the bookmark unmatchable exactly at fire time — the stimulus lookup
filters out bookmarks whose expiry is at/behind the evaluation instant — producing `NotFound` forever
and a permanent hang.

### 3. Idempotency under at-least-once delivery

The pump fires with `idempotencyKey = "timer:{TimerId}"`. A duplicate/late fire finds the single-use
bookmark already consumed and returns `NotFound`; past the `NotFoundGrace` window the pump deletes the
timer. Within grace, `NotFound` is treated as "a very short delay is still committing its bookmark" and
retried. So a duplicate fire can never double-resume.
*Covered by `DurableTimerRestartCrashTests.TimerFire_IsIdempotent_UnderAtLeastOnceDuplicateDelivery`.*

## Durability caveat

`Delay` is restart-durable **only** in a shell with a durable timer store (Groundwork). With the
in-memory default store it still suspends and resumes within the process but does not survive a restart.

## Follow-ups (not in this wave)

- **Timer/Cron start triggers** (schedules that *start* a workflow) depend on W7's trigger/stimulus
  index. The `durableTimer` kind is shaped so a `start-trigger` variant plugs in later without a schema
  change.
- **Native due-time range index.** Groundwork is equality-index only, so `ListDueAsync` loads the whole
  timer partition each tick and filters `DueTime` in memory. `MaxTimersPerTick` bounds the dispatch
  burst, not the load. A native range index is the scale follow-up.
- **Atomic timer registration (Option B).** Registering the timer via a post-commit
  `RegisterDurableTimer` intent would make the timer==bookmark lifecycle fully atomic, at the cost of
  editing the core create-bookmark handler and the single-implementation post-commit intent dispatcher.
- **Node-scoped resume targets.** The executable compiler keys `ResumeTargets` by the `[ResumeTarget]`
  attribute ID, so only one instance of a given resume-target activity is supported per workflow this
  wave. Node-scoped IDs (keyed by node + attribute) would lift the single-`Delay`-per-workflow limit.
