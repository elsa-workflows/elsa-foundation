import React, { lazy, useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ActivityPropertiesPanel } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel";
import { ActivityDefinitionsPage } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionsPage";
import { activityGraphImplementationEditorContribution } from "../../src/Elsa.Studio.Workflows/Client/src/activityGraphContribution";
import { WorkflowLazyBoundary } from "../../src/Elsa.Studio.Workflows/Client/src/WorkflowLazyBoundary";
import { useRunDetailLayout } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/useRunDetailLayout";
import { createEndpointContext, type StudioActivityDescriptor, type StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "../../src/Elsa.Studio.Workflows/Client/src/workflowTypes";
import type { ActivityCatalogItem, ActivityExecutionStateSummary } from "../../src/Elsa.Studio.Workflows/Client/src/workflowTypes";
import { listActivities, listRecommendedActivityDefinitions, useActivityDefinitionVersion } from "../../src/Elsa.Studio.Workflows/Client/src/api/activityDesign";
import { runExecutable } from "../../src/Elsa.Studio.Workflows/Client/src/api/runtime";
import { createActivityNode, getActivityDisplay } from "../../src/Elsa.Studio.Workflows/Client/src/workflowAdapter";
import { projectRecommendedPalette } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/useWorkflowEditorData";
import { ActivityPalettePanel } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPalettePanel";
import { InspectorPanel } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/InspectorPanel";
import { WorkflowActivityExecutionDetails } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import "../../src/Elsa.Studio.Web/Client/src/app/ui/tokens.css";
import "../../src/Elsa.Studio.Workflows/Client/src/styles.css";
import "./fixture.css";

const searchParams = new URLSearchParams(window.location.search);
const scrollingFixture = searchParams.get("mode") === "scroll";
const dictionaryFixture = searchParams.get("mode") === "dictionary";
const lazyBoundaryFixture = searchParams.get("mode") === "lazy-boundary";
const runDetailFixture = searchParams.get("mode") === "run-detail";
const activityDefinitionsFixture = searchParams.get("mode") === "activity-definitions" || window.location.pathname.startsWith("/workflows/activity-definitions");
const reusableBoundaryFixture = searchParams.get("mode") === "reusable-boundary";
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
const endpointContext = createEndpointContext(window.location.origin);

const DeferredWorkflowPanel = lazy(() => new Promise<{ default: React.ComponentType }>(resolve => {
  window.setTimeout(() => resolve({ default: () => <section aria-label="Deferred workflow designer">Workflow designer ready</section> }), 3_000);
}));

const expressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Input", displayName: "Input", editingMode: "reference" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
  { type: "Liquid", displayName: "Liquid", editingMode: "text" },
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "Object", displayName: "Object", editingMode: "structured" },
  { type: "Secret", displayName: "Secret", editingMode: "reference" },
  { type: "Variable", displayName: "Variable", editingMode: "reference" }
];

const descriptor: StudioActivityDescriptor = {
  typeName: "Elsa.Activities.Http.Activities.HttpEndpoint",
  displayName: "HTTP Endpoint",
  inputs: [{
    name: "Path",
    typeName: "System.String",
    displayName: "Path",
    description: "The route handled by this endpoint.",
    order: 0,
    category: "General",
    isBrowsable: true,
    uiHint: "singleline",
    isWrapped: true,
    defaultSyntax: "Literal"
  }],
  outputs: [],
  ports: []
};

const dictionaryDescriptor: StudioActivityDescriptor = {
  typeName: "Elsa.Activities.Http.Activities.HttpRequest",
  displayName: "HTTP Request",
  inputs: [{
    name: "Headers",
    typeName: "System.Collections.Generic.IDictionary`2[System.String,System.String]",
    displayName: "Headers",
    description: "Headers sent with the request.",
    order: 0,
    category: "General",
    isBrowsable: true,
    isWrapped: true,
    defaultSyntax: "Literal",
    uiSpecifications: {
      dictionary: {
        keyLabel: "Header name",
        valueLabel: "Header value",
        keyPlaceholder: "Content-Type",
        valuePlaceholder: "application/json",
        keyComparison: "ordinalIgnoreCase"
      }
    }
  }],
  outputs: [],
  ports: []
};

