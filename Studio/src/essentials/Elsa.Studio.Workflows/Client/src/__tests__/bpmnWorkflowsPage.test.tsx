import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { BpmnWorkflowsPage } from "../bpmn/BpmnWorkflowsPage";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { BpmnInspection } from "../api/bpmnRuntime";

beforeEach(() => { clearApiCapabilityCache(); sessionStorage.clear(); });
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); sessionStorage.clear(); });
const pin = { artifactId: "artifact", definitionId: "model", definitionVersionId: "model-version", artifactVersion: "1.0.0", artifactHash: "model-hash" };
const publication = { sourceReferenceId: "source", sourceId: "authored-version", sourceVersion: "2.0.0", executable: pin, materialIncarnationId: "incarnation", profile: "foundation-direct-v1", displayName: "Review order", createdAt: "2026-11-01T10:00:00Z", publishedAt: "2026-11-01T10:00:00Z" };
const execution = { executionId: "run", revision: 1, status: "Running", protocol: { key: "elsa.bpmn", version: 1 }, executable: pin, materialIncarnationId: "incarnation", createdAt: "2026-11-01T10:01:00Z", updatedAt: "2026-11-01T10:01:00Z", result: null };
const work = { kind: "Human", elementId: "review", bindingId: "human:review", tokenId: "token", iterationId: null, invocationId: "invocation", attemptId: "attempt", workerExecutionId: null, dueAt: null };
const inspection = { execution, authority: { systemIdentity: "system", rootInitiator: "author", metadata: {} }, profile: "foundation-direct-v1",
  process: { processId: "process", name: "Review order", elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "review", elementType: "userTask", name: "Review order", bindingRef: "human:review" }, { elementId: "end", elementType: "endEvent" }], sequenceFlows: [{ flowId: "a", sourceRef: "start", targetRef: "review" }, { flowId: "b", sourceRef: "review", targetRef: "end" }] },
  source: { sourceReferenceId: "source", sourceKind: "BpmnModel", sourceId: "authored-version", sourceVersion: "2.0.0", layout: [{ nodeId: "review", x: 240, y: 0 }], activityPresentation: [], createdAt: publication.createdAt, publishedAt: publication.publishedAt, retiredAt: null }, work: [work], attempts: [], incidents: [], settlements: [] };

function fixture(view: "processes" | "runs" | "run" = "processes", runInspection: unknown = inspection, withClaims = true) {
  window.history.replaceState({}, "", view === "run" ? "/workflows/bpmn/runs/run?backend=selected" : `/workflows/bpmn${view === "runs" ? "/runs" : ""}?backend=selected`);
  const capabilities = { capabilities: [
    { id: "elsa.api.bpmn-catalog", contractVersion: "1", links: [{ rel: "bpmn-publications", href: "bpmn/publications" }, { rel: "bpmn-executions", href: "bpmn/executions" }] },
    { id: "elsa.api.bpmn-runtime", contractVersion: "1", links: [{ rel: "bpmn-admission", href: "bpmn/admission" }, { rel: "bpmn-start", href: "bpmn/executions" }, { rel: "bpmn-complete-human", href: "bpmn/executions/{id}/work/{invocation}/complete", templated: true }, { rel: "bpmn-retry-service", href: "bpmn/executions/{id}/retry-service", templated: true }, { rel: "bpmn-command", href: "bpmn/executions/{id}/commands/{request}", templated: true }] },
    { id: "elsa.api.bpmn-inspection", contractVersion: "1", links: [{ rel: "bpmn-inspection", href: "bpmn/executions/{id}/inspection", templated: true }] },
    ...(withClaims ? [{ id: "elsa.api.bpmn-claims", contractVersion: "1", links: [{ rel: "bpmn-claim-human", href: "bpmn/executions/{id}/work/{invocation}/claim", templated: true },
      { rel: "bpmn-complete-claimed-human", href: "bpmn/executions/{id}/work/{invocation}/complete-claimed", templated: true }] }] : [])
  ] };
  const getJson = vi.fn(async (path: string): Promise<unknown> => {
    if (path.endsWith("/capabilities")) return capabilities;
    if (path.includes("bpmn/publications")) return { items: [publication], nextContinuationToken: null };
    if (path.endsWith("bpmn/admission")) return { admitNewExecutions: true };
    if (path.includes("bpmn/executions?")) return { items: [execution], nextContinuationToken: null };
    if (path.endsWith("/inspection")) return structuredClone(runInspection);
    throw new Error(`Unexpected read ${path}`);
  });
  const postJson = vi.fn(async (path: string, body: { requestId: string }): Promise<unknown> => ({ executionId: "run", inputId: body.requestId, replay: false, pending: false, result: { status: "Accepted", revision: path.endsWith("/complete") ? 2 : 1, lifecycle: path.endsWith("/complete") ? "TerminalSettled" : "Running" } }));
  const session = { key: "connection", profile: { id: "backend", name: "Backend", baseUrl: "https://backend.example", environment: "Test", source: "personal" }, context: { baseUrl: "https://backend.example", http: { getJson, postJson } },
    capabilities, identity: { status: "authenticated", subject: "author", tenantId: "tenant", roles: [], permissions: [] }, disposed: false, pendingWrites: 0, refreshCapabilities: vi.fn().mockResolvedValue(capabilities) } as unknown as StudioBackendSession;
  const query = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container); const navigate = vi.fn();
  const render = (selected = view, selectedSession = session) => flushSync(() => root.render(<QueryClientProvider client={query}><BpmnWorkflowsPage session={selectedSession} view={selected} navigate={navigate} /></QueryClientProvider>));
  render();
  const button = (label: string) => [...container.querySelectorAll<HTMLButtonElement>("button")].find(node => node.textContent?.trim() === label)!;
  const click = (label: string) => flushSync(() => button(label).click());
  return { session, getJson, postJson, query, container, navigate, render, button, click, dispose: () => { flushSync(() => root.unmount()); query.clear(); container.remove(); } };
}

