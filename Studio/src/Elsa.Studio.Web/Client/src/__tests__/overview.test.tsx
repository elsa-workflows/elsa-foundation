import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { getNavigationSection, Home } from "../app/App";
import type { ElsaStudioModuleApi } from "../sdk";

describe("overview", () => {
  it("shows host health instead of Studio-only module count cards", async () => {
    const backendGetJson = vi.fn(async () => healthyRegistry());
    const { container, unmount } = await renderHome(stubApi({ backendGetJson }));

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
});

describe("navigation sections", () => {
  it("groups module and feature management under Settings", () => {
    expect(getNavigationSection({ id: "home", path: "/" })).toBe("workspace");
    expect(getNavigationSection({ id: "weather", path: "/weather" })).toBe("workspace");
    expect(getNavigationSection({ id: "modules", path: "/modules" })).toBe("settings");
    expect(getNavigationSection({ id: "package-feeds", path: "/package-feeds" })).toBe("settings");
    expect(getNavigationSection({ id: "feature-management", path: "/features" })).toBe("settings");
  });
});

function stubApi(options: { backendGetJson: (url: string) => Promise<unknown> }): ElsaStudioModuleApi {
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
      list: () => []
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

async function renderHome(api: ElsaStudioModuleApi) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(<Home api={api} />);
  });

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}
