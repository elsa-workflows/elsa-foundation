import { useCallback, useState } from "react";

export type WorkflowInspectorView = "configuration" | "run-data";

export function useWorkflowInspectorView(
  select: (nodeId: string | null) => void,
  selectNodes: (nodeIds: string[], primaryNodeId?: string | null) => void
) {
  const [inspectorView, setInspectorView] = useState<WorkflowInspectorView>("configuration");
  const showRunData = useCallback(() => setInspectorView("run-data"), []);
  const selectEditorNode = useCallback((nodeId: string | null) => select(nodeId), [select]);
  const selectEditorNodes = useCallback((nodeIds: string[], primaryNodeId?: string | null) => selectNodes(nodeIds, primaryNodeId), [selectNodes]);

  return { inspectorView, setInspectorView, showRunData, selectEditorNode, selectEditorNodes };
}