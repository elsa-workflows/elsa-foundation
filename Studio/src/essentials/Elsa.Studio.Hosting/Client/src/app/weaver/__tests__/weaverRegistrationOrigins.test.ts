import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { loadStudioModules, registerStudioModuleInstances } from "../../loader";
import { createWeaverRegistry, createEndpointContext, presentsResourceModule, type ElsaStudioModuleApi, type StudioModuleInstance, type StudioModuleManifest } from "../../../sdk";
import { collectWeaverContext } from "../weaverContext";
import { beginWeaverModuleRegistration, retireWeaverModuleRegistration, retireWeaverRegistryGeneration, isWeaverContributionCurrent } from "../weaverRegistrationOrigins";

const modes = ["instances", "legacy"] as const;
type Mode = typeof modes[number];

describe("Weaver registration origin lifetime", () => {
  it.each(modes)("detaches failed prompt/result subscriptions before healthy host delivery, including captured legacy functions (%s)", async mode => {
    const api = registry(); const unavailable = vi.fn(() => { throw new Error("Unavailable module callback ran."); });
    let captured!: ElsaStudioModuleApi;
    await register(mode, api, [{ id: "Broken", instance: { register(scoped) {
      captured = scoped;
      scoped.weaver.onPrompt(unavailable); scoped.ai.onPromptResult(unavailable);
      throw new Error("Registration failed.");
    } } }]);
    const prompt = vi.fn(); const result = vi.fn(); api.weaver.onPrompt(prompt); api.weaver.onPromptResult(result);
    expect(() => api.weaver.dispatchPrompt({ message: "Healthy" })).not.toThrow();
    expect(() => api.weaver.publishPromptResult({ requestId: "healthy", status: "completed", text: "" })).not.toThrow();
    const subscribe = captured.ai.onPrompt; const dispatch = captured.ai.dispatchPrompt; const publish = captured.weaver.publishPromptResult;
    subscribe(unavailable); captured.weaver.onPromptResult(unavailable);
    dispatch({ requestId: "refused", message: "Stale" }); publish({ requestId: "forged", status: "completed", text: "" });
    expect(unavailable).not.toHaveBeenCalled(); expect(prompt).toHaveBeenCalledTimes(1);
    expect(result.mock.calls.map(([value]) => value)).toEqual([{ requestId: "healthy", status: "completed", text: "" }, { requestId: "refused", status: "failed", text: "" }]);
  });

  it.each(modes.flatMap(mode => (["weaver", "ai"] as const).map(facade => ({ mode, facade }))))(
    "activates pending $facade subscriptions only after registration and never after withdrawal ($mode)", async ({ mode, facade }) => {
      const api = registry(); const entered = deferred(); const release = deferred(); const prompt = vi.fn(); const result = vi.fn(); const cancelled = vi.fn();
      let captured!: ElsaStudioModuleApi;
      const operation = register(mode, api, [{ id: "Pending", instance: { async register(scoped) {
        captured = scoped; scoped[facade].onPrompt(prompt); scoped[facade].onPromptResult(result);
        const stop = scoped[facade].onPrompt(cancelled); stop(); entered.resolve(); await release.promise;
      } } }]);
      await entered.promise;
      api.weaver.dispatchPrompt({ message: "Before activation" }); api.weaver.publishPromptResult({ requestId: "before", status: "completed", text: "" });
      expect(prompt).not.toHaveBeenCalled(); expect(result).not.toHaveBeenCalled();
      release.resolve(); await operation;
      api.weaver.dispatchPrompt({ message: "After activation" }); api.weaver.publishPromptResult({ requestId: "after", status: "completed", text: "" });
      expect(prompt).toHaveBeenCalledTimes(1); expect(result).toHaveBeenCalledTimes(1); expect(cancelled).not.toHaveBeenCalled();
      retireWeaverModuleRegistration(api, "Pending");
      captured[facade].onPrompt(cancelled); captured[facade].onPromptResult(cancelled);
      api.weaver.dispatchPrompt({ message: "After withdrawal" }); api.weaver.publishPromptResult({ requestId: "withdrawn", status: "completed", text: "" });
      expect(prompt).toHaveBeenCalledTimes(1); expect(result).toHaveBeenCalledTimes(1); expect(cancelled).not.toHaveBeenCalled();
    }
  );

  it("preserves original callback identity across canonical and legacy facades", () => {
    const api = registry(); const registration = beginWeaverModuleRegistration(api, "Satellite"); const receive = vi.fn();
    registration.api.weaver.onPrompt(receive); const stop = registration.api.ai.onPrompt(receive); registration.complete();
    api.weaver.dispatchPrompt({ message: "One delivery" }); expect(receive).toHaveBeenCalledTimes(1);
    stop(); api.weaver.dispatchPrompt({ message: "Unsubscribed" }); expect(receive).toHaveBeenCalledTimes(1);
  });

  it("never attaches pending callbacks after whole-generation retirement or late completion", () => {
    const api = registry(); const registration = beginWeaverModuleRegistration(api, "Pending"); const receive = vi.fn();
    registration.api.weaver.onPrompt(receive); registration.api.ai.onPromptResult(receive);
    retireWeaverRegistryGeneration(api); expect(registration.complete()).toBe(false);
    registration.api.ai.onPrompt(receive); api.weaver.dispatchPrompt({ message: "Late" });
    api.weaver.publishPromptResult({ requestId: "late", status: "cancelled", text: "" }); expect(receive).not.toHaveBeenCalled();
  });

  it("does not repeat a manual unsubscribe during retirement and remove a later direct subscription", () => {
    const api = registry(); const registration = beginWeaverModuleRegistration(api, "Satellite"); registration.complete(); const receive = vi.fn();
    const stop = registration.api.weaver.onPrompt(receive); stop(); api.weaver.onPrompt(receive);
    retireWeaverModuleRegistration(api, "Satellite"); api.weaver.dispatchPrompt({ message: "Direct host delivery" });
    expect(receive).toHaveBeenCalledTimes(1);
  });

  it("retains native repeated old-unsubscribe behavior after an explicit re-add", () => {
    const api = registry(); const registration = beginWeaverModuleRegistration(api, "Satellite"); registration.complete(); const receive = vi.fn();
    const stop = registration.api.weaver.onPrompt(receive); stop(); registration.api.ai.onPrompt(receive); stop();
    api.weaver.dispatchPrompt({ message: "Removed by the old SDK handle" }); expect(receive).not.toHaveBeenCalled();
  });

  it("retains the SDK Set's single shared callback and cross-registration unsubscribe behavior", () => {
    const api = registry(); const first = beginWeaverModuleRegistration(api, "First"); const second = beginWeaverModuleRegistration(api, "Second"); const receive = vi.fn();
    first.api.weaver.onPrompt(receive); second.api.ai.onPrompt(receive); first.complete(); second.complete();
    api.weaver.dispatchPrompt({ message: "Shared" }); expect(receive).toHaveBeenCalledTimes(1);
    retireWeaverModuleRegistration(api, "First"); api.weaver.dispatchPrompt({ message: "Native Set removal" }); expect(receive).toHaveBeenCalledTimes(1);
  });

  it("removes an unavailable listener before its turn in the SDK's live iteration", () => {
    const api = registry(); const later = vi.fn(); const healthy = vi.fn();
    api.weaver.onPrompt(() => retireWeaverModuleRegistration(api, "Satellite"));
    const registration = beginWeaverModuleRegistration(api, "Satellite"); registration.api.weaver.onPrompt(later); registration.complete(); api.weaver.onPrompt(healthy);
    api.weaver.dispatchPrompt({ message: "Withdraw before callback" }); expect(later).not.toHaveBeenCalled(); expect(healthy).toHaveBeenCalledTimes(1);
  });

  it("withdraws a loaded listener before a replacement registration fails", () => {
    const api = registry(); const first = beginWeaverModuleRegistration(api, "Satellite"); const old = vi.fn(); const replacement = vi.fn(); const healthy = vi.fn();
    first.api.weaver.onPrompt(old); first.complete(); api.weaver.onPrompt(healthy);
    const next = beginWeaverModuleRegistration(api, "Satellite"); next.api.ai.onPrompt(replacement); next.fail();
    api.weaver.dispatchPrompt({ message: "Independent" }); expect(old).not.toHaveBeenCalled(); expect(replacement).not.toHaveBeenCalled(); expect(healthy).toHaveBeenCalledTimes(1);
  });

  it("preserves direct host terminal cancellation publication after generation retirement", () => {
    const api = registry(); const registration = beginWeaverModuleRegistration(api, "Satellite"); const unavailable = vi.fn(); const host = vi.fn();
    registration.api.ai.onPromptResult(unavailable); registration.complete(); api.weaver.onPromptResult(host);
    retireWeaverRegistryGeneration(api); api.weaver.publishPromptResult({ requestId: "pending", status: "cancelled", text: "" });
    expect(unavailable).not.toHaveBeenCalled(); expect(host).toHaveBeenCalledTimes(1); expect(host).toHaveBeenCalledWith({ requestId: "pending", status: "cancelled", text: "" });
  });

  it("preserves custom method receivers, plain listener receivers and primary failure during owned cleanup", () => {
    const api = registry(); const original = api.weaver.onPrompt; const healthy = vi.fn();
    api.weaver.onPrompt = function (listener) {
      expect(this).toBe(api.weaver); const stop = original.call(this, listener);
      return () => { stop(); throw new Error("Private disposer failure."); };
    };
    const registration = beginWeaverModuleRegistration(api, "Satellite");
    const receive = vi.fn(function (this: unknown) { expect(this).toBeUndefined(); });
    registration.api.weaver.onPrompt(receive); registration.api.ai.onPromptResult(healthy); registration.complete();
    api.weaver.dispatchPrompt({ message: "Loaded" }); expect(receive).toHaveBeenCalledTimes(1);
    expect(() => registration.fail()).not.toThrow();
    api.weaver.publishPromptResult({ requestId: "closed", status: "completed", text: "" }); expect(healthy).not.toHaveBeenCalled();
    expect(api.diagnostics.list().at(-1)?.reason).toBe("Module prompt subscription cleanup could not complete.");
    expect(JSON.stringify(api.diagnostics.list())).not.toContain("Private disposer failure.");
  });

  it.each(modes)("hides partial failed registration before availability or collection, retaining independent sources (%s)", async mode => {
    const api = registry(); const broken = provider("broken"); const healthy = provider("healthy"); const direct = provider("direct");
    api.weaver.contextProviders.add(direct);
    await register(mode, api, [
      { id: "Satellite", instance: { register(scoped) { scoped.weaver.contextProviders.add(broken); throw new Error("Registration failed."); } } },
      { id: "Independent", instance: { register(scoped) { scoped.weaver.contextProviders.add(healthy); } } }
    ]);
    expect((await collectWeaverContext(api, { route: "/resource" }, "explain")).attachments.map(item => item.id)).toEqual(["direct", "healthy"]);
    expect(broken.availability).not.toHaveBeenCalled(); expect(broken.collect).not.toHaveBeenCalled();
    expect(healthy.collect).toHaveBeenCalledTimes(1); expect(direct.collect).toHaveBeenCalledTimes(1);
    expect(api.diagnostics.list().map(item => item.status)).toEqual(["failed", "loaded"]);
  });

  it.each(modes)("keeps satellite resource ownership and callback receivers intact (%s)", async mode => {
    const api = registry(); const source = provider("satellite");
    const supports = vi.fn(function (this: unknown) { expect(this).toBe(renderer); return true; });
    const renderer = { id: "renderer", moduleId: "Resource", displayName: "Resource result", resultTypes: ["resource.result"], supports, component: () => null };
    await register(mode, api, [{ id: "Satellite", instance: { register(scoped) {
      scoped.weaver.contextProviders.add(source); scoped.weaver.resultRenderers.add(renderer);
    } } }]);
    expect(api.weaver.contextProviders.list()[0]).toBe(source);
    expect(api.weaver.resultRenderers.list().find(item => presentsResourceModule(item, "Resource") && item.supports?.({ proposal: {}, result: {} }))).toBe(renderer);
    expect(supports).toHaveBeenCalledTimes(1);
    await collectWeaverContext(api, { route: "/resource" }, "explain");
    expect(source.collect).toHaveBeenCalledTimes(1);
    retireWeaverModuleRegistration(api, "Satellite");
    expect(api.weaver.contextProviders.list()).toEqual([]); expect(api.weaver.resultRenderers.list()).toEqual([]);
    expect(source.moduleId).toBe("Resource"); expect(renderer.moduleId).toBe("Resource");
  });

  it.each(modes)("does not expose a registering module before its awaited registration completes (%s)", async mode => {
    const api = registry(); const source = provider("pending"); const entered = deferred(); const release = deferred();
    const operation = register(mode, api, [{ id: "Pending", instance: { async register(scoped) {
      scoped.weaver.contextProviders.add(source); entered.resolve(); await release.promise;
    } } }]);
    await entered.promise;
    try {
      expect(api.weaver.contextProviders.list()).toEqual([]); expect(source.availability).not.toHaveBeenCalled();
    } finally { release.resolve(); await operation; }
    expect(api.weaver.contextProviders.list()).toEqual([source]);
    expect(source.availability).toHaveBeenCalledTimes(1);
  });

  it.each(modes)("retirement wins over pending completion and captured late additions (%s)", async mode => {
    const api = registry(); const source = provider("late"); const entered = deferred(); const release = deferred();
    let captured!: ElsaStudioModuleApi; const later = vi.fn();
    const operation = register(mode, api, [
      { id: "Pending", instance: { async register(scoped) { captured = scoped; entered.resolve(); await release.promise; scoped.weaver.contextProviders.add(source); } } },
      { id: "Next", instance: { register: later } }
    ]);
    await entered.promise; retireWeaverRegistryGeneration(api);
    release.resolve(); await operation;
    captured.weaver.contextProviders.add(provider("later"));
    expect(api.weaver.contextProviders.list()).toEqual([]); expect(captured.weaver.contextProviders.list()).toEqual([]);
    expect(isWeaverContributionCurrent(captured, source)).toBe(false);
    expect(source.availability).not.toHaveBeenCalled(); expect(later).not.toHaveBeenCalled();
  });

  it("preserves independent provenance when a module reuses an object in a different registry", async () => {
    const first = registry(); const second = registry(); const source = provider("shared");
    await register("instances", first, [{ id: "Satellite", instance: { register(scoped) { scoped.weaver.contextProviders.add(source); throw new Error("First generation failed."); } } }]);
    await register("instances", second, [{ id: "Satellite", instance: { register(scoped) { scoped.weaver.contextProviders.add(source); } } }]);
    expect(first.weaver.contextProviders.list()).toEqual([]); expect(source.availability).not.toHaveBeenCalled();
    expect((await collectWeaverContext(second, { route: "/resource" }, "explain")).attachments.map(item => item.id)).toEqual(["shared"]);
    retireWeaverRegistryGeneration(first);
    expect(second.weaver.contextProviders.list()).toEqual([source]); expect(source.collect).toHaveBeenCalledTimes(1);
  });

  it("does not let a second origin overwrite an already failed origin for the same object", async () => {
    const api = registry(); const source = provider("shared"); const independent = provider("independent");
    await register("instances", api, [
      { id: "Failed", instance: { register(scoped) { scoped.weaver.contextProviders.add(source); throw new Error("Failed."); } } },
      { id: "Second", instance: { register(scoped) { scoped.weaver.contextProviders.add(source); scoped.weaver.contextProviders.add(independent); } } }
    ]);
    expect((await collectWeaverContext(api, { route: "/resource" }, "explain")).attachments.map(item => item.id)).toEqual(["independent"]);
    expect(source.availability).not.toHaveBeenCalled(); expect(source.collect).not.toHaveBeenCalled();
    expect(independent.collect).toHaveBeenCalledTimes(1);
  });

  it("binds an API assembled with the public SDK factory without treating direct sources as discovered modules", async () => {
    const weaver = createWeaverRegistry();
    const api = { ...registry(), weaver, ai: { dispatchPrompt: weaver.dispatchPrompt, onPrompt: weaver.onPrompt, publishPromptResult: weaver.publishPromptResult, onPromptResult: weaver.onPromptResult } };
    const direct = provider("direct"); const broken = provider("broken");
    api.weaver.contextProviders.add(direct);
    await register("legacy", api, [{ id: "Failed", instance: { register(scoped) { scoped.weaver.contextProviders.add(broken); throw new Error("Failed."); } } }]);
    expect(direct.availability).not.toHaveBeenCalled(); expect(broken.availability).not.toHaveBeenCalled();
    expect((await collectWeaverContext(api, { route: "/resource" }, "explain")).attachments.map(item => item.id)).toEqual(["direct"]);
    expect(broken.availability).not.toHaveBeenCalled(); expect(broken.collect).not.toHaveBeenCalled();
  });

  it("withdraws an old registration before a legacy reload fails compatibility", async () => {
    const api = registry(); const source = provider("old");
    await register("legacy", api, [{ id: "Satellite", instance: { register(scoped) { scoped.weaver.contextProviders.add(source); } } }]);
    await loadStudioModules([{ ...manifest("Satellite"), requiredHostVersion: "^99.0.0" }], api, { hostVersion: "1.0.0", sdkVersion: "1.0.0", importModule: async () => { throw new Error("Must not import."); } });
    expect(api.weaver.contextProviders.list()).toEqual([]); expect(source.availability).not.toHaveBeenCalled();
    expect(api.diagnostics.list().map(item => item.status)).toEqual(["loaded", "incompatible"]);
  });

  it("preserves fatal connection registration failure and retires earlier Weaver sources", async () => {
    const api = registry(); const earlier = provider("earlier"); const broken = provider("broken"); const later = vi.fn();
    await expect(register("instances", api, [
      { id: "Earlier", instance: { register(scoped) { scoped.weaver.contextProviders.add(earlier); } } },
      { id: "Connection", instance: {
        connection: { hostContext: api.host, hostIdentity: { status: "anonymous", roles: [], permissions: [] },
          getSnapshot: () => ({ active: null, loading: false, error: null }), subscribe: () => () => {}, initialize: async () => {}, qualifyPath: () => null },
        register(scoped) { scoped.weaver.contextProviders.add(broken); throw new Error("Connection registration failed."); }
      } },
      { id: "Later", instance: { register: later } }
    ])).rejects.toThrow("Connection registration failed.");
    expect(api.weaver.contextProviders.list()).toEqual([]);
    expect(earlier.availability).not.toHaveBeenCalled(); expect(broken.availability).not.toHaveBeenCalled(); expect(later).not.toHaveBeenCalled();
  });
});

