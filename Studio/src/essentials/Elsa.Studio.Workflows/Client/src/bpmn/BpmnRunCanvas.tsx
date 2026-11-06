import { useMemo } from "react";
import { Background, Controls, ReactFlow } from "@xyflow/react";
import type { BpmnInspection } from "../api/bpmnRuntime";
import type { ActivityNode } from "../workflowTypes";
import { buildBpmnCanvas } from "./bpmnAdapter";
import { bpmnElementTypeLabel, bpmnStructureKind, isEventElementType, isGatewayElementType, isTaskElementType, type BpmnElement } from "./bpmnTypes";
import type { BpmnNotationDescriptor } from "./bpmnNotation";
import { nodeTypes, edgeTypes } from "../workflow-editor/graph";
import { FitReadOnlyFlowView, useReadOnlyFlowNodes } from "../workflow-editor/useReadOnlyFlowCanvas";
import { workflowCanvasAriaLabelConfig } from "../workflow-editor/workflowAccessibility";
import { layoutFromBpmnDiagram } from "../api/bpmnInterchange";

type Bounds = { x: number; y: number; width: number; height: number };
function record(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : undefined;
}
function bounds(value: unknown): Bounds | undefined {
  const source = record(value);
  return source && [source.x, source.y, source.width, source.height].every(value => typeof value === "number" && Number.isFinite(value))
    && (source.width as number) > 0 && (source.height as number) > 0 ? source as Bounds : undefined;
}
function frozenBounds(inspection: BpmnInspection, diagram: unknown) {
  const result = new Map<string, Bounds>();
  for (const [id, value] of Object.entries(record(record(diagram)?.shapes) ?? {})) {
    const shape = bounds(value);
    if (shape) result.set(id, shape);
  }
  for (const item of inspection.source?.layout ?? []) {
    const previous = result.get(item.nodeId);
    const shape = bounds({ x: item.x, y: item.y, width: item.width ?? previous?.width, height: item.height ?? previous?.height });
    if (shape) result.set(item.nodeId, shape);
    else result.delete(item.nodeId);
  }
  return result;
}
function touchesHost(boundary: Bounds, host: Bounds) {
  return boundary.x <= host.x + host.width && boundary.x + boundary.width >= host.x
    && boundary.y <= host.y + host.height && boundary.y + boundary.height >= host.y
    && (boundary.x <= host.x || boundary.x + boundary.width >= host.x + host.width
      || boundary.y <= host.y || boundary.y + boundary.height >= host.y + host.height);
}
function frozenNotation(element: BpmnElement, shape: Bounds): BpmnNotationDescriptor {
  const loop = record(element.loopCharacteristics);
  return { type: element.elementType, name: element.name ?? undefined, width: shape.width, height: shape.height,
    eventDefinitions: element.eventDefinitions?.map(definition => definition.type),
    cancelActivity: typeof element.cancelActivity === "boolean" ? element.cancelActivity : undefined,
    interrupting: typeof element.isInterrupting === "boolean" ? element.isInterrupting : undefined,
    eventSubProcess: element.triggeredByEvent === true, transaction: element.isTransaction === true,
    loop: loop ? loop.isSequential ? "sequential" : "parallel" : undefined, compensation: element.isForCompensation === true };
}

