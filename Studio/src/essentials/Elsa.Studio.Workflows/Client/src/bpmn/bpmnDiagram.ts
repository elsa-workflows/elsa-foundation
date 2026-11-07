import type { BpmnElement, BpmnSequenceFlow } from "./bpmnTypes";

type Point = { x: number; y: number };
type Bounds = Point & { width: number; height: number };
type RecordValue = Record<string, unknown>;

function record(value: unknown): RecordValue | null {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as RecordValue : null;
}
function finite(value: unknown): value is number { return typeof value === "number" && Number.isFinite(value); }
function point(value: unknown): value is Point {
  const source = record(value);
  return !!source && finite(source.x) && finite(source.y);
}
function bounds(value: unknown): value is Bounds {
  const source = record(value);
  return point(value) && !!source && finite(source.width) && source.width > 0 && finite(source.height) && source.height > 0;
}
function shifted(value: unknown, delta: Point): unknown {
  return point(value) ? { ...value, x: value.x + delta.x, y: value.y + delta.y } : value;
}
function defaultSize(element: BpmnElement): [number, number] {
  if (/Event$/.test(element.elementType)) return [36, 36];
  if (/Gateway$/.test(element.elementType)) return [50, 50];
  return [100, 80];
}
function route(source: Bounds, target: Bounds): Point[] {
  const sourceCenter = { x: source.x + source.width / 2, y: source.y + source.height / 2 };
  const targetCenter = { x: target.x + target.width / 2, y: target.y + target.height / 2 };
  if (source.x === target.x && source.y === target.y && source.width === target.width && source.height === target.height) {
    return [{ x: source.x + source.width, y: sourceCenter.y }, { x: source.x + source.width + 40, y: sourceCenter.y },
      { x: source.x + source.width + 40, y: source.y + source.height + 40 },
      { x: sourceCenter.x, y: source.y + source.height + 40 }, { x: sourceCenter.x, y: source.y + source.height }];
  }
  const horizontal = Math.abs(targetCenter.x - sourceCenter.x) >= Math.abs(targetCenter.y - sourceCenter.y);
  if (horizontal) {
    const forward = targetCenter.x >= sourceCenter.x;
    const first = { x: forward ? source.x + source.width : source.x, y: sourceCenter.y };
    const last = { x: forward ? target.x : target.x + target.width, y: targetCenter.y };
    if (first.y === last.y) return [first, last];
    const middle = (first.x + last.x) / 2;
    return [first, { x: middle, y: first.y }, { x: middle, y: last.y }, last];
  }
  const forward = targetCenter.y >= sourceCenter.y;
  const first = { x: sourceCenter.x, y: forward ? source.y + source.height : source.y };
  const last = { x: targetCenter.x, y: forward ? target.y : target.y + target.height };
  if (first.x === last.x) return [first, last];
  const middle = (first.y + last.y) / 2;
  return [first, { x: first.x, y: middle }, { x: last.x, y: middle }, last];
}
function midpoint(points: Point[]): Point {
  const lengths = points.slice(1).map((end, index) => Math.hypot(end.x - points[index].x, end.y - points[index].y));
  let remaining = lengths.reduce((sum, length) => sum + length, 0) / 2;
  for (const [index, length] of lengths.entries()) {
    if (remaining <= length && length > 0) {
      const fraction = remaining / length;
      return { x: points[index].x + (points[index + 1].x - points[index].x) * fraction,
        y: points[index].y + (points[index + 1].y - points[index].y) * fraction };
    }
    remaining -= length;
  }
  return points[0];
}
function equalDelta(left: Point, right: Point) { return left.x === right.x && left.y === right.y; }
const zero = { x: 0, y: 0 };

/**
 * Reconcile existing V1 DI with an integrated-canvas transaction. Untouched geometry is exact;
 * moving both ends together translates the route; independent moves/reconnects redock that edge.
 * Schema-2 documents own geometry in XML and use their document commands instead.
 */
