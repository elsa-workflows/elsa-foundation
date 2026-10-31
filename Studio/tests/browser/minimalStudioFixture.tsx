import React, { useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import "@xyflow/react/dist/style.css";
import type { ElsaStudioModuleApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ShellFrame, getStudioNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { ThemeSwitcher } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeSwitcher";
import { UserMenu } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/UserMenu";
import { AuthContext } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/auth/AuthContext";
import { AppearancePage } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/settings/AppearancePage";
import { SettingsShell } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/settings/SettingsShell";
import { WorkflowInstancesPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/pages";
import { StructuredLogsPage, register as registerLogs } from "../../../Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module";
import { WorkflowDefinitionBrowseFixture } from "./workflowDefinitionBrowseFixture";
import { ActivityDuplicateEditorFixture } from "./activityDuplicateEditorFixture";
import { WorkflowTemplatesFixture } from "./workflowTemplatesFixture";
import { WorkflowLibraryFixture } from "./workflowLibraryFixture";
import { minimalWorkflowNames } from "./minimalStudioData";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/settings/settings.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/workspaceAppearance.css";
import "./workspaceAppearanceFixture.css";

const navigation = getStudioNavigation([
  { id: "home", label: "Home", path: "/dashboard", placement: "main", order: 10 },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", placement: "main", order: 20 },
  { id: "runs", label: "Runs", path: "/workflows/instances", placement: "main", order: 30 },
  { id: "templates", label: "Templates", path: "/workflows/templates", placement: "more" },
  { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", placement: "more" }
], { includeThemeBuilder: false });

registerLogs({
  navigation: { add() {} }, routes: { add() {} }, panels: { add() {} },
  backend: { baseUrl: window.location.origin, http: { getJson: async (path: string) => {
    const response = await fetch(path);
    if (!response.ok) throw new Error(`Structured log request failed (${response.status}).`);
    return response.json();
  } } }
} as unknown as ElsaStudioModuleApi);

function Runs() {
  const context = useMemo(() => ({ baseUrl: window.location.origin, http: { getJson: async (path: string) => {
    if (path === "/capabilities") return { capabilities: [
      { id: "elsa.api.runtime", contractVersion: "1", links: [{ rel: "workflow-instances-page", href: "minimal/runs" }, { rel: "workflow-instances-health-filter", href: "minimal/runs" }] },
      { id: "elsa.api.workflow-design", contractVersion: "1", links: [{ rel: "workflow-definitions", href: "minimal/definitions" }] }
    ] };
    if (path.startsWith("/minimal/definitions")) return { items: minimalWorkflowNames.map((name, index) => ({ id: `definition-${index}`, name })) };
    if (path.startsWith("/minimal/runs")) return { items: ["Completed", "Running", "Suspended", "Completed", "Faulted", "Completed", "Completed", "Cancelled"].map((status, index) => ({
      workflowExecutionId: `run_${index + 1}f27d9`, definitionId: `definition-${index % 6}`, artifactId: `artifact-${index % 6}`, definitionVersionId: "v6",
      status, subStatus: null, runKind: index === 3 || index === 7 ? "TestRun" : "PublishedRun",
      createdAt: "2026-10-31T10:42:18Z", startedAt: "2026-10-31T10:42:18Z", completedAt: status === "Running" || status === "Suspended" ? null : "2026-10-31T10:42:20Z",
      elapsedMilliseconds: 1800, activityCount: 8, incidentCount: index === 2 || index === 4 ? 1 : 0,
      activeIncidentCount: index === 2 || index === 4 ? 1 : 0, blockingIncidentCount: index === 2 ? 1 : 0
    })), hasNext: false, nextCursor: null, count: 8, totalCount: 8 };
    throw new Error(`Unexpected minimal run read: ${path}`);
  } } } as unknown as StudioEndpointContext), []);
  return <WorkflowInstancesPage context={context} navigate={() => undefined} />;
}

function Fixture() {
  const [view, setView] = useState(new URLSearchParams(location.search).get("screen") ?? new URLSearchParams(location.search).get("view") ?? "appearance");
  const path = view === "appearance" ? "/settings/appearance" : view === "runs" || view === "inspection" ? "/workflows/instances" : view === "templates" ? "/workflows/templates" : view === "logs" ? "/diagnostics/structured-logs" : "/workflows/definitions";
  return <ThemeProvider><ShellFrame navigation={navigation} panels={[]} path={path} title={view === "appearance" ? "Appearance" : view === "runs" || view === "inspection" ? "Runs" : "Workflows"}
    backendBaseUrl="https://backend.example/" themeAction={<ThemeSwitcher />} accountAction={<AuthContext.Provider value={{ session: { status: "authenticated", displayName: "Administrator", roles: [], permissions: [] }, capabilities: null, login: async () => undefined, logout: async () => undefined, refresh: async () => ({ status: "authenticated", roles: [], permissions: [] }) }}><UserMenu /></AuthContext.Provider>} onNavigate={next => setView(next.startsWith("/settings") ? "appearance" : next.includes("instances") ? "runs" : next.includes("templates") ? "templates" : next.includes("diagnostics") ? "logs" : "list")}>
    {view === "appearance" ? <SettingsShell activeId="appearance" groups={[{ id: "studio", label: "Studio", sections: [{ id: "appearance", label: "Appearance", path: "/settings/appearance" }] }]} onNavigate={() => undefined}><AppearancePage onNavigate={() => undefined} /></SettingsShell> : view === "list" ? <WorkflowDefinitionBrowseFixture /> : view === "library" ? <WorkflowLibraryFixture /> : view === "runs" ? <Runs /> : view === "templates" ? <WorkflowTemplatesFixture /> : view === "logs" ? <StructuredLogsPage /> : <ActivityDuplicateEditorFixture />}
  </ShellFrame></ThemeProvider>;
}

createRoot(document.getElementById("root")!).render(<Fixture />);
