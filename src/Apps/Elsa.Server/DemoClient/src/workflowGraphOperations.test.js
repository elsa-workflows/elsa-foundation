import test from "node:test";
import assert from "node:assert/strict";
import {
  applyWorkflowGraphOperationBatchToWorkflow,
  collectActivityVersionIds,
  createDemoWeaverGraphOperationBatch,
  findActivityAvailabilityDiagnostic,
  getDesignerPosition
} from "./workflowGraphOperations.js";

test("applies a workflow graph operation batch as one draft mutation", () => {
  const workflow = createWorkflow();
  const previousJson = JSON.stringify(workflow, null, 2);

  const result = applyWorkflowGraphOperationBatchToWorkflow(workflow, createDemoWeaverGraphOperationBatch());

  assert.equal(result.appliedCount, 4);
  assert.deepEqual(result.finalActivityIds, ["activity-send-email-1"]);
  assert.equal(result.temporaryReferences["temp:activity:send-email-1"], "activity-send-email-1");
  assert.equal(workflow.state.rootActivity.nodeId, "activity-send-email-1");
  assert.equal(workflow.state.rootActivity.activityVersionId, "Elsa.Email.SendEmail");
  assert.deepEqual(workflow.state.rootActivity.designer.position, { x: 320, y: 180 });
  assert.equal(workflow.state.rootActivity.inputs[0].referenceKey, "Subject");
  assert.equal(workflow.state.rootActivity.inputs[0].value.value, "Hello from Weaver");
  assert.notEqual(JSON.stringify(workflow, null, 2), previousJson);
});

test("normalizes invalid designer positions before rendering", () => {
  const activity = {
    designer: {
      position: {
        x: -50,
        y: "12.8"
      }
    }
  };

  assert.deepEqual(getDesignerPosition(activity, { x: 280, y: 160 }), { x: 40, y: 40 });
});

test("collects authored activity version ids without consulting picker addability", () => {
  const workflow = createWorkflow();
  workflow.state.rootActivity.structure = {
    kind: "elsa.sequence.structure",
    payload: {
      activities: [
        { nodeId: "child-one", activityVersionId: "version-hidden" },
        { nodeId: "child-two", activityVersionId: "version-available" }
      ]
    }
  };

  assert.deepEqual(
    [...collectActivityVersionIds(workflow.state.rootActivity)],
    ["Elsa.Workflows.WriteLine", "version-hidden", "version-available"]
  );
});

test("skips unresolved activity version template tokens", () => {
  const workflow = createWorkflow();
  workflow.state.rootActivity.activityVersionId = "{{writeLineActivityVersionId}}";

  assert.deepEqual([...collectActivityVersionIds(workflow.state.rootActivity)], []);
});

test("matches non-addable diagnostics through authored version definitions", () => {
  const activity = { activityVersionId: "version-hidden" };
  const diagnostics = {
    items: [
      {
        activityDefinitionId: "definition-hidden",
        activityTypeKey: "Elsa.Hidden",
        state: "HiddenByManagementSettings"
      }
    ]
  };
  const versionDefinitions = {
    "version-hidden": {
      id: "definition-hidden",
      activityTypeKey: "Elsa.Hidden"
    }
  };

  assert.equal(
    findActivityAvailabilityDiagnostic(activity, diagnostics, versionDefinitions),
    diagnostics.items[0]
  );
});

test("does not warn for addable authored activity diagnostics", () => {
  const activity = { activityVersionId: "version-available" };
  const diagnostics = {
    items: [
      {
        activityDefinitionId: "definition-available",
        activityTypeKey: "Elsa.Available",
        state: "Available"
      }
    ]
  };
  const versionDefinitions = {
    "version-available": {
      id: "definition-available",
      activityTypeKey: "Elsa.Available"
    }
  };

  assert.equal(findActivityAvailabilityDiagnostic(activity, diagnostics, versionDefinitions), null);
});

test("supports whole-batch undo by restoring the previous working-state snapshot", () => {
  const workflow = createWorkflow();
  const undoSnapshot = JSON.stringify(workflow, null, 2);

  applyWorkflowGraphOperationBatchToWorkflow(workflow, createDemoWeaverGraphOperationBatch());
  const restored = JSON.parse(undoSnapshot);

  assert.equal(restored.state.rootActivity.nodeId, "write-hello-world");
  assert.equal(restored.state.rootActivity.inputs[0].value.value, "Hello World");
});

test("rejects unsupported operations without mutating the workflow", () => {
  const workflow = createWorkflow();
  const previousJson = JSON.stringify(workflow, null, 2);
  const batch = createDemoWeaverGraphOperationBatch();
  batch.operations = [
    ...batch.operations,
    {
      id: "op-disconnect",
      kind: "DisconnectActivities",
      parameters: {},
      temporaryReferences: [],
      summary: "Unsupported by the demo designer apply path."
    }
  ];

  assert.throws(
    () => applyWorkflowGraphOperationBatchToWorkflow(workflow, batch),
    /operation 'DisconnectActivities' is not supported/
  );
  assert.equal(JSON.stringify(workflow, null, 2), previousJson);
});

test("rejects unknown operations without mutating the workflow", () => {
  const workflow = createWorkflow();
  const previousJson = JSON.stringify(workflow, null, 2);
  const batch = createDemoWeaverGraphOperationBatch();
  batch.operations = [
    ...batch.operations,
    {
      id: "op-unknown",
      kind: "ReplaceEverything",
      parameters: {},
      temporaryReferences: [],
      summary: "Unknown provider operation."
    }
  ];

  assert.throws(
    () => applyWorkflowGraphOperationBatchToWorkflow(workflow, batch),
    /operation 'ReplaceEverything' is not supported/
  );
  assert.equal(JSON.stringify(workflow, null, 2), previousJson);
});

function createWorkflow() {
  return {
    name: "Hello World",
    description: "Writes Hello World through the WriteLine activity.",
    state: {
      variables: [],
      rootActivity: {
        nodeId: "write-hello-world",
        activityVersionId: "Elsa.Workflows.WriteLine",
        inputs: [
          {
            referenceKey: "Text",
            value: {
              value: "Hello World",
              expressionType: "Literal"
            },
            autoEvaluate: null,
            evaluatorType: null,
            storageDriverType: null,
            isSensitive: null
          }
        ],
        outputs: []
      },
      inputs: [],
      outputs: [],
      workflowActivityOptions: null,
      strategyOptions: null
    }
  };
}
