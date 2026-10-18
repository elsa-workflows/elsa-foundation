import React from "react";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowInstance } from "../api/runtime";
import { HttpTestResponse, type HttpTestResult } from "../workflow-editor/HttpTestResponse";
import type { IncidentStateSummary, WorkflowInstanceDetails, WorkflowInstanceSummary } from "../workflowTypes";

vi.mock("../api/runtime", () => ({ getWorkflowInstance: vi.fn() }));
vi.mock("../workflow-editor/HttpMessageBodyEditor", () => ({
  HttpMessageBodyEditor: ({ label, value }: { label: string; value: string }) => <pre data-label={label}>{value}</pre>
}));

let mounted: { root: Root; container: HTMLDivElement } | null = null;
const context = {} as StudioEndpointContext;

afterEach(() => {
  if (mounted) {
    flushSync(() => mounted!.root.unmount());
    mounted.container.remove();
    mounted = null;
  }
  vi.mocked(getWorkflowInstance).mockReset();
});

describe("HTTP test response feedback", () => {
  it("reports a faulted run and links directly to it", async () => {
    vi.mocked(getWorkflowInstance).mockResolvedValue(workflowDetails({ status: "Faulted", workflowExecutionId: "run/1" }));
    const result = acceptedResult({ workflowExecutionId: "run/1" });
    const container = renderResponse(result);

    await waitFor(() => expect(container.querySelector(".wf-http-test-accepted")?.textContent).toContain("encountered an error"));

    expect(getWorkflowInstance).toHaveBeenCalledTimes(1);
    expect(getWorkflowInstance).toHaveBeenCalledWith(context, "run/1");
    expect(container.querySelector(".wf-http-test-accepted")?.getAttribute("data-error")).toBe("true");
    expect(container.querySelector<HTMLAnchorElement>(".wf-http-test-accepted a")?.getAttribute("href"))
      .toBe("/workflows/instances/run%2F1");
    expect(container.textContent).not.toContain("Inspect workflow runs");
    expect(container.querySelector("pre[data-label='Response body']")?.textContent).toBe(result.body);
  });

  it("reports an active blocking error incident even while the run is still running", async () => {
    vi.mocked(getWorkflowInstance).mockResolvedValue(workflowDetails({
      status: "Running",
      incidents: [incident({ status: "Open", severity: "Error", isBlocking: true })]
    }));
    const container = renderResponse(acceptedResult());

    await waitFor(() => expect(container.querySelector(".wf-http-test-accepted")?.textContent).toContain("encountered an error"));

    expect(container.querySelector(".wf-http-test-accepted")?.getAttribute("data-error")).toBe("true");
  });

  it("describes a blocking non-error incident as a blocking issue", async () => {
    vi.mocked(getWorkflowInstance).mockResolvedValue(workflowDetails({
      status: "Running",
      incidents: [incident({ status: "Open", severity: "Warning", isBlocking: true })]
    }));
    const container = renderResponse(acceptedResult());

    await waitFor(() => expect(container.querySelector(".wf-http-test-accepted")?.textContent).toContain("blocking issue"));
  });

  it("ignores resolved error incidents and keeps the generic no-response message", async () => {
    vi.mocked(getWorkflowInstance).mockResolvedValue(workflowDetails({
      status: "Running",
      incidents: [incident({ status: "Resolved", severity: "Error", isBlocking: true })]
    }));
    const result = acceptedResult();
    const container = renderResponse(result);

    await waitFor(() => expect(getWorkflowInstance).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(container.querySelector(".wf-http-test-accepted")?.textContent).toContain(result.acceptedMessage!));

    expect(container.querySelector(".wf-http-test-accepted")?.getAttribute("data-error")).toBeNull();
  });

  it("keeps the raw accepted response and body when run inspection is denied", async () => {
    vi.mocked(getWorkflowInstance).mockRejectedValue(new Error("Forbidden"));
    const result = acceptedResult();
    const container = renderResponse(result);

    await waitFor(() => expect(getWorkflowInstance).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(container.querySelector(".wf-http-test-accepted")?.textContent).toContain(result.acceptedMessage!));

    expect(container.querySelector("strong")?.textContent).toBe("202 Accepted");
    expect(container.querySelector("pre[data-label='Response body']")?.textContent).toBe(result.body);
    expect(container.querySelector(".wf-http-test-accepted")?.getAttribute("data-error")).toBeNull();
  });

  it("does not inspect runs for a background-accepted response", async () => {
    const result = acceptedResult({ responseReason: "background-accepted" });
    const container = renderResponse(result);

    await waitFor(() => expect(container.querySelector(".wf-http-test-accepted")?.textContent).toContain(result.acceptedMessage!));

    expect(getWorkflowInstance).not.toHaveBeenCalled();
  });

  it("does not let a late fault lookup replace a newer successful response", async () => {
    const pending = deferred<WorkflowInstanceDetails>();
    vi.mocked(getWorkflowInstance).mockReturnValue(pending.promise);
    const container = renderResponse(acceptedResult());

    await waitFor(() => expect(getWorkflowInstance).toHaveBeenCalledTimes(1));
    const success: HttpTestResult = {
      ...acceptedResult(),
      status: 200,
      statusText: "OK",
      responseReason: null,
      workflowExecutionId: null,
      acceptedMessage: null,
      body: "ok"
    };
    renderResponse(success);
    pending.resolve(workflowDetails({ status: "Faulted" }));

    await waitFor(() => expect(container.querySelector("strong")?.textContent).toBe("200 OK"));
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.querySelector(".wf-http-test-accepted")).toBeNull();
    expect(container.querySelector("pre[data-label='Response body']")?.textContent).toBe("ok");
  });
});

