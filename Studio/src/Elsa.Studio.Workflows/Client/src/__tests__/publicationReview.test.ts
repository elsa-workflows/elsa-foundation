import { describe, expect, it } from "vitest";
import type { WorkflowDraft } from "../workflowTypes";
import { createPublicationReview, publicationIntentFor, summarizePublicationChanges } from "../workflow-editor/publicationReview";

describe("publication review model", () => {
  it("summarizes changed activities, inputs, and outputs against the source version", () => {
    const before = {
      rootActivity: activity("root", { activities: [activity("kept", { value: 1 }), activity("removed"), activity("trigger", {}, "trigger-v1")] }),
      inputs: [{ name: "orderId", type: "string" }],
      outputs: [{ name: "result", type: "string" }]
    };
    const after = {
      rootActivity: activity("root", { activities: [activity("kept", { value: 2 }), activity("added"), activity("trigger", { inputs: [{ value: 2 }] }, "trigger-v1")] }),
      inputs: [{ name: "orderId", type: "number" }, { name: "priority", type: "number" }],
      outputs: []
    };

    expect(summarizePublicationChanges(before, after, new Set(["trigger-v1"]))).toEqual({
      activities: { added: 1, changed: 2, removed: 1 },
      inputs: { added: 1, changed: 1, removed: 0 },
      outputs: { added: 0, changed: 0, removed: 1 },
      triggers: { added: 0, changed: 1, removed: 0 }
    });
  });

  it("blocks an invalid draft before publication and protects replacement with the active publication id", () => {
    const review = createPublicationReview({
      draft: draft({ rootActivity: null }),
      details: null,
      sourceVersion: null,
      policy: { defaultAction: "replace", defaultSlotName: "default", source: "host" },
      slots: [{
        definitionId: "definition-1",
        slotName: "default",
        status: "active",
        publication: {
          publicationId: "publication-1",
          definitionId: "definition-1",
          versionId: "version-1",
          artifactId: "artifact-1",
          slotName: "default",
          sourceReferenceId: "reference-1",
          status: "active",
          artifactVersion: "1.0.0"
        }
      }],
      catalog: []
    });

    expect(review.phase).toBe("validationBlocked");
    expect(review.validationErrors).toEqual(["Workflow has no root activity."]);
    expect(review.currentVersion).toBe("1.0.0");
    expect(publicationIntentFor(review, "replace", "default")).toEqual({
      action: "replace",
      slotName: "default",
      expectedPublicationId: "publication-1"
    });
  });

  it("captures an immutable snapshot before publication", () => {
    const original = draft();
    const review = createPublicationReview({
      draft: original,
      details: null,
      sourceVersion: null,
      policy: { defaultAction: "replace", defaultSlotName: "default", source: "host" },
      slots: [],
      catalog: []
    });

    original.state.inputs = [{ name: "late-change" }];

    expect(review.draftSnapshot.state.inputs).toBeUndefined();
  });

  it("requires the author to name a side-by-side slot when policy forbids implicit replacement", () => {
    const review = createPublicationReview({
      draft: draft(),
      details: null,
      sourceVersion: null,
      policy: { defaultAction: "requireExplicitSlot", defaultSlotName: "default", source: "workflow" },
      slots: [],
      catalog: []
    });

    expect(review.intent).toEqual({ action: "sideBySide", slotName: "" });
  });
});

function draft(state: Partial<WorkflowDraft["state"]> = {}): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    sourceVersionId: "version-1",
    state: { rootActivity: activity("root"), ...state },
    layout: [],
    validationErrors: []
  };
}

function activity(nodeId: string, extra: Record<string, unknown> = {}, activityVersionId = `activity-${nodeId}`) {
  return { nodeId, activityVersionId, inputs: [], outputs: [], ...extra };
}
