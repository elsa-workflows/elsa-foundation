import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createContributionRegistry, type StudioEndpointContext, type StudioTestStimulusEditorContribution } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, capabilityIds } from "../api/capabilities";
import type { WorkflowTestRunState, WorkflowTestRunWaitDraftState } from "../workflow-editor/editorTypes";

const publishingMocks = vi.hoisted(() => ({
  deliver: vi.fn(),
  getRun: vi.fn(),
  getInstance: vi.fn(),
  getWaits: vi.fn(),
  stop: vi.fn()
}));
vi.mock("../api/publishing", () => ({
  deliverWorkflowTestRunWaitEvent: publishingMocks.deliver,
  getWorkflowTestRun: publishingMocks.getRun,
  getWorkflowTestRunWaits: publishingMocks.getWaits,
  stopWorkflowTestRun: publishingMocks.stop
}));
vi.mock("../api/runtime", () => ({ getWorkflowInstance: publishingMocks.getInstance }));

import { WorkflowTestRunControls } from "../workflow-editor/WorkflowTestRunControls";

let active: { root: Root; container: HTMLElement } | null = null;

beforeEach(() => {
  publishingMocks.deliver.mockReset();
  publishingMocks.getRun.mockReset();
  publishingMocks.getInstance.mockReset();
  publishingMocks.getWaits.mockReset();
  publishingMocks.stop.mockReset();
  publishingMocks.getWaits.mockResolvedValue({ waits: [customWait] });
  publishingMocks.getRun.mockResolvedValue({ ...testRun.view, status: "DispatchAccepted", workflowExecutionId: "execution-2" });
  publishingMocks.getInstance.mockResolvedValue({ instance: { workflowExecutionId: "execution-1", status: "Running" } });
  publishingMocks.stop.mockResolvedValue(testRun.view);
});

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
  vi.useRealTimers();
});

