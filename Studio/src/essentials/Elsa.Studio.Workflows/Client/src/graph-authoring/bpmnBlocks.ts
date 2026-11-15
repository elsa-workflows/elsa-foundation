import type { ActivityNode, ActivityPresentationRecord } from "../workflowTypes";
import { createActivityNode, getChildSlots } from "../workflowAdapter";
import { bpmnStructureKind, type BpmnElement, type BpmnSequenceFlow } from "../bpmn/bpmnTypes";
import { nativeBlockPlanning, type NativeBlockContext, type NativeBlockPlan, type ExtractNativeBlock } from "./nativeBlocks";
import { readBpmnBlockScope, readOrdinaryBpmnBody, type BpmnBlockRefuse } from "./bpmnBlockRegion";
import { extractBpmnBlockRegion, ungroupBpmnBlockRegion } from "./bpmnBlockTransform";
import { transferNativeBlockResults, type NativeBlockResultRouting } from "./nativeBlockResultTransfers";

const { guarded, ready, prepareDocument, edited, assertMemberReadsRemainInScope, assertUnreferenced } = nativeBlockPlanning;
const refuse: BpmnBlockRefuse = nativeBlockPlanning.refuse;
const typeKey = "Elsa.Activities.Bpmn.Activities.BpmnProcess";
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const empty = (value: unknown) => value == null || (Array.isArray(value) ? value.length === 0 : record(value) && Object.keys(value).length === 0);

function prepare(context: NativeBlockContext) {
  const scope = prepareDocument(context);
  readBpmnBlockScope(scope.owner, refuse);
  const reserved = new Set(scope.identities);
  const visit = (node: ActivityNode) => {
    if (node.structure?.kind === bpmnStructureKind) {
      const payload = node.structure.payload;
      for (const [field, key] of [["elements", "elementId"], ["sequenceFlows", "flowId"], ["lanes", "laneId"], ["pools", "poolId"]])
        if (Array.isArray(payload[field])) for (const item of payload[field]) if (record(item) && typeof item[key] === "string") reserved.add(item[key]);
      if (record(payload.diagram)) for (const field of ["shapes", "edges"]) if (record(payload.diagram[field])) Object.keys(payload.diagram[field]).forEach(id => reserved.add(id));
    }
    for (const slot of getChildSlots(node, context.catalog)) slot.activities.forEach(visit);
  };
  visit(context.document.rootActivity);
  context.document.layout.forEach(item => reserved.add(item.nodeId));
  context.document.activityPresentation.forEach(item => reserved.add(item.nodeId));
  return { ...scope, reserved };
}

function template(context: NativeBlockContext, version: string, id: string) {
  const choices = context.catalog.filter(item => item.activityVersionId === version);
  if (choices.length !== 1 || choices[0].available === false || choices[0].activityTypeKey !== typeKey || choices[0].executionType !== "Action")
    refuse("unavailable-bpmn-composite", "activityVersionId", "Choose an exact available native BPMN process version.");
  const block = createActivityNode(choices[0], id);
  if (block.structure?.kind !== bpmnStructureKind || block.structure.schemaVersion !== "1.0.0")
    refuse("unsupported-bpmn-composite", "activityVersionId", "This native BPMN structure version is not supported.");
  return block;
}
function newBlock(context: NativeBlockContext, request: Omit<ExtractNativeBlock, "selectedNodeIds">, reserved: Set<string>) {
  if (!request.nodeId || request.nodeId !== request.nodeId.trim() || reserved.has(request.nodeId))
    refuse("occupied-identity", "nodeId", "Choose a new, nonempty block identity without surrounding whitespace.");
  const name = request.displayName.trim(), description = request.description?.trim();
  if (!name || name.length > 200 || (description?.length ?? 0) > 2000)
    refuse("invalid-name", "displayName", "Enter a name of 1–200 characters and a description of at most 2,000 characters.");
  const block = template(context, request.activityVersionId, request.nodeId);
  const presentation: ActivityPresentationRecord = { nodeId: block.nodeId, displayName: name, ...(description ? { description } : {}) };
  return { block, presentation };
}

