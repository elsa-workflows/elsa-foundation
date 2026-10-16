import React from "react";
import { ValidationPanel, WorkflowRuntimePanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorPanels";
import {
  createValidationActivityLabels,
  humanizeValidationErrors
} from "../../src/essentials/Elsa.Studio.Workflows/Client/src/validationActivityLabels";
import { flowchartStructureKind } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/flowchartStartNode";
import type {
  ActivityCatalogItem,
  ActivityNode,
  ValidationError,
  WorkflowDraft,
  WorkflowTestRunView
} from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const writeLine: ActivityCatalogItem = {
  activityVersionId: "write-line-v1",
  activityTypeKey: "Elsa.Activities.Console.WriteLine",
  version: "1.0.0",
  category: "Console",
  displayName: "Write Line",
  executionType: "Task",
  inputs: [{ referenceKey: "text", name: "Text", displayName: "Text" }],
  outputs: []
};

const namedId = "writeline-22ab948a";
const unnamedId = "writeline-7b09879d";

function activity(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] };
}

const rootActivity: ActivityNode = {
  nodeId: "root",
  activityVersionId: "flowchart-v1",
  inputs: [],
  outputs: [],
  structure: {
    kind: flowchartStructureKind,
    schemaVersion: "1",
    payload: {
      activities: [activity(namedId), activity(unnamedId)],
      connections: [{ source: { nodeId: namedId }, target: { nodeId: unnamedId } }],
      startNodeId: namedId
    }
  }
};

const draft: WorkflowDraft = {
  id: "draft-1",
  definitionId: "definition-1",
  sourceVersionId: null,
  state: { rootActivity },
  layout: [],
  activityPresentation: [{ nodeId: namedId, displayName: "Say hello" }],
  validationErrors: []
};

const catalogByVersion = new Map([[writeLine.activityVersionId, writeLine]]);
const activityLabels = createValidationActivityLabels({
  root: rootActivity,
  presentation: draft.activityPresentation,
  catalogByVersion
});

const rawErrors: ValidationError[] = [
  {
    type: "InputOutput/MissingRequired",
    message: `Required input 'Text' on activity '${namedId}' is missing or empty.`,
    path: `${namedId}/inputs/text`
  },
  {
    type: "Structure/DisconnectedNode",
    message: `Activity '${unnamedId}' is not connected to any other activity.`,
    path: unnamedId
  }
];

const rawReason = `VF-ACT-003: Activity node '${namedId}' omits required input 'text', which has no pinned default. Every published input must lower to exactly one canonical binding.`;

const before = new URLSearchParams(window.location.search).get("mode") === "validation-labels-before";

export function ValidationLabelsFixture() {
  const errors = before ? rawErrors : humanizeValidationErrors(rawErrors, activityLabels);
  const testRun: WorkflowTestRunView = {
    testRunId: "test-run-1",
    definitionId: "definition-1",
    definitionVersionId: "draft:1",
    status: "Rejected",
    reason: rawReason
  };

  return (
    <main className="wf-editor validation-labels-fixture" data-testid="validation-labels-fixture" data-copy={before ? "before" : "after"}>
      <section className="wf-runtime-panel" aria-label="Designer validation">
        <ValidationPanel draft={draft} errors={errors} onRepair={() => {}} onSelectNode={() => {}} />
      </section>
      <WorkflowRuntimePanel
        testRun={testRun}
        onOpenRun={() => {}}
        activityLabels={before ? undefined : activityLabels}
      />
    </main>
  );
}
