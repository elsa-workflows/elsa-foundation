import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";
import { CanvasEditorChoice, CanvasEditorHost, matchingCanvasEditors, useCanvasEditorChoice } from "../graph-authoring/CanvasEditorHost";
import type { WorkflowCanvasEditorContext } from "../graph-authoring/canvasEditorContract";
import type { BpmnDocumentCanvasEditorContext } from "../bpmn/bpmnDocumentSurfacePort";

let root: Root | undefined;
let container: HTMLDivElement;
afterEach(() => { if (root) flushSync(() => root!.unmount()); root = undefined; container?.remove(); vi.restoreAllMocks(); });
async function render(content: React.ReactNode) {
  if (!root) { container = document.createElement("div"); document.body.append(container); root = createRoot(container); }
  flushSync(() => root!.render(content));
  await new Promise(resolve => setTimeout(resolve, 20));
}
function context(scopeId = "process"): WorkflowCanvasEditorContext {
  return { protocol: { id: "elsa.workflows.controlled-canvas", version: 1 }, resourceKind: "workflow-definition", documentId: "draft", scopeId,
    owner: { nodeId: scopeId, activityVersionId: "bpmn@1", inputs: [], outputs: [] }, layout: [], selectedElementId: "decision", readOnly: false,
    onChange: vi.fn(() => true), onSelect: vi.fn(), onUndo: vi.fn(), onRedo: vi.fn(), onStateChange: vi.fn() };
}
function contribution(load: StudioWorkflowDesignerCanvasContribution["load"]): StudioWorkflowDesignerCanvasContribution {
  return { id: "external", title: "External", protocol: { id: "elsa.workflows.controlled-canvas", version: 1 }, supportedResourceKinds: ["workflow-definition"],
    supportedStructures: [{ kind: "elsa.bpmn.structure", schemaVersions: ["1.0.0"] }], load };
}

