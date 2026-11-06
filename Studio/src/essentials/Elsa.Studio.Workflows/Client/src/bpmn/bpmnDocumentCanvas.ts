import type { Edge, Node } from "@xyflow/react";
import type { ActivityNode } from "../workflowTypes";
import { bpmnNotationFromElement, type BpmnNotationDescriptor } from "./bpmnNotation";
import { readBpmnDocumentAppearance, type BpmnDocumentAppearance } from "./bpmnDocumentAppearance";
import { isGatewayElementType, isSubProcessElementType } from "./bpmnTypes";
import { projectBpmnDocumentScene, readBpmnDocument, resolveBpmnDocumentReference,
  type BpmnBounds, type BpmnDocumentBinding, type BpmnDocumentCommand, type BpmnDocumentScene, type BpmnDocumentShape, type BpmnPoint, type BpmnScopeAddress, type ParsedBpmnDocument } from "./bpmnDocument";
import { assessBpmnBoundaryAttachment, assessBpmnDocumentResize, bpmnDocumentDisplayEndpoints, bpmnFlowScope, bpmnOwningProcess, isBpmnModelElement, type BpmnDocumentEndpoint } from "./bpmnDocumentPolicy";

export type { BpmnScopeAddress } from "./bpmnDocument";
export interface BpmnDocumentCanvasDiagnostic { code: string; elementId: string; diId?: string; relatedDiIds?: string[]; message: string }
export interface BpmnDocumentNodeData extends Record<string, unknown> {
  semanticElementId: string;
  semanticType: string;
  element: Element;
  notation: BpmnNotationDescriptor;
  absoluteBounds: BpmnBounds;
  labelBounds?: BpmnBounds;
  semanticScopeId?: string;
  laneIds: string[];
  attachmentHostId?: string;
  binding?: BpmnDocumentBinding;
}
export interface BpmnDocumentEdgeData extends Record<string, unknown> {
  semanticElementId: string;
  semanticType: string;
  element: Element;
  points: BpmnPoint[];
  labelBounds?: BpmnBounds;
  semanticSource: BpmnDocumentEndpoint;
  semanticTarget: BpmnDocumentEndpoint;
  appearance: BpmnDocumentAppearance;
  isDefault?: boolean;
  isConditional?: boolean;
}
export interface BpmnDocumentCanvas {
  scope: BpmnScopeAddress;
  scene: BpmnDocumentScene;
  nodes: Node<BpmnDocumentNodeData>[];
  edges: Edge<BpmnDocumentEdgeData>[];
  /** Retained DI whose endpoints cannot be drawn in this view; never used as a write mirror. */
  unresolvedEdgeIds: string[];
  diagnostics: BpmnDocumentCanvasDiagnostic[];
}

function fail(message: string): never { throw new Error(`Cannot plan BPMN canvas edit: ${message}.`); }
function identity(element: Element | null | undefined) { return element?.getAttribute("id") ?? undefined; }
function center(bounds: BpmnBounds): BpmnPoint { return { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 }; }
function distance(point: BpmnPoint, bounds: BpmnBounds) {
  return Math.hypot(Math.max(bounds.x - point.x, 0, point.x - bounds.x - bounds.width), Math.max(bounds.y - point.y, 0, point.y - bounds.y - bounds.height));
}
function scopeElement(parsed: ParsedBpmnDocument, address: BpmnScopeAddress, owner: ActivityNode, scene: BpmnDocumentScene) {
  if (owner.nodeId !== address.documentOwnerNodeId || scene.diagramId !== address.diagramId) fail("document or diagram scope is stale");
  const scope = parsed.byId.get(address.scopeElementId), planeScope = parsed.byId.get(scene.scopeId);
  if (!isBpmnModelElement(scope) || !["collaboration", "process", "subProcess", "transaction", "adHocSubProcess"].includes(scope.localName)) fail("standard scope is unavailable");
  const representedProcesses = planeScope?.localName === "collaboration" ? [...planeScope.children]
    .filter(element => isBpmnModelElement(element) && element.localName === "participant")
    .map(element => resolveBpmnDocumentReference(parsed, element, element.getAttribute("processRef"))).filter(Boolean) : [];
  if (scope !== planeScope && !planeScope?.contains(scope) && !representedProcesses.includes(bpmnOwningProcess(scope))) fail("scope is not represented in this DI plane");
  return scope;
}