function renderResponse(result: HttpTestResult) {
  if (!mounted) {
    const container = document.createElement("div");
    document.body.append(container);
    mounted = { root: createRoot(container), container };
  }
  flushSync(() => mounted!.root.render(<HttpTestResponse result={result} context={context} />));
  return mounted.container;
}

function acceptedResult(overrides: Partial<HttpTestResult> = {}): HttpTestResult {
  const workflowExecutionId = overrides.workflowExecutionId ?? "run-1";
  return {
    status: 202,
    statusText: "Accepted",
    contentType: "application/json",
    headers: [["content-type", "application/json"]],
    body: JSON.stringify({
      started: workflowExecutionId ? [workflowExecutionId] : [],
      resumed: [],
      response: { mode: "sync", reason: "workflow-response-not-produced" }
    }),
    acceptedMessage: "The workflow did not produce an HTTP response during this exchange. It may have suspended or completed without writing one.",
    responseReason: "workflow-response-not-produced",
    workflowExecutionId: "run-1",
    ...overrides
  };
}

function workflowDetails({ status = "Faulted", incidents = [], workflowExecutionId = "run-1" }: {
  status?: string;
  incidents?: IncidentStateSummary[];
  workflowExecutionId?: string;
} = {}): WorkflowInstanceDetails {
  const instance: WorkflowInstanceSummary = {
    workflowExecutionId,
    artifactId: "artifact-1",
    artifactVersion: "1.0.0",
    artifactHash: "sha256:1",
    definitionId: "definition-1",
    definitionVersionId: "version-1",
    status,
    createdAt: "2026-10-18T12:00:00Z",
    startedAt: "2026-10-18T12:00:00Z",
    activityCount: 0,
    incidentCount: incidents.length
  };
  return { instance, activities: [], incidents };
}

function incident(overrides: Partial<IncidentStateSummary> = {}): IncidentStateSummary {
  return {
    incidentId: "incident-1",
    workflowExecutionId: "run-1",
    severity: "Error",
    status: "Open",
    resolutionAction: "None",
    failureType: "ActivityFailed",
    message: "The activity failed.",
    createdAt: "2026-10-18T12:00:00Z",
    isBlocking: true,
    metadata: {},
    ...overrides
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

async function waitFor(assertion: () => void) {
  const deadline = Date.now() + 2_000;
  while (true) {
    try {
      assertion();
      return;
    } catch (error) {
      if (Date.now() >= deadline) throw error;
      await new Promise(resolve => setTimeout(resolve, 10));
    }
  }
}
