import { isCompensationScope, readCompensationSteps } from "./compensationScope";
import { getChildSlots } from "./workflowAdapter";
import type { ActivityNode } from "./workflowTypes";

// Spec 258 FR-320: Confirm and Compensate reach the nearest enclosing compensation scope, or an enclosing scope they
// name. The picker offers exactly the scopes the Draft validator (T314) accepts, with its messages for the rest.

export const confirmTargetUiHint = "compensation-confirm-target";
export const compensateTargetUiHint = "compensation-compensate-target";

export interface EnclosingCompensationScope {
  nodeId: string;
  label: string;
  /** The activity sits in this scope's compensation handler, where the scope ignores it during recovery. */
  inHandler: boolean;
}

/** The compensation scopes that enclose `nodeId`, nearest first; null when the node is not in the graph. */
export function enclosingCompensationScopes(root: ActivityNode | null | undefined, nodeId: string): EnclosingCompensationScope[] | null {
  const walk = (node: ActivityNode, scopes: EnclosingCompensationScope[]): EnclosingCompensationScope[] | null => {
    if (node.nodeId === nodeId) return [...scopes].reverse();
    if (isCompensationScope(node)) {
      for (const step of readCompensationSteps(node)) {
        for (const [child, inHandler] of [[step.activity, false], [step.compensation, true]] as const) {
          if (!child) continue;
          const found = walk(child, [...scopes, { nodeId: node.nodeId, label: scopeLabel(node), inHandler }]);
          if (found) return found;
        }
      }
      return null;
    }
    for (const slot of getChildSlots(node)) {
      for (const child of slot.activities) {
        const found = walk(child, scopes);
        if (found) return found;
      }
    }
    return null;
  };
  return root ? walk(root, []) : null;
}

/** The Draft validator's message for a target choice, or null when the choice is valid (spec 258 T314). */
export function compensationTargetProblem(
  activityName: "Confirm" | "Compensate",
  nodeId: string,
  scopes: EnclosingCompensationScope[],
  target: string | null | undefined
): string | null {
  const nearest = scopes[0];
  if (!nearest) return `${activityName} '${nodeId}' needs an enclosing compensation scope.`;
  if (nearest.inHandler) return insideHandler(activityName, nodeId, nearest.nodeId);
  const named = target?.trim();
  if (!named) return null;
  const scope = scopes.find(candidate => candidate.nodeId === named);
  if (!scope) return `${activityName} '${nodeId}' names scope '${named}', which does not enclose it.`;
  return scope.inHandler && scope !== nearest ? insideHandler(activityName, nodeId, named) : null;
}

function insideHandler(activityName: string, nodeId: string, scopeNodeId: string) {
  return `${activityName} '${nodeId}' sits in a compensation handler of scope '${scopeNodeId}', which ignores it during recovery.`;
}

function scopeLabel(node: ActivityNode): string {
  for (const key of ["displayName", "name", "description"]) {
    const value = node[key];
    if (typeof value === "string" && value.trim()) return value.trim();
  }
  return node.nodeId;
}
