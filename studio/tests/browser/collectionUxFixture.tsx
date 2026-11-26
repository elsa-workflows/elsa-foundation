import { useState } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthProvider, type AuthCapabilities, type AuthProviderManager, type AuthSession, type ElsaStudioModuleApi, type StudioWeaverRegistry, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { WorkflowInstances } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import { WorkflowExecutables } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowExecutables";
import { RuntimeDiagnosticsSettingsPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/RuntimeDiagnosticsSettingsPage";
import { AgentsPage } from "../../../extensions/Agent/studio/Elsa.Studio.Agent/Client/src/AgentsPage";
import { ConnectionList } from "../../../extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionList";
import { NamedDashboardsRoute } from "../../../extensions/Dashboard/studio/Elsa.Studio.Dashboard/Client/src/native/NamedDashboards";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/workspaceStyles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";
import "../../../extensions/Agent/studio/Elsa.Studio.Agent/Client/src/styles.css";
import "../../../extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css";

const params = new URLSearchParams(location.search);
writeAppearancePreferences({ themeId: params.has("linen") ? "linen" : "classic", mode: params.get("theme") === "dark" ? "dark" : "light", navigation: "left" });
const settings = {
  requested: { scope: "host-default", defaultLevel: "DiagnosticSnapshot", subjectOverrides: { durableValues: "Metadata" } },
  effective: { defaultLevel: "DiagnosticSnapshot", subjectOverrides: { durableValues: "Metadata" }, limitationReasons: [] },
  hostPolicy: { maximumLevel: "DiagnosticSnapshot", subjectMaximums: { durableValues: "Metadata" }, limitationReasons: ["Full payload capture is disabled by host policy."], snapshotLimits: { maxDepth: 6, maxObjectProperties: 32, maxArrayItems: 32, maxStringLength: 256, maxTotalBytes: 16384 } },
  permissions: { canManage: true, canEnableFullPayloads: false }
};
const context = {
  baseUrl: location.origin,
  http: {
    getJson: async (path: string) => {
      if (path === "/capabilities") return { capabilities: [
        { id: "elsa.api.runtime", contractVersion: "1", links: [{ rel: "workflow-instances-page", href: "runtime/workflows/instances/page" }, { rel: "runtime-diagnostics", href: "runtime/workflows/diagnostics/settings" }, { rel: "workflow-executables", href: "runtime/workflows/executables" }] },
        { id: "elsa.api.workflow-design", contractVersion: "1", links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }] }
      ] };
      if (path.includes("instances/page")) return { items: [], hasNext: false, nextCursor: null, count: 0, totalCount: 0 };
      if (path.includes("diagnostics/settings")) return settings;
      if (path.includes("authoring-capabilities")) return { contractSchemaVersions: ["1"], providers: [{ providerKey: "elsa.agent", manifestSchemas: [{ schemaVersion: "2", isAuthorable: true }], requiredOutcomes: [] }] };
      if (path.includes("design/activities/definitions")) return { items: [], totalCount: 0, hasMore: false };
      if (path.includes("design/workflows/definitions")) return { items: [{ id: "hello", name: "Hello World", createdAt: "2026-11-03T10:00:00Z", lastModifiedAt: "2026-11-03T10:00:00Z", versionCount: 1 }], page: 1, pageSize: 100, totalCount: 1 };
      if (path.includes("runtime/workflows/executables")) return { items: params.has("empty") ? [] : [{
        artifactId: "hello-artifact", artifactVersion: "1.0.0", artifactHash: "sha256:example", definitionId: "hello", definitionVersionId: "hello-v1",
        sourceKind: "WorkflowDefinitionVersion", sourceId: "hello-v1", sourceVersion: "1.0.0", createdAt: "2026-11-03T10:00:00Z",
        rootActivityType: "Elsa.Flowchart", nodeCount: 1, resumeTargetCount: 0,
        references: [{
          sourceReferenceId: "published-hello", artifactId: "hello-artifact", sourceKind: "definition", sourceId: "hello",
          sourceVersion: "1.0.0", definitionId: "hello", definitionVersionId: "hello-v1", artifactVersion: "1.0.0",
          createdAt: "2026-11-03T10:00:00Z", publishedAt: "2026-11-03T10:00:00Z", scope: "Published",
          expiresAt: null, deletedAt: null, deletedReason: null
        }]
      }] };
      throw new Error(`Unexpected fixture read: ${path}`);
    },
    postJson: async () => ({ definition: { definitionId: "agent-new" }, draft: { draftId: "draft-new" } })
  }
} as unknown as StudioEndpointContext;
const ai = { promptActions: { list: () => [] }, dispatchPrompt() {} } as unknown as StudioWeaverRegistry;
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
const dashboardSession: AuthSession = {
  status: "authenticated", subject: "collection-ux", displayName: "Collection UX", tenantId: "fixture", roles: [],
  permissions: ["studio/dashboards:create", "studio/questions:create"], provider: { id: "collection-ux", kind: "fixture" }
};
const dashboardCapabilities: AuthCapabilities = { ownershipMode: "foundation-owned", providers: [] };
const dashboardAuthManager: AuthProviderManager = {
  getSession: () => dashboardSession,
  getCapabilities: async () => dashboardCapabilities,
  initialize: async () => dashboardSession,
  login: async () => undefined,
  handleCallback: async () => dashboardSession,
  logout: async () => undefined,
  getAccessToken: async () => "collection-ux-token",
  refresh: async () => dashboardSession
};
const dashboardApi = {
  backend: {
    baseUrl: location.origin,
    http: {
      getJson: async (url: string) => {
        const request = new URL(url, location.origin);
        if (["/_elsa/studio/dashboards", "/_elsa/studio/questions"].includes(request.pathname)) {
          if (params.has("dashboard-error")) throw new Error("Dashboard collection unavailable.");
          return { items: [] };
        }
        throw new Error(`Unexpected dashboard fixture read: ${request.pathname}`);
      }
    }
  },
  runtime: { hostId: "collection-ux" },
  dialogs: { confirm: async () => false }
} as unknown as ElsaStudioModuleApi;
function Fixture() {
  const [filter, setFilter] = useState<string | null>(null);
  return <ThemeProvider><QueryClientProvider client={queryClient}><main style={{ padding: 24, minWidth: 0 }}>
    {params.get("view") === "connections" ? <section className="studio-page"><h2>Connections</h2><ConnectionList items={[]} descriptors={null} loading={false} error={false} searched={params.has("searched")} onRetry={() => undefined} onClear={() => undefined} onCreate={() => undefined} onOpen={() => undefined} /></section> : params.get("view") === "dashboards" || params.get("view") === "questions" ? <AuthProvider manager={dashboardAuthManager}><NamedDashboardsRoute api={dashboardApi} kind={params.get("view") === "questions" ? "question" : "dashboard"} view="list" navigate={path => { history.pushState({}, "", path); }} /></AuthProvider> : params.get("view") === "runs" ? <section className="studio-page wf-page"><WorkflowInstances context={context} navigate={() => undefined} /></section> : params.get("view") === "settings" ? <RuntimeDiagnosticsSettingsPage context={context} /> : params.get("view") === "agents" ? <AgentsPage context={context} navigate={() => undefined} /> :
      <section className="studio-page wf-page"><WorkflowExecutables context={context} ai={ai} runInputEditors={[]} definitionFilter={filter} onDefinitionFilterChange={setFilter} canBrowseDefinitions canManagePublications /></section>}
  </main></QueryClientProvider></ThemeProvider>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
