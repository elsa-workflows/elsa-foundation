import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { WorkflowTag } from "../api/workflowOrganization";
import { WorkflowTagCatalogDialog } from "../workflow-editor/WorkflowTagCatalogDialog";
import { button, click, fill, select } from "./workflowDefinitionTestHelpers";

let mounted: { root: Root; container: HTMLDivElement } | undefined;
afterEach(() => { if (mounted) { flushSync(() => mounted!.root.unmount()); mounted.container.remove(); mounted = undefined; } clearApiCapabilityCache(); });
const existing: WorkflowTag = { id: "tag-server", name: "Region", mode: "controlled", cardinality: "multiple", deprecated: false, revision: "revision-1", values: [{ id: "value-existing", name: "Europe" }] };

function render(tags: WorkflowTag[] = []) {
  const postJson = vi.fn(async () => ({ ...existing, values: [{ id: "value-new", name: "Europe" }] }));
  const putJson = vi.fn(async () => ({ ...existing, revision: "revision-2", values: [...existing.values, { id: "value-new", name: "Asia" }] }));
  const context = { baseUrl: "https://catalog.test/", http: { postJson, putJson, getJson: async () => ({ capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1", links: [{ rel: "workflow-tags", href: "tags" }, { rel: "workflow-tag", href: "tags/{tagId}", templated: true }] }] }) } } as unknown as StudioEndpointContext;
  const container = document.createElement("div"); document.body.appendChild(container);
  const root = createRoot(container); mounted = { root, container };
  flushSync(() => root.render(<WorkflowTagCatalogDialog context={context} tags={tags} onClose={() => undefined} onSaved={() => undefined} />));
  return { container, postJson, putJson };
}

describe("workflow controlled value wire identities", () => {
  it("sends empty IDs for new controlled values so the server allocates their identities", async () => {
    const { container, postJson } = render();
    await fill(container.querySelector('input[aria-label="Tag name"]'), "Region");
    await select(container.querySelector('select[aria-label="Tag kind"]'), "controlled");
    await click(button(container, "Add controlled value"));
    await fill(container.querySelector('input[aria-label="Value name 1"]'), "Europe");
    await click(button(container, "Create tag"));
    await vi.waitFor(() => expect(postJson).toHaveBeenCalledOnce());
    expect(postJson.mock.calls[0]).toEqual(["/tags", expect.objectContaining({ values: [{ id: "", name: "Europe", deprecated: false }] })]);
  });

  it("preserves known server IDs and leaves only newly added values unassigned", async () => {
    const { container, putJson } = render([existing]);
    await select(container.querySelector('select[aria-label="Tag to manage"]'), existing.id);
    await click(button(container, "Add controlled value"));
    await fill(container.querySelector('input[aria-label="Value name 2"]'), "Asia");
    await click(button(container, "Save tag"));
    await vi.waitFor(() => expect(putJson).toHaveBeenCalledOnce());
    expect(putJson.mock.calls[0]).toEqual(["/tags/tag-server", expect.objectContaining({ revision: "revision-1", values: [{ id: "value-existing", name: "Europe" }, { id: "", name: "Asia", deprecated: false }] })]);
  });
});
