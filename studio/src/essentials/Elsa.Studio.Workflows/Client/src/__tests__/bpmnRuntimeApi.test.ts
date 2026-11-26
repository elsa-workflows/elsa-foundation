import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioBackendSession, StudioApiCapabilityDocument } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache, capabilityIds } from "../api/capabilities";
import { createBpmnRuntimeClient, type BpmnOperation } from "../api/bpmnRuntime";

afterEach(clearApiCapabilityCache);

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(value => { resolve = value; });
  return { promise, resolve };
}
function fixture(options: { subject?: string; tenantId?: string; key?: string; baseUrl?: string; document?: StudioApiCapabilityDocument } = {}) {
  const relations: Record<string, Record<string, string>> = {
    [capabilityIds.bpmnCatalog]: { "bpmn-publications": "bpmn/publications", "bpmn-executions": "bpmn/executions" },
    [capabilityIds.bpmnInspection]: { "bpmn-inspection": "bpmn/executions/{id}/inspection" },
    [capabilityIds.bpmnPublishing]: { "bpmn-authored-publish": "interchange/bpmn/versions/{versionId}/publish-direct" },
    [capabilityIds.bpmnRuntime]: {
      "bpmn-admission": "bpmn/admission", "bpmn-start": "bpmn/executions", "bpmn-execution": "bpmn/executions/{id}",
      "bpmn-work": "bpmn/executions/{id}/work", "bpmn-complete-human": "bpmn/executions/{id}/work/{invocation}/complete",
      "bpmn-cancel": "bpmn/executions/{id}/cancel", "bpmn-incidents": "bpmn/executions/{id}/incidents",
      "bpmn-retry-service": "bpmn/executions/{id}/retry-service", "bpmn-command": "bpmn/executions/{id}/commands/{request}"
    },
    [capabilityIds.bpmnClaims]: {
      "bpmn-claim-human": "bpmn/executions/{id}/work/{invocation}/claim",
      "bpmn-complete-claimed-human": "bpmn/executions/{id}/work/{invocation}/complete-claimed"
    }
  };
  const document = options.document ?? { capabilities: Object.entries(relations).map(([id, links]) => ({ id, contractVersion: "1", links: Object.entries(links).map(([rel, href]) => ({ rel, href })) })) };
  const getJson = vi.fn(async (url: string, init?: unknown) => { void init; return url.endsWith("/capabilities") ? document : { items: [], nextContinuationToken: "protected-next" }; });
  const pending = { executionId: "execution", inputId: "operation", pending: true, replay: false, result: null };
  const postJson = vi.fn().mockResolvedValue(pending);
  const session = {
    key: options.key ?? "connection-1", profile: { id: "backend", name: "Backend", baseUrl: options.baseUrl ?? "https://server.example/shell-a", environment: "Development", source: "personal" },
    identity: { status: "authenticated", subject: options.subject ?? "author", tenantId: options.tenantId ?? "tenant", roles: [], permissions: [] },
    context: { baseUrl: options.baseUrl ?? "https://server.example/shell-a", http: { getJson, postJson } }, disposed: false,
    capabilities: document, pendingWrites: 0, refreshCapabilities: vi.fn().mockResolvedValue(document)
  };
  return { session, client: createBpmnRuntimeClient(session as unknown as StudioBackendSession), getJson, postJson, pending };
}