it("discovers a published process, starts it explicitly and opens its durable run", async () => {
  const view = fixture();
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("Review order"));
    expect(view.container.textContent).toContain("2.0.0"); view.click("Start process");
    await vi.waitFor(() => expect(view.button("Open run")).toBeTruthy()); view.click("Open run");
    expect(view.postJson).toHaveBeenCalledTimes(1);
    expect(view.navigate).toHaveBeenCalledWith("/workflows/bpmn/runs/run?backend=selected");
  } finally { view.dispose(); }
});

it("shows the persisted human wait and completes the exact invocation and attempt", async () => {
  const view = fixture("run");
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("Waiting for a person")); view.click("Complete work");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(1));
    expect(view.postJson).toHaveBeenCalledWith("/bpmn/executions/run/work/invocation/complete", { requestId: expect.any(String), attemptId: "attempt" }, { retryOnUnauthorized: false, signal: expect.any(AbortSignal) });
    expect(view.container.textContent).not.toContain("No activity bound");
  } finally { view.dispose(); }
});

it("leaves an empty filtered page navigable and fetches only when Load more is chosen", async () => {
  const view = fixture(); const normal = view.getJson.getMockImplementation()!;
  view.getJson.mockImplementation(async path => path.includes("bpmn/publications")
    ? path.includes("continuationToken") ? { items: [publication], nextContinuationToken: null } : { items: [], nextContinuationToken: "protected-next" }
    : normal(path));
  try {
    await vi.waitFor(() => expect(view.button("Load more")).toBeTruthy());
    expect(view.getJson.mock.calls.filter(([path]) => path.includes("bpmn/publications"))).toHaveLength(1);
    view.click("Load more"); await vi.waitFor(() => expect(view.container.textContent).toContain("Review order"));
    expect(view.getJson.mock.calls.filter(([path]) => path.includes("bpmn/publications"))).toHaveLength(2);
  } finally { view.dispose(); }
});

it("recovers a lost start response with the same request after reconnecting", async () => {
  const view = fixture(); view.postJson.mockRejectedValueOnce(new TypeError("Response lost"));
  try {
    await vi.waitFor(() => expect(view.button("Start process")).toBeTruthy()); view.click("Start process");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Response lost"));
    const first = view.postJson.mock.calls[0]; view.render("processes", { ...view.session, key: "fresh" });
    await vi.waitFor(() => expect(view.button("Resume start")).toBeTruthy()); expect(view.postJson).toHaveBeenCalledTimes(1);
    view.click("Resume start"); await vi.waitFor(() => expect(view.button("Open run")).toBeTruthy());
    expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(first.slice(0, 2));
  } finally { view.dispose(); }
});

