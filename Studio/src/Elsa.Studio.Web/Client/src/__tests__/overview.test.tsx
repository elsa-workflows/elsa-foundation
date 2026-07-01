import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Dashboard, Diagnostics, getNavigationSection, getStudioNavigation, getTopLevelNavigationItems, isDashboardPath } from "../app/App";
import type { ElsaStudioModuleApi, StudioDiagnosticsWidgetContribution, StudioDiagnosticsWidgetProps } from "../sdk";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("dashboard", () => {
  it("shows host health instead of Studio-only module count cards", async () => {
    stubBackendFetch();
    const backendGetJson = vi.fn(async () => healthyRegistry());
    const { container, unmount } = await renderDashboard(stubApi({ backendGetJson }));

    await flushPromises();

    expect(container.textContent).toContain("Studio host");
    expect(container.textContent).toContain("Server host");
    expect(container.textContent).toContain("Backend API");
    expect(container.textContent).toContain("Attention");
    expect(container.textContent).toContain("No host issues reported.");
    expect(container.textContent).not.toContain("Available modules");
    expect(container.textContent).not.toContain("Loaded modules");
    expect(container.textContent).not.toContain("Failed modules");
    expect(backendGetJson).toHaveBeenCalledWith("/_elsa/module-management/registry");

    await unmount();
  });

  it("reports backend reachability separately from the server module registry", async () => {
    const fetchMock = stubBackendFetch();
    const backendGetJson = vi.fn(async () => {
      throw new Error("Request failed with 404.");
    });
    const { container, unmount } = await renderDashboard(stubApi({ backendGetJson }));

    await flushPromises();

    expect(container.textContent).toContain("Backend API");
    expect(container.textContent).toContain("Connected");
    expect(container.textContent).toContain("Server module registry is unavailable: Request failed with 404.");
    expect(container.textContent).toContain("Open Modules to review host-scoped issues.");
    expect(fetchMock).toHaveBeenCalledWith("https://foundation.example/_elsa/health", { cache: "no-store" });

    await unmount();
  });

  it("flags the Backend API tile for attention when the health probe returns an error status", async () => {
    stubBackendFetch(() => new Response("", { status: 503 }));
    const { container, unmount } = await renderDashboard(stubApi({
      backendGetJson: async () => healthyRegistry()
    }));

    await flushPromises();

    expect(container.textContent).toContain("Backend API");
    expect(container.textContent).toContain("Review");
    expect(container.textContent).toContain("https://foundation.example/_elsa/health responded with 503.");

    await unmount();
  });

  it("marks the Backend API tile unavailable when the health probe cannot reach the host", async () => {
    stubBackendFetch(() => {
      throw new Error("Failed to fetch");
    });
    const { container, unmount } = await renderDashboard(stubApi({
      backendGetJson: async () => healthyRegistry()
    }));

    await flushPromises();

    expect(container.textContent).toContain("Backend API");
    expect(container.textContent).toContain("Unavailable");
    expect(container.textContent).toContain("https://foundation.example/_elsa/health (Failed to fetch)");

    await unmount();
  });

  it("flags the backend as degraded when it responds with a server error", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("", { status: 503 })));
    const { container, unmount } = await renderDashboard(stubApi({ backendGetJson: async () => healthyRegistry() }));

    await flushPromises();

    expect(container.textContent).toContain("responded with HTTP 503");
    expect(container.textContent).not.toContain("Connected");

    await unmount();
  });

  it("re-checks host health when modules change", async () => {
    stubBackendFetch();
    const backendGetJson = vi.fn(async () => healthyRegistry());
    const { unmount } = await renderDashboard(stubApi({ backendGetJson }));

    await flushPromises();
    expect(backendGetJson).toHaveBeenCalledTimes(1);

    window.dispatchEvent(new Event("elsa-studio:modules-changed"));
    await flushPromises();

    expect(backendGetJson).toHaveBeenCalledTimes(2);

    await unmount();
  });

  it("re-checks host health when Refresh is clicked", async () => {
    stubBackendFetch();
    const backendGetJson = vi.fn(async () => healthyRegistry());
    const { container, unmount } = await renderDashboard(stubApi({ backendGetJson }));

    await flushPromises();
    expect(backendGetJson).toHaveBeenCalledTimes(1);

    const refresh = container.querySelector<HTMLButtonElement>(".host-health-refresh");
    expect(refresh).not.toBeNull();
    flushSync(() => refresh!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await flushPromises();

    expect(backendGetJson).toHaveBeenCalledTimes(2);

    await unmount();
  });

  it("renders dashboard widgets contributed through the Studio SDK", async () => {
    stubBackendFetch();
    const { container, unmount } = await renderDashboard(stubApi({
      backendGetJson: async () => healthyRegistry(),
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
    stubBackendFetch();
    const { container, unmount } = await renderDashboard(stubApi({
      backendGetJson: async () => healthyRegistry(),
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
    stubBackendFetch();
    const { container, unmount } = await renderDashboard(stubApi({
      backendGetJson: async () => healthyRegistry()
    }));

    await flushPromises();

    expect(container.textContent).toContain("No dashboard widgets are registered.");

    await unmount();
  });
});

describe("diagnostics", () => {
  it("renders diagnostics widgets in deterministic order", async () => {
    const { container, unmount } = await renderDiagnostics(stubApi({
      backendGetJson: async () => healthyRegistry(),
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
      backendGetJson: async () => healthyRegistry(),
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
      backendGetJson: async () => healthyRegistry(),
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
      backendGetJson: async () => healthyRegistry(),
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

function stubApi(options: {
  backendGetJson: (url: string) => Promise<unknown>;
  widgets?: Array<{ id: string; title: string; order?: number; component: React.ComponentType }>;
  diagnosticsWidgets?: StudioDiagnosticsWidgetContribution[];
  diagnostics?: Array<{ moduleId: string; status: string; reason: string }>;
}): ElsaStudioModuleApi {
  return {
    host: {
      baseUrl: "https://studio.example/",
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      http: {
        getJson: async () => healthyRegistry(),
        postJson: async () => ({})
      }
    },
    backend: {
      baseUrl: "https://foundation.example/",
      http: {
        getJson: options.backendGetJson,
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
    root.render(component);
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
  await new Promise(resolve => setTimeout(resolve, 0));
}

function stubBackendFetch(responder: () => Response | Promise<Response> = () => new Response("", { status: 200 })) {
  const fetchMock = vi.fn(async () => responder());
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}
