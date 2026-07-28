import React, { lazy, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ActivityPropertiesPanel } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel";
import { ActivityDefinitionsPage } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionsPage";
import { ActivityUpgradeWorkbenchPage } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityUpgradeWorkbenchPage";
import { Elsa3ReusableImportPage } from "../../src/Elsa.Studio.Workflows/Client/src/Elsa3ReusableImportPage";
import {
  activityGraphImplementationEditorContribution,
  activityGraphSchema2ImplementationEditorContribution
} from "../../src/Elsa.Studio.Workflows/Client/src/activityGraphContribution";
import {
  ActivityGraphImplementationEditor,
  ActivityGraphPublicInterfaceEditor
} from "../../src/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor";
import { ActivityDefinitionCreateDialog } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionCreateDialog";
import { ActivityDefinitionDraftCodeView } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftCodeView";
import { ActivityDefinitionDiagnosticsPanel } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDiagnosticsPanel";
import { ActivityDefinitionTestRunDialog } from "../../src/Elsa.Studio.Workflows/Client/src/ActivityDefinitionTestRunDialog";
import { WorkflowLazyBoundary } from "../../src/Elsa.Studio.Workflows/Client/src/WorkflowLazyBoundary";
import { WorkflowDefinitions } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions";
import { setDialogs } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/dialogs";
import { useRunDetailLayout } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/useRunDetailLayout";
import {
  createEndpointContext,
  type StudioActivityDescriptor,
  type StudioAiContributionApi,
  type StudioEndpointContext,
  type StudioExpressionDescriptor
} from "@elsa-workflows/studio-sdk";
import type {
  ActivityCatalogItem,
  ActivityExecutionStateSummary,
  ActivityNode,
  WorkflowDraft
} from "../../src/Elsa.Studio.Workflows/Client/src/workflowTypes";
import { listActivities, listRecommendedActivityDefinitions, useFullActivityDefinitionVersion } from "../../src/Elsa.Studio.Workflows/Client/src/api/activityDesign";
import { runExecutable } from "../../src/Elsa.Studio.Workflows/Client/src/api/runtime";
import { getDraft, updateDraft } from "../../src/Elsa.Studio.Workflows/Client/src/api/workflowDesign";
import { createActivityNode, getActivityDisplay, type ChildSlot } from "../../src/Elsa.Studio.Workflows/Client/src/workflowAdapter";
import { decorateReusableCatalog, projectRecommendedPalette } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/useWorkflowEditorData";
import { ActivityPalettePanel } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPalettePanel";
import { InspectorPanel } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/InspectorPanel";
import { ActivityVersionChangeDialog } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityVersionChangeDialog";
import { PublicationReviewDialog } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/PublicationReviewDialog";
import { createPublicationReview, type PublicationReviewState, type PublicationVersionSelection } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/publicationReview";
import type { PublicationIntent } from "../../src/Elsa.Studio.Workflows/Client/src/api/publishing";
import {
  applyActivityVersionChange,
  findActivityOccurrence,
  validateActivityVersionChangePrecondition
} from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/activityVersionChangeModel";
import { WorkflowActivityExecutionDetails } from "../../src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import type {
  ActivityDefinitionDraftView,
  ActivityDefinitionVersionView
} from "../../src/Elsa.Studio.Workflows/Client/src/activityDefinitionTypes";
import "../../src/Elsa.Studio.Web/Client/src/app/ui/tokens.css";
import "../../src/Elsa.Studio.Workflows/Client/src/styles.css";
import "./fixture.css";

const searchParams = new URLSearchParams(window.location.search);
const scrollingFixture = searchParams.get("mode") === "scroll";
const dictionaryFixture = searchParams.get("mode") === "dictionary";
const lazyBoundaryFixture = searchParams.get("mode") === "lazy-boundary";
const runDetailFixture = searchParams.get("mode") === "run-detail";
const moveDefinitionsFixture = searchParams.get("mode") === "move-definitions";
const folderRestructureFixture = searchParams.get("mode") === "folder-restructure";
const moveDefinitionsFailureFixture = moveDefinitionsFixture && searchParams.get("move") === "failure";
const moveDefinitionsFolderSourceFixture = moveDefinitionsFixture && searchParams.get("source") === "folder";
const elsa3ReusableImportFixture = window.location.pathname.startsWith("/workflows/activity-definitions/import-elsa3");
const activityUpgradeFixture = window.location.pathname.startsWith("/workflows/activity-definitions/upgrades");
const activityDefinitionsFixture = searchParams.get("mode") === "activity-definitions" ||
  (window.location.pathname.startsWith("/workflows/activity-definitions") && !elsa3ReusableImportFixture && !activityUpgradeFixture);
const reusableBoundaryFixture = searchParams.get("mode") === "reusable-boundary";
const versionChangeFixture = searchParams.get("mode") === "version-change";
const activityInspectorTabsFixture = searchParams.get("mode") === "activity-inspector-tabs";
const publicationReviewFixture = searchParams.get("mode") === "publication-review";
const activityGraphAuthoringFixture = searchParams.get("mode") === "activity-definition-graph-authoring";
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
const endpointContext = createEndpointContext(window.location.origin);

const DeferredWorkflowPanel = lazy(() => new Promise<{ default: React.ComponentType }>(resolve => {
  window.setTimeout(() => resolve({ default: () => <section aria-label="Deferred workflow designer">Workflow designer ready</section> }), 3_000);
}));

