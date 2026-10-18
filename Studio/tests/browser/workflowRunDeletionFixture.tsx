import { useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { DialogHost } from "../../src/apps/Elsa.Studio.Web/Client/src/app/ui/dialog/DialogHost";
import { dialogs } from "../../src/apps/Elsa.Studio.Web/Client/src/app/dialogs";
import { setDialogs } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/dialogs";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { WorkflowInstances } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

setDialogs(dialogs);

function Fixture() {
  const [opened, setOpened] = useState("");
  const context = useMemo(() => {
    let runs = ["quote-completed", "quote-active", "quote-retry"];
    let failedOnce = false;
    const deleteEnabled = new URLSearchParams(window.location.search).get("delete") !== "disabled";
    return {
      baseUrl: "run-deletion-fixture",
      http: {
        getJson: async (path: string) => {
          if (path === "/capabilities") return { capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: [
            { rel: "workflow-instances-page", href: "runtime/workflows/instances/page" },
            ...(deleteEnabled ? [{ rel: "workflow-instance-delete", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true }] : [])
          ] }] };
          if (path.startsWith("/runtime/workflows/instances/page")) return {
            items: runs.map((id, index) => ({
              workflowExecutionId: id, artifactId: "customer-quote", definitionId: "quote-definition", definitionVersionId: "quote-v3",
              status: id === "quote-active" ? "Running" : "Completed", subStatus: null, runKind: "PublishedRun",
              createdAt: "2026-10-18T10:00:00Z", startedAt: "2026-10-18T10:00:00Z",
              completedAt: id === "quote-active" ? null : "2026-10-18T10:00:02Z", elapsedMilliseconds: 2000,
              activityCount: 4, incidentCount: index === 2 ? 1 : 0, activeIncidentCount: 0, blockingIncidentCount: 0
            })), hasNext: false, nextCursor: null, count: runs.length, totalCount: runs.length
          };
          throw new Error(`Unexpected fixture read ${path}`);
        },
        deleteJson: async (path: string) => {
          const id = decodeURIComponent(path.split("/").at(-1)!);
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
