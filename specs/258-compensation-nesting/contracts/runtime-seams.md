# Contract: M3 runtime seams (generic)

All three seams are additive and opt-in; no existing activity changes behaviour.

## R-1 — Ancestor notification by activity type

```csharp
void RequestAncestorNotification(RuntimeAncestorSelector selector, string code, JsonElement? payload = null);

public sealed record RuntimeAncestorSelector(
    string? ConsumerKey = null,          // existing behaviour when only this is set
    string? ActivityTypeKey = null,      // match the ancestor node's activity type
    string? AuthoredActivityId = null,   // optional: one authored node of that type
    bool StopAtGraphBoundary = false,    // an ancestor with consumer elsa.graph-activity ends the walk
    bool Optional = false);              // no match: send nothing instead of faulting
```

- Exactly one of `ConsumerKey` or `ActivityTypeKey` is set, and `AuthoredActivityId` only with `ActivityTypeKey`. The existing string overload maps to `ConsumerKey`. `ActivityTypeKey` matches the executable node's activity type, which for a CLR activity is its type key.
- The walk is the existing committed-ancestry walk (`ParentNotificationEvaluation.FindNearestAncestorAsync`). An ancestor is checked for a match before the boundary stop, so a selector by consumer `elsa.graph-activity` still reaches the boundary itself.
- `ActivityChildNotifiedContext` gains `ViaChildActivityExecutionId` and `ViaChildExecutableNodeId`: the receiver's direct child on the path from the sender. For a direct child they equal the notifying child.
- No match and not optional: the sender faults with `runtime.ancestor-notification.target-not-found` (unchanged).

## R-2 — Ancestor notification from a completing child-completion evaluation

- Valid in a child-completion evaluation whose continuation is `Complete`. The notifications are enqueued ahead of the parent's own upward completion, in the same checkpoint, in staging order.
- Still refused in child-fault, notification and resume evaluations, and with any other continuation.
- No match and not optional: the completing parent faults with `runtime.ancestor-notification.target-not-found` instead of completing.

## R-3 — Descendant-fault delivery

```csharp
public interface IRuntimeActivityDescendantFaultHandler
{
    ValueTask<RuntimeStructuralContinuation> OnDescendantFaultedAsync(ActivityDescendantFaultedContext context);
}
```

- Compiled into the executable node as a capability flag, so the runtime decides without constructing ancestors: `runtime.structuralFaultHandling` is `descendant` for a CLR type implementing the interface, `child` for one that handles child faults or combined outcomes, and absent otherwise. Executables published before M3 carry no flags, so the walk finds no receiver and nothing changes for them.
- When a child faults and its parent handles neither child faults nor combined outcomes, the runtime walks up, starting at that parent. It stops at the first ancestor that handles child faults (nothing changes: today's suspend), at a reusable boundary (today's suspend), or at an ancestor flagged for descendant faults. Only in the last case is the fault delivered there, by a work item enqueued in the same checkpoint as the declining parent's suspension. A combined fault-and-notification outcome is not relayed.
- `ActivityDescendantFaultedContext` carries the faulted descendant's ids, incident id, fault code and category, and the via-child ids. The delivery's command metadata is the faulted chain's, overlaid with the via child's scheduling provenance metadata, so a receiver can route it the way it routes its own children (M2 lesson).
- A receiver still gets its direct children's faults through `IRuntimeActivityChildFaultHandler`; the descendant callback only ever sees faults that passed through a composite.
- The delivery records its own processed marker (`runtime.descendantFaultProcessedWorkItemId`) on the faulted activity, because the declining parent already set the ordinary one; a redelivered item is acknowledged.
- The receiver may request absorption (cancels the via-child's subtree, not just the faulted leaf) or intervention, under spec 251's rules for those seams.
- The intermediate composites stay suspended until the absorption cancels them.
- **Known limit:** a non-handling composite under a fault-handling one (a Sequence inside a Flowchart step) still suspends, because a Flowchart expects faults from its direct children only. Such a step blocks as today until that composite opts in too. The composition tests pin this.

## R-4 — A structural child reports with the metadata its parent gave it

Found while building Phase B, not planned in PR 1. A structural activity that completes or faults from a child (or notification) evaluation reported upward with the command metadata of the child that triggered the evaluation. A nested compensation scope stamps the same `compensation.*` keys on its children as the scope around it, so the outer scope read the inner scope's keys, did not recognise its own child, and the run stalled.

- `SchedulerWorkItems.NewCompletionWorkItem` and `ChildFaultParentEvaluation.TryBuildAsync` overlay the reporting activity's `Provenance.Metadata` on the command metadata, as the R-3 delivery already does with the via child's.
- Keys the reporting activity's own children added and its parent never set pass through unchanged, so composites that read pass-through keys see what they saw before.
- Pinned by `StructuralChildReportMetadataExecutionTests` (completion and fault); the Activities.Runtime and Workflows.Runtime suites pass unchanged.

