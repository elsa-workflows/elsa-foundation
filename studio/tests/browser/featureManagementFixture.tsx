import React from "react";
import { createRoot } from "react-dom/client";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { FeatureManagementPage, register } from "../../../extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

function createFeature(id: string, enabled = false, categories = ["Fixture"], advanced = false) {
  return {
    id,
    displayName: id,
    description: `Provides the ${categories[0]} capability.`,
    categories,
    sourceKind: "runtime",
    packageId: "Fixture.Features",
    packageVersion: "1.0.0",
    enabled,
    configuration: {},
    advanced,
    experimental: false,
    settings: [],
    dependencies: []
  };
}

const persistenceSettingsFixture = new URLSearchParams(window.location.search).has("persistence-settings");

function createPersistenceFeature() {
  return {
    ...createFeature("AgentsEntityFrameworkCorePersistence", true, ["Persistence"]),
    displayName: "Agents Entity Framework Core Persistence",
    description: "Persists scoped agent turn records with database-backed idempotency checks.",
    configuration: {
      ConnectionName: "",
      ConnectionString: "",
      PooledContexts: false,
      Provider: "Sqlite",
      Schema: "",
      MigrationOptions: { useTransactions: true }
    },
    settings: [
      {
        name: "ConnectionName",
        displayName: "Connection name",
        description: "Named database connection when the explicit string is omitted.",
        group: "Persistence",
        jsonType: "string",
        required: false,
        secret: false,
        sensitive: false,
        restartRequired: false,
        advanced: false,
        experimental: false,
        options: []
      },
      {
        name: "ConnectionString",
        displayName: "Connection string",
        description: "Optional explicit database connection string.",
        group: "Persistence",
        jsonType: "string",
        required: false,
        secret: false,
        sensitive: false,
        restartRequired: false,
        advanced: false,
        experimental: false,
        options: []
      },
      {
        name: "PooledContexts",
        displayName: "Pooled contexts",
        description: "Reuse contexts across operations.",
        group: "Persistence",
        jsonType: "boolean",
        required: false,
        defaultValue: false,
        secret: false,
        sensitive: false,
        restartRequired: false,
        advanced: false,
        experimental: false,
        options: []
      },
      {
        name: "Provider",
        displayName: "Provider",
        description: "Sqlite, SqlServer, PostgreSql or MySql; the host supplies the selected provider package.",
        group: "Persistence",
        jsonType: "string",
        required: false,
        defaultValue: "Sqlite",
        secret: false,
        sensitive: false,
        restartRequired: false,
        advanced: false,
        experimental: false,
        options: ["Sqlite", "SqlServer", "PostgreSql", "MySql"].map(value => ({ label: value, value }))
      },
      {
        name: "Schema",
        displayName: "Schema",
        description: "Optional module schema. Ignored on Sqlite and refused on MySql, where the database is named in the connection string.",
        group: "Persistence",
        jsonType: "string",
        required: false,
        secret: false,
        sensitive: false,
        restartRequired: false,
        advanced: false,
        experimental: false,
        options: []
      },
      {
        name: "MigrationOptions",
        displayName: "Migration options",
        description: "Optional migration behavior in JSON.",
        group: "Advanced",
        jsonType: "object",
        required: false,
        defaultValue: {},
        secret: false,
        sensitive: false,
        restartRequired: false,
        advanced: true,
        experimental: false,
        uiHint: "json",
        options: []
      }
    ]
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
  features: [createFeature("StudioFeatureA", false, ["BPMN"]), createFeature("StudioFeatureB", false, ["Persistence"])]
};
const serverCatalog = {
  revision: "server-layout-fixture",
  features: Array.from({ length: 114 }, (_, index) => {
    if (persistenceSettingsFixture && index === 0) {
      return createPersistenceFeature();
    }

    const categories = ["BPMN", "Workflow Activities", "Persistence", "APIs", "Unmapped category"];
    return createFeature(`ServerFeature${String(index + 1).padStart(3, "0")}`, false, [categories[index % categories.length]], index % 17 === 0);
  })
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
