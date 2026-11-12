import React from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { applyCreateDialogPrefill } from "../workflow-editor/workflowLibraryWeaver";
import type { CreateWorkflowDraft } from "../workflow-editor/editorTypes";
import * as templateApi from "../workflowTemplates";
import type { WorkflowTemplate } from "../workflowTemplates";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CreateWorkflowDialog } from "../workflow-editor/CreateWorkflowDialog";
import { flowchartActivity } from "./fixtures/workflowCatalog";
import { testApi } from "./routeRenderingHelpers";

let container: HTMLDivElement;
let opener: HTMLButtonElement;
let root: ReturnType<typeof createRoot>;

beforeEach(() => {
  vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => { callback(0); return 1; });
  vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => undefined);
  opener = document.createElement("button");
  container = document.createElement("div");
  document.body.append(opener, container);
  opener.focus();
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  opener.remove();
  vi.restoreAllMocks();
});

function renderDialog(creating = false) {
  const onClose = vi.fn();
  flushSync(() => root.render(<CreateWorkflowDialog
    draft={{ name: "QA workflow", description: "", rootActivityVersionId: null }}
    catalog={[flowchartActivity()]}
    creating={creating}
    ai={testApi().weaver}
    onChange={vi.fn()}
    onClose={onClose}
    onSubmit={vi.fn()}
  />));
  return onClose;
}

function tabFrom(element: HTMLElement, shiftKey = false) {
  element.focus();
  element.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey, bubbles: true, cancelable: true }));
}

describe("Create Workflow keyboard behavior", () => {
  it("uses StudioDialogFooter so Create is the filled primary and Cancel stays default", () => {
    renderDialog();
    const cancel = [...container.querySelectorAll("button")].find(button => button.textContent === "Cancel")!;
    const submit = container.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(cancel.classList.contains("studio-button")).toBe(true);
    expect(cancel.getAttribute("data-variant")).toBe("default");
    expect(submit.classList.contains("studio-button")).toBe(true);
    expect(submit.getAttribute("data-variant")).toBe("primary");
    expect(submit.textContent).toBe("Create");
  });

  it("contains forward and backward keyboard focus, closes on Escape and restores the opener", () => {
    const close = renderDialog();
    const startingPoint = container.querySelector<HTMLSelectElement>('[aria-label="Starting point"]')!;
    const submit = container.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(document.activeElement).toBe(startingPoint);
    tabFrom(submit);
    expect(document.activeElement).toBe(startingPoint);
    tabFrom(startingPoint, true);
    expect(document.activeElement).toBe(submit);
    submit.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true }));
    expect(close).toHaveBeenCalledOnce();
    flushSync(() => root.render(null));
    expect(document.activeElement).toBe(opener);
  });

  it("keeps an in-flight creation open on Escape", () => {
    const close = renderDialog(true);
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true }));
    expect(close).not.toHaveBeenCalled();
  });
});

const catalog = [flowchartActivity()];
const templates: WorkflowTemplate[] = [
  { id: "one", name: "Hello World", description: "Sends a greeting.", revision: "r1", createdAt: "", lastModifiedAt: "", content: { state: { rootActivity: catalog[0].authoringTemplate! }, layout: [] } },
  { id: "two", name: "Order processing", description: "Processes an order.", revision: "r1", createdAt: "", lastModifiedAt: "", content: { state: { rootActivity: catalog[0].authoringTemplate! }, layout: [] } },
  { id: "no-description", name: "No description", description: null, revision: "r1", createdAt: "", lastModifiedAt: "", content: { state: { rootActivity: catalog[0].authoringTemplate! }, layout: [] } }
];

