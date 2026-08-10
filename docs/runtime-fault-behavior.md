# Runtime fault behavior map

> **Audience:** engineers working on or reading the Elsa 4 runtime.
> **Purpose:** answer "what happens when an activity throws" and "what happens when a handler throws"
> from one place, and keep the two apart. They are different paths with different outcomes, and
> conflating them is the specific mistake this doc exists to prevent.
> **Knowledge role:** worked reference. It describes behavior and the decisions behind it; it is not a
> registration inventory. Canonical short definitions live in
> [`docs/glossary/elsa.md`](glossary/elsa.md), and the extension-point contracts live in
> [`src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`](../src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md).

## Why this doc exists

Reading `WorkflowSchedulerDrainer` end to end tells you almost nothing about what a fault does to a
workflow. Every collaborator that decides an outcome sits behind an interface the drain loop does not
name: the activity fault is caught inside a work handler, the decision to terminate the workflow is
made by an authored strategy applied by a drain observer, and a handler crash is projected into an
incident by a different drain observer. That separation is deliberate. It also means a careful reader
working from the hot path reaches the wrong conclusion, which is what happened to three of the seven
findings in the execution model comparison that produced backlog item D1 (issue #1226).

So this map starts from the observer list and the defaults, not from the loop.

## The short answer

| | An **activity** throws | A **handler** throws |
| --- | --- | --- |
| Caught by | the scheduler work handler that invoked it | `WorkflowSchedulerDrainer.DispatchAsync` |
| Recorded as | a blocking `IncidentState` (severity `Error`), plus the activity state moved to `Faulted` | a `RuntimeSchedulerPoisonRecord` in the poison store |
| Dispatch result | `Completed`, so the drain loop keeps going | `Faulted`, so the drain loop breaks |
| Decided by | the workflow's authored `IIncidentStrategy`, applied at quiescence | `IRuntimeDomainRetryPolicy`, applied immediately |
| Default outcome | workflow status becomes `Faulted` (default strategy is `Fault/1`) | item parked as `Poisoned`, projected into a blocking incident; **the workflow stays `Running`** |
| Activity state | `Faulted` with a fault sub-status | untouched, it stays whatever it was |
| Retried by default | no | no (`NoopRuntimeDomainRetryPolicy` returns an explicit `DoNotRetry`) |

The single most load-bearing difference: an activity fault never reaches the drainer's fault arm, and
a handler fault never reaches the incident strategy.

## Path A: an activity throws

### Where it is caught

Inside the work handler, not in the drain loop.
`WorkflowInvokeActivitySchedulerWorkHandler.InvokeActivityAsync` wraps activity work in three
sequential fault boundaries, and each one funnels into the same private `RecordFaultAsync`:

| Boundary | Recorded sub-status |
| --- | --- |
| durable-value listing, contract and input-snapshot checks | `InputMaterializationFailed` |
| attempt claim, activation, execution-context construction | `ActivityConstructionFailed` |
| the activity's own execution | `ActivityFaulted` |
| activation-lease disposal failing on any of the above | `ActivityDisposalFailed` |
| the activity returning a fault transition rather than throwing | `ActivityReturnedFault` |

`OperationCanceledException` is re-thrown from every arm when cancellation was requested; a genuine
cancel is not a fault. The other three activity-facing handlers (`WorkflowResumeBookmark...`,
`WorkflowParentActivityCompletion...`, `WorkflowNotifyParentActivity...`) use the same recorder with
the same shape.

### What it records

`ActivityFaultIncidentRecorder.CommitAsync` commits one `IncidentRecorded` checkpoint holding both:

- the activity execution state as `Faulted` (fault sub-status, `FaultCount` and `AggregateFaultCount`
  incremented, incident id appended, owned container frames closed, open attempt closed with a `Fault`
  transition kind); and
- an `IncidentState` with severity `Error`, status `Blocking`, and **`ResolutionOutcome` null**.

The null resolution outcome is the signal that no one has decided anything yet. Everything downstream
keys off it.

The handler then returns normally. The dispatch is a `Completed` work-item result, the drain loop
continues, and no poison record is written.

### Who decides whether the workflow dies

`IncidentStrategyResolutionDrainObserver`, and only once the outer drain has reached quiescence:

```
if (result.StopReason != RuntimeSchedulerDrainStopReason.Quiesced)
    return;
```

Quiescence is decided by `WorkflowDrainOrchestrator.DrainSchedulerAndPostCommitWorkAsync`: the drain
plus post-commit outbox delivery cycle until a cycle delivers nothing. The observer then collects
blocking incidents that have no resolution outcome **and** carry an `ActivityExecutionId`, and hands
them to `IncidentResolutionBatchExecutor`.

The executor resolves the incident strategy pinned on the executable (`WorkflowExecutable.IncidentStrategy`,
fixed at publish, not read from live configuration), asks it for an action, and applies the whole batch
in one `IncidentResolutionBatchApplied` checkpoint:

| Built-in action | Effect on the incident | Effect on the workflow |
| --- | --- | --- |
| `FaultWorkflow` | stays `Blocking` | status becomes `Faulted` in the same commit |
| `ContinueWithIncidents` | becomes `Open` | unchanged; the run continues |
| `WaitForIntervention` | stays `Blocking` | unchanged |

`FaultWorkflow` is what the default gives you: `IncidentStrategyCatalogOptions.DefaultStrategy` is
`Fault/1`, `WorkflowExecutableCompiler` pins that reference when nothing is authored, and
`FaultIncidentStrategy` returns `FaultWorkflow` unconditionally.

The executor is fail-closed twice over. If the strategy cannot be resolved, throws, or returns null,
the action falls back to `FaultWorkflow`; if a resolved action throws while executing, its staging is
discarded and `FaultWorkflow` is applied to a fresh staging context. Both fallbacks stamp the outcome
with system source `IncidentStrategyFailure` plus a `phase` metadata entry, so a fault-by-fallback is
distinguishable from a fault the strategy actually chose.

Note that quiescence is measured by outbox delivery, not by the scheduler queue being empty, and the
two can disagree. `WorkflowSchedulerDrainer`'s loop has four exits: the queue runs dry, an item faults,
an item pauses, or the per-drain work-item budget (`RuntimeSchedulerDrainRequest.MaxWorkItems`,
unbounded by default) is exhausted. Budget exhaustion is not a distinct stop reason anywhere. The
drainer reports no explicit reason, `RuntimeSchedulerDrainResult` infers `Quiesced` from an
all-completed item set, and the aggregate reason the observers actually see is seeded `Quiesced` by the
orchestrator and never reconsiders the budget. So a budget-truncated drain looks exactly like a settled
one, and the strategy is applied while work is still queued.

### The backstop, and when it takes over

`BlockingIncidentWorkflowFaultObserver` runs after the strategy observer and faults the workflow
directly, committing a `WorkflowFaulted` checkpoint. It is the reason an incident recorded on a drain
that did **not** quiesce (it stopped on a fault or a pause) still terminalizes the run instead of
leaving it `Running` forever.

It returns without doing anything when any of these hold:

- the workflow is missing or already terminal;
- no blocking incident has a null `ResolutionOutcome` (so anything the strategy observer already
  decided is left alone, including a deliberate `ContinueWithIncidents`);
- every remaining blocking incident is an `ArtifactActivationFailed` incident. A missing consumer or
  schema is a deployment problem, so the run is kept recoverable while deployment is corrected.

When it does fault the workflow, it also walks each incident activity's ancestor chain and marks the
non-terminal ancestors `Faulted` with sub-status `BlockingIncident`, so an enclosing container does not
sit `Running` under a dead workflow. Sibling branches are not touched.

### One special case worth knowing

An activation failure (`ArtifactActivationFailed`, classified by `ActivityActivationFailureHandler`)
is recorded differently at the source: the activity is left `Waiting` rather than `Faulted`, and the
incident is written with a `WaitForIntervention` outcome and system source `ActivityActivationFailure`
already attached. That non-null outcome is what makes both downstream observers skip it.

## Path B: a handler throws

### Where it is caught

`WorkflowSchedulerDrainer.DispatchAsync`, in the general `catch (Exception)` arm. Three exception
types are filtered out ahead of it and re-thrown untouched, because none of them is a decided outcome
about this item:

- `OperationCanceledException`, a real cancel or shutdown. The item must stay queued.
- `RuntimeSchedulerWorkClaimLostException`, so a successor may own the work. Never ack, never poison.
- `RuntimeSchedulerWorkConsumeConflictException`, where the atomic commit's fence-checked consume lost to a
  successor; the commit rolled back and persisted nothing.

A dispatch that breaches `RuntimeSchedulerWorkClaimOptions.MaxDispatchDuration` deliberately throws a
`RuntimeSchedulerDispatchDeadlineExceededException`, which is neither of the above and therefore lands
here on purpose: a hung dispatch becomes a bounded, visible outcome. A work item no handler accepts
also lands here, via the internal `FaultingMissingSchedulerWorkHandler`.

### What it does

1. Captures fault info (and inner fault info) through `IRuntimeFaultCapturePolicy`.
2. **Ack-deletes the work item first**, before any poison handling. A handler fault is a decided
   outcome, not a crash, so the source item must leave the queue or a deterministically-poisoning
   handler would be redelivered forever. (A process crash never reaches this line, which is exactly
   the redrive safety the claim protocol provides.) The ack is skipped when a checkpoint commit inside
   this dispatch already consumed the item durably.
3. Calls `HandleHandlerCrashAsync`, which asks `IRuntimeDomainRetryPolicy` what to do and records a
   `RuntimeSchedulerPoisonRecord` whichever answer comes back (subject to the poison-store caveat
   below):

| Retry mode | Disposition | Requeue |
| --- | --- | --- |
| `RetryNow` | `RetryScheduled` | re-enqueued immediately through the queue contract |
| `RetryAfter` | `RetryScheduled` | not re-enqueued; `NextRetryAt` is left for the durable resumption pump (see [durable resumption](runtime-durable-resumption.md)) |
| `DoNotRetry`, `Fault`, or no policy | `Poisoned` | none |

4. Returns a `Faulted` work-item result. `DrainAsync` breaks out of its loop, and the orchestrator's
   aggregate stop reason becomes `Faulted`.

The **default** retry policy is `NoopRuntimeDomainRetryPolicy`, which returns an explicit `DoNotRetry`
decision (not a null, not a no-op): a handler fault parks as `Poisoned` on the first failure. The
default poison store is `InMemoryWorkflowSchedulerPoisonStore`, so poison records are process-local
until a durable provider is composed.

One composition edge case is worth knowing, because it inverts everything above. The drainer takes its
poison store as an optional constructor parameter, and `HandleHandlerCrashAsync` returns immediately
when none was supplied. Step 2 has already ack-deleted the work item by then, so with no poison store
the item is gone with no trace: nothing recorded, no incident ever projected, and the retry policy
never consulted, which means even a `RetryNow` decision does not re-enqueue. `AddWorkflowRuntime`
registers the in-memory store with `TryAddSingleton`, so no host reaches this by default. It is
reachable by constructing the drainer directly, which tests and custom compositions do.

### How it becomes visible

`PoisonedSchedulerWorkIncidentObserver` runs ahead of both other observers, and only when the drain
result reports a faulted item. It lists the poison records for the execution, skips anything still
`RetryScheduled`, and commits one `IncidentRecorded` checkpoint per `Poisoned` record: severity
`Critical`, status `Blocking`, and a resolution outcome of `WaitForIntervention` with system source
`PoisonedSchedulerWork`.

Two properties matter for operators. Incident ids are deterministic per work item and an existing
incident is never overwritten, so a resolved incident stays resolved and repeated drains are
idempotent. And incident recording is best-effort surfacing of an already-durable poison record: a
persistence failure is caught and logged with the original fault, and the drain continues.

### What it does not do

It does not terminate the workflow. The `WaitForIntervention` outcome is non-null, so
`BlockingIncidentWorkflowFaultObserver` filters the incident out and leaves the workflow in its
existing status; the strategy observer skips it too, both because the drain stopped on a fault rather
than quiescing and because the incident carries no `ActivityExecutionId`. It also does not touch
activity state: the activity that was mid-dispatch stays whatever it was, typically `Scheduled` or
`Running`.

So the honest one-line answer to "what happens by default when a handler faults" is: **the work item
is dropped from the queue, parked as poisoned with no retry, and surfaced as a blocking critical
incident, while the workflow itself stays `Running` and needs an operator.**

## The observer chain

Observers run in registration order inside `WorkflowDrainOrchestrator.NotifyObserversAsync`, after the
drain has finished (and after the coalescing flush, on a coalescing host). The order is load-bearing
and the registrations say so:

1. `NoopWorkflowSchedulerDrainObserver`, a placeholder.
2. `PoisonedSchedulerWorkIncidentObserver`, poison records to blocking incidents. Registered before
   the fault observer so the incident it projects is visible in the same notification pass.
3. `IncidentStrategyResolutionDrainObserver`, the authored strategy, at quiescence only.
4. `BlockingIncidentWorkflowFaultObserver`, the backstop that terminalizes on an undecided blocking
   incident.

For an **ordinary** observer exception, `NotifyObserversAsync` does not stop at the first failure: it
runs every observer, collects the exceptions, and throws a single `AggregateException` at the end. One
broken observer therefore cannot silently suppress the ones behind it, though the aggregate does
surface out of the drain call.

**Cancellation is the exception, and it truncates the chain.** A `catch (OperationCanceledException)
when (cancellationToken.IsCancellationRequested)` arm sits ahead of the general catch and throws
immediately rather than continuing the loop: bare when nothing had failed yet, or as an
`AggregateException` carrying the earlier failures when something had. Either way the observers behind
it never run.

That has a visible consequence on host shutdown mid-drain. If the poison observer (2nd of 4) is the
one that observes cancellation, neither the strategy observer nor the fault observer runs, so an
activity-fault blocking incident is left with a null `ResolutionOutcome` and the workflow stays
`Running` with nobody having decided its outcome. The incident is durable and the next drain of that
execution will resolve it, but until then the run looks undecided rather than faulted.

The registrations themselves live at the end of `RuntimeCoreServiceCollectionExtensions.AddWorkflowRuntime`.
Issue #1230 (backlog item C2) is building a test-asserted inventory of the default registrations;
when it lands, cite it for the exhaustive list rather than duplicating one here.

## The defaults that decide outcomes

| Seam | Default | What it means |
| --- | --- | --- |
| `IIncidentStrategy` (per workflow, pinned at publish) | `Fault/1` (`FaultIncidentStrategy`) | an unhandled activity fault terminalizes the run |
| `IRuntimeDomainRetryPolicy` | `NoopRuntimeDomainRetryPolicy` → `DoNotRetry` | no handler-fault retries; poison on first failure |
| `IWorkflowSchedulerPoisonStore` | `InMemoryWorkflowSchedulerPoisonStore` | poison records are process-local until a durable provider is composed |
| `IRuntimeFaultCapturePolicy` | `DefaultRuntimeFaultCapturePolicy` | what of the exception reaches durable state |
| `IIncidentStateStore` | `InMemoryIncidentStateStore` | same caveat as the poison store |

The four seams are registered with `TryAdd`, so a host or module that registers its own first wins.
The incident strategy is different in kind: it is not a single registered default but a per-workflow
reference pinned into the executable at publish, resolved against the strategy catalog at runtime.
Changing the catalog default only affects workflows published afterwards.

## Where each claim comes from

| Claim | Source |
| --- | --- |
| activity faults are caught in the work handler and funnel to one recorder | `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs` (`InvokeActivityAsync` fault arms, `RecordFaultAsync`) |
| the recorded incident is `Blocking` with a null resolution outcome, and the activity goes `Faulted` | `src/Elsa/Activities/Runtime/Services/ActivityFaultIncidentRecorder.cs` (`NewIncident`, `NewFaultedActivityState`) |
| activation failures are recorded pre-resolved and leave the activity `Waiting` | `ActivityFaultIncidentRecorder.NewIncident` / `NewFaultedActivityState`, `src/Elsa/Workflows/Runtime/Services/ActivityActivationFailureHandler.cs` |
| the strategy observer runs only at quiescence and only for activity-scoped undecided incidents | `src/Elsa/Workflows/Runtime/Services/IncidentStrategyResolutionDrainObserver.cs` |
| the strategy is pinned on the executable and applied in one checkpoint, with a fail-closed fallback to `FaultWorkflow` | `src/Elsa/Workflows/Runtime/Services/IncidentResolutionBatchExecutor.cs` |
| `FaultWorkflow` keeps the incident blocking and faults the workflow; `ContinueWithIncidents` opens it and does not | `src/Elsa/Workflows/Runtime/Core/Services/Strategies/IncidentResolutionActions.cs` |
| the default strategy is `Fault/1` | `src/Elsa/Workflows/Runtime/Core/Models/IncidentStrategyCatalogOptions.cs`, `src/Elsa/Workflows/Publishing/Services/WorkflowExecutableCompiler.cs` (`ResolveIncidentStrategy`), `src/Elsa/Workflows/Runtime/Services/FaultIncidentStrategy.cs` |
| the fault observer skips terminal workflows, decided incidents, and activation failures; it faults non-terminal ancestors | `src/Elsa/Workflows/Runtime/Services/BlockingIncidentWorkflowFaultObserver.cs` |
| handler faults are caught in the dispatch arm, with claim-lost / consume-conflict / cancellation excluded | `src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerDrainer.cs` (`DispatchAsync`) |
| ack-before-poison, and the retry-mode ladder | `WorkflowSchedulerDrainer.HandleHandlerCrashAsync` |
| the dispatch deadline is routed into the poison ladder on purpose | `WorkflowSchedulerDrainer.RenewClaimUntilStoppedAsync`, `RuntimeSchedulerDispatchDeadlineExceededException` |
| the default retry policy returns an explicit `DoNotRetry` | `src/Elsa/Workflows/Runtime/Services/NoopRuntimeDomainRetryPolicy.cs` |
| poison records become blocking critical incidents with a `WaitForIntervention` outcome, idempotently and best-effort | `src/Elsa/Workflows/Runtime/Services/PoisonedSchedulerWorkIncidentObserver.cs` |
| observer order, and the defaults table | `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs` |
| quiescence is the orchestrator's aggregate stop reason, and observers are notified after the drain | `src/Elsa/Workflows/Runtime/Services/WorkflowDrainOrchestrator.cs` (`DrainSchedulerAndPostCommitWorkAsync`, `NotifyObserversAsync`) |

Behavioral guards worth reading alongside the code:

- `tests/Elsa/Activities/Runtime/Tests/FaultIncidentExecutionTests.cs`:
  `FaultActivity_WorkflowFault_IsAuthoredByTheIncidentStrategy` and
  `FaultActivity_WithContinueWithIncidentsStrategy_LeavesTheWorkflowRunning` establish, end to end,
  that the strategy and not the fault observer decides Path A's outcome.
- `tests/Elsa/Workflows/Runtime/Tests/PoisonedSchedulerWorkIncidentObserverTests.cs`:
  `ObserverChain_PoisonedRecord_PreservesSystemWaitAndNonterminalWorkflow` establishes that Path B
  leaves the workflow non-terminal.
- `tests/Elsa/Workflows/Runtime/Tests/WorkflowSchedulerPoisonDrainTests.cs`: the retry-mode ladder,
  including that the default policy neither retries nor re-enqueues.
- `tests/Elsa/Workflows/Runtime/Tests/BlockingIncidentWorkflowFaultObserverTests.cs`: every skip
  condition and the ancestor-faulting rule.
- `tests/Elsa/Workflows/Runtime/Tests/IncidentResolutionBatchExecutorTests.cs`: batch atomicity and
  both fail-closed fallbacks.

## Not covered here

- Retention, resolution APIs, and the operator surfaces that act on an incident once it exists.
- The drain and claim protocol itself. See [durable resumption](runtime-durable-resumption.md) for
  crash windows and redelivery, ADR 0029 for the execution pipeline, ADR 0031 for the burst drain, and
  ADR 0032 for checkpoint cadence.
- Cancellation. A cancel is not a fault on either path and is committed by its own checkpoint service.
- Child-fault propagation to a parent fork/join. The invoke handler rides a parent-evaluation work item
  on the incident checkpoint so a join resolves deterministically instead of waiting for a completion
  that never arrives; the joining semantics belong with the container activities, not here.
