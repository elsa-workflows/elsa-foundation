import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Dashboard, Diagnostics, getNavigationSection, getStudioNavigation, getTopLevelNavigationItems, isDashboardPath } from "../app/App";
import type { ElsaStudioModuleApi, StudioBackendManagementStatus, StudioDiagnosticsWidgetContribution, StudioDiagnosticsWidgetProps } from "../sdk";
import { withQueryClient } from "./queryTestUtils";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("dashboard", () => {
  it("keeps a healthy optional privileged host-management integration out of global health", async () => {
    const { container, unmount } = await renderDashboard(stubApi({ backendStatus: bridgeStatus("available") }));

    await flushPromises();

    expect(container.textContent).toContain("Studio host");
    expect(container.textContent).not.toContain("Privileged host management");
    expect(container.textContent).toContain("Attention");
    expect(container.textContent).toContain("No host issues reported.");
    // The old direct-backend probes are gone: no "Server host" tile, no "Backend API" liveness tile.
    expect(container.textContent).not.toContain("Server host");
    expect(container.textContent).not.toContain("Backend API");
    expect(container.textContent).not.toContain("Available modules");

    await unmount();
  });

  it("reads backend management status from the Studio bridge, never the backend context", async () => {
    const bridgeGetJson = vi.fn(async () => bridgeStatus("available"));
    const backendGetJson = vi.fn(async () => healthyRegistry());
    const { container, unmount } = await renderDashboard(stubApi({ bridgeGetJson, backendGetJson }));

    await flushPromises();

    // Bridge read goes to the Studio host origin; the backend context is never probed for registry/liveness.
    expect(bridgeGetJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/status");
    expect(backendGetJson).not.toHaveBeenCalled();
    expect(container.textContent).not.toContain("Connected");

    await unmount();
  });

  it("does not issue any direct browser fetch to backend host-control endpoints", async () => {
    // A global fetch that fails loudly: host-health must never reach it (all reads go through api.host.http).
    const fetchMock = vi.fn(async () => { throw new Error("host-health must not fetch directly"); });
    vi.stubGlobal("fetch", fetchMock);
    const { unmount } = await renderDashboard(stubApi({ backendStatus: bridgeStatus("available") }));

    await flushPromises();

    expect(fetchMock).not.toHaveBeenCalled();

    await unmount();
  });

  it("hides an unconfigured optional privileged host-management integration", async () => {
    const { container, unmount } = await renderDashboard(stubApi({
      backendStatus: bridgeStatus("unconfigured", "Backend management is not configured on the Studio host.")
    }));

    await flushPromises();

    expect(container.textContent).not.toContain("Privileged host management");
    expect(container.textContent).not.toContain("Not configured");
    expect(container.textContent).not.toContain("Backend management is not configured on the Studio host.");
    expect(container.textContent).toContain("No host issues reported.");

    await unmount();
  });

  it("renders an explicit unauthorized state", async () => {
    const { container, unmount } = await renderDashboard(stubApi({
      backendStatus: bridgeStatus("unauthorized", "The backend rejected the Studio management key.")
    }));

    await flushPromises();

    expect(container.textContent).toContain("Unauthorized");
    expect(container.textContent).toContain("Privileged host management");
    expect(container.textContent).toContain("The backend rejected the Studio management key.");
    expect(container.textContent).not.toContain("Connected");

    await unmount();
  });

  it("renders an explicit unreachable state", async () => {
    const { container, unmount } = await renderDashboard(stubApi({
      backendStatus: bridgeStatus("unreachable", "The backend management surface could not be reached.")
    }));

    await flushPromises();

    expect(container.textContent).toContain("Unreachable");
    expect(container.textContent).toContain("Privileged host management");
    expect(container.textContent).toContain("The backend management surface could not be reached.");

    await unmount();
  });

  it("renders a degraded state for attention", async () => {
    const { container, unmount } = await renderDashboard(stubApi({
      backendStatus: bridgeStatus("degraded", "The backend management surface responded with an unexpected status.")
    }));

    await flushPromises();

    expect(container.textContent).toContain("Degraded");
    expect(container.textContent).toContain("Privileged host management");
    expect(container.textContent).toContain("Review Modules or Extension Builder for host-management issues.");

    await unmount();
  });

  it("uses privileged host-management terminology when a configured failure has no detail", async () => {
    const { container, unmount } = await renderDashboard(stubApi({ backendStatus: bridgeStatus("degraded") }));

    await flushPromises();

    expect(container.textContent).toContain("Privileged host management is degraded.");
    expect(container.textContent).not.toContain("Backend management is degraded.");

    await unmount();
  });

  it("hides an unknown bridge state because it does not prove the optional integration is configured", async () => {
    const bridgeGetJson = vi.fn(async () => { throw new Error("bridge offline"); });
    const { container, unmount } = await renderDashboard(stubApi({ bridgeGetJson }));

    await flushPromises();

    expect(container.textContent).not.toContain("Privileged host management");
    expect(container.textContent).not.toContain("Unknown");
    expect(container.textContent).not.toContain("Backend management status is unavailable: bridge offline");

    await unmount();
  });

  it("re-checks host health when modules change", async () => {
    const bridgeGetJson = vi.fn(async () => bridgeStatus("available"));
    const { unmount } = await renderDashboard(stubApi({ bridgeGetJson }));

    await flushPromises();
    expect(bridgeGetJson).toHaveBeenCalledTimes(1);

    window.dispatchEvent(new Event("elsa-studio:modules-changed"));
    await flushPromises();

    expect(bridgeGetJson).toHaveBeenCalledTimes(2);

    await unmount();
  });

  it("re-checks host health when Refresh is clicked", async () => {
    const bridgeGetJson = vi.fn(async () => bridgeStatus("available"));
    const { container, unmount } = await renderDashboard(stubApi({ bridgeGetJson }));

    await flushPromises();
    expect(bridgeGetJson).toHaveBeenCalledTimes(1);

    const refresh = container.querySelector<HTMLButtonElement>(".host-health-refresh");
    expect(refresh).not.toBeNull();
    flushSync(() => refresh!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await flushPromises();

    expect(bridgeGetJson).toHaveBeenCalledTimes(2);

    await unmount();
  });

  it("renders dashboard widgets contributed through the Studio SDK", async () => {
    const { container, unmount } = await renderDashboard(stubApi({
      widgets: [
        {
          id: "sample-widget",
          title: "Sample",
          component: () => <div>Contributed widget</div>
        }
      ]
    }));

    await flushPromises();

    expect(container.textContent).toContain("Contributed widget");

    await unmount();
  });

  it("renders dashboard widgets in deterministic order", async () => {
    const { container, unmount } = await renderDashboard(stubApi({
      widgets: [
        { id: "beta", title: "Beta", component: () => <div>Beta widget</div> },
        { id: "alpha", title: "Alpha", component: () => <div>Alpha widget</div> },
        { id: "first", title: "First", order: 10, component: () => <div>First widget</div> }
      ]
    }));

    await flushPromises();

    expect(visibleText(container)).toMatch(/First widget.*Alpha widget.*Beta widget/s);

    await unmount();
  });

  it("shows a useful Dashboard empty state when no widgets are registered", async () => {
    const { container, unmount } = await renderDashboard(stubApi({
    }));

    await flushPromises();

    expect(container.textContent).toContain("No dashboard widgets are registered.");

    await unmount();
  });
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
  it("keeps Dashboard owned by the host shell", () => {
    const navigation = getStudioNavigation([
      { id: "dashboard-sample", label: "Dashboard", path: "/dashboard", order: 100 },
      { id: "overview", label: "Overview", path: "/overview", order: 10 },
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

async function renderDashboard(api: ElsaStudioModuleApi) {
  return renderComponent(<Dashboard api={api} />);
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
