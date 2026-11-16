import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { NativeBlockCommands } from "../graph-authoring/NativeBlockCommands";
import type { NativeBlockContext, NativeBlockDocument } from "../graph-authoring/nativeBlocks";
import { createActivityNode } from "../workflowAdapter";
import { bpmnActivity, writeLine } from "./fixtures";
import type { ActivityNode } from "../workflowTypes";
let root: Root, container: HTMLDivElement, context: NativeBlockContext, selection: string[];
const validate = vi.fn(async (_document: NativeBlockDocument) => ({ state: "validated", messages: [] as string[] }));
const commitStates: boolean[] = [];
const apply = vi.fn(async (_document: NativeBlockDocument, _selection: string[], current: () => boolean) => { commitStates.push(current()); });
const open = vi.fn();
beforeEach(() => {
  validate.mockClear(); apply.mockClear(); open.mockClear(); commitStates.length = 0;
  const owner = createActivityNode(bpmnActivity, "root");
  owner.structure!.payload = { activities: [createActivityNode(writeLine, "a"), createActivityNode(writeLine, "b")],
    elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "ea", elementType: "task", childNodeId: "a" }, { elementId: "eb", elementType: "task", childNodeId: "b" }, { elementId: "end", elementType: "endEvent" }],
    sequenceFlows: [{ flowId: "entry", sourceRef: "start", targetRef: "ea" }, { flowId: "inside", sourceRef: "ea", targetRef: "eb" }, { flowId: "exit", sourceRef: "eb", targetRef: "end" }] };
  context = { document: { rootActivity: owner, layout: [], activityPresentation: [] }, catalog: [bpmnActivity, writeLine], scope: [], referenceContext: [], sourceKey: "resource/1/rev/1", canEdit: true };
  selection = ["ea", "eb"];
  container = document.createElement("div"); document.body.append(container); root = createRoot(container); render();
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
function render() { flushSync(() => root.render(<NativeBlockCommands context={context} selectedNodeIds={selection} createPosition={{ x: 80, y: 100 }} validate={validate} apply={apply} open={open} />)); }
function button(label: string) { const item = [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === label); if (!item) throw new Error(label); return item; }
function click(label: string) { flushSync(() => button(label).click()); }
function name(value: string) {
  const input = container.querySelector<HTMLInputElement>("input")!;
  flushSync(() => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value); input.dispatchEvent(new Event("input", { bubbles: true })); });
}
async function preview() {
  click("Extract block…"); name("Shipping"); click("Preview change");
  await vi.waitFor(() => expect(validate).toHaveBeenCalledTimes(1)); flushSync(() => {});
  await vi.waitFor(() => expect(button("Apply change").disabled).toBe(false));
}
it("offers a real BPMN subprocess and validates/commits the whole resource once using canvas element selection", async () => {
  expect(container.textContent).toContain("Create BPMN subprocess");
  expect(container.textContent).not.toContain("Create Sequence block");
  const original = structuredClone(context.document);
  await preview();
  expect(container.querySelector(".wf-block-preview")?.textContent).toContain("BPMN subprocess");
  expect(container.querySelector(".wf-block-preview")?.textContent).toContain("Change BPMN elements");
  expect(context.document).toEqual(original); expect(apply).not.toHaveBeenCalled();
  click("Apply change"); await vi.waitFor(() => expect(apply).toHaveBeenCalledTimes(1));
  const [document, selected, current] = apply.mock.calls[0];
  const block = (document.rootActivity.structure!.payload.activities as ActivityNode[])[0];
  expect(block.structure?.kind).toBe("elsa.bpmn.structure"); expect(selected[0]).not.toBe(block.nodeId);
  expect(document.rootActivity.structure!.payload.elements).toContainEqual(expect.objectContaining({ elementId: selected[0], childNodeId: block.nodeId, elementType: "subProcess" }));
  expect(commitStates).toEqual([true]); expect(current()).toBe(false); expect(validate).toHaveBeenCalledTimes(2);
  context = { ...context, document }; selection = selected; render();
  click("Open Shipping (2 steps)"); expect(open).toHaveBeenCalledWith(block.nodeId);
});
it("keeps a stale BPMN proposal from committing after edit authority changes", async () => {
  await preview(); context = { ...context, canEdit: false }; render();
  expect(button("Apply change").disabled).toBe(true); expect(apply).not.toHaveBeenCalled();
  expect(container.textContent).toContain("preview is stale");
});
it("refuses a gateway selection before resource validation or mutation", async () => {
  (context.document.rootActivity.structure!.payload.elements as Record<string, unknown>[])[1].elementType = "exclusiveGateway";
  render(); const original = structuredClone(context.document);
  click("Extract block…"); name("Unsafe"); click("Preview change");
  await vi.waitFor(() => expect(container.querySelector("[role=alert]")?.textContent).toContain("gateways"));
  expect(validate).not.toHaveBeenCalled(); expect(apply).not.toHaveBeenCalled(); expect(context.document).toEqual(original);
});
