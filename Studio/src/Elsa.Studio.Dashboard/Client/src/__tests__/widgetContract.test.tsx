import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider, type ElsaStudioModuleApi, type StudioDashboardWidgetContribution } from "@elsa-workflows/studio-sdk";
import { DashboardPage } from "../DashboardPage";
import { reconcileDashboardPreferences } from "../dashboardPreferences";
import { register } from "../module";

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

  it("lets host policy force a personally hidden widget visible", () => {
    const result = reconcileDashboardPreferences({ refreshIntervalMs: 300_000, autoAddNewWidgets: false, widgets: [
      { id: "workflows.runs", visible: false, size: "wide" }
    ] }, [widget()], ["workflows.runs"]);
    expect(result.widgets[0].visible).toBe(true);
  });
});

function widget(overrides: Partial<StudioDashboardWidgetContribution> = {}): StudioDashboardWidgetContribution {
  return { id: "workflows.runs", moduleId: "Elsa.Studio.Workflows.Dashboard", title: "Workflow runs", defaultVisible: true, defaultSize: "wide", supportedSizes: ["medium", "wide"], settings: {
    schemaVersion: 1, defaults: { range: "7d" }, descriptors: [], validate: value => (value as { range?: string })?.range === "7d" ? value as { range: string } : null
  }, component: () => null, ...overrides };
}

function stubApi(): ElsaStudioModuleApi {
  const makeRegistry = <T,>() => { const items: T[] = []; return { add: (item: T) => items.push(item), list: () => items, compose: () => [], slot: { id: "test", kind: "test", owner: { kind: "host", id: "host" } } }; };
  const notFound = async () => { const error = new Error("missing") as Error & { status: number }; error.status = 404; throw error; };
  return { host: { baseUrl: "https://studio.example", hostVersion: "1", sdkVersion: "1", http: {} }, backend: { baseUrl: "https://backend.example", http: { getJson: notFound, putJson: vi.fn(async (_url, body) => ({ namespace: "dashboard", schemaVersion: 1, value: (body as { value: unknown }).value })) } }, runtime: { hostId: "test", dashboard: { defaultRefreshIntervalMs: 300_000, widgetTimeoutMs: 10_000 } }, navigation: makeRegistry(), routes: makeRegistry(), dashboardWidgets: makeRegistry(), settingEditors: makeRegistry() } as unknown as ElsaStudioModuleApi;
}

async function flush() { await new Promise(resolve => setTimeout(resolve, 0)); await new Promise(resolve => setTimeout(resolve, 0)); }
async function waitUntil(predicate: () => boolean) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Condition was not met before the test timeout.");
}
