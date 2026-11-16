import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionVersionView } from "../activityDefinitionTypes";
import type { ActivityNode } from "../workflowTypes";
import { planBlockPromotion } from "../graph-authoring/blockPromotion";
import type { BlockPromotionHost } from "../graph-authoring/BlockPromotionDialog";
import type { NativeBlockContext } from "../graph-authoring/nativeBlocks";
import { context as promotionContext, descriptor } from "./blockPromotionFixtures";

const api = vi.hoisted(() => ({ createActivityDefinition: vi.fn(), getActivityDefinitionVersion: vi.fn() }));
vi.mock("../api/activityDesign", async original => ({ ...(await original<typeof import("../api/activityDesign")>()), ...api }));
// The ordinary publication review has its own suite; here it reports one exact applied outcome.
vi.mock("../ActivityDefinitionPublicationReview", () => ({
  ActivityDefinitionPublicationReview: ({ onPublished, draftId }: { onPublished(outcome: { definitionVersionId: string; version: string }): void; draftId: string }) =>
    <button type="button" onClick={() => onPublished({ definitionVersionId: "published-version", version: "1.0.0" })}>Publish {draftId}</button>
}));
const { BlockPromotionDialog } = await import("../graph-authoring/BlockPromotionDialog");

let root: Root, container: HTMLDivElement, nativeContext: NativeBlockContext, host: BlockPromotionHost;
const endpoint = {} as StudioEndpointContext;
const onClose = vi.fn(), onReplaced = vi.fn();
const children = (node: ActivityNode) => node.structure!.payload.activities as ActivityNode[];

beforeEach(() => {
  const full = promotionContext();
  // The published version is not in the catalog until publication; the host's catalog refresh adds it.
  nativeContext = { document: full.document, catalog: full.catalog.filter(item => item.activityVersionId !== "published-version"), scope: full.scope,
    canEdit: true, referenceContext: full.referenceContext, sourceKey: full.sourceKey };
  host = { resourceKind: full.resourceKind, publicInputs: full.publicInputs, capabilities: full.capabilities, capabilitiesState: "ready",
    expressionBoundary: { revision: "none", allows: () => false },
    refreshCatalog: vi.fn(async () => {
      if (!nativeContext.catalog.some(item => item.activityVersionId === "published-version"))
        nativeContext = { ...nativeContext, catalog: [...nativeContext.catalog, descriptor("published-version", "Reusable.Reviewed")] };
      render();
    }),
    validate: vi.fn(async () => ({ state: "validated", messages: [] })), apply: vi.fn() };
  api.createActivityDefinition.mockReset().mockResolvedValue({
    definition: { definitionId: "definition-1", displayName: "Prepare shipment", activityTypeKey: "Reusable.Prepare", category: "Reusable activities", contentAuthority: "Design" },
    draft: { draftId: "draft-1", definitionId: "definition-1", revision: 1, status: "Draft", providerKey: "elsa.activity-graph", providerSchemaVersion: "2", updatedAt: "" }
  });
  api.getActivityDefinitionVersion.mockReset().mockImplementation(async () => publishedView());
  onClose.mockReset(); onReplaced.mockReset();
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  render();
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });

