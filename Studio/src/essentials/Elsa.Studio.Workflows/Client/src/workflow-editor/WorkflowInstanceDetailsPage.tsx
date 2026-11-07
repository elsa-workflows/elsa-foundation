import { lazy, useRef } from "react";
import { useStudioRouteLocation, useStudioWorkspaceTitle } from "@elsa-workflows/studio-sdk";
import type { StudioBackendSession, StudioAgentRegistry, StudioEndpointContext, StudioExpressionEditorContribution, StudioWorkflowRunRelatedLinkContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowAgentController } from "../workflowAgent";
import { WorkflowLazyBoundary } from "../WorkflowLazyBoundary";
import { WorkflowsPageFrame } from "./WorkflowsPageFrame";
import { readRunSelection } from "./runSelectionUrl";

const WorkflowInstanceDetailsWorkbench = lazy(() => import("./WorkflowInstances").then(module => ({ default: module.WorkflowInstanceDetailsWorkbench })));

export function WorkflowInstanceDetailsPage({ context, ai, weaver, expressionEditors = [], backendSession, runRelatedLinks = [], navigate }: {
  context: StudioEndpointContext;
  ai: StudioAgentRegistry;
  /** The Workflows module's agent state: the run page contributes the exact Run and selected activity execution (spec 1193 FR-029). */
  weaver?: WorkflowAgentController;
  expressionEditors?: StudioExpressionEditorContribution[];
  backendSession?: StudioBackendSession;
  runRelatedLinks?: StudioWorkflowRunRelatedLinkContribution[];
  navigate(path: string): void;
}) {
  // A background workspace tab can render while another tab owns the address bar.
  const routeLocation = useStudioRouteLocation();
  const workflowExecutionId = readWorkflowExecutionIdFromUrl(routeLocation.pathname);
  const searchParams = routeLocation.searchParams;
  // The run view rewrites the selection in the URL as the operator moves around (spec 260 FR-423); read it once per run
  // so a later render never reloads the run for a selection the view itself wrote. Keep the list destination from that
  // same initial URL because the detail view may replace its query while the operator moves between activities.
  const initialSelection = useRef<{ workflowExecutionId: string; activityExecutionId: string | null; returnTo: string | null } | null>(null);
  if (initialSelection.current?.workflowExecutionId !== workflowExecutionId) {
    initialSelection.current = {
      workflowExecutionId,
      activityExecutionId: readRunSelection(routeLocation.search),
      returnTo: searchParams.get("returnTo")
    };
  }
  const initialActivityExecutionId = initialSelection.current.activityExecutionId;
  const initialReturnTo = initialSelection.current.returnTo;
  const requestedInspectorTab = searchParams.get("tab");
  useStudioWorkspaceTitle(workflowExecutionId ? `Run ${workflowExecutionId.length > 10 ? `${workflowExecutionId.slice(0, 8)}…` : workflowExecutionId}` : null, "Execution");
  const initialInspectorTab = requestedInspectorTab === "issues"
    ? "issues"
    : requestedInspectorTab === "details"
      ? "details"
      : requestedInspectorTab === "timeline"
        ? "timeline"
        : requestedInspectorTab === "activity" || initialActivityExecutionId
          ? "activity"
          : "timeline";

  return (
    <WorkflowsPageFrame
      title="Run"
      workbench
      context={workflowExecutionId ? (
        <div className="wf-page-context">
          <span>Workflow Instance ID</span>
          <code title={workflowExecutionId}>{workflowExecutionId}</code>
        </div>
      ) : null}
    >
      <WorkflowLazyBoundary label="workflow run">
        <WorkflowInstanceDetailsWorkbench context={context} ai={ai} weaver={weaver} expressionEditors={expressionEditors} backendSession={backendSession} runRelatedLinks={runRelatedLinks} workflowExecutionId={workflowExecutionId} initialActivityExecutionId={initialActivityExecutionId} initialInspectorTab={initialInspectorTab} initialReturnTo={initialReturnTo} navigate={navigate} />
      </WorkflowLazyBoundary>
    </WorkflowsPageFrame>
  );
}

function readWorkflowExecutionIdFromUrl(pathname = window.location.pathname) {
  const match = /^\/workflows\/instances\/([^/]+)$/.exec(pathname);
  return match ? decodeURIComponent(match[1]) : "";
}