function Fixture() {
  const [activity, setActivity] = useState<ActivityNode>({
    nodeId: "http-endpoint-1",
    activityVersionId: "http-endpoint-v1",
    inputs: [{ referenceKey: "Path", value: { value: "/orders", expressionType: "Literal" } }],
    outputs: [],
    structure: null,
    ...(dictionaryFixture ? {
      headers: {
        typeName: "System.Collections.Generic.IDictionary`2[System.String,System.String]",
        expression: {
          type: "Literal",
          value: {
            Accept: "application/json",
            "X-Correlation-Id": "{{ correlationId }}",
            "Cache-Control": "no-cache",
            "User-Agent": "Elsa Studio",
            "X-Region": "eu-west",
            "X-Trace": "enabled"
          }
        }
      }
    } : {})
  });

  const activeDescriptor = dictionaryFixture ? dictionaryDescriptor : descriptor;

  return (
    <main className="wf-editor browser-fixture">
      <div className="browser-fixture-copy">
        <h1>Workflow designer</h1>
        <p>The inspector intentionally clips its own content to reproduce the original stacking defect.</p>
      </div>
      <aside className={`wf-inspector browser-inspector${scrollingFixture ? " browser-inspector--scroll" : ""}`} aria-label="Activity inspector">
        <h2>{dictionaryFixture ? "HTTP Request" : "HTTP Endpoint"}</h2>
        <div className="browser-inspector-spacer" aria-hidden="true" />
        <ActivityPropertiesPanel
          activity={activity}
          descriptor={activeDescriptor}
          editors={[]}
          expressionEditors={[]}
          expressionDescriptors={expressionDescriptors}
          expressionDescriptorStatus="ready"
          descriptorStatus="ready"
          visibleVariables={[]}
          scopeStatus="ready"
          onChange={setActivity}
        />
        {scrollingFixture ? <div className="browser-inspector-tail" aria-hidden="true" /> : null}
      </aside>
    </main>
  );
}

function LazyBoundaryFixture() {
  return (
    <main className="browser-fixture">
      <h1>Workflow management</h1>
      <WorkflowLazyBoundary label="workflow designer">
        <DeferredWorkflowPanel />
      </WorkflowLazyBoundary>
    </main>
  );
}

function RunDetailFixture() {
  const [selectedActivityId, setSelectedActivityId] = useState<string | null>(null);
  const layout = useRunDetailLayout({ selectedActivityId });

  return (
    <div className="browser-run-shell">
      <header className="browser-run-shell-header">Elsa Foundation Studio</header>
      <div className="content browser-run-content">
        <section className="wf-page wf-page--run-workbench">
          <div className="wf-page-header">
            <div><span className="wf-kicker">Workflow management</span><h2>Run</h2></div>
            <code>wfexec-browser</code>
          </div>
          <div className="wf-toolbar">
            <button type="button" onClick={() => setSelectedActivityId("activity-1")}>Select activity</button>
            <button type="button" onClick={() => {
              layout.closeInspector();
              setSelectedActivityId(null);
            }}>Close details</button>
          </div>
          <div
            ref={layout.containerRef}
            className={layout.workbenchClassName}
            style={layout.workbenchStyle}
            data-testid="run-workbench"
            data-layout-mode={layout.mode}
          >
            <section className="wf-instance-canvas-shell" aria-label="Workflow run canvas">
              <header><h3>Canvas</h3></header>
              <div className="wf-instance-canvas">Activity graph</div>
            </section>
            <div className="wf-side-resize-spacer" />
            <aside className="wf-instance-inspector" aria-label="Run details">
              <header><h3>Activity details</h3></header>
              <div className="wf-instance-section">Evaluated inputs</div>
            </aside>
          </div>
        </section>
      </div>
      <footer className="browser-run-console">Console</footer>
    </div>
  );
}

