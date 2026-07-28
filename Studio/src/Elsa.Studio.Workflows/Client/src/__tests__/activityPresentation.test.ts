import { describe, expect, it } from "vitest";
import {
  activityDescriptionMaxLength,
  activityDisplayNameMaxLength,
  copyActivityPresentation,
  normalizeActivityPresentation,
  removeActivityPresentation,
  resolveActivityLabel,
  updateActivityPresentation
} from "../activityPresentation";
import { createDraftSnapshotId, getDraftSignature } from "../workflow-editor/editorHelpers";
import type { ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";

const catalogItem: ActivityCatalogItem = {
  activityVersionId: "write-line-v1",
  activityTypeKey: "Elsa.Activities.Console.WriteLine",
  version: "1.0.0",
  category: "Console",
  displayName: "Write Line",
  executionType: "Task",
  inputs: [],
  outputs: []
};

describe("activity presentation metadata", () => {
  it("normalizes boundaries, drops empty rows, and keeps the last duplicate", () => {
    const records = normalizeActivityPresentation([
      { nodeId: " ", displayName: "ignored" },
      { nodeId: "node-1", displayName: "  First  " },
      { nodeId: "node-1", description: "  Useful context  " },
      { nodeId: "node-2", displayName: " ", description: "\n" }
    ]);

    expect(records).toEqual([
      { nodeId: "node-1", displayName: undefined, description: "Useful context" }
    ]);
  });

  it("limits authored values while preserving internal description newlines", () => {
    const records = updateActivityPresentation([], "node-1", {
      displayName: "x".repeat(activityDisplayNameMaxLength + 10),
      description: `first\nsecond${"x".repeat(activityDescriptionMaxLength)}`
    });

    expect(records[0]?.displayName).toHaveLength(activityDisplayNameMaxLength);
    expect(records[0]?.description).toHaveLength(activityDescriptionMaxLength);
    expect(records[0]?.description).toContain("\n");
  });

  it("resolves authored, catalog, and technical labels in order", () => {
    expect(resolveActivityLabel({ nodeId: "node-1", displayName: "Notify buyer" }, catalogItem, catalogItem.activityTypeKey))
      .toBe("Notify buyer");
    expect(resolveActivityLabel(undefined, catalogItem, catalogItem.activityTypeKey)).toBe("Write Line");
    expect(resolveActivityLabel(undefined, undefined, catalogItem.activityTypeKey)).toBe("WriteLine");
  });

  it("copies metadata on duplication and prunes selected node identities", () => {
    const copied = copyActivityPresentation(
      [{ nodeId: "source", displayName: "Notify buyer", description: "After payment." }],
      "source",
      "duplicate");

    expect(copied).toContainEqual({
      nodeId: "duplicate",
      displayName: "Notify buyer",
      description: "After payment."
    });
    expect(removeActivityPresentation(copied, ["source"])).toEqual([
      { nodeId: "duplicate", displayName: "Notify buyer", description: "After payment." }
    ]);
  });

  it("keeps presentation and layout in the draft signature but out of behavioral Test Run identity", () => {
    const draft: WorkflowDraft = {
      id: "draft-1",
      definitionId: "definition-1",
      sourceVersionId: null,
      state: {
        rootActivity: {
          nodeId: "node-1",
          activityVersionId: catalogItem.activityVersionId,
          inputs: [],
          outputs: []
        }
      },
      layout: [],
      activityPresentation: [],
      validationErrors: []
    };
    const presented = {
      ...draft,
      layout: [{ nodeId: "node-1", x: 80, y: 120 }],
      activityPresentation: [{ nodeId: "node-1", displayName: "Notify buyer" }]
    };

    expect(getDraftSignature(presented)).not.toBe(getDraftSignature(draft));
    expect(createDraftSnapshotId(presented)).toBe(createDraftSnapshotId(draft));
  });
});
