import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import { collectAgentContext } from "../agentContext";

const attachment = { id: "one", source: "Sample", label: "Selected sample", contentType: "sample", sensitivity: "internal" as const, scope: "selection" };
function registry() { return createStudioRegistry({ ...createEndpointContext("https://example.test/"), hostVersion: "1", sdkVersion: "1" }); }
describe("module context collection", () => {
  it("preserves explicit content and typed optional limitations", async () => {
    const api = registry(); api.agent.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect: async () => ({ attachments: [], diagnostics: [{ contributorId: "sample", code: "absent", message: "Optional evidence unavailable.", required: false }] }) });
    expect(await collectAgentContext(api, { route: "/arbitrary" }, "explain", undefined, [attachment])).toEqual({ attachments: [attachment], diagnostics: [expect.objectContaining({ required: false })] });
  });
  it("diagnoses duplicate attachments rather than silently picking one", async () => {
    await expect(collectAgentContext(registry(), { route: "/" }, "explain", undefined, [attachment, attachment])).rejects.toThrow("Duplicate context attachment");
  });
  it("classifies a contributor fault at its boundary while preserving structural composition failures", async () => {
    const api = registry(); api.agent.contextProviders.add({ id: "broken", displayName: "Broken", surfaces: ["*"], sensitivity: "internal", collect: async () => { throw new Error("Unexpected failure"); } });
    // Contributor failures now become typed limitations; duplicate/malformed composition remains fatal.
    expect(await collectAgentContext(api, { route: "/" }, "explain")).toEqual({ attachments: [], diagnostics: [expect.objectContaining({ contributorId: "broken", code: "agent.context.contributor_failed", required: false })] });
  });

  it.each([false, true])("identifies a thrown contributor as required=%s without discarding independent evidence", async required => {
    const api = registry();
    api.agent.contextProviders.add({ id: "complete", displayName: "Complete", surfaces: ["*"], sensitivity: "internal", order: 1, collect: async () => [attachment] });
    api.agent.contextProviders.add({ id: "broken", displayName: "Broken", surfaces: ["*"], sensitivity: "internal", required, order: 2, collect: async () => { throw new Error("Private contributor implementation detail"); } });
    const result = await collectAgentContext(api, { route: "/sample" }, "explain");
    expect(result.attachments).toEqual([attachment]);
    expect(result.diagnostics).toEqual([expect.objectContaining({ contributorId: "broken", required })]);
    expect(JSON.stringify(result.diagnostics)).not.toContain("Private contributor implementation detail");
  });

  it.each([false, true])("bounds an uncooperative contributor with required=%s and continues complete collection", async required => {
    vi.useFakeTimers();
    const api = registry();
    let release!: (value: typeof attachment[]) => void;
    api.agent.contextProviders.add({ id: "stalled", displayName: "Stalled", surfaces: ["*"], sensitivity: "internal", required, order: 1, collect: () => new Promise(resolve => { release = resolve; }) });
    api.agent.contextProviders.add({ id: "complete", displayName: "Complete", surfaces: ["*"], sensitivity: "internal", order: 2, collect: async () => [attachment] });
    let outcome: Awaited<ReturnType<typeof collectAgentContext>> | undefined;
    let failure: unknown;
    const operation = collectAgentContext(api, { route: "/sample" }, "explain").then(value => { outcome = value; }, error => { failure = error; });
    try {
      // This observation bound does not prescribe a product deadline; no cooperative release occurs.
      await vi.advanceTimersByTimeAsync(60_000);
      expect(failure).toBeUndefined();
      expect(outcome).toEqual({ attachments: [attachment], diagnostics: [expect.objectContaining({ contributorId: "stalled", required })] });
    } finally {
      release([]); await operation; vi.useRealTimers();
    }
  });

  it("ends collection on cancellation even when the contributor ignores its signal", async () => {
    const api = registry(); const controller = new AbortController();
    let release!: (value: typeof attachment[]) => void;
    api.agent.contextProviders.add({ id: "stalled", displayName: "Stalled", surfaces: ["*"], sensitivity: "internal", collect: () => new Promise(resolve => { release = resolve; }) });
    let settled = false; let failure: unknown;
    const operation = collectAgentContext(api, { route: "/sample" }, "explain", undefined, [], controller.signal).then(() => { settled = true; }, error => { settled = true; failure = error; });
    try {
      await Promise.resolve();
      controller.abort();
      for (let index = 0; index < 8; index++) await Promise.resolve();
      expect(settled).toBe(true);
      expect(failure).toMatchObject({ name: "AbortError" });
    } finally { release([]); await operation; }
  });

  it("honors the configured deadline, aborts its contributor and excludes late completion", async () => {
    vi.useFakeTimers();
    const api = registry(); let release!: (value: typeof attachment[]) => void; let observedSignal: AbortSignal | undefined;
    api.agent.contextProviders.add({ id: "late", displayName: "Late", surfaces: ["*"], sensitivity: "internal", collect: context => { observedSignal = context.signal; return new Promise(resolve => { release = resolve; }); } });
    const operation = collectAgentContext(api, { route: "/sample" }, "explain", undefined, [], undefined, { contributorTimeoutMs: 5 });
    try {
      await vi.advanceTimersByTimeAsync(5);
      const result = await operation;
      expect(observedSignal?.aborted).toBe(true);
      expect(result).toEqual({ attachments: [], diagnostics: [expect.objectContaining({ contributorId: "late", code: "agent.context.deadline", required: false })] });
      release([attachment]); await Promise.resolve(); await Promise.resolve();
      expect(result.attachments).toEqual([]);
    } finally { release?.([]); await operation; vi.useRealTimers(); }
  });

  it("copies accepted opaque payloads so later module mutation cannot alter the snapshot", async () => {
    const api = registry(); const candidate = { ...attachment, content: { opaque: ["accepted"] } };
    api.agent.contextProviders.add({ id: "complete", displayName: "Complete", surfaces: ["*"], sensitivity: "internal", collect: async () => [candidate] });
    const result = await collectAgentContext(api, { route: "/sample" }, "explain");
    candidate.content.opaque.push("late mutation");
    expect(result.attachments[0].content).toEqual({ opaque: ["accepted"] });
  });

  it("keeps unrelated evidence when an optional source exceeds the aggregate candidate count", async () => {
    const api = registry(); const explicit = Array.from({ length: 31 }, (_, index) => ({ ...attachment, id: `explicit-${index}` }));
    api.agent.contextProviders.add({ id: "optional-large", displayName: "Optional", surfaces: ["*"], sensitivity: "internal", collect: async () => [{ ...attachment, id: "extra-a" }, { ...attachment, id: "extra-b" }] });
    const result = await collectAgentContext(api, { route: "/sample" }, "explain", undefined, explicit);
    expect(result.attachments).toEqual(explicit);
    expect(result.diagnostics).toEqual([expect.objectContaining({ contributorId: "optional-large", code: "agent.context.budget_exceeded", required: false })]);
  });

  it("does not disguise malformed JSON as optional absence", async () => {
    const api = registry(); const content: Record<string, unknown> = {}; content.self = content;
    api.agent.contextProviders.add({ id: "malformed", displayName: "Malformed", surfaces: ["*"], sensitivity: "internal", collect: async () => [{ ...attachment, content }] });
    await expect(collectAgentContext(api, { route: "/sample" }, "explain")).rejects.toThrow();
  });

  it.each(["count", "summary", "content"] as const)("identifies missing required evidence when the existing candidate %s budget is exceeded", async kind => {
    const api = registry();
    const candidates = kind === "count"
      ? Array.from({ length: 33 }, (_, index) => ({ ...attachment, id: `candidate-${index}` }))
      : [{ ...attachment, ...(kind === "summary" ? { summary: "x".repeat(4097) } : { content: "x".repeat(32769) }) }];
    api.agent.contextProviders.add({ id: "required-large", displayName: "Required large source", surfaces: ["*"], sensitivity: "internal", required: true, collect: async () => candidates });
    const result = await collectAgentContext(api, { route: "/sample" }, "explain");
    expect(result.diagnostics).toEqual(expect.arrayContaining([expect.objectContaining({ required: true })]));
  });
});
