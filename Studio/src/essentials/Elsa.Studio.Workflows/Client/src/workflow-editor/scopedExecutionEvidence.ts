import { useEffect, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityExecutionStateSummary } from "../workflowTypes";
import { browseActivityExecutions, type ActivityExecutionBrowseEntry } from "../api/runtime";
import { runtimeValueContextId } from "./RuntimeValueEvidence";

/** One bounded exact read per visible node, with at most four requests in flight. */
export async function readVisibleActivityExecutions(context: StudioEndpointContext, workflowExecutionId: string, executableNodeIds: string[],
  ancestorId: string | undefined, selectedExecution: ActivityExecutionStateSummary | undefined, signal: AbortSignal) {
  const nodes = [...new Set(executableNodeIds)];
  const activities: ActivityExecutionStateSummary[] = [];
  const observations: ActivityExecutionBrowseEntry[] = [];
  let repeated = false;
  let failed = false;
  let index = 0;
  const worker = async () => {
    while (index < nodes.length && !signal.aborted) {
      const node = nodes[index++];
      const exact = selectedExecution?.workflowExecutionId === workflowExecutionId && selectedExecution.executableNodeId === node ? selectedExecution.activityExecutionId : undefined;
      try {
        const page = await browseActivityExecutions(context, workflowExecutionId, { executableNodeId: node,
          ...(ancestorId ? { ancestorActivityExecutionId: ancestorId } : {}), ...(exact ? { anchorActivityExecutionId: exact } : {}), limit: 1 }, signal);
        const execution = page.items[0]?.execution;
        if (page.totalCount > 1 && !exact) repeated = true;
        if ((page.totalCount === 1 || exact && execution?.activityExecutionId === exact)
          && execution?.workflowExecutionId === workflowExecutionId && execution.executableNodeId === node) {
          activities.push(execution); observations.push(page.items[0]);
        }
      } catch { failed = true; }
    }
  };
  await Promise.all(Array.from({ length: Math.min(4, nodes.length) }, worker));
  return { activities, observations, repeated, failed };
}

/** Refreshes a visible scope without replacing the pinned graph, selected occurrence or confirmed batch. */
export function useScopedCanvasActivities(context: StudioEndpointContext, workflowExecutionId: string, ancestorId: string | undefined,
  executableNodeIds: string[], refreshKey: number, selectedExecution?: ActivityExecutionStateSummary, includeRoot = false) {
  const nodeKey = JSON.stringify([...new Set(executableNodeIds)].sort());
  const selectedId = selectedExecution?.activityExecutionId;
  const selectedNode = selectedExecution?.executableNodeId;
  const key = JSON.stringify([runtimeValueContextId(context), workflowExecutionId, ancestorId, nodeKey, selectedId]);
  const [state, setState] = useState({ key, activities: [] as ActivityExecutionStateSummary[], observations: [] as ActivityExecutionBrowseEntry[], confirmedRevision: null as number | null, repeated: false, failed: false, loading: true });
  const requested = useRef(refreshKey); requested.current = refreshKey;
  const pump = useRef<(() => void) | null>(null);
  useEffect(() => {
    if (!ancestorId && !includeRoot) return;
    const controller = new AbortController();
    let running = false;
    let confirmed: number | null = null;
    setState({ key, activities: [], observations: [], confirmedRevision: null, repeated: false, failed: false, loading: true });
    const load = async () => {
      if (running || controller.signal.aborted || confirmed === requested.current) return;
      running = true;
      try {
        do {
          const revision = requested.current;
          const result = await readVisibleActivityExecutions(context, workflowExecutionId, JSON.parse(nodeKey) as string[], ancestorId, selectedExecution, controller.signal);
          if (controller.signal.aborted) return;
          confirmed = revision;
          setState({ key, ...result, loading: false, confirmedRevision: revision });
        } while (confirmed !== requested.current);
      } finally { running = false; }
    };
    const start = () => { void load(); }; pump.current = start; start();
    return () => { controller.abort(); if (pump.current === start) pump.current = null; };
    // Scope primitives identify a selected occurrence; a refresh is coalesced instead of restarting it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ancestorId, context, includeRoot, key, nodeKey, selectedId, selectedNode, workflowExecutionId]);
  useEffect(() => { pump.current?.(); }, [refreshKey]);
  return state.key === key ? state : { key, activities: [], observations: [], confirmedRevision: null, repeated: false, failed: false, loading: true };
}
