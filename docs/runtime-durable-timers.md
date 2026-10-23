# Durable timers and the `Delay` activity

**Who this is for:** contributors who want to know how a workflow waits for a period of time and still wakes up on schedule after the server restarts.

## The problem

A `Delay` of 5 seconds must suspend the workflow, survive a process restart, and resume on time. Surviving the restart is the hard part: an in-memory timer dies with the process.

Durable timers solve it with a **saved timer record** plus a **background pump** that fires due timers through the **normal** bookmark-resume path. Nothing about resuming is special to timers. The EF store's composite index includes `DueTimeUtcTicks`; `ListDueAsync` filters due timers, orders them and `Take(limit)`s in the database.

> *Good to know:* `Delay` survives a restart **only** in a shell with a durable timer store (EF Core). With the in-memory default it still suspends and resumes within one process, but a restart loses the timer.

## The three pieces

| Piece | What it does |
|---|---|
| **Timer store** | Saves each timer: which run it belongs to, the stimulus it will send, its due time and when it was created. Keyed by run and timer id. In memory by default; a database store when EF Core runtime persistence is on. |
| **Timer pump** | A recurring background task. Each tick it takes a bounded batch of due timers, fires each as a bookmark resume through the same single-writer mailbox path every other resume uses, and backs off (per sweep and per timer) on failure. It never throws out of a tick. It is modelled on the [resumption pump](runtime-durable-resumption.md). |
| **`Delay` activity** | Schedules a timer, then suspends with a matching bookmark registration. |

The real `Delay.ExecuteAsync`, trimmed:

```csharp
var now = timeProvider.GetUtcNow();
var dueTime = Duration <= TimeSpan.Zero ? now : now + Duration;
var timerId = $"timer:{context.InvocationId}";

await scheduler.ScheduleAsync(new DurableTimer(
    TimerId: timerId,
    WorkflowExecutionId: context.WorkflowExecutionId,
    StimulusType: DurableTimerConstants.TimerStimulusType,
    StimulusHash: timerId,
    DueTime: dueTime,
    CreatedAt: now, ...), ct);

return Suspend(new DelayState(timerId), [registration]); // the runtime saves the bookmark
```

Each `Delay` derives its timer id from its own `InvocationId`, so several Delays in one workflow each get their own timer.

## How a delay suspends and resumes

```mermaid
sequenceDiagram
    participant D as Delay activity
    participant T as Timer store
    participant B as Bookmark store
    participant P as Timer pump
    participant M as Run's mailbox
    D->>T: save timer (due = now + Duration)
    D->>B: suspend; runtime saves bookmark (no expiry)
    Note over T,B: both survive a restart
    loop every tick
        P->>T: list due timers
        P->>M: resume bookmark (key "timer:{TimerId}")
        M->>M: queue resume work durably
        M-->>P: Dispatched / Duplicate
        P->>T: delete timer
        M->>M: drain queued resume work
    end
```

## Why it is correct

### 1. Deleting the timer once the resume is accepted is safe

The pump deletes a timer as soon as the dispatcher answers *Dispatched* (or *Duplicate*). That is safe because the resume is **already saved in the queue** before the dispatcher answers: the scheduler's command router queues the work item (durably, with EF Core) before the drain runs and before the mailbox answers *Accepted*.

So if the process crashes after the timer is deleted but before the resume commits, the resumption sweep finds the queued work and finishes the run. The deleted timer has already done its one job.

### 2. The timer owns the deadline, not the bookmark

The bookmark is saved with **no expiry**. If it expired at the due time, it would stop matching at exactly the moment the timer fires (the stimulus lookup filters out bookmarks whose expiry is at or before the evaluation instant). The result would be *NotFound* for ever, and the workflow would hang. See [bookmark expiration](bookmark-expiration.md) for the general rule and examples of finite expiry.

### 3. Firing twice cannot resume twice

Timers are delivered at least once. The pump fires with the idempotency key `timer:{TimerId}`. *Duplicate* and *NotFound* are different outcomes:

- ***Duplicate*** means the idempotency key was already processed. The resume is already in the queue, so the pump deletes the timer the same way it does for *Dispatched*.
- ***NotFound*** is only for an already-consumed bookmark (or one that will never match). **Within** the grace window (`NotFoundGrace`) the pump treats that as "a very short delay is still saving its bookmark" and retries; **after** the window it deletes the timer.

