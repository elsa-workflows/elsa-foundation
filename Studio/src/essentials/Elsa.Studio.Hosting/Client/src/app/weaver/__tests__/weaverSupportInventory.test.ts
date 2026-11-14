import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import { collectWeaverContext, isWeaverContextCollectionCurrent } from "../weaverContext";
import { beginWeaverModuleRegistration, copyWeaverPromptOrigin, isWeaverPromptRequestCurrent, retireWeaverModuleRegistration, retireWeaverRegistryGeneration } from "../weaverRegistrationOrigins";
import { isStudioSupportSnapshotCurrent, refreshStudioSupportInventory, studioSupportAttachment } from "../weaverSupportInventory";

const contract = { data: { support: { completeness: "complete", contexts: [
  { id: "host-support", moduleId: "Elsa.Weaver", contextKinds: ["weaver.host-support"], schemaVersions: ["1"], admission: "availableHint" }
] } } };
const row = (id: string, status = "available") => ({ id, displayName: id, status });
function registry(host: unknown = { complete: true, modules: [] }, backend: unknown = contract) {
  const api = createStudioRegistry({ ...createEndpointContext("https://studio.example/"), hostVersion: "1", sdkVersion: "1" }, "https://backend.example/");
  vi.spyOn(api.host.http, "getJson").mockResolvedValue(host);
  vi.spyOn(api.backend.http, "getJson").mockResolvedValue(backend);
  return api;
}
const attachment = { id: "sample", source: "Sample", label: "Sample", contentType: "sample", sensitivity: "internal" as const, scope: "screen" };

