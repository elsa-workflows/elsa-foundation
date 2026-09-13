import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, expect, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry, StudioSlotDefinition } from "@elsa-workflows/studio-sdk";
import { register } from "../module";
import { clearApiCapabilityCache } from "../api/capabilities";
import { workflowInspectorCollapsedStorageKey, workflowInspectorWidthStorageKey, workflowSidePanelMaximizedStorageKey } from "../workflow-editor/constants";

// Shared by module.test.tsx and lazyRouteAnnouncement.test.tsx so neither copies the other's
// route-rendering setup or teardown. `WorkflowManagementPage` (module.tsx) is a module-scope
// `React.lazy`; importing these helpers does not resolve that lazy payload, so
// lazyRouteAnnouncement.test.tsx still renders the definitions route against a fresh, unresolved
// lazy import. See #504.

// Route hosts still attached to the document. A test that fails before its own `await unmount()` would
// otherwise leave a live tree behind that keeps answering window `popstate`, fetches through the next
// test's `fetch` stub, and shadows the next test's elements in document-scoped lookups.
const mountedRouteHosts = new Set<() => Promise<void>>();

// Runs once per test in every file that imports this module. Unmount registered route hosts and
// assert `document.body` is empty first, then reset the other global state (API capability cache,
// stubbed globals, and side-panel/inspector localStorage keys) so later assertions never see a
// leftover live tree.
afterEach(async () => {
  for (const unmount of [...mountedRouteHosts]) await unmount();
  expect(document.body.children, "a test left elements attached to document.body").toHaveLength(0);
  clearApiCapabilityCache();
  vi.unstubAllGlobals();
  window.localStorage.removeItem?.(workflowInspectorCollapsedStorageKey);
  window.localStorage.removeItem?.(workflowInspectorWidthStorageKey);
  window.localStorage.removeItem?.(workflowSidePanelMaximizedStorageKey);
  window.localStorage.clear();
});

export async function renderRegisteredRoute(
  path = "/workflows/definitions",
  configureApi?: (api: ElsaStudioModuleApi) => void,
  followNavigation = false,
  capabilities = capabilityDocument()
) {
  if (typeof ResizeObserver === "undefined") {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
  }
  const fetchWithoutCapabilities = globalThis.fetch;
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "object" && input && "url" in input ? String(input.url) : String(input);
    if (url.includes("/capabilities")) return response(capabilities);
    if (url.includes("/design/activities/definitions/picker")) return response(recommendedActivityPicker());
    return fetchWithoutCapabilities(input, init);
  }));
  window.history.replaceState({}, "", path);
  const api = testApi();
  configureApi?.(api);
  register(api);
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  function RegisteredRouteHost() {
    const [routePath, setRoutePath] = React.useState(() => window.location.pathname);
    React.useEffect(() => {
      if (!followNavigation) return;
      const syncFromLocation = () => setRoutePath(window.location.pathname);
      window.addEventListener("popstate", syncFromLocation);
      return () => window.removeEventListener("popstate", syncFromLocation);
    }, []);
    const route = api.routes.list().find(candidate => candidate.path === routePath) ??
      api.routes.list().find(candidate => routeMatchesPath(candidate.path, routePath)) ??
      api.routes.list()[0];
    const navigate = (nextPath: string) => {
      window.history.pushState({}, "", nextPath);
      if (followNavigation) setRoutePath(window.location.pathname);
      window.dispatchEvent(new PopStateEvent("popstate"));
    };
    const Component = route.component;
    return <Component navigate={navigate} />;
  }

  flushSync(() => {
    root.render(<QueryClientProvider client={queryClient}><RegisteredRouteHost /></QueryClientProvider>);
  });

  const unmount = async () => {
    if (!mountedRouteHosts.delete(unmount)) return;
    flushSync(() => root.unmount());
    queryClient.clear();
    container.remove();
  };
  mountedRouteHosts.add(unmount);

  return { api, container, unmount };
}

function routeMatchesPath(routePath: string, path: string) {
  const routeSegments = routePath.split("/").filter(Boolean);
  const pathSegments = path.split("/").filter(Boolean);
  return routeSegments.length === pathSegments.length &&
    routeSegments.every((segment, index) => segment.startsWith(":") || segment === pathSegments[index]);
}

