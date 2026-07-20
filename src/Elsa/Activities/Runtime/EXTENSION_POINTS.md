# Extension points — Activities.Runtime domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Runtime`, which coordinates transient activation, pinned-input hydration, atomic transition handling, and CLR type discovery.

> Carries **no** `Elsa.*.Design.*` dependency (Elsa §E2.2). Runtime invocation consumes only the compiled `ActivityContract` and canonical bindings.

---

## Implementable contributor interfaces

### `WorkflowInvokeActivitySchedulerWorkHandler` *(Activities Runtime — `Elsa.Activities.Runtime`)*
- **Kind:** Scheduler work contributor.
- **Register:** `ActivitiesRuntimeFeature` registers it as an `IWorkflowSchedulerWorkHandler`.
- **Usage:** handles `WorkflowExecutionCommandKind.InvokeActivity` work by materializing or reusing the committed input snapshot, acquiring an `IActivityActivator` lease, executing one closed typed transition, and atomically recording completion or fault state. Structural activities are invoked through `IRuntimeStructuralActivity` and must return one `RuntimeStructuralContinuation` decision. When a faulted activity has a parent, it rides a child-fault parent-evaluation work item (`ChildFaultParentEvaluation`) on the fault incident checkpoint so a fork/join parent can resolve its join deterministically (#308). It does not load Design-owned authored workflow models.

### `WorkflowParentActivityCompletionSchedulerWorkHandler` *(Activities Runtime — `Elsa.Activities.Runtime`)*
- **Kind:** Scheduler work contributor.
- **Register:** `ActivitiesRuntimeFeature` registers it as an `IWorkflowSchedulerWorkHandler`.
- **Usage:** handles `ParentCompletionEvaluation` by reactivating the transient parent from its pinned snapshot and invoking `IRuntimeActivityChildCompletionHandler` for a completed child, or `IRuntimeActivityChildFaultHandler` for a faulted child (work items tagged `runtime.childFaulted`, #308). Each callback returns a `RuntimeStructuralContinuation` for the runtime to apply. For a faulted child whose parent does not implement `IRuntimeActivityChildFaultHandler` the handler no-ops, leaving the fault a blocking incident. It does not interpret workflow-level edges or load Design-owned authored workflow models.

### `ResumeTargetAttribute` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Declaration surface (activity author contract).
- **Signature:** `[ResumeTarget("stable-resume-target-id")]` on an activity handler method.
- **Usage:** declares a stable runtime resume target ID. Workflow compile/publish can copy the ID into a runtime executable artifact's resume-target table. Durable bookmarks store this ID, not the C# method name.
- **Related runtime seam:** `IBookmarkResumeResolver` in `Elsa.Workflows.Runtime.Core`.

### `TriggerActivityAttribute` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Declaration surface (activity author contract).
- **Signature:** `[TriggerActivity]` on an activity class that can start a workflow from an external stimulus.
- **Usage:** CLR reconciliation records the activity version as `Trigger`; publish-time compilation also reads the marker from the CLR construction descriptor so legacy catalog rows authored before the marker was persisted still compile into routable trigger nodes.
- **Related runtime seam:** `IActivityTriggerStimulusProvider` in `Elsa.Workflows.Runtime.Core`; a marked activity must have a provider contributed by its owning feature.

### `IActivityActivator` *(Activities Runtime — `Elsa.Activities.Runtime`)*
- **Kind:** Replacement activation boundary.
- **Signature:** `ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken)` returns an async-disposable `ActivityActivationLease`.
- **Usage:** creates one fresh activity and owned service scope per invocation attempt, then hydrates plain annotated inputs from the committed snapshot. The shipped CLR implementation is `ClrActivityActivator` in `Elsa.Activities.Primitives`.

### `IRuntimeStructuralActivity` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Engine-only structural execution protocol.
- **Signature:** `ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)`.
- **Usage:** implemented by composite activities that schedule and coordinate executable children. The runtime invokes this method instead of the ordinary `IActivity.ExecuteAsync` path, then applies exactly one immutable continuation decision: `Complete(outcome)`, `Defer`, `Faulted(fault)`, or `Cancel(reason)`. A terminal decision cannot also schedule children, and the initial `Defer` decision must schedule at least one child.

### `IRuntimeActivityChildCompletionHandler` / `IRuntimeActivityChildFaultHandler` *(Core — `Elsa.Workflows.Runtime.Core`)*
- **Kind:** Engine-only structural re-evaluation protocols.
- **Signatures:** `OnChildCompletedAsync(ActivityChildCompletedContext context)` and `OnChildFaultedAsync(ActivityChildFaultedContext context)` each return `ValueTask<RuntimeStructuralContinuation>`.
- **Usage:** implemented by structural activities that own child completion or fault routing. The runtime invokes them only for parent-evaluation work after reconstructing the parent from the pinned executable artifact. A callback may return `Defer` while existing children are still running or after scheduling the next child; otherwise it returns one terminal continuation decision. A parent that does not implement the fault callback leaves the child's fault as a blocking incident.
- **Child-subtree cancellation (spec 112):** during a callback the parent may also stage `RequestChildSubtreeCancellation(childActivityExecutionId, reason)` on the context for any of its live direct children. The runtime terminalizes the target's whole execution subtree to `Cancelled`/`ParentCancelled`, deletes its bookmarks/durable timers/queued scheduler work via `IActivityScopeCleanupStore`, and suppresses its non-terminal incidents — all inside the same checkpoint commit as the continuation. Honored with `Defer` and `Complete` only; a terminal target is skipped as a legal first-completion-wins race; unknown, non-child, or duplicate targets fault the evaluation.

**Known implementations (shipped):**
- `Elsa.Activities.Flowchart` — `Flowchart` *(routes completed children through Flowchart-owned structure and child projection)*
- `Elsa.Activities.Sequence` — `Sequence` *(schedules child executable nodes in Sequence-owned slot order)*
- `Elsa.Activities.ControlFlow` — `Parallel` *(fork/join: counts branch completions toward the join threshold)* and the `If`/`Switch`/`For`/`ForEach`/`While`/`Do` control-flow composites
- Fault callback implementations: `Parallel` *(faults once its success threshold is unreachable)* and `Flowchart` *(faults when an inbound branch of an all-inbound join faults)*.

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1; Elsa §E2.2 (no Runtime → Design dependency).
