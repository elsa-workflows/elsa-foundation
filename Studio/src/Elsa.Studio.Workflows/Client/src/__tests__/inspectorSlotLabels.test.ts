import { describe, expect, it } from "vitest";
import { describeSlotContents } from "../workflow-editor/editorHelpers";
import type { ChildSlot } from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

const writeLine: ActivityCatalogItem = {
  activityVersionId: "activity-write-line-v1",
  activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
  version: "1.0.0",
  category: "Primitives",
  displayName: "Write Line",
  description: null,
  executionType: "Action",
  inputs: [],
  outputs: [],
  designFacets: []
};

const catalog = new Map<string, ActivityCatalogItem>([[writeLine.activityVersionId, writeLine]]);

function node(activityVersionId: string): ActivityNode {
  return { nodeId: `${activityVersionId}-node`, activityVersionId, inputs: [], outputs: [] };
}

function slot(overrides: Partial<ChildSlot>): ChildSlot {
  return {
    id: "slot",
    label: "Body",
    property: "body",
    cardinality: "many",
    mode: "generic",
    activities: [],
    ...overrides
  };
}

describe("describeSlotContents", () => {
  it("invites a pick for an empty single-cardinality slot", () => {
    expect(describeSlotContents(slot({ cardinality: "single", activities: [] }), catalog)).toBe("Empty — click to choose");
  });

  it("shows the child's friendly display name for a filled single slot", () => {
    const filled = slot({ cardinality: "single", activities: [node(writeLine.activityVersionId)] });
    expect(describeSlotContents(filled, catalog)).toBe("Write Line");
  });

  it("falls back to a short type name when the child is not in the catalog", () => {
    const filled = slot({ cardinality: "single", activities: [node("Some.Namespace.MysteryActivity")] });
    expect(describeSlotContents(filled, catalog)).toBe("MysteryActivity");
    // and gracefully degrades when no catalog is supplied at all
    expect(describeSlotContents(filled)).toBe("MysteryActivity");
  });

  it("keeps a pluralized count for many-cardinality slots", () => {
    expect(describeSlotContents(slot({ cardinality: "many", activities: [] }), catalog)).toBe("0 activities");
    expect(describeSlotContents(slot({ cardinality: "many", activities: [node(writeLine.activityVersionId)] }), catalog)).toBe("1 activity");
    expect(describeSlotContents(slot({
      cardinality: "many",
      activities: [node(writeLine.activityVersionId), node(writeLine.activityVersionId)]
    }), catalog)).toBe("2 activities");
  });
});
