import { desiredFeatureState } from "./weaverInventorySelection";
import { expect, type Page, type TestInfo } from "@playwright/test";
import { dashboardBackend, attachDashboardJson, type Capture } from "./dashboardJourneySupport";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";

export const weaverInventoryJourneys = {
  baseline: "Weaver shipped inventory and structured logs deselection",
  enabled: "Weaver shipped inventory with ExtensionBuilder enabled"
};
export const weaverSemanticJourneys = {
  baseline: "Weaver external protocol support and current route",
  enabled: "Weaver external protocol support after root restart"
};

type Feature = { id: string; enabled: boolean; runtimeAvailable: boolean; configuration: unknown };
type Catalog = { revision: string; features: Feature[] };
type Registry = { complete: boolean; modules: { id: string; status: string }[] };
type Support = { completeness: string; tools: unknown[]; contexts: unknown[] };
/** Only real HTTP, current module discovery and the shipped bootstrap/support APIs supply these facts. */
export async function weaverInventoryJourney(page: Page, info: TestInfo, capture: Capture) {
  return shippedJourney(page, info, capture, false);
}

/** Separate opt-in protocol observation; never substitutes for native unavailable-provider evidence. */
export async function weaverSemanticJourney(page: Page, info: TestInfo, capture: Capture) {
  return shippedJourney(page, info, capture, true);
}

