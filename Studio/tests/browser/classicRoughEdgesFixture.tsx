import React, { useEffect, useMemo } from "react";
import { ShellFrame, getStudioNavigation } from "../../src/apps/Elsa.Studio.Web/Client/src/app/App";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import type { StudioAiContributionApi, StudioEndpointContext, StudioNavigationContribution } from "../../src/apps/Elsa.Studio.Web/Client/src/sdk";
import { WorkflowDefinitions } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

const searchParams = new URLSearchParams(window.location.search);
const view = searchParams.get("view") ?? "definitions";
const dark = searchParams.get("theme") === "dark";

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", activePathPrefix: "/workflows", order: 20, placement: "main" },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", activePathPrefix: "/workflows/instances", order: 30, placement: "main" }
];

function definition(overrides: Record<string, unknown>) {
  return {
    id: "definition-1",
    name: "Order fulfillment",
    description: "Validates an order, reserves stock and notifies the customer.",
    createdAt: "2026-10-14T10:56:00Z",
    lastModifiedAt: "2026-10-14T10:56:00Z",
    latestVersion: "1.0.0",
    latestVersionId: "version-1",
    versionCount: 1,
    draftId: "draft-1",
    deletedAt: null,
    ...overrides
  };
}

export function ClassicRoughEdgesFixture() {
  writeAppearancePreferences({ themeId: "classic", mode: dark ? "dark" : "light", navigation: "left" });
  const path = view === "runs" || view === "nav" ? "/workflows/instances" : "/workflows/definitions";
  const navigation = getStudioNavigation(moduleNavigation);

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", "classic");
    document.documentElement.setAttribute("data-theme-mode", dark ? "dark" : "light");
    document.documentElement.setAttribute("data-theme-appearance", dark ? "dark" : "light");
  }, []);

  const context = useMemo(() => ({
    baseUrl: "browser-classic-rough-edges",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") {
          return {
            capabilities: [{
              id: "elsa.api.workflow-design",
              contractVersion: "1",
              links: [{ rel: "workflow-definitions", href: "browser/definitions" }]
            }]
          };
        }
        if (url.startsWith("/browser/definitions")) {
          return {
            items: [
              definition({ id: "order", name: "Order fulfillment", draftId: "draft-order" }),
              definition({ id: "employee", name: "Employee onboarding", draftId: null }),
              definition({
                id: "draft-only",
                name: "Nightly data sync",
                draftId: "draft-sync",
                latestVersion: null,
                latestVersionId: null,
                versionCount: 0
              })
            ]
          };
        }
        throw new Error(`Unexpected browser fixture request: ${url}`);
      }
    }
  }) as unknown as StudioEndpointContext, []);
  const ai = useMemo(() => ({ promptActions: { list: () => [] }, dispatchPrompt: () => undefined }) as unknown as StudioAiContributionApi, []);

  return (
    <ThemeProvider>
      <ShellFrame
        navigation={navigation}
        panels={[]}
        path={path}
        title={view === "runs" || view === "nav" ? "Runs" : "Workflows"}
        backendBaseUrl="https://backend.example/"
        onNavigate={() => undefined}
      >
        {view === "definitions" ? <WorkflowDefinitions context={context} ai={ai} onOpen={() => undefined} /> : <RunsPreview />}
      </ShellFrame>
    </ThemeProvider>
  );
}

function RunsPreview() {
  return (
    <div className="wf-grid wf-instance-grid" role="table" aria-label="Workflow runs">
      <div className="wf-grid-head" role="row">
        <span>Run</span>
        <span>Kind</span>
        <span>Status</span>
        <span>Health</span>
        <span>Definition</span>
        <span>Activity</span>
        <span>Started</span>
        <span>Duration</span>
      </div>
      <div className="wf-grid-row" role="row">
        <span><strong>wfexec-1</strong><small>artifact-1</small></span>
        <span>Published Run</span>
        <span><span className="wf-status-badge" data-status="completed">Completed</span></span>
        <span>No active incidents</span>
        <span className="wf-identity-cell" title="Order fulfillment (14LSBmpoVN)">
          <strong>Order fulfillment</strong>
          <small>14LSBmpoVN</small>
        </span>
        <span><strong>3 activities</strong><small>0 incidents</small></span>
        <span>10/14/2026, 10:56 AM</span>
        <span>2s</span>
      </div>
    </div>
  );
}