/** Complete source/DI view only. Opening a scope never allocates identities or rewrites the owner. */
export function projectBpmnDocumentCanvas(owner: ActivityNode, address: BpmnScopeAddress): BpmnDocumentCanvas {
  const parsed = readBpmnDocument(owner), sourceScene = projectBpmnDocumentScene(owner, address.diagramId);
  // The public scene parser owns its own DOM. Use one identity index for all relationship comparisons.
  const scene = { ...sourceScene, shapes: sourceScene.shapes.map(shape => ({ ...shape, element: parsed.byId.get(shape.elementId)! })),
    edges: sourceScene.edges.map(edge => ({ ...edge, element: parsed.byId.get(edge.elementId)! })) };
  const scope = scopeElement(parsed, address, owner, scene);
  const inScope = (element: Element) => scope.localName === "collaboration" || scope.contains(element);
  const shapes = scene.shapes.filter(shape => inScope(shape.element) && shape.element !== scope);
  const diagnostics: BpmnDocumentCanvasDiagnostic[] = [];
  const bySemanticId = new Map<string, BpmnDocumentShape[]>();
  for (const shape of shapes) bySemanticId.set(shape.elementId, [...(bySemanticId.get(shape.elementId) ?? []), shape]);
  const selectInstance = (instances: readonly BpmnDocumentShape[], position: BpmnPoint, child?: BpmnDocumentShape): BpmnDocumentShape | undefined => {
    const candidates = [...instances].sort((a, b) => distance(position, a.bounds) - distance(position, b.bounds)
      || a.bounds.width * a.bounds.height - b.bounds.width * b.bounds.height || a.shapeId.localeCompare(b.shapeId));
    if (child && candidates.length > 1 && distance(position, candidates[0].bounds) === distance(position, candidates[1].bounds)) {
      diagnostics.push({ code: "ambiguous-di-parent", elementId: child.elementId, diId: child.shapeId,
        relatedDiIds: candidates.filter(candidate => distance(position, candidate.bounds) === distance(position, candidates[0].bounds)).map(candidate => candidate.shapeId),
        message: "Several DI instances match this visual parent; no relationship is inferred." });
      return undefined;
    }
    return candidates[0];
  };
  const pick = (elementId: string | undefined, position: BpmnPoint, child?: BpmnDocumentShape) => selectInstance(elementId ? bySemanticId.get(elementId) ?? [] : [], position, child);
  const poolFor = (element: Element, shape: BpmnDocumentShape) => {
    const process = bpmnOwningProcess(element);
    if (!process) return undefined;
    const pools = shapes.filter(candidate => candidate.elementType === "participant"
      && resolveBpmnDocumentReference(parsed, candidate.element, candidate.element.getAttribute("processRef")) === process);
    return selectInstance(pools, center(shape.bounds), shape);
  };
  const semanticParent = (element: Element, shape: BpmnDocumentShape) => {
    const parent = bpmnFlowScope(element);
    return parent && parent !== scope && isSubProcessElementType(parent.localName) ? pick(identity(parent), center(shape.bounds), shape) : poolFor(element, shape);
  };
  const lanes = [...parsed.byId.values()].filter(element => isBpmnModelElement(element) && element.localName === "lane");
  const laneMembership = (element: Element) => lanes.filter(lane => bpmnFlowScope(lane) === bpmnFlowScope(element) && [...lane.children]
    .some(child => isBpmnModelElement(child) && child.localName === "flowNodeRef" && resolveBpmnDocumentReference(parsed, child, child.textContent) === element));
  const depth = (element: Element) => { let count = 0; for (let parent = element.parentElement; parent; parent = parent.parentElement) count++; return count; };
  const parentById = new Map<string, string>();
  const membershipById = new Map<string, string[]>();
  const attachmentById = new Map<string, string>();
  for (const shape of shapes) {
    const element = shape.element;
    let parent: BpmnDocumentShape | undefined;
    if (element.localName === "boundaryEvent") {
      const host = resolveBpmnDocumentReference(parsed, element, element.getAttribute("attachedToRef"));
      const hostId = identity(host);
      if (hostId) attachmentById.set(shape.shapeId, hostId);
      parent = hostId && assessBpmnBoundaryAttachment(parsed, shape.elementId, hostId).allowed ? pick(hostId, center(shape.bounds), shape) : undefined;
      if (!parent) diagnostics.push({ code: "missing-attachment-di", elementId: shape.elementId, diId: shape.shapeId, message: "Boundary attachment has no host shape in this plane." });
    } else if (element.localName === "lane") {
      const parentLane = element.parentElement?.parentElement;
      parent = parentLane?.localName === "lane" ? pick(identity(parentLane), center(shape.bounds), shape) : semanticParent(element, shape);
    } else if (element.localName !== "participant") {
      const memberships = laneMembership(element).sort((a, b) => depth(b) - depth(a));
      membershipById.set(shape.shapeId, memberships.map(lane => identity(lane)!));
      if (memberships.length > 1 && depth(memberships[0]) === depth(memberships[1])) {
        diagnostics.push({ code: "ambiguous-lane-membership", elementId: shape.elementId, diId: shape.shapeId, message: "Flow node belongs to several equally nested lanes; memberships are retained." });
      } else parent = pick(identity(memberships[0]), center(shape.bounds), shape);
      parent ??= semanticParent(element, shape);
    }
    if (parent && parent.shapeId !== shape.shapeId) parentById.set(shape.shapeId, parent.shapeId);
  }
  const byShapeId = new Map(shapes.map(shape => [shape.shapeId, shape]));
  const ordered: BpmnDocumentShape[] = [], visiting = new Set<string>(), visited = new Set<string>();
  const visit = (shape: BpmnDocumentShape) => {
    if (visited.has(shape.shapeId)) return;
    if (visiting.has(shape.shapeId)) fail("visual containment is cyclic");
    visiting.add(shape.shapeId);
    const parent = byShapeId.get(parentById.get(shape.shapeId) ?? "");
    if (parent) visit(parent);
    visiting.delete(shape.shapeId); visited.add(shape.shapeId); ordered.push(shape);
  };
  shapes.forEach(visit);
  const hidden = new Map<string, boolean>();
  const nodes = ordered.map(shape => {
    const parent = byShapeId.get(parentById.get(shape.shapeId) ?? "");
    const collapsed = parent && (isSubProcessElementType(parent.elementType) && parent.isExpanded !== true || parent.elementType === "participant" && parent.isExpanded === false);
    hidden.set(shape.shapeId, !!parent && (hidden.get(parent.shapeId) === true || !!collapsed && shape.elementType !== "boundaryEvent"));
    const position = parent ? { x: shape.bounds.x - parent.bounds.x, y: shape.bounds.y - parent.bounds.y } : { x: shape.bounds.x, y: shape.bounds.y };
    return { id: shape.shapeId, type: "bpmnDocumentElement", position, parentId: parent?.shapeId, origin: [0, 0] as [number, number],
      width: shape.bounds.width, height: shape.bounds.height, style: { width: shape.bounds.width, height: shape.bounds.height },
      expandParent: false, hidden: hidden.get(shape.shapeId), zIndex: shape.elementType === "participant" || shape.elementType === "lane" ? 0 : shape.elementType === "boundaryEvent" ? 3 : 2,
      data: { semanticElementId: shape.elementId, semanticType: shape.elementType, element: shape.element,
        notation: bpmnNotationFromElement(shape.element, { ...shape.bounds, isExpanded: shape.isExpanded, isHorizontal: shape.isHorizontal, isMarkerVisible: shape.isMarkerVisible,
          appearance: readBpmnDocumentAppearance(parsed.byId.get(shape.shapeId)!) }),
        absoluteBounds: { ...shape.bounds }, labelBounds: shape.labelBounds && { ...shape.labelBounds }, semanticScopeId: identity(bpmnFlowScope(shape.element)),
        laneIds: membershipById.get(shape.shapeId) ?? [], attachmentHostId: attachmentById.get(shape.shapeId),
        binding: parsed.payload.bindings.find(binding => binding.elementId === shape.elementId) }
    } satisfies Node<BpmnDocumentNodeData>;
  });
  const edges: Edge<BpmnDocumentEdgeData>[] = [], unresolvedEdgeIds: string[] = [];
  for (const edge of scene.edges.filter(edge => inScope(edge.element))) {
    const endpoints = bpmnDocumentDisplayEndpoints(parsed, edge);
    const source = pick(endpoints.source.displayElementId, edge.points[0] ?? { x: 0, y: 0 });
    const target = pick(endpoints.target.displayElementId, edge.points.at(-1) ?? { x: 0, y: 0 });
    if (!source || !target) {
      unresolvedEdgeIds.push(edge.edgeId);
      diagnostics.push({ code: "missing-edge-di", elementId: edge.elementId, diId: edge.edgeId, message: "A semantic connection endpoint has no shape in this view; source references are retained." });
      continue;
    }
    const semanticSource = parsed.byId.get(endpoints.source.referenceIds[0]);
    edges.push({ id: edge.edgeId, type: "bpmnDocumentConnection", source: source.shapeId, target: target.shapeId,
      hidden: hidden.get(source.shapeId) || hidden.get(target.shapeId), zIndex: edge.elementType.endsWith("Association") ? 4 : 1,
      data: { semanticElementId: edge.elementId, semanticType: edge.elementType, element: edge.element, points: edge.points.map(point => ({ ...point })),
        labelBounds: edge.labelBounds && { ...edge.labelBounds }, semanticSource: endpoints.source, semanticTarget: endpoints.target,
        appearance: readBpmnDocumentAppearance(parsed.byId.get(edge.edgeId)!),
        isDefault: edge.elementType === "sequenceFlow" && !!semanticSource && resolveBpmnDocumentReference(parsed, semanticSource, semanticSource.getAttribute("default")) === edge.element,
        isConditional: edge.elementType === "sequenceFlow" && !!semanticSource && !isGatewayElementType(semanticSource.localName)
          && [...edge.element.children].some(child => isBpmnModelElement(child) && child.localName === "conditionExpression") } });
  }
  return { scope: { ...address }, scene, nodes, edges, unresolvedEdgeIds, diagnostics };
}