export function planExtractBpmnBlock(context: NativeBlockContext, request: ExtractNativeBlock): NativeBlockPlan {
  return guarded(() => {
    const scope = prepare(context);
    const { block, presentation } = newBlock(context, request, scope.reserved);
    const transformed = extractBpmnBlockRegion(scope.owner, block, request.selectedNodeIds, context.catalog, context.document.layout, scope.reserved, refuse);
    for (const child of transformed.block.structure!.payload.activities as ActivityNode[])
      if (child.structure?.kind === bpmnStructureKind) readOrdinaryBpmnBody(child, context.catalog, refuse);
    assertMemberReadsRemainInScope(context, transformed.movedNodeIds);
    const document = edited(context, transformed.owner, transformed.layout, [...context.document.activityPresentation, presentation]);
    return ready(context, transferNativeBlockResults(context, document, scope.owner.nodeId, block.nodeId, transformed.movedNodeIds, refuse, undefined, resultRouting(context, scope.owner, transformed.movedNodeIds, transformed.owner, scope.reserved)), [transformed.elementId]);
  });
}

export function planUngroupBpmnBlock(context: NativeBlockContext, elementId: string): NativeBlockPlan {
  return guarded(() => {
    const scope = prepare(context);
    const structure = readBpmnBlockScope(scope.owner, refuse);
    const element = structure.elements.find(item => item.elementId === elementId);
    const block = element?.childNodeId ? structure.children.get(element.childNodeId) : undefined;
    if (!block || element?.elementType !== "subProcess" || block.structure?.kind !== bpmnStructureKind)
      refuse("unsupported-bpmn-block", `elements.${elementId}`, "Choose an ordinary native subprocess to Ungroup.");
    const defaults = template(context, block.activityVersionId, block.nodeId);
    defaults.inputs = [...defaults.inputs.filter(input => !record(input) || input.referenceKey !== "CanStartWorkflow"),
      { referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: false } }];
    for (const [key, value] of Object.entries(block)) {
      if (["nodeId", "activityVersionId", "structure"].includes(key)) continue;
      if (JSON.stringify(value) !== JSON.stringify(defaults[key]))
        refuse("boundary-settings", `nodes.${block.nodeId}.${key}`, "Ungroup would discard block inputs, outputs or execution settings.");
    }
    for (const [key, value] of Object.entries(block.structure!.payload)) {
      if (["activities", "elements", "sequenceFlows", "diagram"].includes(key)) continue;
      if (!empty(value) && JSON.stringify(value) !== JSON.stringify(defaults.structure!.payload[key]))
        refuse("boundary-declarations", `nodes.${block.nodeId}.structure.payload.${key}`, "Ungroup would discard local declarations, lanes, pools or process settings.");
    }
    const bodyDiagram = block.structure!.payload.diagram;
    if (record(bodyDiagram) && Object.keys(bodyDiagram).some(key => !["shapes", "edges"].includes(key)))
      refuse("boundary-metadata", `nodes.${block.nodeId}.diagram`, "Ungroup would discard retained process-diagram metadata.");
    assertUnreferenced(context, scope.owner, block, new Set([element]));
    const transformed = ungroupBpmnBlockRegion(scope.owner, elementId, context.catalog, context.document.layout, refuse);
    const inner = readOrdinaryBpmnBody(block, context.catalog, refuse);
    const removed = new Set([block.nodeId, elementId, inner.start.elementId, inner.end.elementId]);
    if (context.document.activityPresentation.some(item => removed.has(item.nodeId) && item.nodeId !== block.nodeId))
      refuse("boundary-metadata", "activityPresentation", "Ungroup would discard a process-boundary name or summary.");
    return ready(context, edited(context, transformed.owner, transformed.layout,
      context.document.activityPresentation.filter(item => !removed.has(item.nodeId))), transformed.selection);
  });
}

