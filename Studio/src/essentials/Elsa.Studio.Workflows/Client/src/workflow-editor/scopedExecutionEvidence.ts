import { useEffect, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityExecutionStateSummary } from "../workflowTypes";
import { browseActivityExecutions } from "../api/runtime";
import { runtimeValueContextId } from "./RuntimeValueEvidence";

/** Resolve only visible nodes, with one bounded latest read per node. Never hydrate a run-wide history. */
export function useScopedCanvasActivities(context: StudioEndpointContext, workflowExecutionId: string, ancestorId: string | undefined,
  executableNodeIds: string[], refreshKey: number, selectedExecution?: ActivityExecutionStateSummary) {
  const nodeKey = JSON.stringify([...new Set(executableNodeIds)].sort());
  const selectedId = selectedExecution?.activityExecutionId;
  const selectedNode = selectedExecution?.executableNodeId;
  const key = `${runtimeValueContextId(context)}:${workflowExecutionId}:${ancestorId ?? ""}:${nodeKey}:${refreshKey}:${selectedId ?? ""}`;
  const [state, setState] = useState<{ key: string; activities: ActivityExecutionStateSummary[]; failed: boolean; loading: boolean }>({ key, activities: [], failed: false, loading: true });
  useEffect(() => {
    if (!ancestorId) return;
    const controller = new AbortController();
    setState({ key, activities: [], failed: false, loading: true });
    const nodes = JSON.parse(nodeKey) as string[];
    const activities: ActivityExecutionStateSummary[] = [];
    let failed = false;
    let index = 0;
    const worker = async () => {
      while (index < nodes.length && !controller.signal.aborted) {
        const node = nodes[index++];
        try {
          const page = await browseActivityExecutions(context, workflowExecutionId, { executableNodeId: node, ancestorActivityExecutionId: ancestorId,
            ...(node === selectedNode ? { anchorActivityExecutionId: selectedId } : {}), limit: 1 }, controller.signal);
          if (page.items[0]) activities.push(page.items[0].execution);
        } catch { failed = true; }
      }
    };
    void Promise.all(Array.from({ length: Math.min(4, nodes.length) }, worker)).then(() => {
      if (!controller.signal.aborted) setState({ key, activities, failed, loading: false });
    });
    return () => controller.abort();
  }, [ancestorId, context, key, nodeKey, selectedId, selectedNode, workflowExecutionId]);
  return state.key === key ? state : { key, activities: [], failed: false, loading: true };
}
