import type { ActivityCatalogItem, ActivityNode, DesignMetadataRecord } from "../workflowTypes";
import { defaultBpmnPosition } from "../bpmn/bpmnAdapter";
import { bpmnStructureKind } from "../bpmn/bpmnTypes";
import { readBpmnBlockScope, readLinearBpmnBlockRegion, readOrdinaryBpmnBody, type BpmnBlockRefuse } from "./bpmnBlockRegion";

type Fields = Record<string, unknown>;
const record = (value: unknown): value is Fields => value !== null && typeof value === "object" && !Array.isArray(value);
interface Position { x: number; y: number }
export interface BpmnBlockEdit {
  owner: ActivityNode;
  block: ActivityNode;
  elementId: string;
  layout: DesignMetadataRecord[];
  movedNodeIds: Set<string>;
}

function diagram(value: unknown, refuse: BpmnBlockRefuse): { source: Fields; shapes: Fields; edges: Fields } {
  if (value != null && !record(value)) refuse("malformed-bpmn-diagram", "diagram", "Repair the BPMN diagram before refactoring.");
  const source = record(value) ? value : {};
  for (const field of ["shapes", "edges"])
    if (source[field] != null && !record(source[field]))
      refuse("malformed-bpmn-diagram", `diagram.${field}`, "Diagram shapes and edges must be identity maps.");
  return { source, shapes: record(source.shapes) ? source.shapes : {}, edges: record(source.edges) ? source.edges : {} };
}

function point(value: unknown, offset: Position, path: string, refuse: BpmnBlockRefuse): Fields {
  if (!record(value) || !Number.isFinite(value.x) || !Number.isFinite(value.y))
    refuse("malformed-bpmn-position", path, "Diagram positions must be finite before refactoring.");
  return { ...value, x: (value.x as number) + offset.x, y: (value.y as number) + offset.y };
}

function edge(value: unknown, offset: Position, path: string, refuse: BpmnBlockRefuse): Fields {
  if (!record(value) || !Array.isArray(value.waypoints))
    refuse("malformed-bpmn-edge", path, "Repair these diagram edge waypoints before refactoring.");
  return { ...value, waypoints: value.waypoints.map((item, index) => point(item, offset, `${path}.waypoints[${index}]`, refuse)) };
}