async function editableDialog(initial: CreateWorkflowDraft = { name: "", description: "", rootActivityVersionId: null }) {
  vi.spyOn(templateApi, "listWorkflowTemplates").mockResolvedValue(templates);
  const ai = testApi().weaver;
  const onSubmit = vi.fn();
  let current = initial;
  let replace = (_draft: CreateWorkflowDraft) => {};
  const context = {} as StudioEndpointContext;
  function Harness() {
    const [draft, setDraft] = React.useState(initial);
    current = draft;
    replace = next => flushSync(() => setDraft(next));
    return <CreateWorkflowDialog context={context} draft={draft} catalog={catalog} creating={false} ai={ai}
      suggestMetadataAction={{ id: "suggest", label: "Suggest", contextKind: "workflow", placement: "toolbar", createPrompt: () => ({ message: "Suggest metadata" }) }}
      onChange={setDraft} onClose={vi.fn()} onSubmit={() => onSubmit(draft)} />;
  }
  flushSync(() => root.render(<Harness />));
  await vi.waitFor(() => expect(container.querySelector('option[value="one"]')).not.toBeNull());
  const field = (name: string) => container.querySelector<HTMLInputElement | HTMLTextAreaElement>(`[aria-label="${name}"]`)!;
  const select = (id: string) => flushSync(() => {
    const input = container.querySelector<HTMLSelectElement>('[aria-label="Starting point"]')!;
    input.value = id;
    input.dispatchEvent(new Event("change", { bubbles: true }));
  });
  const fill = (name: string, value: string) => flushSync(() => {
    const input = field(name);
    const prototype = input instanceof HTMLInputElement ? HTMLInputElement.prototype : HTMLTextAreaElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
  return { ai, onSubmit, field, select, fill, replace: (next: CreateWorkflowDraft) => replace(next), current: () => current };
}

describe("template metadata defaults", () => {
  it("places Starting point before metadata and refreshes untouched defaults, including missing descriptions and Blank", async () => {
    const dialog = await editableDialog();
    expect([...container.querySelectorAll("select, input, textarea")].slice(0, 3).map(input => input.getAttribute("aria-label")))
      .toEqual(["Starting point", "Display name", "Description"]);
    dialog.select("one");
    expect(dialog.current()).toMatchObject({ name: "Hello World", description: "Sends a greeting.", templateId: "one" });
    expect(dialog.current().templateContent).toEqual(templates[0].content);
    expect(dialog.current().templateContent).not.toBe(templates[0].content);
    dialog.select("two");
    expect(dialog.current()).toMatchObject({ name: "Order processing", description: "Processes an order." });
    dialog.select("no-description");
    expect(dialog.field("Description").value).toBe("");
    dialog.select("blank");
    expect(dialog.current()).toMatchObject({ name: "", description: "", templateId: undefined, templateContent: undefined });
  });

  it.each(["Display name", "Description"])("preserves an edited %s independently while updating the other field", async field => {
    const dialog = await editableDialog();
    dialog.select("one");
    dialog.fill(field, "My own value");
    dialog.select("two");
    expect(dialog.field(field).value).toBe("My own value");
    expect(dialog.field(field === "Display name" ? "Description" : "Display name").value)
      .toBe(field === "Display name" ? "Processes an order." : "Order processing");
  });

  it("preserves deliberate clears and retyped defaults, and submits exactly the visible metadata", async () => {
    const dialog = await editableDialog();
    dialog.select("one");
    dialog.fill("Display name", "Changed");
    dialog.fill("Display name", "Hello World");
    dialog.fill("Description", "");
    dialog.select("two");
    expect(dialog.field("Display name").value).toBe("Hello World");
    expect(dialog.field("Description").value).toBe("");
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    expect(dialog.onSubmit).toHaveBeenCalledWith(expect.objectContaining({ name: "Hello World", description: "", templateId: "two" }));
    dialog.fill("Display name", "");
    dialog.select("one");
    expect(dialog.field("Display name").value).toBe("");
    expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
  });

  it("uses HTTP workflow defaults and clears inherited metadata when returning to Blank", async () => {
    const dialog = await editableDialog();
    dialog.select("http");
    expect(dialog.current()).toMatchObject({ name: "HTTP request and response", description: "An HTTP endpoint connected to an HTTP response.", starter: "http" });
    dialog.select("blank");
    expect(dialog.current()).toMatchObject({ name: "", description: "", starter: undefined });
  });

  it("continues inheriting metadata when opened from Use template", async () => {
    const dialog = await editableDialog({ name: templates[0].name, description: templates[0].description!, rootActivityVersionId: null, templateId: "one", templateContent: structuredClone(templates[0].content) });
    dialog.select("two");
    expect(dialog.current()).toMatchObject({ name: templates[1].name, description: templates[1].description });
  });

  it("preserves an explicit empty Weaver prefill and restores inherited ownership when that prefill is undone", async () => {
    const dialog = await editableDialog();
    const original = dialog.current();
    const prefilled = applyCreateDialogPrefill(original, { name: "Custom workflow", description: "", templateId: "one" }, templates)!;
    dialog.replace(prefilled);
    dialog.select("two");
    expect(dialog.current()).toMatchObject({ name: "Custom workflow", description: "" });
    dialog.replace(original);
    dialog.select("two");
    expect(dialog.current()).toMatchObject({ name: "Order processing", description: "Processes an order." });
  });

  it.each([false, true])("preserves applied AI suggestions when switching templates (automatic: %s)", async autoApply => {
    const dialog = await editableDialog();
    dialog.select("one");
    const dispatch = vi.spyOn(dialog.ai, "dispatchPrompt");
    const click = (text: string) => flushSync(() => [...container.querySelectorAll("button")].find(button => button.textContent?.trim() === text)!.click());
    click("Suggest");
    click("Generate");
    flushSync(() => dialog.ai.publishPromptResult({ requestId: dispatch.mock.calls[0][0].requestId!, status: "completed", autoApply,
      text: JSON.stringify({ name: "Hello World", description: "My AI description" }) }));
    if (!autoApply) click("Apply");
    dialog.select("two");
    expect(dialog.current()).toMatchObject({ name: "Hello World", description: "My AI description" });
  });
});
