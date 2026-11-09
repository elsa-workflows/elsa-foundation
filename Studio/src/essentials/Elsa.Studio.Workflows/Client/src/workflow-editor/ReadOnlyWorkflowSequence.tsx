import { useEffect, useRef } from "react";
import type { Node } from "@xyflow/react";
import { SequenceDesigner } from "../graph-authoring/SequenceDesigner";
import type { WorkflowNodeData } from "../workflowAdapter";

export function ReadOnlyWorkflowSequence({ nodes, onSelectActivity, onSelectEvidence, focusNodeId, focusRequestId }: {
  nodes: Node<WorkflowNodeData>[];
  onSelectActivity(nodeId: string): void;
  onSelectEvidence(evidenceId: string | null): void;
  focusNodeId: string | null;
  focusRequestId: number;
}) {
  const canvasRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!focusNodeId) return;
    const frame = requestAnimationFrame(() => {
      const step = [...(canvasRef.current?.querySelectorAll<HTMLElement>("[data-id]") ?? [])]
        .find(element => element.dataset.id === focusNodeId);
      step?.scrollIntoView?.({ block: "nearest", inline: "nearest", behavior: "auto" });
      step?.focus({ preventScroll: true });
    });
    return () => cancelAnimationFrame(frame);
  }, [focusNodeId, focusRequestId]);

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