export function reconcileBpmnDiagram(
  diagram: unknown,
  previousElements: readonly BpmnElement[],
  previousFlows: readonly BpmnSequenceFlow[],
  elements: readonly BpmnElement[],
  flows: readonly BpmnSequenceFlow[],
  positions: ReadonlyMap<string, Point>
): unknown {
  const root = record(diagram);
  if (!root) return diagram;
  const previousShapes = record(root.shapes);
  const previousEdges = record(root.edges);
  if (!previousShapes || !previousEdges) return diagram; // Malformed data remains visible to validation.
  const shapes: RecordValue = { ...previousShapes };
  const edges: RecordValue = { ...previousEdges };
  const nextElementIds = new Set(elements.map(element => element.elementId));
  const nextFlowIds = new Set(flows.map(flow => flow.flowId));
  for (const element of previousElements) if (!nextElementIds.has(element.elementId)) delete shapes[element.elementId];
  for (const flow of previousFlows) if (!nextFlowIds.has(flow.flowId)) delete edges[flow.flowId];
  const deltas = new Map<string, Point>();
  const materializedShapes = new Set<string>();
  for (const element of elements) {
    const position = positions.get(element.elementId);
    if (!point(position)) continue;
    const previous = previousShapes[element.elementId];
    if (previous === undefined) {
      const size = defaultSize(element);
      shapes[element.elementId] = { ...position, width: size[0], height: size[1] };
      materializedShapes.add(element.elementId);
      continue;
    }
    if (!bounds(previous)) continue;
    const delta = { x: position.x - previous.x, y: position.y - previous.y };
    deltas.set(element.elementId, delta);
    if (equalDelta(delta, zero)) continue;
    const metadata = record(previous)!;
    shapes[element.elementId] = { ...metadata, ...position, ...(metadata.label !== undefined ? { label: shifted(metadata.label, delta) } : {}) };
  }
  const previousFlowsById = new Map(previousFlows.map(flow => [flow.flowId, flow]));
  for (const flow of flows) {
    const previous = previousFlowsById.get(flow.flowId);
    const edge = record(previousEdges[flow.flowId]);
    const changedEndpoints = !previous || previous.sourceRef !== flow.sourceRef || previous.targetRef !== flow.targetRef;
    const needsDocking = materializedShapes.has(flow.sourceRef) || materializedShapes.has(flow.targetRef);
    const sourceDelta = deltas.get(flow.sourceRef) ?? zero;
    const targetDelta = deltas.get(flow.targetRef) ?? zero;
    if (!changedEndpoints && !needsDocking && equalDelta(sourceDelta, zero) && equalDelta(targetDelta, zero)) continue;
    const oldPoints = edge?.waypoints;
    const validPoints = Array.isArray(oldPoints) && oldPoints.length >= 2 && oldPoints.every(point) ? oldPoints : null;
    if (edge && !validPoints) continue; // Do not silently repair/discard invalid authored geometry.
    let points: Point[];
    if (!changedEndpoints && !needsDocking && validPoints && equalDelta(sourceDelta, targetDelta)) {
      points = validPoints.map(value => shifted(value, sourceDelta) as Point);
    } else {
      const source = shapes[flow.sourceRef];
      const target = shapes[flow.targetRef];
      if (!bounds(source) || !bounds(target)) continue;
      points = route(source, target);
      if (validPoints && previous?.sourceRef === flow.sourceRef) points[0] = { ...validPoints[0], ...points[0] };
      if (validPoints && previous?.targetRef === flow.targetRef) points[points.length - 1] = { ...validPoints[validPoints.length - 1], ...points[points.length - 1] };
    }
    const oldMiddle = validPoints ? midpoint(validPoints) : null;
    const nextMiddle = midpoint(points);
    const labelDelta = oldMiddle ? { x: nextMiddle.x - oldMiddle.x, y: nextMiddle.y - oldMiddle.y } : zero;
    edges[flow.flowId] = { ...edge, waypoints: points,
      ...(edge?.label !== undefined ? { label: shifted(edge.label, labelDelta) } : {}) };
  }
  return { ...root, shapes, edges };
}
