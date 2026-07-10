import { describe, expect, it } from "vitest";
import {
  buildCanvas,
  getChildSlots,
  planSlotNavigation,
  resolveScope,
  type ScopeFrame
} from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

// Facets mirroring the real backend attributes:
// ForEach:   [ActivityStructure("elsa.foreach.structure", "1.0.0")] => mode "generic"
//            [ActivityChildSlot("ForEach.Body", "body", "Body", Single)]
// Flowchart: [ActivityStructure("elsa.flowchart.structure", "1.0.0", Mode = "flowchart")]
//            [ActivityChildSlot("Flowchart.Activities", "activities", "Activities", Many)]
// Sequence:  [ActivityStructure("elsa.sequence.structure", "1.0.0", Mode = "sequence")]
//            [ActivityChildSlot("Sequence.Activities", "activities", "Activities", Many)]

const writeLine: ActivityCatalogItem = {
  activityVersionId: "wl-v1",
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
  activityVersionId: "fc-v1",
  activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
  displayName: "Flowchart",
  designFacets: [{
    kind: "elsa.flowchart.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "flowchart",
      supportsScopedVariables: true,
      slots: [{ name: "Flowchart.Activities", property: "activities", displayName: "Activities", cardinality: "many" }],
      initialPayload: { activities: [], connections: [], startNodeId: null, nodeMetadata: {}, connectionMetadata: {} }
    }
  }]
};

const sequenceActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "seq-v1",
  activityTypeKey: "Elsa.Activities.Sequence.Activities.Sequence",
  displayName: "Sequence",
  designFacets: [{
    kind: "elsa.sequence.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "sequence",
      supportsScopedVariables: true,
      slots: [{ name: "Sequence.Activities", property: "activities", displayName: "Activities", cardinality: "many" }],
      initialPayload: { activities: [] }
    }
  }]
};

const forEachActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "fe-v1",
  activityTypeKey: "Elsa.Activities.ForEach.Activities.ForEach",
  displayName: "For Each",
  designFacets: [{
    kind: "elsa.foreach.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "generic",
      supportsScopedVariables: true,
      slots: [{ name: "ForEach.Body", property: "body", displayName: "Body", cardinality: "single" }],
      initialPayload: { body: null }
    }
  }]
};

const catalog = [writeLine, flowchartActivity, sequenceActivity, forEachActivity];
const catalogByVersion = new Map(catalog.map(a => [a.activityVersionId, a]));

function leafNode(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] };
}

function flowchartNode(nodeId: string, activities: ActivityNode[]): ActivityNode {
  return {
    nodeId,
    activityVersionId: flowchartActivity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: {
      kind: "elsa.flowchart.structure",
      schemaVersion: "1.0.0",
      payload: { activities, connections: [], startNodeId: null, nodeMetadata: {}, connectionMetadata: {} }
    }
  };
}

function sequenceNode(nodeId: string, activities: ActivityNode[]): ActivityNode {
  return {
    nodeId,
    activityVersionId: sequenceActivity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities } }
  };
}

function forEachNode(nodeId: string, body: ActivityNode | null): ActivityNode {
  return {
    nodeId,
    activityVersionId: forEachActivity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: { kind: "elsa.foreach.structure", schemaVersion: "1.0.0", payload: { body } }
  };
}

const bodySlotOf = (foreach: ActivityNode) => getChildSlots(foreach, catalogByVersion)[0];

