import React, { useLayoutEffect, useMemo, useRef, useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDefinitionImplementationEditorProps, StudioActivityDefinitionImplementationState } from "@elsa-workflows/studio-sdk";
import { ActivityGraphImplementationEditor } from "../ActivityGraphImplementationEditor";
import { activityGraphDocumentAdapter } from "../activityGraphDocumentAdapter";
import { createAuthoringCheckpoint } from "../graph-authoring/authoringCheckpoint";
import type { ActivityGraphAuthoringHost } from "../graph-authoring/activityGraphAuthoringHost";
import type { ActivityDefinitionDraftView } from "../activityDefinitionTypes";
import type { BpmnDocumentAuthoringSurfaceProps } from "../bpmn/BpmnDocumentAuthoringSurface";
import { applyBpmnDocumentTransaction, readBpmnDocument } from "../bpmn/bpmnDocument";
import { planBpmnLabel } from "../bpmn/bpmnDocumentPropertyOperations";
import { readWrappedInput, withLiteralValue, writeInputValue } from "../activityProperties";
import { getChildSlots, findActivityNode } from "../workflowAdapter";
import type { ComponentProps } from "react";
import type { InspectorPanel } from "../workflow-editor/InspectorPanel";
import type { WorkflowDefinitionState } from "../workflowTypes";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";
const controls = vi.hoisted(() => ({ surface: null as BpmnDocumentAuthoringSurfaceProps | null, inspection: null as ComponentProps<typeof InspectorPanel> | null, queryState: null as WorkflowDefinitionState | null, realSurface: false }));
const catalog = [{ activityVersionId: "bpmn@exact", activityTypeKey: "Elsa.BpmnProcess", version: "1.0.0", category: "Composition", displayName: "BPMN Process", executionType: "Action", inputs: [], outputs: [], ports: [] },
  { activityVersionId: "sequence@exact", activityTypeKey: "Elsa.Sequence", version: "1.0.0", category: "Composition", displayName: "Sequence", executionType: "Action", inputs: [], outputs: [], ports: [] }];
