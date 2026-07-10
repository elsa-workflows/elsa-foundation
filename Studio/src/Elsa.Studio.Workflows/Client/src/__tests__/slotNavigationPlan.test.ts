import { describe, expect, it } from "vitest";
import {
  buildCanvas,
  getChildSlots,
  planSlotNavigation,
  resolveScope,
  type ScopeFrame
} from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";
import {
  flowchartActivity,
  flowchartNode,
  forEachActivity,
  forEachNode,
  leafNode,
  sequenceActivity,
  sequenceNode,
  writeLine
} from "./fixtures";

const catalog = [writeLine, flowchartActivity, sequenceActivity, forEachActivity];
const catalogByVersion = new Map(catalog.map(a => [a.activityVersionId, a]));

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