function resultRouting(context: NativeBlockContext, owner: ActivityNode, selected: Set<string>, transformed: ActivityNode, reserved: Set<string>): NativeBlockResultRouting {
  const ids = new Set(reserved);
  const collect = (node: ActivityNode) => {
    if (node.structure?.kind === bpmnStructureKind) {
      const scope = readBpmnBlockScope(node, refuse);
      scope.elements.forEach(element => ids.add(element.elementId)); scope.flows.forEach(flow => ids.add(flow.flowId));
    }
    for (const slot of getChildSlots(node, context.catalog)) slot.activities.forEach(collect);
  };
  collect(transformed);
  return {
    reservedIds: ids,
    assertTransfer(_producer, consumer, _explicitScope, path) {
      const body = readOrdinaryBpmnBody(owner, context.catalog, refuse);
      const ordered = body.region?.activities.map(child => child.nodeId) ?? [];
      const last = Math.max(...ordered.map((id, index) => selected.has(id) ? index : -1));
      if (last < 0 || ordered.indexOf(consumer) <= last)
        refuse("unproven-result-transfer", path, "The result reader must follow the selected work in one complete ordinary BPMN process.");
    },
    insertCaptures(body, captures, allocate, document) {
      const elements = body.elements as BpmnElement[], flows = body.sequenceFlows as BpmnSequenceFlow[];
      const diagram = body.diagram as { shapes: Record<string, Record<string, unknown>>; edges: Record<string, Record<string, unknown>> };
      for (const [producer, chain] of captures) {
        const element = elements.find(item => item.childNodeId === producer)!;
        const outgoing = flows.filter(flow => flow.sourceRef === element.elementId);
        if (outgoing.length !== 1) refuse("unproven-result-transfer", `elements.${element.elementId}`, "The captured result needs one unconditional normal continuation.");
        let previous = element.elementId;
        const origin = document.layout.find(item => item.nodeId === previous) ?? diagram.shapes[previous] as { x: number; y: number };
        for (const [index, capture] of chain.entries()) {
          const elementId = allocate("capture-element"), flowId = allocate("capture-flow");
          const position = { x: origin.x + 120 * (index + 1), y: origin.y + 100 };
          elements.push({ elementId, elementType: "task", childNodeId: capture.nodeId, laneId: element.laneId ?? null });
          flows.push({ flowId, sourceRef: previous, targetRef: elementId });
          document.layout.push({ nodeId: elementId, ...position });
          diagram.shapes[elementId] = { ...position, width: 100, height: 80 };
          const previousShape = diagram.shapes[previous];
          diagram.edges[flowId] = { waypoints: [{ x: Number(previousShape.x) + Number(previousShape.width ?? 100), y: Number(previousShape.y) + Number(previousShape.height ?? 80) / 2 }, { x: position.x, y: position.y + 40 }] };
          previous = elementId;
        }
        outgoing[0].sourceRef = previous;
        const edge = diagram.edges[outgoing[0].flowId];
        if (edge && Array.isArray(edge.waypoints) && edge.waypoints.length) {
          const shape = diagram.shapes[previous];
          edge.waypoints[0] = { ...(edge.waypoints[0] as Record<string, unknown>), x: Number(shape.x) + 100, y: Number(shape.y) + 40 };
        }
      }
    }
  };
}

