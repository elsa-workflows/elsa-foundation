import React, { useCallback, useEffect, useState } from "react";
import type { StudioActivityPropertyEditorContribution, StudioAiContributionApi, StudioEndpointContext, StudioExpressionEditorContribution, StudioWorkflowDesignerPanelContribution } from "@elsa-workflows/studio-sdk";
import { WorkflowEditor } from "./WorkflowEditor";
import { WorkflowDefinitions } from "./WorkflowDefinitions";
import { WorkflowExecutables } from "./WorkflowExecutables";
import { WorkflowInstances, WorkflowInstanceDetailsWorkbench } from "./WorkflowInstances";

export function WorkflowManagementPage({
  context,
  ai,
  propertyEditors,
  expressionEditors,
  workflowDesignerPanels
}: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  workflowDesignerPanels: StudioWorkflowDesignerPanelContribution[];
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
    ? <WorkflowEditor context={context} definitionId={definitionId} ai={ai} propertyEditors={propertyEditors} expressionEditors={expressionEditors} workflowDesignerPanels={workflowDesignerPanels} onBack={() => openDefinition(null)} />
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

export function WorkflowInstancesPage({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
  return (
    <WorkflowsPageFrame title="Runs">
      <WorkflowInstances context={context} ai={ai} />
    </WorkflowsPageFrame>
  );
}

export function WorkflowInstanceDetailsPage({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
  const workflowExecutionId = readWorkflowExecutionIdFromUrl();

  return (
    <WorkflowsPageFrame title="Run">
      <WorkflowInstanceDetailsWorkbench context={context} ai={ai} workflowExecutionId={workflowExecutionId} />
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

function readWorkflowExecutionIdFromUrl() {
  const match = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return match ? decodeURIComponent(match[1]) : "";
}
