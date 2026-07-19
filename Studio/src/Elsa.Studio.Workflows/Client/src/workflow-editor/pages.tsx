import React, { lazy, useCallback, useEffect, useState } from "react";
import type { StudioActivityPropertyEditorContribution, StudioAiContributionApi, StudioEndpointContext, StudioExpressionEditorContribution, StudioWorkflowDesignerPanelContribution, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import { WorkflowLazyBoundary } from "../WorkflowLazyBoundary";
import { updateWorkflowDefinitionIdInUrl } from "./workflowDefinitionUrl";

const WorkflowEditor = lazy(() => import("./WorkflowEditor").then(module => ({ default: module.WorkflowEditor })));
const WorkflowDefinitions = lazy(() => import("./WorkflowDefinitions").then(module => ({ default: module.WorkflowDefinitions })));
const WorkflowExecutables = lazy(() => import("./WorkflowExecutables").then(module => ({ default: module.WorkflowExecutables })));
const WorkflowExecutableInspectorWorkbench = lazy(() => import("./WorkflowExecutableInspector").then(module => ({ default: module.WorkflowExecutableInspectorWorkbench })));
const WorkflowInstances = lazy(() => import("./WorkflowInstances").then(module => ({ default: module.WorkflowInstances })));
const WorkflowInstanceDetailsWorkbench = lazy(() => import("./WorkflowInstances").then(module => ({ default: module.WorkflowInstanceDetailsWorkbench })));

export function WorkflowManagementPage({
  context,
  ai,
  propertyEditors,
  expressionEditors,
  runInputEditors,
  workflowDesignerPanels,
  autosaveEnabledByDefault
}: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  runInputEditors: StudioWorkflowRunInputEditorContribution[];
  workflowDesignerPanels: StudioWorkflowDesignerPanelContribution[];
  autosaveEnabledByDefault?: boolean;
}) {
  const [definitionId, setDefinitionId] = useState(readDefinitionIdFromUrl);

  useEffect(() => {
    const syncFromLocation = () => setDefinitionId(readDefinitionIdFromUrl());
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, []);

  const openDefinition = (id: string | null) => {
    const url = updateWorkflowDefinitionIdInUrl(new URL(window.location.href), id);
    window.history.pushState({}, "", `${url.pathname}${url.search}${url.hash}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return definitionId
    ? (
      <WorkflowLazyBoundary label="workflow designer">
        <WorkflowEditor context={context} definitionId={definitionId} ai={ai} propertyEditors={propertyEditors} expressionEditors={expressionEditors} runInputEditors={runInputEditors} workflowDesignerPanels={workflowDesignerPanels} autosaveEnabledByDefault={autosaveEnabledByDefault} onBack={() => openDefinition(null)} />
      </WorkflowLazyBoundary>
    )
    : (
      <WorkflowsPageFrame title="Definitions">
        <WorkflowLazyBoundary label="workflow definitions">
          <WorkflowDefinitions context={context} ai={ai} onOpen={openDefinition} />
        </WorkflowLazyBoundary>
      </WorkflowsPageFrame>
    );
}

export function WorkflowExecutablesPage({ context, ai, runInputEditors }: { context: StudioEndpointContext; ai: StudioAiContributionApi; runInputEditors: StudioWorkflowRunInputEditorContribution[] }) {
  const [definitionFilter, setDefinitionFilter] = useState(readExecutableDefinitionFilterFromUrl);

  useEffect(() => {
    const syncFromLocation = () => setDefinitionFilter(readExecutableDefinitionFilterFromUrl());
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, []);

  const updateDefinitionFilter = useCallback((filter: string | null) => {
    const normalizedFilter = filter?.trim() ?? "";
    const url = new URL(window.location.href);

    if (normalizedFilter) {
      url.searchParams.set("definition", normalizedFilter);
    } else {
      url.searchParams.delete("definition");
    }

    setDefinitionFilter(normalizedFilter || null);
    window.history.replaceState({}, "", `${url.pathname}${url.search}${url.hash}`);
  }, []);

  return (
    <WorkflowsPageFrame title="Executables">
      <WorkflowLazyBoundary label="workflow executables">
        <WorkflowExecutables context={context} ai={ai} runInputEditors={runInputEditors} definitionFilter={definitionFilter} onDefinitionFilterChange={updateDefinitionFilter} />
      </WorkflowLazyBoundary>
    </WorkflowsPageFrame>
  );
}

export function WorkflowExecutableInspectorPage({ context, ai, runInputEditors }: { context: StudioEndpointContext; ai: StudioAiContributionApi; runInputEditors: StudioWorkflowRunInputEditorContribution[] }) {
  const [location, setLocation] = useState(readExecutableInspectorLocation);

  useEffect(() => {
    const syncFromLocation = () => setLocation(readExecutableInspectorLocation());
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, []);

  // Selecting a reference re-renders the same artifact from that reference's Layout Sidecar; the
  // choice is part of the page address (?ref=) so it survives reload and can be shared.
  const selectReference = useCallback((sourceReferenceId: string | null) => {
    const url = new URL(window.location.href);

    if (sourceReferenceId) {
      url.searchParams.set("ref", sourceReferenceId);
    } else {
      url.searchParams.delete("ref");
    }

    window.history.replaceState({}, "", `${url.pathname}${url.search}${url.hash}`);
    setLocation(readExecutableInspectorLocation());
  }, []);

  return (
    <WorkflowsPageFrame title="Executable Inspector">
      <WorkflowLazyBoundary label="executable inspector">
        <WorkflowExecutableInspectorWorkbench
          context={context}
          ai={ai}
          runInputEditors={runInputEditors}
          artifactId={location.artifactId}
          sourceReferenceId={location.sourceReferenceId}
          onSelectReference={selectReference}
        />
      </WorkflowLazyBoundary>
    </WorkflowsPageFrame>
  );
}

export function WorkflowInstancesPage({ context, navigate }: { context: StudioEndpointContext; navigate(path: string): void }) {
  return (
    <WorkflowsPageFrame title="Runs">
      <WorkflowLazyBoundary label="workflow runs">
        <WorkflowInstances context={context} navigate={navigate} />
      </WorkflowLazyBoundary>
    </WorkflowsPageFrame>
  );
}

export function WorkflowInstanceDetailsPage({ context, ai, expressionEditors = [], navigate }: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  expressionEditors?: StudioExpressionEditorContribution[];
  navigate(path: string): void;
}) {
  const workflowExecutionId = readWorkflowExecutionIdFromUrl();

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
        <WorkflowInstanceDetailsWorkbench context={context} ai={ai} expressionEditors={expressionEditors} workflowExecutionId={workflowExecutionId} navigate={navigate} />
      </WorkflowLazyBoundary>
    </WorkflowsPageFrame>
  );
}

function WorkflowsPageFrame({ title, context, workbench = false, children }: {
  title: string;
  context?: React.ReactNode;
  workbench?: boolean;
  children: React.ReactNode;
}) {
  return (
    <section className={workbench ? "wf-page wf-page--run-workbench" : "wf-page"}>
      <div className="wf-page-header">
        <div>
          <span className="wf-kicker">Workflow management</span>
          <h2>{title}</h2>
        </div>
        {context}
      </div>
      {children}
    </section>
  );
}

function readDefinitionIdFromUrl() {
  return new URLSearchParams(window.location.search).get("definition");
}

function readExecutableDefinitionFilterFromUrl() {
  return new URLSearchParams(window.location.search).get("definition");
}

function readExecutableInspectorLocation() {
  const match = /^\/workflows\/executables\/([^/]+)$/.exec(window.location.pathname);
  return {
    artifactId: match ? decodeURIComponent(match[1]) : "",
    sourceReferenceId: new URLSearchParams(window.location.search).get("ref")
  };
}

function readWorkflowExecutionIdFromUrl() {
  const match = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return match ? decodeURIComponent(match[1]) : "";
}
