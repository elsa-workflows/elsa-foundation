import React, { useLayoutEffect } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createContributionRegistry, type StudioEndpointContext, type StudioTestStimulusEditorContribution } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError, capabilityIds } from "../api/capabilities";
import * as capabilities from "../api/capabilities";
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
import { defaultHttpTestRequest } from "../workflowTestRequestDefaults";
import { WorkflowRuntimePanel } from "../workflow-editor/editorPanels";

let active: { root: Root; container: HTMLElement } | null = null;

beforeEach(() => {
  publishingMocks.getOptions.mockReset();
  publishingMocks.getRun.mockReset();
  publishingMocks.getOptions.mockResolvedValue({ options: [customOption] });
});

afterEach(() => {
  vi.restoreAllMocks();
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
});

describe("workflow Test Run setup", () => {
  it("uses Start Test Run as the generic submission label and accessible name", async () => {
    const container = renderPanel();
    await settle();
    const submit = container.querySelector<HTMLButtonElement>("button[type='submit']")!;

    expect(submit.getAttribute("aria-label") ?? submit.textContent?.trim()).toBe("Start Test Run");
  });

  it("withholds start on the first commit before setup loading effects run", async () => {
    const onStart = vi.fn(async () => testRunView);
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    active = { root, container };
    let initiallyDisabled = false;
    function FirstCommitProbe() {
      useLayoutEffect(() => {
        initiallyDisabled = container.querySelector<HTMLButtonElement>("button[type='submit']")!.disabled;
        container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
      }, []);
      return <WorkflowTestDrivePanel {...panelProps({ onStart, draft: draft({ state: { rootActivity: draft().state.rootActivity } }) })} />;
    }
    flushSync(() => root.render(<FirstCommitProbe />));
    await settle();
    expect(initiallyDisabled).toBe(true);
    expect(onStart).not.toHaveBeenCalled();
  });

  it.each([
    new ApiCapabilityUnavailableError(capabilityIds.publishing, "workflow-draft-test-run-options"),
    new ApiCapabilityUnavailableError(capabilityIds.publishing),
    new ApiCapabilityVersionMismatchError(capabilityIds.publishing, "1", "2")
  ])("explains unsupported setup, blocks starts, and retries successfully: %s", async error => {
    const invalidate = vi.spyOn(capabilities, "invalidateApiCapabilityCache");
    publishingMocks.getOptions.mockRejectedValueOnce(error);
    const onStart = vi.fn(async () => testRunView);
    const container = renderPanel({ onStart });
    await settle();

    expect(container.textContent).toContain("Test Run setup is unavailable on this backend. Use compatible Studio and backend versions, restart it, then select Refresh setup.");
    expect(container.textContent).not.toContain("API capability 'elsa.api.publishing'");
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(true);
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();
    expect(onStart).not.toHaveBeenCalled();

    let completeSetup!: () => void;
    publishingMocks.getOptions.mockImplementationOnce(() => new Promise(resolve => { completeSetup = () => resolve({ options: [customOption] }); }));
    const refresh = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Refresh setup")!;
    flushSync(() => refresh.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await vi.waitFor(() => expect(completeSetup).toBeTypeOf("function"));
    expect(invalidate).toHaveBeenCalledOnce();
    expect(publishingMocks.getOptions).toHaveBeenCalledTimes(2);
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(true);
    expect(onStart).not.toHaveBeenCalled();
    completeSetup();
    // Recovery is confirmed by rendered readiness, rather than assuming one event-loop turn settles React.
    await vi.waitFor(() => {
      expect(container.textContent).not.toContain("Test Run setup is unavailable");
      expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(false);
    });
    changeValue(container.querySelector<HTMLInputElement>(".wf-run-input-list input")!, "order-1");
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await vi.waitFor(() => expect(onStart).toHaveBeenCalledTimes(1));
  });

  it("preserves the actual start-options request error", async () => {
    publishingMocks.getOptions.mockRejectedValueOnce(new Error("HTTP 503 Service Unavailable"));
    const onStart = vi.fn(async () => testRunView);
    const container = renderPanel({ onStart });
    await settle();

    expect(container.querySelector('[role="alert"]')?.textContent).toBe("HTTP 503 Service Unavailable");
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(true);
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();
    expect(onStart).not.toHaveBeenCalled();
  });

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

    await vi.waitFor(() => expect(container.querySelector<HTMLFormElement>("form")?.style.display).toBe("none"));
    const loadingAnnouncement = container.querySelector<HTMLElement>(".wf-test-drive-loading")?.textContent;
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
    expect(loadingAnnouncement).toBe("Loading Test Run editor…");
  });

  it("starts a fresh HTTP Test Run with a request shape that passes validation", async () => {
    const httpOption = {
      optionId: "http-option",
      driverId: "Elsa.HttpEndpoint",
      nodeId: "http-node",
      label: "HTTP endpoint",
      modes: [{ modeId: "http-request", label: "Send HTTP request", isDefault: true }]
    };
    let resolveOptions!: (result: { options: Array<typeof httpOption> }) => void;
    publishingMocks.getOptions.mockReturnValue(new Promise<{ options: Array<typeof httpOption> }>(resolve => { resolveOptions = resolve; }));
    const onStart = vi.fn(async () => testRunView);
    const container = renderPanel({
      draft: draft({ state: { rootActivity: draft().state.rootActivity, inputs: [] } }),
      stimulusEditors: [{
        ...httpWorkflowTestStimulusEditor,
        component: ({ draft: value, controlProps, onChange, disabled }) =>
          <textarea {...controlProps} value={value} disabled={disabled} onChange={event => onChange(event.currentTarget.value)} />
      }],
      onStart
    });
    await settle();

    const startSelector = container.querySelector<HTMLSelectElement>("select")!;
    expect(startSelector.disabled).toBe(true);
    expect(startSelector.querySelector("option[value='trigger:http-option']")).toBeNull();

    resolveOptions({ options: [httpOption] });
    await vi.waitFor(() => {
      expect(startSelector.disabled).toBe(false);
      expect(startSelector.querySelector("option[value='trigger:http-option']")).toBeTruthy();
    });

    expect(startSelector.value).toBe("trigger:http-option");
    const textarea = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Send HTTP request data']");
    expect(textarea).toBeTruthy();
    expect(JSON.parse(textarea!.value)).toEqual(defaultHttpTestRequest);
    expect(httpWorkflowTestStimulusEditor.validate({
      option: httpOption,
      mode: httpOption.modes[0],
      draft: textarea!.value
    })).toBeUndefined();

    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();

    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(onStart).toHaveBeenCalledTimes(1);
    const [, , start] = onStart.mock.calls[0] as unknown as [WorkflowDraft, WorkflowExecutionInputs, { kind: string; data?: unknown }];
    expect(start).toEqual({
      kind: "trigger",
      optionId: "http-option",
      modeId: "http-request",
      data: defaultHttpTestRequest
    });
  });

  it("uses a public custom editor and keeps its data separate from workflow inputs", async () => {
    const customRegistry = createContributionRegistry<StudioTestStimulusEditorContribution>();
    customRegistry.add({ ...customEditor, startLabel: "Send custom event" });
    const onStart = vi.fn(async () => testRunView);
    const container = renderPanel({ stimulusEditors: customRegistry.list(), onStart });
    await settle();
    const startSelector = container.querySelector<HTMLSelectElement>("select")!;
    expect(startSelector.value).toBe("trigger:custom-option");
    expect(startSelector.labels).toHaveLength(1);
    expect(startSelector.labels?.[0].querySelector("span")?.textContent?.trim()).toBe("Start");

    const customControl = container.querySelector<HTMLInputElement>("input[aria-label='Custom stimulus']");
    expect(customControl).toBeTruthy();
    expect(customControl!.getAttribute("aria-label")).toBe("Custom stimulus");
    changeValue(customControl!, '{"orderId":"stimulus-order"}');
    const workflowInput = container.querySelector<HTMLInputElement>(".wf-run-input-list input");
    expect(workflowInput).toBeTruthy();
    expect(workflowInput!.getAttribute("aria-label")).toBe("Order ID");
    expect(workflowInput!.labels?.[0]?.textContent).toContain("Order ID");
    changeValue(workflowInput!, "workflow-order");
    expect(container.textContent).toContain("Test Run uses the connected environment. Workflow activities can still produce real side effects.");
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.textContent?.trim()).toBe("Send custom event");
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();

    expect(onStart).toHaveBeenCalledTimes(1);
    const [snapshot, inputs, start, label, inputNames] = onStart.mock.calls[0] as unknown as [WorkflowDraft, Record<string, unknown>, { kind: string; data?: unknown }, string, string[]];
    expect(snapshot.state.rootActivity).toBeTruthy();
    expect(inputs).toEqual({ orderId: "workflow-order" });
    expect(start).toEqual({ kind: "trigger", optionId: "custom-option", modeId: "custom-mode", data: { orderId: "stimulus-order" } });
    expect(label).toBe("Custom start · Deliver custom data");
    expect(inputNames).toEqual(["orderId"]);
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

  it("defaults to the first trigger and its declared default mode", async () => {
    const firstOption = {
      ...customOption,
      optionId: "first-option",
      modes: [
        { ...customOption.modes[0], modeId: "other-mode", isDefault: false },
        customOption.modes[0]
      ]
    };
    publishingMocks.getOptions.mockResolvedValue({ options: [firstOption, customOption] });
    const container = renderPanel();
    await settle();

    const selectors = container.querySelectorAll<HTMLSelectElement>("select");
    expect(selectors[0].value).toBe("trigger:first-option");
    expect(selectors[1].value).toBe("custom-mode");
    expect(container.querySelector("input[aria-label='Custom stimulus']")).toBeTruthy();
  });

  it("still defaults to the first trigger when reopened before options finish loading", async () => {
    let resolveOptions!: (result: { options: typeof customOption[] }) => void;
    publishingMocks.getOptions.mockReturnValue(new Promise(resolve => { resolveOptions = resolve; }));
    let savedState: WorkflowTestDriveSetupState | null = null;
    const onSetupStateChange = (state: WorkflowTestDriveSetupState) => { savedState = state; };
    renderPanel({ onSetupStateChange });
    await settle();

    unmountCurrent();
    const container = renderPanel({ setupState: savedState, onSetupStateChange });
    resolveOptions({ options: [customOption] });
    await settle();
    expect(container.querySelector<HTMLSelectElement>("select")?.value).toBe("trigger:custom-option");
  });

  it("falls back to Direct start when the workflow has no triggers", async () => {
    publishingMocks.getOptions.mockResolvedValue({ options: [] });
    const onStart = vi.fn(async () => testRunView);
    const container = renderPanel({ onStart });
    await settle();

    expect(container.querySelector<HTMLSelectElement>("select")?.value).toBe("direct");
    changeValue(container.querySelector<HTMLInputElement>(".wf-run-input-list input")!, "workflow-order");
    flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
    await settle();
    expect(onStart).toHaveBeenCalledWith(expect.anything(), { orderId: "workflow-order" }, { kind: "direct" }, "Direct start", ["orderId"], expect.any(Function));
  });

  it.each(["direct", "trigger:second-option"])("retains an explicit %s selection after refreshing and remounting", async selection => {
    publishingMocks.getOptions.mockResolvedValue({ options: [customOption, { ...customOption, optionId: "second-option" }] });
    let savedState: WorkflowTestDriveSetupState | null = null;
    const onSetupStateChange = (state: WorkflowTestDriveSetupState) => { savedState = state; };
    let container = renderPanel({ onSetupStateChange });
    await settle();
    changeSelect(container.querySelector<HTMLSelectElement>("select")!, selection);

    const refresh = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Refresh setup")!;
    flushSync(() => refresh.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await settle();
    expect(container.querySelector<HTMLSelectElement>("select")?.value).toBe(selection);

    unmountCurrent();
    container = renderPanel({ setupState: savedState, onSetupStateChange });
    await settle();
    expect(container.querySelector<HTMLSelectElement>("select")?.value).toBe(selection);
  });

  it("selects the first remaining trigger when the selected trigger is removed", async () => {
    const container = renderPanel();
    await settle();
    changeSelect(container.querySelector<HTMLSelectElement>("select")!, "trigger:custom-option");
    publishingMocks.getOptions.mockResolvedValue({ options: [{ ...customOption, optionId: "remaining-option" }] });

    const refresh = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Refresh setup")!;
    flushSync(() => refresh.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await settle();
    expect(container.querySelector<HTMLSelectElement>("select")?.value).toBe("trigger:remaining-option");
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

  it("disables the default trigger when its driver has no editor or declared schema", async () => {
    publishingMocks.getOptions.mockResolvedValue({
      options: [{ ...customOption, modes: [{ modeId: "opaque-mode", label: "Opaque", description: "No schema or editor." }] }],
      diagnostics: [{ code: "driver.unavailable", message: "No trigger module is installed.", nodeId: "authored-node" }]
    });
    const container = renderPanel({ stimulusEditors: [] });
    await settle();

    const startSelector = container.querySelector<HTMLSelectElement>("select")!;
    expect(startSelector.value).toBe("trigger:custom-option");
    expect(container.textContent).toContain("driver.unavailable");
    expect(container.textContent).toContain("authored-node");
    expect(container.textContent).toContain("does not declare a JSON Schema");
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(true);
    changeSelect(startSelector, "direct");
    expect(container.querySelector<HTMLButtonElement>("button[type='submit']")?.disabled).toBe(false);
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

  it("validates malformed Test Drive header values before send and returns a safe action error", async () => {
    const malformedDraft = JSON.stringify({
      routeValues: {},
      query: {},
      headers: { "X-Note": ["safe\r\nInjected: secret-value"] },
      body: ""
    });
    const httpOption = { ...customOption, driverId: "Elsa.HttpEndpoint" };
    expect(httpWorkflowTestStimulusEditor.validate({
      option: httpOption,
      mode: customOption.modes[0],
      draft: malformedDraft
    })).toBe("Header 1 has an invalid name or value.");

    const requestResponse = vi.fn();
    const execute = httpWorkflowTestStimulusEditor.executeAction!;
    const result = await execute({
      backend: { baseUrl: "https://studio.example/", http: { requestResponse } } as unknown as StudioEndpointContext,
      option: httpOption,
      mode: customOption.modes[0],
      action: { kind: "http-request", relativeUrl: "/elsa/workflows/test-runs/run-1/http/orders/42", method: "POST" },
      data: { headers: { "X-Note": ["safe\r\nInjected: secret-value"] } },
      editorDraft: malformedDraft
    });

    expect(result.error).toBe("Header 1 has an invalid name or value.");
    expect(JSON.stringify(result)).not.toContain("secret-value");
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it("prevents Test Drive headers from overriding host-configured values without exposing them", async () => {
    const requestResponse = vi.fn();
    const execute = httpWorkflowTestStimulusEditor.executeAction!;
    const result = await execute({
      backend: {
        baseUrl: "https://studio.example/",
        headers: { "X-Studio-Scope": "host-secret" },
        http: { requestResponse }
      } as unknown as StudioEndpointContext,
      option: { ...customOption, driverId: "Elsa.HttpEndpoint" },
      mode: customOption.modes[0],
      action: { kind: "http-request", relativeUrl: "/elsa/workflows/test-runs/run-1/http/orders/42", method: "POST" },
      data: { headers: { "x-studio-scope": ["caller-secret"] } },
      editorDraft: "{}"
    });

    expect(result.error).toBe("Header 1 is configured by Studio and cannot be overridden.");
    expect(JSON.stringify(result)).not.toContain("host-secret");
    expect(JSON.stringify(result)).not.toContain("caller-secret");
    expect(requestResponse).not.toHaveBeenCalled();
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
