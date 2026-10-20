# Research: nested, repeated and confirmed compensation (M3)

Citations are to `main` plus the M2 branch (`agent/claude/3045-compensation-uncertainty`) as of 20 October 2026. Paths are under `Essentials/src/` unless they say otherwise.

## R1 — A completed scope keeps no state, so the parent must hold the entries

- **Evidence:**
  - `StructuralParentEvaluationSupport.CompleteParentActivity` sets `PrivateState = null` when a structural parent completes after a child (`Activities/Runtime/Elsa.Activities.Runtime/Services/StructuralParentEvaluationSupport.cs:412-432`, line 429). The invoke and resume paths do the same (`WorkflowInvokeActivitySchedulerWorkHandler.cs:1327`, `WorkflowResumeBookmarkSchedulerWorkHandler.cs:477`).
  - Cancellation keeps private state: `ActivityCancellationCheckpointService.cs:45-52` and `ActivitySubtreeCancellationPlanner.cs:36-44` change status and metadata only.
- **Decision:** OD8. On `Done`, the inner scope sends its eligible entries to the enclosing scope in the same checkpoint as its completion. Nothing is retained on the completed scope. Cancelled scopes keep their journal (OD10).
- **Alternatives considered:**
  - Keep completed private state and let the parent read it. Rejected: it changes a runtime-wide rule, and the parent would read another activity's private state.
  - A workflow-level registry. Rejected by OD8.

## R2 — Ancestor notification exists but cannot target a scope

- **Evidence:**
  - `IRuntimeActivityExecutionContext.RequestAncestorNotification(ancestorConsumerKey, code, payload)` (`Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionContext.cs:131-139`).
  - The target is the nearest ancestor whose node's `Descriptor.ConsumerKey` equals the key (`Activities/Runtime/Elsa.Activities.Runtime/Services/ParentNotificationEvaluation.cs:197-221`, match at 212).
  - Consumer keys are runtime consumer kinds, not activity types: `elsa.clr-activity`, `elsa.graph-activity`, `intrinsic` (`Activities/Runtime/Core/Models/RuntimeActivityDescriptor.cs:64-68`). Every CLR activity, the scope included, has `elsa.clr-activity`, so the key would select the nearest CLR ancestor, not the nearest scope.
  - The walk follows `ParentActivityExecutionId` with no boundary stop (`ParentNotificationEvaluation.cs:206-218`). The only user today is the graph `Complete` node, which targets `elsa.graph-activity` (`Activities/Graph/Runtime/Activities/Complete.cs:48-54`).
- **Decision (R-1):** add a selector form: activity type, optionally an authored node id, an optional "stop at reusable boundary" flag (a node with consumer `elsa.graph-activity` ends the walk), and an optional "maybe" flag (no match sends nothing instead of faulting). `ActivityChildNotifiedContext` gains the id of the direct child the notification came through (`ViaChildActivityExecutionId`, `ViaChildExecutableNodeId`), so the scope can tell which of its steps a Confirm or Compensate came from.
- **Alternatives considered:** a dedicated consumer key for compensation scopes. Rejected: consumer keys select the runtime's activity factory; overloading them would change how scopes are constructed.

## R3 — Ancestor notification only from a first evaluation

- **Evidence:**
  - The seam is documented as valid only in an initial structural execution that ends `Complete` (`IRuntimeActivityExecutionContext.cs:131-138`); `ParentNotificationEvaluation.cs:138-139` enforces `Complete`.
  - The child-completion handler refuses it outright: "An ancestor notification is an initial-execution-only power." (`Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs:349-351`).
  - A scope completes `Done` from `OnChildCompletedAsync`, after its last step completes; that is a child-completion evaluation.
- **Decision (R-2):** allow ancestor notifications from a child-completion evaluation whose continuation is `Complete`, enqueued ahead of the completion intent in the same checkpoint (the same ordering the first-evaluation path already uses). Other continuations still refuse them.

## R4 — Which composites deliver a step's fault to the scope

- **Evidence:**
  - A child fault reaches a parent only if the parent implements `IRuntimeActivityChildFaultHandler` (or the combined outcome handler). Otherwise the parent's attempt is suspended and the fault stays a blocking incident (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:207-223`).
  - Fault handlers: `Flowchart` (`Activities/Flowchart/Activities/Flowchart.cs:33,49-55`, faults itself on a branch fault), `Parallel` (`Activities/ControlFlow/Parallel/Activities/Parallel.cs:73,119-130`, join decision), `GraphActivity` (`Activities/Graph/Runtime/Activities/GraphActivity.cs:23`, causal failure at the boundary), `BpmnProcess` (`Activities/Bpmn/Elsa.Activities.Bpmn/Activities/BpmnProcess.cs:32`), `CompensationScope` (`Activities/Compensation/Activities/CompensationScope.cs:56`).
  - Completion-only: `Sequence` (`Activities/Sequence/Activities/Sequence.cs:20`), `ForEach` (`Activities/ControlFlow/ForEach/Activities/ForEach.cs:44`), `While` (`Activities/ControlFlow/While/Activities/While.cs:56`), `For` (`Activities/ControlFlow/For/Activities/For.cs:54`).
  - There is no State machine activity in the tree (no type or file matches `StateMachine` under `Essentials/src`).