function ActivityDefinitionRoutesFixture() {
  const [path, setPath] = useState(() => `${window.location.pathname}${window.location.search}`);
  useEffect(() => {
    const sync = () => setPath(`${window.location.pathname}${window.location.search}`);
    window.addEventListener("popstate", sync);
    return () => window.removeEventListener("popstate", sync);
  }, []);
  const navigate = (nextPath: string) => {
    window.history.pushState({}, "", nextPath);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return path.startsWith("/workflows/activity-definitions/upgrades")
    ? <ActivityUpgradeWorkbenchPage context={endpointContext} />
    : <QueryClientProvider client={queryClient}><ActivityDefinitionsPage context={endpointContext} activityEditors={() => [activityGraphImplementationEditorContribution]} runtime={{ identity: { tenantId: "browser-tenant", subject: "browser-author" }, activityDefinitions: { localRecovery: { enabled: true, ttlMinutes: 30 } } }} navigateToStudioPath={navigate} /></QueryClientProvider>;
}

function ActivityDefinitionGraphAuthoringFixture() {
  const [draft, setDraft] = useState<ActivityDefinitionDraftView>(browserGraphAuthoringDraft());
  const [activeView, setActiveView] = useState<"designer" | "public-interface" | "code">("designer");
  const [createOpen, setCreateOpen] = useState(false);
  const [testRunOpen, setTestRunOpen] = useState(false);
  const [createdMessage, setCreatedMessage] = useState("");
  const updateImplementation = (value: { payload: unknown; layout: ActivityDefinitionDraftView["layout"] }) =>
    setDraft(current => ({
      ...current,
      provider: { ...current.provider, payload: value.payload },
      layout: value.layout,
      validation: null
    }));
  const implementationProps = {
    context: endpointContext,
    definitionId: draft.definitionId,
    draftId: draft.draftId,
    revision: draft.revision,
    providerKey: draft.provider.providerKey,
    providerSchemaVersion: draft.provider.schemaVersion,
    manifestFingerprint: draft.provider.manifestFingerprint,
    contract: draft.contract,
    propertyEditors: [],
    expressionEditors: [],
    graphAuthoringPanels: [],
    historyResetKey: `${draft.draftId}:${draft.provider.schemaVersion}`,
    value: { payload: draft.provider.payload, layout: draft.layout },
    readOnly: false,
    onChange: updateImplementation
  };
  const validation = {
    draftId: draft.draftId,
    revision: draft.revision,
    isValid: false,
    validatedAt: "2026-07-28T00:00:00Z",
    diagnostics: [
      browserGraphDiagnostic("activity.contract.outcome-required", "Error", "/contract/outcomes"),
      browserGraphDiagnostic("activity.graph.outcome-mapping-required", "Error", "/outcomeMappings"),
      browserGraphDiagnostic("activity.graph.node-incomplete", "Warning", "/rootActivity/structure"),
      browserGraphDiagnostic("activity.graph.provider-note", "Info", "/provider", "elsa.activity-graph")
    ]
  } satisfies NonNullable<ActivityDefinitionDraftView["validation"]>;

  return (
    <QueryClientProvider client={queryClient}>
      <main className="ad-page ad-draft-editor" aria-label="Activity Definition graph authoring fixture">
        <header className="ad-workbench-header">
          <div>
            <span className="ad-kicker">Activity Definition authoring</span>
            <h1>Browser graph activity</h1>
            <p>Activity-specific graph editing without workflow lifecycle controls.</p>
          </div>
          <div className="ad-header-actions">
            <button type="button" onClick={() => setCreateOpen(true)}>Create Activity Definition</button>
            <button type="button" onClick={() => setTestRunOpen(true)}>Test Run</button>
            <button
              type="button"
              onClick={() => setDraft(current => ({
                ...current,
                provider: {
                  ...current.provider,
                  schemaVersion: current.provider.schemaVersion === "2" ? "1" : "2"
                }
              }))}
            >
              {draft.provider.schemaVersion === "2" ? "Preview legacy schema" : "Return to schema 2"}
            </button>
          </div>
        </header>
        {createdMessage ? <p role="status">{createdMessage}</p> : null}
        <nav className="ad-authoring-view-tabs" role="tablist" aria-label="Activity Definition authoring views">
          {([
            ["designer", "Designer"],
            ["public-interface", "Public Interface"],
            ["code", "Code"]
          ] as const).map(([id, label]) => (
            <button key={id} type="button" role="tab" aria-selected={activeView === id} onClick={() => setActiveView(id)}>
              {label}
            </button>
          ))}
        </nav>
        <section role="tabpanel" aria-label="Designer" hidden={activeView !== "designer"}>
          <ActivityGraphImplementationEditor {...implementationProps} />
        </section>
        <section role="tabpanel" aria-label="Public Interface" hidden={activeView !== "public-interface"}>
          <ActivityGraphPublicInterfaceEditor {...implementationProps} />
        </section>
        <section role="tabpanel" aria-label="Code" hidden={activeView !== "code"}>
          <ActivityDefinitionDraftCodeView
            draft={draft}
            readOnly={false}
            canUndo={false}
            canRedo={false}
            onApply={setDraft}
            onUndo={() => {}}
            onRedo={() => {}}
            onBufferStateChange={() => {}}
          />
        </section>
        <ActivityDefinitionDiagnosticsPanel
          validation={validation}
          canReturn={false}
          onFocus={async diagnostic => ({
            kind: "focused",
            announcement: `Focused ${diagnostic.code}.`
          })}
          onReturn={() => {}}
        />
        {createOpen ? <ActivityDefinitionCreateDialog
          context={endpointContext}
          activityEditors={[activityGraphSchema2ImplementationEditorContribution]}
          onClose={() => setCreateOpen(false)}
          onCreated={created => {
            setCreateOpen(false);
            setCreatedMessage(`Created ${created.definition.displayName}.`);
          }}
        /> : null}
        {testRunOpen ? <ActivityDefinitionTestRunDialog
          context={endpointContext}
          draft={draft}
          definitionLabel="Browser graph activity"
          inputEditors={[]}
          prepareExactRevision={async onPhase => {
            onPhase("validating");
            return {
              revision: draft.revision,
              validation: { ...validation, isValid: true, diagnostics: [] }
            };
          }}
          onFocusDiagnostic={async () => ({ kind: "unsupported", announcement: "No diagnostic location." })}
          onClose={() => setTestRunOpen(false)}
          onOpenRun={() => {}}
        /> : null}
      </main>
    </QueryClientProvider>
  );
}

function browserGraphAuthoringDraft(): ActivityDefinitionDraftView {
  return {
    draftId: "activity-graph-authoring-draft",
    definitionId: "activity-graph-authoring-definition",
    tenantId: "browser-tenant",
    revision: 3,
    sourceVersionId: null,
    status: "active",
    contract: {
      contractSchemaVersion: "1",
      inputs: [],
      outputs: [],
      outcomes: [
        { referenceKey: "accepted", name: "Accepted", isEmitted: true },
        { referenceKey: "declined", name: "Declined", isEmitted: true }
      ]
    },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "2",
      manifestFingerprint: "sha256:browser-graph-authoring",
      payload: {
        rootActivity: {
          nodeId: "root",
          activityVersionId: "flowchart-v1",
          inputs: [],
          outputs: [],
          structure: {
            kind: "Flowchart",
            schemaVersion: "1",
            payload: { activities: [], connections: [] }
          }
        },
        variables: [],
        outputMappings: [],
        outcomeMappings: []
      }
    },
    layout: [],
    validation: null,
    createdAt: "2026-07-28T00:00:00Z",
    updatedAt: "2026-07-28T00:00:00Z",
    presentationLabel: "Browser graph"
  };
}

