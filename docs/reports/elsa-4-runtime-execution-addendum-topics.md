# Elsa 4 Runtime Execution Addendum Topics

Status: addendum brainstorm queue for Runtime Execution Seam topics raised after the initial execution-layer decision report. This is not yet a locked decision report, Speckit spec, implementation plan, glossary entry, or constitution gate.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

Related decisions: [Elsa 4 runtime execution brainstorm decisions](elsa-4-runtime-execution-brainstorm-decisions.md).

Related plan: [Elsa 4 runtime execution action plan](elsa-4-runtime-execution-action-plan.md).

Terminology report: [Elsa 4 runtime terminology decisions](elsa-4-runtime-terminology-decisions.md).

Source evidence: [Elsa Core runtime execution layer analysis](elsa-core-runtime-execution-layer-analysis.md).

## Purpose

Capture additional runtime execution topics before the first Speckit unit is written. These topics affect execution semantics, runtime control, activity lifecycle, event-driven execution, and vocabulary. Some belong in the current execution plan; others should become a closely linked control-plane or glossary work unit.

## Placement Guidance

Add to the current execution plan:

- Await-style volatile wait versus durable suspension.
- Activity completion propagation.
- Event-driven execution and generator activities.

Track as closely linked companion work:

- Pause/resume and runtime control plane.
- Runtime terminology/glossary.

The first Speckit unit can still focus on executable artifact and execution state contracts, but it should not choose state or checkpoint shapes that make these addendum topics impossible.

## Brainstorm Queue

### 1. Await-Style Volatile Wait Versus Durable Suspension

Problem:

Elsa 3 treats waits such as `Delay` as durable suspension: commit workflow state, unload the workflow instance, and resume later from persisted state. That is correct for long-running workflows, but it does not support request-scoped workflows that want `await`-style behavior while preserving the current in-memory context, such as an HTTP request waiting a few seconds before producing a response.

Initial framing:

Elsa 4 should distinguish at least two wait modes:

```text
Durable suspension
  Commit workflow state, unload from memory, resume later by bookmark/event.

Volatile wait
  Keep execution in memory, await a task/timer/event, then continue in the same host context.
```

Possible activity result vocabulary:

```text
Complete
SuspendDurably(bookmark)
WaitVolatile(awaitable)
Fault
Cancel
```

Questions:

- Is volatile wait a first-class activity result, scheduler work item, or host capability?
- What maximum duration, memory-pressure, cancellation, and shutdown policies apply?
- Can volatile waits fall back to durable suspension, or must the author choose explicitly?
- Can HTTP context, DI scope, ambient user/tenant context, and cancellation tokens safely survive volatile waits?
- How does volatile wait interact with checkpoints and execution leases?

Parallelism note:

Volatile wait should not be conflated with true parallel activity execution. Multiple branches may be able to register concurrent in-memory waits, but workflow state mutation can still remain single-threaded through the scheduler. True parallel activity execution, where multiple activity bodies run at the same time and may observe or mutate workflow state concurrently, is a separate future exploration because it affects variable consistency, output capture, checkpoint ordering, branch cancellation/faulting, joins, and activity author thread-safety expectations.

### 2. Activity Completion Propagation

Problem:

Elsa 3 activity completion notifications bubble immediately up the hierarchy of activity execution contexts. This is simple and deterministic, but it can create cascades and tightly couple completion to parent notification behavior.

Initial framing:

Elsa 4 should investigate whether completion propagation can be represented as deterministic scheduler work instead of immediate recursive bubbling.

Possible scheduler events:

```text
ActivityCompleted
ParentCompletionEvaluation
ContinuationScheduling
Checkpoint
```

Questions:

- Is immediate bubbling actually a performance or complexity problem in real workflows?
- Can queued completion work preserve strict parent/child ordering?
- What state must parent activities observe when child completion is processed?
- Should completion propagation be synchronous within one scheduler tick but still represented as queued work?
- How do incidents, cancellation, compensation, and parallel branches affect completion ordering?

### 3. Event-Driven Execution And Generators

Problem:

Some workflow engines allow an activity inside a running workflow to generate events over time. For example, a timer inside a live workflow could repeatedly schedule downstream execution while the workflow instance remains alive. Elsa currently has triggers and bookmarks, but not a clearly named in-workflow event generator concept.

Initial framing:

