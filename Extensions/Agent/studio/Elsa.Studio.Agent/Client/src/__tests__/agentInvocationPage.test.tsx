import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { AgentInvocationPage, readInvocationReference, runPath } from "../AgentInvocationPage";
import type { InvocationEvidence, InvocationEvidenceView } from "../playgroundApi";

const cleanup = new Set<() => void>();
afterEach(() => { for (const dispose of cleanup) dispose(); cleanup.clear(); });
const reference = { workflowExecutionId: "run-1", activityExecutionId: "act-1" };

function invocation(activityStatus: string, overrides: Partial<InvocationEvidence> = {}): InvocationEvidence {
  return {
    workflowExecutionId: "run-1", activityExecutionId: "act-1",
    agent: { definitionId: "copywriter", definitionVersionId: "copywriter-v3", version: "3.0.0", templateHash: "t", artifactId: "a", artifactHash: "h", allowContinuity: false },
    model: { providerKey: "anthropic", connectionName: "writer", modelId: "claude-fable-5-1" },
    limits: { maximumModelCalls: 8, timeoutMilliseconds: 300000 }, activityStatus,
    output: activityStatus === "Completed" ? { captureMode: "Payload", value: "Moss gardens reward patience." } : null,
    turn: { status: activityStatus === "Completed" ? "Accepted" : "Dispatching", application: activityStatus === "Completed" ? "Applied" : "NotAccepted",
      deadline: "2026-11-03T00:00:00Z", maximumModelCalls: 8, modelRequests: [{ id: "r", reservedAt: "2026-11-03T00:00:00Z", maximumOutputTokens: 4096, providerKey: "anthropic", configuredModelId: "claude-fable-5-1" }],
      usage: activityStatus === "Completed" ? { observedAt: "2026-11-03T00:00:00Z", usageAvailable: true, inputTokens: 5, outputTokens: 3, totalTokens: 8 } : null },
    ...overrides
  };
}

function fakeContext(responses: Array<InvocationEvidenceView | Error>) {
  const queue = [...responses];
  const getJson = vi.fn(async (_path: string) => {
    const next = queue.length > 1 ? queue.shift()! : queue[0];
    if (next instanceof Error) throw next;
    return next;
  });
  return { context: { http: { getJson } } as unknown as StudioEndpointContext, getJson };
}

async function settle() {
  for (let index = 0; index < 20; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

async function render(context: StudioEndpointContext, navigate?: (path: string) => void) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<AgentInvocationPage context={context} reference={reference} navigate={navigate} refreshInterval={0} />));
  await settle();
  cleanup.add(() => { root.unmount(); container.remove(); });
  return container;
}

describe("agent invocation page", () => {
  it("reads the occurrence from its route", () => {
    expect(readInvocationReference("/agents/invocations/run%201/act-1")).toEqual({ workflowExecutionId: "run 1", activityExecutionId: "act-1" });
    expect(readInvocationReference("/agents/invocations/run-1")).toBeNull();
  });

  it("shows a settled invocation's exact version, applied result, usage and output, and opens its run", async () => {
    const navigate = vi.fn();
    const { context, getJson } = fakeContext([{ invocation: invocation("Completed"), valuesPermitted: true }]);
    const container = await render(context, navigate);

    expect(getJson).toHaveBeenCalledWith("/agent/invocations/run-1/act-1/evidence", expect.anything());
    expect(container.querySelector("[data-testid='invocation-summary']")!.textContent).toBe("The agent completed. Accepted and applied.");
    expect(container.querySelector("[data-testid='evidence-output']")!.textContent).toBe("Moss gardens reward patience.");
    expect(container.querySelector("[data-testid='evidence-usage']")!.textContent).toBe("5 input, 3 output, 8 total tokens");
    expect(container.textContent).toContain("copywriter-v3");
    [...container.querySelectorAll("button")].find(x => x.textContent?.trim() === "Open the run")!.click();
    expect(navigate).toHaveBeenCalledWith(runPath(reference));
    expect(getJson).toHaveBeenCalledTimes(1);
  });

  it("refreshes an unsettled invocation and stops once it settles", async () => {
    const { context, getJson } = fakeContext([{ invocation: invocation("Running"), valuesPermitted: true }, { invocation: invocation("Completed"), valuesPermitted: true }]);
    const container = await render(context);
    const reads = getJson.mock.calls.length;
    await settle();

    expect(reads).toBe(2);
    expect(getJson.mock.calls.length).toBe(reads);
    expect(container.querySelector("[data-testid='invocation-summary']")!.textContent).toBe("The agent completed. Accepted and applied.");
  });

  it("treats a recovered occurrence as settled", async () => {
    const { context, getJson } = fakeContext([{ invocation: invocation("Recovered", { turn: null, output: null }), valuesPermitted: true }]);
    const container = await render(context);
    await settle();

    expect(getJson).toHaveBeenCalledTimes(1);
    expect(container.querySelector("[data-testid='invocation-summary']")!.textContent).toBe("The agent's fault was recovered. No model turn evidence is available.");
  });

  it("states an unknown outcome without claiming a result", async () => {
    const { context } = fakeContext([{ invocation: invocation("Faulted", { output: null, fault: { code: "agent.turn.failed", message: "The agent turn did not complete successfully." },
      turn: { ...invocation("Faulted").turn!, status: "OutcomeUnknown", application: "NotAccepted" } }), valuesPermitted: true }]);
    const container = await render(context);

    expect(container.textContent).toContain("The model may have received this request, but no result was accepted.");
    expect(container.querySelector("[data-testid='evidence-fault']")!.textContent).toBe("The agent turn did not complete successfully.");
    expect(container.querySelector("[data-testid='evidence-output']")).toBeNull();
  });

  it("leaves the result out without the value permission", async () => {
    const { context } = fakeContext([{ invocation: invocation("Completed", { output: { captureMode: "Payload", value: null } }), valuesPermitted: false }]);
    const container = await render(context);

    expect(container.querySelector("[data-testid='evidence-output-withheld']")!.textContent).toContain("permission to inspect captured values");
  });

  it("keeps the last evidence on screen when refreshes fail, and stops refreshing after five failures", async () => {
    const { context, getJson } = fakeContext([{ invocation: invocation("Running"), valuesPermitted: true }, Object.assign(new Error("Unavailable"), { status: 503 })]);
    const container = await render(context);
    for (let index = 0; index < 200 && !container.textContent?.includes("no longer refreshed"); index++) await new Promise(resolve => setTimeout(resolve, 0));
    await settle();

    const stale = container.querySelector("[data-testid='invocation-stale']")!;
    expect(stale.getAttribute("role")).toBe("status");
    expect(stale.textContent).toContain("It is no longer refreshed automatically.");
    expect(getJson).toHaveBeenCalledTimes(6);
    expect(container.querySelector("[data-testid='invocation-summary']")!.textContent).toContain("still running");
  });

  it("shows an invocation the caller may not inspect as not found", async () => {
    const container = await render(fakeContext([Object.assign(new Error("Not found"), { status: 404 })]).context);

    expect(container.querySelector("[data-testid='invocation-missing']")).not.toBeNull();
  });
});
