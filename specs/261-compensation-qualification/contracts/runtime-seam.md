# Contract: R-7 delayed child schedule

A generic runtime seam: a structural activity schedules a child that becomes deliverable at a due time. It follows R-1 to R-6 (specs 258 and 260): Workflows.Runtime and Activities.Runtime know no compensation term, and the seam is proven with a test structural activity before the scope uses it. Evidence and alternatives are in [research R1–R3](../research.md).

## Surface

```csharp
// Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionContext.cs (new overload; existing callers unchanged)
void ScheduleChildActivity(
    string executableNodeId,
    string? schedulingActivityExecutionId,
    IReadOnlyDictionary<string, string>? metadata,
    ActivitySchedulingProvenance? schedulingProvenance,
    LoopIterationScopeRequest? iterationFrame,
    DateTimeOffset? notBefore);

// Workflows/Runtime/Core/Models/RuntimeChildActivityScheduleRequest.cs
public DateTimeOffset? NotBefore { get; }       // null = deliver at commit, as today

// Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs
// RuntimePostCommitIntent gains:
public DateTimeOffset? NotBefore { get; }       // the outbox item's AvailableAt; null = RecordedAt, as today

// Workflows/Runtime/Core/Options (new)
public sealed class RuntimeDelayedScheduleOptions
{
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromHours(24);
}

// Workflows/Runtime/Core/Contracts (new, registered by the feature that registers the outbox sweep)
public interface IRuntimeDelayedScheduleCapability { }
```

## Staging rules

| Situation | Result |
|---|---|
| `notBefore` is null, or at or before the evaluation's commit time | Scheduled as today; nothing changes on any path. |
| `notBefore` later than commit time, within `MaxDelay` of the runtime clock | Delayed schedule (below). |
| `notBefore` more than `MaxDelay` ahead | The evaluation faults with `runtime.delayed-schedule.too-far`. |
| No `IRuntimeDelayedScheduleCapability` registered | The evaluation faults with `runtime.delayed-schedule.unavailable`. |
| Staged from an evaluation that cannot schedule children | The existing refusal for that evaluation applies, unchanged. |

Several delayed and immediate schedules may be staged in one evaluation; each is its own intent.

## Commit

In the evaluation's checkpoint, atomically with its continuation, private state, absorptions and other requests:

1. The child's schedule work item is materialized exactly as for an immediate schedule: the same work-item id rule, the minted activity execution id, the scheduling provenance and metadata, and the pinned executable identity.
2. It is staged as an `EnqueueSchedulerWork` post-commit intent with `NotBefore = notBefore`. Its outbox item is written `Pending` with `AvailableAt = notBefore`.
3. The checkpoint is mandatory: it is durable in Durable, Balanced and Ephemeral alike.

## Delivery

| Path | Rule |
|---|---|
| In-process hop (`RuntimeCheckpointCommitter.PublishInProcessHopWorkItems`) | Skips an intent whose `NotBefore` is after the commit time. |
| Immediate post-commit dispatch | Skips it the same way; the item stays `Pending`. |
| Coalescing overlay | Delivers it only once `AvailableAt <= now` (existing rule); an undelivered one is flushed as a durable `Pending` item when the session ends. |
| Outbox sweep (`RuntimeResumptionService`, every `RuntimeResumptionOptions.SweepInterval`) | Claims it once due and enqueues the work item into the run's queue; at-least-once, idempotent by work-item id (existing). |

The run's scheduler queue never holds a delayed item before its due time, so it holds back no other work of the run.

## Handling the delivered schedule

`WorkflowScheduleActivitySchedulerWorkHandler` acknowledges the work item and creates no child when:

- the run is terminal or no longer exists;
- the scheduling parent's committed status is not `Running` (cancelled, completed or faulted) — the same "parent not running" gate as FR-405 of spec 260;
- the execution-scope owner is cancelled (existing).

Otherwise it schedules the child as for any schedule work item, against the pinned executable named in the item.

## Liveness and retention

- A run whose only outstanding work is pending delayed items stays `Running`. The recovery candidate selector and run health treat a pending, not-yet-available outbox item as expected work, not as a stall, and do not re-drive the run before it is due.
- The executable named by a pending delayed item stays retained: the run is not terminal, so its retention root holds.
- Deleting a run requires a terminal run, so a delayed item normally cannot outlive its run's history. One that reaches a deleted run is dropped (row 1 of the handling table).

## Observability

- The delayed child has no activity execution until it is delivered. Its id is known from the materialized work item.
- A consumer that wants to show the wait records `notBefore` in its own state (the scope does: attempt `notBefore`).
- Runtime log events when a delayed item is staged and when it is delivered or acknowledged without scheduling, with the run, the parent and the due time.

## Tests (Phase A, test structural activity)

| ID | Case | Expect |
|---|---|---|
| D1 | Delay 0 or in the past | Same commit and queue as an immediate schedule. |
| D2 | Delay 60 s, clock moved 59 s, sweep | No child. Clock to 60 s, sweep: child scheduled once. |
| D3 | Delayed item pending, another notification arrives for the same run | The notification is processed at once. |
| D4 | Host killed after the commit, restarted after the due time | Child scheduled once at the first sweep. |
| D5 | Parent cancelled, run cancelled or run faulted before the due time | Item acknowledged; no child. |
| D6 | `MaxDelay` exceeded; capability missing | The evaluation faults with the matching code; nothing committed. |
| D7 | Balanced and Ephemeral | D2 and D4 hold; the item survives the end-of-drain flush. |
| D8 | Only a delayed item outstanding | The run stays `Running`; no recovery candidate before the due time. |
| D9 | The same evaluation replayed after a crash before the commit | One item, same work-item id. |
