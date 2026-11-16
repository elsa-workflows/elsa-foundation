import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import BpmnInterchangeCommands from "../graph-authoring/BpmnInterchangeCommands";
import type { NativeBlockContext, NativeBlockDocument } from "../graph-authoring/nativeBlocks";
import type { BpmnImportResult } from "../api/bpmnInterchange";
import { createActivityNode } from "../workflowAdapter";
import { bpmnActivity, writeLine } from "./fixtures";

let root: Root, container: HTMLDivElement, context: NativeBlockContext, result: BpmnImportResult, endpoint: StudioEndpointContext, file: File;
const validate = vi.fn(async (_document: NativeBlockDocument) => ({ state: "validated", messages: [] as string[] }));
const appliedCurrent: boolean[] = [];
const apply = vi.fn(async (_document: NativeBlockDocument, _selection: string[], current: () => boolean) => { appliedCurrent.push(current()); });
const postJson = vi.fn();
beforeEach(() => {
  validate.mockClear(); apply.mockClear(); postJson.mockReset(); appliedCurrent.length = 0;
  const owner = createActivityNode(bpmnActivity, "owner");
  owner.structure!.payload = { activities: [], elements: [{ elementId: "keep", elementType: "task" }], sequenceFlows: [] };
  const imported = createActivityNode(bpmnActivity, "imported");
  imported.structure!.payload = { activities: [createActivityNode(writeLine, "new-child")], elements: [{ elementId: "new-task", elementType: "task", childNodeId: "new-child" }], sequenceFlows: [] };
  context = { document: { rootActivity: owner, layout: [], activityPresentation: [] }, catalog: [bpmnActivity, writeLine], scope: [], canEdit: true, sourceKey: "resource/1/rev/1", referenceContext: [] };
  result = { processNode: imported, analysis: { processIds: ["process"], elementCounts: { task: 1 }, issues: [], hostInventoryVerified: true, canExecute: true,
    capabilities: [{ elementId: "new-task", capability: "RetainedBinding", availability: "Available", reason: "Exact version is available", retainedBinding: { activityVersionId: writeLine.activityVersionId, inputKeys: ["Text"] } }] } };
  postJson.mockImplementation(async (_path: string, body: { confirmedBindings?: string[] }) => {
    if (!body.confirmedBindings?.includes("new-task")) return { ...result, analysis: { ...result.analysis, canExecute: false, capabilities: result.analysis.capabilities!.map(item => ({ ...item, availability: "RequiresBinding" })) } };
    return result;
  });
  endpoint = { http: { postJson } } as unknown as StudioEndpointContext;
  file = new File(["<definitions />"], "original.bpmn"); Object.defineProperty(file, "text", { value: async () => "<definitions />" });
  container = document.createElement("div"); document.body.append(container); root = createRoot(container); render();
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
function render() { flushSync(() => root.render(<BpmnInterchangeCommands endpoint={endpoint} context={context} createPosition={{ x: 0, y: 0 }} selectedNodeIds={[]} validate={validate} apply={apply} open={() => {}} bpmnImportFile={file} />)); }
function button(label: string) { const item = [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === label); if (!item) throw new Error(label); return item; }
function click(label: string) { flushSync(() => button(label).click()); }
async function loaded() { await vi.waitFor(() => expect(container.querySelector('input[type="checkbox"]')).not.toBeNull()); }
async function confirmAndPreview() {
  await loaded(); flushSync(() => container.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click());
  click("Preview imported body"); await vi.waitFor(() => expect(button("Apply imported body").disabled).toBe(false));
}
it("requires explicit retained-binding choice, previews the whole document, then validates and commits once", async () => {
  const original = structuredClone(context.document); await loaded();
  expect(postJson.mock.calls[0][1]).toEqual({ xml: "<definitions />" });
  expect(container.textContent).toContain(writeLine.activityVersionId);
  expect(button("Apply imported body").disabled).toBe(true);
  click("Preview imported body");
  await vi.waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent).toContain("Resolve every binding"));
  expect(validate).not.toHaveBeenCalled(); expect(apply).not.toHaveBeenCalled();
  await confirmAndPreview();
  expect(postJson.mock.calls.at(-1)![1]).toEqual({ xml: "<definitions />", confirmedBindings: ["new-task"] });
  expect(container.textContent).toContain("Proposed resource"); expect(context.document).toEqual(original);
  click("Apply imported body"); await vi.waitFor(() => expect(apply).toHaveBeenCalledTimes(1));
  expect(validate).toHaveBeenCalledTimes(2); expect(appliedCurrent).toEqual([true]);
  expect(apply.mock.calls[0][0].rootActivity.nodeId).toBe("owner");
  expect(apply.mock.calls[0][2]()).toBe(false);
});
it("keeps successive binding confirmations after React restores controlled checkbox values", async () => {
  await loaded();
  const capability = result.analysis.capabilities![0];
  result.analysis.capabilities = [capability, { ...capability, elementId: "second-task" }, { ...capability, elementId: "third-task" }];
  file = new File(["<definitions />"], "multiple-bindings.bpmn");
  Object.defineProperty(file, "text", { value: async () => "<definitions />" });
  render();
  await vi.waitFor(() => expect(container.querySelectorAll('input[type="checkbox"]')).toHaveLength(3));
  const choices = [...container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
  for (const choice of choices) {
    choice.click();
    await vi.waitFor(() => expect(choice.checked).toBe(true));
  }
  expect(choices.every(choice => choice.checked)).toBe(true);
  choices[1].click();
  await vi.waitFor(() => expect(choices.map(choice => choice.checked)).toEqual([true, false, true]));
});
it("refuses conversion loss and retains the original file without committing", async () => {
  result.analysis.issues = [{ severity: "Dropped", elementId: "new-task", message: "Unsupported event would be lost" }];
  await confirmAndPreviewRefused();
  expect(container.querySelector('[role="alert"]')?.textContent).toContain("meaning could be lost");
  expect(button("Download original file").disabled).toBe(false);
  expect(apply).not.toHaveBeenCalled(); expect(validate).not.toHaveBeenCalled();
});
async function confirmAndPreviewRefused() {
  await loaded(); flushSync(() => container.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click()); click("Preview imported body");
  await vi.waitFor(() => expect(container.querySelector('[role="alert"]')).not.toBeNull());
}
it("disables a proposal when source authority changes and never commits it", async () => {
  await confirmAndPreview(); context = { ...context, sourceKey: "resource/1/rev/2", canEdit: false }; render();
  expect(button("Apply imported body").disabled).toBe(true); expect(container.textContent).toContain("resource changed"); expect(apply).not.toHaveBeenCalled();
});
it("checks freshness again after final candidate validation", async () => {
  await confirmAndPreview();
  let finish!: (value: { state: string; messages: string[] }) => void;
  validate.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  click("Apply imported body"); await vi.waitFor(() => expect(validate).toHaveBeenCalledTimes(2));
  context = { ...context, sourceKey: "resource/1/rev/2" }; render();
  finish({ state: "validated", messages: [] });
  await vi.waitFor(() => expect(container.textContent).toContain("Nothing was applied")); expect(apply).not.toHaveBeenCalled();
});
