import type { ActivityNode } from "./workflowTypes";

export const flowchartStructureKind = "elsa.flowchart.structure";

/** The authored input that marks an activity as able to start a workflow (`CanStartWorkflow`). */
const startTriggerInputKey = "canstartworkflow";

/**
 * Keeps the authored Flowchart start node explicit and deterministic.
 *
 * When the current value is absent or no longer names an activity in this Flowchart, a start is picked:
 * an activity that can start a workflow wins over the merely-first one, because a trigger authored
 * anywhere in the graph is what an operator means by "the start". Empty Flowcharts keep an explicit
 * null. Applying this at designer and wire boundaries makes Code view, backend validation, and dispatch
 * observe the same document.
 *
 * A start that still resolves is never reassigned here — silently moving it would overwrite an explicit
 * choice. When a start trigger is authored later, {@link findMisplacedStartTriggers} reports it so the
 * designer can offer the change instead of making it.
 */
export function normalizeFlowchartStartNode(activity: ActivityNode): ActivityNode {
  if (activity.structure?.kind !== flowchartStructureKind) return activity;

  const activities = readFlowchartActivities(activity);
  const currentStartNodeId = activity.structure.payload.startNodeId;
  const nextStartNodeId = typeof currentStartNodeId === "string" && activities.some(child => child.nodeId === currentStartNodeId)
    ? currentStartNodeId
    : activities.find(canStartWorkflow)?.nodeId ?? activities[0]?.nodeId ?? null;

  if (currentStartNodeId === nextStartNodeId) return activity;
  return withStartNodeId(activity, nextStartNodeId);
}

/**
 * Points the Flowchart's start at `nodeId`. Ignored when the node is not one of this Flowchart's own
 * activities, so a stale selection cannot write a start that names nothing.
 */
export function setFlowchartStartNode(activity: ActivityNode, nodeId: string): ActivityNode {
  if (activity.structure?.kind !== flowchartStructureKind) return activity;
  if (!readFlowchartActivities(activity).some(child => child.nodeId === nodeId)) return activity;
  if (activity.structure.payload.startNodeId === nodeId) return activity;
  return withStartNodeId(activity, nodeId);
}

/** The Flowchart's current start node id, or null when it has none (or is not a Flowchart). */
export function readFlowchartStartNodeId(activity: ActivityNode | null | undefined): string | null {
  if (activity?.structure?.kind !== flowchartStructureKind) return null;
  const startNodeId = activity.structure.payload.startNodeId;
  return typeof startNodeId === "string" && startNodeId ? startNodeId : null;
}

/**
 * Reports whether an activity is authored to start a workflow — its `CanStartWorkflow` input is a
 * literal true.
 *
 * Both representations of an authored input are accepted: the designer's top-level wrapped property
 * (`activity.canStartWorkflow = { expression: { value: true } }`) and the wire's `inputs` array of
 * `{ referenceKey, value }`. The key is matched case-insensitively because a referenceKey is an opaque
 * contract identifier whose casing is the backend's to choose.
 */
export function canStartWorkflow(activity: ActivityNode): boolean {
  for (const [key, value] of Object.entries(activity)) {
    if (key.toLowerCase() === startTriggerInputKey && readBooleanInput(value)) return true;
  }
  const inputs = Array.isArray(activity.inputs) ? activity.inputs : [];
  return inputs.some(input => {
    if (!isRecord(input)) return false;
    const referenceKey = typeof input.referenceKey === "string" ? input.referenceKey : "";
    return referenceKey.toLowerCase() === startTriggerInputKey && readBooleanInput(input.value);
  });
}

/**
 * The activities that say they can start a workflow but cannot: they are not the start node and nothing
 * connects into them, so execution can never arrive. This is the state the designer surfaces as soon as
 * it arises (with a one-click "set as start"), rather than only reporting unreachability at publish.
 */
export function findMisplacedStartTriggers(activity: ActivityNode): ActivityNode[] {
  if (activity.structure?.kind !== flowchartStructureKind) return [];

  const activities = readFlowchartActivities(activity);
  const startNodeId = readFlowchartStartNodeId(activity);
  const targets = new Set(readFlowchartConnectionTargets(activity));
  return activities.filter(child =>
    canStartWorkflow(child) && child.nodeId !== startNodeId && !targets.has(child.nodeId));
}

export function readFlowchartActivities(activity: ActivityNode): ActivityNode[] {
  if (activity.structure?.kind !== flowchartStructureKind) return [];
  const activities = activity.structure.payload.activities;
  return Array.isArray(activities) ? activities.filter(isActivityNode) : [];
}

function readFlowchartConnectionTargets(activity: ActivityNode): string[] {
  if (activity.structure?.kind !== flowchartStructureKind) return [];
  const connections = activity.structure.payload.connections;
  if (!Array.isArray(connections)) return [];
  return connections.flatMap(connection => {
    const target = isRecord(connection) ? connection.target : null;
    const nodeId = isRecord(target) ? target.nodeId : null;
    return typeof nodeId === "string" && nodeId ? [nodeId] : [];
  });
}

function withStartNodeId(activity: ActivityNode, startNodeId: string | null): ActivityNode {
  return {
    ...activity,
    structure: {
      ...activity.structure!,
      payload: {
        ...activity.structure!.payload,
        startNodeId
      }
    }
  };
}

/** Accepts the wrapped input shape (`{ expression: { value } }`) and a bare authored value. */
function readBooleanInput(value: unknown): boolean {
  if (value === true) return true;
  if (!isRecord(value)) return false;
  if (value.expression !== undefined) {
    const expression = value.expression;
    return isRecord(expression) ? readBooleanInput(expression.value) : false;
  }
  return value.value !== undefined ? readBooleanInput(value.value) : false;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isActivityNode(value: unknown): value is ActivityNode {
  return typeof value === "object" && value !== null
    && typeof (value as ActivityNode).nodeId === "string"
    && typeof (value as ActivityNode).activityVersionId === "string";
}