function finitePoint(point: BpmnPoint) { if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) fail("positions must be finite"); }
function finiteBounds(bounds: BpmnBounds) { finitePoint(bounds); if (!Number.isFinite(bounds.width) || !Number.isFinite(bounds.height) || bounds.width <= 0 || bounds.height <= 0) fail("dimensions must be finite and positive"); }
function equalPoint(a: BpmnPoint, b: BpmnPoint) { return a.x === b.x && a.y === b.y; }
function equalBounds(a: BpmnBounds, b: BpmnBounds) { return equalPoint(a, b) && a.width === b.width && a.height === b.height; }
function translated<T extends BpmnPoint>(value: T, delta: BpmnPoint): T { return { ...value, x: value.x + delta.x, y: value.y + delta.y }; }
function dock(point: BpmnPoint, old: BpmnBounds, next: BpmnBounds): BpmnPoint {
  const sides = [{ distance: Math.abs(point.x - old.x), side: "left" }, { distance: Math.abs(point.x - old.x - old.width), side: "right" },
    { distance: Math.abs(point.y - old.y), side: "top" }, { distance: Math.abs(point.y - old.y - old.height), side: "bottom" }].sort((a, b) => a.distance - b.distance);
  const xRatio = Math.max(0, Math.min(1, (point.x - old.x) / old.width)), yRatio = Math.max(0, Math.min(1, (point.y - old.y) / old.height));
  return sides[0].side === "left" || sides[0].side === "right" ? { x: next.x + (sides[0].side === "right" ? next.width : 0), y: next.y + yRatio * next.height }
    : { x: next.x + xRatio * next.width, y: next.y + (sides[0].side === "bottom" ? next.height : 0) };
}
function midpoint(points: readonly BpmnPoint[]): BpmnPoint {
  const lengths = points.slice(1).map((end, index) => Math.hypot(end.x - points[index].x, end.y - points[index].y));
  let remaining = lengths.reduce((sum, length) => sum + length, 0) / 2;
  for (const [index, length] of lengths.entries()) {
    if (remaining <= length && length > 0) return { x: points[index].x + (points[index + 1].x - points[index].x) * remaining / length,
      y: points[index].y + (points[index + 1].y - points[index].y) * remaining / length };
    remaining -= length;
  }
  return points[0];
}