Differentiate triggers from generators:

```text
Trigger
  Starts or resumes a workflow from outside.

Generator
  An activity inside a live workflow execution that emits one or more execution events over time.
```

Questions:

- Is `Generator` the right term, or should this be called an event source, emitter, producer, or recurring activity?
- Does each generated event create a new `ActivityExecution`, a scheduler work item, or both?
- Are generator emissions durable, volatile, or policy-controlled?
- Can a generator emit while its parent scope is paused, suspended, faulted, or cancelling?
- How does a generator stop: parent completion, explicit cancellation, scope exit, condition, count, timeout, or workflow completion?
- How do generator emissions interact with checkpoints and volatile wait?

### 4. Pause, Resume, And Runtime Control Plane

Problem:

"Pause workflow" can mean several different things. Camunda-style worker pausing does not map one-to-one to Elsa, especially for HTTP endpoints and in-process execution.

Initial framing:

Pause should be modeled as a runtime control-plane policy, not one universal workflow status flag.

Possible pause scopes:

```text
Definition ingress pause
Workflow execution pause
Activity execution pause
Trigger/source pause
Dispatcher/worker pause
Host drain/quiescence
```

Ingress behavior likely depends on source type:

```text
HTTP endpoint paused -> return 503, 423, or configured response
Timer paused -> suppress, skip, or coalesce missed firings by policy
Message paused -> reject, dead-letter, defer, or leave in broker
Event paused -> ignore, persist externally, route to retry/dead-letter, or refuse subscription delivery
Queue worker paused -> stop fetching or locking new work
```

Questions:

- Which pause scopes are required for Elsa 4's first runtime execution release?
- Is workflow execution pause different from ingress pause?
- Should paused executions finish current activity, stop before scheduling the next activity, or checkpoint immediately?
- Should pause be durable runtime state, operational state, ingress source state, or policy metadata?
- What is the default behavior per ingress type?
- How does pause interact with volatile waits, leases, recovery, and host drain?

### 5. Runtime Terminology And Glossary

Problem:

Elsa 3 vocabulary sometimes uses terms that overlap, such as fault and incident. Elsa 4 should formalize terminology while the execution model is being designed.

Initial framing:

Use a report for brainstormed terminology decisions, then move stable terms into `docs/glossary/`.

Initial suggested distinction:

```text
Failure
  General domain-neutral word for something going wrong.

Faulted
  Lifecycle status: workflow/activity cannot continue normally.

Incident
  Operator-visible problem record that may require attention.
```

Questions:

- Should `Fault` exist as a noun at all, or only `Faulted` as a status?
- Should `Incident` be the only persisted problem record?
- What are the canonical names for workflow execution, activity execution, executable artifact, executable node, authored activity, trigger, generator, bookmark, checkpoint, suspension, pause, and recovery?
- Should "resume" mean only durable bookmark resume, or can it also apply to volatile waits?
- Should glossary entries be added incrementally after each locked decision, or in one terminology pass before Speckit?

## Suggested Brainstorm Order

1. Await-style volatile wait versus durable suspension.
2. Activity completion propagation.
3. Event-driven execution and generators.
4. Pause, resume, and runtime control plane.
5. Runtime terminology and glossary.

## Locked Addendum Decisions

### 1. Volatile Wait Versus Durable Suspension

Elsa 4 separates waiting from suspending.

```text
Durable suspension
  Commit state, unload from memory, resume later by bookmark/event.

Volatile wait
  Keep execution in memory, await a task/timer/event, then continue in the same host context.
```

Volatile wait semantics:

- Volatile wait is scoped to an `ActivityExecution` and branch, not the whole workflow.
- Multiple concurrent volatile waits may exist in one workflow execution.
- A volatile wait does not mean the workflow is durably suspended.
- The workflow remains in memory while it has runnable work or active volatile waits.
- When a volatile wait completes, it enqueues deterministic scheduler work for the owning activity execution/branch.
- Workflow state mutation remains single-threaded through the scheduler.
- True parallel activity execution is explicitly deferred as a separate future exploration.

Guardrails to carry forward:

- Maximum duration.
- Host shutdown behavior.
- Request cancellation behavior.
- Memory pressure policy.
- Execution lease behavior.
- Durable fallback policy.

### 2. Activity Completion Propagation