export function testApi(): ElsaStudioModuleApi {
  const navigation = registry();
  const routes = registry();
  return {
    backend: {
      baseUrl: "https://server.example/",
      http: fetchHttp("https://server.example/")
    },
    runtime: { workflows: {} },
    featureAreas: featureAreaRegistry(navigation, routes),
    navigation,
    routes,
    activityEditors: registry(),
    propertyEditors: registry(),
    expressionEditors: registry(),
    workflowRunInputEditors: registry(),
    workflowDesigner: {
      panels: registry()
    },
    ai: (() => {
      const resultListeners = new Set<(result: unknown) => void>();
      return {
        promptActions: registry(),
        providerAvailability: { get: () => true, set: vi.fn(), subscribe: (listener: (available: boolean) => void) => { listener(true); return () => {}; } },
        dispatchPrompt: vi.fn(),
        onPrompt: vi.fn(() => () => {}),
        publishPromptResult: (result: unknown) => { for (const listener of resultListeners) listener(result); },
        onPromptResult: (listener: (result: unknown) => void) => { resultListeners.add(listener); return () => resultListeners.delete(listener); }
      };
    })(),
    dialogs: {
      confirm: vi.fn(async () => true),
      prompt: vi.fn(async () => null),
      alert: vi.fn(async () => {})
    }
  } as unknown as ElsaStudioModuleApi;
}

function featureAreaRegistry(navigation: ReturnType<typeof registry>, routes: ReturnType<typeof registry>) {
  const featureAreas = registry();

  return {
    ...featureAreas,
    add(featureArea: any) {
      featureAreas.add(featureArea);
      navigation.add({
        id: featureArea.id,
        label: featureArea.nav.title,
        path: featureArea.nav.path,
        activePathPrefix: featureArea.ownedPaths[0],
        order: featureArea.order,
        iconColor: featureArea.nav.iconColor
      });
      for (const item of featureArea.nav.items ?? []) {
        navigation.add({
          id: item.id ?? `${featureArea.id}-${item.title.toLowerCase()}`,
          label: item.title,
          path: item.path,
          parentId: featureArea.id,
          activePathPrefix: item.path,
          iconColor: item.iconColor ?? featureArea.nav.iconColor
        });
      }
      for (const route of featureArea.routes) {
        routes.add(route);
      }
    }
  };
}

function fetchHttp(baseUrl: string) {
  async function requestJson<T>(url: string, init?: RequestInit) {
    const response = await fetch(new URL(url, baseUrl).toString(), init);
    const text = await response.text();
    if (!response.ok) {
      throw new Error(text || `Request failed with ${response.status}.`);
    }
    return (text ? JSON.parse(text) : {}) as T;
  }

  return {
    requestJson,
    getJson: <T,>(url: string, init?: RequestInit) => requestJson<T>(url, { ...init, headers: withHeaders(init?.headers) }),
    postJson: <T,>(url: string, body: unknown, init?: RequestInit) => requestJson<T>(url, {
      ...init,
      method: "POST",
      headers: withHeaders(init?.headers, true),
      body: JSON.stringify(body)
    }),
    putJson: <T,>(url: string, body: unknown, init?: RequestInit) => requestJson<T>(url, {
      ...init,
      method: "PUT",
      headers: withHeaders(init?.headers, true),
      body: JSON.stringify(body)
    }),
    deleteJson: <T,>(url: string, init?: RequestInit) => requestJson<T>(url, {
      ...init,
      method: "DELETE",
      headers: withHeaders(init?.headers)
    }),
    postForm: <T,>(url: string, body: FormData, init?: RequestInit) => requestJson<T>(url, {
      ...init,
      method: "POST",
      headers: withHeaders(init?.headers),
      body
    })
  };
}

function withHeaders(headers?: HeadersInit, json = false) {
  const result = new Headers(headers);
  result.set("Accept", "application/json");
  if (json) result.set("Content-Type", "application/json");
  return result;
}

export function registry<T>(): StudioContributionRegistry<T> {
  const items: T[] = [];
  const slot: StudioSlotDefinition = { id: "test-slot", kind: "test", owner: { kind: "host", id: "test" } };
  return {
    slot,
    add: item => { items.push(item); },
    list: () => [...items],
    compose: () => items.map((contribution, order) => ({
      contribution,
      slot,
      availability: { state: "available" },
      order,
      stableKey: `test-${order}`
    }))
  };
}

export function response(body: unknown, status = 200) {
  return new Response(body ? JSON.stringify(body) : "", {
    status,
    headers: { "Content-Type": "application/json" }
  });
}

