import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider, AuthProvider, type AuthProviderManager, type AuthSession } from "@elsa-workflows/studio-sdk";
import { DashboardPage } from "../DashboardPage";
import { DashboardPreferenceStore, reconcileDashboardPreferences } from "../dashboardPreferences";
import { register } from "../module";
import { flush, stubApi, waitUntil, widget } from "./testSupport";

describe("dashboard widget contract", () => {
  it("registers the canonical and legacy Dashboard routes", () => {
    const api = stubApi();
    register(api);
    expect(api.navigation.list().map(item => item.path)).toEqual(["/dashboard"]);
    expect(api.routes.list().map(item => item.path)).toEqual(["/", "/dashboard", "/overview"]);
  });

  it("frames a loader-less contribution and passes default settings to its body", async () => {
    const api = stubApi();
    api.dashboardWidgets.add(widget({ component: ({ settings }) => <span>Body {(settings as { range: string }).range}</span> }));
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AnonymousAuthProvider><DashboardPage api={api} /></AnonymousAuthProvider>));
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
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AnonymousAuthProvider><DashboardPage api={api} /></AnonymousAuthProvider>));
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
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AnonymousAuthProvider><DashboardPage api={api} /></AnonymousAuthProvider>));

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
    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<AnonymousAuthProvider><DashboardPage api={api} /></AnonymousAuthProvider>));

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

    const firstRoot = createRoot(document.createElement("div"));
    flushSync(() => firstRoot.render(<AnonymousAuthProvider><DashboardPage api={api} /></AnonymousAuthProvider>));
    await waitUntil(() => load.mock.calls.length === 1);
    flushSync(() => firstRoot.unmount());

    const secondRoot = createRoot(document.createElement("div"));
    flushSync(() => secondRoot.render(<AnonymousAuthProvider><DashboardPage api={api} /></AnonymousAuthProvider>));
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
      namespace: "dashboard",
      schemaVersion: 1,
      revision: "rev-1",
      value: {
        refreshIntervalMs: 300_000 as const,
        autoAddNewWidgets: true,
        widgets: [
          { id: "workflows.runs", visible: true, size: "wide" as const, settingsSchemaVersion: 1, settings: { range: "7d" } },
          { id: "secrets.attention", visible: true, size: "wide" as const, settingsSchemaVersion: 1, settings: { secretScope: "production" } },
          { id: "missing.widget", visible: false, size: "small" as const, settings: { retained: true } }
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
    flushSync(() => root.render(<AuthProvider manager={manager}><DashboardPage api={api} /></AuthProvider>));

    await waitUntil(() => putJson.mock.calls.length === 1);
    const saved = putJson.mock.calls[0][1] as { value: typeof stored.value };
    expect(saved.value.widgets.map(entry => entry.id)).toEqual(["workflows.runs", "missing.widget"]);
    expect(JSON.stringify(saved)).not.toContain("secrets.attention");
    expect(JSON.stringify(saved)).not.toContain("production");
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