function ReusableBoundaryFixture() {
  const [palette, setPalette] = useState<ActivityCatalogItem[]>([]);
  const [recommendations, setRecommendations] = useState<Awaited<ReturnType<typeof listRecommendedActivityDefinitions>>>([]);
  const [selected, setSelected] = useState<ActivityNode | null>(null);
  const [runActivity, setRunActivity] = useState<ActivityExecutionStateSummary | null>(null);
  const [error, setError] = useState("");
  const selectedRecommendation = recommendations.find(item => item.definitionId === selected?.activityDefinitionId) ?? null;
  const selectedVersion = useActivityDefinitionVersion(
    endpointContext,
    selected?.activityDefinitionId ?? "",
    selected?.activityDefinitionVersionId ?? null,
    Boolean(selected?.activityDefinitionId)
  );
  const groups = useMemo(() => palette.length > 0 ? [{
    category: palette[0].category,
    activities: palette
  }] : [], [palette]);

  useEffect(() => {
    Promise.all([listActivities(endpointContext), listRecommendedActivityDefinitions(endpointContext)]).then(
      ([catalog, nextRecommendations]) => {
        setRecommendations(nextRecommendations);
        setPalette(projectRecommendedPalette(catalog.activities ?? [], nextRecommendations));
      },
      () => setError("The reusable activity picker is unavailable.")
    );
  }, []);

  const place = (activity: ActivityCatalogItem) => {
    setSelected(createActivityNode(activity, "invoice-boundary"));
    setRunActivity(null);
  };

  const dispatch = async () => {
    setError("");
    try {
      const response = await runExecutable(endpointContext, "workflow-artifact-1", {}, "workflow-source-1");
      const workflowExecutionId = response.workflowExecutionId ?? response.runId ?? response.executionId;
      if (!workflowExecutionId) throw new Error("No workflow execution identity was returned.");
      setRunActivity({
        activityExecutionId: "boundary-execution-1",
        workflowExecutionId,
        executableNodeId: "invoice-boundary",
        authoredActivityId: "invoice-boundary",
        activityType: selected?.activityDefinitionId ? "Contoso.InvoiceEvaluator" : "",
        activityTypeVersion: selected?.activityDefinitionVersion ?? "",
        status: "Completed",
        subStatus: null,
        scheduledAt: "2026-07-17T10:00:00Z",
        startedAt: "2026-07-17T10:00:01Z",
        completedAt: "2026-07-17T10:00:02Z",
        bookmarkIds: [],
        incidentIds: [],
        faultCount: 0,
        aggregateFaultCount: 0,
        metadata: {}
      });
    } catch {
      setError("The workflow run could not be started.");
    }
  };

  return (
    <main className="wf-page browser-reusable-journey">
      <header className="wf-page-header">
        <div><span className="wf-kicker">Workflow authoring</span><h1>Reusable activity journey</h1></div>
      </header>
      {error ? <p role="alert">{error}</p> : null}
      <div className="browser-reusable-grid">
        <aside className="wf-palette" aria-label="Activity palette">
          <ActivityPalettePanel
            paletteSearch=""
            onSearchChange={() => undefined}
            groups={groups}
            expandedCategories={new Set(groups.map(group => group.category))}
            onToggleCategory={() => undefined}
            onActivityClick={place}
            onActivityDragStart={() => undefined}
            onActivityDragEnd={() => undefined}
            onActivityPointerDown={() => undefined}
          />
        </aside>
        <section className="wf-instance-canvas-shell" aria-label="Workflow canvas">
          <h2>Workflow canvas</h2>
          {selected ? (
            <button type="button" className="wf-node" data-icon="reusable" aria-label={`${getActivityDisplay(palette[0])} exact version ${selected.activityDefinitionVersion}`}>
              <strong>{getActivityDisplay(palette[0])}</strong>
              <small className="wf-node-version">v{selected.activityDefinitionVersion}</small>
            </button>
          ) : <p>Select the recommended reusable activity.</p>}
          <button type="button" onClick={dispatch} disabled={!selected}>Dispatch workflow</button>
          {runActivity ? <p role="status">One Run · {runActivity.workflowExecutionId}</p> : null}
        </section>
        <aside className="wf-instance-inspector" aria-label={runActivity ? "Run details" : "Activity inspector"}>
          {runActivity ? (
            <WorkflowActivityExecutionDetails
              context={endpointContext}
              activity={runActivity}
              activityCatalog={palette}
            />
          ) : (
            <InspectorPanel
              context={endpointContext}
              selectedNode={selected}
              selectedNodeLabel={selected ? getActivityDisplay(palette[0]) : ""}
              selectedActivityType={selected ? palette[0].activityTypeKey : ""}
              selectedDescriptor={null}
              selectedNodeAvailability={null}
              selectedReusableVersion={selectedVersion.data ?? null}
              selectedReusableVersionStatus={!selected ? "idle" : selectedVersion.isPending ? "loading" : selectedVersion.isError ? "failed" : "ready"}
              selectedRecommendedVersion={selectedRecommendation}
              selectedSlots={[]}
              catalog={palette}
              selectedSupportsScopedVariables={false}
              propertyEditors={[]}
              expressionEditors={[]}
              expressionDescriptors={[]}
              expressionDescriptorStatus="ready"
              descriptorStatus="ready"
              onRetryExpressionDescriptors={() => undefined}
              scopedVariableAnalysis={{ visibleVariables: [], shadowingWarnings: [], status: "unavailable" }}
              onSelectedActivityChange={setSelected}
              onEnterSlot={() => undefined}
              onReplaceSlotActivity={() => undefined}
            />
          )}
        </aside>
      </div>
    </main>
  );
}

const theme = searchParams.get("theme");
document.documentElement.dataset.theme = theme === "black-glass" ? "black-glass" : "harbor";
document.documentElement.dataset.themeMode = theme === "black-glass" ? "dark" : "light";
createRoot(document.getElementById("root")!).render(
  activityDefinitionsFixture
    ? <QueryClientProvider client={queryClient}><ActivityDefinitionsPage context={endpointContext} activityEditors={() => [activityGraphImplementationEditorContribution]} runtime={{ identity: { tenantId: "browser-tenant", subject: "browser-author" }, activityDefinitions: { localRecovery: { enabled: true, ttlMinutes: 30 } } }} /></QueryClientProvider>
    : reusableBoundaryFixture
      ? <QueryClientProvider client={queryClient}><ReusableBoundaryFixture /></QueryClientProvider>
      : runDetailFixture ? <RunDetailFixture /> : lazyBoundaryFixture ? <LazyBoundaryFixture /> : <Fixture />
);