function browserGraphDiagnostic(
  code: string,
  severity: "Error" | "Warning" | "Info",
  jsonPointer: string,
  providerKey?: string
) {
  return {
    code,
    severity,
    message: `${code} browser diagnostic`,
    subject: {
      kind: "ActivityDefinitionDraft",
      id: "activity-graph-authoring-draft",
      revision: 3
    },
    location: { jsonPointer, providerKey },
    metadata: {}
  };
}

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

const activityInspectorTabDescriptor: StudioActivityDescriptor = {
  typeName: "Contoso.Browser.LongRunningActivity",
  displayName: "Long running browser activity",
  inputs: Array.from({ length: 18 }, (_, index) => ({
    name: `Input${index + 1}`,
    displayName: `Long input ${index + 1}`,
    typeName: "System.String",
    description: "A representative configurable input that makes the Inputs panel independently scrollable.",
    order: index,
    category: "Inputs",
    isBrowsable: true,
    isWrapped: true,
    defaultSyntax: "Literal"
  })),
  outputs: [
    { name: "Status", displayName: "Status", typeName: "System.Int32", isBrowsable: true },
    { name: "Response", displayName: "Response", typeName: "System.String", isBrowsable: true }
  ],
  ports: []
};

const activityInspectorTabNode: ActivityNode = {
  nodeId: "browser-long-running-activity",
  activityVersionId: "browser-long-running-v1",
  inputs: activityInspectorTabDescriptor.inputs.map(input => ({
    referenceKey: input.name,
    value: { value: `Browser value for ${input.displayName}`, expressionType: "Literal" }
  })),
  outputs: [],
  structure: {
    kind: "elsa.sequence.structure",
    schemaVersion: "1.0.0",
    payload: {
      variables: [{
        referenceKey: "browserVariable",
        name: "Browser variable",
        type: { typeName: "System.String", isCollection: false }
      }]
    }
  }
};

const activityInspectorTabSlots: ChildSlot[] = [{
  id: "browser-long-running.structure:body",
  label: "Body",
  property: "body",
  cardinality: "single",
  mode: "sequence",
  activities: []
}];

