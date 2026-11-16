import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { NativeBlockCommands } from "../graph-authoring/NativeBlockCommands";
import type { BlockPromotionHost } from "../graph-authoring/BlockPromotionDialog";
import type { NativeBlockContext } from "../graph-authoring/nativeBlocks";
import { context as promotionContext } from "./blockPromotionFixtures";

let root: Root, container: HTMLDivElement, nativeContext: NativeBlockContext, host: BlockPromotionHost, selected: string[];
beforeEach(() => {
  const full = promotionContext();
  // The promoted block sits in the root Sequence scope the commands act on.
  nativeContext = { document: full.document, catalog: full.catalog, scope: [], canEdit: true, referenceContext: [], sourceKey: "workflow:1" };
  host = { resourceKind: full.resourceKind, publicInputs: full.publicInputs, capabilities: full.capabilities, capabilitiesState: "ready",
    expressionBoundary: { revision: "none", allows: () => false }, refreshCatalog: vi.fn(), validate: vi.fn(), apply: vi.fn() };
  selected = ["block"];
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });

function render(promotion: BlockPromotionHost | null = host) {
  flushSync(() => root.render(<NativeBlockCommands endpoint={{} as StudioEndpointContext} context={nativeContext} selectedNodeIds={selected} createPosition={{ x: 0, y: 0 }}
    validate={vi.fn()} apply={vi.fn()} open={vi.fn()} promotion={promotion ?? undefined} />));
}
const promote = () => [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Promote to reusable activity…");

it("offers promotion only where the owning resource can author reusable activities", () => {
  render(null);
  expect(promote()).toBeUndefined();
});

it.each([
  ["a read-only document", () => { nativeContext = { ...nativeContext, canEdit: false }; }, "Open an editable draft to promote a block."],
  ["a selection that is not a block", () => { selected = ["after"]; }, "Select one named block to promote it to a reusable activity."],
  ["capabilities still loading", () => { host = { ...host, capabilitiesState: "loading" }; }, "Loading reusable activity authoring…"],
  ["a server without reusable authoring", () => { host = { ...host, capabilitiesState: "failed" }; }, "Reusable activity authoring is unavailable on this server."]
])("disables promotion for %s and says why", (_, change, reason) => {
  change(); render();
  expect(promote()!.disabled).toBe(true);
  expect(promote()!.title).toBe(reason);
});

it.each([["loading", "Loading reusable activity authoring…"], ["failed", "Reusable activity authoring is unavailable on this server."]] as const)(
  "shows the %s reason beside a selected block, not only in a tooltip", (state, reason) => {
    host = { ...host, capabilitiesState: state }; render();
    const description = document.getElementById(promote()!.getAttribute("aria-describedby")!);
    expect(description?.textContent).toBe(reason);
  });

it("opens the review dialog for the selected block", async () => {
  render();
  expect(promote()!.disabled).toBe(false);
  flushSync(() => promote()!.click());
  await vi.waitFor(() => expect(container.querySelector("[role=dialog]")?.getAttribute("aria-label")).toBe("Promote to reusable activity"));
  expect(container.textContent).toContain("Proposed reusable activity");
});