describe("prompt-time Studio support discovery", () => {
  it("allowlists plain bounded facts and discovers the actual backend contract without forwarding its facts", async () => {
    const canary = `private-${Math.random()}`;
    const api = registry({ complete: true, generatedAt: canary, hostVersion: canary,
      modules: [{ ...row("Sample"), sourceKind: canary, manifest: { entry: canary, styles: [canary], capabilities: [canary] }, diagnostics: [{ reason: canary }], contributions: [{ id: canary }] }] },
    { ...contract, arbitrary: canary });
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content).toEqual({ side: "studio", clientAsserted: true, completeness: "complete", modules: [row("Sample")], registrationCompleteness: "incomplete", loader: [], registrations: [] });
    expect(JSON.stringify(studioSupportAttachment(api, snapshot))).not.toContain(canary);
    expect(api.host.http.getJson).toHaveBeenCalledWith("/_elsa/studio/module-registry", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(api.backend.http.getJson).toHaveBeenCalledWith("/_elsa/weaver/support", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(studioSupportAttachment(api, snapshot)).toMatchObject({ moduleId: "Elsa.Weaver", contentType: "weaver.host-support", schemaVersion: "1", origin: "clientSnapshot", weaverScopes: [] });
  });

  it.each([undefined, false] as const)("does not turn source complete=%s into absence or retirement", async complete => {
    const api = registry({ complete, modules: [] }); const collect = vi.fn(async () => [attachment]);
    const module = beginWeaverModuleRegistration(api, "Sample");
    module.api.weaver.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect }); module.complete();
    const support = await refreshStudioSupportInventory(api);
    const collection = await collectWeaverContext(api, { route: "/unknown" }, "explain", undefined, [], undefined, { support });
    expect(support.content.completeness).toBe(complete === false ? "incomplete" : "unknown");
    expect(collect).toHaveBeenCalledTimes(1); expect(collection.attachments).toContainEqual(attachment);
  });

  it.each(["absent", "disabled", "failed", "incompatible"])("withdraws a %s origin before nested callbacks and preserves a healthy resource owner", async status => {
    const api = registry({ complete: true, modules: [row("Healthy"), ...(status === "absent" ? [] : [row("Satellite", status)])] });
    const removed = vi.fn(async () => [attachment]); const healthy = vi.fn(async () => [attachment]);
    const satellite = beginWeaverModuleRegistration(api, "Satellite");
    satellite.api.weaver.contextProviders.add({ id: "satellite", moduleId: "Resource", displayName: "Satellite", surfaces: ["*"], sensitivity: "internal", collect: removed }); satellite.complete();
    const owner = beginWeaverModuleRegistration(api, "Healthy");
    owner.api.weaver.contextProviders.add({ id: "healthy", moduleId: "Resource", displayName: "Healthy", surfaces: ["*"], sensitivity: "internal", collect: healthy }); owner.complete();
    const support = await refreshStudioSupportInventory(api);
    await collectWeaverContext(api, { route: "/unknown" }, "explain", undefined, [], undefined, { support });
    expect(removed).not.toHaveBeenCalled(); expect(healthy).toHaveBeenCalledTimes(1);
    expect(support.content.registrations).toContainEqual({ kind: "contextProviders", id: "satellite", originModuleId: "Satellite", declaredModuleId: "Resource", status: "unavailable" });
    expect(support.content.registrations).toContainEqual({ kind: "contextProviders", id: "healthy", originModuleId: "Healthy", declaredModuleId: "Resource", status: "available" });
  });

  it("retains opaque custom module identities without deriving an owner from their spelling", async () => {
    const api = registry({ complete: true, modules: [row("@custom/resource-owner")] });
    const module = beginWeaverModuleRegistration(api, "@custom/resource-owner"); module.complete();
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content.modules).toEqual([row("@custom/resource-owner")]); expect(snapshot.content.completeness).toBe("complete");
  });

  it("withholds control-bearing labels without treating omitted modules as absent and preserves readable Unicode", async () => {
    const controls = [...Array.from({ length: 32 }, (_, code) => code), 127];
    const readable = { ...row("Healthy"), displayName: " Shared café 🙂 " };
    const api = registry({ complete: true, modules: [readable, ...controls.map(code => ({ ...row(`module-${code}`), displayName: `label${String.fromCharCode(code)}private` }))] });
    const module = beginWeaverModuleRegistration(api, "Unlisted"); module.complete();
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content.modules).toEqual([readable]);
    expect(snapshot.content.completeness).toBe("incomplete");
    expect(isStudioSupportSnapshotCurrent(api, snapshot)).toBe(true);
    expect(snapshot.content.loader).toContainEqual({ moduleId: "Unlisted", status: "loaded" });
    expect(JSON.stringify(studioSupportAttachment(api, snapshot))).not.toContain("private");
  });

  it.each(["https", "file", "unix", "windows", "relative", "ambiguous-segments"])("withholds %s location-shaped identities and preserves unknown rather than absence", async shape => {
    const canary = `private-${Math.random()}`;
    const identities: Record<string, string> = { https: `https://feed.example/${canary}?token=value`, file: `file:///private/${canary}`,
      unix: `/var/private/${canary}`, windows: `C:\\private\\${canary}`, relative: `../private/${canary}`,
      "ambiguous-segments": `${canary}${"-".repeat(110)}!` };
    const unsafe = identities[shape];
    const api = registry({ complete: true, modules: [row(unsafe), row("Healthy")] });
    const unknown = beginWeaverModuleRegistration(api, unsafe);
    unknown.api.weaver.contextProviders.add({ id: "source", moduleId: unsafe, displayName: "Unsafe owner", surfaces: ["*"], sensitivity: "internal", collect: async () => [] });
    unknown.api.weaver.contextProviders.add({ id: unsafe, displayName: "Unsafe identity", surfaces: ["*"], sensitivity: "internal", collect: async () => [] }); unknown.complete();
    const healthy = beginWeaverModuleRegistration(api, "Healthy");
    healthy.api.weaver.resultRenderers.add({ id: "renderer", moduleId: unsafe, resourceModuleIds: [unsafe], displayName: "Renderer", component: () => null }); healthy.complete();
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content.completeness).toBe("incomplete");
    expect(snapshot.content.registrationCompleteness).toBe("incomplete");
    expect(snapshot.content.modules).toEqual([row("Healthy")]);
    expect(JSON.stringify(studioSupportAttachment(api, snapshot))).not.toContain(canary);
  });

  it.each([
    { complete: true, modules: [row("same"), row("SAME")] },
    { complete: true, modules: [row("good"), row("bad", "future-state")] },
    { complete: true, modules: [{ ...row("bad"), displayName: "x".repeat(257) }] },
    { complete: true, modules: Array.from({ length: 129 }, (_, index) => row(`module-${index}`)) }
  ])("marks malformed or truncated discovery incomplete instead of proving absence", async body => {
    const api = registry(body); const module = beginWeaverModuleRegistration(api, "Unlisted"); module.complete();
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content.completeness).toBe("incomplete");
    expect(isStudioSupportSnapshotCurrent(api, snapshot)).toBe(true);
    expect(snapshot.content.modules.length).toBeLessThanOrEqual(128);
  });

  it("bounds the whole observation and marks every dropped projection incomplete", async () => {
    const api = registry({ complete: true, modules: Array.from({ length: 128 }, (_, index) => ({ ...row(`module-${index}`), displayName: "x".repeat(256) })) });
    const module = beginWeaverModuleRegistration(api, "module-1");
    for (let index = 0; index < 128; index++) module.api.weaver.contextProviders.add({ id: `context-${index}`, moduleId: "resource", displayName: "Context", surfaces: ["*"], sensitivity: "internal", collect: async () => [] });
    module.complete(); const snapshot = await refreshStudioSupportInventory(api);
    expect(JSON.stringify(snapshot.content).length).toBeLessThanOrEqual(24_576);
    expect(snapshot.content.completeness).toBe("incomplete"); expect(snapshot.content.registrationCompleteness).toBe("incomplete");
  });

  it("does not infer registrations from manifest capability hints or attribute direct registrations to an installation", async () => {
    const api = registry({ complete: true, modules: [{ ...row("Installed"), contributions: [{ id: "pretend-tool" }], manifest: { capabilities: ["ai-tools"] } }] });
    api.weaver.contextProviders.add({ id: "unattributed", moduleId: "Resource", displayName: "Direct", surfaces: ["*"], sensitivity: "internal", collect: async () => [] });
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content.registrationCompleteness).toBe("incomplete");
    expect(snapshot.content.registrations).toEqual([{ kind: "contextProviders", id: "unattributed", declaredModuleId: "Resource", status: "available" }]);
    expect(JSON.stringify(snapshot.content)).not.toContain("pretend-tool");
  });

  it("reports pending registrations as incomplete and never invokes their availability callback", async () => {
    const api = registry({ complete: true, modules: [row("Pending")] }); const availability = vi.fn(() => true);
    const pending = beginWeaverModuleRegistration(api, "Pending");
    const source = { id: "pending", displayName: "Pending", surfaces: ["*"], sensitivity: "internal" as const, availability, collect: async () => [] };
    pending.api.weaver.contextProviders.add(source);
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content.registrationCompleteness).toBe("incomplete"); expect(availability).not.toHaveBeenCalled();
  });

  it("distinguishes an unobserved loader from a loaded module that registered no Weaver support", async () => {
    const api = registry({ complete: true, modules: [row("Sample")] });
    const unknown = await refreshStudioSupportInventory(api);
    expect(unknown.content.registrationCompleteness).toBe("incomplete"); expect(unknown.content.loader).toEqual([]);
    beginWeaverModuleRegistration(api, "Sample").complete();
    const loaded = await refreshStudioSupportInventory(api);
    expect(loaded.content.registrationCompleteness).toBe("complete"); expect(loaded.content.loader).toEqual([{ moduleId: "Sample", status: "loaded" }]);
    expect(loaded.content.registrations).toEqual([]);
  });

  it("keeps satellite origin, declared owner and explicit resource presentation scope distinct", async () => {
    const api = registry({ complete: true, modules: [row("Satellite")] });
    const module = beginWeaverModuleRegistration(api, "Satellite");
    module.api.weaver.resultRenderers.add({ id: "result", moduleId: "Satellite", resourceModuleIds: ["Resource"], displayName: "Result", component: () => null }); module.complete();
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content.registrations).toEqual([{ kind: "resultRenderers", id: "result", originModuleId: "Satellite", declaredModuleId: "Satellite", resourceModuleIds: ["Resource"], status: "available" }]);
  });

  it.each([
    undefined,
    { data: { support: { completeness: "incomplete", contexts: contract.data.support.contexts } } },
    { data: { support: { completeness: "complete", contexts: [] } } },
    { data: { support: { completeness: "complete", contexts: [{ moduleId: "Other", contextKinds: ["weaver.host-support"], schemaVersions: ["1"] }] } } },
    { data: { support: { completeness: "complete", contexts: [{ moduleId: "Elsa.Weaver", contextKinds: ["weaver.host-support"], schemaVersions: ["2"] }] } } }
  ])("preserves ordinary resource prompts when the current backend does not establish the inventory context contract", async backend => {
    const api = registry({ complete: true, modules: [] }, backend);
    // Explicit undefined means a failed/empty observation, not the helper's default fixture.
    vi.mocked(api.backend.http.getJson).mockResolvedValue(backend);
    api.weaver.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect: async () => [attachment] });
    const support = await refreshStudioSupportInventory(api);
    const collection = await collectWeaverContext(api, { route: "/unknown" }, "explain", undefined, [], undefined, { support });
    expect(collection.attachments).toEqual([attachment]);
    expect(collection.diagnostics).toEqual([expect.objectContaining({ code: "weaver.support.context_unavailable", required: false })]);
  });

  it.each([undefined, "unknown", "withheld", "unchecked", "future"])("preserves resource prompts when a matching source admission is %s", async admission => {
    const api = registry({ complete: true, modules: [] }, { data: { support: { completeness: "complete",
      contexts: [{ ...contract.data.support.contexts[0], admission }] } } });
    api.weaver.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect: async () => [attachment] });
    const support = await refreshStudioSupportInventory(api);
    const collection = await collectWeaverContext(api, { route: "/unknown" }, "explain", undefined, [], undefined, { support });
    expect(support.disclosureSupported).toBe(false);
    expect(collection.attachments).toEqual([attachment]);
    expect(collection.diagnostics).toEqual([expect.objectContaining({ code: "weaver.support.context_unavailable", required: false })]);
  });

  it("does not append support when another matching required source is withheld", async () => {
    const api = registry({ complete: true, modules: [] }, { data: { support: { completeness: "complete", contexts: [
      contract.data.support.contexts[0], { ...contract.data.support.contexts[0], id: "second", admission: "withheld" }
    ] } } });
    const support = await refreshStudioSupportInventory(api);
    expect(support.disclosureSupported).toBe(false);
    expect(studioSupportAttachment(api, support)).toBeUndefined();
  });

  it("does not append support when another matching source cannot admit this schema", async () => {
    const api = registry({ complete: true, modules: [] }, { data: { support: { completeness: "complete", contexts: [
      contract.data.support.contexts[0], { ...contract.data.support.contexts[0], id: "second", schemaVersions: ["2"] }
    ] } } });
    const support = await refreshStudioSupportInventory(api);
    expect(support.disclosureSupported).toBe(false);
    expect(studioSupportAttachment(api, support)).toBeUndefined();
  });

  it("keeps host failure unknown while accepting a separately known current context contract", async () => {
    const api = registry(); vi.mocked(api.host.http.getJson).mockRejectedValue(new Error(`private-${Math.random()}`));
    const snapshot = await refreshStudioSupportInventory(api);
    expect(snapshot.content.completeness).toBe("unavailable"); expect(snapshot.disclosureSupported).toBe(true);
    expect(studioSupportAttachment(api, snapshot)?.content).toEqual(snapshot.content);
  });

  it.each(["backend", "host", "generation", "session", "identity", "disposed"])("rejects an old %s read before any module callback can run", async changed => {
    const api = registry(); let finish!: (value: unknown) => void;
    Object.assign(api, { backendSession: { key: "first", identity: {}, disposed: false } });
    vi.mocked(api.host.http.getJson).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const operation = refreshStudioSupportInventory(api); const refusal = expect(operation).rejects.toThrow("Module context changed");
    await Promise.resolve(); await Promise.resolve();
    if (changed === "generation") retireWeaverRegistryGeneration(api);
    else if (changed === "session") Object.assign(api, { backendSession: { key: "second", identity: {}, disposed: false } });
    else if (changed === "identity") Object.assign(api.backendSession!, { identity: {} });
    else if (changed === "disposed") Object.assign(api.backendSession!, { disposed: true });
    else Object.assign(api, { [changed]: createEndpointContext(`https://${changed}-replacement.example/`) });
    finish({ complete: true, modules: [] }); await refusal;
  });

  it("invalidates a collected observation if unrelated registration changes before the actual send boundary", async () => {
    const api = registry(); const support = await refreshStudioSupportInventory(api);
    const collection = await collectWeaverContext(api, { route: "/unknown" }, "explain", undefined, [], undefined, { support });
    expect(isWeaverContextCollectionCurrent(api, collection)).toBe(true);
    beginWeaverModuleRegistration(api, "Late").complete();
    expect(isWeaverContextCollectionCurrent(api, collection)).toBe(false);
  });

  it("preserves private prompt origin through host copies and rejects its withdrawn or foreign registry", () => {
    const api = registry(); const module = beginWeaverModuleRegistration(api, "Sample"); module.complete();
    const request = { message: "Inspect" }; let delivered: unknown;
    api.weaver.onPrompt(prompt => { delivered = prompt; }); module.api.weaver.dispatchPrompt(request);
    expect(delivered).toBe(request);
    const incoming = { ...request, id: "incoming" }; copyWeaverPromptOrigin(request, incoming);
    const turn = { ...incoming, message: incoming.message.trim() }; copyWeaverPromptOrigin(incoming, turn);
    expect(isWeaverPromptRequestCurrent(api, turn)).toBe(true); expect(isWeaverPromptRequestCurrent(registry(), turn)).toBe(false);
    retireWeaverModuleRegistration(api, "Sample");
    expect(isWeaverPromptRequestCurrent(api, turn)).toBe(false);
    expect(isWeaverPromptRequestCurrent(api, { message: "Root request" })).toBe(true);
  });

  it("refreshes discovery for the next prompt instead of reusing the first successful observation", async () => {
    const api = registry({ complete: true, modules: [row("Sample")] }); const collect = vi.fn(async () => [attachment]);
    const module = beginWeaverModuleRegistration(api, "Sample");
    module.api.weaver.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect }); module.complete();
    const first = await refreshStudioSupportInventory(api);
    const earlier = await collectWeaverContext(api, { route: "/unknown" }, "explain", undefined, [], undefined, { support: first });
    vi.mocked(api.host.http.getJson).mockResolvedValue({ complete: true, modules: [row("Sample", "disabled")] });
    const second = await refreshStudioSupportInventory(api);
    await collectWeaverContext(api, { route: "/unknown" }, "explain", undefined, [], undefined, { support: second });
    expect(api.host.http.getJson).toHaveBeenCalledTimes(2); expect(api.backend.http.getJson).toHaveBeenCalledTimes(2);
    expect(collect).toHaveBeenCalledTimes(1); expect(isWeaverContextCollectionCurrent(api, earlier)).toBe(false);
  });

  it("ends discovery independently of two uncooperative transports on cancellation", async () => {
    const api = registry(); const controller = new AbortController();
    vi.mocked(api.host.http.getJson).mockImplementation(() => new Promise(() => {}));
    vi.mocked(api.backend.http.getJson).mockImplementation(() => new Promise(() => {}));
    const operation = refreshStudioSupportInventory(api, controller.signal); const refusal = expect(operation).rejects.toMatchObject({ name: "AbortError" });
    controller.abort(); await refusal;
  });

  it("settles timed-out discovery without accepting a late transport result", async () => {
    vi.useFakeTimers(); const api = registry(); let finish!: (value: unknown) => void;
    vi.mocked(api.host.http.getJson).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    try {
      const operation = refreshStudioSupportInventory(api, undefined, { timeoutMs: 100 });
      await vi.advanceTimersByTimeAsync(100); const snapshot = await operation;
      expect(snapshot.content.completeness).toBe("unavailable");
      finish({ complete: true, modules: [row("Late")] }); await Promise.resolve();
      expect(snapshot.content.modules).toEqual([]);
    } finally { vi.useRealTimers(); }
  });
});