function registry() {
  return createStudioRegistry({ hostVersion: "1.0.0", sdkVersion: "1.0.0", ...createEndpointContext("https://studio.example/") });
}

function provider(id: string) {
  const source = { id, moduleId: "Resource", displayName: id, surfaces: ["*"], sensitivity: "internal" as const,
    availability: vi.fn(() => true),
    collect: vi.fn(async function (this: unknown) {
      expect(this).toBe(source);
      return [{ id, source: "Resource", label: id, contentType: "resource", sensitivity: "internal" as const, scope: "screen" }];
    }) };
  return source;
}

async function register(mode: Mode, api: ElsaStudioModuleApi, entries: { id: string; instance: StudioModuleInstance }[]) {
  if (mode === "instances") return registerStudioModuleInstances(entries.map(entry => ({ manifest: manifest(entry.id), instance: entry.instance })), api);
  await loadStudioModules(entries.map(entry => manifest(entry.id)), api, { hostVersion: "1.0.0", sdkVersion: "1.0.0", loadStyle: async () => {},
    importModule: async entry => entries.find(item => entry.startsWith(`/modules/${item.id}.js?`))!.instance });
}

function manifest(id: string): StudioModuleManifest {
  return { id, displayName: id, version: "1.0.0", entry: `/modules/${id}.js`, styles: [], requiredHostVersion: "*", requiredSdkVersion: "*", capabilities: [] };
}

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>(complete => { resolve = complete; });
  return { promise, resolve };
}
