import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import {
  clearPendingOperatorAction,
  followOperatorPlan,
  operatorErrorMessage,
  operatorPollDelay,
  operatorRefusalMessage,
  pendingOperatorActionKey,
  readPendingOperatorAction,
  storePendingOperatorAction,
  waitUntilApplied,
  type OperatorActionApi,
  type OperatorActionModel
} from "../operatorActions";
import { AlterationRequestError } from "../api/alterations";
import { getIncidentRetryReviewEligibility, useCompensationJournals, useOperatorActions, type CompensationJournals, type OperatorActionsApi } from "../workflow-editor/useOperatorActions";
import { WorkflowIncidentList } from "../workflow-editor/WorkflowInstances";
import type { CompensationJournalSource } from "../compensationRuntime";
import { compensationScopeStructureKind } from "../compensationScope";
import type { ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary, WorkflowInstanceDetails, WorkflowInstanceSummary } from "../workflowTypes";

// Spec 260 T423: the submission flow. A key per dialog, the pending plan in session storage, polling with back-off and a
// budget, "Applying…" until the run shows the change, refusals and HTTP errors as messages, and a reload that resumes
// without submitting again.

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  // React schedules its work with setImmediate here; only the polling clock is faked.
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "Date"] });
  if (typeof globalThis.CSS === "undefined") vi.stubGlobal("CSS", { escape: (value: string) => value });
  sessionStorage.clear();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  clearApiCapabilityCache();
});

describe("following a plan", () => {
  it("backs off from 1 s to 5 s", () => {
    expect([0, 1, 2, 3, 4, 5, 9].map(operatorPollDelay)).toEqual([1000, 2000, 3000, 4000, 5000, 5000, 5000]);
  });

  it("polls the plan to its end and reads the job's result", async () => {
    const api = fakeApi(["Queued", "Running", "Completed"], { status: "Succeeded", outcomes: [] });
    const sleep = vi.fn(async (_milliseconds: number) => undefined);

    await expect(followOperatorPlan(api, "plan-1", { sleep, now: () => 0 })).resolves.toEqual({ kind: "succeeded" });
    expect(api.getPlan).toHaveBeenCalledTimes(3);
    expect(sleep.mock.calls.map(call => call[0])).toEqual([1000, 2000]);
    expect(api.getJob).toHaveBeenCalledWith("plan-1", "job-1");
  });

  it("returns the refusal code of a failed job", async () => {
    const api = fakeApi(["CompletedWithFailures"], { status: "Failed", outcomes: [{ ordinal: 0, kind: "DecideIntervention", schemaVersion: 1, status: "Failed", code: "InterventionNotPending" }] });

    await expect(followOperatorPlan(api, "plan-1", { sleep: async () => undefined, now: () => 0 })).resolves.toEqual({ kind: "refused", code: "InterventionNotPending" });
  });

  it("returns a plan-level failure when the plan captured no job", async () => {
    const api = fakeApi(["Failed"], null, { code: "TargetNotFound" });

    await expect(followOperatorPlan(api, "plan-1", { sleep: async () => undefined, now: () => 0 })).resolves.toEqual({ kind: "refused", code: "TargetNotFound" });
  });

  it("says once that the plan is still queued after two minutes, and keeps polling", async () => {
    let clock = 0;
    const api = fakeApi([...Array(40).fill("Queued"), "Completed"], { status: "Succeeded", outcomes: [] });
    const onStillQueued = vi.fn();

    const result = await followOperatorPlan(api, "plan-1", { sleep: async milliseconds => { clock += milliseconds; }, now: () => clock, onStillQueued });

    expect(result).toEqual({ kind: "succeeded" });
    expect(onStillQueued).toHaveBeenCalledTimes(1);
    expect(clock).toBeGreaterThan(120_000);
  });

  it("keeps polling through a lost connection, and stops on a refused read", async () => {
    const api = fakeApi(["Completed"], { status: "Succeeded", outcomes: [] });
    api.getPlan.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    await expect(followOperatorPlan(api, "plan-1", { sleep: async () => undefined, now: () => 0 })).resolves.toEqual({ kind: "succeeded" });
    expect(api.getPlan).toHaveBeenCalledTimes(2);

    const refused = fakeApi(["Completed"], null);
    refused.getPlan.mockRejectedValueOnce(new AlterationRequestError(403, null, "Forbidden"));
    await expect(followOperatorPlan(refused, "plan-1", { sleep: async () => undefined, now: () => 0 })).rejects.toMatchObject({ status: 403 });
  });

  it("stops when aborted", async () => {
    const controller = new AbortController();
    controller.abort();
    await expect(followOperatorPlan(fakeApi(["Queued"], null), "plan-1", { sleep: async () => undefined, now: () => 0, signal: controller.signal })).resolves.toEqual({ kind: "aborted" });
  });

  it("keeps following through a lost connection while it reads the finished plan's job", async () => {
    const api = fakeApi(["Completed"], { status: "Succeeded", outcomes: [] });
    api.pageJobs = vi.fn()
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValue({ items: [{ jobId: "job-1", planId: "plan-1", workflowExecutionId: "wf-42", status: "Succeeded" }] });
    api.getJob.mockRejectedValueOnce(new TypeError("Failed to fetch"));

    await expect(followOperatorPlan(api, "plan-1", { sleep: async () => undefined, now: () => 0 })).resolves.toEqual({ kind: "succeeded" });
    expect(api.getJob).toHaveBeenCalledTimes(2);
  });

  it("keeps checking the run through a lost connection, and stops on a refused read", async () => {
    const options = { sleep: async () => undefined, now: () => 0 };
    const refresh = vi.fn().mockRejectedValueOnce(new TypeError("Failed to fetch")).mockResolvedValueOnce(true);
    await expect(waitUntilApplied(refresh, options)).resolves.toBe(true);
    expect(refresh).toHaveBeenCalledTimes(2);

    await expect(waitUntilApplied(async () => { throw Object.assign(new Error("forbidden"), { status: 403 }); }, options)).rejects.toMatchObject({ status: 403 });
  });

  it("refreshes until the run shows the change, or gives up after the budget", async () => {
    let clock = 0;
    const options = { sleep: async (milliseconds: number) => { clock += milliseconds; }, now: () => clock };
    const refresh = vi.fn().mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    await expect(waitUntilApplied(refresh, options)).resolves.toBe(true);
    expect(refresh).toHaveBeenCalledTimes(2);

    await expect(waitUntilApplied(async () => false, options, 10_000)).resolves.toBe(false);
  });
});

