import test from "node:test";
import assert from "node:assert/strict";
import {
  applyWorkflowGraphOperationBatchToWorkflow,
  classifyWorkflowGraphOperationBatchForDesigner,
  createDemoWeaverClarificationResult,
  createDemoWeaverGraphOperationBatch,
  getDesignerPosition,
  getWorkflowClarificationSummary,
  summarizeWorkflowDesignerValidation
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

test("summarizes structured clarification results for Studio rendering", () => {
  const clarification = createDemoWeaverClarificationResult();

  const summary = getWorkflowClarificationSummary(clarification);

  assert.equal(summary.title, "Weaver clarification");
  assert.equal(summary.detail, "Which workflow branch should Weaver update?");
  assert.equal(summary.meta, "3 options available");
  assert.deepEqual(clarification.options.map((option) => option.value), ["active-draft", "selected-activity", "new-workflow"]);
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

test("validates the final Weaver workflow authoring MVP path without save or publish", () => {
  const studio = createStudioValidationHarness();
  const providerResult = {
    providerId: "github-copilot",
    resultKind: "workflowGraphOperationBatch",
    payload: createDemoWeaverGraphOperationBatch()
  };

  const applyResult = studio.applyProviderResult(providerResult);

  assert.equal(applyResult.summary.title, "Weaver batch applied");
  assert.match(applyResult.summary.detail, /4 operations applied/);
  assert.match(applyResult.summary.meta, /Validation passed for 1 designer activity/);
  assert.equal(studio.state.dirty, true);
  assert.equal(studio.state.workflowVersionId, "");
  assert.equal(studio.state.artifactId, "");
  assert.equal(studio.state.publishedWorkflowJson, studio.originalPublishedWorkflowJson);
  assert.equal(studio.state.savedWorkflowJson, studio.originalSavedWorkflowJson);
  assert.equal(studio.state.workflow.state.rootActivity.activityVersionId, "Elsa.Email.SendEmail");
  assert.equal(studio.state.workflow.state.rootActivity.inputs[0].value.value, "Hello from Weaver");
  assert.deepEqual(applyResult.validation, {
    valid: true,
    errorCount: 0,
    activityCount: 1,
    errors: [],
    summary: "Validation passed for 1 designer activity"
  });

  const rejectedBatch = createDemoWeaverGraphOperationBatch();
  rejectedBatch.operations = [
    {
      id: "op-remove-root",
      kind: "RemoveActivity",
      parameters: { activityId: "write-hello-world" },
      temporaryReferences: [],
      summary: "Remove root activity."
    }
  ];

  assert.throws(
    () => studio.applyProviderResult({
      providerId: "github-copilot",
      resultKind: "workflowGraphOperationBatch",
      payload: rejectedBatch
    }, { liveRevision: "stale-revision" }),
    /failed direct-apply recheck/
  );
  assert.equal(studio.auditLog.at(-1).outcome, "direct-apply-rejected");
  assert.equal(studio.auditLog.at(-1).resultKind, "proposal");
  assert.equal(studio.state.workflow.state.rootActivity.activityVersionId, "Elsa.Email.SendEmail");
  assert.equal(studio.state.publishedWorkflowJson, studio.originalPublishedWorkflowJson);

  studio.undoLastApply();

  assert.equal(studio.state.dirty, false);
  assert.equal(studio.state.workflowVersionId, "saved-version-1");
  assert.equal(studio.state.artifactId, "published-artifact-1");
  assert.equal(studio.state.workflow.state.rootActivity.activityVersionId, "Elsa.Workflows.WriteLine");
  assert.equal(studio.auditLog.at(0).outcome, "direct-apply-succeeded");
  assert.equal(studio.auditLog.at(0).providerId, "github-copilot");
  assert.equal(studio.auditLog.at(0).persistedWorkflowRevisionChanged, false);
  assert.equal(studio.auditLog.at(1).outcome, "direct-apply-rejected");
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

function createStudioValidationHarness() {
  const workflow = createWorkflow();
  const savedWorkflowJson = JSON.stringify(workflow, null, 2);
  const state = {
    workflow,
    workflowVersionId: "saved-version-1",
    artifactId: "published-artifact-1",
    publishedWorkflowJson: savedWorkflowJson,
    savedWorkflowJson,
    dirty: false
  };
  const auditLog = [];
  let undoSnapshot = null;

  return {
    state,
    auditLog,
    originalSavedWorkflowJson: savedWorkflowJson,
    originalPublishedWorkflowJson: savedWorkflowJson,
    applyProviderResult(providerResult, options = {}) {
      const batch = providerResult.payload;
      const classification = classifyWorkflowGraphOperationBatchForDesigner(state.workflow, batch, {
        canDirectApply: true,
        liveRevision: "demo-revision",
        ...options
      });

      try {
        const previousSnapshot = {
          workflow: JSON.parse(JSON.stringify(state.workflow)),
          workflowVersionId: state.workflowVersionId,
          artifactId: state.artifactId,
          dirty: state.dirty
        };
        const result = applyWorkflowGraphOperationBatchToWorkflow(state.workflow, batch, {
          canDirectApply: true,
          liveRevision: "demo-revision",
          ...options
        });
        const validation = summarizeWorkflowDesignerValidation(state.workflow);
        state.workflowVersionId = "";
        state.artifactId = "";
        state.dirty = true;
        undoSnapshot = previousSnapshot;
        const summary = {
          title: "Weaver batch applied",
          detail: `${result.appliedCount} operations applied as one undoable designer transaction.`,
          meta: `${validation.summary}; final ID: ${result.finalActivityIds.join(", ")}`
        };

        auditLog.push(createAuditRecord(providerResult, "direct-apply-succeeded", "workflowGraphOperationBatch"));
        return { result, validation, summary };
      } catch (error) {
        auditLog.push(createAuditRecord(providerResult, "direct-apply-rejected", classification.resultKind));
        throw error;
      }
    },
    undoLastApply() {
      if (!undoSnapshot)
        throw new Error("No Weaver apply snapshot is available.");

      state.workflow = JSON.parse(JSON.stringify(undoSnapshot.workflow));
      state.workflowVersionId = undoSnapshot.workflowVersionId;
      state.artifactId = undoSnapshot.artifactId;
      state.dirty = undoSnapshot.dirty;
      undoSnapshot = null;
    }
  };
}

function createAuditRecord(providerResult, outcome, resultKind) {
  return {
    outcome,
    providerId: providerResult.providerId,
    resultKind,
    persistedWorkflowRevisionChanged: false,
    operationIds: (providerResult.payload?.operations ?? []).map((operation) => operation.id)
  };
}
