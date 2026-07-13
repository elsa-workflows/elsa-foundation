import type { ActivityNode } from "./workflowTypes";

export const flowchartStructureKind = "elsa.flowchart.structure";

/**
 * Keeps the authored Flowchart start node explicit and deterministic.
 *
 * The first authored activity becomes the start when the current value is absent or no longer names
 * an activity in this Flowchart. Empty Flowcharts keep an explicit null. Applying this at designer and
 * wire boundaries makes Code view, backend validation, and dispatch observe the same document.
 */
export function normalizeFlowchartStartNode(activity: ActivityNode): ActivityNode {
  if (activity.structure?.kind !== flowchartStructureKind) return activity;

  const activities = Array.isArray(activity.structure.payload.activities)
    ? activity.structure.payload.activities.filter(isActivityNode)
    : [];
  const currentStartNodeId = activity.structure.payload.startNodeId;
  const nextStartNodeId = typeof currentStartNodeId === "string" && activities.some(child => child.nodeId === currentStartNodeId)
    ? currentStartNodeId
    : activities[0]?.nodeId ?? null;

  if (currentStartNodeId === nextStartNodeId) return activity;

  return {
    ...activity,
    structure: {
      ...activity.structure,
      payload: {
        ...activity.structure.payload,
        startNodeId: nextStartNodeId
      }
    }
  };
}

function isActivityNode(value: unknown): value is ActivityNode {
  return typeof value === "object" && value !== null
    && typeof (value as ActivityNode).nodeId === "string"
    && typeof (value as ActivityNode).activityVersionId === "string";
}
