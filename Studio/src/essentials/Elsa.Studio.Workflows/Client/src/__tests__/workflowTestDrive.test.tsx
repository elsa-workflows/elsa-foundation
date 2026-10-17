import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createContributionRegistry, type StudioEndpointContext, type StudioTestStimulusEditorContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft, WorkflowExecutionInputs, WorkflowTestRunView } from "../workflowTypes";
import type { WorkflowTestDriveSetupState } from "../workflow-editor/editorTypes";

const publishingMocks = vi.hoisted(() => ({ getOptions: vi.fn(), getRun: vi.fn() }));
const stimulusEditorChunk = vi.hoisted(() => {
  let resolveModule!: (module: Record<string, unknown>) => void;
  const promise = new Promise<Record<string, unknown>>(resolve => { resolveModule = resolve; });
  return { promise, resolveModule: (module: Record<string, unknown>) => resolveModule(module) };
});
vi.mock("../api/publishing", () => ({
  getWorkflowDraftTestRunOptions: publishingMocks.getOptions,
  getWorkflowTestRun: publishingMocks.getRun
}));
vi.mock("../workflowTestStimulusEditorComponents", () => stimulusEditorChunk.promise);

import { WorkflowTestDrivePanel } from "../workflow-editor/WorkflowTestDrivePanel";
import { httpWorkflowTestStimulusEditor } from "../workflowTestStimulusEditors";
import { WorkflowRuntimePanel } from "../workflow-editor/editorPanels";

let active: { root: Root; container: HTMLElement } | null = null;

beforeEach(() => {
  publishingMocks.getOptions.mockReset();
  publishingMocks.getRun.mockReset();
  publishingMocks.getOptions.mockResolvedValue({ options: [customOption] });
});

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
});

