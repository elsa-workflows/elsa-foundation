import { describe, expect, it } from "vitest";
import { planActivityDrop } from "../workflow-editor/addActivityRouting";
import {
  createActivityNode,
  flowchartStructureKind,
  type ScopeFrame
} from "../workflowAdapter";
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

const flowchartActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-flowchart-v1",
  activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
  category: "Composition",
  displayName: "Flowchart"
};

const forEachActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-for-each-v1",
  activityTypeKey: "Elsa.Activities.ControlFlow.Activities.ForEach",
  category: "Control Flow",
  displayName: "For Each",
  designFacets: [{
    kind: "elsa.foreach.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "sequence",
      supportsScopedVariables: true,
      slots: [{ name: "Body", property: "body", displayName: "Body", cardinality: "single" }],
      initialPayload: { body: null }
    }
  }]
};

const catalog = [writeLine, flowchartActivity, forEachActivity];

function leaf(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [], structure: null };
}

function flowchartRoot(activities: ActivityNode[]): ActivityNode {
  return {
    nodeId: "root",
    activityVersionId: flowchartActivity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: { kind: flowchartStructureKind, schemaVersion: "1.0.0", payload: { activities, connections: [] } }
  };
}

function enterSlotFrame(ownerNodeId: string, slotId: string): ScopeFrame {
  return { ownerNodeId, slotId, label: `${ownerNodeId} / ${slotId}` };
}

describe("planActivityDrop", () => {
  it("routes the first drop into becoming the workflow root", () => {
    const next = createActivityNode(writeLine, "first");
    expect(planActivityDrop(null, [], next, writeLine, catalog)).toEqual({ kind: "becomeRoot" });
  });

  it("wraps a bare leaf root only when the dropped activity is itself a container (top level)", () => {
    const leafRoot = leaf("root");
    const droppedContainer = createActivityNode(flowchartActivity, "wrapper");
    expect(planActivityDrop(leafRoot, [], droppedContainer, flowchartActivity, catalog))
      .toEqual({ kind: "wrapRoot" });
  });

  it("reports a leaf error when a leaf is dropped onto a bare leaf root (top level)", () => {
    const leafRoot = leaf("root");
    const droppedLeaf = createActivityNode(writeLine, "dropped");
    expect(planActivityDrop(leafRoot, [], droppedLeaf, writeLine, catalog))
      .toEqual({ kind: "leafError" });
  });

  it("never wraps or leaf-errors the root while inside a slot — a failed resolve is a stale frame", () => {
    const leafRoot = leaf("root");
    const droppedContainer = createActivityNode(flowchartActivity, "wrapper");
    // frames non-empty but nothing resolves (a leaf root has no children to descend into).
    const plan = planActivityDrop(leafRoot, [enterSlotFrame("ghost", "elsa.foreach.structure:body")], droppedContainer, flowchartActivity, catalog);
    expect(plan).toEqual({ kind: "staleFrames" });
  });

  it("returns staleFrames when a frame's owner no longer exists in the current tree", () => {
    const forEach = createActivityNode(forEachActivity, "foreach");
    const root = flowchartRoot([forEach]);
    const plan = planActivityDrop(root, [enterSlotFrame("missing", "elsa.foreach.structure:body")], leaf("x"), writeLine, catalog);
    expect(plan).toEqual({ kind: "staleFrames" });
  });

  it("appends into a many-cardinality slot without reporting a replacement", () => {
    const existing = leaf("existing");
    const root = flowchartRoot([existing]);
    const plan = planActivityDrop(root, [], leaf("added"), writeLine, catalog);
    expect(plan.kind).toBe("addToSlot");
    if (plan.kind !== "addToSlot") throw new Error("expected addToSlot");
    expect(plan.slot.cardinality).toBe("many");
    expect(plan.replacedActivity).toBeNull();
  });

  it("sets a single-cardinality slot and reports the replaced activity for a replace toast", () => {
    const forEach = createActivityNode(forEachActivity, "foreach");
    // Pre-populate the ForEach body slot with an existing child.
    forEach.structure = { ...forEach.structure!, payload: { body: leaf("old-body") } };
    const root = flowchartRoot([forEach]);
    const frame = enterSlotFrame("foreach", "elsa.foreach.structure:body");

    const plan = planActivityDrop(root, [frame], leaf("new-body"), writeLine, catalog);
    expect(plan.kind).toBe("addToSlot");
    if (plan.kind !== "addToSlot") throw new Error("expected addToSlot");
    expect(plan.slot.cardinality).toBe("single");
    expect(plan.slot.label).toBe("Body");
    expect(plan.replacedActivity?.nodeId).toBe("old-body");
  });

  it("sets an empty single-cardinality slot without reporting a replacement", () => {
    const forEach = createActivityNode(forEachActivity, "foreach");
    const root = flowchartRoot([forEach]);
    const frame = enterSlotFrame("foreach", "elsa.foreach.structure:body");

    const plan = planActivityDrop(root, [frame], leaf("body-child"), writeLine, catalog);
    expect(plan.kind).toBe("addToSlot");
    if (plan.kind !== "addToSlot") throw new Error("expected addToSlot");
    expect(plan.replacedActivity).toBeNull();
  });
});
