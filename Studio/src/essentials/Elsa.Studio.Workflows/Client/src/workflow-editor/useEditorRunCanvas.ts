import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { Node } from "@xyflow/react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { browseActivityExecutions, getExecutable, getExecutableInputSources, getWorkflowInstance } from "../api/runtime";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "../api/capabilities";
import { buildExecutableActivityGraph, type ExecutableActivityGraph } from "../executableGraph";
import type { ActivityCatalogItem, ActivityExecutionStateSummary, WorkflowInstanceDetails } from "../workflowTypes";
import type { WorkflowNodeData } from "../workflowAdapter";
import { applyRuntimeOverlays } from "../workflowRuntimeOverlays";
import { decorateWorkflowCanvasElements } from "./workflowAccessibility";
import type { WorkflowTestRunState } from "./editorTypes";
import { isRejectedTestRun } from "./editorHelpers";
import type { TestRunObservation } from "./useTestRunObservation";

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
export function useEditorRunCanvas({ context, definitionId, testRun, draftSignature, catalog, nodes, selectedNodeId, onOpenIncident, ancestorId, selectedExecution, observation, snapshotView = false }: {
  context: StudioEndpointContext;
  definitionId: string;
  testRun: WorkflowTestRunState | null;
  draftSignature: string;
  catalog: ActivityCatalogItem[];
  nodes: Node<WorkflowNodeData>[];
  selectedNodeId: string | null;
  onOpenIncident?: WorkflowNodeData["onIncidentClick"];
  ancestorId?: string;
  selectedExecution?: ActivityExecutionStateSummary | null;
  observation?: TestRunObservation;
  snapshotView?: boolean;
}) {
  const [dismissedRunId, setDismissedRunId] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [authorized, setAuthorized] = useState(true);
  const [authorizationVersion, setAuthorizationVersion] = useState(0);
  const [evidence, setEvidence] = useState<RunCanvasEvidence | null>(null);
  const previousEvidence = useRef(evidence); previousEvidence.current = evidence;
  const graphCache = useRef<{ key: string; context: StudioEndpointContext; catalog: ActivityCatalogItem[]; graph: ExecutableActivityGraph } | null>(null);
  const runId = testRun?.view.testRunId;
  const executionId = testRun?.view.workflowExecutionId;
  const artifactId = testRun?.view.artifactId;
  const snapshot = testRun?.snapshot;
  const stale = Boolean(testRun && (testRun.draftSignature !== draftSignature || testRun.view.definitionId !== definitionId || snapshot?.definitionId !== definitionId));
  const authorityChanged = Boolean(testRun?.endpointContext && testRun.endpointContext !== context);
  const eligible = Boolean(snapshot && executionId && artifactId && testRun?.endpointContext === context && testRun && !isRejectedTestRun(testRun.view));
  const attached = eligible && (!stale || snapshotView) && dismissedRunId !== runId && authorized;
  const nodeKey = JSON.stringify([...new Set(nodes.map(node => node.data.runtimeNodeId ?? node.id))].sort());
  const key = JSON.stringify([runId, executionId, artifactId, nodeKey, authorizationVersion, observation?.identity, ancestorId, selectedExecution?.activityExecutionId]);
  const refreshObservation = observation?.refresh;
  const refresh = useCallback(() => { setRevision(value => value + 1); refreshObservation?.(); }, [refreshObservation]);

  useEffect(() => {
    const revoke = () => { setAuthorized(false); setEvidence(null); graphCache.current = null; };
    const restore = () => { setAuthorized(true); setEvidence(null); graphCache.current = null; setAuthorizationVersion(value => value + 1); refresh(); };
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
    const previous = previousEvidence.current;
    const next: RunCanvasEvidence = previous?.key === key && previous.context === context
      ? { ...previous, loading: true, error: "" }
      : { key, context, loading: true, error: "", repeated: false, details: null, graph: null, activities: [] };
    setEvidence(next);
    const load = async () => {
      try {
        const details = observation?.details ?? await getWorkflowInstance(context, executionId);
        if (controller.signal.aborted) return;
        if (details.instance.workflowExecutionId !== executionId || details.instance.artifactId !== artifactId) {
          throw new Error("This execution does not match the Test Run snapshot.");
        }
        const graphKey = JSON.stringify([artifactId, details.instance.sourceReferenceId, authorizationVersion, observation?.identity]);
        let graph = graphCache.current?.key === graphKey && graphCache.current.context === context && graphCache.current.catalog === catalog ? graphCache.current.graph : null;
        if (!graph) {
          const executable = await getExecutable(context, artifactId, details.instance.sourceReferenceId);
          if (controller.signal.aborted) return;
          if (executable.artifactId !== artifactId) throw new Error("The Test Run executable could not be verified.");
          const sourceId = executable.chosenReference?.sourceReferenceId ?? details.instance.sourceReferenceId;
          const sources = sourceId ? await getExecutableInputSources(context, artifactId, sourceId).catch(() => null) : null;
          if (controller.signal.aborted) return;
          graph = buildExecutableActivityGraph(executable.rootActivity, catalog, sources?.authoredInputs ?? [], sources?.compiledInputs ?? [],
            sources?.accessState ?? sources?.access ?? "unavailable", executable.chosenReference?.activityPresentation ?? []);
          graphCache.current = { key: graphKey, context, catalog, graph };
        }
        const visible = (JSON.parse(nodeKey) as string[]).map(id => graph.factsByNodeId.get(id)).filter(item => item !== undefined);
        const activities: ActivityExecutionStateSummary[] = [];
        let repeated = false;
        let index = 0;
        const worker = async () => {
          while (index < visible.length && !controller.signal.aborted) {
            const fact = visible[index++];
            const exact: string | undefined = selectedExecution?.workflowExecutionId === executionId && selectedExecution.executableNodeId === fact.executableNodeId ? selectedExecution.activityExecutionId : undefined;
            const page = await browseActivityExecutions(context, executionId, { executableNodeId: fact.executableNodeId,
              ...(ancestorId ? { ancestorActivityExecutionId: ancestorId } : {}), ...(exact ? { anchorActivityExecutionId: exact } : {}), limit: 1 }, controller.signal);
            const execution = page.items[0]?.execution;
            // A repeated node has no selected occurrence yet. Do not paint a global latest result.
            if (page.totalCount > 1 && !exact) repeated = true;
            if ((page.totalCount === 1 || exact && execution?.activityExecutionId === exact) && execution?.workflowExecutionId === executionId && execution.executableNodeId === fact.executableNodeId) activities.push(execution);
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
    // Observation revision advances only after a verified read; keep a deliberately selected occurrence.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ancestorId, artifactId, attached, authorizationVersion, catalog, context, executionId, key, nodeKey, observation?.revision, revision, selectedExecution]);

  const current = attached && evidence?.key === key && evidence.context === context ? evidence : null;
  const shownNodes = useMemo(() => {
    if (!current?.details || !current.graph) return nodes;
    const executionIds = new Set(current.activities.map(activity => activity.activityExecutionId));
    const incidents = (current.details.incidents ?? []).filter(incident => incident.activityExecutionId && executionIds.has(incident.activityExecutionId));
    return decorateWorkflowCanvasElements(
      applyRuntimeOverlays(nodes, current.activities, incidents, selectedNodeId, catalog, current.graph)
        .map((node, index) => ({ ...node, selected: nodes[index].selected,
          data: { ...node.data, onIncidentClick: onOpenIncident ? (incidentId: string, targetNodeId?: string | null) => onOpenIncident(incidentId, targetNodeId === null ? null : targetNodeId ?? node.id) : undefined,
            ...(node.data.runtime ? { runtime: { ...node.data.runtime, selected: !!nodes[index].selected } } : {}) } })), []).nodes;
  }, [catalog, current, nodes, onOpenIncident, selectedNodeId]);

  return {
    nodes: shownNodes,
    eligible,
    graph: current?.graph ?? null,
    details: current?.details ?? null,
    activities: current?.activities ?? [],
    refreshKey: revision + (observation?.revision ?? 0),
    observationStatus: observation?.status,
    attached,
    stale,
    snapshotView,
    loading: attached && (!current || current.loading),
    error: authorityChanged ? "This Test Run belongs to another backend session." : !authorized ? "Sign in again to inspect execution evidence." : current?.error || observation?.error || "",
    repeated: current?.repeated ?? false,
    status: observation?.waits.length ? "Waiting" : current?.details?.instance.status,
    granularity: current?.details?.inspectionGranularity,
    refresh,
    detach: () => setDismissedRunId(runId ?? null),
    attach: () => setDismissedRunId(null)
  };
}

export type EditorRunCanvas = ReturnType<typeof useEditorRunCanvas>;