Elsa 4 models activity completion propagation as deterministic scheduler work, not immediate recursive bubbling.

Conceptual flow:

```text
ActivityCompleted
  -> ParentCompletionEvaluation
  -> ContinuationScheduling
  -> Checkpoint
```

Rules:

- Completion work is queued internally.
- The scheduler drains completion-related work deterministically before advancing to unrelated work.
- Parent activities observe completed child state before continuations run.
- Joins evaluate only after required branch completions are recorded.
- Incidents and cancellation can interrupt completion drain in a controlled way.
- This is not fire-and-forget async events; it is ordered internal scheduler work.

Rationale:

This keeps completion inspectable and queue-shaped while preserving deterministic execution ordering.

### 3. Event-Driven Execution And Generators

Elsa 4 distinguishes triggers from generators.

```text
Trigger
  External source that starts or resumes a workflow.

Generator
  In-workflow activity that emits one or more execution events over time.
```

Runtime model:

- A generator owns a long-lived `ActivityExecution`.
- Each generator emission creates scheduler work.
- Downstream activities get their own `ActivityExecution`s as usual.
- The emission itself is tracked as scheduler/history data and does not have to become a full activity execution.
- Emissions can be volatile, durable, or policy-controlled.
- Generator lifetime is tied to its owning execution scope.
- Each emission should have identity for diagnostics and ordering.

Possible emission model:

```csharp
GeneratedEvent
{
    WorkflowExecutionId
    GeneratorActivityExecutionId
    EmissionId
    EmissionName
    Payload?
    OccurredAt
}
```

Generator lifetime is defined by owning scope lifetime, generator stop policy, and runtime control policy.

Default scope-end rule:

```text
A generator ends when its owning execution scope ends.
```

Examples:

- Workflow completes.
- Parent composite completes.
- Branch is cancelled.
- Activity execution is cancelled.
- Scope faults and does not continue.

Generator stop policies can include:

- Repeat count.
- Time window or expiration.
- Expression condition.
- External signal.
- No subscribers/downstream paths remain.

Runtime control can affect a generator:

- `Paused`: do not emit now; may resume later.
- `Completed`: generator is done.
- `Cancelled`: generator was terminated by control/failure.
- `Suspended`: durable state recorded; generator may be rehydrated later if durable.
- `Faulted`: generator cannot continue because of failure.

Backpressure/failure policy:

- Runtime may stop, throttle, pause, or fault a generator when emissions cannot be safely processed.
- Downstream activity failure does not automatically stop the generator unless generator policy says so.

Detached behavior:

By default, a generator does not outlive its owning workflow/scope. Detached recurring behavior belongs to trigger/scheduler infrastructure, not an in-workflow generator.

### 4. Pause, Unpause, Suspension, And Resume

Elsa 4 models pause/unpause as runtime control-plane policy with explicit scopes. Pause is not the same as durable suspension.

Pause scopes:

```text
Ingress pause
  Stop accepting new external starts/resumes for a definition/source.

Workflow execution pause
  Stop advancing a specific workflow execution after a safe boundary.

Activity/generator pause
  Stop or suppress activity-local emissions/work without completing it.

Worker/dispatcher pause
  Stop fetching or dispatching runtime work.

Host drain
  Operational shutdown mode; finish/checkpoint active work and stop taking new work.
```

Workflow execution pause is cooperative:

```text
Pause requested
  -> runtime records control-plane pause state
  -> current activity may finish, reach volatile wait, reach durable suspension, or checkpoint
  -> scheduler stops before starting unrelated/new activity executions
  -> unpause allows scheduler advancement again
```

Rules:

- Pause does not normally abort currently executing activity code.
- Pause prevents scheduler advancement after a safe boundary.
- Pause is distinct from cancel, terminate, drain, and durable suspension.
- Pause state belongs primarily to `ControlPlaneState`, not ordinary workflow execution state.
- Workflow execution state may reference or reflect effective pause, such as `Status = Running` with `ControlState = Paused`.

Safe pause boundaries:

```text
Before starting a new ActivityExecution
After ActivityCompleted propagation drains
After Checkpoint commit
When entering DurableSuspension
When registering VolatileWait
Before processing a Generator emission
Before dispatching post-commit work
```

Unsafe pause boundaries:

```text
mid-activity method execution
mid-state mutation
mid-checkpoint commit
mid-output capture
mid-parent completion evaluation
mid-transaction/outbox commit
```

