import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Diagnostics, getNavigationSection, getStudioNavigation, getTopLevelNavigationItems, isDashboardPath, ShellFrame } from "../app/App";
import { moreToolsStorageKey } from "../app/navigation/placement";
import { ThemeProvider } from "../app/components/ThemeProvider";
import type { ElsaStudioModuleApi, StudioBackendManagementStatus, StudioDiagnosticsWidgetContribution, StudioDiagnosticsWidgetProps, StudioNavigationContribution } from "../sdk";
import { withQueryClient } from "./queryTestUtils";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("diagnostics", () => {
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

async function renderComponent(component: React.ReactNode) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(withQueryClient(component));
  });

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
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