/** Immutable structural edit only; the composing resource still validates and commits the complete document. */
export function extractBpmnBlockRegion(owner: ActivityNode, template: ActivityNode, selectedElementIds: string[],
  catalog: ActivityCatalogItem[], layout: DesignMetadataRecord[], reservedIds: ReadonlySet<string>, refuse: BpmnBlockRefuse): BpmnBlockEdit {
  const source = structuredClone(owner);
  const scope = readBpmnBlockScope(source, refuse);
  const region = readLinearBpmnBlockRegion(source, selectedElementIds, catalog, refuse);
  const block = structuredClone(template);
  if (block.structure?.kind !== bpmnStructureKind || block.structure.schemaVersion !== "1.0.0")
    refuse("unavailable-bpmn-composite", "activityVersionId", "Choose a supported native BPMN process version.");
  const selected = new Set(selectedElementIds);
  const moved = new Set(region.activities.map(child => child.nodeId));
  const occupied = new Set([...reservedIds, source.nodeId, ...scope.elements.map(element => element.elementId), ...scope.flows.map(flow => flow.flowId),
    ...scope.children.keys(), ...layout.map(item => item.nodeId)]);
  if (occupied.has(block.nodeId)) refuse("occupied-identity", "nodeId", "Choose an unoccupied block identity.");
  occupied.add(block.nodeId);
  const allocate = (suffix: string) => {
    let id = `${block.nodeId}-${suffix}`, index = 1;
    while (occupied.has(id)) id = `${block.nodeId}-${suffix}-${++index}`;
    occupied.add(id); return id;
  };
  const elementId = allocate("subprocess"), startId = allocate("start"), endId = allocate("end");
  const entryId = allocate("entry"), exitId = allocate("exit");
  const stored = diagram(scope.payload.diagram, refuse);
  const positions = new Map(scope.elements.map((element, index) => {
    const saved = layout.find(item => item.nodeId === element.elementId) ?? stored.shapes[element.elementId] ?? defaultBpmnPosition(index);
    const resolved = point(saved, { x: 0, y: 0 }, `layout.${element.elementId}`, refuse);
    return [element.elementId, { x: resolved.x as number, y: resolved.y as number }];
  }));
  const origin = { x: Math.min(...region.elements.map(element => positions.get(element.elementId)!.x)),
    y: Math.min(...region.elements.map(element => positions.get(element.elementId)!.y)) };
  const delta = { x: -origin.x, y: -origin.y };
  const innerShapes: Fields = {};
  for (const element of region.elements) {
    const previous = stored.shapes[element.elementId];
    if (previous != null && !record(previous)) refuse("malformed-bpmn-shape", `diagram.shapes.${element.elementId}`, "Repair this diagram shape before refactoring.");
    if (record(previous) && [previous.width, previous.height].some(size => size != null && (typeof size !== "number" || !Number.isFinite(size) || size <= 0)))
      refuse("malformed-bpmn-shape", `diagram.shapes.${element.elementId}`, "Diagram shape sizes must be positive finite numbers.");
    const position = positions.get(element.elementId)!;
    innerShapes[element.elementId] = { ...(record(previous) ? previous : {}), width: (record(previous) ? previous.width : null) ?? 100, height: (record(previous) ? previous.height : null) ?? 80, x: position.x + delta.x, y: position.y + delta.y };
  }
  const first = innerShapes[region.elements[0].elementId] as Fields, last = innerShapes[region.elements.at(-1)!.elementId] as Fields;
  const right = Math.max(...Object.values(innerShapes).map(shape => Number((shape as Fields).x) + Number((shape as Fields).width)));
  const start = { x: -96, y: Number(first.y) + Number(first.height) / 2 - 18 }, end = { x: right + 96, y: Number(last.y) + Number(last.height) / 2 - 18 };
  innerShapes[startId] = { ...start, width: 36, height: 36 };
  innerShapes[endId] = { ...end, width: 36, height: 36 };
  const internalIds = new Set(region.internal.map(flow => flow.flowId));
  const innerEdges: Fields = Object.fromEntries(Object.entries(stored.edges).filter(([id]) => internalIds.has(id))
    .map(([id, value]) => [id, edge(value, delta, `diagram.edges.${id}`, refuse)]));
  innerEdges[entryId] = { waypoints: [{ x: start.x + 36, y: start.y + 18 }, { x: first.x, y: Number(first.y) + Number(first.height) / 2 }] };
  innerEdges[exitId] = { waypoints: [{ x: Number(last.x) + Number(last.width), y: Number(last.y) + Number(last.height) / 2 }, { x: end.x, y: end.y + 18 }] };
  block.inputs = [...block.inputs.filter(input => !record(input) || input.referenceKey !== "CanStartWorkflow"),
    { referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: false } }];
  block.structure = { ...block.structure!, payload: { ...block.structure!.payload, activities: region.activities,
    elements: [{ elementId: startId, elementType: "startEvent" }, ...region.elements.map(element => ({ ...element, laneId: null })),
      { elementId: endId, elementType: "endEvent" }],
    sequenceFlows: [{ flowId: entryId, sourceRef: startId, targetRef: region.elements[0].elementId }, ...region.internal,
      { flowId: exitId, sourceRef: region.elements.at(-1)!.elementId, targetRef: endId }],
    diagram: { shapes: innerShapes, edges: innerEdges } } };
  const parentElements = scope.elements.filter(element => !selected.has(element.elementId));
  parentElements.splice(scope.elements.findIndex(element => selected.has(element.elementId)), 0,
    { elementId, elementType: "subProcess", childNodeId: block.nodeId, laneId: region.laneId });
  const parentActivities = [...scope.children.values()].filter(child => !moved.has(child.nodeId));
  const firstChild = [...scope.children.keys()].findIndex(id => moved.has(id));
  parentActivities.splice(firstChild, 0, block);
  const parentFlows = scope.flows.filter(flow => !internalIds.has(flow.flowId)).map(flow => ({ ...flow,
    sourceRef: selected.has(flow.sourceRef) ? elementId : flow.sourceRef,
    targetRef: selected.has(flow.targetRef) ? elementId : flow.targetRef }));
  const outerShapes = Object.fromEntries(Object.entries(stored.shapes).filter(([id]) => !selected.has(id)));
  outerShapes[elementId] = { ...origin, width: 100, height: 80 };
  const outerEdges = Object.fromEntries(Object.entries(stored.edges).filter(([id]) => !internalIds.has(id)));
  // Keep authored intermediate bends; only the endpoint attached to the replaced region moves.
  for (const flow of [region.incoming, region.outgoing]) if (outerEdges[flow.flowId] != null) {
    const previous = edge(outerEdges[flow.flowId], { x: 0, y: 0 }, `diagram.edges.${flow.flowId}`, refuse);
    const points = previous.waypoints as Fields[];
    if (points.length) points[flow === region.incoming ? points.length - 1 : 0] =
      { ...points[flow === region.incoming ? points.length - 1 : 0], x: origin.x + (flow === region.incoming ? 0 : 100), y: origin.y + 40 };
    outerEdges[flow.flowId] = previous;
  }
  source.structure = { ...source.structure!, payload: { ...scope.payload, activities: parentActivities, elements: parentElements,
    sequenceFlows: parentFlows, diagram: { ...stored.source, shapes: outerShapes, edges: outerEdges } } };
  const nextLayout = structuredClone(layout).map(item => selected.has(item.nodeId) ? { ...item, x: item.x + delta.x, y: item.y + delta.y } : item);
  for (const element of region.elements) if (!nextLayout.some(item => item.nodeId === element.elementId))
    nextLayout.push({ nodeId: element.elementId, x: positions.get(element.elementId)!.x + delta.x, y: positions.get(element.elementId)!.y + delta.y });
  nextLayout.push({ nodeId: elementId, ...origin }, { nodeId: startId, ...start }, { nodeId: endId, ...end });
  return { owner: source, block, elementId, layout: nextLayout, movedNodeIds: moved };
}

