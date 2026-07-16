# Extension points — Activities.Runtime domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Runtime`, which coordinates transient activation, pinned-input hydration, atomic transition handling, and CLR type discovery.

> Carries **no** `Elsa.*.Design.*` dependency (Elsa §E2.2). Runtime invocation consumes only the compiled `ActivityContract` and canonical bindings.

---

## Implementable contributor interfaces

### `WorkflowInvokeActivitySchedulerWorkHandler` *(Activities Runtime — `Elsa.Activities.Runtime`)*
- **Kind:** Scheduler work contributor.
- **Register:** `ActivitiesRuntimeFeature` registers it as an `IWorkflowSchedulerWorkHandler`.
- **Usage:** handles `WorkflowExecutionCommandKind.InvokeActivity` work by materializing or reusing the committed input snapshot, acquiring an `IActivityActivator` lease, executing one closed typed transition, and atomically recording completion or fault state. When a faulted activity has a parent, it rides a child-fault parent-evaluation work item (`ChildFaultParentEvaluation`) on the fault incident checkpoint so a fork/join parent can resolve its join deterministically (#308). It does not load Design-owned authored workflow models.

### `WorkflowParentActivityCompletionSchedulerWorkHandler` *(Activities Runtime — `Elsa.Activities.Runtime`)*
- **Kind:** Scheduler work contributor.
- **Register:** `ActivitiesRuntimeFeature` registers it as an `IWorkflowSchedulerWorkHandler`.
- **Usage:** handles `ParentCompletionEvaluation` by reactivating the transient parent from its pinned snapshot and invoking `IActivityChildCompletionHandler` for a completed child, or `IActivityChildFaultHandler` for a faulted child (work items tagged `runtime.childFaulted`, #308). For a faulted child whose parent does not implement `IActivityChildFaultHandler` the handler no-ops, leaving the fault a blocking incident. It does not interpret workflow-level edges or load Design-owned authored workflow models.

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

### `IActivityChildCompletionHandler` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Activity-owned continuation handler.
- **Signature:** `ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)`.
- **Usage:** implemented by composite activities that own child-completion routing semantics. The runtime invokes it only for parent-completion evaluation work after reconstructing the parent activity from the pinned executable artifact.

**Known implementations (shipped):**
- `Elsa.Activities.Flowchart` — `Flowchart` *(routes completed children through Flowchart-owned structure and child projection)*
- `Elsa.Activities.Sequence` — `Sequence` *(schedules child executable nodes in Sequence-owned slot order)*
- `Elsa.Activities.ControlFlow` — `Parallel` *(fork/join: counts branch completions toward the join threshold)* and the `If`/`Switch`/`For`/`ForEach`/`While`/`Do` control-flow composites

### `IActivityChildFaultHandler` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Activity-owned continuation handler (fault side of `IActivityChildCompletionHandler`).
- **Signature:** `ValueTask OnChildFaultedAsync(ActivityChildFaultedContext context)`.
- **Usage:** implemented by composite activities that must react to a child branch reaching a terminal `Faulted` state. The runtime invokes it for parent-completion evaluation work tagged `runtime.childFaulted` (raised by `ChildFaultParentEvaluation` on the branch fault incident). A composite that does not implement it is unaffected: a faulted child stays a blocking incident and is not propagated to the parent.

**Known implementations (shipped):**
- `Elsa.Activities.ControlFlow` — `Parallel` *(fault-aware fork/join: faults the composite once the join's success threshold is unreachable, #308)*
- `Elsa.Activities.Flowchart` — `Flowchart` *(fault-aware fork/join: faults the flowchart when an inbound branch of an all-inbound join faults, #308)*

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1; Elsa §E2.2 (no Runtime → Design dependency).
