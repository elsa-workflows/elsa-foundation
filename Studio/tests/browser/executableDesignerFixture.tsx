import { useState } from "react";
import { createRoot } from "react-dom/client";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { WorkflowExecutableCanvas } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowExecutableInspector";
import { buildExecutableActivityGraph } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/executableGraph";
import { bpmnActivity, sequenceActivity, writeLine } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/fixtures";
import type { ScopeFrame } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter";
import type { WorkflowExecutableDetails, WorkflowExecutableNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

writeAppearancePreferences({ themeId: "classic", mode: new URLSearchParams(location.search).get("theme") === "dark" ? "dark" : "light", workspaceStyle: "linen", navigation: "left" });
const catalog = [bpmnActivity, sequenceActivity, writeLine];
const root: WorkflowExecutableNode = { executableNodeId: "root", authoredActivityId: "root", activityType: sequenceActivity.activityTypeKey,
  activityTypeVersion: "1.0.0", structureKind: "elsa.sequence.structure", inputBindings: [], childSlots: [{ name: "Sequence.Activities", activities: [
    { executableNodeId: "ghost", authoredActivityId: "ghost", activityType: "Unavailable.Activity", activityTypeVersion: "1.0.0", inputBindings: [], childSlots: [] }
  ] }] };
const tree = new URLSearchParams(location.search).get("view") === "bpmn" ? {
  ...root, activityType: bpmnActivity.activityTypeKey, structureKind: "elsa.bpmn.structure",
  childSlots: [{ name: "Bpmn.Activities", activities: [{ executableNodeId: "exec-leaf", authoredActivityId: "leaf", activityType: writeLine.activityTypeKey, activityTypeVersion: "1.0.0", inputBindings: [], childSlots: [] }] }],
  bpmnStructure: { elements: [{ elementId: "task", elementType: "task", childNodeId: "exec-leaf" }], sequenceFlows: [] }
} : root;
const graph = buildExecutableActivityGraph(tree, catalog);
const detail = { artifactId: "artifact-pinned", references: [] } as unknown as WorkflowExecutableDetails;
function Fixture() {
  const [frames, setFrames] = useState<ScopeFrame[]>([]);
  const [selected, select] = useState<string | null>(null);
  return <ThemeProvider><output aria-label="Selected activity">{selected ?? "None"}</output><main style={{ height: "90vh", display: "flex" }}><WorkflowExecutableCanvas
    detail={detail} activityCatalog={catalog} graph={graph} chosenReference={null} displayName="Pinned workflow" frames={frames} onNavigateToScope={setFrames}
    selectedNodeId={selected} onSelectNode={select} /></main></ThemeProvider>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