function geometryCommands(canvas: BpmnDocumentCanvas, next: ReadonlyMap<string, BpmnBounds>): BpmnDocumentCommand[] {
  const commands: BpmnDocumentCommand[] = [];
  const nodes = new Map(canvas.nodes.map(node => [node.id, node]));
  const changed = new Set([...next].filter(([id, bounds]) => {
    const node = nodes.get(id);
    return node && !equalBounds(node.data.absoluteBounds, bounds);
  }).map(([id]) => nodes.get(id)!.data.semanticElementId));
  for (const edge of canvas.scene.edges.filter(edge => canvas.unresolvedEdgeIds.includes(edge.edgeId))) {
    const references = [...edge.sourceRefs, ...edge.targetRefs,
      ...(edge.elementType === "dataInputAssociation" || edge.elementType === "dataOutputAssociation" ? [identity(edge.element.parentElement)] : [])];
    if (references.some(reference => reference && changed.has(reference))) fail(`edge '${edge.edgeId}' needs its missing endpoint DI before this move`);
  }
  for (const diagnostic of canvas.diagnostics.filter(item => item.code === "ambiguous-di-parent" && item.diId)) {
    for (const id of [diagnostic.diId!, ...(diagnostic.relatedDiIds ?? [])]) {
      const old = nodes.get(id)?.data.absoluteBounds, bounds = next.get(id);
      if (old && bounds && !equalBounds(old, bounds)) fail("visual parent DI is ambiguous for this move");
    }
  }
  for (const [id, bounds] of next) {
    const node = nodes.get(id);
    if (!node) fail(`shape '${id}' is outside this scope`);
    finiteBounds(bounds);
    const previous = node.data.absoluteBounds;
    if (equalBounds(previous, bounds)) continue;
    commands.push({ kind: "bounds", shapeId: id, bounds });
    if (node.data.labelBounds && !equalPoint(previous, bounds)) commands.push({ kind: "labelBounds", diId: id,
      bounds: translated(node.data.labelBounds, { x: bounds.x - previous.x, y: bounds.y - previous.y }) });
  }
  for (const edge of canvas.edges) {
    const source = nodes.get(edge.source)!, target = nodes.get(edge.target)!;
    const oldSource = source.data.absoluteBounds, oldTarget = target.data.absoluteBounds;
    const nextSource = next.get(source.id) ?? oldSource, nextTarget = next.get(target.id) ?? oldTarget;
    if (equalBounds(oldSource, nextSource) && equalBounds(oldTarget, nextTarget)) continue;
    const oldPoints = edge.data!.points;
    if (oldPoints.length < 2) fail(`edge '${edge.id}' has no usable authored route`);
    const sourceDelta = { x: nextSource.x - oldSource.x, y: nextSource.y - oldSource.y }, targetDelta = { x: nextTarget.x - oldTarget.x, y: nextTarget.y - oldTarget.y };
    const translatedTogether = equalPoint(sourceDelta, targetDelta) && oldSource.width === nextSource.width && oldSource.height === nextSource.height
      && oldTarget.width === nextTarget.width && oldTarget.height === nextTarget.height;
    const points = oldPoints.map(point => translated(point, translatedTogether ? sourceDelta : { x: 0, y: 0 }));
    if (!translatedTogether) {
      points[0] = oldSource.width === nextSource.width && oldSource.height === nextSource.height ? translated(oldPoints[0], sourceDelta) : dock(oldPoints[0], oldSource, nextSource);
      const end = points.length - 1;
      points[end] = oldTarget.width === nextTarget.width && oldTarget.height === nextTarget.height ? translated(oldPoints[end], targetDelta) : dock(oldPoints[end], oldTarget, nextTarget);
      // Change only the bend adjacent to a moved endpoint; retain the remaining authored route.
      if (points.length === 3) {
        const x: number[] = [], y: number[] = [];
        if (oldPoints[0].x === oldPoints[1].x) x.push(points[0].x);
        if (oldPoints[0].y === oldPoints[1].y) y.push(points[0].y);
        if (oldPoints[2].x === oldPoints[1].x) x.push(points[2].x);
        if (oldPoints[2].y === oldPoints[1].y) y.push(points[2].y);
        if (new Set(x).size > 1 || new Set(y).size > 1) fail(`edge '${edge.id}' needs an explicit reroute for its shared bend`);
        points[1] = { x: x[0] ?? points[1].x, y: y[0] ?? points[1].y };
      } else if (points.length > 3) {
        if (oldPoints[0].x === oldPoints[1].x) points[1].x = points[0].x;
        else if (oldPoints[0].y === oldPoints[1].y) points[1].y = points[0].y;
        if (oldPoints[end].x === oldPoints[end - 1].x) points[end - 1].x = points[end].x;
        else if (oldPoints[end].y === oldPoints[end - 1].y) points[end - 1].y = points[end].y;
      }
    }
    if (points.every((point, index) => equalPoint(point, oldPoints[index]))) continue;
    commands.push({ kind: "waypoints", edgeId: edge.id, points });
    if (edge.data!.labelBounds) {
      const before = midpoint(oldPoints), after = midpoint(points);
      commands.push({ kind: "labelBounds", diId: edge.id, bounds: translated(edge.data!.labelBounds, { x: after.x - before.x, y: after.y - before.y }) });
    }
  }
  return commands;
}

