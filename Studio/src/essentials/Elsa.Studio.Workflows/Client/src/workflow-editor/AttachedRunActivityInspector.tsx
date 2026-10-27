import type { StudioEndpointContext, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { findExecutableNodeFacts } from "../executableGraph";
import { resolveRuntimeActivityLabel } from "../runtimeActivityDisplay";
import type { ActivityCatalogItem, ActivityExecutionStateSummary } from "../workflowTypes";
import { ActivityExecutionInspectionPanel, ExecutionScopeNavigator } from "./ActivityExecutionBrowser";
import { WorkflowActivityExecutionDetails } from "./WorkflowActivityExecutionDetails";
import type { EditorRunCanvas } from "./useEditorRunCanvas";
import type { AttachedExecutionSelection } from "./useAttachedExecutionSelection";

export function AttachedRunActivityInspector({ context, canvas, selection, activityId, ownerId, catalog, expressionEditors, onEnter }: {
  context: StudioEndpointContext; canvas: EditorRunCanvas; selection: AttachedExecutionSelection;
  activityId: string | null; ownerId: string | null; catalog: ActivityCatalogItem[];
  expressionEditors: StudioExpressionEditorContribution[];
  onEnter(execution: ActivityExecutionStateSummary): void;
}) {
  const graph = canvas.graph;
  const details = canvas.details;
  if (!canvas.attached) return <p role="status">Attach the unchanged Test Run snapshot to inspect its run data.</p>;
  if (canvas.error) return <p role="status">{canvas.error}</p>;
  if (!graph || !details) return <p role="status">Loading execution evidence…</p>;
  const labelFor = (execution: ActivityExecutionStateSummary) => resolveRuntimeActivityLabel(
    findExecutableNodeFacts(graph, execution)?.presentation,
    catalog.find(item => item.activityTypeKey === execution.activityType), execution.activityType);
  const authoredFacts = activityId ? graph.factsByNodeId.get(activityId) : undefined;
  const execution = selection.execution;
  // A child inside a pinned reusable boundary may have no node in the outer authored graph.
  const boundaryChild = execution && !findExecutableNodeFacts(graph, execution) ? execution : null;
  const identity = boundaryChild ?? authoredFacts;
  if (!identity) return <p role="status">This canvas element has no executable activity evidence.</p>;
  const selected = execution?.executableNodeId === identity.executableNodeId ? execution : null;
  const scopeIndex = selection.scopes.map(item => item.execution.authoredActivityId).lastIndexOf(ownerId ?? "");
  const scope = scopeIndex >= 0 ? selection.scopes[scopeIndex] : undefined;
  const ancestorId = scope?.execution.executableNodeId === identity.executableNodeId
    ? scope.execution.parentActivityExecutionId ?? undefined : scope?.execution.activityExecutionId;
  const choose = (value: ActivityExecutionStateSummary) => { void selection.choose(value, graph, labelFor); };
  return <div className="wf-attached-run-inspection">
    <nav className="wf-execution-scope" aria-label="Execution scope">
      <button type="button" onClick={selection.clear}>All executions</button>
      {selection.scopes.map(item => <button type="button" key={item.execution.activityExecutionId} onClick={() => {
        void selection.choose(item.execution, graph, labelFor, true, item.label).then(ready => { if (ready) onEnter(item.execution); });
      }}>{item.label}</button>)}
    </nav>
    {scope ? <ExecutionScopeNavigator context={context} execution={scope.execution} refreshKey={canvas.refreshKey}
      onSelect={value => { void selection.choose(value, graph, labelFor, true); }} onUnavailable={selection.unavailable} onIncident={choose} /> : null}
    {selection.loading ? <p role="status">Resolving execution scope…</p> : selection.error ? <p role="status">{selection.error} Choose the execution again to retry.</p> : null}
    {!selection.loading && !selection.error ? <ActivityExecutionInspectionPanel
      key={`${details.instance.workflowExecutionId}:${identity.executableNodeId}:${ancestorId ?? ""}`}
      context={context} workflowExecutionId={details.instance.workflowExecutionId} nodeId={identity.executableNodeId}
      nodeLabel={authoredFacts?.presentation?.displayName ?? identity.activityType}
      exactId={selected?.activityExecutionId} seed={null} ancestorId={ancestorId} refreshKey={canvas.refreshKey}
      requireExplicitSelection labelFor={labelFor} onSelect={choose} onIncident={choose}
      onEnter={entry => {
        const occurrence = entry.execution.iterationId && entry.iterationPosition && !entry.attempt ? `Iteration ${entry.iterationPosition}` : `Execution ${entry.position}`;
        void selection.choose(entry.execution, graph, labelFor, true, `${labelFor(entry.execution)} · ${occurrence}`)
          .then(ready => { if (ready) onEnter(entry.execution); });
      }}
      renderDetails={(value, navigation, children) => <WorkflowActivityExecutionDetails
        compact context={context} activity={value} activityCatalog={catalog} executableNodeFacts={findExecutableNodeFacts(graph, value)}
        expressionEditors={expressionEditors} executionNavigation={navigation} childExecutions={children} refreshKey={canvas.refreshKey} />}
    /> : null}
  </div>;
}
