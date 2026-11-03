import React, { useEffect, useMemo } from "react";
import {
  AuthProvider,
  type AuthCapabilities,
  type AuthProviderManager,
  type AuthSession,
  type ElsaStudioModuleApi,
  type StudioNavigationContribution,
  type StudioSettingDescriptor
} from "@elsa-workflows/studio-sdk";
import { registerBuiltInSettingEditors } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/shared";
import { ShellFrame, getStudioNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { HomePage } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/HomePage";
import { createHomeLayoutScope, homeLayoutLocalStoragePrefix, homeLayoutRoute } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/homeLayout";
import { registerCoreHomeWidgets } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/widgets/registerCoreHomeWidgets";
import { register as registerWorkflowDashboardWidgets } from "../../../Extensions/Workflows.Dashboard/studio/Elsa.Studio.Workflows.Dashboard/Client/src/module";
import { createWidgetHost } from "./homeFixture";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Dashboard/Client/src/styles.css";

declare global {
  interface Window {
    __homeDashboardRequests?: string[];
    __homeDashboardLayouts?: unknown[];
  }
}

const params = new URLSearchParams(window.location.search);
const state = params.get("state") ?? "ready";
const dark = params.get("theme") === "dark";
const appearance = dark ? "dark" : "light";
const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "main" },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", order: 30, placement: "main" }
];

export function HomeDashboardFixture() {
  const permissions = useMemo(() => state === "denied" ? [] : ["workflows/dashboard:read"], []);
  const manager = useMemo(() => createManager(permissions), [permissions]);
  const api = useMemo(() => createDashboardApi(state, permissions), [permissions]);
  writeAppearancePreferences({ themeId: "classic", mode: appearance, navigation: "left" });
  const navigation = getStudioNavigation(moduleNavigation);

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", "classic");
    document.documentElement.setAttribute("data-theme-mode", appearance);
    document.documentElement.setAttribute("data-theme-appearance", appearance);
  }, []);

  return (
    <ThemeProvider>
      <AuthProvider manager={manager}>
        <ShellFrame navigation={navigation} panels={[]} path="/dashboard" title="Home" backendBaseUrl="https://backend.example/" enabledFeatures={api.runtime.enabledFeatures} onNavigate={() => undefined}>
          <HomePage api={api} />
        </ShellFrame>
      </AuthProvider>
    </ThemeProvider>
  );
}

function createDashboardApi(responseState: string, permissions: string[]): ElsaStudioModuleApi {
  const { api } = createWidgetHost();
  const session = createSession(permissions);
  api.runtime.identity = { subject: session.subject, tenantId: session.tenantId };
  const scope = createHomeLayoutScope(api, session);
  window.localStorage.removeItem(`${homeLayoutLocalStoragePrefix}${encodeURIComponent(JSON.stringify(scope))}`);
  window.__homeDashboardRequests = [];
  window.__homeDashboardLayouts = [];

  const http = api.backend.http;
  http.getJson = async <T,>(url: string): Promise<T> => {
    window.__homeDashboardRequests?.push(url);
    const request = new URL(url, api.backend.baseUrl);
    const path = request.pathname;

    if (path === homeLayoutRoute) throw httpNotFound();
    if (path === "/design/workflows/definitions") return workflowDefinitions as T;
    if (path === "/runtime/workflows/instances/page") {
      const activeIncidents = request.searchParams.get("incidentHealth") === "active";
      return (activeIncidents ? attentionRuns : recentRuns) as T;
    }
    if (path === "/_elsa/workflows/dashboard/definitions") return portfolioSnapshot(responseState) as T;
    if (path === "/_elsa/workflows/dashboard/runs") return runHealthSnapshot(request.searchParams, responseState) as T;

    const definitionMatch = path.match(/^\/design\/workflows\/definitions\/([^/]+)$/);
    if (definitionMatch) {
      const id = decodeURIComponent(definitionMatch[1]);
      const name = definitionNames.get(id);
      if (name) return { id, name } as T;
    }
    throw httpNotFound();
  };
  http.putJson = async <T,>(_url: string, body: unknown): Promise<T> => {
    const value = (body as { value?: unknown }).value;
    window.__homeDashboardLayouts?.push(value);
    return {
      source: "user",
      namespace: "home-layout",
      schemaVersion: 1,
      revision: `browser-rev-${window.__homeDashboardLayouts?.length ?? 1}`,
      value
    } as T;
  };

  registerBuiltInSettingEditors(api);
  registerCoreHomeWidgets(api);
  registerWorkflowDashboardWidgets(api);
  if (responseState === "settings") registerSettingsFixture(api, scope);
  return api;
}

function registerSettingsFixture(api: ElsaStudioModuleApi, scope: ReturnType<typeof createHomeLayoutScope>) {
  const descriptor: StudioSettingDescriptor = {
    name: "message", displayName: "Message", jsonType: "string", required: true,
    secret: false, sensitive: false, restartRequired: false, advanced: false, experimental: false, options: []
  };
  api.dashboardWidgets.add({
    id: "fixture.settings",
    moduleId: "HomeDashboardFixture",
    title: "Fixture settings",
    defaultVisible: true,
    defaultSize: "wide",
    supportedSizes: ["wide"],
    settings: {
      schemaVersion: 1,
      defaults: { message: "not-ready" },
      descriptors: [descriptor],
      validate: value => {
        const message = (value as { message?: unknown } | null)?.message;
        return message === "ready" ? { message } : null;
      }
    },
    isEmpty: () => true,
    emptyState: { title: "This widget needs configuration.", description: "Set the message to make the widget ready." },
    component: () => null
  });

  window.localStorage.setItem(`${homeLayoutLocalStoragePrefix}${encodeURIComponent(JSON.stringify(scope))}`, JSON.stringify({
    source: "local",
    namespace: "home-layout",
    schemaVersion: 1,
    value: { widgets: [{ id: "fixture.settings", size: "wide", visible: true, options: { message: "not-ready" } }] }
  }));
}