/** Final React Flow positions are relative to parentId. Parent and child selections never add a delta twice. */
export function planBpmnDocumentMove(canvas: BpmnDocumentCanvas, positions: ReadonlyMap<string, BpmnPoint>): BpmnDocumentCommand[] {
  const nodes = new Map(canvas.nodes.map(node => [node.id, node]));
  for (const [id, point] of positions) {
    const node = nodes.get(id);
    if (!node) fail(`shape '${id}' is outside this scope`);
    if (node.data.semanticType === "boundaryEvent" && !node.parentId) fail("boundary attachment context is unavailable");
    finitePoint(point);
  }
  const next = new Map<string, BpmnBounds>();
  for (const node of canvas.nodes) {
    const position = positions.get(node.id) ?? node.position;
    const parent = node.parentId ? next.get(node.parentId) : undefined;
    const previousParent = node.parentId ? nodes.get(node.parentId)?.data.absoluteBounds : undefined;
    const bounds = { ...node.data.absoluteBounds,
      x: node.data.absoluteBounds.x + (parent && previousParent ? parent.x - previousParent.x : 0) + (position.x - node.position.x),
      y: node.data.absoluteBounds.y + (parent && previousParent ? parent.y - previousParent.y : 0) + (position.y - node.position.y) };
    if (node.data.attachmentHostId && positions.has(node.id) && !equalPoint(position, node.position) && parent) {
      const attached = dock(center(bounds), parent, parent);
      bounds.x = attached.x - bounds.width / 2; bounds.y = attached.y - bounds.height / 2;
    }
    next.set(node.id, bounds);
  }
  return geometryCommands(canvas, next);
}

