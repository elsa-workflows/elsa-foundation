import React, { useEffect } from "react";
import { AnonymousAuthProvider, type ElsaStudioModuleApi, type StudioDashboardWidgetContribution } from "@elsa-workflows/studio-sdk";
import { ShellFrame, getStudioNavigation } from "../../src/apps/Elsa.Studio.Web/Client/src/app/App";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import { HomePage } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/HomePage";
import { homeLayoutLocalStoragePrefix } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/homeLayout";
import type { StudioNavigationContribution } from "../../src/apps/Elsa.Studio.Web/Client/src/sdk";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Dashboard/Client/src/styles.css";

const searchParams = new URLSearchParams(window.location.search);
const edit = searchParams.get("edit") === "1";
const before = searchParams.get("mode") === "home-before";

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: before ? "Dashboard" : "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "main" },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", order: 30, placement: "main" }
];

function widget(overrides: Partial<StudioDashboardWidgetContribution>): StudioDashboardWidgetContribution {
  return {
    id: "widget",
    moduleId: "Elsa.Studio.Dashboard",
    title: "Widget",
    defaultVisible: true,
    defaultSize: "medium",
    supportedSizes: ["medium", "full"],
    component: () => null,
    ...overrides
  };
}

function createHomeApi(): ElsaStudioModuleApi {
  const items: StudioDashboardWidgetContribution[] = [];
  const widgets = {
    add: (item: StudioDashboardWidgetContribution) => { items.push(item); },
    list: () => items,
    compose: () => [],
    slot: { id: "test", kind: "test", owner: { kind: "host" as const, id: "host" } }
  };
  const notFound = async () => {
    const error = new Error("missing") as Error & { status: number };
    error.status = 404;
    throw error;
  };
  const api = {
    host: { baseUrl: "https://studio.example", hostVersion: "1", sdkVersion: "1", http: {} },
    backend: { baseUrl: "https://backend.example", http: { getJson: notFound, putJson: async (_url: string, body: { value: unknown }) => ({ source: "local", namespace: "home-layout", schemaVersion: 1, value: body.value }), deleteJson: async () => ({}) } },
    runtime: { hostId: "browser-home", dashboard: { defaultRefreshIntervalMs: 300_000, widgetTimeoutMs: 10_000 }, enabledFeatures: ["workflows"] },
    navigation: { add() {}, list: () => [], compose: () => [], slot: widgets.slot },
    routes: { add() {}, list: () => [], compose: () => [], slot: widgets.slot },
    dashboardWidgets: widgets,
    homeWidgets: widgets,
    settingEditors: { add() {}, list: () => [], compose: () => [], slot: widgets.slot }
  } as unknown as ElsaStudioModuleApi;

  api.dashboardWidgets.add(widget({
    id: "home.get-started",
    title: "Get started",
    defaultSize: "full",
    supportedSizes: ["full"],
    core: true,
    component: () => (
      <div className="home-get-started">
        <p className="home-get-started-progress">1 of 3 done</p>
        <ol className="home-get-started-steps">
          <li data-done="true">Create a workflow</li>
          <li data-done="false">Run it</li>
          <li data-done="false">See what happened</li>
        </ol>
        <a className="home-get-started-cta" href="/workflows/instances">Run it</a>
      </div>
    )
  }));
  api.dashboardWidgets.add(widget({
    id: "home.recent-workflows",
    title: "Recent workflows",
    core: true,
    component: () => <ul className="home-widget-list"><li><a href="/workflows/definitions/orders">Orders</a></li></ul>
  }));
  api.dashboardWidgets.add(widget({
    id: "home.needs-attention",
    title: "Needs attention",
    core: true,
    component: () => <p className="home-widget-empty">All clear — nothing needs attention.</p>
  }));
  api.dashboardWidgets.add(widget({
    id: "home.recent-runs",
    title: "Recent runs",
    defaultSize: "full",
    supportedSizes: ["full"],
    core: true,
    component: () => <ul className="home-widget-list"><li><a href="/workflows/instances/run-1">Orders · Completed</a></li></ul>
  }));

  const scope = {
    studioHostId: api.runtime.hostId ?? "default",
    backendBaseUrl: api.backend.baseUrl,
    subjectId: "anonymous",
    tenantId: "default"
  };
  window.localStorage.setItem(`${homeLayoutLocalStoragePrefix}${encodeURIComponent(JSON.stringify(scope))}`, JSON.stringify({
    source: "local",
    namespace: "home-layout",
    schemaVersion: 1,
    value: {
      widgets: [
        { id: "home.get-started", size: "full", visible: true },
        { id: "home.recent-workflows", size: "medium", visible: true },
        { id: "home.needs-attention", size: "medium", visible: true },
        { id: "home.recent-runs", size: "full", visible: true }
      ]
    }
  }));
  return api;
}

export function HomeFixture() {
  writeAppearancePreferences({ themeId: "classic", mode: "light", navigation: "left" });
  const navigation = getStudioNavigation(moduleNavigation);
  const api = createHomeApi();

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", "classic");
    document.documentElement.setAttribute("data-theme-mode", "light");
    document.documentElement.setAttribute("data-theme-appearance", "light");
    if (edit) {
      const editButton = [...document.querySelectorAll("button")].find(item => item.textContent?.includes("Edit home"));
      editButton?.click();
    }
  }, []);

  if (before) {
    return (
      <ThemeProvider>
        <ShellFrame navigation={navigation} panels={[]} path="/dashboard" title="Dashboard" backendBaseUrl="https://backend.example/" onNavigate={() => undefined}>
          <section className="dashboard-page">
            <header className="dashboard-page-header">
              <div><h2>Dashboard</h2><p>Prioritized work and operational summaries from Studio modules.</p></div>
            </header>
            <div className="dashboard-grid">
              <article className="dashboard-widget" data-size="full" aria-labelledby="before-title">
                <header className="dashboard-widget-header">
                  <div><h3 id="before-title">Workflow portfolio</h3></div>
                  <div className="dashboard-widget-actions" aria-label="Workflow portfolio controls">
                    <button type="button">Move</button>
                    <button type="button">Hide</button>
                  </div>
                </header>
                <div className="dashboard-widget-body">Active definitions: 3</div>
              </article>
            </div>
          </section>
        </ShellFrame>
      </ThemeProvider>
    );
  }

  return (
    <ThemeProvider>
      <AnonymousAuthProvider>
        <ShellFrame navigation={navigation} panels={[]} path="/dashboard" title="Home" backendBaseUrl="https://backend.example/" onNavigate={() => undefined}>
          <HomePage api={api} />
        </ShellFrame>
      </AnonymousAuthProvider>
    </ThemeProvider>
  );
}
