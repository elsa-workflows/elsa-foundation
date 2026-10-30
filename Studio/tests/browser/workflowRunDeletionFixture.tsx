import { useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { DialogHost } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/dialog/DialogHost";
import { dialogs } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/dialogs";
import { setDialogs } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/dialogs";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { WorkflowInstances } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

setDialogs(dialogs);

const deletionRequests: string[] = [];
(window as Window & { workflowRunDeletionRequests?: string[] }).workflowRunDeletionRequests = deletionRequests;

function Fixture() {
  const parameters = new URLSearchParams(window.location.search);
  writeAppearancePreferences({ themeId: "classic", mode: parameters.get("theme") === "light" ? "light" : "dark", navigation: "left" });
  const [opened, setOpened] = useState("");
  const context = useMemo(() => {
    const cleanList = new URLSearchParams(window.location.search).has("clean-list");
    const statuses = new URLSearchParams(window.location.search).get("statuses") === "all"
      ? ["Pending", "Running", "Suspended", "Completed", "Faulted", "Cancelled", "Unknown"]
      : undefined;
    let runs = statuses?.map(status => `status-${status.toLowerCase()}`) ?? ["quote-completed", "quote-active", "quote-retry"];
    const statusByRunId = new Map((statuses ?? []).map(status => [`status-${status.toLowerCase()}`, status] as const));
    let failedOnce = false;
    const deleteEnabled = new URLSearchParams(window.location.search).get("delete") !== "disabled";
    const delayDeletion = new URLSearchParams(window.location.search).get("delete") === "delayed";
    return {
      baseUrl: "run-deletion-fixture",
      http: {
        getJson: async (path: string) => {
          if (path === "/capabilities") return { capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: [
            { rel: "workflow-instances-page", href: "runtime/workflows/instances/page" },
            ...(deleteEnabled ? [{ rel: "workflow-instance-delete", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true }] : [])
          ] }, ...(cleanList ? [{ id: "elsa.api.workflow-design", contractVersion: "1", links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }] }] : [])] };
          if (path.startsWith("/design/workflows/definitions")) return { items: [{ id: "quote-definition", name: "Unified workspace qualification — interaction" }] };
          if (path.startsWith("/runtime/workflows/instances/page")) return {
            items: runs.map((id, index) => ({
              workflowExecutionId: id, artifactId: "customer-quote", definitionId: "quote-definition", definitionVersionId: "quote-v3",
              status: cleanList && index === 0 ? "Faulted" : statusByRunId.get(id) ?? (id === "quote-active" ? "Running" : "Completed"), subStatus: null, runKind: "PublishedRun",
              createdAt: "2026-10-18T10:00:00Z", startedAt: "2026-10-18T10:00:00Z",
              completedAt: id === "quote-active" || statusByRunId.get(id) === "Running" ? null : "2026-10-18T10:00:02Z", elapsedMilliseconds: 2000,
              activityCount: 4, incidentCount: !statuses && index === 2 ? 1 : 0, activeIncidentCount: cleanList && index === 0 ? 2 : 0, blockingIncidentCount: cleanList && index === 0 ? 1 : 0
            })), hasNext: false, nextCursor: null, count: runs.length, totalCount: runs.length
          };
          throw new Error(`Unexpected fixture read ${path}`);
        },
        deleteJson: async (path: string) => {
          const id = decodeURIComponent(path.split("/").at(-1)!);
          deletionRequests.push(id);
          if (delayDeletion) await new Promise(resolve => window.setTimeout(resolve, 1_500));
          if (id === "quote-retry" && !failedOnce) { failedOnce = true; throw new Error("The run is still stopping. Wait for it to stop, then retry deletion."); }
          runs = runs.filter(run => run !== id);
          return { workflowExecutionId: id, status: "Deleted" };
        }
      }
    } as unknown as StudioEndpointContext;
  }, []);
  return <ThemeProvider><main className="wf-editor" style={{ padding: "1rem" }}>
    <h1>Runs</h1><WorkflowInstances context={context} navigate={path => {
      if (/\/instances\/[^?]+$/.test(path)) setOpened(path);
      else window.history.replaceState({}, "", path);
    }} />
    {opened ? <output>Opened {opened}</output> : null}
  </main><DialogHost /></ThemeProvider>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
