import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { NativeBlockCommands, type NativeBlockValidation } from "../graph-authoring/NativeBlockCommands";
import { planExtractNativeBlock, type NativeBlockContext, type NativeBlockDocument } from "../graph-authoring/nativeBlocks";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { normalizeActivityStructures } from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";
import { composeExpressionScopePreservation } from "../graph-authoring/expressionScopePreservation";
import { sequenceActivity, sequenceNode, writeLine, leafNode, setVariableActivity } from "./fixtures";

let root: Root, container: HTMLDivElement;
let context: NativeBlockContext;
const validate = vi.fn<(document: NativeBlockDocument) => Promise<NativeBlockValidation>>();
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

it.each(["catalog", "expression support", "same-primary selection"])("rechecks %s at a deferred host commit boundary", async change => {
  let guardedCommit!: () => boolean, finish!: () => void;
  let committed = false;
  apply.mockImplementation((_document, _selection, isCurrent: () => boolean) => new Promise<void>(resolve => {
    guardedCommit = isCurrent;
    finish = () => { if (!isCurrent()) { resolve(); return; } committed = true; resolve(); };
  }));
  click("Extract block…"); name("Review"); click("Preview change"); await settle();
  click("Apply change"); await settle();
  expect(guardedCommit()).toBe(true);
  if (change === "catalog") context = { ...context, catalog: [] };
  if (change === "expression support") context = { ...context, expressionScopePreservation: { revision: "replacement", allowsVariableAddition: () => true } };
  if (change === "same-primary selection") context = { ...context, sourceKey: "resource:1/selection:two" };
  render();
  expect(guardedCommit()).toBe(false);
  finish(); await settle(); expect(committed).toBe(false);
});

it("refuses result-crossing preview when the composing host has no language preservation support", async () => {
  const consumer = leafNode("after");
  consumer.inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: {
    language: "JavaScript", source: "args.prior + 1", resultType: { alias: "Object", collectionKind: "single" }, options: {}, capabilityProfile: "binding-pure-v1",
    parameters: { prior: { kind: "ActivityResult", producerNodeId: "one", projectionKey: "$result" } }
  } } }];
  (context.document.rootActivity.structure!.payload.activities as ReturnType<typeof leafNode>[]).push(consumer);
  context = { ...context, catalog: [...context.catalog, setVariableActivity] };
  expect(planExtractNativeBlock({ ...context, expressionScopePreservation: { revision: "available", allowsVariableAddition: () => true } },
    { selectedNodeIds: ["one", "two"], nodeId: "block", activityVersionId: sequenceActivity.activityVersionId, displayName: "Grouped" }).status).toBe("ready");
  context = { ...context, expressionScopePreservation: composeExpressionScopePreservation([]) }; render();
  const before = structuredClone(context.document);
  click("Extract block…"); name("Grouped"); click("Preview change"); await settle();
  expect(container.querySelector("[role=alert]")?.textContent).toContain("Adding a result variable cannot preserve");
  expect(button("Apply change").disabled).toBe(true);
  expect(validate).not.toHaveBeenCalled(); expect(apply).not.toHaveBeenCalled(); expect(context.document).toEqual(before);
});

it("preserves explicit result bindings through the expanded editor input boundary", async () => {
  const consumer = leafNode("after");
  consumer.inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: {
    language: "JavaScript", source: "args.prior + 1", resultType: { alias: "Object", collectionKind: "single" }, options: {}, capabilityProfile: "binding-pure-v1",
    parameters: { prior: { kind: "ActivityResult", producerNodeId: "one", projectionKey: "$result" } }
  } } }];
  (context.document.rootActivity.structure!.payload.activities as ActivityNode[]).push(consumer);
  context = { ...context, catalog: [...context.catalog, setVariableActivity],
    document: { ...context.document, rootActivity: expandStateFromWire({ rootActivity: context.document.rootActivity }).rootActivity! },
    expressionScopePreservation: { revision: "available", allowsVariableAddition: () => true } };
  render();
  const before = structuredClone(context.document);
  click("Extract block…"); name("Grouped"); click("Preview change"); await settle();
  expect(validate).toHaveBeenCalledTimes(1);
  const candidate = validate.mock.calls[0][0];
  const wire = canonicalizeStateForWire({ rootActivity: candidate.rootActivity }).rootActivity!;
  const payload = wire.structure!.payload;
  expect(payload.variables).toHaveLength(1);
  const children = payload.activities as ActivityNode[];
  expect(children[0].structure!.payload.activities).toHaveLength(3);
  const transfer = (children[0].structure!.payload.activities as ActivityNode[]).find(node => node.nodeId.endsWith("-capture-1"))!;
  expect(transfer.structure).toBeNull();
  expect(normalizeActivityStructures(candidate.rootActivity, context.catalog)).toBe(candidate.rootActivity);
  const binding = children[1].inputs[0] as { value: { value: { source: string; parameters: unknown } } };
  expect(binding.value.value.source).toBe("args.prior + 1");
  expect(binding.value.value.parameters).toEqual({ prior: { kind: "Variable", declaringScopeNodeId: "root", variableKey: expect.any(String) } });
  // Validation and the resource commit both receive the editor form, including the rewritten binding.
  expect((candidate.rootActivity.structure!.payload.activities as ActivityNode[])[1].inputs).toEqual([]);
  click("Apply change"); await settle();
  expect(apply).toHaveBeenCalledTimes(1);
  expect(canonicalizeStateForWire({ rootActivity: apply.mock.calls[0][0].rootActivity }).rootActivity).toEqual(wire);
  expect(context.document).toEqual(before);
});