Volatile wait policy:

- If pause is requested while a volatile wait is registered, the wait may remain registered.
- When the volatile wait completes, it enqueues scheduler work.
- The scheduler checks pause state before resuming the branch.
- User workflow pause defaults to strict pause: do not resume volatile continuations while paused.
- Host drain defaults to drain-in-flight: allow already-registered continuations to finish, but start no new activity executions.

Ingress pause defaults:

```text
HTTP Endpoint
  Do not queue requests by default.
  Return configured response.
  Default: 503 Service Unavailable.
  Optional: 423 Locked for administrative pause semantics.

Timer
  Do not enqueue every missed tick by default.
  Default: skip while paused.
  Optional: coalesce one missed tick, catch up all missed ticks, fire immediately on unpause.

Message / Queue
  Prefer broker-native backpressure.
  Default: stop fetching/locking messages when paused.
  Already-delivered message behavior is adapter policy: abandon, defer, nack, requeue, or dead-letter.

External Event / Webhook
  Request/response webhook: return configured unavailable response.
  Durable event stream: stop checkpointing/advancing consumer offset, or pause subscription where supported.

Manual/API Start
  Reject with clear paused-state error by default.
```

Principle:

```text
If the ingress source has durable buffering, use the source's native buffering.
If it is synchronous request/response, return a clear paused response.
```

Terminology:

```text
Pause
  Administrative hold.

Unpause
  Remove administrative hold.

Suspend
  Persisted execution wait/unload boundary.

Resume
  Continue from durable suspension/bookmark.

Wait
  Activity/execution is waiting.

Continue
  Proceed after a volatile wait or internal scheduler event.
```

API and command naming should qualify behavior:

```http
POST /workflow-executions/{id}/pause
POST /workflow-executions/{id}/unpause
POST /bookmarks/{id}/resume
```

```csharp
PauseWorkflowExecution
UnpauseWorkflowExecution
ResumeBookmark
ContinueVolatileWait
```

Resume is reserved for runtime continuation from durable suspension/bookmarks. Unpause is used for reversing administrative pause. Continue is used for volatile wait and internal scheduler continuations.

### 5. Runtime Terminology And Glossary

Elsa 4 should use a formal glossary for canonical runtime terms. During design, terminology decisions may first be captured in a report, then promoted into `docs/glossary/` when stable.

Initial core vocabulary:

```text
Authored Workflow Document
  Design-owned durable authoring/import/export shape.

Workflow Executable
  Runtime-owned compiled artifact that can be executed.

Executable Node
  Runtime node inside the executable artifact.

Workflow Execution
  One running or resumable execution of a workflow executable.

Activity Execution
  One concrete execution of one executable activity node.

Trigger
  External source that starts or resumes workflow execution.

Generator
  In-workflow activity that emits execution events over time.

Bookmark
  Durable runtime resume handle.

Checkpoint
  Named persistence boundary for runtime state changes and post-commit intents.

Pause
  Administrative hold.

Unpause
  Remove administrative hold.

Suspend
  Enter durable persisted wait/unload state.

Resume
  Continue from durable suspension/bookmark.

Wait
  Activity/execution is waiting.

Continue
  Proceed after volatile wait or internal scheduler event.

Incident
  Operator-visible problem record.

Faulted
  Lifecycle status meaning execution cannot continue normally.

Failure
  Generic explanatory word for something going wrong.
```

Fault terminology:

- Use `Faulted` as a lifecycle status.
- Use `Incident` as the persisted problem record.
- Use `Failure` as a generic explanatory word.
- Avoid `Fault` as a noun in new Elsa 4 model names unless referring to legacy/Elsa 3 behavior.

## Plan Impact

The current action plan should be amended after this addendum is reviewed:

- Slice 3, checkpoint contract, must account for volatile waits and durable suspension.
- Slice 4, pipeline slots, must account for completion propagation strategy.
- Slice 5, bookmark resume contract, must distinguish durable resume from volatile continuation.
- Slice 6, input bindings and durable value capture, must account for generator emissions and in-memory waits.
- Slice 8, operational recovery and outbox, should include pause/resume control-plane boundaries or explicitly defer them to a companion plan.
- A glossary/terminology report should be created before final Speckit wording freezes public concepts.