it("keeps pending admission distinct from a running execution and checks the retained outcome", async () => {
  const view = fixture(); view.postJson.mockImplementationOnce(async (_path, body) => ({ executionId: "pending-run", inputId: body.requestId, pending: true, replay: false, result: null }));
  const normal = view.getJson.getMockImplementation()!;
  view.getJson.mockImplementation(async path => path.includes("/commands/")
    ? { executionId: "pending-run", inputId: view.postJson.mock.calls[0][1].requestId, pending: false, replay: false, result: { status: "Accepted", lifecycle: "Running" } } : normal(path));
  try {
    await vi.waitFor(() => expect(view.button("Start process")).toBeTruthy()); view.click("Start process");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Pending:"));
    expect(view.button("Open run")).toBeUndefined(); expect(view.container.textContent).not.toContain("Running");
    view.click("Check outcome"); await vi.waitFor(() => expect(view.button("Open run")).toBeTruthy());
    expect(view.postJson).toHaveBeenCalledTimes(1); view.click("Open run");
    expect(view.navigate).toHaveBeenCalledWith("/workflows/bpmn/runs/pending-run?backend=selected");
  } finally { view.dispose(); }
});

it("replays an uncertain completion even when the server now reports no outstanding work", async () => {
  const view = fixture("run"); view.postJson.mockRejectedValueOnce(new TypeError("Completion response lost"));
  try {
    await vi.waitFor(() => expect(view.button("Complete work")).toBeTruthy()); view.click("Complete work");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Completion response lost"));
    const first = view.postJson.mock.calls[0]; const normal = view.getJson.getMockImplementation()!;
    view.getJson.mockImplementation(async path => path.endsWith("/inspection") ? { ...structuredClone(inspection), work: [], execution: { ...execution, revision: 2, status: "TerminalSettled", result: { status: "Completed", outcome: "Done" } } } : normal(path));
    view.render("run", { ...view.session, key: "fresh" });
    await vi.waitFor(() => expect(view.container.textContent).toContain("No outstanding work."));
    expect(view.button("Complete work")).toBeUndefined(); view.click("Resume completion");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(2));
    expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(first.slice(0, 2));
    await vi.waitFor(() => expect(view.container.textContent).toContain("Last completion: accepted"));
  } finally { view.dispose(); }
});

it("does not prepare another request for a repeated click on the same work", async () => {
  const view = fixture("run");
  try {
    await vi.waitFor(() => expect(view.button("Complete work")).toBeTruthy()); view.click("Complete work");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Last completion: accepted"));
    await vi.waitFor(() => expect(view.button("Complete work").disabled).toBe(false)); view.click("Complete work");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(2));
    expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(view.postJson.mock.calls[0].slice(0, 2));
  } finally { view.dispose(); }
});

it("completes accepted human work without a Design or admission-query dependency", async () => {
  const view = fixture("run");
  try {
    await vi.waitFor(() => expect(view.button("Complete work")).toBeTruthy()); view.click("Complete work");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(1));
    expect(view.getJson.mock.calls.every(([path]) => !path.includes("design/") && !path.includes("bpmn/admission"))).toBe(true);
  } finally { view.dispose(); }
});

it("disables new starts when admission is off", async () => {
  const view = fixture(); const normal = view.getJson.getMockImplementation()!;
  view.getJson.mockImplementation(async path => path.endsWith("bpmn/admission") ? { admitNewExecutions: false } : normal(path));
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("New starts are disabled"));
    expect(view.button("Start process").disabled).toBe(true); view.click("Start process"); expect(view.postJson).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("refuses a write when browser storage cannot retain its request identity", async () => {
  const view = fixture(); const fail = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage unavailable"); });
  try {
    await vi.waitFor(() => expect(view.button("Start process")).toBeTruthy()); view.click("Start process");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Storage unavailable"));
    expect(view.postJson).not.toHaveBeenCalled(); expect(view.container.textContent).not.toContain("identity is retained");
  } finally { fail.mockRestore(); view.dispose(); }
});

const claimInspection = (claim: { claimId: string; claimantId: string; claimedAt: string } | null) =>
  ({ ...structuredClone(inspection), profile: "foundation-direct-claims-v1", work: [{ ...work, claimRequired: true, claim }] });

it("offers only a claim for unclaimed claim-required work and never sends legacy completion", async () => {
  const view = fixture("run", claimInspection(null));
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("Unclaimed. Claim this work before completing it."));
    expect(view.button("Complete work")).toBeUndefined(); view.click("Claim work");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(1));
    expect(view.postJson).toHaveBeenCalledWith("/bpmn/executions/run/work/invocation/claim", { requestId: expect.any(String), attemptId: "attempt" },
      { retryOnUnauthorized: false, signal: expect.any(AbortSignal) });
    await vi.waitFor(() => expect(view.container.textContent).toContain("Last claim: accepted."));
  } finally { view.dispose(); }
});

