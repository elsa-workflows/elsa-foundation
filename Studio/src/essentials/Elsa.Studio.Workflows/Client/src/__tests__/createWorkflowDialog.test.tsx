import React from "react";
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
    ai={testApi().agent}
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
  it("contains forward and backward keyboard focus, closes on Escape and restores the opener", () => {
    const close = renderDialog();
    const name = container.querySelector<HTMLInputElement>('[aria-label="Display name"]')!;
    const submit = container.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(document.activeElement).toBe(name);
    tabFrom(submit);
    expect(document.activeElement).toBe(name);
    tabFrom(name, true);
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