/** Display the model-owned graph with the existing notation. This creates no native execution or authoring document. */
export function BpmnRunCanvas({ inspection, selectedElement, onSelect }: { inspection: BpmnInspection; selectedElement: string | null; onSelect(id: string): void }) {
  const graph = useMemo(() => {
    const process = inspection.process;
    const elements = process.elements;
    if (!Array.isArray(elements) || !Array.isArray(process.sequenceFlows) || elements.some(element => !element || typeof element !== "object"
      || typeof element.elementId !== "string" || !element.elementId || typeof element.elementType !== "string"
      || !(isEventElementType(element.elementType) || isGatewayElementType(element.elementType) || isTaskElementType(element.elementType) || element.elementType === "subProcess"))
      || new Set(elements.map(element => element.elementId)).size !== elements.length) {
      return { nodes: [], edges: [], unavailable: true };
    }
    const diagram = inspection.source?.layout.find(item => item.nodeId.startsWith("bpmn-diagram:"))?.additionalProperties;
    const geometry = frozenBounds(inspection, diagram);
    const elementsById = new Map((elements as BpmnElement[]).map(element => [element.elementId, element]));
    const attachedShapes = new Set<string>();
    // This viewer needs the frozen attachment context, independently of authoring type support.
    for (const element of elements as BpmnElement[]) {
      if (element.elementType !== "boundaryEvent") continue;
      const host = typeof element.attachedToRef === "string" ? elementsById.get(element.attachedToRef) : undefined;
      const shape = geometry.get(element.elementId), hostShape = host && geometry.get(host.elementId);
      if (!host || !(isTaskElementType(host.elementType) || host.elementType === "subProcess") || !shape || !hostShape || !touchesHost(shape, hostShape))
        return { nodes: [], edges: [], unavailable: true };
      attachedShapes.add(element.elementId); attachedShapes.add(host.elementId);
    }
    const owner: ActivityNode = { nodeId: String(process.processId ?? "process"), activityVersionId: "", inputs: [], outputs: [],
      structure: { kind: bpmnStructureKind, schemaVersion: "1.0.0", payload: { ...process, activities: [], diagram } } };
    const layout = [...layoutFromBpmnDiagram(owner), ...(inspection.source?.layout.map(item => ({ nodeId: item.nodeId, x: item.x, y: item.y })) ?? [])];
    const canvas = buildBpmnCanvas({ owner, slot: { id: "Bpmn.Activities", label: "Process", property: "activities", cardinality: "many", mode: "bpmn", activities: [] } }, [], layout);
    return { ...canvas, unavailable: false, nodes: canvas.nodes.map(node => {
      const work = inspection.work.find(item => item.elementId === node.id);
      const attachedShape = attachedShapes.has(node.id) ? geometry.get(node.id) : undefined;
      return { ...node, draggable: false, connectable: false, deletable: false, selected: node.id === selectedElement,
        ...(attachedShape ? { position: { x: attachedShape.x, y: attachedShape.y } } : {}),
        className: work ? "wf-bpmn-current-work" : undefined,
        data: { ...node.data, ...(attachedShape ? { notation: frozenNotation(node.data.element, attachedShape) } : {}),
          bindingLabel: work?.kind === "Human" ? "Human wait" : work?.kind === "Timer" ? "Timer wait" : work?.kind === "Native" ? "Native work" : bpmnElementTypeLabel(node.data.element) } };
    }) };
  }, [inspection, selectedElement]);
  const { flowNodes, onNodesChange } = useReadOnlyFlowNodes(graph.nodes);
  if (graph.unavailable) return <p role="note">This process uses notation the diagram cannot display. Its current work remains available below.</p>;
  return <div className="wf-bpmn-run-canvas" aria-label="Published BPMN process diagram">
    <ReactFlow nodes={flowNodes} edges={graph.edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes} onNodesChange={changes => {
      for (const change of changes) if (change.type === "select" && change.selected) onSelect(change.id);
      onNodesChange(changes);
    }}
      nodesDraggable={false} nodesConnectable={false} edgesReconnectable={false} deleteKeyCode={null}
      onNodeClick={(_, node) => onSelect(node.id)} onNodeDragStart={undefined} minZoom={0.2} maxZoom={1.8}
      ariaLabelConfig={workflowCanvasAriaLabelConfig}>
      <Background gap={18} size={1} /><Controls showInteractive={false} /><FitReadOnlyFlowView nodeCount={flowNodes.length} />
    </ReactFlow>
  </div>;
}
