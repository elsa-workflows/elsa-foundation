import { useCallback, useEffect, useMemo, useState } from "react";
import type { Node } from "@xyflow/react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { browseActivityExecutions, getExecutable, getWorkflowInstance } from "../api/runtime";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "../api/capabilities";
import { buildExecutableActivityGraph, type ExecutableActivityGraph } from "../executableGraph";
import type { ActivityCatalogItem, ActivityExecutionStateSummary, WorkflowInstanceDetails } from "../workflowTypes";
import type { WorkflowNodeData } from "../workflowAdapter";
import { applyRuntimeOverlays } from "../workflowRuntimeOverlays";
import { decorateWorkflowCanvasElements } from "./workflowAccessibility";
import type { WorkflowTestRunState } from "./editorTypes";
import { isRejectedTestRun } from "./editorHelpers";

interface RunCanvasEvidence {
  key: string;
  context: StudioEndpointContext;
  loading: boolean;
  error: string;
  repeated: boolean;
  details: WorkflowInstanceDetails | null;
  graph: ExecutableActivityGraph | null;
  activities: ActivityExecutionStateSummary[];
}

/** Adds observation to the existing canvas without changing its document, selection or viewport. */
export function useEditorRunCanvas({ context, definitionId, testRun, draftSignature, catalog, nodes, selectedNodeId, onOpenIncident }: {
  context: StudioEndpointContext;
  definitionId: string;
  testRun: WorkflowTestRunState | null;
  draftSignature: string;
  catalog: ActivityCatalogItem[];
  nodes: Node<WorkflowNodeData>[];
  selectedNodeId: string | null;
  onOpenIncident?(): void;
}) {
  const [dismissedRunId, setDismissedRunId] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [authorized, setAuthorized] = useState(true);
  const [evidence, setEvidence] = useState<RunCanvasEvidence | null>(null);
  const runId = testRun?.view.testRunId;
  const executionId = testRun?.view.workflowExecutionId;
  const artifactId = testRun?.view.artifactId;
  const snapshot = testRun?.snapshot;
  const stale = Boolean(testRun && (testRun.draftSignature !== draftSignature || testRun.view.definitionId !== definitionId || snapshot?.definitionId !== definitionId));
  const authorityChanged = Boolean(testRun?.endpointContext && testRun.endpointContext !== context);
  const eligible = Boolean(snapshot && executionId && artifactId && testRun?.endpointContext === context && testRun && !isRejectedTestRun(testRun.view));
  const attached = eligible && !stale && dismissedRunId !== runId && authorized;
  const nodeKey = JSON.stringify([...new Set(nodes.map(node => node.data.runtimeNodeId ?? node.id))].sort());
  const key = JSON.stringify([runId, executionId, artifactId, nodeKey, revision]);
  const refresh = useCallback(() => setRevision(value => value + 1), []);

  useEffect(() => {
    const revoke = () => setAuthorized(false);
    const restore = () => { setAuthorized(true); refresh(); };
    window.addEventListener(authSessionEndedEvent, revoke);
    window.addEventListener(authSessionStartedEvent, restore);
    return () => {
      window.removeEventListener(authSessionEndedEvent, revoke);
      window.removeEventListener(authSessionStartedEvent, restore);
    };
  }, [refresh]);

  useEffect(() => {
    if (!attached || !executionId || !artifactId) return;
    const controller = new AbortController();
    const next: RunCanvasEvidence = { key, context, loading: true, error: "", repeated: false, details: null, graph: null, activities: [] };
    setEvidence(next);
    const load = async () => {
      try {
        const details = await getWorkflowInstance(context, executionId);
        if (controller.signal.aborted) return;
        if (details.instance.workflowExecutionId !== executionId || details.instance.artifactId !== artifactId) {
          throw new Error("This execution does not match the Test Run snapshot.");
        }
        const executable = await getExecutable(context, artifactId, details.instance.sourceReferenceId);
        if (controller.signal.aborted) return;
        if (executable.artifactId !== artifactId) throw new Error("The Test Run executable could not be verified.");
        const graph = buildExecutableActivityGraph(executable.rootActivity, catalog);
        const visible = (JSON.parse(nodeKey) as string[]).map(id => graph.factsByNodeId.get(id)).filter(item => item !== undefined);
        const activities: ActivityExecutionStateSummary[] = [];
        let repeated = false;
        let index = 0;
        const worker = async () => {
          while (index < visible.length && !controller.signal.aborted) {
            const fact = visible[index++];
            const page = await browseActivityExecutions(context, executionId, { executableNodeId: fact.executableNodeId, limit: 1 }, controller.signal);
            const execution = page.items[0]?.execution;
            // A repeated node has no selected occurrence yet. Do not paint a global latest result.
            if (page.totalCount > 1) repeated = true;
            else if (page.totalCount === 1 && execution?.workflowExecutionId === executionId && execution.executableNodeId === fact.executableNodeId) activities.push(execution);
          }
        };
        await Promise.all(Array.from({ length: Math.min(4, visible.length) }, worker));
        if (!controller.signal.aborted) setEvidence({ ...next, loading: false, details, graph, activities, repeated });
      } catch (error) {
        if (controller.signal.aborted) return;
        const message = error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError
          ? "Execution evidence is unavailable on this backend."
          : error instanceof Error ? error.message : "Execution evidence could not be loaded.";
        setEvidence({ ...next, loading: false, error: message });
      }
    };
    void load();
    return () => controller.abort();
  }, [artifactId, attached, catalog, context, executionId, key, nodeKey]);

  const current = attached && evidence?.key === key && evidence.context === context ? evidence : null;
  const shownNodes = useMemo(() => {
    if (!current?.details || !current.graph) return nodes;
    const executionIds = new Set(current.activities.map(activity => activity.activityExecutionId));
    const incidents = (current.details.incidents ?? []).filter(incident => incident.activityExecutionId && executionIds.has(incident.activityExecutionId));
    return decorateWorkflowCanvasElements(
      applyRuntimeOverlays(nodes, current.activities, incidents, selectedNodeId, catalog, current.graph)
        .map((node, index) => ({ ...node, selected: nodes[index].selected,
          data: { ...node.data, onIncidentClick: onOpenIncident,
            ...(node.data.runtime ? { runtime: { ...node.data.runtime, selected: !!nodes[index].selected } } : {}) } })), []).nodes;
  }, [catalog, current, nodes, onOpenIncident, selectedNodeId]);

  return {
    nodes: shownNodes,
    eligible,
    attached,
    stale,
    loading: attached && (!current || current.loading),
    error: authorityChanged ? "This Test Run belongs to another backend session." : !authorized ? "Sign in again to inspect execution evidence." : current?.error ?? "",
    repeated: current?.repeated ?? false,
    status: current?.details?.instance.status,
    granularity: current?.details?.inspectionGranularity,
    refresh,
    detach: () => setDismissedRunId(runId ?? null),
    attach: () => setDismissedRunId(null)
  };
}

export type EditorRunCanvas = ReturnType<typeof useEditorRunCanvas>;
