import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences, type ThemeModePreference } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import React, { useMemo, useRef, useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type {
  StudioActivityDefinitionImplementationEditorProps,
  StudioAiContributionApi,
  StudioEndpointContext,
  StudioExpressionEditorContribution
} from "@elsa-workflows/studio-sdk";
import { activityGraphDiagnosticFocusEvent } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityGraphDiagnosticFocus";
import { ActivityGraphImplementationEditor } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor";
import { WorkflowEditor } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor";
import { WorkflowInstanceDetailsWorkbench } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import {
  bpmnActivity,
  flowchartActivity,
  flowchartNode,
  sequenceActivity,
  sequenceNode,
  forEachActivity,
  ifActivity,
  ifNode,
  whileActivity,
  whileNode,
  forEachNode,
  leafNode,
  writeLine
} from "../../src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/fixtures";
import { JavaScriptExpandedEditor, JavaScriptInlineEditor } from "../../../Extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/module";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import { createActivityNode, getChildSlots } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter";
import { builtInPropertyEditors } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/propertyEditors";
import { activityGraphLayoutFromDesign } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityGraphDocumentAdapter";

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
const bpmnDecisionFixture = new URLSearchParams(window.location.search).get("bpmn-decision");
const persistEditor = new URLSearchParams(window.location.search).has("adaptive-ports") || !!bpmnDecisionFixture;
const adaptivePorts = new URLSearchParams(window.location.search).get("adaptive-ports");
const adaptiveStorageKey = `adaptive-ports:${window.location.search}`;

const writeLineCatalog: ActivityCatalogItem = {
  ...writeLine,
  inputs: [{
    referenceKey: "text",
    name: "Text",
    typeName: "System.String",
    displayName: new URLSearchParams(window.location.search).has("long-label")
      ? "Message to include when notifying the customer about their completed order"
      : "Text",
    description: "Text to write.",
    order: 0,
    category: "General",
    isBrowsable: true,
    uiHint: "singleline",
    isWrapped: true,
    defaultSyntax: "Literal"
  }],
  ports: adaptivePorts === "branches" ? [
    { type: "outcome", name: "True", displayName: "True", isBrowsable: true },
    { type: "outcome", name: "False", displayName: "False", isBrowsable: true }
  ] : [{ name: "Done", displayName: "Done", isBrowsable: true }],
  available: true
};
const denseOutcomeCatalog: ActivityCatalogItem = {
  ...writeLineCatalog,
  activityVersionId: "dense-outcomes-v1",
  ports: Array.from({ length: 30 }, (_, index) => ({ type: "outcome", name: `Case ${index + 1}`, displayName: `Case ${index + 1}` }))
};

const workflowStartFixture = new URLSearchParams(window.location.search).has("workflow-start");
const httpEndpointCatalog: ActivityCatalogItem = {
  ...writeLineCatalog,
  activityVersionId: "http-endpoint-v1",
  activityTypeKey: "Elsa.HttpEndpoint",
  displayName: "Http Endpoint",
  inputs: [
    { name: "CanStartWorkflow", referenceKey: "CanStartWorkflow", displayName: "Start new workflows", typeName: "System.Boolean", isWrapped: true, defaultSyntax: "Literal", isBrowsable: true, tier: "Setting" },
    { name: "Path", referenceKey: "Path", displayName: "Path", typeName: "System.String", isWrapped: true, defaultSyntax: "Literal", isBrowsable: true }
  ]
};

const notifyBuyer: ActivityNode = workflowStartFixture ? {
  nodeId: "write-one", activityVersionId: "http-endpoint-v1", outputs: [],
  inputs: [
    { referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: "true" } },
    { referenceKey: "Path", value: { expressionType: "Literal", value: "/hello" } }
  ]
} : {
  ...leafNode("write-one"),
  inputs: [{
    referenceKey: "text",
    value: { value: "buyer.email", expressionType: "JavaScript" }
  }]
};

