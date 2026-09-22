import { describe, expect, it } from "vitest";
import {
  buildIntrinsicWireBlock,
  isIntrinsicCatalogItem,
  readIntrinsicDescriptor,
  readIntrinsicVariable,
  readNodeIntrinsic,
  writeIntrinsicVariable
} from "../intrinsicActivities";
import { createActivityNode } from "../workflowAdapter";
import { groupActivityPalette } from "../workflow-editor/editorHelpers";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import type { ActivityCatalogItem, ActivityNode, WorkflowDefinitionState } from "../workflowTypes";
import { setOutputActivity, setVariableActivity, writeLine } from "./fixtures";

describe("intrinsic authoring descriptors", () => {
  it("reads the additive intrinsic block from a catalog item", () => {
    expect(readIntrinsicDescriptor(setVariableActivity)).toEqual({
      kind: "Set",
      valueInputKey: "value",
      variableInputKey: "variable",
      outputNameInputKey: null
    });
    expect(isIntrinsicCatalogItem(setOutputActivity)).toBe(true);
  });

  it("tolerates an older backend without an intrinsic block", () => {
    expect(readIntrinsicDescriptor(writeLine)).toBeNull();
    expect(isIntrinsicCatalogItem(writeLine)).toBe(false);
    expect(readIntrinsicDescriptor(undefined)).toBeNull();
  });

  it("ignores a malformed intrinsic block", () => {
    const malformed = { ...writeLine, intrinsic: { kind: "", valueInputKey: "value" } } as ActivityCatalogItem;
    expect(readIntrinsicDescriptor(malformed)).toBeNull();
  });
});

describe("intrinsic node materialization", () => {
  it("materializes a Set Variable node with the authored intrinsic wire shape", () => {
    const node = createActivityNode(setVariableActivity, "node-1");

    expect(node).toEqual({
      nodeId: "node-1",
      activityVersionId: "elsa.intrinsic.set@1",
      inputs: [],
      outputs: [],
      intrinsic: {
        kind: "Set",
        valueType: { alias: "Elsa.Any", collectionKind: "Single" },
        variable: { referenceKey: "", declaringScopeId: "workflow" }
      }
    });
    // Not an activity-reference node: it carries no authored structure.
    expect(node.structure).toBeUndefined();
  });

  it("materializes a Set Output node without a variable target", () => {
    const node = createActivityNode(setOutputActivity, "node-2");

    expect(node.activityVersionId).toBe("elsa.intrinsic.set-output@1");
    expect(node.intrinsic).toEqual({
      kind: "SetOutput",
      valueType: { alias: "Elsa.Any", collectionKind: "Single" }
    });
    expect(node.intrinsic).not.toHaveProperty("variable");
  });

  it("seeds Set Variable with a value type and a (blank) variable so the backend accepts the draft", () => {
    const block = buildIntrinsicWireBlock(readIntrinsicDescriptor(setVariableActivity)!);
    expect(block.valueType).toEqual({ alias: "Elsa.Any", collectionKind: "Single" });
    expect(block.variable).toEqual({ referenceKey: "", declaringScopeId: "workflow" });
  });
});

describe("intrinsic variable target editing", () => {
  const node = createActivityNode(setVariableActivity, "node-1");

  it("writes and reads the variable target", () => {
    expect(readIntrinsicVariable(node)).toBeNull(); // blank seed reads as unset

    const next = writeIntrinsicVariable(node, { referenceKey: "counter", declaringScopeId: "workflow" });
    expect(readNodeIntrinsic(next)?.variable).toEqual({ referenceKey: "counter", declaringScopeId: "workflow" });
    expect(readIntrinsicVariable(next)).toEqual({ referenceKey: "counter", declaringScopeId: "workflow" });
    // The value input and kind are untouched.
    expect(next.intrinsic?.kind).toBe("Set");
    expect(next.inputs).toEqual([]);
  });

  it("clears the variable target", () => {
    const withVar = writeIntrinsicVariable(node, { referenceKey: "counter", declaringScopeId: "workflow" });
    const cleared = writeIntrinsicVariable(withVar, null);
    expect(cleared.intrinsic?.variable).toBeUndefined();
  });

  it("is a no-op on a non-intrinsic node", () => {
    const leaf: ActivityNode = { nodeId: "leaf", activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] };
    expect(writeIntrinsicVariable(leaf, { referenceKey: "x", declaringScopeId: "workflow" })).toBe(leaf);
  });
});

describe("catalog → palette mapping", () => {
  it("surfaces the intrinsics under their Primitives category", () => {
    const catalog: ActivityCatalogItem[] = [setVariableActivity, setOutputActivity];
    const groups = groupActivityPalette(catalog);
    const primitives = groups.find(group => group.category === "Primitives");
    expect(primitives?.activities.map(activity => activity.displayName)).toEqual(["Set Output", "Set Variable"]);
  });
});

describe("intrinsic node serialization round-trip", () => {
  // A Set Variable node whose value input was authored, mirroring what the inspector writes: the value
  // rides the top-level wrapped property (expand form) and the intrinsic block rides verbatim.
  function draftWithSetVariable(): WorkflowDefinitionState {
    const node: ActivityNode = {
      nodeId: "set-1",
      activityVersionId: "elsa.intrinsic.set@1",
      inputs: [],
      outputs: [],
      intrinsic: {
        kind: "Set",
        valueType: { alias: "Elsa.Any", collectionKind: "Single" },
        variable: { referenceKey: "counter", declaringScopeId: "workflow" }
      },
      value: { typeName: "", expression: { type: "Literal", value: "42" } }
    };
    return {
      rootActivity: {
        nodeId: "root",
        activityVersionId: "elsa.sequence@1",
        inputs: [],
        outputs: [],
        structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [node] } }
      }
    };
  }

  it("folds the value input into inputs[] while preserving the intrinsic block, and round-trips", () => {
    const canonical = canonicalizeStateForWire(draftWithSetVariable());
    const wireNode = (canonical.rootActivity!.structure!.payload.activities as ActivityNode[])[0];

    // Value folded into inputs[]; intrinsic block preserved verbatim as a node member (not an input).
    expect(wireNode.inputs).toEqual([{ referenceKey: "value", value: { value: "42", expressionType: "Literal" } }]);
    expect(wireNode.intrinsic).toEqual({
      kind: "Set",
      valueType: { alias: "Elsa.Any", collectionKind: "Single" },
      variable: { referenceKey: "counter", declaringScopeId: "workflow" }
    });
    expect(wireNode).not.toHaveProperty("value");

    // Reload expands inputs back to the top-level wrapped property, keeping the intrinsic block.
    const reloaded = expandStateFromWire(canonical);
    const reloadedNode = (reloaded.rootActivity!.structure!.payload.activities as ActivityNode[])[0];
    expect(reloadedNode.intrinsic).toEqual(wireNode.intrinsic);
    expect((reloadedNode as Record<string, unknown>).value).toEqual({
      typeName: "",
      expression: { type: "Literal", value: "42" }
    });
    expect(reloadedNode.inputs).toEqual([]);
  });
});
