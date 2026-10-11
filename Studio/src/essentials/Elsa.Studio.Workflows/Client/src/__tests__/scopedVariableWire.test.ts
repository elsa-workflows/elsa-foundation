import { describe, expect, it } from "vitest";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { findNodeScopePath, resolveScope } from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode, WorkflowDefinitionState } from "../workflowTypes";

const leaf = (nodeId: string, extra: Record<string, unknown> = {}): ActivityNode => ({
  nodeId,
  activityVersionId: "leaf-v1",
  inputs: [],
  outputs: [],
  ...extra
});

const sequence = (nodeId: string, activities: ActivityNode[]): ActivityNode => ({
  nodeId,
  activityVersionId: "seq-v1",
  inputs: [],
  outputs: [],
  structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities, variables: [] } }
});

// A facet-backed container with two single-cardinality slots, to exercise findNodeScopePath landing a
// node in a NON-primary slot (impossible under the old primary-slot-only semantics).
const twoSlotCatalog: ActivityCatalogItem = {
  activityVersionId: "two-slot-v1",
  activityTypeKey: "Acme.TwoSlot",
  version: "1.0.0",
  category: "Composition",
  displayName: "Two Slot",
  description: null,
  executionType: "Action",
  inputs: [],
  outputs: [],
  designFacets: [{
    kind: "acme.two-slot.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "sequence",
      supportsScopedVariables: false,
      slots: [
        { name: "Primary", property: "primary", displayName: "Primary", cardinality: "single" },
        { name: "Secondary", property: "secondary", displayName: "Secondary", cardinality: "single" }
      ],
      initialPayload: { primary: null, secondary: null }
    }
  }]
};

const twoSlot = (nodeId: string, primary: ActivityNode | null, secondary: ActivityNode | null): ActivityNode => ({
  nodeId,
  activityVersionId: twoSlotCatalog.activityVersionId,
  inputs: [],
  outputs: [],
  structure: { kind: "acme.two-slot.structure", schemaVersion: "1.0.0", payload: { primary, secondary } }
});

describe("findNodeScopePath", () => {
  // Contract: findNodeScopePath returns frames such that resolveScope(root, frames).slot.activities
  // contains nodeId. The last frame names the container that directly holds the node and its slot.
  const root = sequence("root", [leaf("a"), sequence("c", [leaf("b")])]);

  it("returns an empty path for a direct child of the root scope", () => {
    expect(findNodeScopePath(root, "a")).toEqual([]);
    expect(findNodeScopePath(root, "c")).toEqual([]);
  });

  it("returns the frame path to a nested node's scope with an Owner / Slot crumb", () => {
    expect(findNodeScopePath(root, "b")).toEqual([
      { ownerNodeId: "c", slotId: "elsa.sequence.structure:activities", label: "c / Activities" }
    ]);
  });

  it("uses the supplied label resolver for descended containers", () => {
    expect(findNodeScopePath(root, "b", activity => `label:${activity.nodeId}`)?.[0].label).toBe("label:c / Activities");
  });

  it("returns an empty path for the root node itself", () => {
    expect(findNodeScopePath(root, "root")).toEqual([]);
  });

  it("returns null for an unknown node", () => {
    expect(findNodeScopePath(root, "missing")).toBeNull();
  });

  it("lands a node that lives in a child container's non-primary slot and round-trips", () => {
    // The target sits in the SECONDARY slot of a child container — reachable only because the last
    // frame descends into that child and names its secondary slot (impossible under old semantics).
    const container = twoSlot("container", leaf("in-primary"), leaf("in-secondary"));
    const rootWithChild = sequence("root", [container]);

    const path = findNodeScopePath(rootWithChild, "in-secondary", activity => activity.nodeId, twoSlotCatalog);

    expect(path).toEqual([
      { ownerNodeId: "container", slotId: "acme.two-slot.structure:secondary", label: "container / Secondary" }
    ]);
    const scope = resolveScope(rootWithChild, path!, twoSlotCatalog);
    expect(scope?.slot.activities.map(activity => activity.nodeId)).toEqual(["in-secondary"]);
  });

  it("returns null for a node stranded in the root's own non-primary slot", () => {
    // Frames only ever descend into children, so the root's non-primary slots are unreachable — the
    // canvas can't surface them either, so navigation must honestly report "no path".
    const rootWithSlots = twoSlot("root", leaf("in-primary"), leaf("in-secondary"));

    expect(findNodeScopePath(rootWithSlots, "in-secondary", activity => activity.nodeId, twoSlotCatalog)).toBeNull();
  });
});

describe("Variable expression wire round-trip", () => {
  const reference = { referenceKey: "count-ref", declaringScopeId: "container-1" };

  const state = (): WorkflowDefinitionState => ({
    rootActivity: sequence("root", [
      leaf("writer", {
        text: { typeName: "System.String", expression: { type: "Literal", value: "hi" } },
        source: { typeName: "System.Object", expression: { type: "Variable", value: { ...reference } } }
      })
    ])
  });

  it("keeps a Variable reference as a structured object while literals serialize to strings", () => {
    const writer = activitiesOf(canonicalizeStateForWire(state()))[0];
    const inputs = writer.inputs as Array<{ referenceKey: string; value: { value: unknown; expressionType: string } }>;

    const variableArg = inputs.find(input => input.referenceKey === "source");
    expect(variableArg?.value.expressionType).toBe("Variable");
    expect(variableArg?.value.value).toEqual(reference); // object preserved, not JSON-stringified

    const literalArg = inputs.find(input => input.referenceKey === "text");
    expect(literalArg?.value.value).toBe("hi");
  });

  it("restores the structured reference on expand", () => {
    const restored = expandStateFromWire(canonicalizeStateForWire(state()));
    const writer = activitiesOf(restored)[0] as Record<string, unknown>;
    const source = writer.source as { expression: { type: string; value: unknown } };

    expect(source.expression.type).toBe("Variable");
    expect(source.expression.value).toEqual(reference);
  });
});

function activitiesOf(state: WorkflowDefinitionState): ActivityNode[] {
  return (state.rootActivity?.structure?.payload.activities as ActivityNode[]) ?? [];
}
