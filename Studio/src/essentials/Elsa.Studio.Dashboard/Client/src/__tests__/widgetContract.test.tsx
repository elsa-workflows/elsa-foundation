import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider, AuthProvider, homeWidgetIds, registerHomeWidget, type AuthProviderManager, type AuthSession } from "@elsa-workflows/studio-sdk";
import { HomePage } from "../HomePage";
import { DashboardPreferenceStore, reconcileDashboardPreferences } from "../dashboardPreferences";
import { HomeLayoutStore, homeLayoutDefaultRoute, homeLayoutLocalStoragePrefix, homeLayoutRoute, reconcileHomeLayout, type HomeLayoutDocument } from "../homeLayout";
import { register } from "../module";
import { flush, seedHomeLayout, stubApi, waitUntil, widget } from "./testSupport";

describe("dashboard widget contract", () => {
  afterEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it("keeps unregistered Home widget ids in the saved layout", () => {
    const result = reconcileHomeLayout({
      widgets: [
        { id: "home.recent-runs", size: "full", visible: true },
        { id: "weather.forecast", size: "medium", visible: true }
      ]
    }, [widget({ id: "home.recent-runs", title: "Recent runs", defaultSize: "full", supportedSizes: ["full"] })]);
    expect(result.widgets.map(entry => entry.id)).toEqual(["home.recent-runs", "weather.forecast"]);
    expect(result.widgets[1]).toMatchObject({ id: "weather.forecast", size: "medium", visible: true });
  });

  it("registers Home routes and core widgets through registerHomeWidget", () => {
    const api = stubApi();
    register(api);
    expect(api.navigation.list().map(item => item.path)).toEqual(["/dashboard", "/dashboards", "/questions"]);
    expect(api.navigation.list().map(item => item.label)).toEqual(["Home", "Dashboards", "Questions"]);
    expect(api.routes.list().map(item => item.path)).toEqual(["/", "/dashboard", "/overview", "/dashboards", "/dashboards/new", "/dashboards/:resourceId", "/questions", "/questions/new", "/questions/:resourceId"]);
    expect(api.routes.list().filter(item => item.id.startsWith("saved-")).every(item =>
      item.requiresBackend?.some(requirement => requirement.id === "elsa.api.studio-dashboard" && requirement.contractVersion === "1"))).toBe(true);
    expect(api.homeWidgets.list().map(item => item.id)).toEqual([
      homeWidgetIds.getStarted,
      homeWidgetIds.quickActions,
      homeWidgetIds.recentWorkflows,
      homeWidgetIds.recentRuns,
      homeWidgetIds.needsAttention,
      homeWidgetIds.tasks
    ]);
  });

  it("normalizes S/M/L sizes on registerHomeWidget", () => {
    const api = stubApi();
    registerHomeWidget(api, {
      id: "custom.tile",
      moduleId: "test",
      title: "Custom",
      defaultSize: "S",
      supportedSizes: ["S", "M", "L"],
      component: () => null
    });
    expect(api.homeWidgets.list()[0]).toMatchObject({ defaultSize: "small", supportedSizes: ["small", "medium", "full"] });
  });

  it("frames a loader-less contribution and passes default settings to its body", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget({ component: ({ settings }) => <span>Body {(settings as { range: string }).range}</span> }));
    seedHomeLayout(api, ["workflows.runs"]);
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AnonymousAuthProvider><HomePage api={api} /></AnonymousAuthProvider>));
    await flush();
    expect(container.textContent).toContain("Workflow runs");
    expect(container.textContent).toContain("Body 7d");
    expect(container.querySelector("article.dashboard-widget")).not.toBeNull();
    flushSync(() => root.unmount());
  });

  it("exposes a data-backed widget's configured stale time", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget({
      cacheLifetimeMs: 60_000,
      load: async () => ({ total: 12 }),
      component: ({ snapshot }) => <span>Total {(snapshot as { total?: number } | undefined)?.total}</span>
    }));
    seedHomeLayout(api, ["workflows.runs"]);
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AnonymousAuthProvider><HomePage api={api} /></AnonymousAuthProvider>));
    await waitUntil(() => container.textContent?.includes("Total 12") === true);
    expect(container.textContent).toContain("Total 12");
    expect(container.querySelector("footer")?.textContent).toContain("stale");
    flushSync(() => root.unmount());
  });

  it("times out even when a contribution consumes the abort without rejecting", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget({
      timeoutMs: 10,
      load: ({ signal }) => new Promise(resolve => {
        signal.addEventListener("abort", () => resolve({ ignoredAbort: true }), { once: true });
      }),
      component: () => <span>Late widget body</span>
    }));
    seedHomeLayout(api, ["workflows.runs"]);
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AnonymousAuthProvider><HomePage api={api} /></AnonymousAuthProvider>));

    await waitUntil(() => container.textContent?.includes("The widget timed out.") === true);
    expect(container.textContent).not.toContain("Late widget body");
    flushSync(() => root.unmount());
  });

  it("renders a contribution's empty result with the host-owned empty state", async () => {
    const api = stubApi();
    const Body = vi.fn(() => <span>Module body</span>);
    api.dashboardWidgets.add(widget({
      load: async () => [],
      isEmpty: snapshot => Array.isArray(snapshot) && snapshot.length === 0,
      emptyState: { title: "No workflow runs", description: "Runs will appear after the first dispatch." },
      component: Body
    }));
    seedHomeLayout(api, ["workflows.runs"]);
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AnonymousAuthProvider><HomePage api={api} /></AnonymousAuthProvider>));

    await waitUntil(() => container.textContent?.includes("No workflow runs") === true);
    expect(container.textContent).toContain("Runs will appear after the first dispatch.");
    expect(container.querySelector(".dashboard-widget-empty")?.getAttribute("role")).toBe("status");
    expect(Body).not.toHaveBeenCalled();
    flushSync(() => root.unmount());
  });

  it("clears scoped snapshot data when the Dashboard surface unmounts", async () => {
    const api = stubApi();
    const load = vi.fn(async () => ({ total: 12 }));
    api.dashboardWidgets.add(widget({ cacheLifetimeMs: 60_000, load, component: () => null }));
    seedHomeLayout(api, ["workflows.runs"]);

    const firstRoot = createRoot(document.createElement("div"));
    flushSync(() => firstRoot.render(<AnonymousAuthProvider><HomePage api={api} /></AnonymousAuthProvider>));
    await waitUntil(() => load.mock.calls.length === 1);
    flushSync(() => firstRoot.unmount());

    const secondRoot = createRoot(document.createElement("div"));
    flushSync(() => secondRoot.render(<AnonymousAuthProvider><HomePage api={api} /></AnonymousAuthProvider>));
    await waitUntil(() => load.mock.calls.length === 2);
    flushSync(() => secondRoot.unmount());
  });

  it("retains unknown preferences and repairs unsupported sizes and invalid settings", () => {
    const result = reconcileDashboardPreferences({ refreshIntervalMs: 300_000, autoAddNewWidgets: true, widgets: [
      { id: "workflows.runs", visible: true, size: "full", settingsSchemaVersion: 1, settings: { range: "bad" } },
      { id: "missing.widget", visible: false, size: "small" }
    ] }, [widget()]);
    expect(result.widgets).toEqual([
      { id: "workflows.runs", visible: true, size: "wide", settingsSchemaVersion: 1, settings: { range: "7d" } },
      { id: "missing.widget", visible: false, size: "small" }
    ]);
  });

  it("purges known unauthorized widget identities while retaining unloaded modules", () => {
    const result = reconcileDashboardPreferences({ refreshIntervalMs: 300_000, autoAddNewWidgets: true, widgets: [
      { id: "workflows.runs", visible: true, size: "wide" },
      { id: "secrets.attention", visible: true, size: "wide", settings: { secretScope: "production" } },
      { id: "missing.widget", visible: false, size: "small", settings: { retained: true } }
    ] }, [widget()], [], undefined, new Set(["secrets.attention"]));

    expect(result.widgets.map(entry => entry.id)).toEqual(["workflows.runs", "missing.widget"]);
    expect(result.widgets.find(entry => entry.id === "missing.widget")?.settings).toEqual({ retained: true });
  });

  it("removes revoked widget metadata from the next canonical preference write", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget());
    api.dashboardWidgets.add(widget({
      id: "secrets.attention",
      title: "Secrets attention",
      permissions: ["secrets.read"],
      settings: {
        schemaVersion: 1,
        defaults: { secretScope: "all" },
        descriptors: [],
        validate: value => value as { secretScope: string }
      }
    }));
    const stored = {
      source: "user" as const,
      namespace: "home-layout",
      schemaVersion: 1,
      revision: "rev-1",
      value: {
        widgets: [
          { id: "workflows.runs", visible: true, size: "wide" as const },
          { id: "secrets.attention", visible: true, size: "wide" as const, options: { secretScope: "production" } },
          { id: "missing.widget", visible: false, size: "small" as const, options: { retained: true } }
        ]
      }
    };
    const getJson = vi.fn(async () => stored);
    const putJson = vi.fn(async (_url, body) => ({ ...stored, value: (body as { value: typeof stored.value }).value, revision: "rev-2" }));
    const http = api.backend.http as unknown as { getJson: typeof getJson; putJson: typeof putJson };
    http.getJson = getJson;
    http.putJson = putJson;
    const manager = authenticatedManager({ permissions: [] });
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AuthProvider manager={manager}><HomePage api={api} /></AuthProvider>));

    await waitUntil(() => container.querySelector("article") != null);
    expect(container.textContent).toContain("Workflow runs");
    expect(container.textContent).not.toContain("Secrets attention");
    flushSync(() => root.unmount());
  });

  it("lets host policy force a personally hidden widget visible", () => {
    const result = reconcileDashboardPreferences({ refreshIntervalMs: 300_000, autoAddNewWidgets: false, widgets: [
      { id: "workflows.runs", visible: false, size: "wide" }
    ] }, [widget()], ["workflows.runs"]);
    expect(result.widgets[0].visible).toBe(true);
  });

  it("keeps a newly discovered widget hidden when automatic add is disabled", () => {
    const result = reconcileDashboardPreferences({ refreshIntervalMs: 300_000, autoAddNewWidgets: false, widgets: [] }, [widget()]);
    expect(result.widgets[0]).toMatchObject({ id: "workflows.runs", visible: false });
  });

  it("resets only the widget whose saved settings cannot migrate", () => {
    const reset: string[] = [];
    const result = reconcileDashboardPreferences({ refreshIntervalMs: 300_000, autoAddNewWidgets: true, widgets: [
      { id: "workflows.runs", visible: true, size: "wide", settingsSchemaVersion: 0, settings: { range: "legacy" } },
      { id: "missing.widget", visible: false, size: "small", settings: { retained: true } }
    ] }, [widget()], [], id => reset.push(id));

    expect(reset).toEqual(["workflows.runs"]);
    expect(result.widgets[0].settings).toEqual({ range: "7d" });
    expect(result.widgets[1]).toMatchObject({ id: "missing.widget", settings: { retained: true } });
  });

  it("serializes canonical preference writes against the latest revision", async () => {
    const api = stubApi();
    const headers: Array<Record<string, string>> = [];
    let revision = 0;
    (api.backend.http as unknown as { putJson: ReturnType<typeof vi.fn> }).putJson = vi.fn(async (_url, body, options) => {
      headers.push((options as { headers: Record<string, string> }).headers);
      await new Promise(resolve => setTimeout(resolve, 1));
      return { namespace: "dashboard", schemaVersion: 1, revision: `rev-${++revision}`, value: (body as { value: unknown }).value };
    });
    const store = new DashboardPreferenceStore(api, { studioHostId: "studio", backendBaseUrl: "backend", subjectId: "user", tenantId: "tenant" });
    const document = { namespace: "dashboard", schemaVersion: 1, value: { refreshIntervalMs: 300_000 as const, autoAddNewWidgets: true, widgets: [] } };

    await Promise.all([store.save(document), store.save({ ...document, value: { ...document.value, autoAddNewWidgets: false } })]);

    expect(headers).toEqual([
      expect.objectContaining({ "If-None-Match": "*" }),
      expect.objectContaining({ "If-Match": '"rev-1"' })
    ]);
  });

  it("isolates device-local fallback by tenant and host scope", async () => {
    const values = new Map<string, string>();
    vi.stubGlobal("localStorage", { getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => values.set(key, value) });
    const api = stubApi();
    const first = new DashboardPreferenceStore(api, { studioHostId: "studio-a", backendBaseUrl: "backend", subjectId: "anonymous", tenantId: "tenant-a" });
    const second = new DashboardPreferenceStore(api, { studioHostId: "studio-a", backendBaseUrl: "backend", subjectId: "anonymous", tenantId: "tenant-b" });
    const document = { namespace: "dashboard", schemaVersion: 1, value: { refreshIntervalMs: 60_000 as const, autoAddNewWidgets: true, widgets: [] } };

    await first.save(document);

    expect((await first.load()).value.refreshIntervalMs).toBe(60_000);
    expect((await second.load()).value.refreshIntervalMs).toBe(300_000);
    vi.unstubAllGlobals();
  });

  it("isolates anonymous Home layouts by tenant and host and resets to the builtin default", async () => {
    const values = new Map<string, string>();
    vi.stubGlobal("localStorage", { getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => values.set(key, value), removeItem: (key: string) => { values.delete(key); } });
    const api = stubApi();
    vi.spyOn(api.backend.http, "getJson");
    const first = new HomeLayoutStore(api, { studioHostId: "studio-a", backendBaseUrl: "backend", subjectId: "anonymous", tenantId: "tenant-a" });
    const second = new HomeLayoutStore(api, { studioHostId: "studio-a", backendBaseUrl: "backend", subjectId: "anonymous", tenantId: "tenant-b" });
    const document = { source: "local" as const, namespace: "home-layout", schemaVersion: 1, value: { widgets: [{ id: "home.recent-runs", size: "full" as const, visible: true }] } };

    await first.save(document);

    expect((await first.load()).value.widgets.map(entry => entry.id)).toEqual(["home.recent-runs"]);
    expect((await second.load()).value.widgets.map(entry => entry.id)).toEqual([
      "home.get-started",
      "workflows.run-health",
      "workflows.portfolio",
      "home.quick-actions",
      "home.needs-attention",
      "home.recent-runs",
      "home.recent-workflows",
      "home.tasks"
    ]);
    await first.reset();
    expect((await first.load()).source).toBe("builtin");
    expect(api.backend.http.getJson).not.toHaveBeenCalled();
    expect(api.backend.http.putJson).not.toHaveBeenCalled();
    expect(api.backend.http.deleteJson).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });

  it("creates a personal Home layout from an inherited default before queuing and advancing personal revisions", async () => {
    const api = stubApi();
    const inherited: HomeLayoutDocument = { source: "default", namespace: "home-layout", schemaVersion: 1, revision: "admin-r7", value: { widgets: [] } };
    const writes: Array<{ value: HomeLayoutDocument["value"]; headers: Record<string, string> }> = [];
    let revision = 0;
    let releaseFirst!: () => void;
    const firstWrite = new Promise<void>(resolve => { releaseFirst = resolve; });
    const getJson = vi.fn(async () => inherited);
    const putJson = vi.fn(async (_url: string, body: { value: HomeLayoutDocument["value"] }, options: { headers: Record<string, string> }) => {
      writes.push({ value: body.value, headers: options.headers });
      if (writes.length === 1) await firstWrite;
      const matches = revision ? options.headers["If-Match"] === `"user-r${revision}"` : options.headers["If-None-Match"] === "*";
      if (!matches) throw Object.assign(new Error("The personal layout precondition failed."), { status: 412 });
      return { ...inherited, source: "user", revision: `user-r${++revision}`, value: body.value };
    });
    Object.assign(api.backend.http, { getJson, putJson });
    const store = new HomeLayoutStore(api, { studioHostId: "studio", backendBaseUrl: "backend", subjectId: "alice", tenantId: "tenant" });
    const loaded = await store.load();
    // Home edits retain the inherited metadata while changing the source to user.
    const edited: HomeLayoutDocument = { ...loaded, source: "user", value: { widgets: [{ id: "home.recent-runs", size: "medium", visible: true }] } };
    const resized: HomeLayoutDocument = { ...edited, value: { widgets: [{ ...edited.value.widgets[0], size: "full" }] } };
    const saves = Promise.all([store.save(edited), store.save(resized)]);
    await waitUntil(() => putJson.mock.calls.length > 0);
    try { expect(putJson).toHaveBeenCalledTimes(1); } finally { releaseFirst(); }
    const saved = await saves;
    expect(saved.map(document => document.revision)).toEqual(["user-r1", "user-r2"]);
    await store.save({ ...edited, value: { widgets: [{ ...edited.value.widgets[0], visible: false }] } });
    expect(writes.map(write => write.headers["If-Match"])).toEqual([undefined, '"user-r1"', '"user-r2"']);
    expect(writes.map(write => write.headers["If-None-Match"])).toEqual(["*", undefined, undefined]);
    expect(writes.map(write => write.value.widgets[0])).toEqual([edited.value.widgets[0], resized.value.widgets[0], { ...edited.value.widgets[0], visible: false }]);
  });

  it("creates a new personal layout after reset returns an inherited default revision", async () => {
    const api = stubApi();
    const personal: HomeLayoutDocument = { source: "user", namespace: "home-layout", schemaVersion: 1, revision: "user-r8", value: { widgets: [] } };
    const inherited: HomeLayoutDocument = { ...personal, source: "default", revision: "admin-r9" };
    const getJson = vi.fn().mockResolvedValueOnce(personal).mockResolvedValue(inherited);
    const putJson = vi.fn(async (_url: string, body: { value: HomeLayoutDocument["value"] }, options: { headers: Record<string, string> }) => {
      if (options.headers["If-None-Match"] !== "*" || options.headers["If-Match"]) throw Object.assign(new Error("No personal layout exists after reset."), { status: 412 });
      return { ...personal, revision: "user-r10", value: body.value };
    });
    Object.assign(api.backend.http, { getJson, putJson });
    const store = new HomeLayoutStore(api, { studioHostId: "studio", backendBaseUrl: "backend", subjectId: "alice", tenantId: "tenant" });
    await store.load();
    const reset = await store.reset();
    expect(reset).toMatchObject({ source: "default", revision: "admin-r9" });
    const saved = await store.save({ ...reset, source: "user", value: { widgets: [{ id: "home.recent-runs", size: "medium", visible: true }] } });
    expect(saved).toMatchObject({ source: "user", revision: "user-r10" });
    expect(api.backend.http.deleteJson).toHaveBeenCalledWith(homeLayoutRoute, { headers: { "X-Elsa-Studio-Host-Id": "studio" } });
  });

  it("keeps reset authoritative when reloading falls back to a stale device document", async () => {
    const api = stubApi();
    const scope = { studioHostId: "studio", backendBaseUrl: "backend", subjectId: "alice", tenantId: "tenant" };
    const device: HomeLayoutDocument = { source: "local", namespace: "home-layout", schemaVersion: 1, revision: "old-user-r4", value: { widgets: [{ id: "home.recent-runs", size: "full", visible: true }] } };
    localStorage.setItem(`${homeLayoutLocalStoragePrefix}${encodeURIComponent(JSON.stringify(scope))}`, JSON.stringify(device));
    const getJson = vi.fn().mockRejectedValue(new Error("The reload is offline."));
    const putJson = vi.fn(async (_url: string, body: { value: HomeLayoutDocument["value"] }, options: { headers: Record<string, string> }) => {
      if (options.headers["If-None-Match"] !== "*" || options.headers["If-Match"]) throw Object.assign(new Error("The reset deleted the personal layout."), { status: 412 });
      return { ...device, source: "user", revision: "user-r5", value: body.value };
    });
    Object.assign(api.backend.http, { getJson, putJson });
    const store = new HomeLayoutStore(api, scope);
    const reset = await store.reset();
    expect(reset).toMatchObject({ source: "local", revision: "old-user-r4" });
    const saved = await store.save({ ...reset, source: "user" });
    expect(saved).toMatchObject({ source: "user", revision: "user-r5", value: device.value });
  });

  it("retains create preconditions while an inherited Home edit falls back to device storage", async () => {
    const api = stubApi();
    const scope = { studioHostId: "studio", backendBaseUrl: "backend", subjectId: "alice", tenantId: "tenant" };
    const inherited: HomeLayoutDocument = { source: "default", namespace: "home-layout", schemaVersion: 1, revision: "admin-r2", value: { widgets: [] } };
    const getJson = vi.fn().mockResolvedValueOnce(inherited).mockRejectedValue(new Error("Offline"));
    const putJson = vi.fn().mockRejectedValueOnce(new Error("Offline")).mockImplementation(async (_url: string, body: { value: HomeLayoutDocument["value"] }, options: { headers: Record<string, string> }) => {
      if (options.headers["If-None-Match"] !== "*" || options.headers["If-Match"]) throw Object.assign(new Error("The personal layout still does not exist."), { status: 412 });
      return { ...inherited, source: "user", revision: "user-r1", value: body.value };
    });
    Object.assign(api.backend.http, { getJson, putJson });
    const store = new HomeLayoutStore(api, scope);
    const loaded = await store.load();
    const value: HomeLayoutDocument["value"] = { widgets: [{ id: "home.recent-runs", size: "full", visible: false }] };
    const device = await store.save({ ...loaded, source: "user", value });
    expect(device).toMatchObject({ source: "local", value });
    expect(await store.load()).toEqual(device);
    const saved = await store.save({ ...device, source: "user" });
    expect(saved).toMatchObject({ source: "user", revision: "user-r1", value });
  });

  it("uses a supplied personal revision when saving through a store that has not loaded", async () => {
    const api = stubApi();
    const personal: HomeLayoutDocument = { source: "user", namespace: "home-layout", schemaVersion: 1, revision: "user-r4", value: { widgets: [{ id: "home.recent-runs", size: "full", visible: true }] } };
    const getJson = vi.fn();
    const putJson = vi.fn(async (_url: string, body: { value: HomeLayoutDocument["value"] }, options: { headers: Record<string, string> }) => {
      if (options.headers["If-Match"] !== '"user-r4"' || options.headers["If-None-Match"]) throw Object.assign(new Error("The personal layout already exists."), { status: 412 });
      return { ...personal, revision: "user-r5", value: body.value };
    });
    Object.assign(api.backend.http, { getJson, putJson });
    const store = new HomeLayoutStore(api, { studioHostId: "studio", backendBaseUrl: "backend", subjectId: "alice", tenantId: "tenant" });
    expect(await store.save(personal)).toMatchObject({ source: "user", revision: "user-r5", value: personal.value });
    expect(getJson).not.toHaveBeenCalled();
  });

  it("updates the current server default with compare-and-swap without advancing the personal revision", async () => {
    const api = stubApi();
    const personalDocument = {
      source: "user" as const,
      namespace: "home-layout",
      schemaVersion: 1,
      revision: "user-r1",
      value: { widgets: [{ id: "home.recent-runs", size: "full" as const, visible: true }] }
    };
    let defaultRevision = "default-r1";
    let nextDefaultRevision = 2;
    let conflictNextDefaultWrite = false;
    const defaultReads: string[] = [];
    const writes: Array<{ url: string; body: unknown; headers: Record<string, string> }> = [];
    const getJson = vi.fn(async (url: string) => {
      if (url === homeLayoutRoute) return personalDocument;
      if (url === homeLayoutDefaultRoute) {
        defaultReads.push(defaultRevision);
        return { ...personalDocument, source: "default" as const, revision: defaultRevision };
      }
      throw new Error(`Unexpected route: ${url}`);
    });
    const putJson = vi.fn(async (url: string, body: unknown, options?: unknown) => {
      const headers = (options as { headers: Record<string, string> }).headers;
      writes.push({ url, body, headers });
      if (url === homeLayoutDefaultRoute) {
        if (conflictNextDefaultWrite || headers["If-Match"] !== `"${defaultRevision}"`) {
          conflictNextDefaultWrite = false;
          throw Object.assign(new Error("The default changed."), { status: 412 });
        }
        defaultRevision = `default-r${nextDefaultRevision++}`;
        return { ...personalDocument, source: "default" as const, revision: defaultRevision, value: (body as { value: unknown }).value };
      }
      return { ...personalDocument, revision: "user-r2", value: (body as { value: unknown }).value };
    });
    const http = api.backend.http as unknown as { getJson: typeof getJson; putJson: typeof putJson };
    http.getJson = getJson;
    http.putJson = putJson;
    const store = new HomeLayoutStore(api, {
      studioHostId: "studio",
      backendBaseUrl: "https://backend.example",
      subjectId: "alice",
      tenantId: "tenant-a"
    });

    const loaded = await store.load();
    await store.saveDefault({ ...loaded, value: { widgets: [{ ...loaded.value.widgets[0], size: "wide" }] } });
    await store.saveDefault({ ...loaded, value: { widgets: [{ ...loaded.value.widgets[0], visible: false }] } });

    expect(defaultReads).toEqual(["default-r1", "default-r2"]);
    expect(writes.filter(write => write.url === homeLayoutDefaultRoute).map(write => write.headers["If-Match"])).toEqual([
      '"default-r1"',
      '"default-r2"'
    ]);

    conflictNextDefaultWrite = true;
    await expect(store.saveDefault(loaded)).rejects.toMatchObject({ status: 412 });
    expect(defaultReads).toEqual(["default-r1", "default-r2", "default-r3"]);
    expect(writes.filter(write => write.url === homeLayoutDefaultRoute)).toHaveLength(3);

    await store.save({ ...loaded, value: { widgets: [{ ...loaded.value.widgets[0], size: "medium" }] } });
    const personalWrite = writes.find(write => write.url === homeLayoutRoute);
    expect(personalWrite?.headers["If-Match"]).toBe('"user-r1"');
    expect(personalWrite?.headers["If-None-Match"]).toBeUndefined();
  });
});

function authenticatedManager(overrides: Partial<AuthSession> = {}): AuthProviderManager {
  const session: AuthSession = {
    status: "authenticated",
    subject: "alice",
    tenantId: "tenant-a",
    roles: [],
    permissions: [],
    ...overrides
  };

  return {
    getSession: () => session,
    getCapabilities: async () => ({ ownershipMode: "foundation-owned", providers: [] }),
    initialize: async () => session,
    login: async () => undefined,
    handleCallback: async () => session,
    logout: async () => undefined,
    getAccessToken: async () => "token",
    refresh: async () => session
  };
}