async function shippedJourney(page: Page, info: TestInfo, capture: Capture, semantic: boolean) {
  const enabled = process.env.UX_WEAVER_PHASE === "enabled";
  const backend = await dashboardBackend(page.request);
  const exchanged = await page.request.get(`${backend.base}/_elsa/identity/token`);
  expect(exchanged.ok()).toBe(true);
  const { accessToken } = await exchanged.json() as { accessToken: string };
  expect(typeof accessToken).toBe("string");
  const observations: unknown[] = [];
  const network: { side: string; path: string; method: string; status: number }[] = [];
  const failedRequests: string[] = [];
  const turns: unknown[] = [];
  const messagePosts: unknown[] = [];
  const onMessage = (request: import("@playwright/test").Request) => {
    if (semantic && request.method() === "POST" && /\/_elsa\/weaver\/sessions\/[^/]+\/messages$/.test(new URL(request.url()).pathname))
      messagePosts.push({ path: new URL(request.url()).pathname, body: request.postDataJSON() });
  };
  page.on("request", onMessage);
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
  const ask = async (state: string, route: string, before: { status: string; shell: string; generation: number }) => {
    // The peer receives only the actual shipped SDK prompt, never these native expectations.
    const nonce = `semantic-${crypto.randomUUID()}`;
    const question = `What support is installed for modules structured-logs, extension-builder and absent-module?${state === "unknown" ? " What is known about the current route?" : ""} nonce=${nonce}`;
    const composer = page.getByRole("textbox", { name: "Ask Weaver", exact: true });
    const assistant = page.locator('.weaver-message[data-role="assistant"]');
    const prior = await assistant.evaluateAll(items => items.map(item => item.getAttribute("data-message-id")));
    await expect(composer).toBeEnabled();
    await composer.fill(question);
    const responsePromise = page.waitForResponse(response => response.request().method() === "POST"
      && /\/_elsa\/weaver\/sessions\/[^/]+\/messages$/.test(new URL(response.url()).pathname));
    await page.getByRole("button", { name: "Send message", exact: true }).click();
    const response = await responsePromise;
    expect(response.ok()).toBe(true);
    const request = response.request().postDataJSON() as Record<string, unknown>;
    expect(request.message).toBe(question); expect(request.content).toBe(question);
    const path = new URL(response.url()).pathname;
    const sessionId = decodeURIComponent(path.split("/").at(-2)!);
    const accepted = (await response.json() as { data: { message: { id: string; sessionId: string; requestId: string; content: string; context: unknown[] }; warnings: unknown[] } }).data;
    expect(accepted.message.sessionId).toBe(sessionId);
    expect(accepted.message.requestId).toBe(request.requestId);
    expect(accepted.message.content).toBe(question);
    expect(accepted.warnings).toEqual([]);
    await expect(assistant).toHaveCount(prior.length + 1, { timeout: 30_000 });
    const terminal = assistant.last();
    await expect(terminal).toHaveAttribute("data-status", "completed", { timeout: 45_000 });
    const assistantId = await terminal.getAttribute("data-message-id");
    expect(prior).not.toContain(assistantId);
    const rendered = await terminal.locator(".weaver-markdown").innerText();
    const detail = await server<{ data: { sessionId: string; messages: { id: string; role: string; content: string; status: string }[] } }>(`/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}`);
    expect(detail.data.sessionId).toBe(sessionId);
    const recorded = detail.data.messages.filter(item => item.id === assistantId);
    expect(recorded).toHaveLength(1);
    expect(recorded[0]).toMatchObject({ role: "assistant", status: "completed", content: rendered });
    const after = await server<{ status: string; shell: string; generation: number }>("/health/ready");
    expect(after).toEqual(before);
    const surface = request.activeSurface as { route?: string; resourceType?: string; resourceId?: string };
    expect(surface.route).toBe(route);
    if (state === "unknown") {
      expect(surface.resourceType ?? null).toBeNull(); expect(surface.resourceId ?? null).toBeNull();
      expect((request.contextAttachments as { contentType: string }[]).every(item => item.contentType === "weaver.host-support")).toBe(true);
      expect(accepted.message.context.every(item => (item as { contentType: string }).contentType === "weaver.host-support")).toBe(true);
    }
    const user = page.locator('.weaver-message[data-role="user"]').last();
    await expect(user.locator(".weaver-message-text")).toHaveText(question);
    const userDomId = await user.getAttribute("data-message-id");
    const dialogueCaptures: Record<string, { pngHash: string; ariaHash: string; messageListAria: string; messages: unknown[] }> = {};
    turns.push({ state, route, nonce, question, sessionId, request, accepted, assistantId, userDomId, rendered,
      recorded: recorded[0], before, after, dialogueCaptures });
    const answer = terminal.locator(".weaver-markdown");
    const scrollHeight = await page.locator(".weaver-surface .weaver-scroll").evaluate(element => element.clientHeight);
    expect(scrollHeight).toBeGreaterThan(0);
    // Real scrolling only: bounded start/end frames must cover the answer without editing its DOM or style.
    expect(await answer.evaluate(element => element.getBoundingClientRect().height)).toBeLessThanOrEqual(2 * scrollHeight);
    expect(await user.evaluate(element => element.getBoundingClientRect().height)).toBeLessThanOrEqual(scrollHeight);
    for (const [part, element, block] of [["question", user, "start"], ["answer-start", answer, "start"], ["answer-end", answer, "end"]] as const) {
      await element.evaluate((node, position) => node.scrollIntoView({ block: position, behavior: "instant" }), block);
      const messages = await page.locator(".weaver-surface .weaver-message").evaluateAll(elements => elements.map(element => ({
        id: element.getAttribute("data-message-id"), role: element.getAttribute("data-role"), status: element.getAttribute("data-status"),
        content: (element.querySelector(".weaver-markdown, .weaver-message-text") as HTMLElement | null)?.innerText ?? ""
      })));
      const name = `semantic-${part}-${state}`;
      const messageListAria = await page.locator(".weaver-surface .weaver-messages").ariaSnapshot();
      await capture(page, info, name);
      dialogueCaptures[name] = { messages, messageListAria,
        pngHash: createHash("sha256").update(readFileSync(info.outputPath(`${name}.png`))).digest("hex"),
        ariaHash: createHash("sha256").update(readFileSync(info.outputPath(`${name}.aria.yml`))).digest("hex") };
    }
    // Failure conversation frames remain private; only the runner can admit their unchanged bytes after correlation.
    await page.getByRole("button", { name: "Close assistant", exact: true }).click();
    await expect(composer).not.toBeVisible();
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
      question: semantic ? { status: "see-private-turn" } : { text: question, status: unavailable ? "unanswered-provider-unavailable" : "unverified-not-submitted" },
      disclosure: semantic ? { status: "see-private-admission" } : { status: "not-observed", reason: unavailable ? "provider-unavailable" : "not-submitted" } });
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
    expect(bootstrap.data).toMatchObject(semantic ? { enabled: true, providerStatus: "available" } : { enabled: false, providerStatus: "unavailable" });
    const launcher = page.getByRole("button", { name: "Weaver", exact: true });
    await expect(launcher).toBeEnabled();
    const composer = page.getByRole("textbox", { name: "Ask Weaver", exact: true });
    if (!await composer.isVisible()) { await launcher.focus(); await launcher.press("Enter"); }
    if (semantic) {
      await ask(state, route, readiness);
      await capture(page, info, `semantic-${state}`);
    } else {
      await expect(page.getByTestId("weaver-provider-status")).toContainText("Weaver is unavailable");
      await expect(composer).toBeDisabled();
      await expect(page.getByRole("button", { name: "Send message", exact: true })).toBeDisabled();
      await capture(page, info, `inventory-${state}`);
    }
  };
  try {
    if (!enabled) {
      await observe("baseline", "/workflows/definitions");
      const disabled = await backend.response("/_elsa/extension-builder/capabilities");
      expect(disabled.status()).toBe(404);
      observations.push({ kind: "root-extension", enabled: false, status: disabled.status() });
      await apply("backend", { DiagnosticsStructuredLogsEntityFrameworkCore: false, DiagnosticsStructuredLogs: false });
      await apply("studio", { StructuredLogsStudio: false });
      if (semantic) {
        expect(new URL(page.url()).pathname).toBe("/workflows/definitions");
        await observe("deselected-list", "/workflows/definitions");
      }
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
    page.off("response", onResponse); page.off("requestfailed", onFailed); page.off("request", onMessage);
    await attachDashboardJson(info, semantic ? "weaver-semantic.json" : "weaver-inventory.json", { source: process.env.UX_SOURCE_SHA,
      studioSource: process.env.UX_STUDIO_SOURCE_SHA, phase: enabled ? "enabled" : "baseline", observations, network,
      failedRequests, providerEvidence: semantic ? "external-protocol-observation" : "not-observed",
      storyAnswers: semantic ? "machine protocol only; live reasoning unverified" : "unanswered-provider-unavailable", t045Acceptance: "incomplete",
      excluded: semantic ? ["live model reasoning", "independent UX acceptance", "whole T045", "effects/review/result UX"]
        : ["independent module nested removal belongs to the packaged probe", "live providers", "Studio loader/context and admitted/provider disclosure are not observed while the provider is unavailable", "effects/review/result UX belongs to its assigned probe journeys"],
      ...(semantic ? { turns, messagePosts } : {}), completed });
  }
}