function render() {
  flushSync(() => root.render(<BlockPromotionDialog endpoint={endpoint} context={nativeContext} createPosition={{ x: 0, y: 0 }} host={host} blockNodeId="block" onClose={onClose} onReplaced={onReplaced} />));
}
function reviewed() {
  const plan = planBlockPromotion({ ...promotionContext(), document: nativeContext.document }, "block");
  if (plan.status !== "ready") throw new Error(JSON.stringify(plan));
  return plan;
}
function publishedView(change: Partial<ActivityDefinitionVersionView> = {}): ActivityDefinitionVersionView {
  const plan = reviewed();
  return { definition: { definitionId: "definition-1", activityTypeKey: "Reusable.Prepare", category: "Reusable activities", displayName: "Prepare shipment", contentAuthority: "Design" as never },
    versionId: "published-version", version: "1.0.0", lifecycle: "active", publishedAt: "", contract: structuredClone(plan.candidate.contract),
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", manifestFingerprint: "x", payload: structuredClone(plan.candidate.provider.payload) }, ...change };
}
function button(label: string) {
  const item = [...container.querySelectorAll<HTMLButtonElement>("button")].find(candidate => candidate.textContent === label);
  if (!item) throw new Error(`${label} in ${container.textContent}`);
  return item;
}
const click = (label: string) => flushSync(() => button(label).click());
async function settle(times = 3) { for (let index = 0; index < times; index++) { await new Promise(resolve => setTimeout(resolve, 0)); flushSync(() => {}); } }
async function createAndPublish() {
  click("Create reusable activity draft"); await settle();
  click("Publish draft-1"); await settle();
}

it("reviews the proposed interface and changes nothing until an exact published version replaces the block", async () => {
  const before = JSON.stringify(nativeContext.document);
  expect(container.textContent).toContain("1 input, 0 outputs and 1 outcome");
  expect(container.textContent).toContain("Greeting");
  await createAndPublish();
  expect(api.createActivityDefinition).toHaveBeenCalledTimes(1);
  expect(api.createActivityDefinition.mock.calls[0][1]).toMatchObject({ displayName: "Prepare shipment", category: "Reusable activities", provider: { providerKey: "elsa.activity-graph" } });
  expect(host.apply).not.toHaveBeenCalled();
  expect(JSON.stringify(nativeContext.document)).toBe(before);

  expect(container.textContent).toContain("Published version 1.0.0");
  click("Replace block with this version"); await settle(6);

  expect(host.refreshCatalog).toHaveBeenCalledTimes(1);
  expect(api.getActivityDefinitionVersion).toHaveBeenCalledWith(endpoint, "published-version");
  expect(host.validate).toHaveBeenCalledTimes(1);
  expect(host.apply).toHaveBeenCalledTimes(1);
  const [document, , selection] = vi.mocked(host.apply).mock.calls[0];
  expect(children(document.rootActivity)[0]).toMatchObject({ nodeId: "block", activityVersionId: "published-version", structure: null });
  expect(selection).toEqual(["block"]);
  expect(onReplaced.mock.calls[0][0]).toContain("Undo restores the block; the reusable activity stays published.");
  expect(onReplaced.mock.calls[0][1]).toBe("published-version");
});

it("keeps the document when the server refuses to create the reusable activity", async () => {
  api.createActivityDefinition.mockRejectedValueOnce(new StudioHttpError(500, "Internal Server Error", null));
  click("Create reusable activity draft"); await settle();
  expect(container.textContent).toContain("The reusable activity was not created. Your document has not changed.");
  expect(button("Create reusable activity draft").disabled).toBe(false);
  expect(host.apply).not.toHaveBeenCalled();
});

it("does not create again when the create outcome is unknown", async () => {
  api.createActivityDefinition.mockRejectedValueOnce(new TypeError("Failed to fetch"));
  click("Create reusable activity draft"); await settle();
  expect(container.textContent).toContain("could not confirm whether the reusable activity was created");
  expect(button("Create reusable activity draft").disabled).toBe(true);
  expect(api.createActivityDefinition).toHaveBeenCalledTimes(1);
});

it.each([
  ["a different implementation", () => publishedView({ provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", manifestFingerprint: "x", payload: { rootActivity: { nodeId: "other" } } } }), "implementation differs"],
  ["a retired version", () => publishedView({ lifecycle: "retired" }), "retired"]
])("refuses to replace with %s and keeps the block", async (_, view, reason) => {
  api.getActivityDefinitionVersion.mockImplementation(async () => view());
  await createAndPublish();
  click("Replace block with this version"); await settle(6);
  expect(container.textContent).toContain("The block was not replaced.");
  expect(container.textContent).toContain(reason);
  expect(host.validate).not.toHaveBeenCalled();
  expect(host.apply).not.toHaveBeenCalled();
});

it("refuses when the block changed after review", async () => {
  await createAndPublish();
  nativeContext = { ...nativeContext, document: { ...nativeContext.document, activityPresentation: [...nativeContext.document.activityPresentation, { nodeId: "inside", displayName: "Edited" }] } };
  render();
  expect(container.textContent).toContain("The block changed after you created the reusable activity.");
  click("Replace block with this version"); await settle(6);
  expect(container.textContent).toContain("Review the updated proposal before replacing it.");
  expect(host.apply).not.toHaveBeenCalled();
});

it("refuses when the owning resource does not accept the replacement", async () => {
  vi.mocked(host.validate).mockResolvedValueOnce({ state: "denied", messages: ["You no longer have permission to edit this workflow."] });
  await createAndPublish();
  click("Replace block with this version"); await settle(6);
  expect(container.textContent).toContain("You no longer have permission to edit this workflow.");
  expect(host.apply).not.toHaveBeenCalled();
});

it("resumes with an exact published version chosen later", async () => {
  nativeContext = { ...nativeContext, catalog: [...nativeContext.catalog, { ...descriptor("published-version", "Reusable.Reviewed"), displayName: "Prepare shipment", activityDefinitionVersionId: "published-version" }] };
  render();
  flushSync(() => (container.querySelectorAll<HTMLInputElement>("input[type=radio]")[1]).click());
  const select = container.querySelector("select")!;
  flushSync(() => { select.value = "published-version"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  click("Replace block with this version"); await settle(6);
  expect(api.createActivityDefinition).not.toHaveBeenCalled();
  expect(host.apply).toHaveBeenCalledTimes(1);
});

it("cancels without creating anything", () => {
  click("Cancel");
  expect(onClose).toHaveBeenCalledTimes(1);
  expect(api.createActivityDefinition).not.toHaveBeenCalled();
  expect(host.apply).not.toHaveBeenCalled();
});

it("keeps the dialog open while a create is in flight, so a late success is not lost", async () => {
  let finish!: (value: unknown) => void;
  api.createActivityDefinition.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
  click("Create reusable activity draft"); await settle(1);
  expect(button("Cancel").disabled).toBe(true);
  window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })); await settle(1);
  expect(onClose).not.toHaveBeenCalled();
  finish({ definition: { definitionId: "definition-1", displayName: "Prepare shipment" }, draft: { draftId: "draft-1", definitionId: "definition-1", revision: 1 } }); await settle();
  expect(container.textContent).toContain("Draft created");
  expect(button("Close").disabled).toBe(false);
});

it("shows where each input's value comes from and how many steps the block has", () => {
  expect(container.textContent).toContain("1 step; 1 input");
  expect(container.textContent).toContain("Greeting · String · from workflow input Greeting");
});
