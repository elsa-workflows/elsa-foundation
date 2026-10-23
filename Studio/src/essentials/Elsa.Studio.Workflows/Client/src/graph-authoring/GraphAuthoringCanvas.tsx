import type { HTMLAttributes, ReactNode, RefObject } from "react";
import { Background, Controls, ReactFlow, type Node, type ReactFlowProps } from "@xyflow/react";
import { GraphOverflowMiniMap } from "./GraphOverflowMiniMap";
import type { WorkflowEdge, WorkflowNodeAvailabilityLookup } from "../workflow-editor/editorTypes";
import type { WorkflowNodeData } from "../workflowAdapter";
import type { WorkflowSlotNavigation } from "../workflow-editor/contexts";
import {
  WorkflowEdgeActionsContext,
  WorkflowNodeActionsContext,
  WorkflowNodeAvailabilityContext,
  WorkflowSlotNavigationContext
} from "../workflow-editor/contexts";
import type { WorkflowEdgeActions, WorkflowNodeActions } from "../workflow-editor/contexts";
import { edgeTypes, nodeTypes } from "../workflow-editor/graph";
import { workflowCanvasAriaLabelConfig } from "../workflow-editor/workflowAccessibility";

export interface GraphAuthoringCanvasProps {
  canvasRef?: RefObject<HTMLDivElement | null>;
  canvasProps?: Omit<HTMLAttributes<HTMLDivElement>, "children">;
  reactFlowProps: ReactFlowProps<Node<WorkflowNodeData>, WorkflowEdge>;
  edgeActions?: WorkflowEdgeActions | null;
  nodeActions?: WorkflowNodeActions | null;
  availabilityLookup?: WorkflowNodeAvailabilityLookup | null;
  slotNavigation?: WorkflowSlotNavigation | null;
  overlays?: ReactNode;
}

/**
 * Shared controlled canvas surface. Resource hosts own document state and persistence while this
 * component owns the common React Flow primitives, contexts, accessibility, and viewport chrome.
 */
export function GraphAuthoringCanvas({
  canvasRef,
  canvasProps,
  reactFlowProps,
  edgeActions = null,
  nodeActions = null,
  availabilityLookup = null,
  slotNavigation = null,
  overlays
}: GraphAuthoringCanvasProps) {
  const className = ["wf-canvas", canvasProps?.className].filter(Boolean).join(" ");
  return (
    <div {...canvasProps} className={className} ref={canvasRef}>
      <WorkflowEdgeActionsContext.Provider value={edgeActions}>
        <WorkflowNodeActionsContext.Provider value={nodeActions}>
          <WorkflowNodeAvailabilityContext.Provider value={availabilityLookup}>
            <WorkflowSlotNavigationContext.Provider value={slotNavigation}>
              <ReactFlow<Node<WorkflowNodeData>, WorkflowEdge>
                nodeTypes={nodeTypes}
                edgeTypes={edgeTypes}
                minZoom={0.2}
                maxZoom={1.8}
                nodeDragThreshold={5}
                selectionOnDrag
                multiSelectionKeyCode={["Shift", "Meta", "Control"]}
                panActivationKeyCode={null}
                ariaLabelConfig={workflowCanvasAriaLabelConfig}
                defaultEdgeOptions={{ type: "workflow" }}
                {...reactFlowProps}
              >
                <Background gap={18} size={1} />
                <Controls />
                <GraphOverflowMiniMap />
              </ReactFlow>
            </WorkflowSlotNavigationContext.Provider>
          </WorkflowNodeAvailabilityContext.Provider>
        </WorkflowNodeActionsContext.Provider>
      </WorkflowEdgeActionsContext.Provider>
      {overlays}
    </div>
  );
}
