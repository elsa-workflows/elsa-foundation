import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { BlockPromotionHost } from "../graph-authoring/BlockPromotionDialog";
import type { NativeBlockContext } from "../graph-authoring/nativeBlocks";
import { context as promotionContext } from "./blockPromotionFixtures";

const api = vi.hoisted(() => ({ createActivityDefinition: vi.fn(), getActivityDefinitionVersion: vi.fn() }));
vi.mock("../api/activityDesign", async original => ({ ...(await original<typeof import("../api/activityDesign")>()), ...api }));
// The ordinary review reports a rejected or unknown publication by never reporting an applied outcome.
vi.mock("../ActivityDefinitionPublicationReview", () => ({
  ActivityDefinitionPublicationReview: ({ onPublicationPending }: { onPublicationPending(pending: boolean): void }) =>
    <button type="button" onClick={() => { onPublicationPending(true); onPublicationPending(false); }}>Publish (rejected)</button>
}));
const { BlockPromotionDialog } = await import("../graph-authoring/BlockPromotionDialog");

let root: Root, container: HTMLDivElement, host: BlockPromotionHost, nativeContext: NativeBlockContext;
beforeEach(() => {
  const full = promotionContext();
  nativeContext = { document: full.document, catalog: full.catalog, scope: [], canEdit: true, referenceContext: [], sourceKey: "workflow:1" };
  host = { resourceKind: full.resourceKind, publicInputs: full.publicInputs, capabilities: full.capabilities, capabilitiesState: "ready",
    expressionBoundary: { revision: "none", allows: () => false }, refreshCatalog: vi.fn(), validate: vi.fn(), apply: vi.fn() };
  api.createActivityDefinition.mockReset().mockResolvedValue({ definition: { definitionId: "definition-1", displayName: "Prepare shipment" }, draft: { draftId: "draft-1", definitionId: "definition-1", revision: 1 } });
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<BlockPromotionDialog endpoint={{} as StudioEndpointContext} context={nativeContext} createPosition={{ x: 0, y: 0 }} host={host} blockNodeId="block" onClose={vi.fn()} onReplaced={vi.fn()} />));
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
const buttons = () => [...container.querySelectorAll<HTMLButtonElement>("button")].map(button => button.textContent);

it("offers no replacement when publication is rejected and keeps the document and draft", async () => {
  const before = JSON.stringify(nativeContext.document);
  flushSync(() => [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Create reusable activity draft")!.click());
  await vi.waitFor(() => expect(container.textContent).toContain("Draft created"));
  flushSync(() => [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Publish (rejected)")!.click());
  expect(buttons()).not.toContain("Replace block with this version");
  expect(container.textContent).toContain("Closing this dialog keeps the draft; your document has not changed.");
  expect(host.apply).not.toHaveBeenCalled();
  expect(api.getActivityDefinitionVersion).not.toHaveBeenCalled();
  expect(JSON.stringify(nativeContext.document)).toBe(before);
});