describe("controlled canvas module boundary", () => {
  it("keeps the current editor and preference until its resource checkpoint applies the choice", async () => {
    let finish!: () => void;
    const pending = new Promise<void>(resolve => { finish = resolve; });
    const preferences = { load: vi.fn(async () => null), save: vi.fn(async () => undefined) };
    function Choice() {
      const choice = useCanvasEditorChoice([contribution(vi.fn())], "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0" }, "draft:process", preferences);
      return <CanvasEditorChoice choice={choice} onChoose={async id => { await pending; choice.choose(id); }} />;
    }
    await render(<Choice />);
    const select = container.querySelector("select")!;
    flushSync(() => { select.value = "external"; select.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(select.value).toBe("integrated");
    expect(select.disabled).toBe(true);
    expect(preferences.save).not.toHaveBeenCalled();
    finish();
    await vi.waitFor(() => expect(select.value).toBe("external"));
    expect(select.disabled).toBe(false);
    expect(preferences.save).toHaveBeenCalledTimes(1);
    expect(preferences.save).toHaveBeenCalledWith("external");
  });

  it("retains the editor and its preference when pending work refuses the switch", async () => {
    const preferences = { load: vi.fn(async () => null), save: vi.fn(async () => undefined) };
    function Choice() {
      const choice = useCanvasEditorChoice([contribution(vi.fn())], "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0" }, "draft:process", preferences);
      return <CanvasEditorChoice choice={choice} onChoose={async () => { throw new Error("Retained native edits need attention"); }} />;
    }
    await render(<Choice />);
    const select = container.querySelector("select")!;
    flushSync(() => { select.value = "external"; select.dispatchEvent(new Event("change", { bubbles: true })); });
    await vi.waitFor(() => expect(container.querySelector("[role=alert]")?.textContent).toContain("Retained native edits"));
    expect(select.value).toBe("integrated");
    expect(select.disabled).toBe(false);
    expect(preferences.save).not.toHaveBeenCalled();
  });

  it("does not load contributors with a different protocol, host resource, or structure schema", () => {
    const load = vi.fn();
    const editor = contribution(load);
    expect(matchingCanvasEditors([editor], "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0" })).toEqual([editor]);
    expect(matchingCanvasEditors([editor, { ...editor, protocol: { ...editor.protocol, version: 2 } }], "activity-definition-graph", { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0" })).toEqual([]);
    expect(matchingCanvasEditors([editor], "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0" })).toEqual([]);
    expect(matchingCanvasEditors([{ ...editor, protocol: { id: "different", version: 1 } }], "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0" })).toEqual([]);
    expect(load).not.toHaveBeenCalled();
  });

  it("requires an explicit exact document protocol before selecting a source2 contributor", () => {
    const editor = { ...contribution(vi.fn()), protocol: { id: "elsa.workflows.bpmn-document-canvas", version: 1 },
      supportedStructures: [{ kind: "elsa.bpmn.structure", schemaVersions: ["2.0.0"] }] };
    const structure = { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0" };
    expect(matchingCanvasEditors([editor], "workflow-definition", structure)).toEqual([]);
    expect(matchingCanvasEditors([editor], "workflow-definition", structure, editor.protocol)).toEqual([editor]);
    expect(matchingCanvasEditors([editor], "workflow-definition", structure, { ...editor.protocol, version: 2 })).toEqual([]);
    expect(editor.load).not.toHaveBeenCalled();
  });

  it("retires native document view callbacks on the full selected DI address while retaining parent-owned ports", async () => {
    const received: BpmnDocumentCanvasEditorContext[] = [];
    const editor = { ...contribution(async () => ({ default: ({ context: value }) => { received.push(value as BpmnDocumentCanvasEditorContext); return <div>Document canvas</div>; } })),
      protocol: { id: "elsa.workflows.bpmn-document-canvas", version: 1 } };
    const legacy = context(), source = { owner: legacy.owner, token: { authorityId: "author", resourceId: "draft", scopeId: "root", revision: 0 } };
    const parentPort = vi.fn(() => { throw new Error("Parent registry test control"); });
    const previous: BpmnDocumentCanvasEditorContext = { protocol: { id: "elsa.workflows.bpmn-document-canvas", version: 1 },
      resourceKind: legacy.resourceKind, documentId: legacy.documentId, scope: { documentOwnerNodeId: "owner", diagramId: "first", scopeElementId: "process" },
      owner: legacy.owner, preparation: null, selection: null, ready: true, readOnly: false,
      readLive: vi.fn(() => source), createSessionHost: parentPort, retainCanvasController: (_id, create) => create(), onSelect: vi.fn(), onNavigateScope: vi.fn(), resolveStandardScope: vi.fn(() => null),
      onUndo: vi.fn(), onRedo: vi.fn(), onStateChange: vi.fn() };
    await render(<CanvasEditorHost editor={editor} context={previous} onFallback={vi.fn()} />);
    const retired = received.at(-1)!; retired.onStateChange({ status: "ready" });
    const current = { ...previous, scope: { ...previous.scope, diagramId: "second" }, readOnly: true, onSelect: vi.fn(), onNavigateScope: vi.fn() };
    await render(<CanvasEditorHost editor={editor} context={current} onFallback={vi.fn()} />);
    retired.onSelect(null); retired.onNavigateScope(previous.scope); retired.onUndo();
    expect(previous.onSelect).not.toHaveBeenCalled(); expect(previous.onNavigateScope).not.toHaveBeenCalled(); expect(previous.onUndo).not.toHaveBeenCalled();
    expect(retired.readLive()).toBe(source);
    expect(() => retired.createSessionHost("late", {} as never)).toThrow("retired");
    expect(parentPort).not.toHaveBeenCalled();
    const active = received.at(-1)!; active.onStateChange({ status: "ready" });
    active.onSelect(null); active.onNavigateScope(current.scope); active.onUndo();
    expect(current.onSelect).toHaveBeenCalledWith(null); expect(current.onNavigateScope).toHaveBeenCalledWith(current.scope);
    expect(current.onUndo).not.toHaveBeenCalled();
  });

  it("recovers a rejected lazy chunk with a fresh load and keeps Elsa fallback local", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const load = vi.fn<StudioWorkflowDesignerCanvasContribution["load"]>()
      .mockRejectedValueOnce(new Error("Chunk unavailable"))
      .mockResolvedValueOnce({ default: () => <div>Recovered canvas</div> });
    const controlled = context();
    const fallback = vi.fn();
    await render(<><div>Shared inspector and draft</div><CanvasEditorHost editor={contribution(load)} context={controlled} onFallback={fallback} /></>);
    await vi.waitFor(() => expect(container!.textContent).toContain("Chunk unavailable"));
    expect(container!.textContent).toContain("Shared inspector and draft");
    expect(controlled.onChange).not.toHaveBeenCalled();
    flushSync(() => (Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Retry loading editor")!).click());
    await vi.waitFor(() => expect(container.textContent).toContain("Recovered canvas"));
    expect(load).toHaveBeenCalledTimes(2);
    expect(container.textContent).toContain("Recovered canvas");
    flushSync(() => (Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Use Elsa editor")!).click());
    expect(fallback).toHaveBeenCalledOnce();
  });

  it("retires previous-scope callbacks and rejects document/history mutations in read-only mode", async () => {
    const received: WorkflowCanvasEditorContext[] = [];
    const editor = contribution(async () => ({ default: ({ context: value }) => { received.push(value as WorkflowCanvasEditorContext); return <div>Canvas</div>; } }));
    const previous = context("first");
    await render(<CanvasEditorHost editor={editor} context={previous} onFallback={() => undefined} />);
    const retired = received.at(-1)!;
    const current = { ...context("second"), readOnly: true };
    await render(<CanvasEditorHost editor={editor} context={current} onFallback={() => undefined} />);
    const change = { owner: previous.owner, baseOwner: previous.owner, layout: [], baseLayout: [] };
    expect(retired.onChange(change)).toBe(false);
    retired.onUndo(); retired.onSelect("stale"); retired.onStateChange({ status: "ready" });
    expect(previous.onChange).not.toHaveBeenCalled();
    expect(previous.onUndo).not.toHaveBeenCalled();
    expect(previous.onSelect).not.toHaveBeenCalled();
    const active = received.at(-1)!;
    active.onStateChange({ status: "ready" });
    expect(active.onChange(change)).toBe(false);
    active.onUndo(); active.onRedo(); active.onSelect("decision");
    expect(current.onChange).not.toHaveBeenCalled(); expect(current.onUndo).not.toHaveBeenCalled(); expect(current.onRedo).not.toHaveBeenCalled();
    expect(current.onSelect).toHaveBeenCalledWith("decision");
  });

  it("keeps an unavailable preference without loading it or overwriting it during fallback", async () => {
    const preferences = { load: vi.fn(async () => "missing-module"), save: vi.fn(async () => undefined) };
    const load = vi.fn();
    function Choice() {
      const choice = useCanvasEditorChoice([contribution(load)], "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0" }, "draft:process", preferences);
      return <><CanvasEditorChoice choice={choice} /><output>{choice.active?.id ?? "Elsa"}</output></>;
    }
    await render(<Choice />);
    expect(container.querySelector("select")?.value).toBe("missing-module");
    expect(container.querySelector("output")?.textContent).toBe("Elsa");
    expect(load).not.toHaveBeenCalled(); expect(preferences.save).not.toHaveBeenCalled();
  });
});
