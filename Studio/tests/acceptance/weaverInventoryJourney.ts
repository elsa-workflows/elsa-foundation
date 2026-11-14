import { expect, type Page, type TestInfo } from "@playwright/test";
import { existsSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dashboardBackend, attachDashboardJson, type Capture } from "./dashboardJourneySupport";

export const weaverInventoryJourneys = {
  baseline: "Weaver shipped inventory and structured logs deselection",
  enabled: "Weaver shipped inventory with ExtensionBuilder enabled"
};

type Feature = { id: string; enabled: boolean; runtimeAvailable: boolean; configuration: unknown };
type Catalog = { revision: string; features: Feature[] };
type Registry = { complete: boolean; modules: { id: string; status: string }[] };
type Attachment = { id: string; contentType: string; origin: string; content: unknown };
type Support = { completeness: string; tools: unknown[]; contexts: unknown[] };
type Protocol = { evidenceKind: string; kind: string; data: { commandId: string; prompt?: string; toolCount?: number; tools?: unknown[] } };

/** Only real HTTP, current module discovery and actual admitted/provider payloads supply these facts. */
export async function weaverInventoryJourney(page: Page, info: TestInfo, capture: Capture) {
  const enabled = process.env.UX_WEAVER_PHASE === "enabled";
  const backend = await dashboardBackend(page.request);
  const exchanged = await page.request.get(`${backend.base}/_elsa/identity/token`);
  expect(exchanged.ok()).toBe(true);
  const { accessToken } = await exchanged.json() as { accessToken: string };
  expect(typeof accessToken).toBe("string");
  const observations: unknown[] = [];
  const network: { side: string; path: string; method: string; status: number }[] = [];
  const failedRequests: string[] = [];
  let completed = false;
  const onResponse = (response: import("@playwright/test").Response) => {
    const url = new URL(response.url());
    if (url.origin !== new URL(backend.base).origin && url.origin !== new URL(page.url()).origin) return;
    if (!/weaver|modularity|module-registry|extension-builder/.test(url.pathname)) return;
    network.push({ side: url.origin === new URL(backend.base).origin ? "backend" : "studio", path: url.pathname,
      method: response.request().method(), status: response.status() });
  };
  page.on("response", onResponse);
  const onFailed = (request: import("@playwright/test").Request) => {
    const path = new URL(request.url()).pathname;
    // Navigation intentionally closes the old session's long-lived event stream.
    if (!path.endsWith("/stream") && /weaver|modularity|module-registry/.test(path)) failedRequests.push(path);
  };
  page.on("requestfailed", onFailed);
  const host = async <T>(path: string, data?: unknown): Promise<T> => {
    // Studio's ordinary bridge scheme requires the native session bearer; a backend cookie alone is insufficient.
    const headers = { Authorization: `Bearer ${accessToken}` };
    const response = data === undefined ? await page.request.get(path, { headers }) : await page.request.post(path, { data, headers });
    network.push({ side: "studio", path, method: data === undefined ? "GET" : "POST", status: response.status() });
    expect(response.ok(), `Studio native ${path}: HTTP ${response.status()}`).toBe(true);
    return response.json() as Promise<T>;
  };
  const server = async <T>(path: string, method = "GET", data?: unknown): Promise<T> => {
    const response = await backend.response(path, method, data);
    network.push({ side: "backend", path, method, status: response.status() });
    expect(response.ok(), `Backend native ${path}: HTTP ${response.status()}`).toBe(true);
    return response.json() as Promise<T>;
  };
  const selected = (catalog: Catalog, id: string, value: boolean) => {
    const rows = catalog.features.filter(feature => feature.id === id);
    expect(rows, `Exactly one discovered feature ${id}`).toHaveLength(1);
    expect(rows[0].enabled, `${id} selection`).toBe(value);
    expect(rows[0].runtimeAvailable, `${id} runtime descriptor`).toBe(true);
    return rows[0];
  };
  const module = (registry: Registry, id: string, status: string) => {
    expect(registry.complete, "Unknown discovery cannot establish module absence").toBe(true);
    expect(registry.modules.filter(row => row.id === id)).toEqual([expect.objectContaining({ id, status })]);
  };
  const apply = async (side: "studio" | "backend", states: Record<string, boolean>) => {
    const catalog = side === "studio" ? await host<Catalog>("/modularity/features") : await server<Catalog>("/modularity/features");
    const features = Object.entries(states).map(([id, value]) => {
      const current = selected(catalog, id, !value);
      return { id, enabled: value, configuration: current.configuration };
    });
    const body = { revision: catalog.revision, features };
    const result = side === "studio" ? await host<{ catalog: Catalog }>("/modularity/features/apply", body)
      : await server<{ catalog: Catalog }>("/modularity/features/apply", "POST", body);
    for (const [id, value] of Object.entries(states)) selected(result.catalog, id, value);
    observations.push({ kind: "selection", side, beforeRevision: catalog.revision, afterRevision: result.catalog.revision,
      states, unrelatedConfigurationSubmitted: false });
  };
  const observe = async (state: string, route: string) => {
    const registry = await host<Registry>("/_elsa/studio/module-registry");
    const studioCatalog = await host<Catalog>("/modularity/features");
    const backendCatalog = await server<Catalog>("/modularity/features");
    const support = await server<{ data: { policyScope: string; support: Support } }>("/_elsa/weaver/support");
    expect(support.data.support.completeness).toBe("complete");
    selected(studioCatalog, "StructuredLogsStudio", state === "baseline");
    selected(backendCatalog, "DiagnosticsStructuredLogs", state === "baseline");
    selected(backendCatalog, "DiagnosticsStructuredLogsEntityFrameworkCore", state === "baseline");
    selected(studioCatalog, "ExtensionBuilderStudio", enabled);
    module(registry, "structured-logs", state === "baseline" ? "available" : "disabled");
    module(registry, "extension-builder", enabled ? "available" : "disabled");
    const fixture = process.env.UX_WEAVER_COMMAND;
    let disclosure: { request: unknown; admitted: Attachment[]; provider: Protocol[] } | undefined;
    if (fixture) {
      selected(backendCatalog, "AcmeCopilotProtocolFixture", true);
      selected(backendCatalog, "WeaverGitHubCopilot", true);
      const native = await host<{ registry: { modules: { packageId: string; version: string; status: string }[] } }>("/_elsa/studio/backend-management/registry");
      expect(native.registry.modules.filter(row => row.packageId === "Acme.Weaver.ProtocolFixtures")).toEqual([
        expect.objectContaining({ version: process.env.UX_WEAVER_FIXTURE_VERSION, status: "loaded" })
      ]);
      observations.push({ kind: "native-package-admission", state,
        modules: native.registry.modules.map(({ packageId, version, status }) => ({ packageId, version, status })) });
      const commandId = `${info.project.name}-${state}`;
      const temporary = `${fixture}.next`;
      writeFileSync(temporary, JSON.stringify({ commandId, mode: "observe" }), { mode: 0o600 });
      renameSync(temporary, fixture);
      const launcher = page.getByRole("button", { name: "Weaver", exact: true });
      await expect(launcher).toBeEnabled(); // Ready-shell marker; never navigate through the temporary shell.
      const composer = page.getByRole("textbox", { name: "Ask Weaver", exact: true });
      if (!await composer.isVisible()) { await launcher.focus(); await launcher.press("Enter"); }
      await expect(composer).toBeEditable();
      await composer.fill(`Observe current module support: ${state}.`);
      const sent = page.waitForResponse(response => response.request().method() === "POST" && /\/_elsa\/weaver\/sessions\/[^/]+\/messages$/.test(new URL(response.url()).pathname));
      await page.getByRole("button", { name: "Send message", exact: true }).click();
      const response = await sent; expect(response.ok()).toBe(true);
      const accepted = await response.json() as { data: { message: { context: Attachment[] } } };
      const admitted = accepted.data.message.context;
      const studio = admitted.find(item => item.id === "weaver.studio-support");
      const server = admitted.find(item => item.id === "weaver.backend-support");
      expect(studio).toMatchObject({ origin: "clientSnapshot", contentType: "weaver.host-support",
        content: { side: "studio", clientAsserted: true, completeness: "complete", registrationCompleteness: "complete" } });
      expect(server).toMatchObject({ origin: "authoritativeRead", contentType: "weaver.host-support", content: { side: "backend" } });
      const projected = studio!.content as Registry & { loader: { moduleId: string; status: string }[]; registrations: { originModuleId: string }[] };
      module(projected, "structured-logs", state === "baseline" ? "available" : "disabled");
      module(projected, "extension-builder", enabled ? "available" : "disabled");
      expect(projected.loader.some(item => item.moduleId === "workflows" && item.status === "loaded")).toBe(true);
      if (state !== "baseline") expect(projected.registrations.filter(item => item.originModuleId === "structured-logs")).toEqual([]);
      if (state === "unknown") {
        expect(admitted.filter(item => item.contentType !== "weaver.host-support"), "Unknown route offers no resource or module context").toEqual([]);
      }
      const tracePath = process.env.UX_WEAVER_TRACE!;
      const trace = () => existsSync(tracePath) ? readFileSync(tracePath, "utf8").trim().split("\n").filter(Boolean).map(line => JSON.parse(line) as Protocol)
        .filter(item => item.data.commandId === commandId) : [];
      await expect.poll(() => trace().filter(item => item.kind === "copilot.observation").length).toBe(1);
      const provider = trace();
      expect(provider.every(item => item.evidenceKind === "deterministic-provider-protocol")).toBe(true);
      expect(provider.map(item => item.kind)).toEqual(["copilot.request", "copilot.observation"]);
      const submission = response.request().postDataJSON() as { activeSurface: { route: string; resourceId?: string; resourceType?: string } };
      if (state === "unknown") {
        expect(submission.activeSurface.route).toBe(route);
        expect(submission.activeSurface.resourceId ?? null).toBeNull();
        expect(submission.activeSurface.resourceType ?? null).toBeNull();
        expect(provider[0].data.tools).toEqual([]);
      }
      const prompt = provider[0].data.prompt!;
      // Compare typed contents at the actual adapter boundary; the fixture's fixed answer has no semantic oracle.
      const contents = prompt.split("\n").filter(line => line.startsWith("  Content: ")).map(line => JSON.parse(line.slice(11)) as unknown);
      expect(contents).toContainEqual(studio!.content); expect(contents).toContainEqual(server!.content);
      await expect(page.locator('.weaver-message[data-role="assistant"]').last()).toContainText("Observed the supplied context and tool declarations. No tool was requested.");
      await expect(composer).toBeEditable();
      await composer.fill("A new explicit observation remains available.");
      await expect(page.getByRole("button", { name: "Send message", exact: true })).toBeEnabled();
      await composer.fill("");
      disclosure = { request: submission, admitted, provider };
    }
    observations.push({ kind: "inventory", state, route, side: { studio: new URL(page.url()).origin, backend: backend.base },
      registry, studioFeatures: studioCatalog.features.map(({ id, enabled, runtimeAvailable }) => ({ id, enabled, runtimeAvailable })),
      backendFeatures: backendCatalog.features.map(({ id, enabled, runtimeAvailable }) => ({ id, enabled, runtimeAvailable })),
      support: support.data, disclosure: disclosure ?? { status: "blocked", reason: "No same-source independently packed provider fixture was supplied" } });
    await capture(page, info, `inventory-${state}`);
  };
  try {
    if (process.env.UX_WEAVER_COMMAND) {
      // Ordinary Nuplane admission/reload may finish after the initial root readiness observation.
      await expect.poll(async () => (await server<Catalog>("/modularity/features")).features
        .some(row => row.id === "AcmeCopilotProtocolFixture" && row.enabled && row.runtimeAvailable), { timeout: 60_000 }).toBe(true);
      await page.reload();
      await expect(page.getByRole("button", { name: "Weaver", exact: true })).toBeEnabled();
    }
    if (!enabled) {
      await observe("baseline", "/workflows/definitions");
      const disabled = await backend.response("/_elsa/extension-builder/capabilities");
      expect(disabled.status()).toBe(404);
      observations.push({ kind: "root-extension", enabled: false, status: disabled.status() });
      await apply("backend", { DiagnosticsStructuredLogsEntityFrameworkCore: false, DiagnosticsStructuredLogs: false });
      await apply("studio", { StructuredLogsStudio: false });
      await page.goto("/diagnostics/structured-logs");
      await expect(page.getByRole("button", { name: "Weaver", exact: true })).toBeEnabled();
      await observe("deselected", "/diagnostics/structured-logs");
      const unknown = "/qualification-unknown-module";
      await page.goto(unknown);
      await expect(page.getByRole("button", { name: "Weaver", exact: true })).toBeEnabled();
      await observe("unknown", unknown);
    } else {
      // The runner has already restarted the root backend with Elsa:ExtensionBuilder:Enabled=true.
      // This API selects only Studio; shell Apply cannot register root services or map root endpoints.
      await apply("studio", { ExtensionBuilderStudio: true });
      await page.goto("/extension-builder");
      await expect(page.getByRole("heading", { name: "Extension Builder", exact: true })).toBeVisible();
      const registered = await backend.response("/_elsa/extension-builder/capabilities");
      expect([401, 403], "Normal bearer auth does not supply the root management credential").toContain(registered.status());
      observations.push({ kind: "root-extension", enabled: true, status: registered.status(), authority: "not granted" });
      await observe("enabled", "/extension-builder");
    }
    expect(failedRequests).toEqual([]);
    completed = true;
  } finally {
    page.off("response", onResponse); page.off("requestfailed", onFailed);
    await attachDashboardJson(info, "weaver-inventory.json", { source: process.env.UX_SOURCE_SHA,
      studioSource: process.env.UX_STUDIO_SOURCE_SHA, phase: enabled ? "enabled" : "baseline", observations, network,
      failedRequests, providerEvidence: process.env.UX_WEAVER_COMMAND ? "deterministic-provider-protocol" : "blocked",
      excluded: ["independent module nested removal belongs to the packaged probe", "live providers", "effects/review/result UX belongs to its assigned probe journeys"],
      completed });
  }
}
