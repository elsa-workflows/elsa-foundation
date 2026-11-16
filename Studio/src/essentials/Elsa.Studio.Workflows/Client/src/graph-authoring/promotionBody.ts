import type { ActivityCatalogItem, ActivityNode, DesignMetadataRecord } from "../workflowTypes";
import { getChildSlots, sequenceStructureKind, flowchartStructureKind } from "../workflowAdapter";
import { bpmnStructureKind, type BpmnElement, type BpmnSequenceFlow } from "../bpmn/bpmnTypes";
import { defaultBpmnPosition } from "../bpmn/bpmnAdapter";
import { readOrdinaryBpmnBody } from "./bpmnBlockRegion";
import { promotionRecord as record, refusePromotion as refuse } from "./blockPromotionTypes";

type Fields = Record<string, unknown>;

export interface PromotionNodeLocation { node: ActivityNode; parent: ActivityNode | null }

/** Traversal is through the owning structure adapters, never an invented generic children collection. */
export function promotionNodes(root: ActivityNode, catalog: ActivityCatalogItem[]) {
  const result = new Map<string, PromotionNodeLocation>();
  const visit = (node: ActivityNode, parent: ActivityNode | null) => {
    if (!node.nodeId || result.has(node.nodeId) || !Array.isArray(node.inputs) || !Array.isArray(node.outputs))
      refuse("malformed-graph", `nodes.${node.nodeId}`, "Repair duplicate identities or malformed arguments before promotion.");
    result.set(node.nodeId, { node, parent });
    for (const slot of getChildSlots(node, catalog)) for (const child of slot.activities) visit(child, node);
  };
  visit(root, null);
  return result;
}

export function promotionBody(root: ActivityNode, catalog: ActivityCatalogItem[]) {
  if (![sequenceStructureKind, flowchartStructureKind, bpmnStructureKind].includes(root.structure?.kind ?? ""))
    refuse("unsupported-block", `nodes.${root.nodeId}`, "Select a native Sequence, Flowchart or ordinary BPMN block.");
  if (root.structure!.kind === bpmnStructureKind) readOrdinaryBpmnBody(root, catalog, refuse);
  const nodes = promotionNodes(root, catalog);
  for (const { node } of nodes.values()) {
    if (node.structure?.kind === bpmnStructureKind) readOrdinaryBpmnBody(node, catalog, refuse);
    if (node.structure?.kind === flowchartStructureKind && !plainFlowchartPolicies(node))
      refuse("unsupported-routing-policy", `nodes.${node.nodeId}`, "This Flowchart policy needs a closed-boundary preservation profile before promotion.");
  }
  return nodes;
}