describe("operator action messages", () => {
  it.each([
    ["NoBlockingIncident", "Someone else already acted on this. The run has been refreshed. Nothing was changed."],
    ["InterventionNotPending", "Someone else already acted on this. The run has been refreshed. Nothing was changed."],
    ["SourceActivityNotFaulted", "Someone else already acted on this. The run has been refreshed. Nothing was changed."],
    ["IncidentNotAwaitingIntervention", "The runtime has not finished deciding what to do with this failure. Try again shortly. Nothing was changed."],
    ["SourceWorkClaimed", "The scope is busy. Try again in a moment. Nothing was changed."],
    ["RequesterWorkClaimed", "The scope is busy. Try again in a moment. Nothing was changed."],
    ["SourceParentNotRunning", "The scope is no longer running, so it cannot be changed. Nothing was changed."],
    ["RequesterNotRunning", "The scope is no longer running, so it cannot be changed. Nothing was changed."],
    ["DecisionNotOffered", "This action is not available for this failure. Nothing was changed."],
    ["WorkflowTerminal", "The run has ended; it cannot be changed. Nothing was changed."],
    ["TargetNotFound", "This run is unavailable for operator actions. Nothing was changed."],
    ["InvalidDecideInterventionPayload", "Check the reason: 1 to 500 characters, no control characters. Nothing was changed."],
    ["InvalidRetryFaultedActivityPayload", "Check the reason: 1 to 500 characters, no control characters. Nothing was changed."],
    ["PinnedExecutableUnavailable", "The action could not be applied (PinnedExecutableUnavailable). Nothing was changed."]
  ])("names the refusal %s", (code, message) => {
    expect(operatorRefusalMessage(code)).toBe(message);
  });

  it("names HTTP failures, refreshing the permission signal on a 403", () => {
    expect(operatorErrorMessage(new AlterationRequestError(401, null, "")).message).toBe("Your session has expired. Sign in again to act on this run. Nothing was changed.");
    expect(operatorErrorMessage(new AlterationRequestError(403, null, ""))).toEqual({ message: "You do not have permission to change this run. Nothing was changed.", refreshPermissions: true });
    expect(operatorErrorMessage(new AlterationRequestError(404, null, ""))).toMatchObject({ message: "This run is unavailable or you do not have access to it.", refreshRun: true });
    expect(operatorErrorMessage(new AlterationRequestError(409, "AlterationIdempotencyConflict", "")).message).toBe("This action was already submitted with different details. Refresh and try again. Nothing was changed.");
    expect(operatorErrorMessage(new AlterationRequestError(429, "AlterationAdmissionBackpressure", "", 7)).message).toBe("The runtime is busy. Try again in 7 seconds. Nothing was changed.");
    expect(operatorErrorMessage(new AlterationRequestError(422, "InvalidAlterationRequest", "")).message).toBe("The action could not be applied (InvalidAlterationRequest). Nothing was changed.");
  });
});

describe("the pending plan in session storage", () => {
  it("is kept per run and survives malformed or unavailable storage", () => {
    const pending = { planId: "plan-1", kind: "resolve" as const, incidentId: "inc-5", key: "studio-1", submittedLabel: "Resolve submitted", scopeActivityExecutionId: "ae-scope" };
    storePendingOperatorAction("wf-42", pending);
    expect(readPendingOperatorAction("wf-42")).toEqual(pending);
    expect(readPendingOperatorAction("wf-other")).toBeNull();
    clearPendingOperatorAction("wf-42");
    expect(readPendingOperatorAction("wf-42")).toBeNull();

    sessionStorage.setItem(pendingOperatorActionKey("wf-42"), "{not json");
    expect(readPendingOperatorAction("wf-42")).toBeNull();
    const throwing = { getItem: () => { throw new Error("blocked"); }, setItem: () => { throw new Error("blocked"); }, removeItem: () => { throw new Error("blocked"); } };
    expect(readPendingOperatorAction("wf-42", throwing)).toBeNull();
    expect(() => storePendingOperatorAction("wf-42", pending, throwing)).not.toThrow();
    expect(() => clearPendingOperatorAction("wf-42", throwing)).not.toThrow();
  });
});

