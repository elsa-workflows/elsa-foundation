import { describe, expect, it } from "vitest";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { findNodeScopePath } from "../workflowAdapter";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";

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

describe("findNodeScopePath", () => {
  const root = sequence("root", [leaf("a"), sequence("c", [leaf("b")])]);

  it("returns an empty path for a direct child of the root scope", () => {
    expect(findNodeScopePath(root, "a")).toEqual([]);
    expect(findNodeScopePath(root, "c")).toEqual([]);
  });

  it("returns the frame path to a nested node's scope", () => {
    expect(findNodeScopePath(root, "b")).toEqual([
      { ownerNodeId: "c", slotId: "elsa.sequence.structure:activities", label: "c" }
    ]);
  });

  it("uses the supplied label resolver for descended containers", () => {
    expect(findNodeScopePath(root, "b", activity => `label:${activity.nodeId}`)?.[0].label).toBe("label:c");
  });

  it("returns an empty path for the root node itself", () => {
    expect(findNodeScopePath(root, "root")).toEqual([]);
  });

  it("returns null for an unknown node", () => {
    expect(findNodeScopePath(root, "missing")).toBeNull();
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

    const variableArg = inputs.find(input => input.referenceKey === "Source");
    expect(variableArg?.value.expressionType).toBe("Variable");
    expect(variableArg?.value.value).toEqual(reference); // object preserved, not JSON-stringified

    const literalArg = inputs.find(input => input.referenceKey === "Text");
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
