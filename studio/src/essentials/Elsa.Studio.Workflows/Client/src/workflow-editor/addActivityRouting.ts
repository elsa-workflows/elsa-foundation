import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import {
  getChildSlots,
  resolveScope,
  type ActivityCatalogLookup,
  type ChildSlot
} from "../workflowAdapter";
import type { ScopeFrame } from "../workflowAdapter";

// A pure decision for "what should dropping `next` do, given where we are (root + frames)?".
// Keeping it separate from useWorkflowCanvas makes the scope===null branching — the source of the
// "wrapped the workflow root while I was inside a slot" corruption — unit-testable without React.
export type ActivityDropPlan =
  // No root yet: the drop becomes the workflow root (unchanged first-drop behaviour).
  | { kind: "becomeRoot" }
  // Root is a bare leaf (no child slots) and we're at the top level: wrap it in the dropped container.
  | { kind: "wrapRoot" }
  // Root is a bare leaf, we're at the top level, and the dropped activity is itself a leaf: nothing to do.
  | { kind: "leafError" }
  // Frames point at a slot that no longer resolves (stale/broken draft edit): reset to root, skip the drop.
  | { kind: "staleFrames" }
  // The current scope resolved: append (many) or set (single) the dropped activity into that slot.
  // `replacedActivity` is set only when a single-cardinality slot already held a *different* node, so the
  // caller can surface a "replaced X" toast (undo history covers recovery).
  | { kind: "addToSlot"; slot: ChildSlot; replacedActivity: ActivityNode | null };

// Decide how a drop of `next` (constructed from `activity`) should mutate the document, given the live
// root and scope frames. Never wraps / mutates the root while `frames` is non-empty — that guard is the
// whole point: a non-empty frame path means the user is inside a slot, so a failed resolve is a stale
// frame (reset), never a licence to rewrite the root.
export function planActivityDrop(
  root: ActivityNode | null | undefined,
  frames: ScopeFrame[],
  next: ActivityNode,
  activity: ActivityCatalogItem,
  catalog: ActivityCatalogLookup
): ActivityDropPlan {
  if (!root) return { kind: "becomeRoot" };

  const scope = resolveScope(root, frames, catalog);

  if (!scope) {
    // Only the true top level may fall back to wrap-or-error. A non-empty frame path that fails to
    // resolve is a stale frame, not a leaf root — never touch state.rootActivity in that case.
    if (frames.length > 0) return { kind: "staleFrames" };
    const childSlot = getChildSlots(next, activity)[0];
    return childSlot ? { kind: "wrapRoot" } : { kind: "leafError" };
  }

  const replacedActivity = scope.slot.cardinality === "single"
    ? scope.slot.activities.find(existing => existing.nodeId !== next.nodeId) ?? null
    : null;

  return { kind: "addToSlot", slot: scope.slot, replacedActivity };
}