function createManager(permissions: string[]): AuthProviderManager {
  const session = createSession(permissions);
  const capabilities: AuthCapabilities = { ownershipMode: "foundation-owned", providers: [] };
  return {
    getSession: () => session,
    getCapabilities: async () => capabilities,
    initialize: async () => session,
    login: async () => undefined,
    handleCallback: async () => session,
    logout: async () => undefined,
    getAccessToken: async () => "browser-home-token",
    refresh: async () => session
  };
}

function createSession(permissions: string[]): AuthSession {
  return {
    status: "authenticated",
    subject: "browser-operator",
    displayName: "Browser Operator",
    tenantId: "browser-tenant",
    roles: ["operator"],
    permissions,
    provider: { id: "browser", kind: "fixture" }
  };
}

const workflowDefinitions = [
  { id: "orders", name: "Orders", latestVersion: "5", publicationState: "Published", lastModifiedAt: "2026-10-18T12:00:00Z" },
  { id: "invoices", name: "Invoices", latestVersion: "2", publicationState: "Published", lastModifiedAt: "2026-10-17T09:00:00Z" }
];
const recentRuns = [
  { workflowExecutionId: "run-orders", definitionId: "orders", status: "Completed", startedAt: "2026-10-19T08:00:00Z", createdAt: "2026-10-19T08:00:00Z" },
  { workflowExecutionId: "run-invoices", definitionId: "invoices", status: "Faulted", activeIncidentCount: 1, startedAt: "2026-10-19T07:00:00Z", createdAt: "2026-10-19T07:00:00Z" }
];
const attentionRuns = [recentRuns[1]];
const definitionNames = new Map(workflowDefinitions.map(definition => [definition.id, definition.name]));

function portfolioSnapshot(responseState: string) {
  if (responseState === "unavailable") return { status: "unavailable", generatedAt: "", errorCode: "DASHBOARD_NOT_INSTALLED" };
  const empty = responseState === "empty";
  return {
    status: "ready",
    generatedAt: new Date().toISOString(),
    activeDefinitionCount: empty ? 0 : 12,
    publishedDefinitionCount: empty ? 0 : 9,
    unpublishedDraftCount: empty ? 0 : 4,
    invalidDraftCount: empty ? 0 : 2
  };
}

function runHealthSnapshot(query: URLSearchParams, responseState: string) {
  if (responseState === "unavailable") return { status: "unavailable", generatedAt: "", errorCode: "DASHBOARD_NOT_INSTALLED" };
  const from = query.get("from") ?? new Date().toISOString();
  const to = query.get("to") ?? new Date().toISOString();
  const bucket = query.get("bucket") === "day" ? "day" : "hour";
  const count = bucket === "hour" ? 24 : Math.max(1, Math.round((Date.parse(to) - Date.parse(from)) / 86_400_000));
  const empty = responseState === "empty";
  const buckets = Array.from({ length: count }, (_, index) => {
    const start = Date.parse(from) + index * (bucket === "hour" ? 3_600_000 : 86_400_000);
    const startedCount = empty ? 0 : bucket === "hour"
      ? (index % 6 === 2 ? 2 : index % 4 === 0 ? 1 : 0)
      : index % 2 === 0 ? 3 : 1;
    const failedCount = startedCount > 0 && index % 6 === 2 ? 1 : 0;
    return {
      from: new Date(start).toISOString(),
      to: new Date(start + (bucket === "hour" ? 3_600_000 : 86_400_000)).toISOString(),
      startedCount,
      failedCount,
      incidentBearingRunCount: failedCount
    };
  });
  const startedCount = buckets.reduce((total, item) => total + item.startedCount, 0);
  const failedCount = buckets.reduce((total, item) => total + item.failedCount, 0);
  const incidentBearingRunCount = buckets.reduce((total, item) => total + item.incidentBearingRunCount, 0);
  const cancelledCount = startedCount > 0 ? 1 : 0;
  const incompleteCount = startedCount > 0 ? 1 : 0;
  return {
    status: "ready",
    generatedAt: to,
    from,
    to,
    timeZone: query.get("timeZone") || "UTC",
    bucket,
    includeTestRuns: query.get("includeTestRuns") === "true",
    startedCount,
    succeededCount: Math.max(0, startedCount - failedCount - cancelledCount - incompleteCount),
    failedCount,
    cancelledCount,
    incompleteCount,
    incidentBearingRunCount,
    incidentCount: failedCount ? failedCount + 1 : 0,
    runningCount: 2,
    failurePercentage: startedCount ? failedCount / startedCount * 100 : 0,
    incidentBearingPercentage: startedCount ? incidentBearingRunCount / startedCount * 100 : 0,
    buckets,
    highestFailureDefinitions: failedCount
      ? [
          { definitionId: "orders", failedCount: Math.max(1, failedCount - 1) },
          ...(failedCount > 1 ? [{ definitionId: "invoices", failedCount: 1 }] : [])
        ]
      : []
  };
}

function httpNotFound() {
  return Object.assign(new Error("Not found"), { status: 404 });
}
