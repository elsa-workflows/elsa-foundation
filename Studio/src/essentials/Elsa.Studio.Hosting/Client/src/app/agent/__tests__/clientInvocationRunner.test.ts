import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext, type StudioAgentPreparedInvocation } from "../../../sdk";
import { createClientInvocationRunner } from "../clientInvocationRunner";

function fixture() {
  const api = createStudioRegistry({ ...createEndpointContext("https://example.test/"), hostVersion: "1", sdkVersion: "1" });
  const execute = vi.fn(async () => ({ outcome: "applied" as const, summary: "Observed module effect.", result: { typeId: "sample.result", schemaVersion: "1", data: { value: 2 } } }));
  api.agent.toolContracts.add({ id: "sample_change", moduleId: "Sample", slotId: "sample", displayName: "Change", description: "Change", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], contractVersion: "1", executionLocation: "clientWorkingState", executeClient: execute });
  const invocation: StudioAgentPreparedInvocation = { invocationId: "i", sessionId: "s", turnId: "t", toolCallId: "tc", toolName: "sample_change", toolContractVersion: "1", moduleId: "Sample", actorId: "a", tenantId: "tenant", target: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r" }, arguments: { value: 2 }, approvalDigest: "digest", risk: "reviewRequired", preview: { typeId: "sample.preview", schemaVersion: "1", data: {} }, executionLocation: "clientWorkingState", requiresApproval: true, client: { clientId: api.agent.clientId, generationId: "generation" }, expiresAt: new Date(Date.now() + 60000).toISOString() };
  return { api, execute, invocation, runner: createClientInvocationRunner(api) };
}

describe("reviewed client delivery", () => {
  it("coalesces identical delivery and preserves typed observation", async () => {
    const { runner, invocation, execute } = fixture();
    const [first, second] = await Promise.all([runner.run(invocation), runner.run(structuredClone(invocation))]);
    expect(execute).toHaveBeenCalledTimes(1); expect(second).toEqual(first);
    expect(first).toMatchObject({ outcome: "applied", approvalDigest: "digest", result: { typeId: "sample.result", schemaVersion: "1", data: { value: 2 } } });
  });
  it("rejects conflicting duplicate delivery without another effect", async () => {
    const { runner, invocation, execute } = fixture(); await runner.run(invocation);
    await expect(runner.run({ ...invocation, arguments: { value: 3 } })).rejects.toThrow("Conflicting");
    expect(execute).toHaveBeenCalledTimes(1);
  });
  it.each(["client", "version", "expiry", "cancel"])("refuses invalid %s before dispatch", async kind => {
    const { runner, invocation, execute } = fixture(); const abort = new AbortController();
    if (kind === "client") invocation.client = { clientId: "other", generationId: "generation" };
    if (kind === "version") invocation.toolContractVersion = "2";
    if (kind === "expiry") invocation.expiresAt = new Date(0).toISOString();
    if (kind === "cancel") abort.abort();
    expect((await runner.run(invocation, abort.signal)).outcome).toBe("refused"); expect(execute).not.toHaveBeenCalled();
  });
  it("reports post-dispatch uncertainty without replay", async () => {
    const { runner, invocation, execute } = fixture(); execute.mockRejectedValue(new Error("Receipt lost after effect."));
    expect((await runner.run(invocation)).outcome).toBe("uncertain");
    expect((await runner.run(invocation)).outcome).toBe("uncertain"); expect(execute).toHaveBeenCalledTimes(1);
  });
});

// Preview (#3441 T021): module-owned availability of the unsaved target, checked before dispatch.
describe("module-owned client target availability", () => {
  function withAvailability(availability: (invocation: StudioAgentPreparedInvocation, signal?: AbortSignal) => ReturnType<NonNullable<Parameters<ReturnType<typeof fixture>["api"]["agent"]["toolContracts"]["add"]>[0]["clientTargetAvailability"]>>) {
    const api = createStudioRegistry({ ...createEndpointContext("https://example.test/"), hostVersion: "1", sdkVersion: "1" });
    const execute = vi.fn(async () => ({ outcome: "applied" as const, summary: "Observed module effect." }));
    const check = vi.fn(availability);
    api.agent.toolContracts.add({ id: "sample_change", moduleId: "Sample", slotId: "sample", displayName: "Change", description: "Change", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], contractVersion: "1", executionLocation: "clientWorkingState", executeClient: execute, clientTargetAvailability: check });
    const { invocation } = fixture();
    invocation.client = { clientId: api.agent.clientId, generationId: "generation" };
    return { runner: createClientInvocationRunner(api), execute, check, invocation };
  }

  it("refuses an undispatched operation whose unsaved target is gone with the module's reason and zero effect", async () => {
    const { runner, execute, check, invocation } = withAvailability(() => ({ status: "unavailable", reason: "The workflow editor that held this draft was closed." }));
    const acknowledgement = await runner.run(invocation);
    expect(acknowledgement).toMatchObject({ outcome: "refused", summary: "The workflow editor that held this draft was closed.", approvalDigest: "digest", client: invocation.client });
    expect(check).toHaveBeenCalledWith(invocation, undefined);
    expect(execute).not.toHaveBeenCalled();
  });

  it.each([
    ["a disabled target", () => ({ status: "disabled" as const })],
    ["an unanswered check", () => { throw new Error("module state lost"); }],
    ["a rejected check", async () => { throw new Error("module state lost"); }],
    ["no answer", () => undefined as never]
  ])("refuses on %s without dispatching", async (_name, availability) => {
    const { runner, execute, invocation } = withAvailability(availability);
    const acknowledgement = await runner.run(invocation);
    expect(acknowledgement.outcome).toBe("refused");
    expect(acknowledgement.summary).toContain("Nothing was applied.");
    expect(execute).not.toHaveBeenCalled();
  });

  it("dispatches once when the module confirms the target, and a dispatched failure stays uncertain", async () => {
    const { runner, execute, check, invocation } = withAvailability(async () => ({ status: "available" }));
    execute.mockRejectedValueOnce(new Error("The editor closed after the change was sent."));
    expect((await runner.run(invocation)).outcome).toBe("uncertain");
    expect((await runner.run(invocation)).outcome).toBe("uncertain");
    expect(check).toHaveBeenCalledTimes(1);
    expect(execute).toHaveBeenCalledTimes(1);
  });

  it("refuses when cancelled while the module was answering", async () => {
    const abort = new AbortController();
    const { runner, execute, invocation } = withAvailability(async () => { abort.abort(); return { status: "available" }; });
    expect((await runner.run(invocation, abort.signal)).outcome).toBe("refused");
    expect(execute).not.toHaveBeenCalled();
  });

  it("refuses when the deadline passed while the module was answering", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    try {
      const { runner, execute, invocation } = withAvailability(async () => { vi.setSystemTime(Date.parse(invocation.expiresAt) + 1); return { status: "available" }; });
      const acknowledgement = await runner.run(invocation);
      expect(acknowledgement).toMatchObject({ outcome: "refused", summary: "Client invocation was cancelled or expired before dispatch." });
      expect(execute).not.toHaveBeenCalled();
    } finally { vi.useRealTimers(); }
  });

  it("reports a missing module adapter explicitly", async () => {
    const { runner, invocation } = fixture();
    const acknowledgement = await runner.run({ ...invocation, toolName: "removed_tool" });
    expect(acknowledgement).toMatchObject({ outcome: "refused", summary: "The owning module's client adapter for this change is not present. Nothing was applied." });
  });
});