vi.mock("../api/activityDesign", () => ({ useWorkflowActivities: () => ({ data: { activities: catalog }, isPending: false, isError: false }) }));
vi.mock("../api/workflowDesign", () => ({ useScopedVariableAnalysis: (_context: unknown, state: WorkflowDefinitionState) => { controls.queryState = state; return { visibleVariables: [], shadowingWarnings: [], status: "ready" }; } }));
const headerInput = { name: "HeaderValue", referenceKey: "header-value", typeName: "String" };
vi.mock("../workflow-editor/InspectorPanel", () => ({ InspectorPanel: (props: ComponentProps<typeof InspectorPanel>) => {
  controls.inspection = props; const activity = props.selectedNode!;
  return <><span>Native Slot inspector</span>{activity?.nodeId?.startsWith("actual-") ? <input aria-label="Configured header input" disabled={props.readOnly} value={String(readWrappedInput(activity, headerInput).expression.value)}
    onInput={event => props.onSelectedActivityChange(writeInputValue(activity, headerInput, withLiteralValue(readWrappedInput(activity, headerInput), event.currentTarget.value)))} /> : null}</>;
} }));
vi.mock("../api/bpmnInterchange", () => ({ requireBpmnDocumentAuthoring: async () => undefined, exportBpmnDocument: vi.fn() }));
vi.mock("../bpmn/BpmnDocumentCanvasView", () => ({ BpmnDocumentCanvasView: () => <output>Controlled complete-source canvas</output> }));
vi.mock("../api/expressions", () => ({ listExpressionDescriptors: async () => [], listConversionProfiles: async () => [], listVariableTypeDescriptors: async () => [] }));
// Actual graph adapter, history, workbench and parent-CAS callbacks run. Only the canvas view is controlled.
vi.mock("../bpmn/BpmnDocumentAuthoringSurface", async original => {
  const actual = await original<typeof import("../bpmn/BpmnDocumentAuthoringSurface")>();
  return { ...actual, BpmnDocumentAuthoringSurface: (props: BpmnDocumentAuthoringSurfaceProps) => {
    controls.surface = props;
    if (controls.realSurface) return <actual.BpmnDocumentAuthoringSurface {...props} />;
    return props.children({ palette: <span>Source palette</span>, inspector: <span>Source inspector</span>, canvas: <output data-source-graph>{readBpmnDocument(props.owner).byId.get("B")?.getAttribute("name") ?? "Original"}</output>, controlsBlocked: false });
  } };
});
const cleanup: (() => void)[] = [];
afterEach(() => { cleanup.splice(0).forEach(dispose => dispose()); controls.surface = null; controls.realSurface = false; });
function renderGraph(scopeKind: "main" | "standard" | "other" = "main") {
  const owner = operationOwner(), payload = owner.structure!.payload;
  if (scopeKind === "standard") {
    payload.bindings = [{ elementId: "Child", childNodeId: "native" }];
    payload.scopeHeaders = [{ scopeElementId: "S", nodeId: "actual-standard-header", activityVersionId: "bpmn@exact", inputs: [{ referenceKey: "header-value", value: { expressionType: "Literal", value: "Stored header" }, vendor: { exact: true } }, { referenceKey: "kept-ref", value: { expressionType: "WorkflowRequest", value: { memberKey: "route", future: { keep: true } } } }], outputs: [], variables: [] }];
  }
  if (scopeKind === "other") {
    payload.documentXml = readBpmnDocument(owner).payload.documentXml.replace('<bd:BPMNDiagram id="Diagram">', '<b:process id="Other"><b:task id="OtherTask"/></b:process><bd:BPMNDiagram id="OtherDiagram"><bd:BPMNPlane id="OtherPlane" bpmnElement="Other"/></bd:BPMNDiagram><bd:BPMNDiagram id="Diagram">');
    payload.bindings = [{ elementId: "OtherTask", childNodeId: "native" }];
    payload.scopeHeaders = [{ scopeElementId: "Other", nodeId: "actual-Other-header", activityVersionId: "bpmn@exact", inputs: [{ referenceKey: "header-value", value: { expressionType: "Literal", value: "Stored header" }, vendor: { exact: true } }, { referenceKey: "kept-ref", value: { expressionType: "WorkflowRequest", value: { memberKey: "route", future: { keep: true } } } }], outputs: [], variables: [] }];
  }
  const initial = activityGraphDocumentAdapter.replaceRoot({ payload: { variables: [], outputMappings: [], outcomeMappings: [] }, layout: [] }, owner);
  const changed = vi.fn(), attempted = vi.fn(); let refuses = false;
  let read!: () => StudioActivityDefinitionImplementationState;
  function Parent() {
    const [value, setValue] = useState(initial), [revision, setRevision] = useState(1);
    const committed = useRef({ snapshot: initial, token: { authorityId: "ad-authority", resourceId: "definition:draft", scopeId: "implementation", revision: 1 }, lineage: [] });
    useLayoutEffect(() => { committed.current = { snapshot: value, token: { ...committed.current.token, revision }, lineage: [] }; });
    read = () => committed.current.snapshot;
    const coordinator = useMemo(() => createAuthoringCheckpoint<ActivityDefinitionDraftView>({ readLive: () => ({ token: committed.current.token, snapshot: {} as ActivityDefinitionDraftView, lineage: [] }) }), []);
    const parent = useMemo<ActivityGraphAuthoringHost>(() => ({ getResource: () => coordinator, readLive: () => committed.current, canEdit: () => true, probe: () => coordinator.probe(), notifyWorkChanged() {},
      invalidateScope() { setRevision(previous => previous + 1); }, async transition(intent, action) { return coordinator.transition(intent, action); } }), [coordinator]);
    const props = { context: { baseUrl: "test://source2-graph" }, definitionId: "definition", draftId: "draft", revision: 1, providerKey: "elsa.activity-graph", providerSchemaVersion: "2", manifestFingerprint: "test",
      contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] }, value, propertyEditors: [], expressionEditors: [], graphAuthoringPanels: [], graphCanvasEditors: [], historyResetKey: "draft:active", readOnly: false,
      authoringHost: { protocol: { id: "elsa.workflows.activity-graph-authoring", version: 1 }, context: parent },
      onChange(next: typeof initial, expected?: typeof initial) {
        attempted(next, expected, committed.current.snapshot);
        if (refuses || JSON.stringify(expected) !== JSON.stringify(committed.current.snapshot)) return false;
        changed(next, expected); setValue(next); setRevision(previous => previous + 1); return true;
      } } as unknown as StudioActivityDefinitionImplementationEditorProps;
    return <ActivityGraphImplementationEditor {...props} />;
  }
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  flushSync(() => root.render(<Parent />)); cleanup.push(() => { flushSync(() => root.unmount()); container.remove(); });
  return { container, changed, attempted, initial, read: () => read(), refuse: () => { refuses = true; } };
}
describe("actual ActivityGraph source-owned Surface host", () => {
  it("uses its actual parent baseline and ordinary graph history without creating a V1 semantic mirror", async () => {
    const h = renderGraph(); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    await vi.waitFor(() => expect(h.container.textContent).toContain("Source palette")); expect(h.container.textContent).toContain("Source inspector"); expect(h.container.querySelector(".react-flow")).toBeNull();
    const host = controls.surface!.host!, before = host.readCanonical(), transaction = planBpmnLabel(before.owner, operationScope, { elementId: "B" }, "Graph label").transaction;
    const candidate = applyBpmnDocumentTransaction(before.owner, transaction);
    expect(host.admit(before, candidate, { transaction, removedNodeIds: [], removedElementIds: [], unqualifiedOwnershipIds: [] }, { preparation: null })).toBe(true);
    expect(h.changed.mock.calls[0][1]).toEqual(h.initial);
    await vi.waitFor(() => expect(h.container.querySelector("[data-source-graph]")?.textContent).toBe("Graph label"));
    flushSync(() => controls.surface!.onSelectNative!("native"));
    expect(activityGraphDocumentAdapter.readRoot(h.read()).structure!.payload).not.toHaveProperty("elements");
    const undo = h.container.querySelector<HTMLButtonElement>('[aria-label="Undo Activity Graph edit"]')!;
    expect(undo.disabled).toBe(false); flushSync(() => undo.click());
    await vi.waitFor(() => expect(h.container.querySelector("[data-source-graph]")?.textContent).toBe("Original"));
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[aria-label="Redo Activity Graph edit"]')!.click());
    await vi.waitFor(() => expect(h.container.querySelector("[data-source-graph]")?.textContent).toBe("Graph label"));
    expect(activityGraphDocumentAdapter.readRoot(h.read()).structure!.payload).not.toHaveProperty("diagram");
  });
  it("keeps parent-refused source edits out of the canvas and shared history", async () => {
    const h = renderGraph(); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    const host = controls.surface!.host!, before = host.readCanonical(); h.refuse();
    const transaction = planBpmnLabel(before.owner, operationScope, { elementId: "B" }, "Refused").transaction;
    expect(host.admit(before, applyBpmnDocumentTransaction(before.owner, transaction), { transaction, removedNodeIds: [], removedElementIds: [], unqualifiedOwnershipIds: [] }, { preparation: null })).toBe(false);
    expect(h.changed).not.toHaveBeenCalled(); expect(h.read()).toEqual(h.initial);
    expect(h.container.querySelector<HTMLButtonElement>('[aria-label="Undo Activity Graph edit"]')!.disabled).toBe(true);
  });
  it.each(["main", "standard", "other"] as const)("keeps %s lexical context across actual native Slot descent and writes only physical configuration", async scopeKind => {
    const h = renderGraph(scopeKind); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    const surface = controls.surface!, native = readBpmnDocument(surface.owner).payload.activities[0], slot = getChildSlots(native)[0];
    await surface.onOpenNativeSlot(native.nodeId, slot, "Native body", { documentOwnerNodeId: surface.owner.nodeId, processId: scopeKind === "other" ? "Other" : "P", nativeOwnerNodeId: native.nodeId });
    await vi.waitFor(() => expect(h.container.textContent).toContain("Native Slot inspector"));
    expect(findActivityNode(controls.queryState!.rootActivity!, "inline")).toBeTruthy();
    const queryOwner = controls.queryState!.rootActivity!;
    expect(queryOwner.structure!.kind).toBe("elsa.bpmn.scope-view");
    expect(queryOwner.structure!.payload.owner).toEqual(surface.owner);
    expect(queryOwner.nodeId).toBe(scopeKind === "other" ? "actual-Other-header" : "owner");
    if (scopeKind === "standard") expect(findActivityNode(queryOwner, "actual-standard-header")).toBeTruthy();
    const selected = controls.inspection!.selectedNode!, beforeAttempts = h.attempted.mock.calls.length;
    flushSync(() => controls.inspection!.onSelectedActivityChange({ ...selected, executionPlacement: "background" }));
    expect(h.attempted.mock.calls.length).toBeGreaterThan(beforeAttempts);
    expect(h.attempted.mock.calls.at(-1)?.[1]).toEqual(h.attempted.mock.calls.at(-1)?.[2]);
    expect(findActivityNode(activityGraphDocumentAdapter.readRoot(h.attempted.mock.calls.at(-1)![0]), selected.nodeId)!.executionPlacement).toBe("background");
    const actual = activityGraphDocumentAdapter.readRoot(h.read());
    expect(actual.structure!.kind).toBe("elsa.bpmn.structure"); expect(actual.structure!.schemaVersion).toBe("2.0.0");
    expect(actual.structure!.payload).not.toHaveProperty("elements"); expect(actual.structure!.payload).not.toHaveProperty("diagram");
    expect(readBpmnDocument(actual).payload.documentXml).toBe(readBpmnDocument(surface.owner).payload.documentXml);
    expect(findActivityNode(actual, selected.nodeId)!.executionPlacement).toBe("background");
    const priorCallback = controls.inspection!.onSelectedActivityChange, previousSelected = controls.inspection!.selectedNode!;
    flushSync(() => controls.inspection!.onSelectedActivityChange({ ...previousSelected, executionPlacement: "inline" }));
    const acceptedCount = h.changed.mock.calls.length;
    flushSync(() => priorCallback({ ...previousSelected, executionPlacement: "background" }));
    expect(h.changed).toHaveBeenCalledTimes(acceptedCount);
    expect(findActivityNode(activityGraphDocumentAdapter.readRoot(h.read()), selected.nodeId)!.executionPlacement).toBe("inline");
    flushSync(() => h.container.querySelector<HTMLButtonElement>(".wf-breadcrumb button")!.click());
    await vi.waitFor(() => expect(h.container.querySelector("[data-source-graph]")).not.toBeNull());
  });

  it.each(["standard", "other"] as const)("edits a compact %s header input through actual shared Surface and Graph parent admission", async scopeKind => {
    controls.realSurface = true;
    const h = renderGraph(scopeKind); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    const source = readBpmnDocument(activityGraphDocumentAdapter.readRoot(h.read())), originalHeaders = structuredClone(source.payload.scopeHeaders!);
    await vi.waitFor(() => expect(h.container.querySelector('[aria-label="BPMN scope"]')).not.toBeNull());
    const select = h.container.querySelector<HTMLSelectElement>('[aria-label="BPMN scope"]')!;
    const target = [...select.options].find(option => { const scope = JSON.parse(option.value); return scope.scopeElementId === (scopeKind === "other" ? "Other" : "S") && scope.diagramId === (scopeKind === "other" ? "OtherDiagram" : "SubDiagram"); })!;
    if (select.value !== target.value) flushSync(() => { select.value = target.value; select.dispatchEvent(new Event("change", { bubbles: true })); });
    await vi.waitFor(() => expect(h.container.querySelector<HTMLInputElement>('[aria-label="Configured header input"]')?.value).toBe("Stored header"));
    await vi.waitFor(() => expect(h.container.querySelector<HTMLInputElement>('[aria-label="Configured header input"]')?.disabled).toBe(false));
    const input = h.container.querySelector<HTMLInputElement>('[aria-label="Configured header input"]')!;
    flushSync(() => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, "Edited header"); input.dispatchEvent(new Event("input", { bubbles: true })); });
    await vi.waitFor(() => expect(readBpmnDocument(activityGraphDocumentAdapter.readRoot(h.read())).payload.scopeHeaders![0].inputs[0]).toMatchObject({ referenceKey: "header-value", value: { expressionType: "Literal", value: "Edited header" }, vendor: { exact: true } }));
    const after = readBpmnDocument(activityGraphDocumentAdapter.readRoot(h.read()));
    expect(after.payload.scopeHeaders![0].inputs[1]).toEqual(originalHeaders[0].inputs[1]);
    expect(after.payload.scopeHeaders![0]).not.toHaveProperty("structure"); expect(after.payload.scopeHeaders![0]).not.toHaveProperty("header-value");
    expect(after.payload.documentXml).toBe(source.payload.documentXml); expect(after.payload.activities).toEqual(source.payload.activities);
  });

});
