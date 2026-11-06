import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { loadStudioModules, registerStudioModuleInstances } from "../../loader";
import { createAgentRegistry, createEndpointContext, presentsResourceModule, type ElsaStudioModuleApi, type StudioModuleInstance, type StudioModuleManifest } from "../../../sdk";
import { collectAgentContext } from "../agentContext";
import { retireAgentModuleRegistration, retireAgentRegistryGeneration, isAgentContributionCurrent } from "../agentRegistrationOrigins";

const modes = ["instances", "legacy"] as const;
type Mode = typeof modes[number];

describe("Weaver registration origin lifetime", () => {
  it.each(modes)("hides partial failed registration before availability or collection, retaining independent sources (%s)", async mode => {
    const api = registry(); const broken = provider("broken"); const healthy = provider("healthy"); const direct = provider("direct");
    api.agent.contextProviders.add(direct);
    await register(mode, api, [
      { id: "Satellite", instance: { register(scoped) { scoped.agent.contextProviders.add(broken); throw new Error("Registration failed."); } } },
      { id: "Independent", instance: { register(scoped) { scoped.agent.contextProviders.add(healthy); } } }
    ]);
    expect((await collectAgentContext(api, { route: "/resource" }, "explain")).attachments.map(item => item.id)).toEqual(["direct", "healthy"]);
    expect(broken.availability).not.toHaveBeenCalled(); expect(broken.collect).not.toHaveBeenCalled();
    expect(healthy.collect).toHaveBeenCalledTimes(1); expect(direct.collect).toHaveBeenCalledTimes(1);
    expect(api.diagnostics.list().map(item => item.status)).toEqual(["failed", "loaded"]);
  });

  it.each(modes)("keeps satellite resource ownership and callback receivers intact (%s)", async mode => {
    const api = registry(); const source = provider("satellite");
    const supports = vi.fn(function (this: unknown) { expect(this).toBe(renderer); return true; });
    const renderer = { id: "renderer", moduleId: "Resource", displayName: "Resource result", resultTypes: ["resource.result"], supports, component: () => null };
    await register(mode, api, [{ id: "Satellite", instance: { register(scoped) {
      scoped.agent.contextProviders.add(source); scoped.agent.resultRenderers.add(renderer);
    } } }]);
    expect(api.agent.contextProviders.list()[0]).toBe(source);
    expect(api.agent.resultRenderers.list().find(item => presentsResourceModule(item, "Resource") && item.supports?.({ proposal: {}, result: {} }))).toBe(renderer);
    expect(supports).toHaveBeenCalledTimes(1);
    await collectAgentContext(api, { route: "/resource" }, "explain");
    expect(source.collect).toHaveBeenCalledTimes(1);
    retireAgentModuleRegistration(api, "Satellite");
    expect(api.agent.contextProviders.list()).toEqual([]); expect(api.agent.resultRenderers.list()).toEqual([]);
    expect(source.moduleId).toBe("Resource"); expect(renderer.moduleId).toBe("Resource");
  });

  it.each(modes)("does not expose a registering module before its awaited registration completes (%s)", async mode => {
    const api = registry(); const source = provider("pending"); const entered = deferred(); const release = deferred();
    const operation = register(mode, api, [{ id: "Pending", instance: { async register(scoped) {
      scoped.agent.contextProviders.add(source); entered.resolve(); await release.promise;
    } } }]);
    await entered.promise;
    try {
      expect(api.agent.contextProviders.list()).toEqual([]); expect(source.availability).not.toHaveBeenCalled();
    } finally { release.resolve(); await operation; }
    expect(api.agent.contextProviders.list()).toEqual([source]);
    expect(source.availability).toHaveBeenCalledTimes(1);
  });

  it.each(modes)("retirement wins over pending completion and captured late additions (%s)", async mode => {
    const api = registry(); const source = provider("late"); const entered = deferred(); const release = deferred();
    let captured!: ElsaStudioModuleApi; const later = vi.fn();
    const operation = register(mode, api, [
      { id: "Pending", instance: { async register(scoped) { captured = scoped; entered.resolve(); await release.promise; scoped.agent.contextProviders.add(source); } } },
      { id: "Next", instance: { register: later } }
    ]);
    await entered.promise; retireAgentRegistryGeneration(api);
    release.resolve(); await operation;
    captured.agent.contextProviders.add(provider("later"));
    expect(api.agent.contextProviders.list()).toEqual([]); expect(captured.agent.contextProviders.list()).toEqual([]);
    expect(isAgentContributionCurrent(captured, source)).toBe(false);
    expect(source.availability).not.toHaveBeenCalled(); expect(later).not.toHaveBeenCalled();
  });

  it("preserves independent provenance when a module reuses an object in a different registry", async () => {
    const first = registry(); const second = registry(); const source = provider("shared");
    await register("instances", first, [{ id: "Satellite", instance: { register(scoped) { scoped.agent.contextProviders.add(source); throw new Error("First generation failed."); } } }]);
    await register("instances", second, [{ id: "Satellite", instance: { register(scoped) { scoped.agent.contextProviders.add(source); } } }]);
    expect(first.agent.contextProviders.list()).toEqual([]); expect(source.availability).not.toHaveBeenCalled();
    expect((await collectAgentContext(second, { route: "/resource" }, "explain")).attachments.map(item => item.id)).toEqual(["shared"]);
    retireAgentRegistryGeneration(first);
    expect(second.agent.contextProviders.list()).toEqual([source]); expect(source.collect).toHaveBeenCalledTimes(1);
  });

  it("does not let a second origin overwrite an already failed origin for the same object", async () => {
    const api = registry(); const source = provider("shared"); const independent = provider("independent");
    await register("instances", api, [
      { id: "Failed", instance: { register(scoped) { scoped.agent.contextProviders.add(source); throw new Error("Failed."); } } },
      { id: "Second", instance: { register(scoped) { scoped.agent.contextProviders.add(source); scoped.agent.contextProviders.add(independent); } } }
    ]);
    expect((await collectAgentContext(api, { route: "/resource" }, "explain")).attachments.map(item => item.id)).toEqual(["independent"]);
    expect(source.availability).not.toHaveBeenCalled(); expect(source.collect).not.toHaveBeenCalled();
    expect(independent.collect).toHaveBeenCalledTimes(1);
  });

  it("binds an API assembled with the public SDK factory without treating direct sources as discovered modules", async () => {
    const agent = createAgentRegistry();
    const api = { ...registry(), agent, ai: { dispatchPrompt: agent.dispatchPrompt, onPrompt: agent.onPrompt, publishPromptResult: agent.publishPromptResult, onPromptResult: agent.onPromptResult } };
    const direct = provider("direct"); const broken = provider("broken");
    api.agent.contextProviders.add(direct);
    await register("legacy", api, [{ id: "Failed", instance: { register(scoped) { scoped.agent.contextProviders.add(broken); throw new Error("Failed."); } } }]);
    expect(direct.availability).not.toHaveBeenCalled(); expect(broken.availability).not.toHaveBeenCalled();
    expect((await collectAgentContext(api, { route: "/resource" }, "explain")).attachments.map(item => item.id)).toEqual(["direct"]);
    expect(broken.availability).not.toHaveBeenCalled(); expect(broken.collect).not.toHaveBeenCalled();
  });

  it("withdraws an old registration before a legacy reload fails compatibility", async () => {
    const api = registry(); const source = provider("old");
    await register("legacy", api, [{ id: "Satellite", instance: { register(scoped) { scoped.agent.contextProviders.add(source); } } }]);
    await loadStudioModules([{ ...manifest("Satellite"), requiredHostVersion: "^99.0.0" }], api, { hostVersion: "1.0.0", sdkVersion: "1.0.0", importModule: async () => { throw new Error("Must not import."); } });
    expect(api.agent.contextProviders.list()).toEqual([]); expect(source.availability).not.toHaveBeenCalled();
    expect(api.diagnostics.list().map(item => item.status)).toEqual(["loaded", "incompatible"]);
  });

  it("preserves fatal connection registration failure and retires earlier Weaver sources", async () => {
    const api = registry(); const earlier = provider("earlier"); const broken = provider("broken"); const later = vi.fn();
    await expect(register("instances", api, [
      { id: "Earlier", instance: { register(scoped) { scoped.agent.contextProviders.add(earlier); } } },
      { id: "Connection", instance: {
        connection: { hostContext: api.host, hostIdentity: { status: "anonymous", roles: [], permissions: [] },
          getSnapshot: () => ({ active: null, loading: false, error: null }), subscribe: () => () => {}, initialize: async () => {}, qualifyPath: () => null },
        register(scoped) { scoped.agent.contextProviders.add(broken); throw new Error("Connection registration failed."); }
      } },
      { id: "Later", instance: { register: later } }
    ])).rejects.toThrow("Connection registration failed.");
    expect(api.agent.contextProviders.list()).toEqual([]);
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
