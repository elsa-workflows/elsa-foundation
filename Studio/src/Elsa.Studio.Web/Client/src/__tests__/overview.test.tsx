import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Diagnostics, getNavigationSection, getStudioNavigation, getTopLevelNavigationItems, isDashboardPath } from "../app/App";
import type { ElsaStudioModuleApi, StudioBackendManagementStatus, StudioDiagnosticsWidgetContribution, StudioDiagnosticsWidgetProps } from "../sdk";
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
  it("accepts Dashboard navigation from its dedicated module", () => {
    const navigation = getStudioNavigation([
      { id: "dashboard", label: "Dashboard", path: "/dashboard", order: 0 },
      { id: "weather", label: "Weather", path: "/weather", order: 20 }
    ]);

    expect(navigation.filter(item => item.path === "/dashboard").map(item => item.id)).toEqual(["dashboard"]);
    expect(navigation.map(item => item.id)).not.toContain("overview");
    expect(navigation.map(item => item.id)).toContain("weather");
  });

  it("resolves the legacy Overview path as Dashboard", () => {
    expect(isDashboardPath("/overview")).toBe(true);
  });

  it("groups module and feature management under Settings", () => {
    expect(getNavigationSection({ id: "dashboard", path: "/dashboard" })).toBe("workspace");
    expect(getNavigationSection({ id: "weather", path: "/weather" })).toBe("workspace");
    expect(getNavigationSection({ id: "extension-builder", path: "/extension-builder" })).toBe("settings");
    expect(getNavigationSection({ id: "modules", path: "/modules" })).toBe("settings");
    expect(getNavigationSection({ id: "package-feeds", path: "/package-feeds" })).toBe("settings");
    expect(getNavigationSection({ id: "feature-management", path: "/features" })).toBe("settings");
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