- **Decision (R-3, OD11):** an opt-in marker `IRuntimeActivityDescendantFaultHandler`, read at compile time into the executable node. In the bypass branch above, instead of suspending, the runtime walks up to the nearest ancestor that handles descendant faults and has only non-fault-handling composites in between, and delivers the fault there with the via-child ids. Absorbing it cancels the via-child's subtree. Sequence and the loops are unchanged.
- **Matrix (FR-316):** leaf, Flowchart, Parallel, reusable activity: verified, unchanged. Sequence, ForEach, While, For: supported through R-3. State machine: excluded (absent). BPMN: excluded (ADR 0086).
- **Alternatives considered:** Sequence re-faults like Flowchart. Rejected (spec 250 R5): it changes incident counts for every Sequence fault in every workflow.

## R5 — The parent can schedule the inner handler, but must route it from provenance

- **Evidence:**
  - `ScheduleChildActivity` takes any executable node id and records the request (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/ActivityExecutions/SimpleActivityExecutionContext.cs:93-106`); no structural slot check was found at the schedule seam. PR 2 adds a test that schedules a handler node owned by an inner scope's slot from the outer scope, and stops if the runtime refuses it.
  - A child's command metadata is the parent's inherited command metadata plus the request metadata (`Activities/Runtime/Elsa.Activities.Runtime/Services/SchedulerWorkItems.cs:140-149`). Request metadata becomes provenance metadata only when the parent gives no provenance (`SchedulerWorkItems.cs:118-136`).
  - M2 lesson: an operator retry rebuilds the successor's command from the source's provenance metadata (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/Handlers/RetryFaultedActivityAlterationHandler.cs:116,163`). Routing data held only in command metadata is lost on retry; the scope missed the successor's completion until that was fixed.
- **Decision (FR-304, FR-307):** the scope puts all routing keys (role, scope, entry, attempt, origin) in both the request metadata and the provenance metadata of every child it schedules, handed-over handlers included.

## R6 — What a late handler can bind to

- **Evidence:** a completed scope closes its owned container frames (`StructuralParentEvaluationSupport.cs:425`, `RuntimeContainerScopeService.CloseOwnedFrames`), so values of containers inside the origin scope are gone once it completes.
- **Decision:** a handed-over handler binds to the forward step's result and workflow variables. A design validator refuses a handler that reads a container-local variable declared inside its own scope or a container below it, but only where that scope can hand over (it has an enclosing scope and `confirmOnComplete` is off).
- **Open check for PR 2:** an `ActivityResult` reference carries the producer's authored scope (`producerScopeId`). PR 2 must prove that it resolves when the handler runs as the outer scope's child; if not, the hand-over carries the forward result reference and the scope rebinds it.

## R7 — Cancellation

- **Evidence:** cancellation marks the subtree `Cancelled` without calling the structural activity (`ActivitySubtreeCancellationPlanner.cs:32-44`), and a late completion of a cancelled child is acked without a callback (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:138-141`).
- **Decision (OD10):** no runtime change. The scope never sees the cancellation, so it cannot compensate, and its state is kept. Studio derives "Cancelled — nothing undone" from the scope's status and journal.

## R8 — Combined fault and notification outcomes

- **Evidence:** a parent that handles child notifications but not combined outcomes refuses a child's fault that carries notifications (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:112, 212-214`).
- **Decision:** the scope implements `IRuntimeActivityChildNotificationHandler` (to receive hand-overs, Confirm and Compensate through R-1) and also `IRuntimeActivityChildOutcomeHandler`, which delivers the fault first and then the notifications, so a step that faults while a Confirm is pending is not refused.

## R9 — A stopped step without a handler

- **Problem:** Compensate stops the running step. An effect may exist (spec 251: unknown is never "no effect"), but a cancelled step has no incident, so spec 251's intervention request has nothing to keep blocking.
- **Decision:** the step is journaled `Uncertain` with no handler. The scope recovers the other entries, then faults with `compensation.scope.uncertain-unhandled`, whatever `faultAfterCompensation` says. That fault is a blocking incident an operator can act on.
- **Alternatives considered:** wait for the step to finish before compensating. Rejected: a waiting step may never finish, and Compensate exists to stop it.

## R10 — Hand-over size

- **Decision:** at most 16 entries per message, several messages in one checkpoint when needed. Each payload carries the entry's identity, keys and handler node, never forward values, which stay in the run's value store and are resolved when the handler runs.