function ActivityInspectorTabsFixture() {
  const [activeOuterPanel, setActiveOuterPanel] = useState<"inspector" | "runtime" | "artifacts">("inspector");
  const [activeTabId, setActiveTabId] = useState<React.ComponentProps<typeof InspectorPanel>["activeTabId"]>("inputs");
  const [activity, setActivity] = useState(activityInspectorTabNode);
  const [presentation, setPresentation] = useState({
    nodeId: activityInspectorTabNode.nodeId,
    displayName: "",
    description: ""
  });

  return (
    <main className="wf-editor browser-fixture">
      <h1>Activity inspector tabs</h1>
      <div aria-label="Workflow inspector panel controls">
        <button type="button" aria-pressed={activeOuterPanel === "inspector"} onClick={() => setActiveOuterPanel("inspector")}>Inspector</button>
        <button type="button" aria-pressed={activeOuterPanel === "runtime"} onClick={() => setActiveOuterPanel("runtime")}>Runtime</button>
        <button type="button" aria-pressed={activeOuterPanel === "artifacts"} onClick={() => setActiveOuterPanel("artifacts")}>Artifacts</button>
      </div>
      <aside
        className="wf-inspector browser-inspector"
        aria-label="Activity inspector"
        style={{ width: "264px", height: "510px", padding: 0 }}
      >
        {activeOuterPanel === "inspector" ? (
          <InspectorPanel
            context={endpointContext}
            selectedNode={activity}
            selectedNodeLabel="Long running browser activity"
            selectedActivityType={activityInspectorTabDescriptor.typeName}
            selectedPresentation={presentation}
            selectedDescriptor={activityInspectorTabDescriptor}
            selectedNodeAvailability={{
              state: "RemovedFromCatalog",
              layer: "Catalog",
              referenceKind: "ActivityType",
              reason: "This fixture activity is intentionally unavailable."
            }}
            selectedSlots={activityInspectorTabSlots}
            inspectingScopeOwner
            catalog={[]}
            selectedSupportsScopedVariables
            propertyEditors={[]}
            expressionEditors={[]}
            expressionDescriptors={expressionDescriptors}
            expressionDescriptorStatus="ready"
            descriptorStatus="ready"
            onRetryExpressionDescriptors={() => undefined}
            scopedVariableAnalysis={{ visibleVariables: [], shadowingWarnings: [], status: "ready" }}
            activeTabId={activeTabId}
            onActiveTabChange={setActiveTabId}
            onSelectedActivityChange={setActivity}
            onSelectedPresentationChange={value => setPresentation(current => ({ ...current, ...value }))}
            onEnterSlot={() => undefined}
            onReplaceSlotActivity={() => undefined}
          />
        ) : (
          <section aria-label={`${activeOuterPanel} panel`} style={{ padding: "12px" }}>
            {activeOuterPanel === "runtime" ? "Runtime inspector panel" : "Artifacts inspector panel"}
          </section>
        )}
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

function MoveDefinitionsFixture() {
  const [moved, setMoved] = useState(false);
  const movedRef = useRef(false);
  const destinationRef = useRef<string | null | undefined>(undefined);
  const movedDefinitionIdRef = useRef<string | undefined>(undefined);
  const folder = useMemo(() => ({ id: "folder-operations", parentId: null, name: "Operations", normalizedName: "operations", createdAt: "", lastModifiedAt: "" }), []);
  const definition = (id: string, name: string, folderId: string | null = null) => ({
    id, name, description: "Browser workflow", createdAt: "2026-07-19T00:00:00Z", lastModifiedAt: movedRef.current ? "2026-07-19T00:01:00Z" : "2026-07-19T00:00:00Z",
    latestVersion: "1.0.0", versionCount: 1, draftId: null, deletedAt: null, folderId
  });
  const context = useMemo(() => ({
    baseUrl: "browser-move-definitions",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") return { capabilities: [{
          id: "elsa.api.workflow-design", contractVersion: "1", links: [
            { rel: "workflow-definitions-page", href: "browser/definition-pages" },
            { rel: "workflow-folders", href: "browser/folders" },
            { rel: "workflow-definition-folder-move", href: "browser/definition-placement" }
          ]
        }] };
        if (url.startsWith("/browser/folders/folder-operations")) return { folder, ancestors: [] };
        if (url.startsWith("/browser/folders")) return { items: [folder], nextContinuationToken: null };
        if (url.startsWith("/browser/definition-pages")) {
          (window as Window & { capabilityRequests?: string[] }).capabilityRequests = [
            ...((window as Window & { capabilityRequests?: string[] }).capabilityRequests ?? []),
            url
          ];
          const query = new URL(url, window.location.origin).searchParams;
          const folderId = query.get("folderId");
          const unfiled = query.get("unfiled") === "true";
          const next = query.get("continuationToken");
          const movedId = movedDefinitionIdRef.current ?? "definition-browser";
          const movedName = movedId === "folder-definition-2" ? "Moved folder workflow" : "Moved workflow";
          if (folderId && moveDefinitionsFolderSourceFixture) {
            if (movedRef.current) return {
              items: [
                definition("folder-definition-1", "Folder remaining workflow", folder.id),
                ...(destinationRef.current === folderId ? [definition(movedId, movedName, folder.id)] : [])
              ],
              nextContinuationToken: null
            };
            return next
              ? { items: [definition("folder-definition-2", "Folder page 2 workflow", folder.id)], nextContinuationToken: null }
              : { items: [definition("folder-definition-1", "Folder page 1 workflow", folder.id)], nextContinuationToken: "folder-page-2" };
          }
          if (folderId) return { items: destinationRef.current === folderId ? [definition(movedId, movedName, folderId)] : [], nextContinuationToken: null };
          if (unfiled) return { items: destinationRef.current === null ? [definition(movedId, movedName)] : [], nextContinuationToken: null };
          if (movedRef.current) return { items: [definition(movedId, movedName)], nextContinuationToken: null };
          return next ? { items: [definition("definition-2", "Second page workflow")], nextContinuationToken: null } : { items: [definition("definition-1", "First page workflow")], nextContinuationToken: "page-2" };
        }
        throw new Error(`Unexpected browser fixture request: ${url}`);
      },
      postJson: async (url: string, body: unknown) => {
        (window as Window & { moveRequests?: unknown[] }).moveRequests = [...((window as Window & { moveRequests?: unknown[] }).moveRequests ?? []), { url, body }];
        if (moveDefinitionsFailureFixture) throw new Error("Destination is currently unavailable.");
        const placement = body as { definitionIds: string[]; folderId: string | null };
        movedDefinitionIdRef.current = placement.definitionIds[0];
        destinationRef.current = placement.folderId;
        movedRef.current = true;
        setMoved(true);
        return {};
      }
    }
  }) as unknown as StudioEndpointContext, [folder]);
  const ai = useMemo(() => ({ promptActions: { list: () => [] }, dispatchPrompt: () => undefined }) as unknown as StudioAiContributionApi, []);

  return <main className="wf-editor browser-fixture" data-moved={moved}>
    <h1>Workflow definitions</h1>
    <WorkflowDefinitions context={context} ai={ai} onOpen={() => undefined} />
  </main>;
}

