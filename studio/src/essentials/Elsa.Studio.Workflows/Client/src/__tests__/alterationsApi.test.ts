import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import {
  AlterationRequestError,
  alterationJobFailureCode,
  decideInterventionPlan,
  getAlterationJob,
  getAlterationPlan,
  pageAlterationJobs,
  retryFaultedActivityPlan,
  submitAlterationPlan,
  supportsOperatorAlterations
} from "../api/alterations";
import { getActivityStateView, getWorkflowInstance, supportsActivityStateView } from "../api/runtime";
import { readWorkflowInstancePermissions } from "../operatorActions";

afterEach(clearApiCapabilityCache);

// Spec 260 T419: the alteration client and the runtime additions, against the real response shapes (PR 1).
describe("operator alteration client", () => {
  it("resolves every route through the runtime capability links", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("capabilities")) return alterationCapabilities;
      if (url.endsWith("/jobs/page?take=10")) return { items: [{ jobId: "job 1", planId: "plan/1", workflowExecutionId: "wf-1", status: "Succeeded" }], hasNext: false };
      if (url.includes("/jobs/")) return { jobId: "job 1", planId: "plan/1", workflowExecutionId: "wf-1", status: "Succeeded", outcomes: [] };
      return { planId: "plan/1", status: "Completed" };
    });
    const client = context({ getJson });

    await expect(supportsOperatorAlterations(client)).resolves.toBe(true);
    await expect(getAlterationPlan(client, "plan/1")).resolves.toMatchObject({ status: "Completed" });
    await expect(pageAlterationJobs(client, "plan/1")).resolves.toMatchObject({ items: [{ jobId: "job 1" }] });
    await expect(getAlterationJob(client, "plan/1", "job 1")).resolves.toMatchObject({ status: "Succeeded", outcomes: [] });

    expect(getJson.mock.calls.map(call => call[0])).toEqual([
      "/capabilities",
      "/runtime/workflows/alteration-plans/plan%2F1",
      "/runtime/workflows/alteration-plans/plan%2F1/jobs/page?take=10",
      "/runtime/workflows/alteration-plans/plan%2F1/jobs/job%201"
    ]);
  });

  it("does not offer actions when a route is missing", async () => {
    const partial = { capabilities: [{ ...alterationCapabilities.capabilities[0], links: alterationCapabilities.capabilities[0].links.slice(0, 2) }] };
    await expect(supportsOperatorAlterations(context({ getJson: vi.fn(async () => partial) }))).resolves.toBe(false);
    await expect(supportsOperatorAlterations(context({ getJson: vi.fn(async () => ({ capabilities: [] })) }))).resolves.toBe(false);
  });

  it("submits one alteration for one run with the Idempotency-Key header and reads Accepted and Replayed alike", async () => {
    const requestResponse = vi.fn(async () => json(202, { planId: "plan-1", status: "CapturingTargets", submissionDisposition: "Replayed", createdAt: "2026-10-20T10:00:00Z" }));
    const client = context({ getJson: vi.fn(async () => alterationCapabilities), requestResponse });

    const receipt = await submitAlterationPlan(client, decideInterventionPlan("wf-42", "inc-5", "compensation.abandon", "  Written off  "), "studio-key-1");

    expect(receipt).toEqual({ planId: "plan-1", status: "CapturingTargets", submissionDisposition: "Replayed", createdAt: "2026-10-20T10:00:00Z" });
    const [url, init] = requestResponse.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/runtime/workflows/alteration-plans");
    expect(init.method).toBe("POST");
    expect((init.headers as Record<string, string>)["Idempotency-Key"]).toBe("studio-key-1");
    expect(JSON.parse(String(init.body))).toEqual({
      target: { executionIds: ["wf-42"] },
      alterations: [{ kind: "DecideIntervention", schemaVersion: 1, payload: { incidentId: "inc-5", decision: "compensation.abandon", reason: "Written off" } }]
    });
  });

  it("falls back to postJson with the header when the host has no raw transport", async () => {
    const postJson = vi.fn(async () => ({ planId: "plan-2", status: "Queued", submissionDisposition: "Accepted", createdAt: "" }));
    const client = context({ getJson: vi.fn(async () => alterationCapabilities), postJson });

    await submitAlterationPlan(client, retryFaultedActivityPlan("wf-42", "ae-11"), "studio-key-2");

    expect(postJson).toHaveBeenCalledWith("/runtime/workflows/alteration-plans", {
      target: { executionIds: ["wf-42"] },
      alterations: [{ kind: "RetryFaultedActivity", schemaVersion: 1, payload: { sourceActivityExecutionId: "ae-11" } }]
    }, { headers: { "Idempotency-Key": "studio-key-2" } });
  });

  it("keeps an optional retry reason only when it is not blank", () => {
    expect(retryFaultedActivityPlan("wf", "ae", "  Service restored ").alterations[0].payload).toEqual({ sourceActivityExecutionId: "ae", reason: "Service restored" });
    expect(retryFaultedActivityPlan("wf", "ae", "   ").alterations[0].payload).toEqual({ sourceActivityExecutionId: "ae" });
  });

  it.each([
    [401, { code: "Unauthorized", message: "Sign in", status: 401 }, null, "Unauthorized", null],
    [403, null, null, null, null],
    [404, null, null, null, null],
    [409, { code: "AlterationIdempotencyConflict", message: "Conflict", status: 409 }, null, "AlterationIdempotencyConflict", null],
    [422, { code: "InvalidAlterationRequest", message: "Invalid", status: 422 }, null, "InvalidAlterationRequest", null],
    [429, { code: "AlterationAdmissionBackpressure", message: "Busy", status: 429 }, "7", "AlterationAdmissionBackpressure", 7],
    [400, { code: "MissingIdempotencyKey", message: "Missing", status: 400 }, null, "MissingIdempotencyKey", null]
  ])("maps HTTP %i to a typed error with its problem code", async (status, body, retryAfter, code, seconds) => {
    const requestResponse = vi.fn(async () => json(status, body, retryAfter ? { "Retry-After": retryAfter } : {}));
    const client = context({ getJson: vi.fn(async () => alterationCapabilities), requestResponse });

    const error = await submitAlterationPlan(client, retryFaultedActivityPlan("wf", "ae"), "studio-key").catch(caught => caught);

    expect(error).toBeInstanceOf(AlterationRequestError);
    expect(error).toMatchObject({ status, code, retryAfterSeconds: seconds });
  });

  it("maps a read failure from the JSON transport to the same typed error", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("capabilities")) return alterationCapabilities;
      throw new StudioHttpError(404, "Not found");
    });

    await expect(getAlterationPlan(context({ getJson }), "plan-1")).rejects.toMatchObject({ name: "AlterationRequestError", status: 404 });
  });

  it("reads the refusal code from the first failed outcome, else the job's safe failure", () => {
    expect(alterationJobFailureCode({ outcomes: [{ ordinal: 0, kind: "DecideIntervention", schemaVersion: 1, status: "Failed", code: "InterventionNotPending" }], failure: { code: "Other" } }))
      .toBe("InterventionNotPending");
    expect(alterationJobFailureCode({ outcomes: [], failure: { code: "TargetNotFound" } })).toBe("TargetNotFound");
    expect(alterationJobFailureCode({ outcomes: [], failure: null })).toBeNull();
  });
});

