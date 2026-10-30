import React from "react";
import { createRoot } from "react-dom/client";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { FeatureManagementPage, register } from "../../../Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

function createFeature(id: string, enabled = false) {
  return {
    id,
    displayName: id,
    categories: ["Fixture"],
    sourceKind: "runtime",
    packageId: "Fixture.Features",
    packageVersion: "1.0.0",
    enabled,
    configuration: {},
    advanced: false,
    experimental: false,
    settings: [],
    dependencies: []
  };
}

function registry() {
  const items: unknown[] = [];
  return {
    items,
    add(item: unknown) {
      items.push(item);
    },
    list() {
      return items;
    }
  };
}

const studioCatalog = {
  revision: "studio-layout-fixture",
  features: [createFeature("StudioFeatureA"), createFeature("StudioFeatureB")]
};
const serverCatalog = {
  revision: "server-layout-fixture",
  features: Array.from({ length: 114 }, (_, index) => createFeature(`ServerFeature${String(index + 1).padStart(3, "0")}`))
};

const api = {
  host: {
    http: {
      getJson: async () => studioCatalog,
      postJson: async () => ({ catalog: studioCatalog, featureDescriptorCount: studioCatalog.features.length, reloadedShellCount: 1 })
    }
  },
  backend: {
    http: {
      getJson: async () => serverCatalog,
      postJson: async () => ({ catalog: serverCatalog, featureDescriptorCount: serverCatalog.features.length, reloadedShellCount: 1 })
    }
  },
  navigation: registry(),
  routes: registry(),
  settingEditors: registry()
};

register(api as never);

const rootElement = document.getElementById("root");
if (!rootElement) throw new Error("Feature Management fixture root is missing.");

const moduleWidth = Number(new URLSearchParams(window.location.search).get("moduleWidth"));
if (Number.isFinite(moduleWidth) && moduleWidth > 0) {
  rootElement.style.width = `${moduleWidth}px`;
}

createRoot(rootElement).render(
  <ThemeProvider>
    <AnonymousAuthProvider>
      <FeatureManagementPage />
    </AnonymousAuthProvider>
  </ThemeProvider>
);
