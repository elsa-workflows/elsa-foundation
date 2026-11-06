import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BpmnDocumentPalette, type BpmnDocumentPaletteProps } from "../bpmn/BpmnDocumentPalette";
import { BpmnDocumentProperties, type BpmnDocumentPropertiesProps, type BpmnDocumentPropertyDraft } from "../bpmn/BpmnDocumentProperties";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { bpmnDocumentToolboxShapes } from "../bpmn/bpmnDocumentToolbox";
import type { BpmnDocumentCanvasSnapshot } from "../bpmn/bpmnDocumentCanvasPort";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

let root: Root, container: HTMLDivElement;
beforeEach(() => { container = document.createElement("div"); document.body.append(container); root = createRoot(container); });
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
function button(label: string) { return [...container.querySelectorAll("button")].find(element => (element.querySelector(":scope > span:last-child")?.textContent ?? element.textContent) === label)!; }
function click(label: string) { flushSync(() => button(label).click()); }
function edit(input: HTMLInputElement | HTMLTextAreaElement, value: string) {
  Object.getOwnPropertyDescriptor(input instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, "value")!.set!.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}
function snapshot(): BpmnDocumentCanvasSnapshot { return { owner: operationOwner(), scope: operationScope, token: { authorityId: "host", resourceId: "actual-draft", scopeId: "owner", revision: 4 } }; }

describe("unenabled integrated standard BPMN controls", () => {
  it("offers every shape and typed connection, searches actual constructs, and honors authority/pending controls", () => {
    const onChoose = vi.fn(), props: BpmnDocumentPaletteProps = { readOnly: false, onChoose };
    flushSync(() => root.render(<BpmnDocumentPalette {...props} />));
    expect(container.querySelectorAll(".wf-bpmn-document-tools button")).toHaveLength(bpmnDocumentToolboxShapes.length + 6);
    click("Timer boundary");
    expect(onChoose).toHaveBeenCalledWith({ kind: "shape", entry: expect.objectContaining({ id: "boundaryEvent-timer", placement: "activityBoundary" }) });
    edit(container.querySelector("input")!, "data store");
    expect(container.querySelectorAll(".wf-bpmn-document-tools button")).toHaveLength(1);
    click("Data store");
    flushSync(() => root.render(<BpmnDocumentPalette {...props} readOnly />));
    expect(button("Data store").disabled).toBe(true);
    flushSync(() => root.render(<BpmnDocumentPalette {...props} pending />));
    expect(button("Data store").disabled).toBe(true);
  });

  it("dispatches label edits with the exact host baseline and retains controlled choices on refusal and selection changes", () => {
    const source = snapshot(), applied = vi.fn(), native = vi.fn(() => <div>Existing Elsa inspector</div>);
    let draft: BpmnDocumentPropertyDraft | null = null, props: BpmnDocumentPropertiesProps;
    const render = () => flushSync(() => root.render(<BpmnDocumentProperties {...props} draft={draft} />));
    props = { snapshot: source, selection: { elementId: "A", diId: "ADI" }, readOnly: false, draft, onDraftChange: next => { draft = next; render(); }, onApply: applied, renderNativeConfiguration: native };
    render(); click("Edit label");
    edit(container.querySelector(".wf-bpmn-document-property-edit input")!, "New name");
    click("Apply BPMN property");
    expect(applied.mock.calls[0][1]).toBe(source);
    const plan = applied.mock.calls[0][0];
    expect(readBpmnDocument(applyBpmnDocumentTransaction(source.owner, plan.transaction)).byId.get("A")!.getAttribute("name")).toBe("New name");
    expect(readBpmnDocument(source.owner).byId.get("A")!.getAttribute("name")).toBe("Bound");
    // A host refusal only reports a message; it cannot erase the controlled edit.
    props = { ...props, message: "Host refused this revision", selection: { elementId: "B", diId: "BDI" }, snapshot: { ...source, token: { ...source.token, revision: 5 } } }; render();
    expect((container.querySelector(".wf-bpmn-document-property-edit input") as HTMLInputElement).value).toBe("New name");
    expect(container.querySelector('[role="alert"]')!.textContent).toBe("Host refused this revision");
    expect(native).toHaveBeenLastCalledWith({ elementId: "A", diId: "ADI" }, source);
    click("Apply BPMN property"); expect(applied.mock.calls[1][1]).toBe(source);
    click("Discard BPMN property");
    expect(container.querySelector(".wf-bpmn-document-property-edit")).toBeNull();
    expect(native).toHaveBeenLastCalledWith({ elementId: "B", diId: "BDI" }, props.snapshot);
  });

  it("edits the real standard condition without a fake activity and does not dispatch an invalid structural property", () => {
    const source = snapshot(), apply = vi.fn(); let draft: BpmnDocumentPropertyDraft | null = null;
    const props: BpmnDocumentPropertiesProps = { snapshot: source, selection: { elementId: "F", diId: "FDI" }, readOnly: false, draft, onDraftChange: next => { draft = next; render(); }, onApply: apply };
    const render = () => flushSync(() => root.render(<BpmnDocumentProperties {...props} draft={draft} />));
    render(); click("Edit condition");
    edit(container.querySelector("textarea")!, "route === 'High'"); click("Apply BPMN property");
    const next = readBpmnDocument(applyBpmnDocumentTransaction(source.owner, apply.mock.calls[0][0].transaction));
    expect(next.byId.get("F")!.getElementsByTagNameNS(ns.model, "conditionExpression")[0].textContent).toBe("route === 'High'");
    expect(next.payload.activities).toEqual(readBpmnDocument(source.owner).payload.activities);
    draft = { source, selection: { elementId: "A" }, intent: { kind: "removeProperty", target: { elementId: "A" } } }; render(); click("Apply BPMN property");
    expect(apply).toHaveBeenCalledTimes(1);
    expect(container.querySelector('[role="alert"]')!.textContent).toContain("ownership-aware");
    expect(draft).not.toBeNull();
  });

  it("blocks property changes/dispatch while pending and retains the pending draft", () => {
    const source = snapshot(), apply = vi.fn(), change = vi.fn();
    const draft: BpmnDocumentPropertyDraft = { source, selection: { elementId: "B", diId: "BDI" }, intent: { kind: "label", value: "Pending name" } };
    flushSync(() => root.render(<BpmnDocumentProperties snapshot={source} selection={draft.selection} readOnly={false} pending draft={draft} onDraftChange={change} onApply={apply} />));
    expect(button("Apply BPMN property").disabled).toBe(true);
    expect(button("Discard BPMN property").disabled).toBe(true);
    click("Apply BPMN property"); expect(apply).not.toHaveBeenCalled(); expect(change).not.toHaveBeenCalled();
  });
});
