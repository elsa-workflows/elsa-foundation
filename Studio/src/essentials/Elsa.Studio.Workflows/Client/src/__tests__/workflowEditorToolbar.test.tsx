import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WorkflowEditorToolbar } from "../workflow-editor/WorkflowEditorToolbar";

const mounted: { root: ReturnType<typeof createRoot>; container: HTMLDivElement }[] = [];
afterEach(() => {
  mounted.splice(0).forEach(({ root, container }) => {
    flushSync(() => root.unmount());
    container.remove();
  });
});

function render(overrides: Partial<React.ComponentProps<typeof WorkflowEditorToolbar>> = {}) {
  const commands = { onSave: vi.fn(), onPublish: vi.fn(), onRun: vi.fn() };
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  mounted.push({ root, container });
  const props = {
    name: "Customer quote", status: "Autosaved", saving: false, busy: false, autosaveEnabled: true,
    onAutosaveChange: vi.fn(), canUndo: true, canRedo: false, canAutoLayout: true,
    onUndo: vi.fn(), onRedo: vi.fn(), onAutoLayout: vi.fn(), onBack: vi.fn(),
    ...commands, canRun: true, runTitle: "Run a transient test", moreActions: [], ...overrides
  };
  flushSync(() => root.render(<WorkflowEditorToolbar {...props} />));
  return { container, commands };
}

function click(button: HTMLButtonElement) {
  flushSync(() => button.click());
}

describe("compact workflow toolbar", () => {
  it("keeps primary commands directly available with descriptive accessible labels", () => {
    const { container, commands } = render();
    const save = container.querySelector<HTMLButtonElement>(".wf-save-button")!;
    const publish = container.querySelector<HTMLButtonElement>(".wf-publish-primary")!;
    const run = container.querySelector<HTMLButtonElement>(".wf-run-button")!;
    const publishAccessibleName = publish.getAttribute("aria-label") ?? publish.textContent?.trim();
    const runAccessibleName = run.getAttribute("aria-label") ?? run.textContent?.trim();
    expect(save.title).toBe("Save workflow");
    expect(save.textContent).toBe("Save");
    expect(save.querySelector(".wf-toolbar-sr-only")).not.toBeNull();
    expect(container.querySelector("[aria-label='Publish options']")).not.toBeNull();
    click(save); click(publish); click(run);
    expect(commands.onSave).toHaveBeenCalledOnce();
    expect(commands.onPublish).toHaveBeenCalledOnce();
    expect(commands.onPublish).toHaveBeenCalledWith("review");
    expect(commands.onRun).toHaveBeenCalledOnce();
    expect(publishAccessibleName).toBe("Review & publish");
    expect(runAccessibleName).toBe("Test Run");
  });

  it("keeps save feedback with the actions and preserves busy and run eligibility guards", () => {
    const { container, commands } = render({ busy: true, saving: true, canRun: false, status: "Saving before review..." });
    const status = container.querySelector<HTMLElement>("[role='status']")!;
    expect(status.closest(".wf-editor-toolbar")).not.toBeNull();
    expect(status.getAttribute("data-pending")).toBe("true");
    expect(status.textContent).toBe("Saving before review...");
    const buttons = [".wf-save-button", ".wf-publish-primary", ".wf-run-button"].map(selector => container.querySelector<HTMLButtonElement>(selector)!);
    expect(buttons.every(button => button.disabled)).toBe(true);
    buttons.forEach(click);
    Object.values(commands).forEach(command => expect(command).not.toHaveBeenCalled());
  });

  it("reports unsaved changes without a pending spinner or saved check", () => {
    const { container } = render({ status: "Unsaved changes" });
    const status = container.querySelector<HTMLElement>("[role='status']")!;
    expect(status.getAttribute("data-unsaved")).toBe("true");
    expect(status.getAttribute("data-pending")).toBeNull();
    expect(status.querySelector("svg")).toBeNull();
    expect(status.textContent).toBe("Unsaved changes");
  });

  it("explains unsupported publication and test-run operations",() => {
    const {container,commands} = render({publicationUnavailableReason:"Publishing is not supported by this backend.",canRun:false,runTitle:"Test runs are not supported by this backend."});
    const publish=container.querySelector<HTMLButtonElement>(".wf-publish-primary")!;
    const run=container.querySelector<HTMLButtonElement>(".wf-run-button")!;
    expect(publish.disabled).toBe(true);expect(publish.title).toContain("not supported");
    expect(run.disabled).toBe(true);expect(run.title).toContain("not supported");
    click(publish);click(run);expect(commands.onPublish).not.toHaveBeenCalled();expect(commands.onRun).not.toHaveBeenCalled();
  });

});
