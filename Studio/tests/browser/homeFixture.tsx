import React, { useEffect } from "react";
import { AnonymousAuthProvider, type ElsaStudioModuleApi, type StudioDashboardWidgetContribution } from "@elsa-workflows/studio-sdk";
import { StatusChip } from "@elsa-workflows/studio-ui";
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
const dark = searchParams.get("theme") === "dark";
const appearance = dark ? "dark" : "light";

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
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

function createWidgetHost() {
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
    runtime: { hostId: "browser-home", dashboard: { defaultRefreshIntervalMs: 300_000, widgetTimeoutMs: 10_000 }, enabledFeatures: ["Elsa.Studio.Workflows"] },
    navigation: { add() {}, list: () => [], compose: () => [], slot: widgets.slot },
    routes: { add() {}, list: () => [], compose: () => [], slot: widgets.slot },
    dashboardWidgets: widgets,
    homeWidgets: widgets,
    settingEditors: { add() {}, list: () => [], compose: () => [], slot: widgets.slot }
  } as unknown as ElsaStudioModuleApi;
  return { api, widgets };
}

function seedLayout(api: ElsaStudioModuleApi, widgetIds: string[]) {
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
      widgets: widgetIds.map(id => ({ id, size: id === "home.get-started" ? "full" : "medium", visible: true }))
    }
  }));
}

/** Empty Home at main fbfc347: feature-gated widgets never rendered, so only Get started showed. */
function createEmptyHomeApi(): ElsaStudioModuleApi {
  const { api } = createWidgetHost();
  api.dashboardWidgets.add(widget({
    id: "home.get-started",
    title: "Get started",
    description: "Create a workflow, run it, and see what happened.",
    defaultSize: "full",
    supportedSizes: ["full"],
    core: true,
    component: () => (
      <div className="home-get-started">
        <p className="home-get-started-progress">2 of 3 done</p>
        <ol className="home-get-started-steps">
          <li data-done="true">Create a workflow</li>
          <li data-done="true">Run it</li>
          <li data-done="false">See what happened</li>
        </ol>
        <a className="home-get-started-cta" href="/workflows/instances">See what happened</a>
      </div>
    )
  }));
  seedLayout(api, ["home.get-started"]);
  return api;
}

function createHomeApi(): ElsaStudioModuleApi {
  const { api } = createWidgetHost();

  api.dashboardWidgets.add(widget({
    id: "home.get-started",
    title: "Get started",
    description: "Create a workflow, run it, and see what happened.",
    defaultSize: "full",
    supportedSizes: ["full"],
    core: true,
    component: () => (
      <div className="home-get-started">
        <p className="home-get-started-progress">2 of 3 done</p>
        <ol className="home-get-started-steps">
          <li data-done="true">Create a workflow</li>
          <li data-done="true">Run it</li>
          <li data-done="false">See what happened</li>
        </ol>
        <a className="home-get-started-cta" href="/workflows/instances">See what happened</a>
        <section className="home-docs" aria-labelledby="home-docs-title">
          <h4 id="home-docs-title">Documentation</h4>
          <p>Guides for Studio and your next workflow.</p>
          <ul>
            <li><a href="https://v4.elsaworkflows.io/essentials/quickstart" target="_blank" rel="noreferrer">Try Elsa locally</a></li>
            <li><a href="https://v4.elsaworkflows.io/essentials/first-workflow" target="_blank" rel="noreferrer">Your first workflow</a></li>
            <li><a href="https://v4.elsaworkflows.io/studio" target="_blank" rel="noreferrer">Elsa Studio</a></li>
          </ul>
        </section>
      </div>
    )
  }));
  api.dashboardWidgets.add(widget({
    id: "home.quick-actions",
    title: "Quick actions",
    description: "Create a workflow or open the designer.",
    core: true,
    requiresFeature: "workflows",
    component: () => (
      <ul className="home-quick-actions">
        <li><a href="/workflows/definitions">Create a workflow</a></li>
        <li><a href="/workflows/definitions">Open the designer</a></li>
      </ul>
    )
  }));
  api.dashboardWidgets.add(widget({
    id: "home.recent-workflows",
    title: "Recent workflows",
    description: "The latest workflow definitions.",
    core: true,
    requiresFeature: "workflows",
    component: () => <ul className="home-widget-list"><li><a href="/workflows/definitions/orders"><span>Orders</span></a></li></ul>
  }));
  api.dashboardWidgets.add(widget({
    id: "home.recent-runs",
    title: "Recent runs",
    description: "The latest workflow executions and their status.",
    core: true,
    requiresFeature: "workflows",
    component: () => (
      <ul className="home-widget-list">
        <li>
          <a href="/workflows/instances/run-1">
            <span>Orders</span>
            <StatusChip tone="success">Completed</StatusChip>
          </a>
        </li>
      </ul>
    )
  }));
  api.dashboardWidgets.add(widget({
    id: "home.needs-attention",
    title: "Needs attention",
    description: "Failed, faulted, incident-bearing and long-waiting runs.",
    core: true,
    requiresFeature: "workflows",
    component: () => (
      <div className="home-empty" role="status">
        <strong>All clear</strong>
        <span>Nothing needs attention.</span>
      </div>
    )
  }));
  api.dashboardWidgets.add(widget({
    id: "home.tasks",
    title: "Tasks",
    requiresFeature: "user-tasks",
    core: true,
    component: () => <span>Task inbox</span>
  }));

  seedLayout(api, [
    "home.get-started",
    "home.quick-actions",
    "home.recent-workflows",
    "home.recent-runs",
    "home.needs-attention",
    "home.tasks"
  ]);
  return api;
}

export function HomeFixture() {
  writeAppearancePreferences({ themeId: "classic", mode: appearance, navigation: "left" });
  const navigation = getStudioNavigation(moduleNavigation);
  const api = before ? createEmptyHomeApi() : createHomeApi();

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", "classic");
    document.documentElement.setAttribute("data-theme-mode", appearance);
    document.documentElement.setAttribute("data-theme-appearance", appearance);
    if (edit) {
      const editButton = [...document.querySelectorAll("button")].find(item => item.textContent?.includes("Edit home"));
      editButton?.click();
    }
  }, []);

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
