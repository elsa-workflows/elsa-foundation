import { findBpmnElementForActivity } from "../bpmn/bpmnAdapter";
import { findNodeScopePath, resolveScope, type ActivityCatalogLookup, type ScopeFrame } from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";

export interface WorkflowActivityCanvasTarget {
  frames: ScopeFrame[];
  canvasNodeId: string;
}

export function resolveWorkflowActivityCanvasTarget(
  root: ActivityNode | null | undefined,
  activityNodeId: string,
  labelFor: (activity: ActivityNode) => string,
  catalog?: ActivityCatalogLookup
): WorkflowActivityCanvasTarget | null {
  const frames = findNodeScopePath(root, activityNodeId, labelFor, catalog);
  if (!frames) return null;

  const scope = resolveScope(root, frames, catalog);
  if (!scope) return null;

  const canvasNodeId = scope.slot.mode === "bpmn"
    ? findBpmnElementForActivity(scope.owner, activityNodeId)?.elementId
    : activityNodeId;
  return canvasNodeId ? { frames, canvasNodeId } : null;
}