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

- Exactly one of `ConsumerKey` or `ActivityTypeKey` is set. The existing string overload maps to `ConsumerKey`.
- The walk is the existing committed-ancestry walk (`ParentNotificationEvaluation.FindNearestAncestorAsync`).
- `ActivityChildNotifiedContext` gains `ViaChildActivityExecutionId` and `ViaChildExecutableNodeId`: the receiver's direct child on the path from the sender. For a direct child they equal the notifying child.
- No match and not optional: the sender faults with `runtime.ancestor-notification.target-not-found` (unchanged).

## R-2 — Ancestor notification from a completing child-completion evaluation

- Valid in a child-completion evaluation whose continuation is `Complete`. The notifications are enqueued ahead of the parent's own upward completion, in the same checkpoint, in staging order.
- Still refused in child-fault, notification and resume evaluations, and with any other continuation.

## R-3 — Descendant-fault delivery

```csharp
public interface IRuntimeActivityDescendantFaultHandler
{
    ValueTask<RuntimeStructuralContinuation> OnDescendantFaultedAsync(ActivityDescendantFaultedContext context);
}
```

- Compiled into the executable node as a capability flag, so the runtime decides without constructing ancestors.
- When a child faults and its parent handles neither child faults nor combined outcomes, the runtime walks up. It stops at the first ancestor that handles child faults (nothing changes: today's suspend), at a reusable boundary (today's suspend), or at an ancestor flagged for descendant faults. Only in the last case is the fault delivered there.
- `ActivityDescendantFaultedContext` carries the faulted descendant's ids, incident id, fault code and category, and the via-child ids.
- The receiver may request absorption (cancels the via-child's subtree, not just the faulted leaf) or intervention, under spec 251's rules for those seams.
- The intermediate composites stay suspended until the absorption cancels them.
- **Known limit:** a non-handling composite under a fault-handling one (a Sequence inside a Flowchart step) still suspends, because a Flowchart expects faults from its direct children only. Such a step blocks as today until that composite opts in too. The composition tests pin this.