describe("useOperatorActions", () => {
  it("distinguishes checking, advertised, unsupported and failed action discovery while ignoring stale context results", async () => {
    let resolveOld!: (document: unknown) => void;
    const oldContext = {
      baseUrl: "https://operator-check.example/old",
      http: { getJson: vi.fn(() => new Promise(resolve => { resolveOld = resolve; })) }
    } as unknown as StudioEndpointContext;
    const newContext = fakeBackend({}).context;
    const sink: { api?: OperatorActionsApi } = {};

    flushSync(() => root.render(<Harness context={oldContext} sink={sink} />));
    expect(sink.api?.actionAvailability).toBe("checking");
    flushSync(() => root.render(<Harness context={newContext} sink={sink} />));
    await flush();
    expect(sink.api?.actionAvailability).toBe("available");
    expect(sink.api?.canAct).toBe(true);
    resolveOld({ capabilities: [] });
    await flush();
    expect(sink.api?.actionAvailability).toBe("available");

    const unsupportedContext = {
      baseUrl: "https://operator-check.example/unsupported",
      http: { getJson: vi.fn(async () => ({ capabilities: [] })) }
    } as unknown as StudioEndpointContext;
    flushSync(() => root.render(<Harness context={unsupportedContext} sink={sink} />));
    await flush();
    expect(sink.api?.actionAvailability).toBe("unavailable");
    expect(sink.api?.canAct).toBe(false);

    const failedContext = {
      baseUrl: "https://operator-check.example/failed",
      http: { getJson: vi.fn(async () => { throw new TypeError("network failure"); }) }
    } as unknown as StudioEndpointContext;
    flushSync(() => root.render(<Harness context={failedContext} sink={sink} />));
    await flush();
    expect(sink.api?.actionAvailability).toBe("unknown");
    expect(sink.api?.canAct).toBe(false);
  });

  it("only calls a requester canceled when the exact requester execution is known canceled", async () => {
    const backend = fakeBackend({});
    const harness = await mount(backend.context);
    const canceled = { ...journalRun()[0], workflowExecutionId: "wf-42", status: "Cancelled" };
    const canceledRequester = {
      ...waitingIncident(),
      resolutionOutcome: { ...waitingIncident().resolutionOutcome!, metadata: {
        ...waitingIncident().resolutionOutcome!.metadata,
        "runtime.interventionRequestedBy": "ae-scope"
      } }
    };
    flushSync(() => root.render(<Harness context={backend.context} sink={harness.sink} activities={[canceled]} extra={api => <output>{JSON.stringify(api.guidanceForIncident(canceledRequester))}</output>} />));
    await flush();

    const unrelatedRequester = {
      ...waitingIncident(),
      resolutionOutcome: { ...waitingIncident().resolutionOutcome!, metadata: { "runtime.interventionRequestedBy": "ae-other" } }
    };
    expect(harness.current().guidanceForIncident(canceledRequester).requesterCancelled).toBe(true);
    expect(harness.current().guidanceForIncident(unrelatedRequester).requesterCancelled).toBe(false);
  });

  it("maps a RetryScheduled row back only to its exact failed handler execution", async () => {
    const backend = fakeBackend({});
    const source = scheduledJournalView();
    const incident = journalIncidents()[1];
    let actions: OperatorActionsApi | undefined;
    function ScheduledHarness() {
      const api = useOperatorActions({
        context: backend.context,
        workflowExecutionId: "wf-1",
        details: { instance: { workflowExecutionId: "wf-1" } as WorkflowInstanceDetails["instance"], activities: journalRun(), incidents: [incident], permissions: { canManage: true, canInspectValues: false } },
        root: journalScope(),
        activities: journalRun().filter(activity => activity.activityExecutionId !== "ae-4" && activity.activityExecutionId !== "ae-5"),
        journals: { get: () => source, request: vi.fn(), reload: async () => source, reset: vi.fn(), generation: 0 },
        labelOf: () => undefined,
        refreshRun: async () => null
      });
      actions = api;
      return null;
    }
    flushSync(() => root.render(<ScheduledHarness />));
    await flush();

    expect(actions?.guidanceForIncident(incident)).toMatchObject({ retryNotBefore: "2026-10-23T10:42:30Z", retryDue: false });
    const unrelated = { ...incident, activityExecutionId: "different-execution" };
    expect(actions?.guidanceForIncident(unrelated).retryNotBefore).toBeUndefined();
  });

  it.each([
    ["future", "2026-10-23T10:42:30Z", "An automatic retry is scheduled"],
    ["due", "2026-10-21T10:42:30Z", "An automatic retry is due"]
  ])("hides duplicate incident controls in the actual details dialog when the exact retry is %s", async (_state, notBefore, expectedGuidance) => {
    vi.setSystemTime(new Date("2026-10-22T10:00:00Z"));
    const backend = fakeBackend({});
    const incident = journalIncidents()[1];
    const source = scheduledJournalView(notBefore);
    const activities = journalRun().filter(activity => activity.activityExecutionId !== "ae-4" && activity.activityExecutionId !== "ae-5");

    flushSync(() => root.render(<ScheduledIncidentListHarness context={backend.context} incidents={[incident]} activities={activities} source={source} />));
    await flush();
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-incident-open-details")!.click());
    await flush();

    const dialog = container.querySelector<HTMLElement>("[role=dialog]")!;
    expect(dialog.textContent).toContain(expectedGuidance);
    expect(dialog.querySelector(".wf-incident-operator-actions")).toBeNull();
  });

  it("keeps canonical controls for an unrelated incident while another incident has a scheduled retry", async () => {
    const backend = fakeBackend({});
    const scheduled = journalIncidents()[1];
    const unrelated = { ...scheduled, incidentId: "inc-7", activityExecutionId: "ae-7" };
    const activities = [
      ...journalRun().filter(activity => activity.activityExecutionId !== "ae-4" && activity.activityExecutionId !== "ae-5"),
      { ...journalRun()[6], activityExecutionId: "ae-7" }
    ];

    flushSync(() => root.render(<ScheduledIncidentListHarness context={backend.context} incidents={[scheduled, unrelated]} activities={activities} source={scheduledJournalView()} />));
    await flush();
    const unrelatedRow = container.querySelector<HTMLElement>('[data-incident-id="inc-7"]')!;
    flushSync(() => unrelatedRow.querySelector<HTMLButtonElement>(".wf-incident-open-details")!.click());
    await flush();

    const dialog = container.querySelector<HTMLElement>("[role=dialog]")!;
    expect(dialog.textContent).not.toContain("An automatic retry is scheduled");
    expect([...dialog.querySelectorAll(".wf-incident-operator-actions button")].map(button => button.textContent!.trim()))
      .toEqual(["Retry", "Resolve: effect absent or undone", "Resolve: effect present, keep it", "Abandon recovery"]);
  });

  it("keeps the selected incident's controls available while its journal is not loaded", async () => {
    const backend = fakeBackend({});
    const incident = journalIncidents()[1];
    const activities = journalRun().filter(activity => activity.activityExecutionId !== "ae-4" && activity.activityExecutionId !== "ae-5");

    flushSync(() => root.render(<ScheduledIncidentListHarness context={backend.context} incidents={[incident]} activities={activities} />));
    await flush();
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-incident-open-details")!.click());
    await flush();

    const dialog = container.querySelector<HTMLElement>("[role=dialog]")!;
    expect(dialog.textContent).not.toMatch(/no automatic retry|no retry is scheduled/i);
    expect([...dialog.querySelectorAll(".wf-incident-operator-actions button")].map(button => button.textContent!.trim()))
      .toContain("Retry");
  });

  it("keeps the incident open when its first Retry click reloads an exact scheduled attempt", async () => {
    const backend = fakeBackend({});
    const incident = journalIncidents()[1];
    const activities = journalRun().filter(activity => activity.activityExecutionId !== "ae-4" && activity.activityExecutionId !== "ae-5");
    const reload = vi.fn(async () => scheduledJournalView());

    flushSync(() => root.render(<ScheduledIncidentListHarness context={backend.context} incidents={[incident]} activities={activities} reload={reload} />));
    await flush();
    const returnTrigger = container.querySelector<HTMLButtonElement>(".wf-incident-open-details")!;
    returnTrigger.focus();
    flushSync(() => returnTrigger.click());
    await flush();

    const retryButton = container.querySelector<HTMLButtonElement>(".wf-incident-details-dialog .wf-incident-operator-actions button")!;
    expect(retryButton.textContent).toContain("Retry");
    retryButton.focus();
    expect(document.activeElement).toBe(retryButton);
    flushSync(() => retryButton.click());
    await flush();

    expect(reload).toHaveBeenCalledTimes(1);
    const detailsDialog = container.querySelector<HTMLElement>(".wf-incident-details-dialog");
    expect(detailsDialog).not.toBeNull();
    expect(detailsDialog!.contains(document.activeElement)).toBe(true);
    expect(container.querySelector(".wf-incident-details-dialog .wf-incident-next-step")?.textContent).toContain("An automatic retry is scheduled");
    expect(container.querySelector(".wf-operator-dialog")).toBeNull();
    expect(backend.submits).toHaveLength(0);

    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    await flush();
    expect(container.querySelector(".wf-incident-details-dialog")).toBeNull();
    expect(document.activeElement).toBe(returnTrigger);
  });

  it("keeps focus inside and cancels repeated incident actions while the exact journal reload is pending", async () => {
    const backend = fakeBackend({});
    const incident = journalIncidents()[1];
    const activities = journalRun().filter(activity => activity.activityExecutionId !== "ae-4" && activity.activityExecutionId !== "ae-5");
    let resolveReload!: (source: CompensationJournalSource) => void;
    const reload = vi.fn(() => new Promise<CompensationJournalSource>(resolve => { resolveReload = resolve; }));

    flushSync(() => root.render(<ScheduledIncidentListHarness context={backend.context} incidents={[incident]} activities={activities} reload={reload} />));
    await flush();
    const returnTrigger = container.querySelector<HTMLButtonElement>(".wf-incident-open-details")!;
    returnTrigger.focus();
    flushSync(() => returnTrigger.click());
    await flush();

    const retryButton = container.querySelector<HTMLButtonElement>(".wf-incident-details-dialog .wf-incident-operator-actions button")!;
    retryButton.focus();
    expect(document.activeElement).toBe(retryButton);
    flushSync(() => {
      retryButton.click();
      retryButton.click();
    });
    expect(reload).toHaveBeenCalledTimes(1);
    expect(container.querySelector<HTMLButtonElement>(".wf-incident-details-dialog .wf-incident-operator-actions button")?.disabled).toBe(true);
    const detailsDialog = container.querySelector<HTMLElement>(".wf-incident-details-dialog")!;
    expect(detailsDialog.contains(document.activeElement)).toBe(true);

    flushSync(() => document.activeElement?.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    await flush();
    expect(container.querySelector(".wf-incident-details-dialog")).toBeNull();
    expect(document.activeElement).toBe(returnTrigger);

    resolveReload(journalView());
    await flush();
    expect(reload).toHaveBeenCalledTimes(1);
    expect(container.querySelector(".wf-operator-dialog")).toBeNull();
    expect(backend.submits).toHaveLength(0);
  });

  it("submits once with the dialog's key, then shows submitted, Applying… and the result, and clears the stored plan", async () => {
    const backend = fakeBackend({ plans: ["Queued", "Completed"], job: { status: "Succeeded", outcomes: [] } });
    const refreshRun = vi.fn()
      .mockResolvedValueOnce(details([waitingIncident()]))
      .mockResolvedValue(details([{ ...waitingIncident(), isBlocking: false, status: "Resolved", resolutionOutcome: { actionKind: "OperatorRetry", metadata: {} } }]));
    const harness = await mount(backend.context, { refreshRun });

    openAndConfirm(harness, retryModel());
    await flush();
    expect(backend.submits).toHaveLength(1);
    expect(backend.submits[0].key).toMatch(/^studio-/);
    expect(JSON.parse(sessionStorage.getItem(pendingOperatorActionKey("wf-42"))!)).toMatchObject({ planId: "plan-1", incidentId: "inc-5", key: backend.submits[0].key });
    expect(harness.current().pending).toEqual({ incidentId: "inc-5", label: "Retry submitted" });
    expect(harness.current().guidanceForIncident(waitingIncident()).pendingIncidentId).toBe("inc-5");
    expect(harness.current().guidanceForIncident({ ...waitingIncident(), incidentId: "inc-6" }).pendingIncidentId).toBe("inc-5");
    expect(status()).toBe("Retry submitted");
    expect(container.querySelector("[role=dialog]")).toBeNull();

    await advance(1000);
    expect(harness.current().pending?.label).toBe("Applying…");
    await advance(1000);
    expect(status()).toBe("Retry applied.");
    expect(harness.current().pending).toBeNull();
    expect(sessionStorage.getItem(pendingOperatorActionKey("wf-42"))).toBeNull();
    expect(backend.submits).toHaveLength(1);
  });

  it("only stores the plan when the page left the run while the submission was in flight", async () => {
    let release!: () => void;
    const backend = fakeBackend({ plans: ["Completed"], submitGate: new Promise<void>(resolve => { release = resolve; }) });
    const harness = await mount(backend.context);

    openAndConfirm(harness, retryModel());
    await flush();
    expect(backend.submits).toHaveLength(1);
    flushSync(() => root.render(<></>));
    release();
    await flush();
    await advance(5000);

    expect(JSON.parse(sessionStorage.getItem(pendingOperatorActionKey("wf-42"))!)).toMatchObject({ planId: "plan-1" });
    expect(backend.planReads).toBe(0);
  });

  it("shows a refusal, refreshes the run and clears the plan", async () => {
    const backend = fakeBackend({ plans: ["CompletedWithFailures"], job: { status: "Failed", outcomes: [{ ordinal: 0, kind: "DecideIntervention", schemaVersion: 1, status: "Failed", code: "InterventionNotPending" }] } });
    const refreshRun = vi.fn(async () => details([waitingIncident()]));
    const harness = await mount(backend.context, { refreshRun });

    openAndConfirm(harness, retryModel(), "");
    await flush();
    await flush();

    expect(status()).toBe("Someone else already acted on this. The run has been refreshed. Nothing was changed.");
    expect(container.querySelector("[role=status]")!.getAttribute("data-tone")).toBe("error");
    expect(refreshRun).toHaveBeenCalled();
    expect(sessionStorage.getItem(pendingOperatorActionKey("wf-42"))).toBeNull();
  });

  it.each([
    [401, "Your session has expired. Sign in again to act on this run. Nothing was changed."],
    [403, "You do not have permission to change this run. Nothing was changed."]
  ])("closes the dialog on a %i, says why and refreshes the run's permission signal", async (statusCode, message) => {
    const backend = fakeBackend({ submitStatus: statusCode });
    const refreshRun = vi.fn(async () => details([waitingIncident()], false));
    const harness = await mount(backend.context, { refreshRun });

    openAndConfirm(harness, retryModel());
    await flush();

    expect(container.querySelector("[role=dialog]")).toBeNull();
    expect(status()).toBe(message);
    expect(refreshRun).toHaveBeenCalledTimes(1);
    expect(sessionStorage.getItem(pendingOperatorActionKey("wf-42"))).toBeNull();
  });

  it("keeps the dialog open on a 429 and resends with the same key", async () => {
    const backend = fakeBackend({ submitStatus: 429, retryAfter: "7" });
    const harness = await mount(backend.context);

    openAndConfirm(harness, retryModel());
    await flush();
    expect(container.querySelector("[role=dialog] [role=alert]")!.textContent).toContain("The runtime is busy. Try again in 7 seconds.");

    flushSync(() => container.querySelector<HTMLButtonElement>("button[type=submit]")!.click());
    await flush();
    expect(backend.submits.map(submit => submit.key)).toHaveLength(2);
    expect(new Set(backend.submits.map(submit => submit.key)).size).toBe(1);
  });

  it("resumes a stored plan after a reload without submitting it again", async () => {
    storePendingOperatorAction("wf-42", { planId: "plan-1", kind: "resolve", incidentId: "inc-5", key: "studio-old", submittedLabel: "Resolve submitted" });
    const backend = fakeBackend({ plans: ["Running", "Completed"], job: { status: "Succeeded", outcomes: [] } });
    const refreshRun = vi.fn(async () => details([{ ...waitingIncident(), isBlocking: false, status: "Resolved", resolutionOutcome: { actionKind: "OperatorDecision", metadata: {} } }]));
    const harness = await mount(backend.context, { refreshRun });

    expect(harness.current().pending).toEqual({ incidentId: "inc-5", label: "Resolve submitted" });
    await advance(1000);
    await flush();
    expect(status()).toBe("Resolve applied.");
    expect(backend.submits).toHaveLength(0);
    expect(backend.planReads).toBeGreaterThan(0);
  });

  it("says the plan is still queued after two minutes and keeps checking", async () => {
    const backend = fakeBackend({ plans: Array(60).fill("Queued"), job: { status: "Succeeded", outcomes: [] } });
    const harness = await mount(backend.context);

    openAndConfirm(harness, retryModel());
    await flush();
    await advance(121_000);

    expect(status()).toBe("The action is still queued. Studio will keep checking while this page is open.");
    expect(harness.current().pending?.label).toBe("Retry submitted");
  });

  it("offers Retry and each offered decision in the incident dialog, only when the caller may manage the run", async () => {
    const backend = fakeBackend({});
    const harness = await mount(backend.context);
    const close = vi.fn();
    flushSync(() => root.render(<Harness context={backend.context} sink={harness.sink} activities={retryActivities()} extra={api => api.renderIncidentActions(waitingIncident(), "Cancel delivery", close, vi.fn())} />));
    await flush();

    expect([...container.querySelectorAll(".wf-incident-operator-actions button")].map(button => button.textContent!.trim()))
      .toEqual(["Retry", "Resolve: effect absent or undone", "Resolve: effect present, keep it", "Abandon recovery"]);

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-incident-operator-actions button")!.click());
    await flush();
    expect(close).toHaveBeenCalled();
    expect(container.querySelector("[role=dialog] h3")!.textContent).toBe('Retry "Cancel delivery"?');

    flushSync(() => root.render(<Harness context={backend.context} sink={harness.sink} canManage={false} activities={retryActivities()} extra={api => api.renderIncidentActions(waitingIncident(), "Cancel delivery", close, vi.fn())} />));
    await flush();
    expect(container.querySelector(".wf-incident-operator-actions")).toBeNull();
  });

  it.each([
    ["a Scheduled source", retryActivities("Scheduled"), "ineligible", "source-not-faulted"],
    ["a missing exact source", [], "unknown", "source-not-loaded"],
    ["a missing recorded parent", retryActivities("Faulted", "missing"), "unknown", "parent-not-loaded"],
    ["a non-Running recorded parent", retryActivities("Faulted", "Completed"), "ineligible", "parent-not-running"]
  ] as const)("keeps Retry hidden for %s while preserving offered decisions", async (_case, activities, expectedStatus, expectedReason) => {
    const backend = fakeBackend({});
    const harness = await mount(backend.context);
    flushSync(() => root.render(<Harness context={backend.context} sink={harness.sink} activities={[...activities]} extra={api => api.renderIncidentActions(waitingIncident(), "Cancel delivery", vi.fn(), vi.fn())} />));
    await flush();

    expect(harness.current().guidanceForIncident(waitingIncident()).retryReviewEligibility).toMatchObject({ status: expectedStatus, reason: expectedReason });
    expect([...container.querySelectorAll(".wf-incident-operator-actions button")].map(button => button.textContent!.trim()))
      .toEqual(["Resolve: effect absent or undone", "Resolve: effect present, keep it", "Abandon recovery"]);
  });

  it("hides Retry and explains a known active same-run descendant blocker", async () => {
    const backend = fakeBackend({});
    const harness = await mount(backend.context);
    const selected = waitingIncident();
    const descendant = {
      ...retryActivities()[1],
      activityExecutionId: "ae-descendant",
      parentActivityExecutionId: "ae-11",
      provenance: { parentActivityExecutionId: "ae-11" }
    };
    const blocker: IncidentStateSummary = {
      ...selected,
      incidentId: "inc-descendant",
      activityExecutionId: "ae-descendant",
      resolutionOutcome: null
    };
    const runDetails = details([selected, blocker]);
    const activities = [...retryActivities(), descendant];

    flushSync(() => root.render(<Harness context={backend.context} sink={harness.sink} activities={activities} runDetails={runDetails} extra={api => api.renderIncidentActions(selected, "Cancel delivery", vi.fn(), vi.fn())} />));
    await flush();

    expect(harness.current().guidanceForIncident(selected).retryReviewEligibility)
      .toMatchObject({ status: "ineligible", reason: "blocking-descendant-incident" });
    expect([...container.querySelectorAll(".wf-incident-operator-actions button")].map(button => button.textContent!.trim()))
      .toEqual(["Resolve: effect absent or undone", "Resolve: effect present, keep it", "Abandon recovery"]);
  });

  it.each([
    ["a different run", (selected: IncidentStateSummary, descendantId: string) => ({ ...selected, incidentId: "inc-other-run", workflowExecutionId: "wf-other", activityExecutionId: descendantId })],
    ["a historical descendant", (selected: IncidentStateSummary, descendantId: string) => ({ ...selected, incidentId: "inc-historical", activityExecutionId: descendantId, status: "Resolved" })],
    ["an unrelated activity", (selected: IncidentStateSummary, _descendantId: string) => ({ ...selected, incidentId: "inc-unrelated", activityExecutionId: "ae-other" })]
  ] as const)("keeps Retry available for %s evidence", async (_label, makeIncident) => {
    const backend = fakeBackend({});
    const harness = await mount(backend.context);
    const selected = waitingIncident();
    const descendant = {
      ...retryActivities()[1],
      activityExecutionId: "ae-descendant",
      parentActivityExecutionId: "ae-11",
      provenance: { parentActivityExecutionId: "ae-11" }
    };
    const unrelatedActivity = {
      ...retryActivities()[1],
      activityExecutionId: "ae-other",
      parentActivityExecutionId: "ae-parent",
      provenance: { parentActivityExecutionId: "ae-parent" }
    };
    const blocker = { ...makeIncident(selected, "ae-descendant"), isBlocking: true };
    const runDetails = details([selected, blocker]);
    const activities = [...retryActivities(), descendant, unrelatedActivity];

    flushSync(() => root.render(<Harness context={backend.context} sink={harness.sink} activities={activities} runDetails={runDetails} extra={api => api.renderIncidentActions(selected, "Cancel delivery", vi.fn(), vi.fn())} />));
    await flush();

    expect(harness.current().guidanceForIncident(selected).retryReviewEligibility.status).toBe("eligible");
    expect([...container.querySelectorAll(".wf-incident-operator-actions button")].map(button => button.textContent!.trim()))
      .toContain("Retry");
  });

  it("does not classify a blocker as a descendant when its activity ancestry is only partially loaded", async () => {
    const backend = fakeBackend({});
    const harness = await mount(backend.context);
    const selected = waitingIncident();
    const partialDescendant = {
      ...retryActivities()[1],
      activityExecutionId: "ae-partial-descendant",
      parentActivityExecutionId: "ae-not-loaded",
      provenance: { parentActivityExecutionId: "ae-not-loaded" }
    };
    const blocker: IncidentStateSummary = {
      ...selected,
      incidentId: "inc-partial-descendant",
      activityExecutionId: "ae-partial-descendant",
      resolutionOutcome: null
    };
    const runDetails = { ...details([selected, blocker]), activityNextContinuationToken: "page-2" };
    const activities = [...retryActivities(), partialDescendant];

    flushSync(() => root.render(<Harness context={backend.context} sink={harness.sink} activities={activities} runDetails={runDetails} extra={api => api.renderIncidentActions(selected, "Cancel delivery", vi.fn(), vi.fn())} />));
    await flush();

    expect(harness.current().guidanceForIncident(selected).retryReviewEligibility.status).toBe("eligible");
    expect([...container.querySelectorAll(".wf-incident-operator-actions button")].map(button => button.textContent!.trim()))
      .toContain("Retry");
  });

  it.each([
    ["a terminal run", waitingIncident(), "Completed"],
    ["a historical incident", { ...waitingIncident(), status: "Resolved" }, "Running"]
  ] as const)("suppresses all controls for %s", async (_case, selectedIncident, runStatus) => {
    const backend = fakeBackend({});
    const harness = await mount(backend.context);
    const runDetails = { ...details([selectedIncident]), instance: { ...details([selectedIncident]).instance, status: runStatus } };
    flushSync(() => root.render(<Harness context={backend.context} sink={harness.sink} activities={retryActivities()} runDetails={runDetails} extra={api => api.renderIncidentActions(selectedIncident, "Cancel delivery", vi.fn(), vi.fn())} />));
    await flush();

    expect(container.querySelector(".wf-incident-operator-actions")).toBeNull();
  });

  it("requires exact Faulted source and Running recorded parent before showing Retry", () => {
    const eligibility = getIncidentRetryReviewEligibility(waitingIncident(), details([waitingIncident()]).instance, retryActivities());
    expect(eligibility).toEqual({ status: "eligible" });
  });
});

describe("an action opened from the incident list", () => {
  it("reads the scope's journal when none is cached and builds the dialog from what it read", async () => {
    // CodeRabbit on #3140: the journal read on click reached the dialog only on the next render, so the dialog fell back
    // to run data: no operation key, no next compensation.
    const getJson = vi.fn(async (url: string) => url.endsWith("capabilities")
      ? { capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: [...capabilities.capabilities[0].links, stateViewLink] }] }
      : journalView());
    const context = { baseUrl: `test://incident-journal-${Math.random()}`, http: { getJson } } as unknown as StudioEndpointContext;
    const incidents = journalIncidents();
    function JournalHarness() {
      const journals = useCompensationJournals(context, "wf-1");
      const api = useOperatorActions({
        context,
        workflowExecutionId: "wf-1",
        details: { instance: { workflowExecutionId: "wf-1" } as WorkflowInstanceDetails["instance"], activities: [], incidents, permissions: { canManage: true, canInspectValues: false } },
        root: journalScope(),
        activities: journalRun(),
        journals,
        labelOf: () => undefined,
        refreshRun: async () => null
      });
      return <>{api.dialog}{api.renderIncidentActions(incidents[1], "cancel", vi.fn(), vi.fn())}</>;
    }
    flushSync(() => root.render(<JournalHarness />));
    await flush();
    expect(getJson.mock.calls.some(call => String(call[0]).endsWith("/state-view"))).toBe(false);

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-incident-operator-actions button")!.click());
    await flush();

    expect(getJson.mock.calls.filter(call => String(call[0]).endsWith("/state-view"))).toHaveLength(1);
    const dialog = container.querySelector("[role=dialog]")!.textContent!;
    expect(dialog).toContain("with the same operation key (ae-scope/step/1/compensate)");
    expect(dialog).toContain('If it succeeds, recovery continues with the compensation of "Reserve inventory".');
  });
});

