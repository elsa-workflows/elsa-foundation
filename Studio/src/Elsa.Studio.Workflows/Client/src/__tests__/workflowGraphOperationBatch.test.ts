import { describe, expect, it } from "vitest";
import { applyWorkflowGraphOperationBatch } from "../workflowGraphOperationBatch";
import type { ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";

describe("workflow graph operation batch", () => {
  it("applies deterministic add root position and property operations to the working draft", () => {
    const draft = workflowDraft();

    const result = applyWorkflowGraphOperationBatch(draft, {
      schemaVersion: "elsa.workflow-graph-operation-batch.v1",
      workflowDefinitionId: "definition-1",
      baseRevision: "rev-1",
      operations: [
        {
          id: "op-add-send-email",
          kind: "add-activity",
          parameters: { activityId: "temp:activity:send-email-1", activityType: "Elsa.Email.SendEmail", displayName: "Send email" },
          temporaryReferences: ["temp:activity:send-email-1"],
          summary: "Add a send email activity."
        },
        {
          id: "op-set-root",
          kind: "set-root",
          parameters: { activityId: "temp:activity:send-email-1" }
        },
        {
          id: "op-set-position",
          kind: "set-designer-position",
          parameters: { activityId: "temp:activity:send-email-1", x: 320, y: 180 }
        },
        {
          id: "op-set-subject",
          kind: "set-activity-property",
          parameters: { activityId: "temp:activity:send-email-1", propertyName: "Subject", value: "Hello from Weaver" }
        }
      ],
      metadata: { source: "deterministic-workflow-authoring" }
    }, [activity("send-email-v1", "Elsa.Email.SendEmail", "Send email")]);

    expect(result.appliedCount).toBe(4);
    expect(result.temporaryReferences).toEqual({ "temp:activity:send-email-1": "activity-send-email-1" });
    expect(result.draft).not.toBe(draft);
    expect(result.draft.sourceVersionId).toBeNull();
    expect(result.draft.state.rootActivity).toMatchObject({
      nodeId: "activity-send-email-1",
      activityVersionId: "send-email-v1",
      subject: {
        typeName: "String",
        expression: { type: "Literal", value: "Hello from Weaver" }
      }
    });
    expect(result.draft.layout).toEqual([{ nodeId: "activity-send-email-1", x: 320, y: 180 }]);
  });

  it("authors a structured property value as an Object expression the backend can deserialize", () => {
    const result = applyWorkflowGraphOperationBatch(workflowDraft(), {
      schemaVersion: "elsa.workflow-graph-operation-batch.v1",
      workflowDefinitionId: "definition-1",
      baseRevision: "rev-1",
      operations: [
        {
          id: "op-add-write-lines",
          kind: "add-activity",
          parameters: { activityId: "temp:activity:write-lines-1", activityType: "Elsa.WriteLines", displayName: "Write Lines" },
          temporaryReferences: ["temp:activity:write-lines-1"]
        },
        { id: "op-set-root", kind: "set-root", parameters: { activityId: "temp:activity:write-lines-1" } },
        {
          id: "op-set-lines",
          kind: "set-activity-property",
          parameters: { activityId: "temp:activity:write-lines-1", propertyName: "Lines", value: ["Hello", "world"] }
        }
      ]
    }, [activity("write-lines-v1", "Elsa.WriteLines", "Write Lines")]);

    // A scalar Literal can't be converted to ICollection<T>; the structured value rides an Object
    // expression so the backend JSON-deserializes it into the target type.
    expect(result.draft.state.rootActivity).toMatchObject({
      lines: {
        typeName: "Object",
        expression: { type: "Object", value: ["Hello", "world"] }
      }
    });
  });
});

function workflowDraft(overrides: Partial<WorkflowDraft> = {}): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    sourceVersionId: "version-1",
    state: { variables: [], rootActivity: null, inputs: [], outputs: [] },
    layout: [],
    validationErrors: [],
    ...overrides
  };
}

function activity(activityVersionId: string, activityTypeKey: string, displayName: string): ActivityCatalogItem {
  return {
    activityVersionId,
    activityTypeKey,
    version: "1.0.0",
    category: "Email",
    displayName,
    executionType: "Activity",
    inputs: [],
    outputs: [],
    designFacets: []
  };
}