describe("runtime additions for operator actions", () => {
  it("reads a scope's state view through its templated link, only when the host advertises it", async () => {
    const view = { workflowExecutionId: "wf/1", activityExecutionId: "ae-scope", executableNodeId: "scope", activityStatus: "Running", available: true, viewKind: "compensation.journal", viewVersion: 1, canInspectValues: false, view: {} };
    const getJson = vi.fn(async (url: string) => url.endsWith("capabilities") ? stateViewCapabilities : view);
    const client = context({ getJson });

    await expect(supportsActivityStateView(client)).resolves.toBe(true);
    await expect(getActivityStateView(client, "wf/1", "ae-scope")).resolves.toEqual(view);
    expect(getJson).toHaveBeenLastCalledWith("/runtime/workflows/instances/wf%2F1/activity-executions/ae-scope/state-view");

    clearApiCapabilityCache();
    await expect(supportsActivityStateView(context({ getJson: vi.fn(async () => alterationCapabilities) }))).resolves.toBe(false);
  });

  it("reads the run's permission signal and fails closed when an older host omits it", async () => {
    const details = { instance: { workflowExecutionId: "wf-1" }, activities: [], permissions: { canManage: true, canInspectValues: false } };
    const getJson = vi.fn(async (url: string) => url.endsWith("capabilities") ? stateViewCapabilities : details);

    const loaded = await getWorkflowInstance(context({ getJson }), "wf-1");

    expect(readWorkflowInstancePermissions(loaded)).toEqual({ canManage: true, canInspectValues: false });
    expect(readWorkflowInstancePermissions({ permissions: null })).toEqual({ canManage: false, canInspectValues: false });
    expect(readWorkflowInstancePermissions(undefined)).toEqual({ canManage: false, canInspectValues: false });
  });
});

function context(http: Record<string, unknown>) {
  return { baseUrl: `test://alterations-${Math.random()}`, http } as unknown as StudioEndpointContext;
}

function json(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(body === null ? "" : JSON.stringify(body), {
    status,
    headers: { ...(body === null ? {} : { "Content-Type": "application/json" }), ...headers }
  });
}

const alterationCapabilities = {
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

const stateViewCapabilities = {
  capabilities: [{
    id: "elsa.api.runtime",
    contractVersion: "1",
    links: [
      { rel: "workflow-instance", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true },
      { rel: "activity-execution-state-view", href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view", templated: true }
    ]
  }]
};
