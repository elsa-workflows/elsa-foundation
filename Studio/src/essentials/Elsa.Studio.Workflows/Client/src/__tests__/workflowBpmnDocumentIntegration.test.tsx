import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import { authSessionEndedEvent, requestStudioNavigation, type StudioAgentRegistry, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityNode, WorkflowDefinitionDetails, WorkflowDefinitionState, WorkflowDraft } from "../workflowTypes";
import type { BpmnDocumentAuthoringSurfaceProps } from "../bpmn/BpmnDocumentAuthoringSurface";
import { WorkflowEditor } from "../workflow-editor/WorkflowEditor";
import type { WorkflowEditorDocumentAdapter } from "../workflow-editor/WorkflowEditorDocumentAdapter";
import type { WorkflowTestRunState } from "../workflow-editor/editorTypes";
import { getDraftSignature } from "../workflow-editor/editorHelpers";
import { findActivityNode, getChildSlots } from "../workflowAdapter";
import { operationOwner } from "./bpmnDocumentOperationsFixture";
import { readBpmnDocument } from "../bpmn/bpmnDocument";

const controls = vi.hoisted(() => ({ draft: null as WorkflowDraft | null, surface: null as BpmnDocumentAuthoringSurfaceProps | null, restoreRun: null as (() => void) | null,
  query: null as WorkflowDefinitionState | null }));
