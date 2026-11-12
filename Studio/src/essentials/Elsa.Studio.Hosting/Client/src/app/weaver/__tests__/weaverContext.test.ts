import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import { collectWeaverContext, isWeaverContextCollectionCurrent } from "../weaverContext";
import { beginWeaverModuleRegistration, retireWeaverModuleRegistration, retireWeaverRegistryGeneration } from "../weaverRegistrationOrigins";

const attachment = { id: "one", source: "Sample", label: "Selected sample", contentType: "sample", sensitivity: "internal" as const, scope: "selection" };
function registry() { return createStudioRegistry({ ...createEndpointContext("https://example.test/"), hostVersion: "1", sdkVersion: "1" }); }
describe("module context collection", () => {
  it("does not invoke a retired satellite provider even when its resource owner remains present", async () => {
    const api = registry(); const collect = vi.fn(async () => [attachment]);
    const registration = beginWeaverModuleRegistration(api, "Sample.Satellite");
    registration.api.weaver.contextProviders.add({ id: "sample", moduleId: "Sample.Resource", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect });
    registration.complete(); retireWeaverModuleRegistration(api, "Sample.Satellite");
    expect(await collectWeaverContext(api, { route: "/" }, "explain")).toEqual({ attachments: [], diagnostics: [] });
    expect(collect).not.toHaveBeenCalled();
  });

  it("rechecks registration before the deferred contributor callback begins", async () => {
    const api = registry(); const collect = vi.fn(async () => [attachment]);
    const registration = beginWeaverModuleRegistration(api, "Sample.Satellite");
    registration.api.weaver.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect });
    registration.complete();
    const operation = collectWeaverContext(api, { route: "/" }, "explain");
    const refusal = expect(operation).rejects.toThrow("Module context changed");
    retireWeaverModuleRegistration(api, "Sample.Satellite");
    await refusal;
    expect(collect).not.toHaveBeenCalled();
  });

  it.each(["module", "generation"] as const)("rejects a late snapshot after its %s is retired", async kind => {
    const api = registry(); let release!: (value: typeof attachment[]) => void;
    let enter!: () => void; const entered = new Promise<void>(resolve => { enter = resolve; });
    const registration = beginWeaverModuleRegistration(api, "Sample.Satellite");
    registration.api.weaver.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect: () => { enter(); return new Promise(resolve => { release = resolve; }); } });
    registration.complete();
    const refusal = expect(collectWeaverContext(api, { route: "/" }, "explain")).rejects.toThrow("Module context changed");
    await entered;
    if (kind === "module") retireWeaverModuleRegistration(api, "Sample.Satellite");
    else retireWeaverRegistryGeneration(api);
    release([attachment]); await refusal;
  });

  it("rejects an earlier completed source retired while an independent source is awaiting", async () => {
    const api = registry(); let release!: (value: typeof attachment[]) => void;
    let enter!: () => void; const entered = new Promise<void>(resolve => { enter = resolve; });
    const first = beginWeaverModuleRegistration(api, "Sample.First");
    first.api.weaver.contextProviders.add({ id: "first", displayName: "First", surfaces: ["*"], sensitivity: "internal", order: 1, collect: async () => [attachment] }); first.complete();
    const second = beginWeaverModuleRegistration(api, "Sample.Second");
    second.api.weaver.contextProviders.add({ id: "second", displayName: "Second", surfaces: ["*"], sensitivity: "internal", order: 2, collect: () => { enter(); return new Promise(resolve => { release = resolve; }); } }); second.complete();
    const refusal = expect(collectWeaverContext(api, { route: "/" }, "explain")).rejects.toThrow("Module context changed");
    await entered; retireWeaverModuleRegistration(api, "Sample.First"); release([]); await refusal;
  });

  it("binds an accepted snapshot to its actual registry and registration lifetime", async () => {
    const api = registry(); const registration = beginWeaverModuleRegistration(api, "Sample.Satellite");
    registration.api.weaver.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect: async () => [attachment] }); registration.complete();
    const collection = await collectWeaverContext(api, { route: "/" }, "explain");
    expect(collection.attachments).toEqual([attachment]);
    expect(isWeaverContextCollectionCurrent(api, collection)).toBe(true);
    expect(isWeaverContextCollectionCurrent(registry(), collection)).toBe(false);
    retireWeaverModuleRegistration(api, "Sample.Satellite");
    expect(isWeaverContextCollectionCurrent(api, collection)).toBe(false);
  });

  it("preserves explicit content and typed optional limitations", async () => {
    const api = registry(); api.weaver.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect: async () => ({ attachments: [], diagnostics: [{ contributorId: "sample", code: "absent", message: "Optional evidence unavailable.", required: false }] }) });
    expect(await collectWeaverContext(api, { route: "/arbitrary" }, "explain", undefined, [attachment])).toEqual({ attachments: [attachment], diagnostics: [expect.objectContaining({ required: false })] });
  });
  it("diagnoses duplicate attachments rather than silently picking one", async () => {
    await expect(collectWeaverContext(registry(), { route: "/" }, "explain", undefined, [attachment, attachment])).rejects.toThrow("Duplicate context attachment");
  });
  it("classifies a contributor fault at its boundary while preserving structural composition failures", async () => {
    const api = registry(); api.weaver.contextProviders.add({ id: "broken", displayName: "Broken", surfaces: ["*"], sensitivity: "internal", collect: async () => { throw new Error("Unexpected failure"); } });
    // Contributor failures now become typed limitations; duplicate/malformed composition remains fatal.
    expect(await collectWeaverContext(api, { route: "/" }, "explain")).toEqual({ attachments: [], diagnostics: [expect.objectContaining({ contributorId: "broken", code: "weaver.context.contributor_failed", required: false })] });
  });

  it.each([false, true])("identifies a thrown contributor as required=%s without discarding independent evidence", async required => {
    const api = registry();
    api.weaver.contextProviders.add({ id: "complete", displayName: "Complete", surfaces: ["*"], sensitivity: "internal", order: 1, collect: async () => [attachment] });
    api.weaver.contextProviders.add({ id: "broken", displayName: "Broken", surfaces: ["*"], sensitivity: "internal", required, order: 2, collect: async () => { throw new Error("Private contributor implementation detail"); } });
    const result = await collectWeaverContext(api, { route: "/sample" }, "explain");
    expect(result.attachments).toEqual([attachment]);
    expect(result.diagnostics).toEqual([expect.objectContaining({ contributorId: "broken", required })]);
    expect(JSON.stringify(result.diagnostics)).not.toContain("Private contributor implementation detail");
  });

  it.each([false, true])("bounds an uncooperative contributor with required=%s and continues complete collection", async required => {
    vi.useFakeTimers();
    const api = registry();
    let release!: (value: typeof attachment[]) => void;
    api.weaver.contextProviders.add({ id: "stalled", displayName: "Stalled", surfaces: ["*"], sensitivity: "internal", required, order: 1, collect: () => new Promise(resolve => { release = resolve; }) });
    api.weaver.contextProviders.add({ id: "complete", displayName: "Complete", surfaces: ["*"], sensitivity: "internal", order: 2, collect: async () => [attachment] });
    let outcome: Awaited<ReturnType<typeof collectWeaverContext>> | undefined;
    let failure: unknown;
    const operation = collectWeaverContext(api, { route: "/sample" }, "explain").then(value => { outcome = value; }, error => { failure = error; });
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
    api.weaver.contextProviders.add({ id: "stalled", displayName: "Stalled", surfaces: ["*"], sensitivity: "internal", collect: () => new Promise(resolve => { release = resolve; }) });
    let settled = false; let failure: unknown;
    const operation = collectWeaverContext(api, { route: "/sample" }, "explain", undefined, [], controller.signal).then(() => { settled = true; }, error => { settled = true; failure = error; });
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
    api.weaver.contextProviders.add({ id: "late", displayName: "Late", surfaces: ["*"], sensitivity: "internal", collect: context => { observedSignal = context.signal; return new Promise(resolve => { release = resolve; }); } });
    const operation = collectWeaverContext(api, { route: "/sample" }, "explain", undefined, [], undefined, { contributorTimeoutMs: 5 });
    try {
      await vi.advanceTimersByTimeAsync(5);
      const result = await operation;
      expect(observedSignal?.aborted).toBe(true);
      expect(result).toEqual({ attachments: [], diagnostics: [expect.objectContaining({ contributorId: "late", code: "weaver.context.deadline", required: false })] });
      release([attachment]); await Promise.resolve(); await Promise.resolve();
      expect(result.attachments).toEqual([]);
    } finally { release?.([]); await operation; vi.useRealTimers(); }
  });

  it("copies accepted opaque payloads so later module mutation cannot alter the snapshot", async () => {
    const api = registry(); const candidate = { ...attachment, content: { opaque: ["accepted"] } };
    api.weaver.contextProviders.add({ id: "complete", displayName: "Complete", surfaces: ["*"], sensitivity: "internal", collect: async () => [candidate] });
    const result = await collectWeaverContext(api, { route: "/sample" }, "explain");
    candidate.content.opaque.push("late mutation");
    expect(result.attachments[0].content).toEqual({ opaque: ["accepted"] });
  });

  it("keeps unrelated evidence when an optional source exceeds the aggregate candidate count", async () => {
    const api = registry(); const explicit = Array.from({ length: 31 }, (_, index) => ({ ...attachment, id: `explicit-${index}` }));
    api.weaver.contextProviders.add({ id: "optional-large", displayName: "Optional", surfaces: ["*"], sensitivity: "internal", collect: async () => [{ ...attachment, id: "extra-a" }, { ...attachment, id: "extra-b" }] });
    const result = await collectWeaverContext(api, { route: "/sample" }, "explain", undefined, explicit);
    expect(result.attachments).toEqual(explicit);
    expect(result.diagnostics).toEqual([expect.objectContaining({ contributorId: "optional-large", code: "weaver.context.budget_exceeded", required: false })]);
  });

  it("does not disguise malformed JSON as optional absence", async () => {
    const api = registry(); const content: Record<string, unknown> = {}; content.self = content;
    api.weaver.contextProviders.add({ id: "malformed", displayName: "Malformed", surfaces: ["*"], sensitivity: "internal", collect: async () => [{ ...attachment, content }] });
    await expect(collectWeaverContext(api, { route: "/sample" }, "explain")).rejects.toThrow();
  });

  it.each(["count", "summary", "content"] as const)("identifies missing required evidence when the existing candidate %s budget is exceeded", async kind => {
    const api = registry();
    const candidates = kind === "count"
      ? Array.from({ length: 33 }, (_, index) => ({ ...attachment, id: `candidate-${index}` }))
      : [{ ...attachment, ...(kind === "summary" ? { summary: "x".repeat(4097) } : { content: "x".repeat(32769) }) }];
    api.weaver.contextProviders.add({ id: "required-large", displayName: "Required large source", surfaces: ["*"], sensitivity: "internal", required: true, collect: async () => candidates });
    const result = await collectWeaverContext(api, { route: "/sample" }, "explain");
    expect(result.diagnostics).toEqual(expect.arrayContaining([expect.objectContaining({ required: true })]));
  });
});