function FolderRestructureFixture() {
  const [, setRevision] = useState(0);
  const foldersRef = useRef([
    { id: "folder-platform", parentId: null as string | null, name: "Platform", normalizedName: "platform", createdAt: "", lastModifiedAt: "" },
    { id: "folder-operations", parentId: "folder-platform" as string | null, name: "Operations", normalizedName: "operations", createdAt: "", lastModifiedAt: "" },
    { id: "folder-descendant", parentId: "folder-operations" as string | null, name: "Private descendant", normalizedName: "private descendant", createdAt: "", lastModifiedAt: "" },
    { id: "folder-archive", parentId: null as string | null, name: "Archive", normalizedName: "archive", createdAt: "", lastModifiedAt: "" },
    { id: "folder-empty", parentId: "folder-archive" as string | null, name: "Empty folder", normalizedName: "empty folder", createdAt: "", lastModifiedAt: "" }
  ]);
  const capabilityAbsent = searchParams.get("capabilities") === "absent";
  const folderCapabilityAbsent = searchParams.get("capabilities") === "folders-absent";
  const continuationPaging = searchParams.get("paging") === "continuation";
  const rejectedOperation = searchParams.get("failure");
  const definition = useCallback((id: string, name: string, folderId: string | null = null, folderBreadcrumb?: { id: string; name: string }[]) => ({
    id, name, description: "Browser workflow", createdAt: "2026-07-19T00:00:00Z", lastModifiedAt: "2026-07-19T00:00:00Z",
    latestVersion: "1.0.0", versionCount: 1, draftId: null, deletedAt: null, folderId, ...(folderBreadcrumb ? { folderBreadcrumb } : {})
  }), []);
  const findFolder = useCallback((id: string) => foldersRef.current.find(folder => folder.id === id), []);
  const ancestorsOf = useCallback((folder: NonNullable<ReturnType<typeof findFolder>>) => {
    const result: typeof foldersRef.current = [];
    let parentId = folder.parentId;
    while (parentId) {
      const parent = findFolder(parentId);
      if (!parent) break;
      result.unshift(parent);
      parentId = parent.parentId;
    }
    return result;
  }, [findFolder]);

  setDialogs({
    confirm: async options => {
      (window as Window & { folderConfirmations?: unknown[] }).folderConfirmations = [
        ...((window as Window & { folderConfirmations?: unknown[] }).folderConfirmations ?? []),
        options
      ];
      return true;
    },
    prompt: async () => null,
    alert: async () => undefined
  });

  const context = useMemo(() => ({
    baseUrl: "browser-folder-restructure",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") return { capabilities: [{
          id: "elsa.api.workflow-design", contractVersion: "1", links: [
            { rel: "workflow-definitions-page", href: "browser/restructure/definition-pages" },
            ...(folderCapabilityAbsent ? [] : [
              { rel: "workflow-folders", href: "browser/restructure/folders" }
            ]),
            ...(capabilityAbsent ? [] : [
              { rel: "workflow-folder-rename", href: "browser/restructure/folders/{folderId}/rename", templated: true },
              { rel: "workflow-folder-move", href: "browser/restructure/folders/{folderId}/move", templated: true },
              { rel: "workflow-folder-delete-empty", href: "browser/restructure/folders/{folderId}", templated: true }
            ])
          ]
        }] };
        if (url.startsWith("/browser/restructure/folders?")) {
          const query = new URL(url, window.location.origin).searchParams;
          const parentId = query.get("parentId");
          if (continuationPaging && !parentId) {
            return query.get("continuationToken") === "root-next"
              ? { items: foldersRef.current.filter(folder => folder.id === "folder-archive"), nextContinuationToken: null }
              : { items: foldersRef.current.filter(folder => folder.id === "folder-platform"), nextContinuationToken: "root-next" };
          }
          return { items: foldersRef.current.filter(folder => folder.parentId === parentId), nextContinuationToken: null };
        }
        if (url.startsWith("/browser/restructure/folders/")) {
          const id = decodeURIComponent(url.slice("/browser/restructure/folders/".length));
          const folder = findFolder(id);
          return folder ? { folder, ancestors: ancestorsOf(folder) } : null;
        }
        if (url.startsWith("/browser/restructure/definition-pages")) {
          (window as Window & { browseRequests?: string[] }).browseRequests = [
            ...((window as Window & { browseRequests?: string[] }).browseRequests ?? []),
            url
          ];
          const query = new URL(url, window.location.origin).searchParams;
          const folderId = query.get("folderId");
          const items = folderId === "folder-operations"
            ? [definition("definition-operations", "Operations workflow", folderId)]
            : folderId === "folder-platform"
              ? [definition("definition-platform", "Platform workflow", folderId)]
              : folderId === "folder-archive"
                ? [definition("definition-archive", "Archive workflow", folderId)]
                : folderId === "folder-empty"
                  ? []
                  : [definition("definition-all", "All workflow", "folder-operations", [
                    { id: "folder-platform", name: "Platform" },
                    { id: "folder-operations", name: "Operations" }
                  ])];
          return { items, nextContinuationToken: null };
        }
        throw new Error(`Unexpected browser fixture request: ${url}`);
      },
      postJson: async (url: string, body: unknown) => {
        (window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests = [
          ...((window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests ?? []),
          { method: "POST", url, body }
        ];
        const rename = url.match(/^\/browser\/restructure\/folders\/([^/]+)\/rename$/);
        if (rename) {
          if (rejectedOperation === "rename") throw new Error("Rename rejected by the server.");
          const folder = findFolder(decodeURIComponent(rename[1]));
          if (folder) {
            folder.name = (body as { name: string }).name;
            folder.normalizedName = folder.name.toLocaleLowerCase();
          }
          setRevision(current => current + 1);
          return {};
        }
        const move = url.match(/^\/browser\/restructure\/folders\/([^/]+)\/move$/);
        if (move) {
          if (rejectedOperation === "move") throw new Error("Move rejected by the server.");
          const folder = findFolder(decodeURIComponent(move[1]));
          if (folder) folder.parentId = (body as { parentId: string | null }).parentId;
          setRevision(current => current + 1);
          return {};
        }
        throw new Error(`Unexpected browser fixture POST: ${url}`);
      },
      deleteJson: async (url: string) => {
        (window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests = [
          ...((window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests ?? []),
          { method: "DELETE", url }
        ];
        if (rejectedOperation === "delete") throw new Error("Folder is not empty.");
        const id = decodeURIComponent(url.slice("/browser/restructure/folders/".length));
        foldersRef.current = foldersRef.current.filter(folder => folder.id !== id);
        setRevision(current => current + 1);
        return {};
      }
    }
  }) as unknown as StudioEndpointContext, [ancestorsOf, capabilityAbsent, continuationPaging, definition, findFolder, folderCapabilityAbsent, rejectedOperation]);
  const ai = useMemo(() => ({ promptActions: { list: () => [] }, dispatchPrompt: () => undefined }) as unknown as StudioAiContributionApi, []);

  return <main className="wf-editor browser-fixture">
    <h1>Workflow folders</h1>
    <WorkflowDefinitions context={context} ai={ai} onOpen={() => undefined} />
  </main>;
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
  const [persistenceStatus, setPersistenceStatus] = useState("");
  const [error, setError] = useState("");
  const selectedCatalogItem = palette.find(item => item.activityVersionId === selected?.activityVersionId) ?? null;
  const selectedRecommendation = recommendations.find(item => item.definitionId === selectedCatalogItem?.activityDefinitionId) ?? null;
  const selectedVersion = useFullActivityDefinitionVersion(
    endpointContext,
    selected?.activityVersionId ?? null,
    Boolean(selectedCatalogItem?.activityDefinitionId)
  );
  const groups = useMemo(() => palette.length > 0 ? [{
    category: palette[0].category,
    activities: palette
  }] : [], [palette]);

  useEffect(() => {
    Promise.all([listActivities(endpointContext), listRecommendedActivityDefinitions(endpointContext)]).then(
      ([catalog, nextRecommendations]) => {
        setRecommendations(nextRecommendations);
        const decoratedCatalog = decorateReusableCatalog(catalog.activities ?? [], nextRecommendations);
        setPalette(projectRecommendedPalette(decoratedCatalog, nextRecommendations));
      },
      () => setError("The reusable activity picker is unavailable.")
    );
  }, []);

  const place = async (activity: ActivityCatalogItem) => {
    setError("");
    setPersistenceStatus("Saving exact draft…");
    try {
      await updateDraft(endpointContext, {
        id: "workflow-draft-1",
        definitionId: "workflow-definition-1",
        state: { rootActivity: createActivityNode(activity, "invoice-boundary") },
        layout: [],
        validationErrors: []
      });
      const reloaded = await getDraft(endpointContext, "workflow-draft-1");
      setSelected(reloaded.state.rootActivity ?? null);
      setRunActivity(null);
      setPersistenceStatus("Draft saved and reloaded");
    } catch {
      setPersistenceStatus("");
      setError("The exact workflow draft could not be persisted.");
    }
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
        activityType: selectedCatalogItem?.activityTypeKey ?? "",
        activityTypeVersion: selectedCatalogItem?.version ?? "",
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
      {persistenceStatus ? <p role="status">{persistenceStatus}</p> : null}
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
            <button
              type="button"
              className="wf-node"
              data-icon="reusable"
              aria-label={`${getActivityDisplay(palette[0])} exact version ${selectedCatalogItem?.activityDefinitionVersion}`}
              title={`Exact version ${selectedCatalogItem?.activityDefinitionVersion}`}
            >
              <strong>{getActivityDisplay(palette[0])}</strong>
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
              selectedReusableDefinitionId={selectedCatalogItem?.activityDefinitionId}
              selectedReusableSemanticVersion={selectedCatalogItem?.activityDefinitionVersion}
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

function VersionChangeFixture() {
  const [draft, setDraft] = useState<WorkflowDraft>(() => versionChangeDraft());
  const [reviewing, setReviewing] = useState(false);
  const [message, setMessage] = useState("");
  const occurrence = findActivityOccurrence(draft.state.rootActivity, "invoice-one")!;

  useEffect(() => {
    if (!reviewing || searchParams.get("stale") !== "true") return;
    setDraft(current => ({
      ...current,
      state: { ...current.state, strategyOptions: { locallyEditedAfterReview: true } }
    }));
  }, [reviewing]);

  return (
    <main className="wf-page browser-reusable-journey">
      <header className="wf-page-header">
        <div><span className="wf-kicker">Workflow authoring</span><h1>Exact version change</h1></div>
      </header>
      {message ? <p role="status">{message}</p> : null}
      <button type="button" onClick={() => {
        setMessage("");
        setReviewing(true);
      }}>Change exact version</button>
      <section aria-label="Workflow draft occurrences">
        {["invoice-one", "invoice-two", "other"].map(nodeId => {
          const node = findActivityOccurrence(draft.state.rootActivity, nodeId)!;
          return <p key={nodeId} data-testid={nodeId}>{nodeId}: {node.activityVersionId}</p>;
        })}
      </section>
      {reviewing ? (
        <ActivityVersionChangeDialog
          context={endpointContext}
          draft={draft}
          occurrence={occurrence}
          current={browserVersion("version-1", "1.0.0", ["Amount", "Legacy"], ["Done", "Rejected"])}
          recommendation={{
            definitionId: "activity-def-browser",
            activityTypeKey: "Contoso.Invoice",
            category: "Browser tests",
            displayName: "Invoice",
            versionId: "version-2",
            version: "2.0.0",
            isAvailable: true
          }}
          onCancel={() => setReviewing(false)}
          onApply={async request => {
            const stale = validateActivityVersionChangePrecondition(draft, request.precondition);
            if (stale) throw new Error(stale);
            const proposed = applyActivityVersionChange(
              draft,
              request.precondition.occurrenceId,
              request.precondition.fromVersionId,
              request.targetVersionId,
              request.scope
            );
            const saved = await updateDraft(endpointContext, proposed);
            setDraft(saved);
            setReviewing(false);
            setMessage("Authoritative exact version change applied");
          }}
        />
      ) : null}
    </main>
  );
}

function PublicationReviewFixture() {
  const [message, setMessage] = useState("");
  const [review, setReview] = useState<PublicationReviewState>(() => publicationReview());
  const outcome = searchParams.get("outcome") ?? "success";

  const refreshReview = async (
    _review: PublicationReviewState,
    intent: PublicationIntent,
    versionSelection: PublicationVersionSelection
  ) => {
    await new Promise(resolve => window.setTimeout(resolve, 60));
    setReview(current => ({
      ...current,
      intent,
      versionSelection,
      reviewPending: false,
      preflight: {
        ...current.preflight!,
        slotName: intent.slotName,
        resolvedAction: intent.action,
        policySource: "request",
        policyRevision: 12
      },
      versionPreflight: {
        isReady: true,
        assignmentMode: versionSelection.mode,
        requestedVersion: versionSelection.mode === "exact" ? versionSelection.requestedVersion.trim() : null,
        resolvedVersion: versionSelection.mode === "exact" ? versionSelection.requestedVersion.trim() : "2.0.0",
        latestVersion: "1.4.0",
        issues: []
      }
    }));
  };

  const publish = async (intent: PublicationIntent, versionSelection: PublicationVersionSelection = { mode: "automatic" }) => {
    if (outcome === "recovery") {
      setReview(current => ({
        ...current,
        phase: "partialFailure",
        intent,
        versionSelection,
        proposedVersion: versionSelection.mode === "exact" ? versionSelection.requestedVersion : "2.0.0",
        promotedVersionId: "version-2",
        failureMessage: "The activation request timed out. The promoted version was retained and can be retried without another promotion."
      }));
      return;
    }
    setReview(current => ({
      ...current,
      phase: "success",
      intent,
      versionSelection,
      proposedVersion: versionSelection.mode === "exact" ? versionSelection.requestedVersion : "2.0.0",
      promotedVersionId: "version-2",
      published: {
        publicationId: "publication-2",
        definitionId: "definition-browser",
        versionId: "version-2",
        artifactId: "executable-browser-2",
        slotName: intent.slotName,
        sourceReferenceId: "source-reference-browser-2",
        status: "active"
      }
    }));
  };

  return (
    <main className="wf-editor browser-publication-workbench">
      <header><span>Studio / Workflows</span><h1>Orders workflow</h1></header>
      <section aria-label="Workflow canvas"><p>Designer canvas</p></section>
      {message ? <p role="status">{message}</p> : null}
      <PublicationReviewDialog
        review={review}
        busy={false}
        onReview={refreshReview}
        onPublish={publish}
        onCancel={() => setMessage("Publication review closed")}
        onOpenPublishedExecutable={() => setMessage("Published executable opened")}
      />
    </main>
  );
}

function publicationReview(): PublicationReviewState {
  const draft: WorkflowDraft = {
    id: "draft-browser",
    definitionId: "definition-browser",
    sourceVersionId: "version-default",
    state: {
      rootActivity: {
        nodeId: "root",
        activityVersionId: "flowchart",
        inputs: [],
        outputs: [],
        structure: {
          kind: "Flowchart",
          schemaVersion: "1",
          payload: {
            activities: [
              { nodeId: "write", activityVersionId: "write-line", inputs: [], outputs: [] },
              { nodeId: "http", activityVersionId: "http-endpoint", inputs: [], outputs: [] }
            ]
          }
        }
      },
      inputs: [{ name: "orderId" }],
      outputs: [{ name: "result" }]
    },
    layout: [],
    validationErrors: []
  };
  const slots = [
    {
      definitionId: "definition-browser",
      slotName: "default",
      status: "active" as const,
      publication: {
        publicationId: "publication-default",
        definitionId: "definition-browser",
        versionId: "version-default",
        artifactId: "artifact-default",
        artifactVersion: "1.4.0",
        slotName: "default",
        sourceReferenceId: "reference-default",
        status: "active" as const
      }
    },
    {
      definitionId: "definition-browser",
      slotName: "blue",
      status: "active" as const,
      publication: {
        publicationId: "publication-blue",
        definitionId: "definition-browser",
        versionId: "version-blue",
        artifactId: "artifact-blue",
        artifactVersion: "1.3.0",
        slotName: "blue",
        sourceReferenceId: "reference-blue",
        status: "active" as const
      }
    }
  ];
  const review = createPublicationReview({
    draft,
    details: null,
    slotVersions: {},
    policy: { defaultAction: "replace", defaultSlotName: "default", source: "host" },
    slots,
    catalog: []
  });
  return {
    ...review,
    exactVersionSupported: searchParams.get("exact") !== "unsupported",
    versionPreflightSupported: searchParams.get("exact") !== "unsupported",
    proposedVersion: "2.0.0",
    preflight: {
      preflightToken: "browser-preflight",
      candidateHash: "browser-candidate",
      definitionId: "definition-browser",
      versionId: null,
      slotName: "default",
      resolvedAction: "replace",
      policySource: "host",
      policyRevision: 12,
      canActivate: true,
      claims: [{ key: "http:/orders", cardinality: "exclusive" }],
      triggers: [{ change: "added", key: "http:/orders", cardinality: "exclusive" }],
      conflicts: []
    },
    versionPreflight: {
      isReady: true,
      assignmentMode: "automatic",
      requestedVersion: null,
      resolvedVersion: "2.0.0",
      latestVersion: "1.4.0",
      issues: []
    }
  };
}

function versionChangeDraft(): WorkflowDraft {
  const activity = (nodeId: string, activityVersionId: string): ActivityNode => ({
    nodeId,
    activityVersionId,
    inputs: [
      { referenceKey: "Amount", value: { expressionType: "Literal", value: "42" } },
      { referenceKey: "Legacy", value: { expressionType: "Literal", value: "kept-unresolved" } }
    ],
    outputs: []
  });
  return {
    id: "workflow-draft-version-change",
    definitionId: "workflow-definition-version-change",
    sourceVersionId: "published-workflow-version",
    state: {
      rootActivity: {
        nodeId: "root",
        activityVersionId: "flowchart",
        inputs: [],
        outputs: [],
        structure: {
          kind: "Flowchart",
          schemaVersion: "1",
          payload: {
            activities: [
              activity("invoice-one", "version-1"),
              activity("invoice-two", "version-1"),
              activity("other", "other-version")
            ],
            connections: [
              { id: "done", source: { nodeId: "invoice-one", port: "Done" }, target: { nodeId: "other" } },
              { id: "rejected", source: { nodeId: "invoice-one", port: "Rejected" }, target: { nodeId: "invoice-two" } }
            ]
          }
        }
      }
    },
    layout: [],
    validationErrors: []
  };
}

function browserVersion(
  versionId: string,
  version: string,
  inputs: string[],
  outcomes: string[]
): ActivityDefinitionVersionView {
  return {
    definition: {
      definitionId: "activity-def-browser",
      activityTypeKey: "Contoso.Invoice",
      category: "Browser tests",
      displayName: "Invoice",
      contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design" }
    },
    versionId,
    version,
    contract: {
      contractSchemaVersion: "1",
      inputs: inputs.map(referenceKey => ({
        referenceKey,
        name: referenceKey,
        type: { alias: "String", collectionKind: "None" },
        isRequired: false,
        isNullable: true,
        default: null,
        storageDriverKey: "Workflow",
        durability: "Durable"
      })),
      outputs: [],
      outcomes: outcomes.map(referenceKey => ({ referenceKey, name: referenceKey, isEmitted: true }))
    },
    provider: { providerKey: "ActivityGraph", schemaVersion: "1", manifestFingerprint: "browser" },
    lifecycle: "Active",
    publishedAt: "2026-07-19T00:00:00Z"
  };
}

const theme = searchParams.get("theme");
document.documentElement.dataset.theme = theme === "black-glass" ? "black-glass" : "harbor";
document.documentElement.dataset.themeMode = theme === "black-glass" ? "dark" : "light";
createRoot(document.getElementById("root")!).render(
  publicationReviewFixture
    ? <PublicationReviewFixture />
    : activityGraphAuthoringFixture
    ? <ActivityDefinitionGraphAuthoringFixture />
    : versionChangeFixture
    ? <QueryClientProvider client={queryClient}><VersionChangeFixture /></QueryClientProvider>
    : elsa3ReusableImportFixture
      ? <Elsa3ReusableImportPage context={endpointContext} navigate={path => window.history.pushState({}, "", path)} />
    : activityUpgradeFixture || activityDefinitionsFixture
    ? <ActivityDefinitionRoutesFixture />
    : reusableBoundaryFixture
      ? <QueryClientProvider client={queryClient}><ReusableBoundaryFixture /></QueryClientProvider>
      : activityInspectorTabsFixture
        ? <ActivityInspectorTabsFixture />
      : runDetailFixture
        ? <RunDetailFixture />
        : lazyBoundaryFixture
          ? <LazyBoundaryFixture />
          : folderRestructureFixture
            ? <FolderRestructureFixture />
            : moveDefinitionsFixture
              ? <MoveDefinitionsFixture />
              : <Fixture />
);