describe("Workflow Test Run controls", () => {
  it("reuses a custom stimulus editor for supported waits and retries uncertain delivery with the same UUID", async () => {
    publishingMocks.deliver
      .mockRejectedValueOnce(new Error("connection dropped"))
      .mockResolvedValueOnce({ status: "Delivered" });
    publishingMocks.getInstance.mockResolvedValue({ instance: { workflowExecutionId: "execution-1", status: "Suspended" } });
    publishingMocks.getWaits.mockResolvedValueOnce({ waits: [customWait] }).mockResolvedValueOnce({ waits: [customWait] }).mockResolvedValueOnce({ waits: [] });
    const onRunViewUpdated = vi.fn();
    let savedWaitState: WorkflowTestRunWaitDraftState | undefined;
    const onWaitDraftUpdated = vi.fn((_waitId: string, state: WorkflowTestRunWaitDraftState) => { savedWaitState = state; });
    const editors = createContributionRegistry<StudioTestStimulusEditorContribution>();
    editors.add(customWaitEditor);
    const container = render({ stimulusEditors: editors.list(), onRunViewUpdated, onWaitDraftUpdated });
    await settle();
    expect(container.textContent).toContain("Waiting");

    const input = container.querySelector<HTMLInputElement>("input[aria-label='Custom wait payload']")!;
    changeValue(input, '{"orderId":"wait-123"}');
    const waitForm = container.querySelector(".wf-test-run-wait form")!;
    flushSync(() => waitForm.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();
    expect(container.textContent).toContain("outcome is uncertain");

    flushSync(() => active!.root.unmount());
    container.remove();
    active = null;
    const restored = render({
      stimulusEditors: editors.list(),
      onRunViewUpdated,
      onWaitDraftUpdated,
      testRun: { ...testRun, waitDrafts: { "wait-1": savedWaitState! } }
    });
    await settle();
    expect(restored.querySelector<HTMLInputElement>("input[aria-label='Custom wait payload']")?.value).toBe('{"orderId":"wait-123"}');
    const restoredForm = restored.querySelector(".wf-test-run-wait form")!;
    flushSync(() => restoredForm.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();
    expect(publishingMocks.deliver).toHaveBeenCalledTimes(2);
    const firstRequest = publishingMocks.deliver.mock.calls[0]?.[2];
    const secondRequest = publishingMocks.deliver.mock.calls[1]?.[2];
    expect(firstRequest).toMatchObject({ waitId: "wait-1", modeId: "custom-wake", data: { orderId: "wait-123" } });
    expect(firstRequest.deliveryId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
    expect(secondRequest.deliveryId).toBe(firstRequest.deliveryId);
    expect(onRunViewUpdated).toHaveBeenCalledWith(expect.objectContaining({ workflowExecutionId: "execution-2" }));
    expect(restored.textContent).toContain("Event delivered");
  });

  it("stops a Test Run and refreshes its view", async () => {
    const stoppedView = { ...testRun.view, status: "Stopped" };
    publishingMocks.stop.mockResolvedValue(testRun.view);
    publishingMocks.getRun.mockResolvedValue(stoppedView);
    const onRunViewUpdated = vi.fn();
    const container = render({ stimulusEditors: [], onRunViewUpdated });
    await settle();

    const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === "Stop Test Run")!;
    flushSync(() => button.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await settle();
    expect(publishingMocks.stop).toHaveBeenCalledWith(expect.anything(), "test-run-1");
    expect(publishingMocks.getRun).toHaveBeenCalledWith(expect.anything(), "test-run-1");
    expect(onRunViewUpdated).toHaveBeenCalledWith(stoppedView);
  });

  it("shows Waiting for an active wait and refreshes the wait list while the root is Running", async () => {
    publishingMocks.getInstance.mockResolvedValue({ instance: { workflowExecutionId: "execution-1", status: "Running" } });
    const container = render({ stimulusEditors: [] });
    await settle();

    expect(container.querySelector(".wf-test-run-execution-state")?.textContent).toContain("Waiting");
    const initialWaitReads = publishingMocks.getWaits.mock.calls.length;
    expect(initialWaitReads).toBeGreaterThanOrEqual(2);

    await vi.waitFor(() => expect(publishingMocks.getWaits.mock.calls.length).toBeGreaterThan(initialWaitReads), { timeout: 5000, interval: 100 });
  });

  it("stops polling when the host does not expose workflow instance status", async () => {
    vi.useFakeTimers();
    publishingMocks.getInstance.mockRejectedValue(new ApiCapabilityUnavailableError(capabilityIds.runtime, "workflow-instance"));
    render({ stimulusEditors: [] });
    for (let i = 0; i < 10; i++) {
      await Promise.resolve();
      flushSync(() => undefined);
    }

    expect(publishingMocks.getInstance).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(8000);
    expect(publishingMocks.getInstance).toHaveBeenCalledTimes(1);
  });
});

const customWait = {
  waitId: "wait-1",
  nodeId: "authored-event-node",
  label: "Custom event wait",
  driverId: "Contoso.CustomWake",
  isAvailable: true,
  modes: [{ modeId: "custom-wake", label: "Wake custom", description: "A module-owned wait mode.", isDefault: true, configurationSchema: { type: "object" } }]
};

const customWaitEditor: StudioTestStimulusEditorContribution = {
  id: "contoso.test-drive.custom-wait",
  supports: ({ option, mode }) => option.driverId === "Contoso.CustomWake" && mode.modeId === "custom-wake",
  component: ({ draft, controlProps, onChange, disabled }) => <input {...controlProps} aria-label="Custom wait payload" value={draft} disabled={disabled} onChange={event => onChange(event.currentTarget.value)} />,
  validate: ({ draft }) => { try { JSON.parse(draft); return undefined; } catch { return "Invalid JSON"; } },
  serialize: ({ draft }) => JSON.parse(draft)
};

const testRun: WorkflowTestRunState = {
  draftSignature: "draft-signature",
  view: { testRunId: "test-run-1", definitionId: "definition-1", definitionVersionId: "draft:1", status: "DispatchAccepted", workflowExecutionId: "execution-1" },
  startLabel: "Custom event wait",
  workflowInputNames: ["existing-input"],
  driverActionResult: { contributionId: "test-editor", value: { kind: "http-exchange", body: "retained" } }
};

function render(overrides: Partial<React.ComponentProps<typeof WorkflowTestRunControls>> = {}) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  active = { root, container };
  flushSync(() => root.render(<WorkflowTestRunControls
    context={{ baseUrl: "https://studio.example/elsa/api", http: {} } as StudioEndpointContext}
    testRun={testRun}
    stimulusEditors={[]}
    onRunViewUpdated={vi.fn()}
    {...overrides}
  />));
  return container;
}

function changeValue(element: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(element, value);
  flushSync(() => element.dispatchEvent(new Event("change", { bubbles: true })));
}

async function settle() {
  await Promise.resolve();
  await Promise.resolve();
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => undefined);
}