function editorDraft(): WorkflowDraft {
  const composition = new URLSearchParams(window.location.search).get("composition");
  const sequence = composition === "sequence";
  const root = composition === "bpmn" ? createActivityNode(bpmnActivity, "bpmn-root") : sequence
    ? sequenceRootActivity()
    : flowchartNode("flow-root", [notifyBuyer, leafNode("write-two")]);
  if (!sequence && composition !== "bpmn") root.structure!.payload.connections = [{
    id: "flow-write-one-write-two",
    source: { nodeId: "write-one", port: "Done" },
    target: { nodeId: "write-two" }
  }];
  if (adaptivePorts && !sequence) {
    if (composition === "bpmn") {
      root.structure!.payload = { activities: [notifyBuyer, leafNode("write-two")],
        elements: [
          { elementId: "write-one", elementType: "task", childNodeId: "write-one" },
          { elementId: "write-two", elementType: "task", childNodeId: "write-two" }
        ], sequenceFlows: [{ flowId: "flow-write-one-write-two", sourceRef: "write-one", targetRef: "write-two" }] };
    } else {
      root.structure!.payload.activities = [adaptivePorts === "dense" ? { ...notifyBuyer, activityVersionId: denseOutcomeCatalog.activityVersionId } : notifyBuyer, leafNode("write-two"), leafNode("write-three")];
      root.structure!.payload.connections = [{ id: "flow-write-one-write-two",
        source: { nodeId: "write-one", port: adaptivePorts === "branches" ? "True" : "Done" }, target: { nodeId: "write-two" } },
      ...(adaptivePorts === "branches" ? [{ id: "flow-false", source: { nodeId: "write-one", port: "False" }, target: { nodeId: "write-three" } }] : [])];
      if (adaptivePorts === "dense") root.structure!.payload.connections = Array.from({ length: 30 }, (_, index) => ({
        id: `flow-case-${index + 1}`, source: { nodeId: "write-one", port: `Case ${index + 1}` }, target: { nodeId: "write-two" }
      }));
    }
  }
  if (bpmnDecisionFixture) root.structure!.payload = {
    activities: bpmnDecisionFixture === "unbound" ? [] : [{ nodeId: "decision", activityVersionId: "decision-v1", inputs: [], outputs: [], structure: null }],
    elements: [
      { elementId: "decision-element", elementType: bpmnDecisionFixture === "legacy" ? "task" : "exclusiveGateway",
        ...(bpmnDecisionFixture !== "unbound" ? { childNodeId: "decision" } : {}) },
      { elementId: "high-end", elementType: "endEvent", name: "High" },
      { elementId: "low-end", elementType: "endEvent", name: "Low" }
    ],
    sequenceFlows: [
      { flowId: "high-flow", sourceRef: "decision-element", targetRef: "high-end" },
      { flowId: "low-flow", sourceRef: "decision-element", targetRef: "low-end" }
    ]
  };
  return {
    id: "draft-1",
    definitionId: "definition-1",
    state: { variables: [], rootActivity: root, inputs: [], outputs: [] },
    layout: bpmnDecisionFixture ? [
      { nodeId: "decision-element", x: 220, y: 200 }, { nodeId: "high-end", x: 480, y: 80 }, { nodeId: "low-end", x: 480, y: 320 }
    ] : adaptivePorts ? [
      { nodeId: "write-one", x: 200, y: 80 },
      { nodeId: "write-two", x: adaptivePorts === "offset" ? 305 : 200, y: adaptivePorts === "offset" ? 430 : adaptivePorts === "dense" ? 1100 : 320 },
      { nodeId: "write-three", x: 600, y: 80 }
    ] : [
      { nodeId: "write-one", x: 420, y: 180 },
      { nodeId: "write-two", x: 720, y: 180 }
    ],
    activityPresentation: [{ nodeId: "write-one", displayName: workflowStartFixture ? "Http Endpoint" : "Notify buyer" }],
    validationErrors: []
  };
}

const rootSlots = new URLSearchParams(window.location.search).get("root-slots");
const slotAssignment = new URLSearchParams(window.location.search).get("slot-assignment");
function sequenceRootActivity(): ActivityNode {
  if (rootSlots) return ifNode("if-root", rootSlots === "empty" ? null : leafNode("then-sibling"), rootSlots === "filled" ? sequenceNode("else-sequence", [leafNode("else-leaf")]) : null);
  if (slotAssignment) {
    const loop = whileNode("while", null);
    return slotAssignment === "root" ? loop : sequenceNode("sequence-root", [loop]);
  }
  return sequenceNode("sequence-root", [
    notifyBuyer,
    forEachNode("loop", sequenceNode("body", [leafNode("nested")])),
    leafNode("write-two")
  ]);
}