describe("planSlotNavigation", () => {
  const innerFlowchart = flowchartNode("fc-inner", []);
  const foreach = forEachNode("fe-1", innerFlowchart);
  const root = flowchartNode("fc-root", [foreach]);

  it("descends through a single flowchart child: canvas shows the container's contents", () => {
    const plan = planSlotNavigation([], root, foreach.nodeId, bodySlotOf(foreach), "For Each / Body", catalogByVersion)!;

    expect(plan.selectedNodeId).toBeNull();
    // Hidden descent hop + visible leaf frame carrying the label.
    expect(plan.frames.map(frame => frame.label)).toEqual(["", "For Each / Body"]);
    const scope = resolveScope(root, plan.frames, catalogByVersion);
    expect(scope?.owner.nodeId).toBe("fc-inner");
    expect(scope?.slot.mode).toBe("flowchart");
  });

  it("descends through a single sequence child", () => {
    const seq = sequenceNode("seq-1", [leafNode("wl-1")]);
    const feWithSeq = forEachNode("fe-2", seq);
    const seqRoot = flowchartNode("fc-root2", [feWithSeq]);

    const plan = planSlotNavigation([], seqRoot, feWithSeq.nodeId, bodySlotOf(feWithSeq), "For Each / Body", catalogByVersion)!;
    const scope = resolveScope(seqRoot, plan.frames, catalogByVersion);
    expect(scope?.owner.nodeId).toBe("seq-1");
    expect(scope?.slot.mode).toBe("sequence");
  });

  it("does NOT descend through a generic container child (nested ForEach)", () => {
    const innerForEach = forEachNode("fe-inner", null);
    const outer = forEachNode("fe-outer", innerForEach);
    const nestedRoot = flowchartNode("fc-root3", [outer]);

    const plan = planSlotNavigation([], nestedRoot, outer.nodeId, bodySlotOf(outer), "For Each / Body", catalogByVersion)!;
    expect(plan.frames).toHaveLength(1);
    expect(plan.selectedNodeId).toBe("fe-inner");
    const scope = resolveScope(nestedRoot, plan.frames, catalogByVersion);
    expect(scope?.owner.nodeId).toBe("fe-outer");
  });

  it("lands on a single leaf child selected", () => {
    const feWithLeaf = forEachNode("fe-3", leafNode("wl-2"));
    const leafRoot = flowchartNode("fc-root4", [feWithLeaf]);

    const plan = planSlotNavigation([], leafRoot, feWithLeaf.nodeId, bodySlotOf(feWithLeaf), "For Each / Body", catalogByVersion)!;
    expect(plan.frames).toHaveLength(1);
    expect(plan.selectedNodeId).toBe("wl-2");
  });

  it("enters an empty single slot unselected", () => {
    const feEmpty = forEachNode("fe-4", null);
    const emptyRoot = flowchartNode("fc-root5", [feEmpty]);

    const plan = planSlotNavigation([], emptyRoot, feEmpty.nodeId, bodySlotOf(feEmpty), "For Each / Body", catalogByVersion)!;
    expect(plan.frames).toHaveLength(1);
    expect(plan.selectedNodeId).toBeNull();
    expect(resolveScope(emptyRoot, plan.frames, catalogByVersion)?.slot.activities).toEqual([]);
  });

  it("retargets the last frame when entering a slot of the current scope owner", () => {
    // Inside "For Each / Body" without descent (leaf body): owner is the ForEach.
    const feWithLeaf = forEachNode("fe-5", leafNode("wl-3"));
    const ownerRoot = flowchartNode("fc-root6", [feWithLeaf]);
    const frames: ScopeFrame[] = [{ ownerNodeId: "fe-5", slotId: bodySlotOf(feWithLeaf).id, label: "For Each / Body" }];

    const plan = planSlotNavigation(frames, feWithLeaf, feWithLeaf.nodeId, bodySlotOf(feWithLeaf), "For Each / Body", catalogByVersion)!;
    // Same depth — the owner frame is retargeted, not appended (an owner is not a child of itself).
    expect(plan.frames).toHaveLength(1);
    expect(plan.frames[0].ownerNodeId).toBe("fe-5");
    expect(resolveScope(ownerRoot, plan.frames, catalogByVersion)?.owner.nodeId).toBe("fe-5");
  });

  it("root owner: primary slot with a single container child descends with ONE frame", () => {
    const rootForEach = forEachNode("fe-root", flowchartNode("fc-in-root", []));
    const plan = planSlotNavigation([], rootForEach, rootForEach.nodeId, bodySlotOf(rootForEach), "For Each / Body", catalogByVersion)!;
    expect(plan.frames).toHaveLength(1);
    expect(resolveScope(rootForEach, plan.frames, catalogByVersion)?.owner.nodeId).toBe("fc-in-root");
  });

  it("root owner: primary slot with a leaf child stays at root with the child selected", () => {
    const rootForEach = forEachNode("fe-root2", leafNode("wl-4"));
    const plan = planSlotNavigation([], rootForEach, rootForEach.nodeId, bodySlotOf(rootForEach), "For Each / Body", catalogByVersion)!;
    expect(plan.frames).toEqual([]);
    expect(plan.selectedNodeId).toBe("wl-4");
  });

  it("root owner: a non-primary slot is not addressable", () => {
    const rootForEach = forEachNode("fe-root3", null);
    const bogusSlot = { ...bodySlotOf(rootForEach), id: "elsa.foreach.structure:other" };
    expect(planSlotNavigation([], rootForEach, rootForEach.nodeId, bogusSlot, "x", catalogByVersion)).toBeNull();
  });

  it("canvas badge slots (node data) produce the same plan as inspector slots", () => {
    const rootScope = resolveScope(root, [], catalogByVersion)!;
    const canvas = buildCanvas(rootScope, catalog, []);
    const feNode = canvas.nodes.find(node => node.id === "fe-1")!;

    const badgePlan = planSlotNavigation([], root, feNode.id, feNode.data.childSlots[0], "For Each / Body", catalogByVersion);
    const inspectorPlan = planSlotNavigation([], root, foreach.nodeId, bodySlotOf(foreach), "For Each / Body", catalogByVersion);
    expect(badgePlan).toEqual(inspectorPlan);
  });
});