/** Insert ordinary generated steps without changing surviving authored edge data or identities. */
export function insertPromotionSteps(owner: ActivityNode, afterId: string | null, steps: ActivityNode[],
  allocate: (label: string) => string, layout: DesignMetadataRecord[]) {
  if (!steps.length) return;
  const body = owner.structure?.payload;
  if (!body || !Array.isArray(body.activities)) refuse("unsupported-result-location", `nodes.${owner.nodeId}`, "This result needs an activity-owned insertion profile before promotion.");
  const children = body.activities as ActivityNode[];
  const renderIds = new Map(steps.map(step => [step.nodeId, step.nodeId]));
  let origin = layout.find(item => item.nodeId === afterId) ?? { x: 80, y: 80 };
  const index = afterId === null ? children.length - 1 : children.findIndex(child => child.nodeId === afterId);
  if (afterId !== null && index < 0) refuse("stale-body", `nodes.${owner.nodeId}`, "The selected result producer is no longer in its reviewed body.");
  if (owner.structure!.kind === sequenceStructureKind) {
    body.activities = [...children.slice(0, index + 1), ...steps, ...children.slice(index + 1)];
  } else if (owner.structure!.kind === flowchartStructureKind) {
    if (!Array.isArray(body.connections)) refuse("malformed-flowchart", `nodes.${owner.nodeId}.connections`, "Repair the Flowchart routing before promotion.");
    const edges = body.connections as Record<string, unknown>[];
    let producer = afterId;
    if (producer === null) {
      const sinks = children.filter(child => !edges.some(edge => record(edge.source) && edge.source.nodeId === child.nodeId));
      if (children.length && sinks.length !== 1) refuse("unproven-completion", `nodes.${owner.nodeId}`, "Output capture requires one normal Flowchart completion.");
      producer = sinks[0]?.nodeId ?? null;
    }
    const outgoing = edges.filter(edge => record(edge.source) && edge.source.nodeId === producer);
    if (outgoing.length > 1 || outgoing.some(edge => !record(edge.source) || (edge.source.port ?? "Done") !== "Done"))
      refuse("unproven-result-timing", `nodes.${producer}`, "This result capture crosses conditional or multiple routing.");
    for (const edge of outgoing) edge.source = { ...(edge.source as object), nodeId: steps.at(-1)!.nodeId };
    for (const step of steps) {
      if (producer) edges.push({ id: allocate("flow"), source: { nodeId: producer, port: "Done" }, target: { nodeId: step.nodeId } });
      else body.startNodeId = step.nodeId;
      producer = step.nodeId;
    }
    body.activities = [...children, ...steps];
  } else if (owner.structure!.kind === bpmnStructureKind) {
    if (!Array.isArray(body.elements) || !Array.isArray(body.sequenceFlows)) refuse("malformed-bpmn", `nodes.${owner.nodeId}`, "Repair this process before promotion.");
    const elements = body.elements as BpmnElement[], flows = body.sequenceFlows as BpmnSequenceFlow[];
    let elementId: string;
    if (afterId === null) {
      const ends = elements.filter(element => element.elementType === "endEvent");
      const incoming = ends.length === 1 ? flows.filter(flow => flow.targetRef === ends[0].elementId) : [];
      if (incoming.length !== 1) refuse("unproven-completion", `nodes.${owner.nodeId}`, "Output capture requires one ordinary process completion.");
      elementId = incoming[0].sourceRef;
    } else {
      const matches = elements.filter(element => element.childNodeId === afterId);
      if (matches.length !== 1) refuse("unproven-result-location", `nodes.${afterId}`, "This producer must have one native process element.");
      elementId = matches[0].elementId;
    }
    const outgoing = flows.filter(flow => flow.sourceRef === elementId);
    if (outgoing.length !== 1 || outgoing[0].conditionOutcome || outgoing[0].isDefault)
      refuse("unproven-result-timing", `elements.${elementId}`, "This result capture requires one unconditional continuation.");
    const original = elementId;
    if (body.diagram != null && !record(body.diagram)) refuse("malformed-diagram", `nodes.${owner.nodeId}.diagram`, "Repair this diagram before adding output captures.");
    const stored = record(body.diagram) ? body.diagram : {};
    if (stored.shapes != null && !record(stored.shapes) || stored.edges != null && !record(stored.edges))
      refuse("malformed-diagram", `nodes.${owner.nodeId}.diagram`, "Repair diagram shape and edge maps before promotion.");
    const shapes = record(stored.shapes) ? { ...stored.shapes } : {};
    const diagramEdges = record(stored.edges) ? { ...stored.edges } : {};
    const position = (id: string) => {
      const saved = layout.find(item => item.nodeId === id) ?? (record(shapes[id]) ? shapes[id] : defaultBpmnPosition(elements.findIndex(item => item.elementId === id)));
      const event = elements.find(item => item.elementId === id)?.elementType.endsWith("Event");
      return { x: Number(saved.x), y: Number(saved.y), width: Number("width" in saved ? saved.width : event ? 36 : 100), height: Number("height" in saved ? saved.height : event ? 36 : 80) };
    };
    origin = position(original);
    const laneId = elements.find(element => element.elementId === original)?.laneId;
    for (const step of steps) {
      const next = allocate("element");
      elements.push({ elementId: next, elementType: "task", childNodeId: step.nodeId, ...(laneId ? { laneId } : {}) });
      const at = { x: origin.x + 160 * (steps.indexOf(step) + 1), y: origin.y + 120, width: 100, height: 80 };
      if (![at.x, at.y].every(Number.isFinite)) refuse("malformed-diagram", `elements.${original}`, "Repair this element's position before promotion.");
      shapes[next] = at; renderIds.set(step.nodeId, next);
      const from = position(elementId), flowId = allocate("flow");
      flows.push({ flowId, sourceRef: elementId, targetRef: next });
      diagramEdges[flowId] = { waypoints: [{ x: from.x + from.width, y: from.y + from.height / 2 }, { x: at.x, y: at.y + at.height / 2 }] };
      elementId = next;
    }
    outgoing[0].sourceRef = elementId;
    const last = position(elementId), target = position(outgoing[0].targetRef);
    const previous = record(diagramEdges[outgoing[0].flowId]) ? diagramEdges[outgoing[0].flowId] as Fields : {};
    const waypoints = Array.isArray(previous.waypoints) && previous.waypoints.length >= 2 ? [...previous.waypoints] : [{}, { x: target.x, y: target.y + target.height / 2 }];
    waypoints[0] = { x: last.x + last.width, y: last.y + last.height / 2 };
    diagramEdges[outgoing[0].flowId] = { ...previous, waypoints };
    body.diagram = { ...stored, shapes, edges: diagramEdges };
    body.activities = [...children, ...steps];
  } else refuse("unsupported-result-location", `nodes.${owner.nodeId}`, "This structure needs its own result-capture insertion profile.");
  steps.forEach((step, position) => layout.push({ nodeId: renderIds.get(step.nodeId)!, x: origin.x + 160 * (position + 1), y: origin.y + (owner.structure!.kind === bpmnStructureKind ? 120 : 100) }));
}

