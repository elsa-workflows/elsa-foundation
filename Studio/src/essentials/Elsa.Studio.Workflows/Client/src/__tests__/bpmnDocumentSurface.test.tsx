import React, { useLayoutEffect, useMemo, useRef, useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext, StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";
import { createAuthoringCheckpoint } from "../graph-authoring/authoringCheckpoint";
import { applyBpmnDocumentTransaction, readBpmnDocument } from "../bpmn/bpmnDocument";
import type { BpmnDocumentCanvasViewProps } from "../bpmn/bpmnDocumentCanvasPort";
import type { BpmnDocumentCanvasEditorContext, BpmnDocumentCanonicalSource, BpmnDocumentSurfaceHost } from "../bpmn/bpmnDocumentSurfacePort";
import type { BpmnDocumentAuthoringSurfaceProps } from "../bpmn/BpmnDocumentAuthoringSurface";
import { BpmnDocumentAuthoringSurface } from "../bpmn/BpmnDocumentAuthoringSurface";
import { operationOwner } from "./bpmnDocumentOperationsFixture";
import { planBpmnLabel } from "../bpmn/bpmnDocumentPropertyOperations";
import { bpmnDocumentToolboxShapes } from "../bpmn/bpmnDocumentToolbox";
import type { ActivityNode } from "../workflowTypes";

const controls = vi.hoisted(() => ({ view: null as BpmnDocumentCanvasViewProps | null, native: null as BpmnDocumentCanvasEditorContext | null,
  chooseTool: null as ((tool: unknown) => void) | null, created: 0, retired: 0, nativePending: false, readyToRetire: true, failDestroyOnce: false, destroyAttempts: 0, finishInitialization: null as (() => void) | null }));
vi.mock("../api/bpmnInterchange", () => ({ requireBpmnDocumentAuthoring: async () => undefined, exportBpmnDocument: vi.fn() }));
vi.mock("../bpmn/bpmnDocumentMigration", () => ({ prepareLegacyBpmnDocument: async () => operationOwner() }));
vi.mock("../bpmn/BpmnDocumentNativeInspector", () => ({ BpmnDocumentNativeInspector: () => null }));
vi.mock("../bpmn/BpmnDocumentCanvasView", () => ({ BpmnDocumentCanvasView: (props: BpmnDocumentCanvasViewProps) => {
  controls.view = props;
  const source = readBpmnDocument(props.snapshot.owner);
  return <output data-test-document-view data-scope={props.snapshot.scope.scopeElementId} data-selection={props.selection?.elementId ?? ""}>{source.byId.get("B")?.getAttribute("name") ?? "Original"}</output>;
} }));
vi.mock("../bpmn/BpmnDocumentPalette", () => ({ BpmnDocumentPalette: (props: { onChoose(tool: unknown): void }) => { controls.chooseTool = props.onChoose; return <span>Actual source toolbox port</span>; } }));
const editor: StudioWorkflowDesignerCanvasContribution = { id: "bpmn-js", title: "bpmn-js", protocol: { id: "elsa.workflows.bpmn-document-canvas", version: 1 },
  supportedResourceKinds: ["workflow-definition", "activity-definition-graph"], supportedStructures: [{ kind: "elsa.bpmn.structure", schemaVersions: ["2.0.0"] }],
  load: async () => ({ default: function Native({ context }: { context: unknown }) {
    const current = context as BpmnDocumentCanvasEditorContext;
    controls.native = current;
    const initial = useRef(current);
    useLayoutEffect(() => {
      const current = initial.current;
      current.retainCanvasController("native-control", () => {
        controls.created++;
        const listeners = new Set<() => void>();
        const host = current.createSessionHost("native-control", {
          readWork: () => ({ sequence: 0, acceptedSequence: 0, pending: controls.nativePending }), async checkpoint() { return { acknowledgement: { ...current.readLive(), acceptedSequence: 0 }, release() {} }; }
        });
        controls.finishInitialization = () => { controls.readyToRetire = true; for (const listener of listeners) listener(); };
        return { retire() { if (!controls.readyToRetire) throw new Error("Native import is still in progress"); host.retire(); controls.destroyAttempts++; if (controls.failDestroyOnce) { controls.failDestroyOnce = false; throw new Error("Destroy failed after host retirement"); } controls.retired++; },
          subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; } };
      });
      current.onStateChange({ status: "ready" });
    }, []);
    return <output data-native-control>{current.scope.scopeElementId}</output>;
  } }) };