export function planCreateBpmnBlock(context: NativeBlockContext, request: Omit<ExtractNativeBlock, "selectedNodeIds"> & {
  beforeElementId?: string; position: { x: number; y: number };
}): NativeBlockPlan {
  return guarded(() => {
    const scope = prepare(context), current = readBpmnBlockScope(scope.owner, refuse);
    const { block, presentation } = newBlock(context, request, scope.reserved);
    if (!Number.isFinite(request.position.x) || !Number.isFinite(request.position.y))
      refuse("invalid-position", "position", "Choose a finite subprocess position.");
    const reserved = new Set([...scope.reserved, block.nodeId]);
    const allocate = (suffix: string) => {
      let id = `${block.nodeId}-${suffix}`, index = 1;
      while (reserved.has(id)) id = `${block.nodeId}-${suffix}-${++index}`;
      reserved.add(id); return id;
    };
    const elementId = allocate("subprocess"), startId = allocate("start"), endId = allocate("end"), flowId = allocate("empty");
    const bodyShapes = { [startId]: { x: 0, y: 0, width: 36, height: 36 }, [endId]: { x: 160, y: 0, width: 36, height: 36 } };
    block.inputs = [...block.inputs.filter(input => !record(input) || input.referenceKey !== "CanStartWorkflow"),
      { referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: false } }];
    block.structure = { ...block.structure!, payload: { ...block.structure!.payload, activities: [], elements: [
      { elementId: startId, elementType: "startEvent" }, { elementId: endId, elementType: "endEvent" }],
      sequenceFlows: [{ flowId, sourceRef: startId, targetRef: endId }], diagram: { shapes: bodyShapes, edges: { [flowId]: { waypoints: [{ x: 36, y: 18 }, { x: 160, y: 18 }] } } } } };
    let elements: BpmnElement[], flows: BpmnSequenceFlow[];
    const addedLayout = [{ nodeId: elementId, ...request.position }, { nodeId: startId, x: 0, y: 0 }, { nodeId: endId, x: 160, y: 0 }];
    let lane: string | null = null;
    if (!current.elements.length && !current.children.size && !current.flows.length) {
      if (request.beforeElementId) refuse("stale-bpmn-selection", "beforeElementId", "The insertion target is no longer in this process.");
      const start = allocate("parent-start"), end = allocate("parent-end");
      elements = [{ elementId: start, elementType: "startEvent" }, { elementId, elementType: "subProcess", childNodeId: block.nodeId }, { elementId: end, elementType: "endEvent" }];
      flows = [{ flowId: allocate("parent-entry"), sourceRef: start, targetRef: elementId }, { flowId: allocate("parent-exit"), sourceRef: elementId, targetRef: end }];
      addedLayout.push({ nodeId: start, x: request.position.x - 160, y: request.position.y }, { nodeId: end, x: request.position.x + 160, y: request.position.y });
    } else {
      const body = readOrdinaryBpmnBody(scope.owner, context.catalog, refuse);
      const target = request.beforeElementId ?? body.end.elementId;
      const incoming = current.flows.filter(flow => flow.targetRef === target);
      if (!current.elements.some(element => element.elementId === target) || incoming.length !== 1)
        refuse("unsupported-bpmn-insertion", "beforeElementId", "Choose a task, subprocess or plain end with one ordinary incoming flow.");
      lane = current.elements.find(element => element.elementId === target)!.laneId ?? null;
      elements = current.elements.flatMap(element => element.elementId === target ? [{ elementId, elementType: "subProcess", childNodeId: block.nodeId, laneId: lane }, element] : [element]);
      flows = [...current.flows.map(flow => flow === incoming[0] ? { ...flow, targetRef: elementId } : flow), { flowId: allocate("parent-exit"), sourceRef: elementId, targetRef: target }];
    }
    const existingDiagram = current.payload.diagram;
    if (existingDiagram != null && (!record(existingDiagram) || ["shapes", "edges"].some(field => existingDiagram[field] != null && !record(existingDiagram[field]))))
      refuse("malformed-bpmn-diagram", "diagram", "Repair the BPMN diagram before inserting a subprocess.");
    const sourceDiagram = record(existingDiagram) ? existingDiagram : {};
    const shapes = { ...(record(sourceDiagram.shapes) ? sourceDiagram.shapes : {}) };
    const edges = { ...(record(sourceDiagram.edges) ? sourceDiagram.edges : {}) };
    // New flows use native auto-routing; retained incoming bends/metadata keep their identity.
    for (const flow of flows) {
      const old = current.flows.find(item => item.flowId === flow.flowId);
      if (old && old.targetRef !== flow.targetRef && edges[flow.flowId] != null) {
        const edge = edges[flow.flowId];
        if (!record(edge) || !Array.isArray(edge.waypoints) || edge.waypoints.some(item => !record(item) || !Number.isFinite(item.x) || !Number.isFinite(item.y)))
          refuse("malformed-bpmn-edge", `diagram.edges.${flow.flowId}`, "Repair this incoming flow's waypoints before inserting a subprocess.");
        const points = structuredClone(edge.waypoints);
        if (points.length) points[points.length - 1] = { ...points[points.length - 1], x: request.position.x, y: request.position.y + 40 };
        edges[flow.flowId] = { ...edge, waypoints: points };
      }
    }
    for (const item of addedLayout.filter(item => item.nodeId !== startId && item.nodeId !== endId))
      shapes[item.nodeId] = { x: item.x, y: item.y, width: item.nodeId === elementId ? 100 : 36, height: item.nodeId === elementId ? 80 : 36 };
    const owner = { ...scope.owner, structure: { ...scope.owner.structure!, payload: { ...current.payload,
      activities: [...current.children.values(), block], elements, sequenceFlows: flows, diagram: { ...sourceDiagram, shapes, edges } } } };
    return ready(context, edited(context, owner, [...context.document.layout, ...addedLayout], [...context.document.activityPresentation, presentation]), [elementId]);
  });
}