vi.mock("../bpmn/BpmnDocumentNativeInspector", async original => ({ ...await original<object>(),
  BpmnDocumentQueryInspector: (props: { workflowState: WorkflowDefinitionState; render(overrides: { workflowState: WorkflowDefinitionState }): React.ReactNode }) => {
    controls.query = props.workflowState; return props.render({ workflowState: props.workflowState });
  }
}));
vi.mock("../workflow-editor/useTestRunRestoration", () => ({ useTestRunRestoration: (props: { context: StudioEndpointContext; onRestore(run: WorkflowTestRunState): void }) => {
  controls.restoreRun = () => { const snapshot = structuredClone(controls.draft!); snapshot.state.rootActivity!.displayName = "Captured";
    props.onRestore({ snapshot, draftSignature: getDraftSignature(snapshot), endpointContext: props.context,
      view: { testRunId: "test", definitionId: "definition", definitionVersionId: "draft:1", artifactId: "artifact", workflowExecutionId: "run", status: "DispatchAccepted" } }); };
  return { receipt: null, pending: false, message: "", restore: async () => undefined, dismiss() {} };
} }));
vi.mock("../workflow-editor/editorPanels", async original => ({ ...await original<object>(), WorkflowRuntimePanel: (props: { executionControls: React.ReactNode }) => <div>{props.executionControls}</div> }));
vi.mock("../workflow-editor/useWorkflowEditorData", async original => ({ ...await original<object>(),
  useWorkflowEditorData: (props: { loadDraft(draft: WorkflowDraft): void; markSaved(draft: WorkflowDraft): void; resetHistory(draft: WorkflowDraft): void }) => {
    const { loadDraft, resetHistory, markSaved } = props;
    React.useLayoutEffect(() => { loadDraft(controls.draft!); resetHistory(controls.draft!); markSaved(controls.draft!); }, [loadDraft, markSaved, resetHistory]);
    return { details: { definition: { id: "definition", name: "Actual Workflow parent" }, draft: controls.draft, versions: [] }, setDetails: vi.fn(),
      catalog: [], paletteCatalog: [], recommendedDefinitions: [], activityDescriptors: [], availabilityDiagnostics: null,
      expressionDescriptors: [], expressionDescriptorStatus: "ready", descriptorStatus: "ready", reload: vi.fn(), reloadExpressionDescriptors: vi.fn() };
  }
}));
// The actual parent reducer, history, checkpoint host, persistence and workbench run. The Surface is
// replaced only so this focused control can exercise its parent-owned protocol without a canvas DOM.
vi.mock("../bpmn/BpmnDocumentAuthoringSurface", () => ({ BpmnDocumentAuthoringSurface: (props: BpmnDocumentAuthoringSurfaceProps) => {
  controls.surface = props;
  return props.children({ palette: <div>Document palette control</div>, canvas: <><output data-source-canvas>{props.owner.displayName as string ?? "Original"}</output>
    {[["native", "P"], ["native-sub", "P"], ["native-other", "Other"]].map(([nativeOwner, processId]) => {
      const activity = (props.owner.structure?.payload.activities as ActivityNode[] | undefined)?.find(node => node.nodeId === nativeOwner), slot = activity && getChildSlots(activity)[0];
      return slot ? <button key={nativeOwner} onClick={() => { void props.onOpenNativeSlot(nativeOwner, slot, "Body", { documentOwnerNodeId: props.owner.nodeId, processId, nativeOwnerNodeId: nativeOwner }); }}>Open {nativeOwner}</button> : null;
    })}</>,
    inspector: <div>Document inspector control</div>, controlsBlocked: false });
} }));
const cleanups: (() => void)[] = [];
afterEach(() => { cleanups.splice(0).forEach(cleanup => cleanup()); controls.surface = null; controls.query = null; });
function renderEditor(runtime = false, slotOwner?: ActivityNode) {
  controls.draft = { id: "draft", definitionId: "definition", layout: [], validationErrors: [], state: { rootActivity: {
    nodeId: "process", activityVersionId: "bpmn@1", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: { documentXml: "retained source", activities: [], bindings: [], variables: [] } }
  } } };
  if (slotOwner) controls.draft.state.rootActivity = slotOwner;
  const context = { baseUrl: "test://workflow-source2-parent", http: { getJson: async () => ({ capabilities: [], links: [] }) } } as unknown as StudioEndpointContext;
  const saved = vi.fn(async (_context: StudioEndpointContext, value: WorkflowDraft) => structuredClone(value));
  const adapter: WorkflowEditorDocumentAdapter = { save: saved, load: async () => ({}) as WorkflowDefinitionDetails, metadata: async () => ({}) as WorkflowDefinitionDetails };
  const query = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  flushSync(() => root.render(<QueryClientProvider client={query}><WorkflowEditor context={context} definitionId="definition" documentAdapter={runtime ? undefined : adapter}
    ai={{ promptActions: { list: () => [] }, contextProviders: { list: () => [] }, providerAvailability: { get: () => false, subscribe: () => () => undefined } } as unknown as StudioAgentRegistry} propertyEditors={[]} expressionEditors={[]} runInputEditors={[]} testStimulusEditors={[]} workflowDesignerPanels={[]}
    autosaveEnabledByDefault={false} onBack={vi.fn()} /></QueryClientProvider>));
  cleanups.push(() => { flushSync(() => root.unmount()); query.clear(); container.remove(); });
  return { container, saved };
}
function pendingNative(failure?: Error, release?: () => void) {
  const host = controls.surface!.host!;
  let complete!: () => void, accepted = 0;
  const changed = new Promise<void>(resolve => { complete = resolve; });
  const cleanup = vi.fn(release);
  const checkpoint = vi.fn(async () => {
    await changed;
    if (failure) throw failure;
    const expected = host.readCanonical();
    if (!host.admit(expected, { ...expected.owner, displayName: "Checkpointed native" }, { transaction: {}, removedNodeIds: [], removedElementIds: [], unqualifiedOwnershipIds: [] }, { preparation: null })) throw new Error("Native edit refused after authority change");
    accepted = 1;
    return { receipt: registration.acknowledge(1, host.readCanonical().token), release: cleanup };
  });
  const registration = host.coordinator.register("controlled-native", { readWork: () => ({ pending: accepted !== 1, sequence: 1, acceptedSequence: accepted }), checkpoint });
  return { complete, checkpoint, cleanup, registration };
}
describe("actual WorkflowEditor source2 parent wiring (controlled Surface)", () => {
  it("uses the shared slots and commits through ordinary Undo/Redo history without the legacy canvas", async () => {
    const h = renderEditor();
    await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    expect(h.container.textContent).toContain("Document palette control"); expect(h.container.textContent).toContain("Document inspector control");
    expect(h.container.querySelector(".react-flow")).toBeNull();
    const host = controls.surface!.host!, expected = host.readCanonical(), candidate = { ...expected.owner, displayName: "Native acknowledged" };
    expect(host.admit(expected, candidate, { transaction: {}, removedNodeIds: [], removedElementIds: [], unqualifiedOwnershipIds: [] }, { preparation: null })).toBe(true);
    await vi.waitFor(() => expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Native acknowledged"));
    await vi.waitFor(() => expect(h.container.querySelector<HTMLButtonElement>('[aria-label="Undo"]')?.disabled).toBe(false));
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[aria-label="Undo"]')!.click());
    await vi.waitFor(() => expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Original"));
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[aria-label="Redo"]')!.click());
    await vi.waitFor(() => expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Native acknowledged"));
    expect(h.saved).not.toHaveBeenCalled();
  });

  it("uses the actual toolbar checkpoint and cached navigation guard before saving a clean-signature native edit", async () => {
    const h = renderEditor(); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    const native = pendingNative();
    expect(requestStudioNavigation("/workflows/editor", "/settings/backends")).toBe(false);
    expect(native.checkpoint).not.toHaveBeenCalled();
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[title="Save workflow"]')!.click());
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    expect(h.saved).not.toHaveBeenCalled();
    native.complete();
    await vi.waitFor(() => expect(h.saved).toHaveBeenCalledOnce());
    expect(h.saved.mock.calls[0][1].state.rootActivity?.displayName).toBe("Checkpointed native");
  });

  it("retains native work and sends no HTTP if the actual parent loses authorization during toolbar Save", async () => {
    const h = renderEditor(); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    const native = pendingNative();
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[title="Save workflow"]')!.click());
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent)));
    native.complete();
    await vi.waitFor(() => expect(h.container.textContent).toContain("Native edit refused"));
    expect(h.saved).not.toHaveBeenCalled();
    expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Original");
  });

  it("completes delayed native work before pinning the captured snapshot and returns through the clean read-only path", async () => {
    const h = renderEditor(true); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    flushSync(() => controls.restoreRun!());
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[data-tab-id="runtime"]')!.click());
    await vi.waitFor(() => expect(h.container.textContent).toContain("Inspect captured snapshot"));
    const native = pendingNative();
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Inspect captured snapshot")!.click());
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    expect(controls.surface!.readOnly).toBe(false);
    expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Original");
    native.complete();
    await vi.waitFor(() => expect(controls.surface!.readOnly).toBe(true));
    expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Captured");
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Return to editing")!.click());
    await vi.waitFor(() => expect(controls.surface!.readOnly).toBe(false));
    expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Checkpointed native");
    expect(native.checkpoint).toHaveBeenCalledOnce(); expect(h.saved).not.toHaveBeenCalled();
  });

  it("keeps the editable view and retained work when a snapshot attachment checkpoint refuses", async () => {
    const h = renderEditor(true); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    flushSync(() => controls.restoreRun!());
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[data-tab-id="runtime"]')!.click());
    await vi.waitFor(() => expect(h.container.textContent).toContain("Inspect captured snapshot"));
    const native = pendingNative(new Error("Retained native source refused attachment"));
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Inspect captured snapshot")!.click());
    native.complete();
    await vi.waitFor(() => expect(h.container.textContent).toContain("Retained native source refused attachment"));
    expect(controls.surface!.readOnly).toBe(false); expect(requestStudioNavigation("/workflows/editor", "/settings/backends")).toBe(false);
    expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Original");
    expect(h.container.textContent).not.toContain("Return to editing"); expect(h.saved).not.toHaveBeenCalled();
  });

  it("keeps an applied snapshot transition applied when fence cleanup fails and retries only cleanup", async () => {
    const h = renderEditor(true); await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    flushSync(() => controls.restoreRun!());
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[data-tab-id="runtime"]')!.click());
    await vi.waitFor(() => expect(h.container.textContent).toContain("Inspect captured snapshot"));
    const native = pendingNative(undefined, () => { throw new Error("Controlled view fence cleanup failed"); });
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Inspect captured snapshot")!.click());
    native.complete();
    await vi.waitFor(() => expect(controls.surface!.readOnly).toBe(true));
    expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Captured");
    expect(native.checkpoint).toHaveBeenCalledOnce(); expect(native.cleanup).toHaveBeenCalledOnce();
    native.cleanup.mockImplementation(() => undefined); native.registration.retryRelease();
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Return to editing")!.click());
    await vi.waitFor(() => expect(controls.surface!.readOnly).toBe(false));
    expect(native.checkpoint).toHaveBeenCalledOnce(); expect(native.cleanup).toHaveBeenCalledTimes(2);
    expect(h.container.querySelector("[data-source-canvas]")?.textContent).toBe("Checkpointed native");
  });

  it.each([["native", "P", "inline"], ["native-sub", "P", "sub-leaf"], ["native-other", "Other", "other-leaf"]])("retains source-owned lexical query context in actual native Slot %s", async (nativeOwner, processId, leaf) => {
    const owner = operationOwner(), source = readBpmnDocument(owner), payload = owner.structure!.payload;
    const variable = (name: string) => ({ referenceKey: name, name, type: { alias: "String", collectionKind: "single" } });
    const sequence = (id: string, child: string): ActivityNode => ({ nodeId: id, activityVersionId: "sequence@exact", inputs: [], outputs: [], structure: {
      kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: child, activityVersionId: "leaf@exact", inputs: [], outputs: [] }], variables: [variable(`${id}Local`)] }
    } });
    payload.documentXml = source.payload.documentXml.replace('<bd:BPMNDiagram id="Diagram">', '<b:process id="Other"><b:task id="OtherTask"/></b:process><bd:BPMNDiagram id="Diagram">');
    payload.variables = [variable("MainLocal")];
    payload.activities = [sequence("native", "inline"), sequence("native-sub", "sub-leaf"), sequence("native-other", "other-leaf")];
    payload.bindings = [...source.payload.bindings, { elementId: "Child", childNodeId: "native-sub" }, { elementId: "OtherTask", childNodeId: "native-other" }];
    payload.scopeHeaders = [{ scopeElementId: "S", nodeId: "sub-header", activityVersionId: "bpmn@exact", inputs: [], outputs: [], variables: [variable("SubLocal")] },
      { scopeElementId: "Other", nodeId: "other-header", activityVersionId: "bpmn@exact", inputs: [], outputs: [], variables: [variable("OtherLocal")] }];
    const original = structuredClone(owner), h = renderEditor(false, owner);
    await vi.waitFor(() => expect(controls.surface?.host).toBeDefined());
    const host = controls.surface!.host!;
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === `Open ${nativeOwner}`)!.click());
    await vi.waitFor(() => expect(controls.query).not.toBeNull());
    expect(findActivityNode(controls.query!.rootActivity!, leaf)).toBeTruthy();
    if (processId === "Other") {
      expect(controls.query!.rootActivity!.nodeId).toBe("other-header");
      expect(findActivityNode(controls.query!.rootActivity!, "native")).toBeNull();
      expect(controls.query!.rootActivity!.structure!.payload.variables).toEqual([variable("OtherLocal")]);
    } else {
      expect(controls.query!.rootActivity!.nodeId).toBe("owner");
      expect(findActivityNode(controls.query!.rootActivity!, "sub-header")!.structure!.payload.variables).toEqual([variable("SubLocal")]);
    }
    expect(host.readCanonical().owner).toEqual(original);
    controls.query = null;
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>(".wf-breadcrumb button")].find(button => button.textContent === "Root")!.click());
    await vi.waitFor(() => expect(h.container.querySelector("[data-source-canvas]")).not.toBeNull());
    expect(controls.query).toBeNull(); expect(controls.surface!.host!.readCanonical().owner).toEqual(original);
    expect(h.saved).not.toHaveBeenCalled();
  });
});