function activityGraphSequenceValue(): StudioActivityDefinitionImplementationEditorProps["value"] {
  if (persistEditor) {
    const draft = editorDraft();
    return { payload: { rootActivity: draft.state.rootActivity, variables: [], outputMappings: [], outcomeMappings: [] }, layout: activityGraphLayoutFromDesign(draft.layout) };
  }
  return {
    payload: {
      rootActivity: new URLSearchParams(window.location.search).get("composition") === "bpmn" ? createActivityNode(bpmnActivity, "bpmn-root") : sequenceRootActivity(),
      variables: [],
      outputMappings: [],
      outcomeMappings: []
    },
    layout: []
  };
}

const expressionEditors: StudioExpressionEditorContribution[] = [{
  id: "browser.javascript-expression-editor",
  supports: context => context.syntax === "JavaScript",
  surfaces: { inline: JavaScriptInlineEditor, expanded: JavaScriptExpandedEditor }
}];

const paletteTheme = new URLSearchParams(window.location.search).get("palette-theme");
if (paletteTheme) writeAppearancePreferences({ themeId: paletteTheme, mode: (new URLSearchParams(window.location.search).get("appearance") ?? "light") as ThemeModePreference, navigation: "left" });

export function ActivityDuplicateEditorFixture() {
  return paletteTheme ? <ThemeProvider><ActivityDuplicateEditorFixtureContent /></ThemeProvider> : <ActivityDuplicateEditorFixtureContent />;
}

function ActivityDuplicateEditorFixtureContent() {
  const instanceHost = new URLSearchParams(window.location.search).get("host") === "instance";
  const motionFixture = new URLSearchParams(window.location.search).has("execution-motion");
  const live = useRef({ status: "Running", disconnected: false });
  const context = useMemo(() => createEditorContext(instanceHost || motionFixture ? live.current : undefined), [instanceHost, motionFixture]);
  const activityHost = new URLSearchParams(window.location.search).get("host") === "activity";
  const ai = useMemo(() => ({
    promptActions: { list: () => [] },
    providerAvailability: { get: () => false, subscribe: () => () => undefined },
    dispatchPrompt: () => undefined
  } as unknown as StudioAiContributionApi), []);

  return (
    <div className={`activity-duplicate-editor${instanceHost ? " wf-page wf-page--run-workbench" : ""}`} data-testid="activity-duplicate-editor">
      {instanceHost || motionFixture ? <div aria-label="Fixture runtime controls"><button onClick={() => { live.current.status = "Completed"; }}>Complete fixture execution</button>
        <button onClick={() => { live.current.disconnected = true; }}>Disconnect fixture reads</button><button onClick={() => { live.current.disconnected = false; }}>Reconnect fixture reads</button></div> : null}
      <QueryClientProvider client={queryClient}>
        {instanceHost ? <WorkflowInstanceDetailsWorkbench context={context} ai={ai} workflowExecutionId="editor-execution" expressionEditors={expressionEditors} navigate={() => undefined} /> : activityHost
          ? <ControlledActivityGraphFixture context={context} />
          : <WorkflowEditor
              context={context}
              definitionId="definition-1"
              ai={ai}
              propertyEditors={workflowStartFixture ? builtInPropertyEditors : []}
              expressionEditors={expressionEditors}
              runInputEditors={[]}
              workflowDesignerPanels={[]}
              autosaveEnabledByDefault={false}
              onBack={() => undefined}
            />}
      </QueryClientProvider>
    </div>
  );
}

function ControlledActivityGraphFixture({ context }: { context: StudioEndpointContext }) {
  const [value, setValue] = useState<StudioActivityDefinitionImplementationEditorProps["value"]>(() => persistEditor && sessionStorage.getItem(adaptiveStorageKey)
    ? JSON.parse(sessionStorage.getItem(adaptiveStorageKey)!) : activityGraphSequenceValue());
  const readOnly = new URLSearchParams(window.location.search).has("read-only");
  const [diagnosticResult, setDiagnosticResult] = useState("");
  return <><ActivityGraphImplementationEditor
    context={context}
    definitionId="definition-1"
    draftId="draft-1"
    revision={1}
    providerKey="elsa.activity-graph"
    providerSchemaVersion="2"
    manifestFingerprint="sha256:browser-activity-duplicate"
    contract={{ contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] }}
    propertyEditors={[]}
    expressionEditors={expressionEditors}
    graphAuthoringPanels={[]}
    historyResetKey="draft-1:activity-sequence"
    value={value}
    readOnly={readOnly}
    onChange={next => { setValue(next); if (persistEditor) sessionStorage.setItem(adaptiveStorageKey, JSON.stringify(next)); }}
  />{rootSlots && readOnly ? <><button onClick={() => document.querySelector("[data-provider-diagnostic-focus-root]")!.dispatchEvent(new CustomEvent(activityGraphDiagnosticFocusEvent, {
    cancelable: true, detail: { location: { referenceKey: "else-leaf" }, complete: (result: { kind: string }) => setDiagnosticResult(result.kind) }
  }))}>Focus Else diagnostic</button><output data-testid="diagnostic-result">{diagnosticResult}</output></> : null}{slotAssignment || rootSlots ? <output hidden data-testid="activity-graph-payload">{JSON.stringify(value.payload)}</output> : null}</>;
}

