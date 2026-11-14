import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { NativeBlockCommands, type NativeBlockValidation } from "../graph-authoring/NativeBlockCommands";
import type { NativeBlockContext } from "../graph-authoring/nativeBlocks";
import { sequenceActivity, sequenceNode, writeLine, leafNode } from "./fixtures";

let root: Root, container: HTMLDivElement;
let context: NativeBlockContext;
const validate = vi.fn<() => Promise<NativeBlockValidation>>();
const apply = vi.fn();
const open = vi.fn();
beforeEach(() => {
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  context = { document: { rootActivity: sequenceNode("root", [leafNode("one"), leafNode("two")]), layout: [], activityPresentation: [] },
    catalog: [sequenceActivity, writeLine], scope: [], canEdit: true, referenceContext: [], sourceKey: "resource:1/selection:one,two" };
  validate.mockReset().mockResolvedValue({ state: "validated", messages: [] }); apply.mockReset(); open.mockReset();
  render();
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
function render() { flushSync(() => root.render(<NativeBlockCommands context={context} selectedNodeIds={["one", "two"]} validate={validate} apply={apply} open={open} />)); }
function button(label: string) { const item = [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === label); if (!item) throw new Error(label); return item; }
function click(label: string) { flushSync(() => button(label).click()); }
function name(value: string) {
  const input = container.querySelector<HTMLInputElement>("input")!;
  flushSync(() => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value); input.dispatchEvent(new Event("input", { bubbles: true })); });
}
async function settle() { await new Promise(resolve => setTimeout(resolve, 0)); flushSync(() => {}); }

it("previews the whole proposal without mutation, then revalidates at confirmation and commits once", async () => {
  const before = JSON.stringify(context.document);
  click("Extract block…"); name("Prepare shipment"); click("Preview change"); await settle();
  expect(validate).toHaveBeenCalledTimes(1); expect(apply).not.toHaveBeenCalled(); expect(JSON.stringify(context.document)).toBe(before);
  expect(container.querySelector(".wf-block-preview")?.textContent).toContain("Prepare shipment");
  expect(container.textContent).toContain("2 selected steps");
  click("Apply change"); await settle();
  expect(validate).toHaveBeenCalledTimes(2); expect(apply).toHaveBeenCalledTimes(1); expect(container.querySelector("[role=dialog]")).toBeNull();
  expect(apply.mock.calls[0][0].activityPresentation[0].displayName).toBe("Prepare shipment");
});

it.each(["source", "selection", "permission", "catalog"])("refuses a stale %s before confirmation without a history edit", async change => {
  click("Extract block…"); name("Review"); click("Preview change"); await settle();
  if (change === "source") context = { ...context, document: { ...context.document, activityPresentation: [{ nodeId: "one", displayName: "Changed" }] } };
  if (change === "selection") context = { ...context, sourceKey: "resource:1/selection:one" };
  if (change === "permission") context = { ...context, canEdit: false };
  if (change === "catalog") context = { ...context, catalog: [] };
  render();
  expect(button("Apply change").disabled).toBe(true); expect(container.textContent).toContain("preview is stale"); expect(apply).not.toHaveBeenCalled();
});

it("does not accept a candidate whose resource authority changes during the confirmation request", async () => {
  click("Extract block…"); name("Review"); click("Preview change"); await settle();
  validate.mockResolvedValueOnce({ state: "denied", messages: [] });
  click("Apply change"); await settle();
  expect(apply).not.toHaveBeenCalled(); expect(button("Apply change").disabled).toBe(true);
});

it("ignores an in-flight preview after cancellation and restores normal page scrolling", async () => {
  let resolve!: (result: NativeBlockValidation) => void;
  validate.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
  expect(document.body.style.overflow).not.toBe("hidden");
  click("Extract block…"); name("Review"); click("Preview change");
  expect(document.body.style.overflow).toBe("hidden");
  click("Cancel"); resolve({ state: "validated", messages: [] }); await settle();
  expect(container.querySelector("[role=dialog]")).toBeNull(); expect(document.body.style.overflow).not.toBe("hidden"); expect(apply).not.toHaveBeenCalled();
  click("Extract block…"); expect(button("Preview change").disabled).toBe(false);
});

it("keeps a failed normal validation visible and refuses Apply", async () => {
  validate.mockResolvedValueOnce({ state: "invalid", messages: ["A required binding is missing."] });
  click("Extract block…"); name("Review"); click("Preview change"); await settle();
  expect(container.querySelector("[role=alert]")?.textContent).toContain("required binding"); expect(button("Apply change").disabled).toBe(true); expect(apply).not.toHaveBeenCalled();
});
