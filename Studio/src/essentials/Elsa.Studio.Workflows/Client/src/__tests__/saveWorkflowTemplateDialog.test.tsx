import React, { useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { studioWorkspaceTabRequestedEvent, type StudioEndpointContext, type StudioNotificationApi, type StudioWorkspaceTabRequest } from "@elsa-workflows/studio-sdk";
import { SaveWorkflowTemplateDialog } from "../workflow-editor/SaveWorkflowTemplateDialog";
import { setNotifications } from "../workflow-editor/notifications";
import { saveWorkflowTemplate, templateContent, type WorkflowTemplate } from "../workflowTemplates";
import { sequenceNode, leafNode } from "./fixtures";
import type { WorkflowDraft } from "../workflowTypes";

vi.mock("../workflowTemplates", async original => ({ ...await original<typeof import("../workflowTemplates")>(), saveWorkflowTemplate: vi.fn() }));
const draft: WorkflowDraft = { id: "draft", definitionId: "workflow", state: { rootActivity: sequenceNode("root", [leafNode("unsaved-step")]), variables: [], inputs: [], outputs: [] }, layout: [], validationErrors: [] };
const saved: WorkflowTemplate = { id: "template", revision: "1", name: "Starter", content: templateContent(draft), createdAt: "2026-11-05", lastModifiedAt: "2026-11-05" };
const notify = vi.fn<StudioNotificationApi["notify"]>(() => ({ id: "notice", update() {}, dismiss() {} }));
let element: HTMLDivElement;
let root: Root;
function mount() {
  function Host() {
    const [open, setOpen] = useState(false);
    return <><button onClick={() => setOpen(true)}>Save as template</button>{open && <SaveWorkflowTemplateDialog context={{} as StudioEndpointContext} draft={draft} name="Starter" onClose={() => setOpen(false)} />}</>;
  }
  flushSync(() => root.render(<Host />));
  const opener = element.querySelector("button")!; opener.focus(); flushSync(() => opener.click());
  return opener;
}
function submit() { flushSync(() => element.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }))); }
function field(value: string) {
  const input = element.querySelector("input")!;
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}
beforeEach(() => {
  element = document.createElement("div"); document.body.append(element); root = createRoot(element);
  notify.mockClear(); vi.mocked(saveWorkflowTemplate).mockReset();
  setNotifications({ notify, track: work => work });
});
afterEach(() => {
  flushSync(() => root.unmount()); element.remove(); setNotifications(undefined);
  window.history.replaceState({}, "", "/");
});

describe("Save template feedback", () => {
  it("closes on success, restores focus and publishes one backend-scoped toast after one pending save", async () => {
    window.history.replaceState({}, "", "/workflows/definitions?backend=secondary");
    let resolve!: (value: WorkflowTemplate) => void;
    vi.mocked(saveWorkflowTemplate).mockReturnValue(new Promise(done => { resolve = done; }));
    const opener = mount(); field("Unsaved starter"); submit(); submit();
    expect(saveWorkflowTemplate).toHaveBeenCalledTimes(1);
    expect(saveWorkflowTemplate).toHaveBeenCalledWith(expect.anything(), { name: "Unsaved starter", description: "", content: templateContent(draft) });
    expect(notify).not.toHaveBeenCalled();
    expect(element.querySelector('[role="dialog"]')).not.toBeNull();
    const cancel = [...element.querySelectorAll("button")].find(button => button.textContent === "Cancel")!;
    expect(cancel.disabled).toBe(true);
    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(element.querySelector('[role="dialog"]')).not.toBeNull();
    window.history.replaceState({}, "", "/workflows/definitions?backend=other");
    const otherNotify = vi.fn<StudioNotificationApi["notify"]>(() => ({ id: "other", update() {}, dismiss() {} }));
    setNotifications({ notify: otherNotify, track: work => work });
    resolve(saved);
    await vi.waitFor(() => expect(element.querySelector('[role="dialog"]')).toBeNull());
    expect(document.activeElement).toBe(opener);
    expect(notify).toHaveBeenCalledTimes(1); expect(otherNotify).not.toHaveBeenCalled();
    expect(notify).toHaveBeenCalledWith({ level: "success", title: "Template saved", action: { label: "View templates", run: expect.any(Function) } });
    const tabRequests: StudioWorkspaceTabRequest[] = [];
    const openTab = (event: Event) => { tabRequests.push((event as CustomEvent<StudioWorkspaceTabRequest>).detail); event.preventDefault(); };
    const browserOpen = vi.spyOn(window, "open").mockReturnValue(null);
    window.addEventListener(studioWorkspaceTabRequestedEvent, openTab);
    try {
      notify.mock.calls[0][0].action!.run();
      expect(tabRequests).toEqual([{ path: "/workflows/templates?backend=secondary", activate: true }]);
      expect(browserOpen).not.toHaveBeenCalled();
    } finally { window.removeEventListener(studioWorkspaceTabRequestedEvent, openTab); }
    // Older hosts without workspace tabs preserve the editor in its browser tab instead.
    notify.mock.calls[0][0].action!.run();
    expect(browserOpen).toHaveBeenCalledWith("/workflows/templates?backend=secondary", "_blank", "noopener");
    browserOpen.mockRestore();
    expect(element.textContent).not.toContain("Done");
  });

  it("retains failed inputs and inline retry without a success toast", async () => {
    vi.mocked(saveWorkflowTemplate).mockRejectedValueOnce(new Error("Save unavailable")).mockResolvedValueOnce(saved);
    mount(); field("Keep my work"); submit();
    await vi.waitFor(() => expect(element.querySelector('[role="alert"]')?.textContent).toBe("Save unavailable"));
    expect(element.querySelector("input")?.value).toBe("Keep my work"); expect(notify).not.toHaveBeenCalled();
    submit(); await vi.waitFor(() => expect(element.querySelector('[role="dialog"]')).toBeNull());
    expect(saveWorkflowTemplate).toHaveBeenCalledTimes(2); expect(notify).toHaveBeenCalledTimes(1);
  });

  it("cancels with focus restored and no save or confirmation", () => {
    const opener = mount();
    flushSync(() => [...element.querySelectorAll("button")].find(button => button.textContent === "Cancel")!.click());
    expect(element.querySelector('[role="dialog"]')).toBeNull(); expect(document.activeElement).toBe(opener);
    expect(saveWorkflowTemplate).not.toHaveBeenCalled(); expect(notify).not.toHaveBeenCalled();
  });
});
