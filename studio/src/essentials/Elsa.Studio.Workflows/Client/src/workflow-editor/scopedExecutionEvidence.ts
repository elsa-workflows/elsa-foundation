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
  const repeatedNodes: string[] = [];
  let repeated = false;
  let failed = false;
  let denied = false;
  let index = 0;
  const worker = async () => {
    while (index < nodes.length && !signal.aborted) {
      const node = nodes[index++];
      const exact = selectedExecution?.workflowExecutionId === workflowExecutionId && selectedExecution.executableNodeId === node ? selectedExecution.activityExecutionId : undefined;
      try {
        const page = await browseActivityExecutions(context, workflowExecutionId, { executableNodeId: node,
          ...(ancestorId ? { ancestorActivityExecutionId: ancestorId } : {}), ...(exact ? { anchorActivityExecutionId: exact } : {}), limit: 1 }, signal);
        const execution = page.items[0]?.execution;
        if (page.totalCount > 1) repeatedNodes.push(node);
        if (page.totalCount > 1 && !exact) repeated = true;
        if ((page.totalCount === 1 || exact && execution?.activityExecutionId === exact)
          && execution?.workflowExecutionId === workflowExecutionId && execution.executableNodeId === node) {
          activities.push(execution); observations.push(page.items[0]);
        }
      } catch (error) {
        failed = true;
        const status = typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0;
        if (status === 401 || status === 403 || status === 404) denied = true;
      }
    }
  };
  await Promise.all(Array.from({ length: Math.min(4, nodes.length) }, worker));
  return { activities, observations, repeated, repeatedNodes, failed, denied };
}

interface ScopedCanvasActivitiesState {
  key: string;
  activities: ActivityExecutionStateSummary[];
  observations: ActivityExecutionBrowseEntry[];
  confirmedRevision: number | null;
  repeated: boolean;
  repeatedNodes: string[];
  anchoredRepeatedId: string | undefined;
  lastAttemptedSelectedId: string | undefined;
  failed: boolean;
  loading: boolean;
}

function emptyScopedState(key: string): ScopedCanvasActivitiesState {
  return {
    key,
    activities: [],
    observations: [],
    confirmedRevision: null,
    repeated: false,
    repeatedNodes: [],
    anchoredRepeatedId: undefined,
    lastAttemptedSelectedId: undefined,
    failed: false,
    loading: true
  };
}

function needsSelectionRefresh(
  snapshot: Pick<ScopedCanvasActivitiesState, "activities" | "repeatedNodes" | "anchoredRepeatedId" | "lastAttemptedSelectedId" | "failed" | "loading">,
  candidate: ActivityExecutionStateSummary | undefined
) {
  if (snapshot.loading || snapshot.failed) return false;
  const candidateId = candidate?.activityExecutionId;
  const candidateNode = candidate?.executableNodeId;
  if (candidateId === snapshot.lastAttemptedSelectedId) return false;
  if (snapshot.anchoredRepeatedId && candidateId !== snapshot.anchoredRepeatedId) return true;
  if (!candidateId || !candidateNode) return false;
  if (snapshot.repeatedNodes.includes(candidateNode)) return candidateId !== snapshot.anchoredRepeatedId;
  return !snapshot.activities.some(item => item.executableNodeId === candidateNode && item.activityExecutionId === candidateId);
}

/** Refreshes a visible scope without replacing the pinned graph, selected occurrence or confirmed batch. */
export function useScopedCanvasActivities(context: StudioEndpointContext, workflowExecutionId: string, ancestorId: string | undefined,
  executableNodeIds: string[], refreshKey: number, selectedExecution?: ActivityExecutionStateSummary, includeRoot = false) {
  const nodes = [...new Set(executableNodeIds)].sort();
  const nodeKey = JSON.stringify(nodes);
  const scopeKey = JSON.stringify([runtimeValueContextId(context), workflowExecutionId, ancestorId, nodeKey]);
  const selectedForScope = selectedExecution?.workflowExecutionId === workflowExecutionId && nodes.includes(selectedExecution.executableNodeId)
    ? selectedExecution : undefined;
  const selectedId = selectedForScope?.activityExecutionId;
  const selectedNode = selectedForScope?.executableNodeId;
  const [state, setState] = useState<ScopedCanvasActivitiesState>(() => emptyScopedState(scopeKey));
  const requested = useRef(refreshKey); requested.current = refreshKey;
  const selectedRef = useRef(selectedForScope); selectedRef.current = selectedForScope;
  const pump = useRef<(() => void) | null>(null);
  useEffect(() => {
    if (!ancestorId && !includeRoot) return;
    const controller = new AbortController();
    let running = false;
    let confirmed: number | null = null;
    let latest: ScopedCanvasActivitiesState | null = null;
    setState(emptyScopedState(scopeKey));
    const load = async (initial = false) => {
      if (running || controller.signal.aborted) return;
      if (!initial && confirmed === requested.current && (!latest || !needsSelectionRefresh(latest, selectedRef.current))) return;
      running = true;
      try {
        do {
          const revision = requested.current;
          const usedSelected = selectedRef.current;
          const usedSelectedId = usedSelected?.activityExecutionId;
          const result = await readVisibleActivityExecutions(context, workflowExecutionId, JSON.parse(nodeKey) as string[], ancestorId, usedSelected, controller.signal);
          if (controller.signal.aborted) return;
          confirmed = revision;
          const nextState: ScopedCanvasActivitiesState = {
            key: scopeKey,
            activities: result.activities,
            observations: result.observations,
            repeated: result.repeated,
            repeatedNodes: result.repeatedNodes,
            anchoredRepeatedId: usedSelected && result.repeatedNodes.includes(usedSelected.executableNodeId) ? usedSelected.activityExecutionId : undefined,
            lastAttemptedSelectedId: usedSelectedId,
            failed: result.failed,
            loading: false,
            confirmedRevision: revision
          };
          latest = nextState;
          setState(nextState);
          if (confirmed === requested.current && (usedSelectedId === selectedRef.current?.activityExecutionId || !needsSelectionRefresh(nextState, selectedRef.current))) break;
        } while (!controller.signal.aborted);
      } finally { running = false; }
    };
    const start = () => { void load(); }; pump.current = start; void load(true);
    return () => { controller.abort(); if (pump.current === start) pump.current = null; };
    // Scope primitives identify a visible scope; refreshes and repeated-occurrence anchors are coalesced without wiping the canvas.
  }, [ancestorId, context, includeRoot, nodeKey, scopeKey, workflowExecutionId]);
  useEffect(() => { pump.current?.(); }, [refreshKey, selectedId, selectedNode]);
  const current = state.key === scopeKey ? state : emptyScopedState(scopeKey);
  return { ...current, anchoring: !current.loading && needsSelectionRefresh(current, selectedForScope) };
}
