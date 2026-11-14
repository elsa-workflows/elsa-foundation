import { desiredFeatureState } from "./weaverInventorySelection";
import { expect, type Page, type TestInfo } from "@playwright/test";
import { dashboardBackend, attachDashboardJson, type Capture } from "./dashboardJourneySupport";

export const weaverInventoryJourneys = {
  baseline: "Weaver shipped inventory and structured logs deselection",
  enabled: "Weaver shipped inventory with ExtensionBuilder enabled"
};

type Feature = { id: string; enabled: boolean; runtimeAvailable: boolean; configuration: unknown };
type Catalog = { revision: string; features: Feature[] };
type Registry = { complete: boolean; modules: { id: string; status: string }[] };
type Support = { completeness: string; tools: unknown[]; contexts: unknown[] };
/** Only real HTTP, current module discovery and the shipped bootstrap/support APIs supply these facts. */
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
  const recordNative = (side: string, path: string, value: unknown) => {
    if (!value || typeof value !== "object") return;
    const body = value as { features?: Feature[]; complete?: boolean; modules?: { id: string; status: string }[] };
    if (path === "/modularity/features" && Array.isArray(body.features)) observations.push({ kind: "native-features", side,
      features: body.features.map(({ id, enabled, runtimeAvailable }) => ({ id, enabled, runtimeAvailable })) });
    if (path === "/_elsa/studio/module-registry" && Array.isArray(body.modules)) observations.push({ kind: "native-modules", side,
      complete: body.complete, modules: body.modules.map(({ id, status }) => ({ id, status })) });
  };
  const recordFailure = (side: string, path: string, status: number) => {
    if (status < 400) return;
    observations.push({ kind: "native-failure", side, path, status,
      category: status === 401 ? "unauthenticated" : status === 403 ? "forbidden" : status === 409 ? "conflict"
        : status === 400 ? "invalid-input" : status === 503 ? "unavailable" : "http-failure" });
  };
  const host = async <T>(path: string, data?: unknown): Promise<T> => {
    // Studio's ordinary bridge scheme requires the native session bearer; a backend cookie alone is insufficient.
    const headers = { Authorization: `Bearer ${accessToken}` };
    const response = data === undefined ? await page.request.get(path, { headers }) : await page.request.post(path, { data, headers });
    network.push({ side: "studio", path, method: data === undefined ? "GET" : "POST", status: response.status() });
    recordFailure("studio", path, response.status());
    expect(response.ok(), `Studio native ${path}: HTTP ${response.status()}`).toBe(true);
    const value = await response.json() as T; recordNative("studio", path, value); return value;
  };
  const server = async <T>(path: string, method = "GET", data?: unknown): Promise<T> => {
    const response = await backend.response(path, method, data);
    network.push({ side: "backend", path, method, status: response.status() });
    recordFailure("backend", path, response.status());
    expect(response.ok(), `Backend native ${path}: HTTP ${response.status()}`).toBe(true);
    const value = await response.json() as T; recordNative("backend", path, value); return value;
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
    for (const [id, value] of Object.entries(states)) selected(catalog, id, !value);
    const features = desiredFeatureState(catalog.features, states);
    const body = { revision: catalog.revision, features };
    const result = side === "studio" ? await host<{ catalog: Catalog }>("/modularity/features/apply", body)
      : await server<{ catalog: Catalog }>("/modularity/features/apply", "POST", body);
    for (const [id, value] of Object.entries(states)) selected(result.catalog, id, value);
    for (const retained of catalog.features.filter(feature => feature.enabled && !Object.hasOwn(states, feature.id))) {
      const returned = selected(result.catalog, retained.id, true);
      expect(returned.configuration, "Unrelated native configuration and masked secrets must survive Apply").toEqual(retained.configuration);
    }
    observations.push({ kind: "selection", side, beforeRevision: catalog.revision, afterRevision: result.catalog.revision,
      states, retainedEnabledIds: catalog.features.filter(feature => feature.enabled).map(feature => feature.id),
      submittedIds: features.map(feature => feature.id), retainedConfigurationMatched: true });
  };
  const observe = async (state: string, route: string) => {
    const registry = await host<Registry>("/_elsa/studio/module-registry");
    const studioCatalog = await host<Catalog>("/modularity/features");
    const backendCatalog = await server<Catalog>("/modularity/features");
    const support = await server<{ data: { policyScope: string; support: Support } }>("/_elsa/weaver/support");
    const readiness = await server<{ status: string; shell: string; generation: number }>("/health/ready");
    const live = await server<{ status: string }>("/health/live");
    expect(live.status).toBe("live"); expect(readiness.status).toBe("ready");
    const bootstrap = await server<{ data: { enabled: boolean; providerStatus: string } }>("/_elsa/weaver/bootstrap");
    const unavailable = bootstrap.data.enabled === false && bootstrap.data.providerStatus === "unavailable";
    const question = state === "baseline" ? "What support is installed for structured logs and ExtensionBuilder?"
      : state === "unknown" ? "What module owns this unknown route?"
      : state === "enabled" ? "What support is available for ExtensionBuilder now?" : "What support remains for structured logs after deselection?";
    observations.push({ kind: "inventory", state, route, registry,
      studioFeatures: studioCatalog.features.map(({ id, enabled, runtimeAvailable }) => ({ id, enabled, runtimeAvailable })),
      backendFeatures: backendCatalog.features.map(({ id, enabled, runtimeAvailable }) => ({ id, enabled, runtimeAvailable })),
      support: support.data, readiness,
      provider: { enabled: bootstrap.data.enabled, providerStatus: bootstrap.data.providerStatus },
      question: { text: question, status: unavailable ? "unanswered-provider-unavailable" : "unverified-not-submitted" },
      disclosure: { status: "not-observed", reason: unavailable ? "provider-unavailable" : "not-submitted" } });
    const packages = await host<{ status: string; registry?: { modules: { packageId: string; version: string; status: string }[] } }>("/_elsa/studio/backend-management/registry");
    observations.push({ kind: "native-packages", state, status: packages.status,
      modules: packages.registry?.modules?.map(({ packageId, version, status }) => ({ packageId, version, status })) ?? [] });
    expect(packages.registry, "Actual selected-backend package registry must be available").toBeTruthy();
    expect(support.data.support.completeness).toBe("complete");
    selected(studioCatalog, "StructuredLogsStudio", state === "baseline");
    selected(backendCatalog, "DiagnosticsStructuredLogs", state === "baseline");
    selected(backendCatalog, "DiagnosticsStructuredLogsEntityFrameworkCore", state === "baseline");
    selected(studioCatalog, "ExtensionBuilderStudio", enabled);
    module(registry, "structured-logs", state === "baseline" ? "available" : "disabled");
    module(registry, "extension-builder", enabled ? "available" : "disabled");
    expect(bootstrap.data).toMatchObject({ enabled: false, providerStatus: "unavailable" });
    const launcher = page.getByRole("button", { name: "Weaver", exact: true });
    await expect(launcher).toBeEnabled();
    const composer = page.getByRole("textbox", { name: "Ask Weaver", exact: true });
    if (!await composer.isVisible()) { await launcher.focus(); await launcher.press("Enter"); }
    await expect(page.getByTestId("weaver-provider-status")).toContainText("Weaver is unavailable");
    await expect(composer).toBeDisabled();
    await expect(page.getByRole("button", { name: "Send message", exact: true })).toBeDisabled();
    await capture(page, info, `inventory-${state}`);
  };
  try {
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
      failedRequests, providerEvidence: "not-observed", storyAnswers: "unanswered-provider-unavailable", t045Acceptance: "incomplete",
      excluded: ["independent module nested removal belongs to the packaged probe", "live providers", "Studio loader/context and admitted/provider disclosure are not observed while the provider is unavailable", "effects/review/result UX belongs to its assigned probe journeys"],
      completed });
  }
}