const context = {} as StudioEndpointContext;
const cleanups: (() => void)[] = [];
afterEach(async () => { cleanups.splice(0).forEach(dispose => dispose()); await Promise.resolve(); await Promise.resolve(); controls.native = null; controls.view = null; controls.chooseTool = null; controls.created = 0; controls.retired = 0; controls.nativePending = false; controls.readyToRetire = true; controls.failDestroyOnce = false; controls.destroyAttempts = 0; controls.finishInitialization = null; });
function renderSurface(options: { legacy?: boolean; readonly?: boolean; native?: boolean } = {}) {
  const original = options.legacy ? { nodeId: "owner", activityVersionId: "bpmn@exact", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: { processId: "P", elements: [], sequenceFlows: [], activities: [], variables: [] } } } : operationOwner();
  let replace!: (owner: ActivityNode) => void, visibility!: (visible: boolean) => void;
  let host!: BpmnDocumentSurfaceHost;
  let capture!: () => Promise<unknown>;
  const edits = vi.fn();
  function Parent() {
    const [owner, setOwner] = useState<ActivityNode>(original), [revision, setRevision] = useState(1), [visible, setVisible] = useState(true);
    visibility = value => flushSync(() => setVisible(value));
    const initial = useMemo<BpmnDocumentCanonicalSource>(() => ({ owner: original, layout: [], token: { authorityId: "authority", resourceId: "draft", scopeId: "native-root", revision: 1 } }), []);
    const committed = useRef(initial);
    useLayoutEffect(() => { committed.current = { owner, layout: initial.layout, token: { ...initial.token, revision } }; });
    const coordinator = useMemo(() => createAuthoringCheckpoint({ readLive: () => ({ token: committed.current.token, snapshot: committed.current.owner, lineage: [] }) }), []);
    capture = async () => (await coordinator.capture("save")).snapshot;
    host = useMemo<BpmnDocumentSurfaceHost>(() => ({ id: "surface", authorityKey: "authority:draft", coordinator, canEdit: () => true,
      readCanonical: () => committed.current, notifyWorkChanged() {},
      async transitionScope(action) { await coordinator.transition("scope-change", () => { flushSync(() => { setRevision(value => value + 1); action(); }); return undefined; }); },
      admit(expected, candidate) {
        if (JSON.stringify(expected) !== JSON.stringify(committed.current)) return false;
        edits(expected, candidate); flushSync(() => { setOwner(candidate); setRevision(value => value + 1); }); return true;
      } }), [coordinator]);
    replace = next => flushSync(() => { setOwner(next); setRevision(value => value + 1); });
    const preferences = useMemo(() => ({ load: async () => options.native ? "bpmn-js" : "integrated", save: async () => undefined }), []);
    const props: BpmnDocumentAuthoringSurfaceProps = { context, resourceKind: "workflow-definition", documentId: "draft", owner, layout: initial.layout,
      workflowState: { rootActivity: owner }, host: options.readonly ? undefined : host, readOnly: !!options.readonly, catalog: [], activityDescriptors: [],
      editors: [editor], preferences, onHistory() {}, onOpenNativeSlot() {}, renderNativeInspector: () => null,
      children: slots => <>{slots.palette}<main>{slots.canvas}</main><aside>{slots.inspector}</aside></> };
    return visible ? <BpmnDocumentAuthoringSurface {...props} /> : null;
  }
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  flushSync(() => root.render(<Parent />)); cleanups.push(() => { flushSync(() => root.unmount()); container.remove(); });
  return { container, edits, get host() { return host; }, original, capture: () => capture(), hide: () => visibility(false), show: () => visibility(true), replace: (owner: ActivityNode) => replace(owner) };
}
describe("actual shared source surface with parent committed-ref timing", () => {
  it("renders an external source/history change immediately and uses its actual committed baseline", async () => {
    const h = renderSurface(); await vi.waitFor(() => expect(controls.view?.readOnly).toBe(false));
    const changed = applyBpmnDocumentTransaction(h.original, planBpmnLabel(h.original, controls.view!.snapshot.scope, { elementId: "B" }, "From Undo").transaction);
    h.replace(changed);
    await vi.waitFor(() => expect(h.container.querySelector("[data-test-document-view]")?.textContent).toBe("From Undo"));
    const view = controls.view!, next = planBpmnLabel(changed, view.snapshot.scope, { elementId: "B" }, "Next edit");
    const result = await view.onTransaction({ expected: view.snapshot.token, expectedOwner: view.snapshot.owner, scope: view.snapshot.scope, sessionId: "control", sequence: 1, label: "Label", transaction: next.transaction });
    expect(result.kind === "refused" ? result.reason : result.kind).toBe("accepted"); expect(h.edits.mock.calls[0][0].owner).toEqual(changed);
    expect(readBpmnDocument(h.host.readCanonical().owner).byId.get("B")!.getAttribute("name")).toBe("Next edit");
  });
  it("reconciles a removed collaboration plane and selected pool after external Undo without changing authority", async () => {
    const h = renderSurface(); await vi.waitFor(() => expect(controls.view?.readOnly).toBe(false));
    const pool = bpmnDocumentToolboxShapes.find(tool => tool.type === "participant")!;
    flushSync(() => controls.chooseTool!({ kind: "shape", entry: pool }));
    flushSync(() => controls.view!.onPlace!({ x: 40, y: 40 }));
    await vi.waitFor(() => expect(controls.view!.snapshot.scope.scopeElementId).not.toBe("P"));
    const source = readBpmnDocument(h.host.readCanonical().owner), participant = [...source.byId.values()].find(element => element.localName === "participant")!;
    flushSync(() => controls.view!.onSelect({ elementId: participant.id }));
    h.replace(h.original);
    await vi.waitFor(() => { expect(controls.view!.snapshot.scope.scopeElementId).toBe("P"); expect(controls.view!.selection).toBeNull(); expect(controls.view!.readOnly).toBe(false); });
    expect(h.container.textContent).not.toContain("earlier source");
    expect(await h.capture()).toEqual(h.original);
  });
  it("provides a retained non-authoring native controller for a pinned read-only snapshot", async () => {
    const h = renderSurface({ readonly: true, native: true });
    await vi.waitFor(() => expect(h.container.querySelector("[data-native-control]")?.textContent).toBe("P"));
    expect(controls.created).toBe(1); expect(controls.native!.readOnly).toBe(true);
    const retained = controls.native!.retainCanvasController("extra", () => ({}));
    expect(controls.native!.retainCanvasController("extra", () => ({}))).toBe(retained);
    expect(h.edits).not.toHaveBeenCalled(); expect(h.container.textContent).not.toContain("resource is unavailable");
  });
  it("keeps the exact legacy authority and history when a native group produces no actual change", async () => {
    const h = renderSurface({ legacy: true, native: true }); await vi.waitFor(() => expect(controls.native?.readOnly).toBe(false));
    const current = controls.native!, before = h.host.readCanonical();
    let accepted = 0;
    const session = current.createSessionHost("same-value", { readWork: () => ({ sequence: 1, acceptedSequence: accepted, pending: !accepted }),
      async checkpoint() { return { acknowledgement: { ...current.readLive(), acceptedSequence: accepted }, release() {} }; } });
    const live = current.readLive();
    const receipt = session.commit({ token: live.token, sourceXml: readBpmnDocument(live.owner).payload.documentXml, importGeneration: 1, sequence: 1, commands: [] }); accepted = receipt.acceptedSequence;
    expect(h.host.readCanonical()).toEqual(before); expect(h.host.readCanonical().owner).toEqual(h.original); expect(h.edits).not.toHaveBeenCalled();
  });
  it("shows competing standard/native body intent without discarding either authored body", async () => {
    const h = renderSurface(); await vi.waitFor(() => expect(controls.view?.readOnly).toBe(false));
    const before = readBpmnDocument(h.original);
    const owner = applyBpmnDocumentTransaction(h.original, { configuration: { bindings: before.payload.bindings.map(binding => ({ ...binding, elementId: "S" })) } });
    h.replace(owner);
    await vi.waitFor(() => expect(h.container.textContent).toContain("execution/binding conflict"));
    expect(readBpmnDocument(h.host.readCanonical().owner).payload.documentXml).toBe(before.payload.documentXml);
    expect(readBpmnDocument(h.host.readCanonical().owner).payload.activities).toEqual(before.payload.activities);
    expect(h.edits).not.toHaveBeenCalled();
  });

  it("retires clean native resources after Surface exit and recreates them when its resource view returns", async () => {
    const h = renderSurface({ native: true }); await vi.waitFor(() => expect(controls.created).toBe(1));
    h.hide(); await vi.waitFor(() => expect(controls.retired).toBe(1));
    h.show(); await vi.waitFor(() => expect(controls.created).toBe(2));
    expect(h.host.coordinator.probe()).toBe(false);
  });
  it("keeps a detached controller and registration while native work is retained", async () => {
    const h = renderSurface({ native: true }); await vi.waitFor(() => expect(controls.created).toBe(1));
    controls.nativePending = true; h.hide(); await Promise.resolve(); await Promise.resolve();
    expect(controls.retired).toBe(0); expect(h.host.coordinator.probe()).toBe(true);
    h.show(); await vi.waitFor(() => expect(h.container.querySelector("[data-native-control]")).not.toBeNull());
    expect(controls.created).toBe(1);
    controls.nativePending = false; h.hide(); await vi.waitFor(() => expect(controls.retired).toBe(1));
  });
  it("retries clean disposal when a detached native import settles, including a read-only resource", async () => {
    const h = renderSurface({ native: true, readonly: true }); await vi.waitFor(() => expect(controls.created).toBe(1));
    controls.readyToRetire = false; h.hide(); await Promise.resolve(); await Promise.resolve();
    expect(controls.retired).toBe(0);
    controls.finishInitialization!(); await vi.waitFor(() => expect(controls.retired).toBe(1));
  });

  it("keeps a retired controller for actual teardown retry after destroy fails, without reusing it for new work", async () => {
    const h = renderSurface({ native: true }); await vi.waitFor(() => expect(controls.created).toBe(1));
    controls.failDestroyOnce = true; h.hide();
    await vi.waitFor(() => expect(controls.retired).toBe(1));
    expect(controls.destroyAttempts).toBe(2); expect(h.edits).not.toHaveBeenCalled();
    h.show(); await vi.waitFor(() => expect(controls.created).toBe(2));
  });

  it("parks only the clean standard scope address across native resource retirement and native Slot return", async () => {
    const h = renderSurface({ native: true }); await vi.waitFor(() => expect(controls.native?.readOnly).toBe(false));
    await controls.native!.onNavigateScope({ documentOwnerNodeId: "owner", diagramId: "SubDiagram", scopeElementId: "S" });
    await vi.waitFor(() => expect(h.container.querySelector("[data-native-control]")?.textContent).toBe("S"));
    expect(controls.created).toBe(2); h.hide(); await vi.waitFor(() => expect(controls.retired).toBe(2));
    h.show(); await vi.waitFor(() => expect(h.container.querySelector("[data-native-control]")?.textContent).toBe("S"));
    expect(controls.created).toBe(3); expect(h.edits).not.toHaveBeenCalled();
  });

});