it("resumes an uncertain claim with its original request after reload instead of claiming again", async () => {
  const view = fixture("run", claimInspection(null)); view.postJson.mockRejectedValueOnce(new TypeError("Claim response lost"));
  try {
    await vi.waitFor(() => expect(view.button("Claim work")).toBeTruthy()); view.click("Claim work");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Claim response lost"));
    const first = view.postJson.mock.calls[0]; const normal = view.getJson.getMockImplementation()!;
    view.getJson.mockImplementation(async path => path.endsWith("/inspection")
      ? claimInspection({ claimId: "claim:a", claimantId: "author", claimedAt: "2026-11-01T10:02:00Z" }) : normal(path));
    view.render("run", { ...view.session, key: "fresh" });
    await vi.waitFor(() => expect(view.container.textContent).toContain("Claimed by you."));
    // The server already reports this caller as owner; the uncertain claim is still recovered by its own retained request.
    expect(view.button("Complete work").disabled).toBe(false); view.click("Resume claim");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(2));
    expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(first.slice(0, 2));
    await vi.waitFor(() => expect(view.container.textContent).toContain("Last claim: accepted"));
  } finally { view.dispose(); }
});

it("lets only the current owner complete claimed work with its exact claim", async () => {
  const view = fixture("run", claimInspection({ claimId: "claim:a", claimantId: "author", claimedAt: "2026-11-01T10:02:00Z" }));
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("Claimed by you."));
    expect(view.button("Claim work")).toBeUndefined(); view.click("Complete work");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(1));
    expect(view.postJson).toHaveBeenCalledWith("/bpmn/executions/run/work/invocation/complete-claimed",
      { requestId: expect.any(String), attemptId: "attempt", claimId: "claim:a" }, { retryOnUnauthorized: false, signal: expect.any(AbortSignal) });
  } finally { view.dispose(); }
});

