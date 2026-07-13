import React, { useCallback, useEffect, useState } from "react";
import type { StudioActivityPropertyEditorContribution, StudioAiContributionApi, StudioEndpointContext, StudioExpressionEditorContribution, StudioWorkflowDesignerPanelContribution } from "@elsa-workflows/studio-sdk";
import { WorkflowEditor } from "./WorkflowEditor";
import { WorkflowDefinitions } from "./WorkflowDefinitions";
import { WorkflowExecutables } from "./WorkflowExecutables";
import { WorkflowExecutableInspectorWorkbench } from "./WorkflowExecutableInspector";
import { WorkflowInstances, WorkflowInstanceDetailsWorkbench } from "./WorkflowInstances";

export function WorkflowManagementPage({
  context,
  ai,
  propertyEditors,
  expressionEditors,
  workflowDesignerPanels,
  autosaveEnabledByDefault
}: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
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
    const url = id ? `/workflows/definitions?definition=${encodeURIComponent(id)}` : "/workflows/definitions";
    window.history.pushState({}, "", url);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return definitionId
    ? <WorkflowEditor context={context} definitionId={definitionId} ai={ai} propertyEditors={propertyEditors} expressionEditors={expressionEditors} workflowDesignerPanels={workflowDesignerPanels} autosaveEnabledByDefault={autosaveEnabledByDefault} onBack={() => openDefinition(null)} />
    : (
      <WorkflowsPageFrame title="Definitions">
        <WorkflowDefinitions context={context} ai={ai} onOpen={openDefinition} />
      </WorkflowsPageFrame>
    );
}

export function WorkflowExecutablesPage({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
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
      <WorkflowExecutables context={context} ai={ai} definitionFilter={definitionFilter} onDefinitionFilterChange={updateDefinitionFilter} />
    </WorkflowsPageFrame>
  );
}

export function WorkflowExecutableInspectorPage({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
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
      <WorkflowExecutableInspectorWorkbench
        context={context}
        ai={ai}
        artifactId={location.artifactId}
        sourceReferenceId={location.sourceReferenceId}
        onSelectReference={selectReference}
      />
    </WorkflowsPageFrame>
  );
}

export function WorkflowInstancesPage({ context }: { context: StudioEndpointContext }) {
  return (
    <WorkflowsPageFrame title="Runs">
      <WorkflowInstances context={context} />
    </WorkflowsPageFrame>
  );
}

export function WorkflowInstanceDetailsPage({ context, ai, navigate }: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  navigate(path: string): void;
}) {
  const workflowExecutionId = readWorkflowExecutionIdFromUrl();

  return (
    <WorkflowsPageFrame title="Run">
      <WorkflowInstanceDetailsWorkbench context={context} ai={ai} workflowExecutionId={workflowExecutionId} navigate={navigate} />
    </WorkflowsPageFrame>
  );
}

function WorkflowsPageFrame({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="wf-page">
      <div className="wf-page-header">
        <div>
          <span className="wf-kicker">Workflow management</span>
          <h2>{title}</h2>
        </div>
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
