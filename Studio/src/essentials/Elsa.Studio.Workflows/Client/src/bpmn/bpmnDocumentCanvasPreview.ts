import type { BpmnDocumentCommand } from "./bpmnDocument";
import type { BpmnDocumentCanvas } from "./bpmnDocumentCanvas";

/** Geometry-only paint preview. No XML/native configuration is copied, mutated or made authoritative. */
export function previewBpmnDocumentGeometry(view: BpmnDocumentCanvas, commands: readonly BpmnDocumentCommand[]): BpmnDocumentCanvas {
  const bounds = new Map(view.nodes.map(node => [node.id, node.data.absoluteBounds]));
  const labels = new Map(commands.filter(command => command.kind === "labelBounds").map(command => [command.diId, command.bounds]));
  const routes = new Map(commands.filter(command => command.kind === "waypoints").map(command => [command.edgeId, command.points]));
  for (const command of commands) if (command.kind === "bounds") bounds.set(command.shapeId, command.bounds);
  return { ...view, nodes: view.nodes.map(node => {
    const absolute = bounds.get(node.id)!, parent = node.parentId ? bounds.get(node.parentId) : undefined;
    return { ...node, position: { x: absolute.x - (parent?.x ?? 0), y: absolute.y - (parent?.y ?? 0) },
      width: absolute.width, height: absolute.height, style: { ...node.style, width: absolute.width, height: absolute.height },
      data: { ...node.data, absoluteBounds: { ...absolute }, notation: { ...node.data.notation, width: absolute.width, height: absolute.height },
        labelBounds: labels.has(node.id) ? labels.get(node.id) ?? undefined : node.data.labelBounds } };
  }), edges: view.edges.map(edge => ({ ...edge, data: { ...edge.data!,
    points: routes.has(edge.id) ? routes.get(edge.id)!.map(point => ({ ...point })) : edge.data!.points,
    labelBounds: labels.has(edge.id) ? labels.get(edge.id) ?? undefined : edge.data!.labelBounds } })) };
}
