import type { ActivityNode } from "./workflowTypes";

interface CaseOutcome { identity: string; label: string; outcome: string; stable: boolean }
const fixedOutcomes = ["Default", "Break"];

/** Flowchart endpoints and completion outcomes use this same normalization at runtime. */
export const normalizeSwitchOutcome = (label: string) => label.trim();

function cases(activity: ActivityNode): CaseOutcome[] | null {
  if (activity.structure?.kind !== "elsa.switch.structure"
    || !["1.0.0", "2.0.0"].includes(activity.structure.schemaVersion)) return null;
  const items = activity.structure.payload.cases;
  if (!Array.isArray(items) || !items.every(item => record(item) && typeof item.match === "string")) return null;
  return items.map(item => {
    const value = item as Record<string, unknown>;
    const label = value.match as string;
    const stable = typeof value.caseId === "string" && value.caseId.trim().length > 0;
    return { label, outcome: normalizeSwitchOutcome(label), stable,
      identity: stable ? `case:${value.caseId}` : `label:${label}` };
  });
}

/** Unique emitted outcomes, including the existing shared Default/Break control outcomes. */
export function switchOutcomePorts(activity: ActivityNode): { name: string; displayName: string }[] | null {
  const entries = cases(activity);
  if (!entries) return null;
  const names = [...new Set([...entries.map(item => item.outcome).filter(name => name.length > 0 && !fixedOutcomes.includes(name)), ...fixedOutcomes])];
  return names.map(name => ({ name, displayName: name }));
}

function groups(entries: CaseOutcome[]): Map<string, string[]> {
  const result = new Map(fixedOutcomes.map(name => [name, [`fixed:${name}`]]));
  for (const item of entries.filter(item => item.outcome.length > 0)) result.set(item.outcome, [...(result.get(item.outcome) ?? []), item.identity]);
  return result;
}

/** Follow a case's identity rather than its position or old caption when reconciling wires. */
function outcomeChanges(previous: ActivityNode, next: ActivityNode): Map<string, string[]> | null {
  const before = cases(previous), after = cases(next);
  if (!before || !after) return null;
  // An invalid condition document remains diagnosable without silently deleting its connections.
  if ([previous, next].some((node, i) => node.structure?.schemaVersion === "2.0.0"
    && ([before, after][i].some(item => !item.stable)
      || new Set([before, after][i].map(item => item.identity)).size !== [before, after][i].length))) return null;
  const names = new Map(after.map(item => [item.identity, item.outcome]));
  for (const name of fixedOutcomes) names.set(`fixed:${name}`, name);
  for (const old of before.filter(item => !item.stable)) {
    const sameLabel = after.find(item => item.label === old.label);
    if (sameLabel) names.set(old.identity, sameLabel.outcome);
  }
  // Legacy cases have no IDs. A single ordinary rename is unambiguous; additions/removals do not
  // borrow the next case's position, which would accidentally retarget deleted-case connections.
  const removed = before.filter(item => !item.stable && !after.some(candidate => candidate.label === item.label));
  const added = after.filter(item => !before.some(candidate => candidate.label === item.label));
  if (before.length === after.length && removed.length === 1 && added.length === 1)
    names.set(removed[0].identity, added[0].outcome);
  const changes = new Map<string, string[]>();
  for (const [outcome, identities] of groups(before)) {
    const targets = [...new Set(identities.flatMap(id => names.get(id) ? [names.get(id)!] : []))];
    if (targets.length !== 1 || targets[0] !== outcome) changes.set(outcome, targets);
  }
  return changes.size ? changes : null;
}

/** Called with the enclosing scope's direct children, within the same document/history mutation. */
export function reconcileSwitchOutcomeConnections(scope: ActivityNode, previous: ActivityNode[], next: ActivityNode[]): ActivityNode {
  const kind = scope.structure?.kind;
  if (kind !== "elsa.flowchart.structure" && kind !== "elsa.bpmn.structure") return scope;
  const transitions = new Map<string, Map<string, string[]>>();
  for (const old of previous) {
    const updated = next.find(item => item.nodeId === old.nodeId);
    if (!updated || updated === old) continue;
    const changes = outcomeChanges(old, updated);
    if (changes) transitions.set(old.nodeId, changes);
  }
  if (!transitions.size || !scope.structure) return scope;
  const payload = scope.structure.payload;
  const bpmn = kind === "elsa.bpmn.structure";
  const defaultFlows = new Set(bpmn && Array.isArray(payload.elements)
    ? payload.elements.filter(record).map(element => element.defaultFlowId).filter(id => typeof id === "string") : []);
  if (bpmn && Array.isArray(payload.elements)) for (const element of payload.elements) {
    if (record(element) && typeof element.elementId === "string" && typeof element.childNodeId === "string") {
      const changes = transitions.get(element.childNodeId);
      if (changes) transitions.set(element.elementId, changes);
    }
  }
  const property = bpmn ? "sequenceFlows" : "connections";
  const connections = payload[property];
  if (!Array.isArray(connections)) return scope;
  let changed = false;
  const metadata = record(payload.connectionMetadata) ? { ...payload.connectionMetadata } : null;
  const rewired = connections.flatMap(connection => {
    if (!record(connection)) return [connection];
    const source = bpmn ? null : connection.source;
    const sourceId = bpmn ? connection.sourceRef : record(source) ? source.nodeId : null;
    const port = bpmn ? connection.conditionOutcome : record(source) ? source.port : null;
    if (typeof sourceId !== "string" || typeof port !== "string") return [connection];
    if (bpmn && (connection.isDefault === true || defaultFlows.has(connection.flowId as string))) return [connection];
    const targets = transitions.get(sourceId)?.get(normalizeSwitchOutcome(port));
    if (!targets) return [connection];
    changed = true;
    // Prefer keeping the original ID on an outcome that still exists. Shared runtime names may
    // split into several named outcomes after a rename; each retains the original destination.
    const ordered = [...targets].sort((a, b) => Number(b === normalizeSwitchOutcome(port)) - Number(a === normalizeSwitchOutcome(port)));
    if (!ordered.length && metadata && typeof connection.id === "string") delete metadata[connection.id];
    return ordered.map((name, index) => {
      const id = index === 0 ? connection.id : `flow-${crypto.randomUUID()}`;
      if (index > 0 && metadata && typeof connection.id === "string" && typeof id === "string"
        && connection.id in metadata) metadata[id] = metadata[connection.id];
      return bpmn ? { ...connection, ...(index ? { flowId: `flow-${crypto.randomUUID()}` } : {}), conditionOutcome: name }
        : { ...connection, ...(id === undefined ? {} : { id }), source: { ...(source as Record<string, unknown>), port: name } };
    });
  });
  if (!changed) return scope;
  return { ...scope, structure: { ...scope.structure, payload: { ...payload, [property]: rewired,
    ...(metadata ? { connectionMetadata: metadata } : {}) } } };
}

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
