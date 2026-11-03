import React, { Activity } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioWorkspaceTabProvider, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { WorkflowTag } from "../api/workflowOrganization";
import { WorkflowTagEditor } from "../workflow-editor/WorkflowTagEditor";
import { button, click, fill, select } from "./workflowDefinitionTestHelpers";

let mounted: { root: Root; container: HTMLDivElement } | undefined;
afterEach(() => { if (mounted) { flushSync(() => mounted!.root.unmount()); mounted.container.remove(); mounted = undefined; } clearApiCapabilityCache(); });
const existing: WorkflowTag = { id: "tag-server", name: "Region", mode: "controlled", cardinality: "multiple", deprecated: false, revision: "revision-1", values: [{ id: "value-existing", name: "Europe" }] };

function render(tag?: WorkflowTag) {
  const postJson = vi.fn(async () => ({ ...existing, values: [{ id: "value-new", name: "Europe" }] }));
  const putJson = vi.fn(async () => ({ ...existing, revision: "revision-2", values: [...existing.values, { id: "value-new", name: "Asia" }] }));
  const context = { baseUrl: "https://catalog.test/", http: { postJson, putJson, getJson: async () => ({ capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1", links: [{ rel: "workflow-tags", href: "tags" }, { rel: "workflow-tag", href: "tags/{tagId}", templated: true }] }] }) } } as unknown as StudioEndpointContext;
  const container = document.createElement("div"); document.body.appendChild(container);
  const root = createRoot(container); mounted = { root, container };
  const onCreated = vi.fn();
  let visible = true;
  const setVisibility = (mode: "visible" | "hidden") => {
    visible = mode === "visible";
    flushSync(() => root.render(<StudioWorkspaceTabProvider value={{ id: "tags", active: visible, location: "/workflows/tags/new", isActive: () => visible, markPendingWork: () => undefined, setTitle: () => undefined }}><Activity mode={mode}><WorkflowTagEditor context={context} tag={tag} onBack={() => undefined} onCreated={onCreated} /></Activity></StudioWorkspaceTabProvider>));
  };
  setVisibility("visible");
  return { container, postJson, putJson, onCreated, setVisibility };
}

describe("workflow controlled value wire identities", () => {
  it("uses page actions with Create tag as the filled primary and Cancel as default", () => {
    const { container } = render();
    const close = button(container, "Cancel")!;
    const create = button(container, "Create tag")!;
    expect(close.classList.contains("studio-button")).toBe(true);
    expect(close.getAttribute("data-variant")).toBe("default");
    expect(create.classList.contains("studio-button")).toBe(true);
    expect(create.getAttribute("data-variant")).toBe("primary");
    expect(create.getAttribute("type")).toBe("submit");
  });

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
    const { container, putJson } = render(existing);
    await click(button(container, "Add controlled value"));
    await fill(container.querySelector('input[aria-label="Value name 2"]'), "Asia");
    await click(button(container, "Save tag"));
    await vi.waitFor(() => expect(putJson).toHaveBeenCalledOnce());
    expect(putJson.mock.calls[0]).toEqual(["/tags/tag-server", expect.objectContaining({ revision: "revision-1", values: [{ id: "value-existing", name: "Europe" }, { id: "", name: "Asia", deprecated: false }] })]);
  });
  it("finishes a pending creation in a hidden tab and navigates only when restored", async () => {
    const { container, postJson, onCreated, setVisibility } = render();
    let resolve!: (value: WorkflowTag) => void;
    postJson.mockImplementationOnce(() => new Promise<WorkflowTag>(done => { resolve = done; }));
    await fill(container.querySelector('[aria-label="Tag name"]'), "Region");
    await click(button(container, "Create tag"));
    await vi.waitFor(() => expect(postJson).toHaveBeenCalledOnce());
    setVisibility("hidden");
    await new Promise(done => setTimeout(done, 20));
    resolve(existing);
    await new Promise(done => setTimeout(done, 20));
    expect(onCreated).not.toHaveBeenCalled();
    setVisibility("visible");
    await vi.waitFor(() => expect(onCreated).toHaveBeenCalledOnce());
    expect(onCreated).toHaveBeenCalledWith(existing);
    expect(button(container, "Create tag")?.hasAttribute("disabled")).toBe(false);
  });

});
