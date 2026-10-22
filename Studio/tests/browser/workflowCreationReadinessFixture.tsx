import React, { useMemo, useState } from "react";
import type { StudioAiContributionApi } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import type { CreateWorkflowDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorTypes";
import { CreateWorkflowDialog } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/CreateWorkflowDialog";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

const scenario = new URLSearchParams(window.location.search).get("scenario") ?? "ready";
const flowchart = rootActivity("Elsa.Activities.Flowchart.Activities.Flowchart", "Flowchart", "flowchart-v1");
const sequence = rootActivity("Elsa.Sequence", "Sequence", "sequence-v7");
const unavailableFlowchart = { ...flowchart, available: false, availabilityReason: "The selected engine does not expose Flowchart right now." };

export function WorkflowCreationReadinessFixture() {
  const theme = new URLSearchParams(window.location.search).get("theme") === "light" ? "light" : "dark";
  writeAppearancePreferences({ themeId: "classic", mode: theme, navigation: "left" });
  const initialCatalog = useMemo(() => scenario === "root-unavailable"
    ? [unavailableFlowchart]
    : scenario === "http-missing"
      ? [flowchart]
      : [flowchart, sequence], []);
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>(initialCatalog);
  const [catalogState, setCatalogState] = useState<"ready" | "failed" | "loading">(
    scenario === "catalog-loading" ? "loading" : scenario === "catalog-failed" ? "failed" : "ready"
  );
  const [draft, setDraft] = useState<CreateWorkflowDraft>({ name: "", description: "", rootActivityVersionId: null });
  const [open, setOpen] = useState(false);
  const [created, setCreated] = useState("");
  const [submitError, setSubmitError] = useState("");
  const ai = useMemo(() => ({} as StudioAiContributionApi), []);

  return <ThemeProvider>
    <main className="wf-editor" style={{ minHeight: "100vh", padding: "1.5rem" }}>
      <button type="button" onClick={() => setOpen(true)}>Open create workflow</button>
      {created ? <p role="status">Created root version: {created}</p> : null}
      {open ? <CreateWorkflowDialog
        draft={draft}
        catalog={catalog}
        catalogState={catalogState}
        catalogError="The activity catalog could not be loaded."
        creating={false}
        submitError={submitError}
        ai={ai}
        onChange={setDraft}
        onClose={() => setOpen(false)}
        onRetryCatalog={() => {
          setCatalog([flowchart, sequence]);
          setCatalogState("ready");
        }}
        onSubmit={() => {
          if (scenario === "create-failed" && !submitError) {
            setSubmitError("The workflow could not be created. Try again.");
            return;
          }
          setSubmitError("");
          setCreated(draft.rootActivityVersionId ?? "none");
          setOpen(false);
        }}
      /> : null}
    </main>
  </ThemeProvider>;
}

function rootActivity(activityTypeKey: string, displayName: string, activityVersionId: string): ActivityCatalogItem {
  return {
    activityTypeKey,
    activityVersionId,
    version: activityVersionId === "sequence-v7" ? "7.0.0" : "1.0.0",
    category: "Composition",
    displayName,
    executionType: "Action",
    inputs: [],
    outputs: [],
    authoringTemplate: {
      nodeId: `${activityVersionId}-template`,
      activityVersionId,
      inputs: [],
      outputs: [],
      structure: { kind: activityTypeKey, schemaVersion: "1", payload: {} }
    }
  };
}
