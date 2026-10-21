import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { TestRunStatus } from "../workflow-editor/editorPanels";
import { WorkflowEditorToolbar } from "../workflow-editor/WorkflowEditorToolbar";
import type { WorkflowTestRunView } from "../workflowTypes";

const mounted: { root: ReturnType<typeof createRoot>; container: HTMLDivElement }[] = [];
const testRunStatusCases: { name: string; view: Partial<WorkflowTestRunView>; label: string }[] = [
  { name: "accepted", view: { status: "DispatchAccepted" }, label: "Test Run dispatched" },
  { name: "rejected", view: { status: "Rejected", reason: "The workflow rejected the test." }, label: "Test Run rejected" },
  { name: "accepted with incidents", view: { status: "DispatchAccepted", incidentCount: 2 }, label: "Test Run accepted with incidents" },
  { name: "accepted with a runtime issue", view: { status: "DispatchAccepted", commandDispatchStatus: "AcceptedButFaulted" }, label: "Test Run accepted with a runtime issue" }
];

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

  it.each(testRunStatusCases)("keeps the full $name Test Run result and direct details access", ({ view, label }) => {
    const onOpenDetails = vi.fn();
    const testRun: WorkflowTestRunView = {
      testRunId: "test-run-1",
      definitionId: "definition-1",
      definitionVersionId: "draft:3",
      status: "DispatchAccepted",
      ...view
    };
    const { container } = render({ runStatus: <TestRunStatus testRun={testRun} stale onOpenDetails={onOpenDetails} /> });
    const result = container.querySelector<HTMLButtonElement>(".wf-test-run-trigger")!;
    const accessibleName = result.getAttribute("aria-label") ?? result.textContent?.trim();

    click(result);

    expect(onOpenDetails).toHaveBeenCalledOnce();
    expect(accessibleName).toBe(`Earlier draft · ${label}`);
  });

  it("keeps save feedback with the actions and preserves busy and run eligibility guards", () => {
    const { container, commands } = render({ busy: true, saving: true, canRun: false, status: "Saving before review..." });
    const status = container.querySelector<HTMLElement>("[role='status']")!;
    expect(status.closest(".wf-editor-actions")).not.toBeNull();
    expect(status.getAttribute("data-pending")).toBe("true");
    expect(status.textContent).toBe("Saving before review...");
    const buttons = [".wf-save-button", ".wf-publish-primary", ".wf-run-button"].map(selector => container.querySelector<HTMLButtonElement>(selector)!);
    expect(buttons.every(button => button.disabled)).toBe(true);
    buttons.forEach(click);
    Object.values(commands).forEach(command => expect(command).not.toHaveBeenCalled());
  });
});
