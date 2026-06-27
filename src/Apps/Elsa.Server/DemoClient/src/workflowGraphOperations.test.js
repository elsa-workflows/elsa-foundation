import test from "node:test";
import assert from "node:assert/strict";
import {
  applyWorkflowGraphOperationBatchToWorkflow,
  classifyWorkflowGraphOperationBatchForDesigner,
  createDemoWeaverGraphOperationBatch,
  getDesignerPosition
} from "./workflowGraphOperations.js";

test("applies a workflow graph operation batch as one draft mutation", () => {
  const workflow = createWorkflow();
  const previousJson = JSON.stringify(workflow, null, 2);

  const result = applyWorkflowGraphOperationBatchToWorkflow(workflow, createDemoWeaverGraphOperationBatch(), {
    canDirectApply: true,
    liveRevision: "demo-revision"
  });

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

test("supports whole-batch undo by restoring the previous working-state snapshot", () => {
  const workflow = createWorkflow();
  const undoSnapshot = JSON.stringify(workflow, null, 2);

  applyWorkflowGraphOperationBatchToWorkflow(workflow, createDemoWeaverGraphOperationBatch(), {
    canDirectApply: true,
    liveRevision: "demo-revision"
  });
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
    () => applyWorkflowGraphOperationBatchToWorkflow(workflow, batch, { canDirectApply: true, liveRevision: "demo-revision" }),
    /failed direct-apply recheck: destructiveOperation/
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
    () => applyWorkflowGraphOperationBatchToWorkflow(workflow, batch, { canDirectApply: true, liveRevision: "demo-revision" }),
    /failed direct-apply recheck: uncertain/
  );
  assert.equal(JSON.stringify(workflow, null, 2), previousJson);
});

test("classifies low-risk batches for direct apply", () => {
  const workflow = createWorkflow();

  const result = classifyWorkflowGraphOperationBatchForDesigner(workflow, createDemoWeaverGraphOperationBatch(), {
    canDirectApply: true,
    liveRevision: "demo-revision"
  });

  assert.equal(result.canDirectApply, true);
  assert.equal(result.decision, "directApply");
  assert.equal(result.resultKind, "workflowGraphOperationBatch");
  assert.deepEqual(result.reasons, ["lowRisk"]);
});

test("fails closed for stale or destructive direct apply batches", () => {
  const workflow = createWorkflow();
  const previousJson = JSON.stringify(workflow, null, 2);
  const batch = createDemoWeaverGraphOperationBatch();
  batch.operations = [
    {
      id: "op-remove-root",
      kind: "RemoveActivity",
      parameters: {
        activityId: "write-hello-world"
      },
      temporaryReferences: [],
      summary: "Remove root activity."
    }
  ];

  const classification = classifyWorkflowGraphOperationBatchForDesigner(workflow, batch, {
    canDirectApply: true,
    liveRevision: "newer-revision"
  });

  assert.equal(classification.canDirectApply, false);
  assert.equal(classification.decision, "proposal");
  assert.deepEqual(classification.reasons, ["staleRevision", "destructiveOperation", "uncertain"]);
  assert.throws(
    () => applyWorkflowGraphOperationBatchToWorkflow(workflow, batch, { canDirectApply: true, liveRevision: "newer-revision" }),
    /failed direct-apply recheck/
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
