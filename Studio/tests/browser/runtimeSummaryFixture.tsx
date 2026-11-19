import { useState } from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { applyThemeStyleVariables } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { getTheme, themeTokenNames } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import { WorkflowRuntimePanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorPanels";
import type { TestRunObservation } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useTestRunObservation";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

const params = new URLSearchParams(window.location.search);
const mode = params.get("theme") === "dim" ? "dim" : params.get("theme") === "dark" ? "dark" : "light";
const theme = getTheme(params.get("preset") ?? "classic")!;
const palette = mode === "dim" ? theme.dim ?? theme.dark : mode === "dark" ? theme.dark : theme.light;
document.documentElement.dataset.theme = theme.id;
document.documentElement.dataset.themeMode = mode === "light" ? "light" : "dark";
document.documentElement.dataset.themeAppearance = mode;
document.documentElement.dataset.themeLayout = "classic";
applyThemeStyleVariables(document.documentElement, theme);
for (const token of themeTokenNames) document.documentElement.style.setProperty(`--${token.replace(/([A-Z])/g, "-$1").toLowerCase()}`, palette[token]);
document.body.style.margin = "0";

function RuntimeSummaryFixture() {
  const [state, setState] = useState("Completed");
  const [openedRun, setOpenedRun] = useState("");
  const testRun = {
    draftSignature: "current-draft",
    view: {
      testRunId: "test-run-exact-14qxVV1exYE", definitionId: "order-processing", definitionVersionId: "draft:1",
      workflowExecutionId: "run-exact-14qxVV6LzBr", artifactId: "artifact-7e88b5cd7828-exact-very-long-reference-for-copy",
      status: state === "Expired" ? "Expired" : "DispatchAccepted", commandDispatchStatus: "Accepted",
      expiresAt: "2099-11-02T09:00:27Z"
    },
    startLabel: "Direct start", workflowInputNames: ["Order"]
  };
  const observed = {
    details: state === "Accepted" || state === "Disconnected" || state === "Expired" ? null : { instance: {
      status: state === "Paused" ? "Running" : state, workflowExecutionId: testRun.view.workflowExecutionId,
      artifactId: testRun.view.artifactId
    } },
    status: state === "Disconnected" ? "disconnected" : state === "Paused" ? "paused" : state === "Expired" ? "expired" : "connected",
    waits: [], waitsLoading: false, waitsError: "", error: state === "Disconnected" ? "Execution observation disconnected." : "",
    canInteract: state !== "Disconnected" && state !== "Expired" && state !== "Paused", identity: "runtime-panel-fixture",
    revision: 0, following: true, isCurrent: () => true, refresh: () => {}, setFollowing: () => {},
    updateValue: () => {}, refreshWaits: async () => {}, acceptView: () => true
  } as TestRunObservation;
  return <main className="wf-tokens" style={{ maxWidth: 464, margin: "24px auto", minWidth: 0 }}>
    <label style={{ display: "block", margin: "0 12px 16px" }}>Fixture execution state <select aria-label="Fixture execution state" value={state} onChange={event => setState(event.currentTarget.value)}>
      {["Completed", "Faulted", "Suspended", "Accepted", "Paused", "Disconnected", "Expired"].map(value => <option key={value}>{value}</option>)}
    </select></label>
    <WorkflowRuntimePanel testRun={testRun} context={{ baseUrl: "https://runtime.example/api", http: {} } as StudioEndpointContext}
      currentDraftSignature={state === "Paused" ? "changed-draft" : "current-draft"} observation={observed}
      onRunViewUpdated={() => {}} onOpenRun={id => setOpenedRun(id)} />
    <output aria-label="Opened run">{openedRun}</output>
  </main>;
}

createRoot(document.getElementById("root")!).render(<RuntimeSummaryFixture />);
