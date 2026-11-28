import { useEffect, useRef } from "react";
import type { Node } from "@xyflow/react";
import { SequenceDesigner } from "../graph-authoring/SequenceDesigner";
import type { WorkflowNodeData } from "../workflowAdapter";
import type { RunCanvasFocusMode } from "./runCanvasFocus";

export function ReadOnlyWorkflowSequence({ nodes, onSelectActivity, onSelectEvidence, focusNodeId, focusRequestId, moveKeyboardFocus = true, focusMode = "pan" }: {
  nodes: Node<WorkflowNodeData>[];
  onSelectActivity(nodeId: string): void;
  onSelectEvidence(evidenceId: string | null): void;
  focusNodeId: string | null;
  focusRequestId: number;
  moveKeyboardFocus?: boolean;
  focusMode?: RunCanvasFocusMode;
}) {
  const canvasRef = useRef<HTMLDivElement>(null);
  const focusModeRef = useRef(focusMode);
  focusModeRef.current = focusMode;

  useEffect(() => {
    if (!focusNodeId) return;
    const frame = requestAnimationFrame(() => {
      const step = [...(canvasRef.current?.querySelectorAll<HTMLElement>("[data-id]") ?? [])]
        .find(element => element.dataset.id === focusNodeId);
      if (focusModeRef.current !== "off") {
        step?.scrollIntoView?.({ block: moveKeyboardFocus ? "nearest" : "center", inline: "nearest", behavior: "auto" });
      }
      if (moveKeyboardFocus) step?.focus({ preventScroll: true });
    });
    return () => cancelAnimationFrame(frame);
  }, [focusNodeId, focusRequestId, moveKeyboardFocus]);

  return <SequenceDesigner
    nodes={nodes}
    selectedNodeId={nodes.find(node => node.selected)?.id ?? null}
    canvasRef={canvasRef}
    readOnly
    onSelect={nodeId => {
      if (nodeId === null) {
        onSelectEvidence(null);
        return;
      }
      const node = nodes.find(candidate => candidate.id === nodeId);
      if (node) onSelectActivity(node.data.runtimeNodeId ?? node.id);
    }}
    onOpenSlot={(nodeId, _label, slot) => nodes.find(node => node.id === nodeId)?.data.onEnterSlot?.(slot)}
  />;
}