it("shows another person's claim and refuses completion without sending it", async () => {
  const view = fixture("run", claimInspection({ claimId: "claim:b", claimantId: "bob", claimedAt: "2026-11-01T10:02:00Z" }));
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("Claimed by bob."));
    expect(view.button("Complete work").disabled).toBe(true);
    expect(view.button("Complete work").title).toBe("Only the current owner can complete this work.");
    view.click("Complete work"); expect(view.postJson).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("does not offer claims when the backend advertises only unclaimed completion", async () => {
  const view = fixture("run", claimInspection(null), false);
  try {
    await vi.waitFor(() => expect(view.button("Claim work")).toBeTruthy());
    expect(view.button("Claim work").disabled).toBe(true);
    expect(view.button("Claim work").title).toBe("This backend does not offer human work claims.");
    view.click("Claim work"); expect(view.postJson).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("does not offer legacy unclaimed completion for an unknown profile", async () => {
  const view = fixture("run"); const normal = view.getJson.getMockImplementation()!;
  view.getJson.mockImplementation(async path => path.endsWith("/inspection") ? { ...structuredClone(inspection), profile: "future-claims-profile" } : normal(path));
  try {
    await vi.waitFor(() => expect(view.button("Complete work")).toBeTruthy()); expect(view.button("Complete work").disabled).toBe(true);
    view.click("Complete work"); expect(view.postJson).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it.each(["subject", "tenant", "backend"])("discards a late page and its cursor after a %s switch", async change => {
  const view = fixture(); const normal = view.getJson.getMockImplementation()!; let resolve!: (value: unknown) => void;
  const late = new Promise<unknown>(done => { resolve = done; });
  view.getJson.mockImplementation(async path => path.includes("bpmn/publications") ? late : normal(path));
  try {
    await vi.waitFor(() => expect(view.getJson.mock.calls.some(([path]) => path.includes("bpmn/publications"))).toBe(true));
    const nextGet = vi.fn(async (path: string) => path.includes("bpmn/publications") ? { items: [{ ...publication, sourceReferenceId: "other-source", displayName: "Other process" }], nextContinuationToken: null } : normal(path));
    const next = { ...view.session, context: { ...view.session.context, http: { ...view.session.context.http, getJson: nextGet } },
      identity: { ...view.session.identity, subject: change === "subject" ? "another" : view.session.identity.subject, tenantId: change === "tenant" ? "another" : view.session.identity.tenantId },
      profile: { ...view.session.profile, baseUrl: change === "backend" ? "https://other.example" : view.session.profile.baseUrl } } as StudioBackendSession;
    if (change === "backend") Object.assign(next.context, { baseUrl: "https://other.example" });
    view.render("processes", next); await vi.waitFor(() => expect(view.container.textContent).toContain("Other process"));
    resolve({ items: [publication], nextContinuationToken: "old-private-cursor" });
    await new Promise(done => setTimeout(done, 20));
    expect(view.container.textContent).not.toContain("Review order"); expect(view.button("Load more")).toBeUndefined();
    expect(nextGet.mock.calls.filter(([path]) => path.includes("bpmn/publications"))).toHaveLength(1);
    expect(nextGet.mock.calls.find(([path]) => path.includes("bpmn/publications"))![0]).not.toContain("continuationToken");
  } finally { view.dispose(); }
});

it("hides previously cached run details after access is refused", async () => {
  const view = fixture("run");
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("Waiting for a person"));
    view.getJson.mockRejectedValueOnce(Object.assign(new Error("The BPMN resource is not available."), { status: 404 }));
    view.click("Refresh run"); await vi.waitFor(() => expect(view.container.textContent).toContain("The BPMN resource is not available."));
    expect(view.container.textContent).not.toContain("Review order"); expect(view.button("Complete work")).toBeUndefined();
  } finally { view.dispose(); }
});

it("requires an explicit Start another run to replace a confirmed start identity", async () => {
  const view = fixture();
  try {
    await vi.waitFor(() => expect(view.button("Start process")).toBeTruthy()); view.click("Start process");
    await vi.waitFor(() => expect(view.button("Start another run")).toBeTruthy()); const first = view.postJson.mock.calls[0][1].requestId;
    view.click("Start another run"); await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(2));
    expect(view.postJson.mock.calls[1][1].requestId).not.toBe(first);
  } finally { view.dispose(); }
});

it("does not replace uncertain work with a newly observed invocation", async () => {
  const view = fixture("run"); view.postJson.mockRejectedValueOnce(new TypeError("Response lost"));
  try {
    await vi.waitFor(() => expect(view.button("Complete work")).toBeTruthy()); view.click("Complete work");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Response lost")); const original = view.postJson.mock.calls[0];
    const normal = view.getJson.getMockImplementation()!;
    view.getJson.mockImplementation(async path => path.endsWith("/inspection") ? { ...structuredClone(inspection), work: [{ ...work, invocationId: "new-invocation", attemptId: "new-attempt" }] } : normal(path));
    view.render("run", { ...view.session, key: "fresh" });
    await vi.waitFor(() => expect(view.button("Complete work")).toBeTruthy()); expect(view.button("Complete work").disabled).toBe(true);
    view.click("Resume completion"); await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(2));
    expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(original.slice(0, 2));
  } finally { view.dispose(); }
});

it("ignores an old run inspection after navigation to another execution", async () => {
  const view = fixture("run"); const normal = view.getJson.getMockImplementation()!; let resolve!: (value: unknown) => void;
  const late = new Promise<unknown>(done => { resolve = done; });
  view.getJson.mockImplementation(async path => path.includes("/run/inspection") ? late : path.includes("/other/inspection")
    ? { ...structuredClone(inspection), process: { ...inspection.process, name: "Other process" }, execution: { ...execution, executionId: "other" } } : normal(path));
  try {
    await vi.waitFor(() => expect(view.getJson.mock.calls.some(([path]) => path.endsWith("/run/inspection"))).toBe(true));
    flushSync(() => { window.history.pushState({}, "", "/workflows/bpmn/runs/other?backend=selected"); window.dispatchEvent(new PopStateEvent("popstate")); });
    await vi.waitFor(() => expect(view.container.querySelector(".wf-bpmn-run-heading h3")?.textContent).toBe("Other process"));
    resolve(structuredClone(inspection)); await new Promise(done => setTimeout(done, 20));
    expect(view.container.querySelector(".wf-bpmn-run-heading h3")?.textContent).toBe("Other process");
  } finally { view.dispose(); }
});

it("does not apply a late completion response to another selected run", async () => {
  const view = fixture("run"); let resolve!: (value: unknown) => void;
  view.postJson.mockReturnValueOnce(new Promise(done => { resolve = done; }));
  try {
    await vi.waitFor(() => expect(view.button("Complete work")).toBeTruthy()); view.click("Complete work");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(1)); const request = view.postJson.mock.calls[0][1].requestId;
    const normal = view.getJson.getMockImplementation()!;
    view.getJson.mockImplementation(async path => path.includes("/other/inspection") ? { ...structuredClone(inspection), process: { ...inspection.process, name: "Other process" }, execution: { ...execution, executionId: "other" } } : normal(path));
    flushSync(() => { window.history.pushState({}, "", "/workflows/bpmn/runs/other?backend=selected"); window.dispatchEvent(new PopStateEvent("popstate")); });
    await vi.waitFor(() => expect(view.container.querySelector(".wf-bpmn-run-heading h3")?.textContent).toBe("Other process"));
    resolve({ executionId: "run", inputId: request, pending: false, replay: false, result: { status: "Accepted" } });
    await new Promise(done => setTimeout(done, 20));
    expect(view.container.textContent).not.toContain("Last completion");
    expect(view.container.querySelector(".wf-bpmn-run-heading h3")?.textContent).toBe("Other process");
    const retained = [...Array(sessionStorage.length)].map((_, index) => JSON.parse(sessionStorage.getItem(sessionStorage.key(index)!)!));
    expect(retained.some(item => item.operation.executionId === "run" && !item.outcome)).toBe(true);
  } finally { view.dispose(); }
});

it("keeps a corrupt retained command intact and refuses a replacement", async () => {
  const view = fixture(); view.postJson.mockRejectedValueOnce(new TypeError("Response lost"));
  try {
    await vi.waitFor(() => expect(view.button("Start process")).toBeTruthy()); view.click("Start process");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Response lost"));
    const key = sessionStorage.key(0)!; const receipt = JSON.parse(sessionStorage.getItem(key)!); receipt.formatVersion = 99;
    const raw = JSON.stringify(receipt); sessionStorage.setItem(key, raw);
    view.render("processes", { ...view.session, key: "fresh" });
    await vi.waitFor(() => expect(view.container.textContent).toContain("cannot be restored safely"));
    expect(view.button("Start process").disabled).toBe(true); expect(sessionStorage.getItem(key)).toBe(raw); expect(view.postJson).toHaveBeenCalledTimes(1);
  } finally { view.dispose(); }
});

it("refuses to substitute a different execution in a command response", async () => {
  const view = fixture("run"); view.postJson.mockImplementationOnce(async (_path, body) => ({ executionId: "another-run", inputId: body.requestId, pending: false, replay: false, result: { status: "Accepted" } }));
  try {
    await vi.waitFor(() => expect(view.button("Complete work")).toBeTruthy()); view.click("Complete work");
    await vi.waitFor(() => expect(view.container.textContent).toContain("does not match the retained BPMN request"));
    expect(view.container.textContent).not.toContain("Last completion: accepted");
  } finally { view.dispose(); }
});

it("does not draw unsupported notation as an ordinary task", async () => {
  const view = fixture("run"); const normal = view.getJson.getMockImplementation()!;
  view.getJson.mockImplementation(async path => path.endsWith("/inspection") ? { ...structuredClone(inspection), process: { ...inspection.process, elements: [...inspection.process.elements, { elementId: "boundary", elementType: "boundaryEvent" }] } } : normal(path));
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("notation the diagram cannot display"));
    expect(view.container.querySelector(".react-flow__node[data-id='boundary']")).toBeNull(); expect(view.button("Complete work")).toBeTruthy();
  } finally { view.dispose(); }
});

