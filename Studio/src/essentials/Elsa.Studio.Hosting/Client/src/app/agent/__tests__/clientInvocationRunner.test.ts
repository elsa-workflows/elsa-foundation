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
