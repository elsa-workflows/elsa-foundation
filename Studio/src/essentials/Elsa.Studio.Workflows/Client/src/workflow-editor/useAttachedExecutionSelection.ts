import { useCallback, useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { supportsActivityExecutionBrowsing } from "../api/runtime";
import type { ExecutableActivityGraph } from "../executableGraph";
import type { ActivityExecutionStateSummary } from "../workflowTypes";
import type { ExecutionScopeEntry } from "./executionScope";
import { runtimeValueContextId } from "./RuntimeValueEvidence";

interface SelectionState {
  key: string;
  nodeId: string | null;
  execution: ActivityExecutionStateSummary | null;
  scopes: ExecutionScopeEntry[];
  loading: boolean;
  error: string;
}

/** Exact selection survives inspector tabs; obsolete or unauthorized evidence never does. */
export function useAttachedExecutionSelection({ context, attachmentId, workflowExecutionId, enabled, nodeId }: {
  context: StudioEndpointContext; attachmentId: string; workflowExecutionId: string; enabled: boolean; nodeId: string | null;
}) {
  const key = `${runtimeValueContextId(context)}:${workflowExecutionId}:${attachmentId}`;
  const [state, setState] = useState<SelectionState | null>(null);
  const [authorized, setAuthorized] = useState(true);
  const [pending, setPending] = useState<{
    key: string; nodeId: string; execution: ActivityExecutionStateSummary; graph: ExecutableActivityGraph;
    labelFor(execution: ActivityExecutionStateSummary): string;
  } | null>(null);
  const request = useRef<AbortController | null>(null);
  const currentKey = useRef(key); currentKey.current = key;
  const currentNode = useRef(nodeId); currentNode.current = nodeId;
  const active = useRef(enabled && authorized); active.current = enabled && authorized;
  useEffect(() => {
    const revoke = () => { request.current?.abort(); request.current = null; setAuthorized(false); setPending(null); setState(null); };
    const restore = () => { request.current?.abort(); request.current = null; setAuthorized(true); setPending(null); setState(null); };
    window.addEventListener(authSessionEndedEvent, revoke);
    window.addEventListener(authSessionStartedEvent, restore);
    return () => {
      window.removeEventListener(authSessionEndedEvent, revoke);
      window.removeEventListener(authSessionStartedEvent, restore);
    };
  }, []);
  useEffect(() => {
    request.current?.abort();
    setState(value => value?.loading ? { ...value, loading: false, execution: null, error: "" } : value);
    return () => request.current?.abort();
  }, [enabled, key, nodeId]);

  const choose = useCallback(async (execution: ActivityExecutionStateSummary, graph: ExecutableActivityGraph,
    labelFor: (execution: ActivityExecutionStateSummary) => string, includeSelf = false, label?: string) => {
    if (!active.current || execution.workflowExecutionId !== workflowExecutionId) return false;
    setPending(null);
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    const isCurrent = () => active.current && currentKey.current === key && currentNode.current === nodeId && request.current === controller;
    setState(value => ({ key, nodeId, execution: null, scopes: value?.key === key ? value.scopes : [], loading: true, error: "" }));
    try {
      if (!await supportsActivityExecutionBrowsing(context)) throw new Error("Execution browsing is unavailable on this host.");
      if (!isCurrent() || controller.signal.aborted) return false;
      const { loadExecutionScope } = await import("./executionScope");
      if (!isCurrent() || controller.signal.aborted) return false;
      const result = await loadExecutionScope(context, workflowExecutionId, execution, {
        graph, includeSelf, label, labelFor, signal: controller.signal, isCurrent
      });
      if (!isCurrent() || controller.signal.aborted) return false;
      setState({ key, nodeId, execution, scopes: result.scopes, loading: false, error: "" });
      return true;
    } catch (error) {
      if (!isCurrent() || controller.signal.aborted) return false;
      setState({ key, nodeId, execution: null, scopes: [], loading: false,
        error: error instanceof Error ? error.message : "Execution context could not be resolved. Retry the selection." });
      return false;
    }
  }, [context, key, nodeId, workflowExecutionId]);
  useEffect(() => {
    if (!pending) return;
    setPending(null);
    if (pending.key !== key || pending.nodeId !== nodeId || !enabled || !authorized) return;
    void choose(pending.execution, pending.graph, pending.labelFor);
  }, [authorized, choose, enabled, key, nodeId, pending]);
  const current = enabled && authorized && state?.key === key ? state : null;
  return {
    execution: current?.nodeId === nodeId ? current?.execution ?? null : null,
    scopes: current && !current.loading && !current.error ? current.scopes : [],
    loading: current?.loading ?? false,
    error: current?.error ?? "",
    choose,
    queue: (execution: ActivityExecutionStateSummary, graph: ExecutableActivityGraph,
      labelFor: (execution: ActivityExecutionStateSummary) => string, targetNodeId: string) => {
      if (!active.current || execution.workflowExecutionId !== workflowExecutionId) return false;
      request.current?.abort(); request.current = null; setState(null);
      setPending({ key, nodeId: targetNodeId, execution, graph, labelFor });
      return true;
    },
    unavailable: () => {
      request.current?.abort(); request.current = null; setPending(null);
      setState({ key, nodeId, execution: null, scopes: [], loading: false, error: "This execution scope is unavailable in the current context." });
    },
    clear: () => { request.current?.abort(); request.current = null; setPending(null); setState(null); }
  };
}

export type AttachedExecutionSelection = ReturnType<typeof useAttachedExecutionSelection>;