function serviceInspection(): BpmnInspection {
  return { ...structuredClone(inspection), profile: "foundation-native-work-v1",
    execution: { ...execution, status: "WaitingIncident" },
    process: { processId: "etl-process", name: "Import orders", elements: [{ elementId: "import", elementType: "serviceTask", name: "Import orders", bindingRef: "service:import" }], sequenceFlows: [] },
    work: [{ ...work, kind: "Native", elementId: "import", bindingId: "service:import", workerExecutionId: "native/failed" }],
    attempts: [{ bindingId: "service:import", invocationId: "invocation", attemptId: "attempt", workerExecutionId: "native/failed", workerExecutable: { ...pin, artifactId: "worker", artifactHash: "worker-hash" }, status: "Faulted", attemptNumber: 1, outcomeKey: null, faultCode: "etl-partition-failed" }],
    incidents: [{ incidentId: "incident", elementId: "import", bindingId: "service:import", invocationId: "invocation", failedAttemptId: "attempt", workerExecutionId: "native/failed", faultCode: "etl-partition-failed", failedAt: "2026-11-01T10:02:00Z", attemptNumber: 1, maxAttempts: 2, status: "Open", retryAttemptId: null }] };
}

it("shows the held service fault, exact pinned native attempt and explicit retry budget", async () => {
  const view = fixture("run", serviceInspection());
  try {
    await vi.waitFor(() => expect(view.button("Retry service")).toBeTruthy());
    expect(view.container.textContent).toContain("Needs attention");
    expect(view.container.textContent).toContain("1 of 2 attempts used");
    expect(view.container.textContent).toContain("etl-partition-failed");
    expect(view.container.querySelector<HTMLAnchorElement>('a[data-bpmn-native-attempt]')?.getAttribute("href"))
      .toBe("/workflows/instances/native%2Ffailed?backend=backend");
    expect(view.container.textContent).toContain("worker-hash");
    view.click("Retry service");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(1));
    expect(view.postJson).toHaveBeenCalledWith("/bpmn/executions/run/retry-service", {
      requestId: expect.any(String), incidentId: "incident", invocationId: "invocation", failedAttemptId: "attempt"
    }, { retryOnUnauthorized: false, signal: expect.any(AbortSignal) });
  } finally { view.dispose(); }
});

