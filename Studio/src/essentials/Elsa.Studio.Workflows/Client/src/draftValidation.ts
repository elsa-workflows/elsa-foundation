import type { DraftValidationError, ValidationError, WorkflowDefinitionState, ActivityNode } from "./workflowTypes";
import { canStartWorkflow, findMisplacedStartTriggers, flowchartStructureKind } from "./flowchartStartNode";
import {
  STRUCTURAL_DANGLING_TYPE,
  STRUCTURAL_NO_START_TYPE,
  STRUCTURAL_START_TRIGGER_TYPE
} from "./validationDiagnostics";
import {
  createValidationActivityLabels,
  type ValidationActivityLabels
} from "./validationActivityLabels";

/**
 * Unifies the three validation surfaces the QA sweep found disagreeing (issue #453): the draft's own
 * server-reconciled `validationErrors`, the on-demand server-derived validation set (foundation #937),
 * and designer-side structural checks. All three project onto the existing `ValidationError` shape so
 * the editor's validation panel can render, de-duplicate, and navigate them uniformly.
 */

export {
  STRUCTURAL_DANGLING_TYPE,
  STRUCTURAL_NO_START_TYPE,
  STRUCTURAL_START_TRIGGER_TYPE
} from "./validationDiagnostics";

/**
 * Projects a server DraftValidationError (node pointer + member + code) onto the flat ValidationError
 * shape, rebuilding the slash-delimited path the panel's path parser expects.
 */
export function fromDraftValidationError(error: DraftValidationError): ValidationError {
  const nodeId = error.nodeId?.trim() || null;
  const member = error.member?.trim() || null;
  const path = nodeId ? (member ? `${nodeId}/${member}` : nodeId) : (member ? `$workflow/${member}` : "$workflow");
  return { type: error.code, code: error.code, message: error.message, path };
}

/**
 * Designer-side structural validation for flowcharts. Flags activities with no incoming or outgoing
 * connection (dangling), and, when a start node is set, activities unreachable from it or from any start
 * trigger that nothing connects into. Sequences
 * auto-append, so they carry no structural risk here. Returns an empty list for non-flowchart roots.
 *
 * This closes the gap where palette-clicked flowchart nodes land unconnected and no surface flagged it.
 */
export function detectStructuralValidationErrors(
  state: WorkflowDefinitionState | null | undefined,
  labels?: ValidationActivityLabels
): ValidationError[] {
  const errors: ValidationError[] = [];
  const resolved = labels ?? createValidationActivityLabels({ root: state?.rootActivity ?? null });
  visitFlowcharts(state?.rootActivity ?? null, owner => errors.push(...flowchartStructuralErrors(owner, resolved)));
  return errors;
}

function flowchartStructuralErrors(owner: ActivityNode, labels: ValidationActivityLabels): ValidationError[] {
  const payload = owner.structure?.payload ?? {};
  const activities = Array.isArray(payload.activities) ? payload.activities.filter(isActivityNode) : [];
  if (activities.length <= 1) return [];

  const connections = Array.isArray(payload.connections) ? payload.connections : [];
  const connected = new Set<string>();
  const outgoing = new Map<string, Set<string>>();
  for (const connection of connections) {
    const source = readEndpoint(connection, "source");
    const target = readEndpoint(connection, "target");
    if (!source || !target) continue;
    connected.add(source);
    connected.add(target);
    (outgoing.get(source) ?? outgoing.set(source, new Set()).get(source)!).add(target);
  }

  const errors: ValidationError[] = [];
  const name = (activity: ActivityNode) => labels.labelFor(activity.nodeId);
  for (const trigger of findMisplacedStartTriggers(owner)) {
    errors.push({
      type: STRUCTURAL_START_TRIGGER_TYPE,
      message: `${name(trigger)} can start a workflow, but something connects into it and it is not the start node, so publishing refuses it. Make it the start node, remove its incoming connection, or turn off "Can start workflow" to make it a mid-flow wait.`,
      path: trigger.nodeId
    });
  }
  for (const activity of activities) {
    if (!connected.has(activity.nodeId)) {
      errors.push({
        type: STRUCTURAL_DANGLING_TYPE,
        message: `${name(activity)} is not connected to any other activity.`,
        path: activity.nodeId
      });
    }
  }

  // Reachability from every entry: nodes that have connections but can't be reached. A run begins at the
  // start node, or at a start trigger that nothing connects into (spec 238), so each of those is a root.
  const startNodeId = typeof payload.startNodeId === "string" ? payload.startNodeId : null;
  if (startNodeId && activities.some(activity => activity.nodeId === startNodeId)) {
    const targets = new Set([...outgoing.values()].flatMap(next => [...next]));
    const triggerEntries = activities
      .filter(activity => canStartWorkflow(activity) && !targets.has(activity.nodeId))
      .map(activity => activity.nodeId);
    const reachable = new Set<string>();
    const stack = [startNodeId, ...triggerEntries];
    while (stack.length) {
      const current = stack.pop()!;
      if (reachable.has(current)) continue;
      reachable.add(current);
      for (const next of outgoing.get(current) ?? []) stack.push(next);
    }
    for (const activity of activities) {
      if (connected.has(activity.nodeId) && !reachable.has(activity.nodeId)) {
        errors.push({
          type: STRUCTURAL_NO_START_TYPE,
          message: `${name(activity)} cannot be reached from the start node or any start trigger.`,
          path: activity.nodeId
        });
      }
    }
  }
  return errors;
}

/**
 * Merges validation sources into one de-duplicated list, preserving order: server draft validations
 * first (authoritative), then the draft's reconciled errors, then designer structural checks. Entries
 * with the same type+path+message collapse to one so the same fault reported by two surfaces shows once.
 */
export function combineValidationErrors(sources: {
  draftErrors?: ValidationError[] | null;
  serverErrors?: DraftValidationError[] | null;
  structuralErrors?: ValidationError[] | null;
}): ValidationError[] {
  const combined: ValidationError[] = [
    ...(sources.serverErrors ?? []).map(fromDraftValidationError),
    ...(sources.draftErrors ?? []),
    ...(sources.structuralErrors ?? [])
  ];
  const seen = new Set<string>();
  const result: ValidationError[] = [];
  for (const error of combined) {
    const key = `${error.type ?? error.code ?? ""}${error.path ?? ""}${error.message ?? ""}`;
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(error);
  }
  return result;
}

function visitFlowcharts(activity: ActivityNode | null, visit: (owner: ActivityNode) => void) {
  const seen = new Set<object>();
  const walk = (node: ActivityNode | null) => {
    if (!node || seen.has(node)) return;
    seen.add(node);
    if (node.structure?.kind === flowchartStructureKind) visit(node);
    const payload = node.structure?.payload;
    if (payload) {
      for (const value of Object.values(payload)) {
        if (Array.isArray(value)) value.filter(isActivityNode).forEach(walk);
        else if (isActivityNode(value)) walk(value);
      }
    }
  };
  walk(activity);
}

function readEndpoint(connection: unknown, side: "source" | "target"): string | null {
  if (!connection || typeof connection !== "object") return null;
  const endpoint = (connection as Record<string, unknown>)[side];
  const nodeId = endpoint && typeof endpoint === "object" ? (endpoint as { nodeId?: unknown }).nodeId : undefined;
  return typeof nodeId === "string" && nodeId ? nodeId : null;
}

function isActivityNode(value: unknown): value is ActivityNode {
  return typeof value === "object" && value !== null
    && typeof (value as ActivityNode).nodeId === "string"
    && typeof (value as ActivityNode).activityVersionId === "string";
}