/** Resize keeps ordinary children at their absolute positions; attached boundary centers retain their border side/fraction. */
export function planBpmnDocumentResize(canvas: BpmnDocumentCanvas, shapeId: string, bounds: BpmnBounds, direction?: string): BpmnDocumentCommand[] {
  finiteBounds(bounds);
  const node = canvas.nodes.find(candidate => candidate.id === shapeId);
  if (!node) fail(`shape '${shapeId}' is outside this scope`);
  const resize = assessBpmnDocumentResize(node.data.element, { ...bounds, isExpanded: node.data.notation.isExpanded, direction });
  if (!resize.allowed) fail(resize.reason);
  const next = new Map<string, BpmnBounds>([[shapeId, bounds]]);
  const nodeById = new Map(canvas.nodes.map(candidate => [candidate.id, candidate]));
  const laneChildren = (id: string) => canvas.nodes.filter(candidate => candidate.parentId === id && candidate.data.semanticType === "lane");
  const horizontal = (candidate: Node<BpmnDocumentNodeData>): boolean => candidate.data.notation.horizontal
    ?? (candidate.parentId ? horizontal(nodeById.get(candidate.parentId)!) : true);
  const partition = (parent: Node<BpmnDocumentNodeData>, previous: BpmnBounds, resized: BpmnBounds) => {
    for (const lane of laneChildren(parent.id)) {
      const old = lane.data.absoluteBounds;
      const laneBounds = horizontal(parent)
        ? { x: resized.x + old.x - previous.x, y: resized.y + (old.y - previous.y) * resized.height / previous.height,
          width: resized.width - (old.x - previous.x) - (previous.x + previous.width - old.x - old.width), height: old.height * resized.height / previous.height }
        : { x: resized.x + (old.x - previous.x) * resized.width / previous.width, y: resized.y + old.y - previous.y,
          width: old.width * resized.width / previous.width, height: resized.height - (old.y - previous.y) - (previous.y + previous.height - old.y - old.height) };
      finiteBounds(laneBounds); next.set(lane.id, laneBounds); partition(lane, old, laneBounds);
    }
  };
  if (node.data.semanticType === "lane" && node.parentId) {
    const old = node.data.absoluteBounds, parent = nodeById.get(node.parentId)!;
    const isHorizontal = horizontal(node), axis = isHorizontal ? "y" : "x", size = isHorizontal ? "height" : "width";
    if (isHorizontal ? old.x !== bounds.x || old.width !== bounds.width : old.y !== bounds.y || old.height !== bounds.height)
      fail("lane cross-axis span belongs to its container resize");
    const siblings = laneChildren(parent.id).sort((a, b) => a.data.absoluteBounds[axis] - b.data.absoluteBounds[axis]);
    const index = siblings.findIndex(sibling => sibling.id === shapeId), before = siblings[index - 1], after = siblings[index + 1];
    if (!before && bounds[axis] !== old[axis] || !after && bounds[axis] + bounds[size] !== old[axis] + old[size])
      fail("resize the owning container to move an outer lane boundary");
    if (before) {
      const previous = before.data.absoluteBounds, adjusted = { ...previous, [size]: bounds[axis] - previous[axis] };
      finiteBounds(adjusted); next.set(before.id, adjusted); partition(before, previous, adjusted);
    }
    if (after) {
      const previous = after.data.absoluteBounds, adjusted = { ...previous, [axis]: bounds[axis] + bounds[size], [size]: previous[axis] + previous[size] - bounds[axis] - bounds[size] };
      finiteBounds(adjusted); next.set(after.id, adjusted); partition(after, previous, adjusted);
    }
  }
  partition(node, node.data.absoluteBounds, bounds);
  for (const boundary of canvas.nodes.filter(candidate => candidate.parentId === shapeId && candidate.data.attachmentHostId)) {
    const point = dock(center(boundary.data.absoluteBounds), node.data.absoluteBounds, bounds);
    next.set(boundary.id, { ...boundary.data.absoluteBounds, x: point.x - boundary.data.absoluteBounds.width / 2, y: point.y - boundary.data.absoluteBounds.height / 2 });
  }
  return geometryCommands(canvas, next);
}