it("recovers an uncertain retry after reconnecting even after the incident has been retired", async () => {
  const view = fixture("run", serviceInspection()); view.postJson.mockRejectedValueOnce(new TypeError("Retry response lost"));
  try {
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Retry response lost")); const first = view.postJson.mock.calls[0];
    const settled = serviceInspection(); settled.execution.status = "TerminalSettled"; settled.execution.result = { status: "Completed" }; settled.work = [];
    settled.incidents[0] = { ...settled.incidents[0], status: "Retried", retryAttemptId: "replacement" };
    settled.attempts.push({ ...settled.attempts[0], attemptId: "replacement", workerExecutionId: "native/replacement", attemptNumber: 2, status: "Completed", faultCode: null });
    const normal = view.getJson.getMockImplementation()!;
    view.getJson.mockImplementation(async path => path.endsWith("/inspection") ? settled : normal(path));
    view.render("run", { ...view.session, key: "fresh" });
    await vi.waitFor(() => expect(view.container.textContent).toContain("No outstanding work."));
    expect(view.postJson).toHaveBeenCalledTimes(1); expect(view.button("Retry service")).toBeUndefined();
    expect(view.container.textContent).toContain("etl-partition-failed"); expect(view.container.querySelectorAll('a[data-bpmn-native-attempt]')).toHaveLength(2);
    view.click("Resume retry"); await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(2));
    expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(first.slice(0, 2));
    await vi.waitFor(() => expect(view.container.textContent).toContain("Last retry: accepted"));
  } finally { view.dispose(); }
});

it("checks a lost retry response without sending another command", async () => {
  const view = fixture("run", serviceInspection()); view.postJson.mockRejectedValueOnce(new TypeError("Retry response lost"));
  const normal = view.getJson.getMockImplementation()!;
  view.getJson.mockImplementation(async path => path.includes("/commands/") ? { executionId: "run", inputId: view.postJson.mock.calls[0][1].requestId, pending: false, replay: true, result: { status: "Accepted" } } : normal(path));
  try {
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Retry response lost")); view.click("Check outcome");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Last retry: accepted (replayed)"));
    expect(view.postJson).toHaveBeenCalledTimes(1);
    expect(view.getJson.mock.calls.some(([path]) => path === `/bpmn/executions/run/commands/${view.postJson.mock.calls[0][1].requestId}`)).toBe(true);
  } finally { view.dispose(); }
});

it("keeps pending retry and newly observed incidents distinct without replacing the request", async () => {
  const view = fixture("run", serviceInspection());
  view.postJson.mockImplementationOnce(async (_path, body) => ({ executionId: "run", inputId: body.requestId, pending: true, replay: false, result: null }));
  try {
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Pending:"));
    const newer = serviceInspection(); newer.incidents[0].incidentId = "new-incident";
    const normal = view.getJson.getMockImplementation()!;
    view.getJson.mockImplementation(async path => path.endsWith("/inspection") ? newer : normal(path));
    view.render("run", { ...view.session, key: "fresh" });
    await vi.waitFor(() => expect(view.button("Retry service")).toBeTruthy());
    expect(view.button("Retry service").disabled).toBe(true); expect(view.postJson).toHaveBeenCalledTimes(1);
    view.click("Resume retry"); await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(2));
    expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(view.postJson.mock.calls[0].slice(0, 2));
  } finally { view.dispose(); }
});

it("reuses the exact retry identity for another click on the same stale incident", async () => {
  const view = fixture("run", serviceInspection());
  try {
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Last retry: accepted"));
    await vi.waitFor(() => expect(view.button("Retry service").disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.postJson).toHaveBeenCalledTimes(2));
    expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(view.postJson.mock.calls[0].slice(0, 2));
    expect(view.getJson.mock.calls.every(([path]) => !path.includes("design/") && !path.includes("bpmn/admission"))).toBe(true);
  } finally { view.dispose(); }
});