describe("workflow Test Run setup", () => {
  it("withholds submission until a lazy built-in stimulus editor loads", async () => {
    const httpOption = {
      optionId: "http-option",
      driverId: "Elsa.HttpEndpoint",
      label: "HTTP endpoint",
      modes: [{ modeId: "http-request", label: "Send HTTP request", isDefault: true }]
    };
    publishingMocks.getOptions.mockResolvedValue({ options: [httpOption] });
    const onStart = vi.fn(async () => testRunView);
    const container = renderPanel({ stimulusEditors: [httpWorkflowTestStimulusEditor], onStart });
    await settle();

    changeSelect(container.querySelector<HTMLSelectElement>("select")!, "trigger:http-option");
    expect(container.textContent).toContain("Loading Test Drive editor…");
    expect(container.querySelector<HTMLFormElement>("form")?.style.display).toBe("none");
    expect(onStart).not.toHaveBeenCalled();

    stimulusEditorChunk.resolveModule({
      HttpRequestEditorView: ({ draft, controlProps }: { draft: string; controlProps: React.TextareaHTMLAttributes<HTMLTextAreaElement> }) =>
        <textarea {...controlProps} value={draft} />
    });
    await vi.waitFor(() => {
      expect(container.querySelector("textarea[aria-label='Send HTTP request data']")).toBeTruthy();
      expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(false);
    });
    expect(container.textContent).not.toContain("Loading Test Drive editor…");
    expect(onStart).not.toHaveBeenCalled();
  });

  it("uses a public custom editor and keeps its data separate from workflow inputs", async () => {
    const customRegistry = createContributionRegistry<StudioTestStimulusEditorContribution>();
    customRegistry.add(customEditor);
    const onStart = vi.fn(async () => testRunView);
    const container = renderPanel({ stimulusEditors: customRegistry.list(), onStart });
    await settle();
    const startSelector = container.querySelector<HTMLSelectElement>("select")!;
    expect(startSelector.value).toBe("direct");
    changeSelect(startSelector, "trigger:custom-option");

    const customControl = container.querySelector<HTMLInputElement>("input[aria-label='Custom stimulus']");
    expect(customControl).toBeTruthy();
    changeValue(customControl!, '{"orderId":"stimulus-order"}');
    const workflowInput = container.querySelector<HTMLInputElement>(".wf-run-input-list input");
    expect(workflowInput).toBeTruthy();
    changeValue(workflowInput!, "workflow-order");
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();

    expect(onStart).toHaveBeenCalledTimes(1);
    const [snapshot, inputs, start] = onStart.mock.calls[0] as unknown as [WorkflowDraft, Record<string, unknown>, { kind: string; data?: unknown }];
    expect(snapshot.state.rootActivity).toBeTruthy();
    expect(inputs).toEqual({ orderId: "workflow-order" });
    expect(start).toEqual({ kind: "trigger", optionId: "custom-option", modeId: "custom-mode", data: { orderId: "stimulus-order" } });
    expect(publishingMocks.getOptions).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ definitionId: "definition-1", snapshotId: expect.any(String) }));
  });

  it("keeps a captured draft read-only after edits and requires refreshing before a rerun", async () => {
    const onStart = vi.fn(async () => testRunView);
    const container = renderPanel({ onStart });
    await settle();
    const changed = draft();
    changed.state.inputs = [{ referenceKey: "input-order", name: "orderId", displayName: "Edited Order ID", type: { alias: "String", collectionKind: "Single" }, isRequired: true }];
    flushSync(() => active!.root.render(<WorkflowTestDrivePanel {...panelProps({ draft: changed, onStart })} />));

    expect(container.textContent).toContain("Draft changed");
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(true);
    expect(onStart).not.toHaveBeenCalled();
    const refresh = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Refresh setup")!;
    flushSync(() => refresh.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await settle();
    expect(publishingMocks.getOptions.mock.calls.length).toBeGreaterThan(1);
    expect(container.textContent).not.toContain("Draft changed");
  });

  it("retains the selected start and setup values when the Test Run panel remounts", async () => {
    let savedState: WorkflowTestDriveSetupState | null = null;
    const onSetupStateChange = (state: WorkflowTestDriveSetupState) => { savedState = state; };
    let container = renderPanel({ setupState: savedState, onSetupStateChange });
    await settle();

    const startSelector = container.querySelector<HTMLSelectElement>("select")!;
    changeSelect(startSelector, "trigger:custom-option");
    changeValue(container.querySelector<HTMLInputElement>("input[aria-label='Custom stimulus']")!, '{"orderId":"stimulus-order"}');
    changeValue(container.querySelector<HTMLInputElement>(".wf-run-input-list input")!, "workflow-order");
    await settle();

    expect(savedState).toMatchObject({
      selectedOptionId: "custom-option",
      selectedModeId: "custom-mode",
      stimulusDraft: '{"orderId":"stimulus-order"}',
      inputDrafts: { "input-order": "workflow-order" }
    });

    unmountCurrent();
    container = renderPanel({ setupState: savedState, onSetupStateChange });
    await settle();

    expect(container.querySelector<HTMLSelectElement>("select")?.value).toBe("trigger:custom-option");
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Custom stimulus']")?.value).toBe('{"orderId":"stimulus-order"}');
    expect(container.querySelector<HTMLInputElement>(".wf-run-input-list input")?.value).toBe("workflow-order");
  });

  it("retains a safe custom action failure and prepared status for the Runtime result", async () => {
    const preparedView: WorkflowTestRunView = {
      ...testRunView,
      status: "Prepared",
      driverAction: { kind: "custom-action", relativeUrl: "/api/test-run/action", method: "POST" }
    };
    publishingMocks.getRun.mockResolvedValue(preparedView);
    const onStart: React.ComponentProps<typeof WorkflowTestDrivePanel>["onStart"] = async (_snapshot, _inputs, _start, _label, _inputNames, onStarted) => {
      await onStarted?.(preparedView);
      return preparedView;
    };
    const failingEditor: StudioTestStimulusEditorContribution = {
      ...customEditor,
      id: "contoso.test-drive.failing-action",
      executeAction: async () => { throw new Error("Authorization: Bearer secret-token"); }
    };
    const onUpdateRun = vi.fn();
    const container = renderPanel({ stimulusEditors: [failingEditor], onStart, onUpdateRun });
    await settle();
    changeSelect(container.querySelector<HTMLSelectElement>("select")!, "trigger:custom-option");
    changeValue(container.querySelector<HTMLInputElement>("input[aria-label='Custom stimulus']")!, "{}");
    changeValue(container.querySelector<HTMLInputElement>(".wf-run-input-list input")!, "workflow-order");
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();

    const update = onUpdateRun.mock.calls[0] as unknown as [string, WorkflowTestRunView, string, string[], unknown, string];
    expect(update[1]).toEqual(preparedView);
    expect(update[5]).toBe("The trigger action could not be sent. The Test Run preparation remains available.");
    expect(JSON.stringify(update)).not.toContain("secret-token");

    flushSync(() => active!.root.render(<WorkflowRuntimePanel testRun={{
      draftSignature: update[0],
      view: update[1],
      startLabel: update[2],
      workflowInputNames: update[3],
      driverActionError: update[5]
    }} onOpenRun={vi.fn()} />));
    expect(container.querySelector(".wf-runtime-card h3")?.textContent).toBe("Trigger prepared");
    expect(container.textContent).toContain("The trigger action could not be sent.");
  });

  it("does not select a trigger silently and disables an unknown driver without a declared schema", async () => {
    publishingMocks.getOptions.mockResolvedValue({
      options: [{ ...customOption, modes: [{ modeId: "opaque-mode", label: "Opaque", description: "No schema or editor." }] }],
      diagnostics: [{ code: "driver.unavailable", message: "No trigger module is installed.", nodeId: "authored-node" }]
    });
    const container = renderPanel({ stimulusEditors: [] });
    await settle();

    const startSelector = container.querySelector<HTMLSelectElement>("select")!;
    expect(startSelector.value).toBe("direct");
    expect(container.textContent).toContain("driver.unavailable");
    expect(container.textContent).toContain("authored-node");
    changeSelect(startSelector, "trigger:custom-option");
    expect(container.textContent).toContain("does not declare a JSON Schema");
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(true);
  });

  it("normalizes camelCase collection kinds in the captured input contract before editing and parsing", async () => {
    publishingMocks.getOptions.mockResolvedValue({
      options: [],
      inputContract: { version: 1, inputs: [{ name: "customerId", type: { alias: "String", collectionKind: "single" }, isRequired: true }] }
    });
    const capturedInputs = vi.fn();
    const onStart = vi.fn(async (_snapshot: WorkflowDraft, inputs: WorkflowExecutionInputs) => {
      capturedInputs(inputs);
      return testRunView;
    });
    const container = renderPanel({ stimulusEditors: [], onStart });
    await settle();

    expect(container.textContent).toContain("customerId");
    expect(container.textContent).toContain("String");
    expect(container.querySelectorAll(".wf-run-input-list input")).toHaveLength(1);
    const customerId = container.querySelector<HTMLInputElement>(".wf-run-input-list input")!;
    changeValue(customerId, "cust-1042");
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();

    expect(onStart).toHaveBeenCalledTimes(1);
    expect(capturedInputs).toHaveBeenCalledWith({ customerId: "cust-1042" });
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("sends only a same-origin root-relative HTTP action and records the real response", async () => {
    const requestResponse = vi.fn(async () => new Response("created body", {
      status: 202,
      statusText: "Accepted",
      headers: { "x-request-id": "response-123" }
    }));
    const backend = { baseUrl: "https://studio.example/elsa/api", http: { requestResponse } } as unknown as StudioEndpointContext;
    const execute = httpWorkflowTestStimulusEditor.executeAction!;
    const result = await execute({
      backend,
      option: customOption,
      mode: customOption.modes[0],
      action: { kind: "http-request", relativeUrl: "/elsa/workflows/test-runs/run-1/http/orders/42", method: "POST" },
      data: { query: { tag: ["one", "two"] }, headers: { "x-client": ["studio"] }, body: "private request body" },
      editorDraft: "{}"
    });

    expect(requestResponse).toHaveBeenCalledTimes(1);
    const [url, init] = requestResponse.mock.calls[0] as unknown as [string, RequestInit & { retryOnUnauthorized?: boolean }];
    expect(url).toBe("https://studio.example/elsa/workflows/test-runs/run-1/http/orders/42?tag=one&tag=two");
    expect(init.method).toBe("POST");
    expect(init.body).toBe("private request body");
    expect(init.retryOnUnauthorized).toBe(false);
    expect(new Headers(init.headers).get("x-client")).toBe("studio");
    expect(result).toMatchObject({ kind: "http-exchange", status: 202, statusText: "Accepted", body: "created body" });
    expect(JSON.stringify(result)).not.toContain("private request body");

    await expect(execute({
      backend,
      option: customOption,
      mode: customOption.modes[0],
      action: { kind: "http-request", relativeUrl: "https://outside.example/steal", method: "POST" },
      data: {},
      editorDraft: "{}"
    })).rejects.toThrow("safe server-relative URL");
    expect(requestResponse).toHaveBeenCalledTimes(1);

    requestResponse.mockRejectedValueOnce(new Error("Authorization: Bearer secret-token"));
    const failedExchange = await execute({
      backend,
      option: customOption,
      mode: customOption.modes[0],
      action: { kind: "http-request", relativeUrl: "/elsa/workflows/test-runs/run-1/http/orders/42", method: "POST" },
      data: { headers: { "x-client-secret": ["secret-value"] } },
      editorDraft: "{}"
    });
    expect(failedExchange.error).toBe("The HTTP request failed.");
    expect(JSON.stringify(failedExchange)).not.toContain("secret");
  });
});

const customOption = {
  optionId: "custom-option",
  driverId: "Contoso.CustomStart",
  nodeId: "authored-start-node",
  label: "Custom start",
  modes: [{ modeId: "custom-mode", label: "Deliver custom data", description: "A module-owned mode.", isDefault: true, configurationSchema: { type: "object" } }]
};

const customEditor: StudioTestStimulusEditorContribution = {
  id: "contoso.test-drive.custom-start",
  supports: ({ option, mode }) => option.driverId === "Contoso.CustomStart" && mode.modeId === "custom-mode",
  component: ({ draft, controlProps, onChange, disabled }) => <input {...controlProps} aria-label="Custom stimulus" value={draft} disabled={disabled} onChange={event => onChange(event.currentTarget.value)} />,
  validate: ({ draft }) => { try { JSON.parse(draft); return undefined; } catch { return "Invalid JSON"; } },
  serialize: ({ draft }) => JSON.parse(draft)
};

const testRunView: WorkflowTestRunView = {
  testRunId: "test-run-1", definitionId: "definition-1", definitionVersionId: "draft:1", status: "DispatchAccepted"
};

function panelProps(overrides: Partial<React.ComponentProps<typeof WorkflowTestDrivePanel>> = {}): React.ComponentProps<typeof WorkflowTestDrivePanel> {
  return {
    context: { baseUrl: "https://studio.example/elsa/api", http: {} } as StudioEndpointContext,
    draft: draft(),
    runInputEditors: [],
    stimulusEditors: [customEditor],
    setupState: null,
    onSetupStateChange: () => undefined,
    busy: false,
    onStart: vi.fn(async () => testRunView),
    onUpdateRun: vi.fn(),
    ...overrides
  };
}

function unmountCurrent() {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
}

function renderPanel(overrides: Partial<React.ComponentProps<typeof WorkflowTestDrivePanel>> = {}) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  active = { root, container };
  flushSync(() => root.render(<WorkflowTestDrivePanel {...panelProps(overrides)} />));
  return container;
}

function draft(overrides: Partial<WorkflowDraft> = {}): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    state: {
      rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] },
      inputs: [{ referenceKey: "input-order", name: "orderId", displayName: "Order ID", type: { alias: "String", collectionKind: "Single" }, isRequired: true }]
    },
    layout: [],
    validationErrors: [],
    ...overrides
  };
}

function changeValue(element: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(element, value);
  flushSync(() => element.dispatchEvent(new Event("change", { bubbles: true })));
}

function changeSelect(element: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set;
  setter?.call(element, value);
  flushSync(() => element.dispatchEvent(new Event("change", { bubbles: true })));
}

async function settle() {
  await Promise.resolve();
  await Promise.resolve();
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => undefined);
}
