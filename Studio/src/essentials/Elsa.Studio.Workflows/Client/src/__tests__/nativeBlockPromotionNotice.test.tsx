import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "../workflowTypes";
import type { BlockPromotionHost } from "../graph-authoring/BlockPromotionDialog";
import type { NativeBlockContext } from "../graph-authoring/nativeBlocks";
import { context as promotionContext } from "./blockPromotionFixtures";

// The dialog reports a completed replacement; this file checks only how the commands present it afterwards.
vi.mock("../graph-authoring/BlockPromotionDialog", () => ({
  BlockPromotionDialog: ({ onReplaced }: { onReplaced(message: string, versionId: string): void }) =>
    <button type="button" onClick={() => onReplaced("Replaced “Prepare shipment”. Undo restores the block.", "published-version")}>Finish replacement</button>
}));
const { NativeBlockCommands } = await import("../graph-authoring/NativeBlockCommands");

let root: Root, container: HTMLDivElement, nativeContext: NativeBlockContext;
const host = { capabilitiesState: "ready" } as BlockPromotionHost;
const original = promotionContext().document;
const replaced = structuredClone(original);
const children = (node: ActivityNode) => node.structure!.payload.activities as ActivityNode[];
children(replaced.rootActivity)[0] = { ...children(replaced.rootActivity)[0], activityVersionId: "published-version", structure: null };

beforeEach(() => {
  const full = promotionContext();
  nativeContext = { document: original, catalog: full.catalog, scope: [], canEdit: true, referenceContext: [], sourceKey: "workflow:1" };
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  render();
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
function render() {
  flushSync(() => root.render(<NativeBlockCommands endpoint={{} as StudioEndpointContext} context={nativeContext} selectedNodeIds={["block"]} createPosition={{ x: 0, y: 0 }}
    validate={vi.fn()} apply={vi.fn()} open={vi.fn()} promotion={host} />));
}
const button = (label: string) => [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === label)!;
const notice = () => container.querySelector("[role=status]")?.textContent ?? null;

it("keeps the replacement notice while the call is in the document and retires it after Undo", async () => {
  flushSync(() => button("Promote to reusable activity…").click());
  await vi.waitFor(() => expect(button("Finish replacement")).toBeTruthy());
  flushSync(() => button("Finish replacement").click());
  // The host may still render the previous document once; the notice waits for the committed call.
  expect(notice()).toContain("Undo restores the block.");
  nativeContext = { ...nativeContext, document: replaced }; render();
  expect(notice()).toContain("Undo restores the block.");
  nativeContext = { ...nativeContext, document: structuredClone(replaced) }; render();
  expect(notice()).toContain("Undo restores the block.");
  nativeContext = { ...nativeContext, document: original }; render();
  await vi.waitFor(() => expect(notice()).toBeNull());
});