describe("Direct BPMN scoped runtime client", () => {
  it("uses advertised shell routes and leaves an empty authorized page with continuation for the user", async () => {
    const f = fixture();
    const page = await f.client.executions({ limit: 2, artifactId: "pin:/one", continuationToken: "cursor+/=" });
    expect(page).toEqual({ items: [], nextContinuationToken: "protected-next" });
    expect(f.getJson).toHaveBeenCalledTimes(2);
    expect(f.getJson).toHaveBeenLastCalledWith("/shell-a/bpmn/executions?limit=2&continuationToken=cursor%2B%2F%3D&artifactId=pin%3A%2Fone", undefined);
  });

  it("refuses unavailable optional capabilities before sending any domain command", async () => {
    const f = fixture({ document: { capabilities: [] } });
    const operation = f.client.prepare({ kind: "publish", versionId: "version" }, "publish-1");
    await expect(f.client.execute(operation)).rejects.toMatchObject({ name: "ApiCapabilityUnavailableError", capabilityId: capabilityIds.bpmnPublishing });
    expect(f.postJson).not.toHaveBeenCalled(); expect(f.getJson).toHaveBeenCalledTimes(1);
  });

  it("refuses a newer contract version instead of falling back to native runtime routes", async () => {
    const f = fixture({ document: { capabilities: [{ id: capabilityIds.bpmnRuntime, contractVersion: "99", links: [{ rel: "bpmn-start", href: "bpmn/executions" }] }] } });
    await expect(f.client.execute(f.client.prepare({ kind: "start", sourceReferenceId: "source" }, "start-1")))
      .rejects.toMatchObject({ name: "ApiCapabilityVersionMismatchError" });
    expect(f.postJson).not.toHaveBeenCalled();
  });

  it.each(["subject", "tenant", "backend", "context", "retired"])("invalidates requests when the %s binding changes", async change => {
    const f = fixture();
    if (change === "subject") f.session.identity.subject = "another";
    if (change === "tenant") f.session.identity.tenantId = "another";
    if (change === "backend") f.session.profile.baseUrl = "https://another.example";
    if (change === "context") f.session.context.baseUrl = "https://another.example";
    if (change === "retired") f.session.disposed = true;
    await expect(f.client.inspection("execution")).rejects.toMatchObject({ name: "AbortError" });
    expect(f.getJson).not.toHaveBeenCalled(); expect(f.postJson).not.toHaveBeenCalled();
  });

  it("does not send a command after the session retires during capability discovery", async () => {
    const f = fixture(); const discovery = deferred<StudioApiCapabilityDocument>();
    f.getJson.mockImplementationOnce(() => discovery.promise as never);
    const operation = f.client.prepare({ kind: "publish", versionId: "version" }, "publish-1");
    const result = f.client.execute(operation);
    f.session.disposed = true; discovery.resolve(f.session.capabilities);
    await expect(result).rejects.toMatchObject({ name: "AbortError" }); expect(f.postJson).not.toHaveBeenCalled();
  });

  it("rejects a late inspection result after an identity or backend switch", async () => {
    const f = fixture(); await f.client.publications();
    const late = deferred<unknown>(); f.getJson.mockImplementationOnce(() => late.promise as never);
    const result = f.client.inspection("execution");
    await vi.waitFor(() => expect(f.getJson).toHaveBeenCalledTimes(3));
    f.session.disposed = true; late.resolve({ execution: { executionId: "old-run" } });
    await expect(result).rejects.toMatchObject({ name: "AbortError" });
  });

  it("retains a write identity after response loss and retries only on an explicit call", async () => {
    const f = fixture(); const operation = f.client.prepare({ kind: "complete-human", executionId: "run", invocationId: "work", attemptId: "attempt" }, "complete-1");
    f.postJson.mockRejectedValueOnce(new TypeError("Response lost"));
    await expect(f.client.execute(operation)).rejects.toThrow("Response lost"); expect(f.postJson).toHaveBeenCalledTimes(1);
    const retained = JSON.parse(JSON.stringify(operation)) as BpmnOperation;
    await expect(f.client.execute(retained)).resolves.toEqual(f.pending);
    expect(f.postJson).toHaveBeenCalledTimes(2); expect(f.postJson.mock.calls[0]).toEqual(f.postJson.mock.calls[1]);
    expect(f.postJson).toHaveBeenLastCalledWith("/shell-a/bpmn/executions/run/work/work/complete", { requestId: "complete-1", attemptId: "attempt" }, { retryOnUnauthorized: false });
  });

  it("replays a retained operation in a fresh matching connection while isolating the query cache", async () => {
    const first = fixture(); const operation = first.client.prepare({ kind: "start", sourceReferenceId: "source" }, "start-1");
    first.session.disposed = true;
    const fresh = fixture({ key: "fresh-connection" });
    await expect(fresh.client.execute(JSON.parse(JSON.stringify(operation)))).resolves.toEqual(fresh.pending);
    expect(first.client.queryKey).not.toEqual(fresh.client.queryKey); expect(first.client.scope).toBe(fresh.client.scope);
  });

  it.each(["subject", "tenant", "backend"])("does not replay a retained command under another %s", async change => {
    const first = fixture(); const operation = first.client.prepare({ kind: "cancel", executionId: "run" }, "cancel-1");
    const next = fixture({ subject: change === "subject" ? "other" : undefined, tenantId: change === "tenant" ? "other" : undefined,
      baseUrl: change === "backend" ? "https://other.example/shell-a" : undefined });
    await expect(next.client.execute(operation)).rejects.toThrow("belongs to another backend, account or tenant");
    expect(next.getJson).not.toHaveBeenCalled(); expect(next.postJson).not.toHaveBeenCalled();
  });

  it("copies operation content and does not silently turn pending admission into a running result", async () => {
    const f = fixture(); const outcomes = ["Done"];
    const operation = f.client.prepare({ kind: "complete-human", executionId: "run", invocationId: "invocation", attemptId: "attempt", outcomes }, "complete-1");
    outcomes[0] = "Changed"; expect(Object.isFrozen(operation)).toBe(true);
    await expect(f.client.execute(operation)).resolves.toEqual(f.pending);
    expect(f.postJson.mock.calls[0][1]).toEqual({ requestId: "complete-1", attemptId: "attempt", outcomes: ["Done"] });
  });

  it("uses only the authored version route and request id for publication", async () => {
    const f = fixture();
    await f.client.execute(f.client.prepare({ kind: "publish", versionId: "version:/one" }, "publish-1"));
    expect(f.postJson).toHaveBeenCalledWith("/shell-a/interchange/bpmn/versions/version%3A%2Fone/publish-direct", { requestId: "publish-1" }, { retryOnUnauthorized: false });
  });

  it("preserves the exact held incident, invocation and failed attempt for retry", async () => {
    const f = fixture();
    await f.client.execute(f.client.prepare({ kind: "retry-service", executionId: "run", incidentId: "incident", invocationId: "invocation", failedAttemptId: "failed" }, "retry-1"));
    expect(f.postJson).toHaveBeenCalledWith("/shell-a/bpmn/executions/run/retry-service", { requestId: "retry-1", incidentId: "incident", invocationId: "invocation", failedAttemptId: "failed" }, { retryOnUnauthorized: false });
  });

  it("claims and completes through the separate claims capability without sending a claimant", async () => {
    const f = fixture();
    await f.client.execute(f.client.prepare({ kind: "claim-human", executionId: "run", invocationId: "work:/1", attemptId: "attempt" }, "claim-1"));
    expect(f.postJson).toHaveBeenLastCalledWith("/shell-a/bpmn/executions/run/work/work%3A%2F1/claim", { requestId: "claim-1", attemptId: "attempt" }, { retryOnUnauthorized: false });
    const operation = f.client.prepare({ kind: "complete-claimed-human", executionId: "run", invocationId: "work", attemptId: "attempt", claimId: "claim:a", outcomes: ["Done"] }, "done-1");
    expect(Object.isFrozen(operation.outcomes)).toBe(true);
    await f.client.execute(operation);
    expect(f.postJson).toHaveBeenLastCalledWith("/shell-a/bpmn/executions/run/work/work/complete-claimed",
      { requestId: "done-1", attemptId: "attempt", claimId: "claim:a", outcomes: ["Done"] }, { retryOnUnauthorized: false });
    for (const [, body] of f.postJson.mock.calls) expect(JSON.stringify(body)).not.toMatch(/claimant|author/);
  });

  it("refuses claim commands on a host that advertises only unclaimed completion", async () => {
    const f = fixture();
    const document = { capabilities: f.session.capabilities.capabilities.filter(capability => capability.id !== capabilityIds.bpmnClaims) };
    const g = fixture({ document });
    await expect(g.client.execute(g.client.prepare({ kind: "claim-human", executionId: "run", invocationId: "work", attemptId: "attempt" }, "claim-1")))
      .rejects.toMatchObject({ name: "ApiCapabilityUnavailableError", capabilityId: capabilityIds.bpmnClaims });
    expect(g.postJson).not.toHaveBeenCalled();
  });

  it("reads admission separately from accepted-work capability and preserves admission-off", async () => {
    const f = fixture(); await f.client.publications(); f.getJson.mockResolvedValueOnce({ admitNewExecutions: false } as never);
    await expect(f.client.admission()).resolves.toEqual({ admitNewExecutions: false });
    expect(f.getJson).toHaveBeenLastCalledWith("/shell-a/bpmn/admission", undefined);
  });

  it("refuses invalid page budgets without discovering capabilities", () => {
    const f = fixture(); expect(() => f.client.executions({ limit: 501 })).toThrow("between 1 and 500");
    expect(f.getJson).not.toHaveBeenCalled();
  });

  it("passes request cancellation through and rejects a late response even if a custom transport ignores abort", async () => {
    const f = fixture(); await f.client.publications(); const late = deferred<unknown>();
    f.getJson.mockImplementationOnce(() => late.promise as never); const abort = new AbortController();
    const result = f.client.inspection("run", abort.signal); await vi.waitFor(() => expect(f.getJson).toHaveBeenCalledTimes(3));
    abort.abort(); late.resolve({ execution: { executionId: "run" } });
    await expect(result).rejects.toMatchObject({ name: "AbortError" });
    expect(f.getJson.mock.calls[2][1]).toEqual({ signal: abort.signal });
  });
});