export function capabilityDocument() {
  return {
    capabilities: [
      {
        id: "elsa.api.workflow-design",
        contractVersion: "1",
        links: [
          { rel: "workflow-definitions", href: "design/workflows/definitions" },
          { rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true },
          { rel: "workflow-versions", href: "design/workflows/versions/{versionId}", templated: true },
          { rel: "scoped-variable-analysis", href: "design/workflows/scoped-variables/analyze" },
          { rel: "activity-input-options", href: "design/workflows/activities/{activityVersionId}/inputs/{inputName}/options", templated: true }
        ]
      },
      {
        id: "elsa.api.activity-design",
        contractVersion: "1",
        links: [
          { rel: "activity-catalog", href: "design/activities/catalog" },
          { rel: "recommended-activity-definitions", href: "design/activities/definitions/picker" },
          { rel: "activity-availability", href: "design/activities/availability/settings" },
          { rel: "activity-availability-diagnostics", href: "design/activities/availability/diagnostics" }
        ]
      },
      {
        id: "elsa.api.expressions",
        contractVersion: "1",
        links: [
          { rel: "expression-descriptors", href: "expressions/descriptors" },
          { rel: "variable-types", href: "expressions/variable-types" }
        ]
      },
      {
        id: "elsa.api.publishing",
        contractVersion: "1",
        links: [
          { rel: "publication-preflight", href: "publishing/workflows/{versionId}/preflight", templated: true },
          { rel: "workflow-publish", href: "publishing/workflows/{versionId}/publish", templated: true },
          { rel: "publication-record", href: "publishing/publications/{publicationId}", templated: true },
          { rel: "publication-policy", href: "publishing/workflows/{definitionId}/policy", templated: true },
          { rel: "workflow-test-runs", href: "publishing/workflows/{versionId}/test-runs", templated: true },
          { rel: "workflow-draft-test-runs", href: "publishing/workflows/drafts/test-runs" }
        ]
      },
      {
        id: "elsa.api.runtime",
        contractVersion: "1",
        links: [
          { rel: "workflow-executables", href: "runtime/workflows/executables" },
          { rel: "workflow-executable", href: "runtime/workflows/executables/{artifactId}", templated: true },
          { rel: "workflow-executable-provenance", href: "runtime/workflows/executables/{artifactId}/provenance", templated: true },
          { rel: "workflow-execute", href: "runtime/workflows/executables/{artifactId}/execute", templated: true },
          { rel: "workflow-activation-slots", href: "runtime/workflows/activation-slots/{definitionId}", templated: true },
          { rel: "workflow-activation-slot", href: "runtime/workflows/activation-slots/{definitionId}/{slotName}", templated: true },
          { rel: "workflow-instances", href: "runtime/workflows/instances" },
          { rel: "workflow-instance", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true },
          { rel: "activity-execution", href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true },
          { rel: "runtime-diagnostics", href: "runtime/workflows/diagnostics/settings" }
        ]
      }
    ]
  };
}

export const runJavaScriptTypeKey = "Elsa.Workflows.Runtime.JavaScript.Activities.RunJavaScript.Activity";

function recommendedActivityPicker() {
  const candidates = [
    ["activity-1", "Elsa.Activities.Activity"],
    ["write-line-v1", "Elsa.Activities.Primitives.Activities.WriteLine"],
    ["write-line-v1", "Elsa.Activities.WriteLine"],
    ["write-lines-v1", "Elsa.Activities.Primitives.Activities.WriteLines"],
    ["send-email-v1", "Elsa.Activities.SendEmail"],
    ["flowchart-v1", "Elsa.Activities.Flowchart"],
    ["flowchart-v1", "Elsa.Activities.Flowchart.Activities.Flowchart"],
    ["sequence-v1", "Elsa.Activities.Sequence"],
    ["sequence-v1", "Elsa.Activities.Sequence.Activities.Sequence"],
    ["activity-flowchart-v1", "Elsa.Activities.Flowchart.Activities.Flowchart"],
    ["run-javascript-v1", runJavaScriptTypeKey]
  ] as const;
  return {
    items: candidates.map(([versionId, activityTypeKey], index) => ({
      definitionId: `test-definition-${index}`,
      activityTypeKey,
      tenantId: null,
      category: "Tests",
      displayName: activityTypeKey.split(".").at(-1),
      description: null,
      versionId,
      version: "1.0.0",
      isAvailable: true,
      unavailableReason: null
    })),
    nextOffset: null
  };
}

export function definition(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: "definition-1",
    name: "Hello World",
    description: "Writes text.",
    createdAt: "2026-06-18T01:00:00Z",
    lastModifiedAt: "2026-06-18T01:10:00Z",
    deletedAt: null,
    draftId: "draft-1",
    latestVersionId: "version-1",
    latestVersion: "1.0.0",
    versionCount: 1,
    ...overrides
  };
}
