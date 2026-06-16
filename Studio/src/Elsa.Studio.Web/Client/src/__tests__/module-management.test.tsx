import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it } from "vitest";
import { ModuleManagementPage } from "../app/modules/ModuleManagementPage";
import type { ElsaStudioModuleApi, StudioModuleRegistryResponse } from "../sdk";

describe("module management page", () => {
  it("renders backend-aware module registry details", async () => {
    const registry: StudioModuleRegistryResponse = {
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      generatedAt: new Date().toISOString(),
      modules: [{
        id: "Elsa.Studio.FeatureManagement",
        displayName: "Feature management",
        sourceKind: "static-web-asset",
        scope: "full-stack",
        version: "1.0.1",
        requiredHostVersion: "^1.0.0",
        requiredSdkVersion: "^1.0.0",
        compatibility: "compatible",
        status: "available",
        manifest: {
          entry: "/_content/Elsa.Studio.FeatureManagement/studio/modules/features/module.js",
          styles: [],
          capabilities: ["http", "navigation", "routes", "setting-editors"]
        },
        contributions: [
          { id: "feature-management:http", type: "http", label: "HTTP endpoints", status: "active" },
          { id: "feature-management:setting-editors", type: "setting-editors", label: "Setting editors", status: "active" }
        ],
        diagnostics: [{ moduleId: "Elsa.Studio.FeatureManagement", status: "available", reason: "Module manifest accepted." }]
      }]
    };
    const { container, unmount } = await renderModuleManagementPage(stubApi(registry));

    expect(container.textContent).toContain("Feature management");
    expect(container.textContent).toContain("full-stack");
    expect(container.textContent).toContain("HTTP endpoints");
    expect(container.textContent).toContain("loaded");

    await unmount();
  });
});

function stubApi(registry: StudioModuleRegistryResponse): ElsaStudioModuleApi {
  return {
    host: {
      baseUrl: "https://studio.example/",
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      http: {
        getJson: async () => registry,
        postJson: async () => ({})
      }
    },
    diagnostics: {
      add() {},
      list: () => [{ moduleId: "Elsa.Studio.FeatureManagement", status: "loaded", reason: "Module activated." }]
    }
  } as ElsaStudioModuleApi;
}

async function renderModuleManagementPage(api: ElsaStudioModuleApi) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(<ModuleManagementPage api={api} />);
  });
  await flushPromises();

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