/** Remove an ordinary process wrapper without flattening any nested subprocesses or changing child identities. */
export function ungroupBpmnBlockRegion(owner: ActivityNode, elementId: string, catalog: ActivityCatalogItem[],
  layout: DesignMetadataRecord[], refuse: BpmnBlockRefuse) {
  const source = structuredClone(owner);
  const outer = readBpmnBlockScope(source, refuse);
  const boundary = readLinearBpmnBlockRegion(source, [elementId], catalog, refuse);
  const block = boundary.activities[0];
  if (boundary.elements[0].elementType !== "subProcess")
    refuse("unsupported-bpmn-block", `elements.${elementId}`, "Choose an ordinary native subprocess to Ungroup.");
  const inner = readOrdinaryBpmnBody(block, catalog, refuse);
  const outside = diagram(outer.payload.diagram, refuse), inside = diagram(inner.payload.diagram, refuse);
  const selectedElement = boundary.elements[0];
  for (const [key, value] of Object.entries(selectedElement)) {
    // The named wrapper and its placement disappear by the author's explicit Ungroup action.
    if (["elementId", "elementType", "childNodeId", "laneId", "name"].includes(key)) continue;
    if (value == null || value === false || key === "cancelActivity" && value === true || Array.isArray(value) && !value.length || record(value) && !Object.keys(value).length) continue;
    refuse("boundary-metadata", `elements.${elementId}.${key}`, "Ungroup would discard authored subprocess properties. Keep this block or remove those properties first.");
  }
  const position = layout.find(item => item.nodeId === elementId) ?? outside.shapes[elementId] ?? { x: 0, y: 0 };
  const origin = point(position, { x: 0, y: 0 }, `layout.${elementId}`, refuse) as unknown as Position;
  const removed = new Set([elementId, inner.start.elementId, inner.end.elementId]);
  const droppedFlows = new Set([inner.region?.incoming.flowId, inner.region?.outgoing.flowId, ...(!inner.region ? inner.flows.map(flow => flow.flowId) : [])].filter((id): id is string => !!id));
  // Only plain routing scaffolding can disappear. A user-authored name or extension stays visible as a refusal.
  for (const event of [inner.start, inner.end]) for (const [key, value] of Object.entries(event)) {
    if (["elementId", "elementType"].includes(key)) continue;
    if (value == null || value === false || key === "cancelActivity" && value === true || Array.isArray(value) && !value.length || record(value) && !Object.keys(value).length) continue;
    refuse("boundary-metadata", `elements.${event.elementId}.${key}`, "Ungroup would discard authored process-boundary metadata.");
  }
  for (const flow of inner.flows.filter(item => droppedFlows.has(item.flowId))) for (const [key, value] of Object.entries(flow)) {
    if (["flowId", "sourceRef", "targetRef"].includes(key) || value == null || value === false) continue;
    refuse("boundary-metadata", `sequenceFlows.${flow.flowId}.${key}`, "Ungroup would discard authored boundary-flow metadata.");
  }
  for (const id of removed) {
    const shape = id === elementId ? outside.shapes[id] : inside.shapes[id];
    if (record(shape) && Object.keys(shape).some(key => !["x", "y", "width", "height"].includes(key)))
      refuse("boundary-metadata", `diagram.shapes.${id}`, "Ungroup would discard retained shape metadata.");
  }
  for (const id of droppedFlows) if (record(inside.edges[id]) && Object.keys(inside.edges[id]).some(key => key !== "waypoints"))
    refuse("boundary-metadata", `diagram.edges.${id}`, "Ungroup would discard retained boundary-edge metadata.");
  const childElements = inner.region?.elements ?? [];
  const innerFlows = inner.region?.internal ?? [];
  const occupied = new Set([...outer.elements.map(element => element.elementId), ...outer.flows.map(flow => flow.flowId)]);
  if ([...childElements.map(element => element.elementId), ...innerFlows.map(flow => flow.flowId)].some(id => occupied.has(id) || id in outside.shapes || id in outside.edges))
    refuse("bpmn-identity-collision", `elements.${elementId}`, "Ungroup would overwrite a retained outer identity or diagram entry.");
  const shapes = Object.fromEntries(Object.entries(outside.shapes).filter(([id]) => id !== elementId));
  for (const [id, shape] of Object.entries(inside.shapes)) {
    if (removed.has(id)) continue;
    if (!childElements.some(element => element.elementId === id) || id in shapes)
      refuse("bpmn-diagram-collision", `diagram.shapes.${id}`, "Ungroup would relocate unrelated diagram metadata.");
    const saved = layout.find(item => item.nodeId === id);
    shapes[id] = point(saved ? { ...(record(shape) ? shape : {}), x: saved.x, y: saved.y } : shape, origin, `diagram.shapes.${id}`, refuse);
  }
  const edges = { ...outside.edges };
  for (const [id, value] of Object.entries(inside.edges)) {
    if (droppedFlows.has(id)) continue;
    if (!innerFlows.some(flow => flow.flowId === id) || id in edges)
      refuse("bpmn-diagram-collision", `diagram.edges.${id}`, "Ungroup would relocate unrelated edge metadata.");
    edges[id] = edge(value, origin, `diagram.edges.${id}`, refuse);
  }
  let flows = outer.flows.map(flow => ({ ...flow }));
  if (inner.region) {
    const first = childElements[0].elementId, last = childElements.at(-1)!.elementId;
    flows = flows.map(flow => ({ ...flow, sourceRef: flow.sourceRef === elementId ? last : flow.sourceRef,
      targetRef: flow.targetRef === elementId ? first : flow.targetRef }));
    for (const [flow, endpoint, index] of [[boundary.incoming, first, -1], [boundary.outgoing, last, 0]] as const) if (edges[flow.flowId] != null) {
      const value = edge(edges[flow.flowId], { x: 0, y: 0 }, `diagram.edges.${flow.flowId}`, refuse);
      const points = value.waypoints as Fields[], shape = shapes[endpoint];
      if (points.length && record(shape)) {
        const position = index < 0 ? points.length - 1 : index;
        points[position] = { ...points[position], x: Number(shape.x) + (index === 0 ? Number(shape.width ?? 100) : 0), y: Number(shape.y) + Number(shape.height ?? 80) / 2 };
      }
      edges[flow.flowId] = value;
    }
  } else {
    // Preserve the outer incoming flow identity and metadata; refuse loss of a named/extended outgoing edge.
    for (const [key, value] of Object.entries(boundary.outgoing)) if (!["flowId", "sourceRef", "targetRef"].includes(key) && value != null && value !== false)
      refuse("empty-bpmn-exit-metadata", `sequenceFlows.${boundary.outgoing.flowId}.${key}`, "Ungroup would discard this empty subprocess's authored outgoing flow metadata.");
    const a = edges[boundary.incoming.flowId], b = edges[boundary.outgoing.flowId];
    if (a != null || b != null) {
      if (!record(a) || !record(b) || Object.keys(b).some(key => key !== "waypoints"))
        refuse("empty-bpmn-edge-metadata", "diagram.edges", "Ungroup cannot combine these retained boundary edges without loss.");
      const first = edge(a, { x: 0, y: 0 }, "diagram.edges.entry", refuse), last = edge(b, { x: 0, y: 0 }, "diagram.edges.exit", refuse);
      edges[boundary.incoming.flowId] = { ...first, waypoints: [...(first.waypoints as Fields[]).slice(0, -1), ...(last.waypoints as Fields[]).slice(1)] };
    }
    delete edges[boundary.outgoing.flowId];
    flows = flows.filter(flow => flow.flowId !== boundary.outgoing.flowId).map(flow => flow.targetRef === elementId ? { ...flow, targetRef: boundary.outgoing.targetRef } : flow);
  }
  source.structure = { ...source.structure!, payload: { ...outer.payload,
    activities: [...outer.children.values()].flatMap(child => child.nodeId === block.nodeId ? inner.region?.activities ?? [] : [child]),
    elements: outer.elements.flatMap(element => element.elementId === elementId ? childElements.map(child => ({ ...child, laneId: selectedElement.laneId ?? null })) : [element]),
    sequenceFlows: [...flows, ...innerFlows], diagram: { ...outside.source, shapes, edges } } };
  const nextLayout = structuredClone(layout).filter(item => !removed.has(item.nodeId) && item.nodeId !== block.nodeId).map(item =>
    childElements.some(element => element.elementId === item.nodeId) ? { ...item, x: item.x + origin.x, y: item.y + origin.y } : item);
  return { owner: source, block, layout: nextLayout, selection: childElements.map(element => element.elementId) };
}