it.each(["exhausted", "running", "terminal", "wrong-work", "missing-attempt", "foreign-attempt", "future-profile"])("refuses a new service retry for %s inspection state", async change => {
  const value = serviceInspection();
  if (change === "exhausted") value.incidents[0].maxAttempts = 1;
  if (change === "running") value.execution.status = "Running";
  if (change === "terminal") value.execution.status = "TerminalPendingSettlement";
  if (change === "wrong-work") value.work[0].attemptId = "other-attempt";
  if (change === "missing-attempt") value.attempts = [];
  if (change === "foreign-attempt") value.attempts[0].workerExecutionId = "foreign-worker";
  if (change === "future-profile") value.profile = "future-native-profile";
  const view = fixture("run", value);
  try {
    await vi.waitFor(() => expect(view.button("Retry service")).toBeTruthy()); expect(view.button("Retry service").disabled).toBe(true);
    view.click("Retry service"); expect(view.postJson).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("refuses a fresh retry if its request identity cannot be saved", async () => {
  const view = fixture("run", serviceInspection()); const fail = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage unavailable"); });
  try {
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Storage unavailable")); expect(view.postJson).not.toHaveBeenCalled();
  } finally { fail.mockRestore(); view.dispose(); }
});

it.each(["incidentId", "invocationId", "failedAttemptId"])("preserves a retry receipt with corrupt %s and refuses a new command", async field => {
  const view = fixture("run", serviceInspection()); view.postJson.mockRejectedValueOnce(new TypeError("Response lost"));
  try {
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Response lost"));
    const key = sessionStorage.key(0)!; const receipt = JSON.parse(sessionStorage.getItem(key)!); receipt.operation[field] = "";
    const raw = JSON.stringify(receipt); sessionStorage.setItem(key, raw); view.render("run", { ...view.session, key: "fresh" });
    await vi.waitFor(() => expect(view.container.textContent).toContain("cannot be restored safely"));
    expect(view.button("Retry service").disabled).toBe(true); expect(sessionStorage.getItem(key)).toBe(raw); expect(view.postJson).toHaveBeenCalledTimes(1);
  } finally { view.dispose(); }
});

it("refuses a retry response from another execution while preserving the original request", async () => {
  const view = fixture("run", serviceInspection());
  view.postJson.mockImplementationOnce(async (_path, body) => ({ executionId: "other-run", inputId: body.requestId, pending: false, replay: false, result: { status: "Accepted" } }));
  try {
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.container.textContent).toContain("does not match the retained BPMN request"));
    expect(view.container.textContent).not.toContain("Last retry: accepted"); expect(view.button("Resume retry")).toBeTruthy();
  } finally { view.dispose(); }
});

it.each(["subject", "tenant", "backend"])("keeps retained retries out of another %s scope", async change => {
  const view = fixture("run", serviceInspection()); view.postJson.mockRejectedValueOnce(new TypeError("Response lost"));
  try {
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false)); view.click("Retry service");
    await vi.waitFor(() => expect(view.container.textContent).toContain("Response lost"));
    const next = { ...view.session, identity: { ...view.session.identity }, profile: { ...view.session.profile }, context: { ...view.session.context } };
    if (change === "subject") next.identity.subject = "other-subject";
    if (change === "tenant") next.identity.tenantId = "other-tenant";
    if (change === "backend") { next.profile.id = "other-backend"; next.profile.baseUrl = "https://other.example"; Object.assign(next.context, { baseUrl: "https://other.example" }); }
    view.render("run", next);
    await vi.waitFor(() => expect(view.button("Retry service")?.disabled).toBe(false));
    expect(view.button("Resume retry")).toBeUndefined(); expect(view.postJson).toHaveBeenCalledTimes(1); expect(sessionStorage.length).toBe(1);
  } finally { view.dispose(); }
});

it("does not offer retry without its backend relation", async () => {
  const view = fixture("run", serviceInspection());
  view.session.capabilities!.capabilities.find(item => item.id === "elsa.api.bpmn-runtime")!.links = [];
  try {
    await vi.waitFor(() => expect(view.button("Retry service")).toBeTruthy()); expect(view.button("Retry service").disabled).toBe(true);
    view.click("Retry service"); expect(view.postJson).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it.each(["disposed", "subject", "tenant", "backend"])("retires a native-attempt link after its %s scope changes", async change => {
  const view = fixture("run", serviceInspection());
  try {
    await vi.waitFor(() => expect(view.container.querySelector('a[data-bpmn-native-attempt]')).toBeTruthy());
    if (change === "disposed") Object.assign(view.session, { disposed: true });
    if (change === "subject") view.session.identity.subject = "other";
    if (change === "tenant") view.session.identity.tenantId = "other";
    if (change === "backend") view.session.profile.id = "other";
    const click = new MouseEvent("click", { bubbles: true, cancelable: true });
    expect(view.container.querySelector('a[data-bpmn-native-attempt]')!.dispatchEvent(click)).toBe(false);
  } finally { view.dispose(); }
});
