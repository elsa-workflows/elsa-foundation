import { useMemo } from "react";
import { Background, Controls, ReactFlow } from "@xyflow/react";
import type { BpmnInspection } from "../api/bpmnRuntime";
import type { ActivityNode } from "../workflowTypes";
import { buildBpmnCanvas } from "./bpmnAdapter";
import { bpmnElementTypeLabel, bpmnStructureKind, isEventElementType, isGatewayElementType, isTaskElementType } from "./bpmnTypes";
import { nodeTypes, edgeTypes } from "../workflow-editor/graph";
import { FitReadOnlyFlowView, useReadOnlyFlowNodes } from "../workflow-editor/useReadOnlyFlowCanvas";
import { workflowCanvasAriaLabelConfig } from "../workflow-editor/workflowAccessibility";
import { layoutFromBpmnDiagram } from "../api/bpmnInterchange";

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
    const owner: ActivityNode = { nodeId: String(process.processId ?? "process"), activityVersionId: "", inputs: [], outputs: [],
      structure: { kind: bpmnStructureKind, schemaVersion: "1.0.0", payload: { ...process, activities: [], diagram: inspection.source?.layout.find(item => item.nodeId.startsWith("bpmn-diagram:"))?.additionalProperties } } };
    const layout = [...layoutFromBpmnDiagram(owner), ...(inspection.source?.layout.map(item => ({ nodeId: item.nodeId, x: item.x, y: item.y })) ?? [])];
    const canvas = buildBpmnCanvas({ owner, slot: { id: "Bpmn.Activities", label: "Process", property: "activities", cardinality: "many", mode: "bpmn", activities: [] } }, [], layout);
    return { ...canvas, unavailable: false, nodes: canvas.nodes.map(node => {
      const work = inspection.work.find(item => item.elementId === node.id);
      return { ...node, draggable: false, connectable: false, deletable: false, selected: node.id === selectedElement,
        className: work ? "wf-bpmn-current-work" : undefined,
        data: { ...node.data, bindingLabel: work?.kind === "Human" ? "Human wait" : work?.kind === "Timer" ? "Timer wait" : work?.kind === "Native" ? "Native work" : bpmnElementTypeLabel(node.data.element) } };
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