A consumed single-use bookmark therefore cannot resume the workflow twice.

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

## Known limits

- **Due-time lookup.** The EF timer store's composite index includes `DueTimeUtcTicks`. `ListDueAsync` filters due timers, orders them and `Take(limit)`s in the database. `MaxTimersPerTick` is that limit.
- **Atomic timer registration (proposed).** Registering the timer via a post-commit
  `RegisterDurableTimer` intent would make the timer==bookmark lifecycle fully atomic, at the cost of
  editing the core create-bookmark handler and the single-implementation post-commit intent dispatcher.

## How this is tested

| Claim | Test |
|---|---|
| Deleting on *Dispatched* is safe | `DurableTimerRestartCrashTests.DeleteOnDispatched_IsSafe_ResumeSurvivesCrashBeforeDrain_AndConverges` |
| A duplicate fire is harmless | `DurableTimerRestartCrashTests.TimerFire_IsIdempotent_UnderAtLeastOnceDuplicateDelivery` |

## Where this lives in the code

| Role | Types | Location |
|---|---|---|
| `Delay` activity | `Delay`, `DelayState`, `DurableTimerElapsed` | `Essentials/src/Activities/Scheduling/Activities/Delay.cs` (package `Elsa.Activities.Scheduling`) |
| Scheduling a timer | `IDurableTimerScheduler.ScheduleAsync` | `Essentials/src/Workflows/Runtime/Core/Contracts/` |
| Timer record | `DurableTimer { TimerId, WorkflowExecutionId, StimulusType, StimulusHash, DueTime, CreatedAt, … }`, keyed by `(WorkflowExecutionId, TimerId)` | `Essentials/src/Workflows/Runtime/Core/Models/` |
| Timer store | `IDurableTimerStore` (`ListDueAsync`, `DeleteAsync`); `InMemoryDurableTimerStore` (default); `EfDurableTimerStore` (registered by `AddRuntimeEntityFrameworkCore`) | `Essentials/src/Workflows/Runtime/` |
| Timer pump | `DurableTimerPumpTask : IRecurringTask` with `MaxTimersPerTick`, `NotFoundGrace`; feature in `Elsa.Workflows.Runtime.Scheduling` | `Essentials/src/Workflows/Runtime/Scheduling/DurableTimerPumpTask.cs`, `WorkflowsRuntimeSchedulingFeature.cs` |
| Resume path | `IBookmarkResumeDispatcher.DispatchAsync(ResumeBookmark)`; `WorkflowSchedulerCommandRouter.ProcessAsync` queues via `EnqueueAsync(workItem)` before draining | `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/` |
| Crash recovery | `IRuntimeResumptionService.SweepAsync`, `ListClaimableWorkflowExecutionIdsAsync` | see [durable resumption](runtime-durable-resumption.md) |
| Recurring start triggers | `IRecurringTriggerScheduleStore`, `RecurringTriggerPumpTask`, feature `WorkflowsRuntimeRecurringTriggers` | `Essentials/src/Workflows/Runtime/Scheduling/` |
| Tests | `DurableTimerRestartCrashTests` | `Essentials/tests/Activities/Scheduling/Tests/DurableTimerRestartCrashTests.cs` |

Related: [Elsa glossary](glossary/elsa.md) · [Runtime extension points](../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md) · [Recovering after a crash](runtime-durable-resumption.md) · [Bookmark expiration](bookmark-expiration.md)

## Background

This page was the worked reference for roadmap unit **W8** of the Elsa 4 review-remediation programme (finding **E3-2**). It builds on the durable resumption spine (**W2**) and the single-writer mailbox (**W5**). An earlier version said the timer store routed through "the runtime document serializer + `ElsaRuntimeDocumentVersions` with a v1 golden fixture, following the `schedulerWorkItem` pattern (W3)"; that type no longer exists on `main`, and runtime state now evolves through EF Core migrations and schema families (see [serialization](serialization.md)). Timer/Cron *start* triggers shipped with their own recurring schedule store and pump, not as a `start-trigger` variant of the timer record. "Atomic timer registration" was tracked as *Option B*. An earlier version also stated a one-`Delay`-per-workflow limit; that was lifted by node-scoped resume targets (`WorkflowExecutableResumeTarget`, `StatefulSuspensionSupport.ResolveResumeTarget`).