describe("useCompensationJournals", () => {
  it("reads a live scope's journal once, never a completed scope's, and again after a reset", async () => {
    const getJson = vi.fn(async (url: string) => url.endsWith("capabilities")
      ? { capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: [{ rel: "activity-execution-state-view", href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view", templated: true }] }] }
      : { workflowExecutionId: "wf-42", activityExecutionId: "ae-scope", executableNodeId: "scope", activityStatus: "Running", available: true, viewKind: "compensation.journal", viewVersion: 1, canInspectValues: false, view: { phase: "Running", entries: [], remaining: [] } });
    const context = { baseUrl: `test://journals-${Math.random()}`, http: { getJson } } as unknown as StudioEndpointContext;
    const sink: { journals?: CompensationJournals } = {};
    function JournalHarness() {
      sink.journals = useCompensationJournals(context, "wf-42");
      return null;
    }
    flushSync(() => root.render(<JournalHarness />));
    const stateViewReads = () => getJson.mock.calls.filter(call => String(call[0]).endsWith("/state-view")).length;

    flushSync(() => sink.journals!.request("ae-scope", "Running"));
    expect(sink.journals!.get("ae-scope")).toBe("loading");
    await flush();
    expect(sink.journals!.get("ae-scope")).toMatchObject({ available: true, viewKind: "compensation.journal" });
    flushSync(() => sink.journals!.request("ae-scope", "Running"));
    flushSync(() => sink.journals!.request("ae-done", "Completed"));
    await flush();
    expect(stateViewReads()).toBe(1);
    expect(sink.journals!.get("ae-done")).toBeUndefined();

    const generation = sink.journals!.generation;
    flushSync(() => sink.journals!.reset());
    expect(sink.journals!.generation).toBe(generation + 1);
    expect(sink.journals!.get("ae-scope")).toBeUndefined();
    flushSync(() => sink.journals!.request("ae-scope", "Running"));
    await flush();
    expect(stateViewReads()).toBe(2);
  });
});

function retryModel(): OperatorActionModel {
  return {
    kind: "retry",
    title: 'Retry the compensation of "Reserve delivery"?',
    confirmLabel: "Retry",
    tone: "default",
    target: ["Scope"],
    effect: ["Runs it again."],
    reasonRequired: false,
    workflowExecutionId: "wf-42",
    incidentId: "inc-5",
    sourceActivityExecutionId: "ae-11",
    submittedLabel: "Retry submitted"
  };
}

function waitingIncident(): IncidentStateSummary {
  return {
    incidentId: "inc-5",
    workflowExecutionId: "wf-42",
    activityExecutionId: "ae-11",
    severity: "Error",
    status: "Blocking",
    isBlocking: true,
    failureType: "ActivityReturnedFault",
    message: "Delivery service unavailable",
    createdAt: "2026-10-20T10:00:00Z",
    metadata: {},
    resolutionOutcome: {
      actionKind: "WaitForIntervention",
      systemSource: "StructuralIntervention",
      metadata: {
        "runtime.interventionDecisions": JSON.stringify(["compensation.resolve.effect-absent", "compensation.resolve.effect-accepted", "compensation.abandon"]),
        "runtime.interventionRequestedBy": "ae-scope"
      }
    }
  };
}

function details(incidents: IncidentStateSummary[], canManage = true): WorkflowInstanceDetails {
  return {
    instance: { workflowExecutionId: "wf-42" } as WorkflowInstanceDetails["instance"],
    activities: [],
    incidents,
    permissions: { canManage, canInspectValues: false }
  };
}

const journals: CompensationJournals = { get: () => undefined, request: () => undefined, reload: async () => "unavailable", reset: () => undefined, generation: 0 };

function retryActivities(sourceStatus = "Faulted", parentStatus: "Running" | "Completed" | "missing" = "Running"): ActivityExecutionStateSummary[] {
  const parentId = "ae-parent";
  const parent: ActivityExecutionStateSummary = {
    activityExecutionId: parentId,
    workflowExecutionId: "wf-42",
    executableNodeId: "parent-node",
    authoredActivityId: "parent-node",
    activityType: "Elsa.Test.Parent",
    activityTypeVersion: "1.0.0",
    status: parentStatus === "missing" ? "Running" : parentStatus,
    scheduledAt: "2026-10-20T10:00:00Z",
    metadata: {}
  };
  const source: ActivityExecutionStateSummary = {
    activityExecutionId: "ae-11",
    workflowExecutionId: "wf-42",
    executableNodeId: "source-node",
    authoredActivityId: "source-node",
    activityType: "Elsa.Test.Source",
    activityTypeVersion: "1.0.0",
    status: sourceStatus,
    scheduledAt: "2026-10-20T10:00:01Z",
    parentActivityExecutionId: parentId,
    provenance: { parentActivityExecutionId: parentId },
    metadata: {}
  };
  return parentStatus === "missing" ? [source] : [parent, source];
}

function Harness({ context, sink, canManage = true, refreshRun, extra, activities = [], runDetails }: {
  context: StudioEndpointContext;
  sink: { api?: OperatorActionsApi };
  canManage?: boolean;
  refreshRun?: () => Promise<WorkflowInstanceDetails | null>;
  activities?: ActivityExecutionStateSummary[];
  runDetails?: WorkflowInstanceDetails;
  extra?: (api: OperatorActionsApi) => React.ReactNode;
}) {
  const api = useOperatorActions({
    context,
    workflowExecutionId: "wf-42",
    details: runDetails ?? details([waitingIncident()], canManage),
    root: null,
    activities,
    journals,
    labelOf: () => undefined,
    refreshRun: refreshRun ?? (async () => details([waitingIncident()]))
  });
  sink.api = api;
  return <>{api.status}{api.dialog}{extra?.(api)}</>;
}

async function mount(context: StudioEndpointContext, options: { refreshRun?: () => Promise<WorkflowInstanceDetails | null> } = {}) {
  const sink: { api?: OperatorActionsApi } = {};
  flushSync(() => root.render(<Harness context={context} sink={sink} refreshRun={options.refreshRun} />));
  await flush();
  return { sink, current: () => sink.api! };
}

function openAndConfirm(harness: { current(): OperatorActionsApi }, model: OperatorActionModel, reason = "Delivery service restored") {
  flushSync(() => harness.current().open(model));
  const field = container.querySelector<HTMLTextAreaElement>("textarea")!;
  if (reason) {
    flushSync(() => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(field, reason);
      field.dispatchEvent(new Event("input", { bubbles: true }));
    });
  }
  flushSync(() => container.querySelector<HTMLButtonElement>("button[type=submit]")!.click());
}

function status() {
  return container.querySelector("[role=status]")!.textContent;
}

async function flush() {
  for (let round = 0; round < 6; round++) {
    await vi.advanceTimersByTimeAsync(0);
    await new Promise<void>(resolve => (globalThis as unknown as { setImmediate(callback: () => void): void }).setImmediate(resolve));
  }
}

async function advance(milliseconds: number) {
  await vi.advanceTimersByTimeAsync(milliseconds);
  await flush();
}

function fakeApi(plans: string[], job: { status: string; outcomes: Array<{ ordinal: number; kind: string; schemaVersion: number; status: string; code: string }> } | null, failure?: { code: string }): OperatorActionApi & { getPlan: ReturnType<typeof vi.fn>; getJob: ReturnType<typeof vi.fn> } {
  let read = 0;
  return {
    submit: vi.fn(),
    getPlan: vi.fn(async () => ({ planId: "plan-1", status: plans[Math.min(read++, plans.length - 1)], failure: failure ?? null })),
    pageJobs: vi.fn(async () => ({ items: job ? [{ jobId: "job-1", planId: "plan-1", workflowExecutionId: "wf-42", status: job.status }] : [] })),
    getJob: vi.fn(async () => ({ jobId: "job-1", planId: "plan-1", workflowExecutionId: "wf-42", status: job!.status, outcomes: job!.outcomes }))
  };
}

function fakeBackend(options: {
  plans?: string[];
  job?: { status: string; outcomes: Array<{ ordinal: number; kind: string; schemaVersion: number; status: string; code: string }> };
  submitStatus?: number;
  retryAfter?: string;
  submitGate?: Promise<void>;
}) {
  const submits: Array<{ key: string; body: unknown }> = [];
  const state = { planReads: 0 };
  const plans = options.plans ?? ["Completed"];
  const getJson = vi.fn(async (url: string) => {
    if (url.endsWith("capabilities")) return capabilities;
    if (url.includes("/jobs/page")) return { items: [{ jobId: "job-1", planId: "plan-1", workflowExecutionId: "wf-42", status: options.job?.status ?? "Succeeded" }] };
    if (url.includes("/jobs/")) return { jobId: "job-1", planId: "plan-1", workflowExecutionId: "wf-42", status: options.job?.status ?? "Succeeded", outcomes: options.job?.outcomes ?? [] };
    return { planId: "plan-1", status: plans[Math.min(state.planReads++, plans.length - 1)] };
  });
  const requestResponse = vi.fn(async (_url: string, init: RequestInit) => {
    submits.push({ key: (init.headers as Record<string, string>)["Idempotency-Key"], body: JSON.parse(String(init.body)) });
    if (options.submitGate) await options.submitGate;
    if (options.submitStatus) {
      return new Response(JSON.stringify({ code: "Problem", message: "Problem", status: options.submitStatus }), {
        status: options.submitStatus,
        headers: { "Content-Type": "application/json", ...(options.retryAfter ? { "Retry-After": options.retryAfter } : {}) }
      });
    }
    return new Response(JSON.stringify({ planId: "plan-1", status: "CapturingTargets", submissionDisposition: "Accepted", createdAt: "" }), { status: 202, headers: { "Content-Type": "application/json" } });
  });
  const context = { baseUrl: `test://operator-${Math.random()}`, http: { getJson, requestResponse } } as unknown as StudioEndpointContext;
  return {
    context,
    submits,
    get planReads() {
      return state.planReads;
    }
  };
}

const capabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [
      { rel: "workflow-alteration-plans", href: "runtime/workflows/alteration-plans" },
      { rel: "workflow-alteration-plan", href: "runtime/workflows/alteration-plans/{planId}", templated: true },
      { rel: "workflow-alteration-plan-jobs-page", href: "runtime/workflows/alteration-plans/{planId}/jobs/page", templated: true },
      { rel: "workflow-alteration-job", href: "runtime/workflows/alteration-plans/{planId}/jobs/{jobId}", templated: true }
    ]
  }]
};