export function createEditorContext(live?: { status: string; disconnected: boolean }): StudioEndpointContext {
  let draft = persistEditor && new URLSearchParams(window.location.search).get("host") !== "activity" && sessionStorage.getItem(adaptiveStorageKey)
    ? JSON.parse(sessionStorage.getItem(adaptiveStorageKey)!) as WorkflowDraft : editorDraft();
  const runCanvas = new URLSearchParams(window.location.search).get("run-canvas");
  const motionFixture = new URLSearchParams(window.location.search).has("execution-motion");
  const paletteFixtures: ActivityCatalogItem[] = new URLSearchParams(window.location.search).has("palette-context") ? [
    bpmnActivity,
    { ...writeLineCatalog, activityVersionId: "decision-v1", activityTypeKey: "Elsa.BpmnDecision", displayName: "BPMN Decision", category: "BPMN", inputs: [{
      referenceKey: "Outcome", name: "Outcome", typeName: "System.String", displayName: "Outcome", order: 0,
      isBrowsable: true, isWrapped: true, defaultSyntax: "Literal"
    }], paletteHints: { containerModes: ["bpmn"], description: "BPMN Decision is intended for a gateway in a BPMN Process." } },
    { ...writeLineCatalog, activityVersionId: "complete-v1", activityTypeKey: "Elsa.Complete", displayName: "Complete", paletteHints: { resourceKinds: ["activity-definition-graph"] } }
  ] : [];
  const catalog = [writeLineCatalog, flowchartActivity, sequenceActivity, forEachActivity, ...(slotAssignment ? [whileActivity] : []), ...(rootSlots ? [ifActivity] : []), ...paletteFixtures, ...(adaptivePorts && !bpmnDecisionFixture ? [bpmnActivity] : []), ...(adaptivePorts === "dense" ? [denseOutcomeCatalog] : []), ...(workflowStartFixture ? [httpEndpointCatalog] : [])];
  const compiled = (node: ActivityNode): object => ({
    executableNodeId: `exec-${node.nodeId}`, authoredActivityId: node.nodeId,
    activityType: catalog.find(item => item.activityVersionId === node.activityVersionId)?.activityTypeKey ?? "unknown",
    activityTypeVersion: "1.0.0", inputBindings: [], structureKind: node.structure?.kind,
    childSlots: getChildSlots(node, catalog).map(slot => ({ name: `${catalog.find(item => item.activityVersionId === node.activityVersionId)?.activityTypeKey.split(".").at(-1)}.${slot.id.split(":").at(-1)!.replace(/^./, value => value.toUpperCase())}`, activities: slot.activities.map(compiled) })),
    ...(node.structure?.kind === "elsa.flowchart.structure" ? { connections: node.structure.payload.connections } : {})
  });
  const runtimeNodes: Array<{ node: ActivityNode; parent: string | null }> = [];
  const visit = (node: ActivityNode, parent: string | null) => {
    runtimeNodes.push({ node, parent });
    getChildSlots(node, catalog).forEach(slot => slot.activities.forEach(child => visit(child, node.nodeId)));
  };
  visit(draft.state.rootActivity!, null);
  const predecessors = new Map<string, string>();
  for (const { node } of runtimeNodes) for (const slot of getChildSlots(node, catalog))
    slot.activities.forEach((child, index) => { if (index > 0) predecessors.set(child.nodeId, slot.activities[index - 1].nodeId); });
  const records = runtimeNodes.flatMap(({ node, parent }) => Array.from({ length: runCanvas === "repeated" && node.nodeId === "write-one" ? 10 : 1 }, (_, index) => ({
    activityExecutionId: `activity-exec-${node.nodeId}${runCanvas === "repeated" && node.nodeId === "write-one" ? `-${index + 1}` : ""}`,
    workflowExecutionId: "editor-execution", executableNodeId: `exec-${node.nodeId}`, authoredActivityId: node.nodeId,
    activityType: catalog.find(item => item.activityVersionId === node.activityVersionId)?.activityTypeKey ?? "unknown", activityTypeVersion: "1.0.0",
    parentActivityExecutionId: parent ? `activity-exec-${parent}` : null,
    ...(motionFixture ? { outcomeNames: ["Done"], provenance: { parentActivityExecutionId: parent ? `activity-exec-${parent}` : null,
      schedulingActivityExecutionId: `activity-exec-${predecessors.get(node.nodeId) ?? parent}`,
      schedulingCause: draft.state.rootActivity?.structure?.kind === "elsa.sequence.structure" ? "ActivityCompletion" : "continuation" } } : {}),
    scheduledAt: "2026-10-27T10:00:00Z", status: runCanvas === "incident" && node.nodeId === "write-two" ? "Faulted" : "Completed", metadata: {}, position: index + 1
  })));
  const observedRecords = () => records.filter(record => !motionFixture || live?.status !== "Running" || record.authoredActivityId === draft.state.rootActivity?.nodeId || record.authoredActivityId === "write-one")
    .map(record => live && !motionFixture ? { ...record, status: live.status } : record);
  const incident = { incidentId: "incident-write-two", workflowExecutionId: "editor-execution", activityExecutionId: "activity-exec-write-two",
    executableNodeId: "exec-write-two", severity: "Error", status: "Active", failureType: "ActivityFailed", message: "Second activity failed in its exact execution.",
    createdAt: "2026-10-27T10:00:00Z", isBlocking: true, metadata: {} };
  const runView = { testRunId: "editor-test", definitionId: draft.definitionId, definitionVersionId: "draft-1", artifactId: "editor-artifact", workflowExecutionId: "editor-execution", status: "Dispatched" };
  return {
    baseUrl: "browser-activity-duplicate-editor",
    http: {
      getJson: async (url: string) => {
        if (live?.disconnected && url.startsWith("/runtime/")) throw new Error("Fixture connection dropped");
        if (url === "/capabilities") {
          return {
            capabilities: [
              ...(runCanvas ? [
                { id: "elsa.api.publishing", contractVersion: "1", links: [
                  { rel: "workflow-draft-test-run-options", href: "publishing/draft-options" },
                  { rel: "workflow-draft-test-runs", href: "publishing/draft-runs" },
                  { rel: "workflow-test-run-status", href: "publishing/test-runs/{testRunId}", templated: true },
                  { rel: "workflow-test-run-waits", href: "publishing/test-runs/{testRunId}/waits", templated: true }
                ] },
                { id: "elsa.api.runtime", contractVersion: "1", links: [
                  { rel: "workflow-instance", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true },
                  { rel: "workflow-executable", href: "runtime/workflows/executables/{artifactId}", templated: true },
                  { rel: "activity-execution", href: "runtime/workflows/instances/{workflowExecutionId}/activities/{activityExecutionId}", templated: true },
                  { rel: "activity-executions", href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions", templated: true }
                ] }
              ] : []),
              {
                id: "elsa.api.workflow-design",
                contractVersion: "1",
                links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" },
                  ...(persistEditor ? [{ rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true }] : [])]
              },
              {
                id: "elsa.api.activity-design",
                contractVersion: "1",
                links: [{ rel: "activity-catalog", href: "design/activities/catalog" }]
              },
              {
                id: "elsa.api.expressions",
                contractVersion: "1",
                links: [{ rel: "expression-descriptors", href: "expressions/descriptors" }]
              }
            ]
          };
        }
        if (url === "/design/workflows/definitions/definition-1") {
          return {
            definition: {
              id: "definition-1",
              name: "Order confirmation",
              description: "Notifies the buyer.",
              createdAt: "2026-10-14T00:00:00Z",
              lastModifiedAt: "2026-10-14T00:00:00Z",
              deletedAt: null,
              draftId: "draft-1",
              latestVersionId: "version-1",
              latestVersion: "1.0.0",
              versionCount: 1
            },
            draft,
            versions: []
          };
        }
        if (url === "/design/activities/catalog") {
          return { activities: catalog };
        }
        if (url === "/expressions/descriptors") {
          return {
            items: [
              { type: "Literal", displayName: "Literal", editingMode: "literal" },
              { type: "JavaScript", displayName: "JavaScript", editingMode: "text" }
            ]
          };
        }
        if (runCanvas && url === "/runtime/workflows/instances/editor-execution") return {
          instance: { workflowExecutionId: "editor-execution", artifactId: "editor-artifact", definitionId: "definition-1", definitionVersionId: "draft-1",
            createdAt: "2026-10-27T10:00:00Z", status: live?.status ?? "Completed", activityCount: 3 },
          activities: [], incidents: runCanvas === "incident" ? [incident] : [], persistenceMode: "Balanced", inspectionGranularity: "boundary-level"
        };
        if (runCanvas && url === "/runtime/workflows/executables/editor-artifact") return {
          artifactId: "editor-artifact", rootActivity: compiled(draft.state.rootActivity!),
          ...(live ? { chosenReference: { sourceReferenceId: "fixture-reference", ...(adaptivePorts ? { layout: draft.layout } : {}), activityPresentation: draft.activityPresentation?.map(item => ({ ...item, executableNodeId: `exec-${item.nodeId}` })) } } : {})
        };
        if (runCanvas && url.startsWith("/runtime/workflows/instances/editor-execution/activity-executions?")) {
          if (new URLSearchParams(window.location.search).has("evidence-latency")) await new Promise(resolve => setTimeout(resolve, 400));
          const query = new URL(url, "http://fixture").searchParams;
          const node = query.get("executableNodeId"), parent = query.get("parentActivityExecutionId"), ancestor = query.get("ancestorActivityExecutionId");
          const filtered = observedRecords().filter(record => (!node || record.executableNodeId === node) && (!parent || record.parentActivityExecutionId === parent)
            && (!ancestor || record.parentActivityExecutionId === ancestor));
          const anchor = query.get("anchorActivityExecutionId");
          const items = filtered.map((execution, index) => ({ execution, position: index + 1,
            ...(motionFixture ? { attempt: null } : {}), previousActivityExecutionId: filtered[index - 1]?.activityExecutionId ?? null, nextActivityExecutionId: filtered[index + 1]?.activityExecutionId ?? null }));
          return { totalCount: items.length, previousCursor: null, nextCursor: null,
            items: query.get("limit") === "1" ? [anchor ? items.find(item => item.execution.activityExecutionId === anchor) : items.at(-1)].filter(Boolean) : items };
        }
        if (runCanvas && url.startsWith("/runtime/workflows/instances/editor-execution/activities/")) {
          const id = decodeURIComponent(url.split("/activities/")[1]);
          const execution = observedRecords().find(record => record.activityExecutionId === id);
          if (!execution) throw new Error("Unknown activity execution");
          return { ...execution, executionSequence: execution.position,
            provenance: execution.provenance ?? { parentActivityExecutionId: execution.parentActivityExecutionId, metadata: {} },
            bookmarks: [], incidents: runCanvas === "incident" && execution.authoredActivityId === "write-two" ? [incident] : [], outcomeNames: execution.status !== "Completed" ? [] : ["Done"], valueSnapshots: [{ name: "Text", inputKey: "text", subject: "ActivityInput",
              captureMode: "DiagnosticSnapshot", snapshot: { kind: "string", preview: `Current: ${execution.position}` },
              capturedAt: execution.scheduledAt, captureReason: "Captured by fixture policy", isSensitive: false, metadata: {} },
              ...(live?.status === "Completed" ? [{ name: "Receipt", outputKey: "receipt", subject: "ActivityOutput", captureMode: "DiagnosticSnapshot",
                snapshot: { kind: "string", preview: `Completion receipt: ${execution.position}` }, capturedAt: execution.scheduledAt,
                captureReason: "Captured on completion", isSensitive: false, metadata: {} }] : [])] };
        }
        if (runCanvas && url === "/publishing/test-runs/editor-test") return runView;
        if (runCanvas && url === "/publishing/test-runs/editor-test/waits") return { waits: [] };
        throw new Error(`Unexpected editor fixture request: ${url}`);
      },
      postJson: async (url: string) => {
        if (runCanvas && url === "/publishing/draft-options") return { options: [] };
        return {};
      },
      putJson: async (_url: string, body: unknown) => {
        if (persistEditor) {
          draft = { ...draft, ...body as object };
          sessionStorage.setItem(adaptiveStorageKey, JSON.stringify(draft));
        }
        return draft;
      },
      requestJson: async (url: string) => runCanvas && url === "/publishing/draft-runs" ? runView : {},
      deleteJson: async () => undefined
    }
  } as unknown as StudioEndpointContext;
}