/** A finite single-path proof for moving a stable result read to the call's input snapshot. */
export function orderedPromotionChildren(owner: ActivityNode, catalog: ActivityCatalogItem[]): string[] | null {
  const body = owner.structure?.payload;
  if (!body || !Array.isArray(body.activities)) return null;
  const children = body.activities as ActivityNode[];
  if (owner.structure!.kind === sequenceStructureKind) return children.map(node => node.nodeId);
  if (owner.structure!.kind === bpmnStructureKind) {
    const process = readOrdinaryBpmnBody(owner, catalog, refuse);
    return process.region?.activities.map(node => node.nodeId) ?? [];
  }
  if (owner.structure!.kind !== flowchartStructureKind || !Array.isArray(body.connections) || !plainFlowchartPolicies(owner)) return null;
  const edges = body.connections;
  if (edges.length !== Math.max(0, children.length - 1) || edges.some(edge => !record(edge) || !record(edge.source) || !record(edge.target)
    || typeof edge.source.nodeId !== "string" || typeof edge.target.nodeId !== "string" || (edge.source.port ?? "Done") !== "Done"
    || edge.policy != null || edge.isDefault === true)) return null;
  const incoming = new Set(edges.map(edge => (edge.target as Fields).nodeId));
  const entries = children.filter(node => !incoming.has(node.nodeId));
  if (entries.length !== 1 || body.startNodeId && body.startNodeId !== entries[0].nodeId) return null;
  const order: string[] = []; let current: string | undefined = entries[0].nodeId;
  while (current && !order.includes(current)) {
    order.push(current);
    const outgoing = edges.filter(edge => (edge.source as Fields).nodeId === current);
    if (outgoing.length > 1) return null;
    current = outgoing.length ? (outgoing[0].target as Fields).nodeId as string : undefined;
  }
  return current === undefined && order.length === children.length ? order : null;
}

function plainFlowchartPolicies(node: ActivityNode) {
  const empty = (value: unknown) => value == null || Array.isArray(value) && value.length === 0 || record(value) && Object.keys(value).length === 0;
  for (const field of ["nodeMetadata", "connectionMetadata"]) {
    const metadata = node.structure?.payload[field];
    if (metadata == null) continue;
    if (!record(metadata)) return false;
    for (const value of Object.values(metadata)) {
      if (value == null) continue;
      if (!record(value) || value.policyKind != null && value.policyKind !== "" || !empty(value.configuration)
        || value.isDefault != null && value.isDefault !== false
        || Object.keys(value).some(key => !["policyKind", "configuration", ...(field === "connectionMetadata" ? ["label", "isDefault"] : [])].includes(key))) return false;
    }
  }
  return true;
}