const stateViewLink = { rel: "activity-execution-state-view", href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view", templated: true };
const journalDecisions = ["compensation.resolve.effect-absent", "compensation.resolve.effect-accepted", "compensation.abandon"];

// The spec 251 W state: Confirm order failed, Cancel delivery failed three times and waits for an operator.
function journalScope(): ActivityNode {
  const leaf = (nodeId: string): ActivityNode => ({ nodeId, activityVersionId: `${nodeId}-v1`, inputs: [], outputs: [] });
  return {
    nodeId: "scope",
    activityVersionId: "scope-v1",
    displayName: "Order",
    inputs: [],
    outputs: [],
    structure: {
      kind: compensationScopeStructureKind,
      schemaVersion: "1.0.0",
      payload: {
        steps: [
          { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
          { name: "Reserve delivery", activity: leaf("deliver"), compensation: leaf("cancel") },
          { name: "Confirm order", activity: leaf("confirm"), compensation: null }
        ]
      }
    }
  } as ActivityNode;
}

function journalRun(): ActivityExecutionStateSummary[] {
  const execution = (id: string, nodeId: string, status: string, at: string, handlerOf?: string): ActivityExecutionStateSummary => ({
    activityExecutionId: id,
    workflowExecutionId: "wf-1",
    executableNodeId: nodeId,
    authoredActivityId: nodeId,
    activityType: nodeId === "scope" ? "Elsa.Activities.Compensation.Activities.CompensationScope" : "Elsa.Test",
    activityTypeVersion: "1.0.0",
    status,
    scheduledAt: `2026-10-19T10:00:${at}Z`,
    parentActivityExecutionId: null,
    schedulingActivityExecutionId: handlerOf ?? null,
    outcomeNames: null,
    provenance: {
      parentActivityExecutionId: id === "ae-scope" ? null : "ae-scope",
      schedulingActivityExecutionId: handlerOf ?? null,
      schedulingCause: handlerOf ? "compensation" : "ActivityCompletion"
    },
    metadata: {}
  });
  return [
    execution("ae-scope", "scope", "Running", "10"),
    execution("ae-1", "reserve", "Completed", "11"),
    execution("ae-2", "deliver", "Completed", "12"),
    execution("ae-3", "confirm", "Faulted", "13"),
    execution("ae-4", "cancel", "Faulted", "14", "ae-2"),
    execution("ae-5", "cancel", "Faulted", "15", "ae-2"),
    execution("ae-6", "cancel", "Faulted", "16", "ae-2")
  ];
}

function journalIncidents(): IncidentStateSummary[] {
  const base = (incidentId: string, activityExecutionId: string): IncidentStateSummary => ({
    incidentId,
    workflowExecutionId: "wf-1",
    activityExecutionId,
    severity: "Error",
    status: "Resolved",
    resolutionOutcome: { actionKind: "AbsorbFault", systemSource: "StructuralFaultAbsorption", metadata: { "compensation.maxAttempts": "3" } },
    failureType: "ActivityReturnedFault",
    message: "Failed",
    createdAt: `2026-10-19T10:00:${activityExecutionId.slice(3).padStart(2, "0")}Z`,
    isBlocking: false,
    metadata: {}
  });
  return [
    base("inc-3", "ae-3"),
    {
      ...base("inc-6", "ae-6"),
      status: "Blocking",
      isBlocking: true,
      resolvedAt: null,
      resolutionOutcome: {
        actionKind: "WaitForIntervention",
        systemSource: "StructuralIntervention",
        metadata: { "runtime.interventionDecisions": JSON.stringify(journalDecisions), "runtime.interventionRequestedBy": "ae-scope", "compensation.maxAttempts": "3" }
      }
    }
  ];
}

function journalView() {
  const entry = (id: string, step: string, ordinal: number, status: string, forward: string, handler: string, attempts: string[], waiting?: string) => ({
    id,
    step,
    ordinal,
    status,
    forwardOutcome: "Completed",
    operationKey: `ae-scope/step/${ordinal - 1}`,
    forward: { activityExecutionId: forward, iterationId: null },
    handler: { executableNodeId: handler, remaining: true },
    attempts: attempts.map((attempt, index) => ({ number: index + 1, activityExecutionId: attempt, incidentId: `inc-${attempt.slice(3)}`, result: "Faulted" })),
    waiting: waiting ? { incidentId: waiting, decisions: journalDecisions } : null,
    resolution: null,
    origin: null,
    confirmedBy: null
  });
  return {
    workflowExecutionId: "wf-1",
    activityExecutionId: "ae-scope",
    executableNodeId: "scope",
    activityStatus: "Running",
    available: true,
    unavailableReason: null,
    viewKind: "compensation.journal",
    viewVersion: 1,
    canInspectValues: false,
    view: {
      phase: "AwaitingIntervention",
      stateVersion: 4,
      cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", incidentId: "inc-3", failureType: "order.rejected", message: null, messageWithheld: true },
      entries: [
        entry("e1", "Reserve inventory", 1, "Registered", "ae-1", "release", []),
        entry("e2", "Reserve delivery", 2, "Failed", "ae-2", "cancel", ["ae-4", "ae-5", "ae-6"], "inc-6")
      ],
      remaining: ["e2", "e1"],
      confirmations: [],
      handOvers: { received: [], inProgress: [] },
      abandonment: null
    }
  };
}

function scheduledJournalView(notBefore = "2026-10-23T10:42:30Z"): CompensationJournalSource {
  const view = journalView();
  return {
    ...view,
    view: {
      ...view.view,
      phase: "Compensating",
      entries: view.view.entries.map((entry, index) => index === 1 ? {
        ...entry,
        status: "Compensating",
        waiting: null,
        attempts: [
          { number: 1, activityExecutionId: "ae-6", incidentId: "inc-6", result: "Faulted" },
          { number: 2, activityExecutionId: null, incidentId: null, result: "Running", notBefore }
        ]
      } : entry)
    }
  } as unknown as CompensationJournalSource;
}

function ScheduledIncidentListHarness({
  context,
  incidents,
  activities,
  source,
  reload
}: {
  context: StudioEndpointContext;
  incidents: IncidentStateSummary[];
  activities: ActivityExecutionStateSummary[];
  source?: CompensationJournalSource;
  reload?: () => Promise<CompensationJournalSource>;
}) {
  const [journalSource, setJournalSource] = useState(source);
  const summary = { workflowExecutionId: "wf-1", status: "Running" } as WorkflowInstanceSummary;
  const api = useOperatorActions({
    context,
    workflowExecutionId: summary.workflowExecutionId,
    details: { ...details(incidents), instance: summary, activities },
    root: journalScope(),
    activities,
    journals: {
      get: id => id === "ae-scope" ? journalSource : undefined,
      request: () => undefined,
      reload: async () => {
        const updated = reload ? await reload() : journalSource ?? "unavailable";
        setJournalSource(updated);
        return updated;
      },
      reset: () => undefined,
      generation: 0
    },
    labelOf: () => undefined,
    refreshRun: async () => null
  });

  return <>{api.dialog}<WorkflowIncidentList summary={summary} incidents={incidents} activities={activities} renderIncidentActions={api.renderIncidentActions} guidanceForIncident={api.guidanceForIncident} /></>;
}
