import type { Edge, Node } from "@xyflow/react";
import type { KeyboardEvent } from "react";
import type { WorkflowNodeData } from "../workflowAdapter";

/** Activate the domain selection as well as React Flow's focusable wrapper selection. */
export function activateWorkflowCanvasNode(event: KeyboardEvent<HTMLElement>, activate: (nodeId: string) => void) {
  if (event.defaultPrevented || event.repeat || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey
    || event.key !== "Enter" && event.key !== " ") return;
  const target = event.target;
  // Buttons, inputs and slot controls inside a node keep their own keyboard behavior.
  if (!(target instanceof HTMLElement) || !target.classList.contains("react-flow__node")) return;
  const nodeId = target.getAttribute("data-id");
  if (!nodeId) return;
  event.preventDefault();
  event.stopPropagation();
  activate(nodeId);
}

export const workflowCanvasAriaLabelConfig = {
  "node.a11yDescription.default": "Press Enter or Space to select and inspect this activity. Press Tab to move between canvas items. Arrow keys move a selected activity. Press Delete to remove it and Escape to cancel.",
  "node.a11yDescription.keyboardDisabled": "Press Enter or Space to select and inspect this activity. Press Tab to move between canvas items. Arrow keys move a selected activity. Press Delete to remove it and Escape to cancel.",
  "edge.a11yDescription.default": "Press Enter or Space to select this connection. Press Tab to reach its insert and delete controls, or press Delete to remove it. Press Escape to cancel."
};

// React Flow owns the focusable wrappers around custom nodes and edges. Enrich those wrappers here so
// every canvas projection (including nodes produced during an in-place mutation) exposes the same
// domain language and current selection state to assistive technology.
export function decorateWorkflowCanvasElements<TNode extends Node<WorkflowNodeData>, TEdge extends Edge>(nodes: TNode[], edges: TEdge[]) {
  const labels = new Map(nodes.map(node => [node.id, node.data.label]));
  return {
    nodes: nodes.map(node => ({
      ...node,
      ariaRole: "button" as const,
      ariaLabel: describeNode(node),
      domAttributes: {
        ...node.domAttributes,
        "aria-pressed": !!node.selected
      }
    })),
    edges: edges.map(edge => ({
      ...edge,
      focusable: true,
      ariaRole: "button" as const,
      ariaLabel: describeConnection(edge, labels),
      domAttributes: {
        ...edge.domAttributes,
        "aria-pressed": !!edge.selected
      }
    }))
  };
}

function describeNode(node: Node<WorkflowNodeData>) {
  const data = node.data;
  const parts = [
    data.label,
    `Node ID: ${node.id}`,
    `Activity type: ${data.activityTypeKey ?? data.activityVersionId ?? "unknown"}`,
    data.category ? `Category: ${data.category}` : null,
    data.executionType ? `Execution type: ${data.executionType}` : null,
    describeNodeState(data),
    node.selected ? "Selected" : "Not selected"
  ];
  return `${parts.filter((part): part is string => !!part).join(". ")}.`;
}

function describeNodeState(data: WorkflowNodeData) {
  if (data.runtime?.status) return `Run state: ${data.runtime.status}`;
  if (data.ghost) return "State: unavailable in this environment";
  return "State: authoring";
}

function describeConnection(edge: Edge, labels: Map<string, string>) {
  const source = labels.get(edge.source) ?? edge.source;
  const target = labels.get(edge.target) ?? edge.target;
  const output = edge.sourceHandle || "Done";
  return `Connection from ${source} (${edge.source}), ${output} output, to ${target} (${edge.target}). ${edge.selected ? "Selected" : "Not selected"}.`;
}
