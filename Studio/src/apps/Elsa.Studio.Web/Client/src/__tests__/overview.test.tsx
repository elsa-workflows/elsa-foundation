import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Diagnostics, getNavigationSection, getStudioNavigation, getTopLevelNavigationItems, isDashboardPath, ShellFrame } from "../app/App";
import { filterAccessibleNavigation, moreToolsStorageKey, type NavigationAccess } from "../app/navigation/placement";
import { ThemeProvider } from "../app/components/ThemeProvider";
import type { ElsaStudioModuleApi, StudioBackendManagementStatus, StudioDiagnosticsWidgetContribution, StudioDiagnosticsWidgetProps, StudioNavigationContribution } from "../sdk";
import { withQueryClient } from "./queryTestUtils";

const mountedTestRoots = new Set<() => void>();

afterEach(() => {
  for (const unmount of [...mountedTestRoots]) unmount();
  window.localStorage.removeItem(moreToolsStorageKey);
  vi.unstubAllGlobals();
});

describe("diagnostics", () => {
  it("lists only host-filtered diagnostic destinations and preserves their labels, routes, and order", async () => {
    const navigation: StudioNavigationContribution[] = [
      { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", order: 10 },
      { id: "open-telemetry", label: "OpenTelemetry", path: "/diagnostics/open-telemetry", parentId: "diagnostics", order: 20, requiresFeature: "otel-studio" },
      { id: "structured-logs", label: "Structured logs", path: "/diagnostics/structured-logs", parentId: "diagnostics", order: 30, requiresPermission: "diagnostics.logs.read" },
      { id: "console", label: "Console", path: "/diagnostics/console", parentId: "diagnostics", order: 35 },
      { id: "custom-audit", label: "Database audit", path: "/diagnostics/audit", parentId: "diagnostics", order: 40 },
      { id: "custom-parent", label: "Restricted diagnostic group", path: "/diagnostics/restricted", parentId: "diagnostics", order: 50, requiresRole: "diagnostics-admin" },
      { id: "custom-child", label: "Private traces", path: "/diagnostics/restricted/traces", parentId: "custom-parent", order: 60 },
      { id: "workflows-runtime-diagnostics", label: "Runtime evidence settings", path: "/workflows/runtime-diagnostics", order: 70, requiresPermission: "runtime-diagnostics.read" },
      { id: "not-a-diagnostic", label: "Unrelated activity", path: "/workflows/activity-availability", order: 80 }
    ];
    const access: NavigationAccess = {
      enabledFeatures: new Set(["otel-studio"]),
      roles: ["operator"],
      permissions: ["diagnostics.logs.read", "runtime-diagnostics.read"],
      authenticated: true
    };
    const filtered = filterAccessibleNavigation(navigation, access);
    const onNavigate = vi.fn();
    window.localStorage.setItem(moreToolsStorageKey, "true");
    const { container, unmount } = await renderDiagnosticsWithShell(stubApi(), filtered, onNavigate);

    const links = Array.from(container.querySelectorAll<HTMLAnchorElement>(".diagnostics-view a[href]"));
    const expectedEntries = [
      ["OpenTelemetry", "/diagnostics/open-telemetry"],
      ["Structured logs", "/diagnostics/structured-logs"],
      ["Console", "/diagnostics/console"],
      ["Database audit", "/diagnostics/audit"],
      ["Runtime evidence settings", "/workflows/runtime-diagnostics"]
    ];
    expect(links.map(link => link.getAttribute("href"))).toEqual(expectedEntries.map(([, path]) => path));
    for (const [label, path] of expectedEntries) {
      expect(links.find(link => link.getAttribute("href") === path)?.textContent).toContain(label);
    }
    expect(visibleText(container)).toMatch(/trace/i);
    expect(visibleText(container)).toMatch(/metrics/i);
    expect(visibleText(container)).toMatch(/OpenTelemetry Logs.*not available|not available.*OpenTelemetry Logs/i);
    expect(visibleText(container)).toMatch(/console output|live console/i);
    expect(visibleText(container)).toMatch(/navigate|open this destination/i);
    expect(visibleText(container)).not.toContain("No diagnostic tool links are available");
    expect(visibleText(container)).not.toContain("Private traces");
    expect(visibleText(container)).not.toContain("Unrelated activity");
    const sidebarPaths = new Set(Array.from(container.querySelectorAll<HTMLAnchorElement>("nav a[href]"), link => link.getAttribute("href")));
    expect(links.every(link => sidebarPaths.has(link.getAttribute("href")))).toBe(true);

    links[0].dispatchEvent(new MouseEvent("click", { bubbles: true, button: 0 }));
    expect(onNavigate).toHaveBeenCalledWith("/diagnostics/open-telemetry");

    await unmount();
    window.localStorage.removeItem(moreToolsStorageKey);
  });

  it("allows anonymous navigation through role and permission gates but still respects registered features", async () => {
    const navigation: StudioNavigationContribution[] = [
      { id: "diagnostics", label: "Diagnostics", path: "/diagnostics" },
      { id: "anonymous-tool", label: "Anonymous tool", path: "/diagnostics/anonymous", parentId: "diagnostics", requiresRole: "operators", requiresPermission: "diagnostics.read" },
      { id: "missing-feature-parent", label: "Missing feature", path: "/diagnostics/feature", parentId: "diagnostics", requiresFeature: "missing-module" },
      { id: "missing-feature-child", label: "Hidden child", path: "/diagnostics/feature/child", parentId: "missing-feature-parent" }
    ];
    const filtered = filterAccessibleNavigation(navigation, { enabledFeatures: new Set(), authenticated: false });
    const { container, unmount } = await renderDiagnosticsWithNavigation(stubApi(), filtered);

    const links = Array.from(container.querySelectorAll<HTMLAnchorElement>("a[href]"));
    expect(links.map(link => link.getAttribute("href"))).toEqual(["/diagnostics/anonymous"]);
    expect(links[0].textContent).toContain("Anonymous tool");
    expect(container.textContent).not.toContain("Missing feature");
    expect(container.textContent).not.toContain("Hidden child");

    await unmount();
  });

  it("includes registered nested Diagnostics descendants as destinations", async () => {
    const navigation: StudioNavigationContribution[] = [
      { id: "diagnostics", label: "Diagnostics", path: "/diagnostics" },
      { id: "custom-audit-group", label: "Audit tools", path: "/diagnostics/audit-tools", parentId: "diagnostics" },
      { id: "custom-event-details", label: "Event index", path: "/diagnostics/audit-tools/events", parentId: "custom-audit-group" }
    ];
    const filtered = filterAccessibleNavigation(navigation, { authenticated: false });
    const { container, unmount } = await renderDiagnosticsWithNavigation(stubApi(), filtered);

    const links = Array.from(container.querySelectorAll<HTMLAnchorElement>("a[href]"));
    expect(links.map(link => link.getAttribute("href"))).toEqual([
      "/diagnostics/audit-tools",
      "/diagnostics/audit-tools/events"
    ]);
    expect(links[0].textContent).toContain("Audit tools");
    expect(links[1].textContent).toContain("Event index");

    await unmount();
  });

  it("shows the no-links state only for an empty filtered candidate set and keeps widgets visible", async () => {
    function Widget() {
      return <div>Widget remains available</div>;
    }
    const api = stubApi({ diagnosticsWidgets: [diagnosticsWidget("retained", "Retained", Widget)] });
    const restricted: StudioNavigationContribution[] = [
      { id: "diagnostics", label: "Diagnostics", path: "/diagnostics" },
      { id: "secret-tool", label: "Restricted recorder", path: "/diagnostics/recorder", parentId: "diagnostics", requiresPermission: "diagnostics.record" }
    ];
    const filtered = filterAccessibleNavigation(restricted, {
      authenticated: true,
      permissions: [],
      enabledFeatures: new Set()
    });
    const { container, unmount } = await renderDiagnosticsWithNavigation(api, filtered);

    expect(container.textContent).toContain("No diagnostic tool links are available");
    expect(container.textContent).toContain("Widget remains available");
    expect(container.textContent).not.toContain("Restricted recorder");
    expect(container.textContent).not.toMatch(/backend.*(unavailable|outage|down)|no (logs|traces|data) available/i);

    await unmount();
  });

  it("keeps Runtime evidence settings in the baseline candidate set when telemetry modules are absent", async () => {
    const baselineNavigation: StudioNavigationContribution[] = [
      { id: "diagnostics", label: "Diagnostics", path: "/diagnostics" },
      { id: "console", label: "Console", path: "/diagnostics/console", parentId: "diagnostics", order: 10 },
      { id: "workflows-runtime-diagnostics", label: "Runtime evidence settings", path: "/workflows/runtime-diagnostics", order: 20 }
    ];
    const filtered = filterAccessibleNavigation(baselineNavigation, { authenticated: false });
    const { container, unmount } = await renderDiagnosticsWithNavigation(stubApi(), filtered);

    const links = Array.from(container.querySelectorAll<HTMLAnchorElement>("a[href]"));
    expect(links.map(link => link.getAttribute("href"))).toEqual(["/diagnostics/console", "/workflows/runtime-diagnostics"]);
    expect(links[0].textContent).toContain("Console");
    expect(links[1].textContent).toContain("Runtime evidence settings");
    expect(container.textContent).not.toContain("No diagnostic tool links are available");
    expect(container.textContent).not.toContain("OpenTelemetry");
    expect(container.textContent).not.toContain("Structured logs");

    await unmount();
  });

  it("renders diagnostics widgets in deterministic order", async () => {
    const { container, unmount } = await renderDiagnostics(stubApi({
      diagnosticsWidgets: [
        diagnosticsWidget("beta", "Beta", () => <div>Beta widget</div>),
        diagnosticsWidget("alpha", "Alpha", () => <div>Alpha widget</div>),
        diagnosticsWidget("first", "First", () => <div>First widget</div>, 10)
      ]
    }));

    expect(visibleText(container)).toMatch(/First widget.*Alpha widget.*Beta widget/s);

    await unmount();
  });

  it("shows a useful empty state when no diagnostics widgets are registered", async () => {
    const { container, unmount } = await renderDiagnostics(stubApi({
      diagnostics: [{ moduleId: "Elsa.Studio.FeatureManagement", status: "available", reason: "Module manifest accepted." }]
    }));

    expect(container.textContent).toContain("No diagnostics widgets are registered.");
    expect(container.textContent).not.toContain("Module diagnostics");
    expect(container.textContent).not.toContain("Module manifest accepted.");

    await unmount();
  });

  it("isolates failed snapshot widgets from healthy widgets", async () => {
    function SnapshotWidget({ state }: StudioDiagnosticsWidgetProps) {
      return <div>Snapshot status: {state.status} {String(state.snapshot ?? state.error ?? "")}</div>;
    }

    const { container, unmount } = await renderDiagnostics(stubApi({
      diagnosticsWidgets: [
        {
          id: "healthy",
          title: "Healthy",
          load: async () => "ready",
          component: SnapshotWidget
        },
        {
          id: "failed",
          title: "Failed",
          load: async () => {
            throw new Error("stream unavailable");
          },
          component: SnapshotWidget
        }
      ]
    }));

    await flushPromises();

    expect(container.textContent).toContain("Snapshot status: ready ready");
    expect(container.textContent).toContain("Snapshot status: error stream unavailable");

    await unmount();
  });

  it("cleans up live diagnostics subscriptions on unmount", async () => {
    const cleanup = vi.fn();
    function LiveWidget({ state }: StudioDiagnosticsWidgetProps) {
      return <div>Live status: {state.status} {String(state.snapshot ?? "")}</div>;
    }

    const { container, unmount } = await renderDiagnostics(stubApi({
      diagnosticsWidgets: [
        {
          id: "live",
          title: "Live",
          mode: "live",
          subscribe: publish => {
            publish("connected");
            return cleanup;
          },
          component: LiveWidget
        }
      ]
    }));

    await flushPromises();

    expect(container.textContent).toContain("Live status: streaming connected");

    await unmount();

    expect(cleanup).toHaveBeenCalledTimes(1);
  });
});

describe("navigation sections", () => {
  it("accepts Home navigation from the dashboard module", () => {
    const navigation = getStudioNavigation([
      { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
      { id: "weather", label: "Weather", path: "/weather", order: 20 }
    ]);

    expect(navigation.filter(item => item.path === "/dashboard").map(item => item.id)).toEqual(["home"]);
    expect(navigation.map(item => item.id)).not.toContain("overview");
    expect(navigation.map(item => item.id)).toContain("weather");
  });

  it("ships no Extension Builder entry among the built-in navigation items", () => {
    // Extension Builder is an optional module (#535): a default Studio has no nav item for it, and a host without the
    // module infers nothing special for its path.
    const builtIn = getStudioNavigation([], { includeThemeBuilder: true });

    expect(builtIn.map(item => item.id)).not.toContain("extension-builder");
    expect(builtIn.map(item => item.path)).not.toContain("/extension-builder");
    expect(getNavigationSection({ id: "extension-builder", path: "/extension-builder" })).toBe("workspace");
  });

  it("resolves the legacy Overview path as Dashboard", () => {
    expect(isDashboardPath("/overview")).toBe(true);
  });

  it("groups module and feature management under Settings", () => {
    expect(getNavigationSection({ id: "dashboard", path: "/dashboard" })).toBe("workspace");
    expect(getNavigationSection({ id: "weather", path: "/weather" })).toBe("workspace");
    expect(getNavigationSection({ id: "modules", path: "/modules" })).toBe("settings");
    expect(getNavigationSection({ id: "package-feeds", path: "/package-feeds" })).toBe("settings");
    expect(getNavigationSection({ id: "feature-management", path: "/features" })).toBe("settings");
  });

  it("honours a contribution's declared section over the host's path inference", () => {
    expect(getNavigationSection({ id: "tools", path: "/tools", section: "settings" })).toBe("settings");
    expect(getNavigationSection({ id: "modules", path: "/modules", section: "workspace" })).toBe("workspace");
  });

  it("lists a contribution under its declared section and renders its own icon", async () => {
    const navigation: StudioNavigationContribution[] = [
      { id: "dashboard", label: "Dashboard", path: "/dashboard", placement: "main" },
      { id: "tools", label: "Tools", path: "/tools", section: "settings", icon: ({ size }) => <svg data-testid="tools-icon" width={size} /> }
    ];
    const { container, unmount } = await renderComponent(
      <ThemeProvider>
        <ShellFrame navigation={navigation} panels={[]} path="/dashboard" title="Dashboard" backendBaseUrl="https://backend.example/" onNavigate={() => {}}>
          <div>content</div>
        </ShellFrame>
      </ThemeProvider>
    );

    const settings = container.querySelector('nav.nav-section[aria-label="Settings"]');
    expect(settings?.textContent).toContain("Settings");
    expect(settings?.textContent).not.toContain("Tools");
    expect(container.querySelector('nav.nav-section[aria-label="Main"]')?.textContent ?? "").not.toContain("Tools");
    expect(container.querySelector('nav.nav-section[aria-label="Administration"]')).toBeNull();

    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search modules"]')!;
    flushSync(() => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      setter.call(search, "tools");
      search.dispatchEvent(new Event("input", { bubbles: true }));
    });
    const settingsResults = container.querySelector('nav.nav-section[aria-label="Settings"]');
    expect(settingsResults?.textContent).toContain("Tools");
    expect(settingsResults?.querySelector('[data-testid="tools-icon"]')?.getAttribute("width")).toBe("18");

    await unmount();
  });

  it("renders a child with a different placement under that placement instead of hiding it", async () => {
    window.localStorage.setItem(moreToolsStorageKey, "true");
    const navigation: StudioNavigationContribution[] = [
      { id: "workflows", label: "Workflows", path: "/workflows/definitions", placement: "main" },
      { id: "definitions", label: "Definitions", path: "/workflows/definitions/list", parentId: "workflows" },
      { id: "executables", label: "Executables", path: "/workflows/executables", parentId: "workflows", placement: "more" }
    ];
    const { container, unmount } = await renderComponent(
      <ThemeProvider>
        <ShellFrame navigation={navigation} panels={[]} path="/workflows/definitions" title="Workflows" backendBaseUrl="https://backend.example/" onNavigate={() => {}}>
          <div>content</div>
        </ShellFrame>
      </ThemeProvider>
    );

    const main = container.querySelector('nav.nav-section[aria-label="Main"]');
    const more = container.querySelector('nav.nav-section[aria-label="More tools"]');
    expect(main?.textContent).toContain("Workflows");
    expect(main?.textContent).toContain("Definitions");
    expect(main?.textContent).not.toContain("Executables");
    expect(more?.textContent).toContain("Executables");

    window.localStorage.removeItem(moreToolsStorageKey);
    await unmount();
  });

  it("keeps child navigation items out of top-level sections", () => {
    const navigation = [
      { id: "diagnostics", label: "Diagnostics", path: "/diagnostics" },
      { id: "structured-logs", label: "Structured logs", path: "/diagnostics/structured-logs", parentId: "diagnostics" },
      { id: "orphan", label: "Orphan", path: "/orphan", parentId: "missing" }
    ];

    expect(getTopLevelNavigationItems(navigation, "workspace").map(item => item.id)).toEqual(["diagnostics", "orphan"]);
  });
});

// All host-health reads go through `api.host.http` now: the Studio module registry and the Studio management bridge
// status both live on the Studio origin. The host getJson dispatches by URL so a single stub covers both, and tests
// can assert the bridge path is hit while the backend context is never probed.
function stubApi(options: {
  backendStatus?: StudioBackendManagementStatus;
  bridgeGetJson?: (url: string) => Promise<unknown>;
  backendGetJson?: (url: string) => Promise<unknown>;
  widgets?: Array<{ id: string; title: string; order?: number; component: React.ComponentType }>;
  diagnosticsWidgets?: StudioDiagnosticsWidgetContribution[];
  diagnostics?: Array<{ moduleId: string; status: string; reason: string }>;
} = {}): ElsaStudioModuleApi {
  const bridgeGetJson = options.bridgeGetJson ?? (async () => options.backendStatus ?? bridgeStatus("available"));
  const hostGetJson = async (url: string) =>
    url === "/_elsa/studio/backend-management/status" ? bridgeGetJson(url) : healthyRegistry();

  return {
    host: {
      baseUrl: "https://studio.example/",
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      http: {
        getJson: hostGetJson,
        postJson: async () => ({})
      }
    },
    backend: {
      baseUrl: "https://foundation.example/",
      http: {
        // Must never be called by host-health anymore; a probe here fails the test loudly.
        getJson: options.backendGetJson ?? (async () => { throw new Error("backend context must not be probed by host-health"); }),
        postJson: async () => ({})
      }
    },
    dashboardWidgets: {
      add() {},
      list: () => options.widgets ?? []
    },
    diagnosticsWidgets: {
      add() {},
      list: () => options.diagnosticsWidgets ?? []
    },
    diagnostics: {
      add() {},
      list: () => options.diagnostics ?? []
    }
  } as ElsaStudioModuleApi;
}

function bridgeStatus(status: StudioBackendManagementStatus["status"], detail = ""): StudioBackendManagementStatus {
  return { status, detail, backendBaseUrl: "https://foundation.example", checkedAt: new Date().toISOString() };
}

function healthyRegistry() {
  return {
    modules: [
      { status: "available", diagnostics: [] }
    ],
    diagnostics: []
  };
}

async function renderDiagnostics(api: ElsaStudioModuleApi) {
  return renderComponent(<Diagnostics api={api} />);
}

type DiagnosticsIndexProps = {
  api: ElsaStudioModuleApi;
  navigation: StudioNavigationContribution[];
  onNavigate: (path: string) => void;
};

async function renderDiagnosticsWithNavigation(api: ElsaStudioModuleApi, navigation: StudioNavigationContribution[], onNavigate: (path: string) => void = () => {}) {
  const DiagnosticsIndex = Diagnostics as unknown as React.ComponentType<DiagnosticsIndexProps>;
  return renderComponent(<DiagnosticsIndex api={api} navigation={navigation} onNavigate={onNavigate} />);
}

async function renderDiagnosticsWithShell(api: ElsaStudioModuleApi, navigation: StudioNavigationContribution[], onNavigate: (path: string) => void) {
  const DiagnosticsIndex = Diagnostics as unknown as React.ComponentType<DiagnosticsIndexProps>;
  return renderComponent(
    <ThemeProvider>
      <ShellFrame navigation={navigation} panels={[]} path="/diagnostics" title="Diagnostics" backendBaseUrl="https://backend.example/" onNavigate={onNavigate}>
        <DiagnosticsIndex api={api} navigation={navigation} onNavigate={onNavigate} />
      </ShellFrame>
    </ThemeProvider>
  );
}

async function renderComponent(component: React.ReactNode) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(withQueryClient(component));
  });

  const unmount = () => {
    if (!mountedTestRoots.delete(unmount)) return;
    flushSync(() => root.unmount());
    container.remove();
  };
  mountedTestRoots.add(unmount);

  return {
    container,
    unmount: async () => unmount()
  };
}

function diagnosticsWidget(id: string, title: string, component: React.ComponentType<StudioDiagnosticsWidgetProps>, order?: number): StudioDiagnosticsWidgetContribution {
  return { id, title, order, component };
}

function visibleText(container: HTMLElement) {
  return container.textContent?.replace(/\s+/g, " ").trim() ?? "";
}

async function flushPromises() {
  // TanStack Query settles across several microtask/render cycles (queryFn resolve → cache commit →
  // re-render), so flush a handful of macrotasks to let the host-health query reach its terminal state.
  for (let i = 0; i < 5; i++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
}
