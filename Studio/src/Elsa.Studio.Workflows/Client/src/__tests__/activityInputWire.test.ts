import { describe, expect, it } from "vitest";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";

describe("activity input wire adapter", () => {
  it("folds a top-level wrapped input into the canonical inputs array", () => {
    const state = stateOf(writeLine("write-one", { text: wrapped("Hello World!", "Literal") }));

    const wire = canonicalizeStateForWire(state);
    const node = wire.rootActivity!;

    expect(node.inputs).toEqual([
      { referenceKey: "Text", value: { value: "Hello World!", expressionType: "Literal" } }
    ]);
    expect("text" in node).toBe(false);
  });

  it("preserves the authored expression syntax instead of forcing Literal", () => {
    const state = stateOf(writeLine("write-one", { text: wrapped("user.name", "JavaScript") }));

    const node = canonicalizeStateForWire(state).rootActivity!;

    expect(node.inputs).toEqual([
      { referenceKey: "Text", value: { value: "user.name", expressionType: "JavaScript" } }
    ]);
  });

  it("serializes non-string literal values for the string-typed backend contract", () => {
    const state = stateOf(writeLine("write-one", { count: wrapped(42, "Literal"), enabled: wrapped(true, "Literal") }));

    const node = canonicalizeStateForWire(state).rootActivity!;

    expect(node.inputs).toEqual([
      { referenceKey: "Count", value: { value: "42", expressionType: "Literal" } },
      { referenceKey: "Enabled", value: { value: "true", expressionType: "Literal" } }
    ]);
  });

  it("leaves non-input extras and structure untouched while canonicalizing nested activities", () => {
    const child = writeLine("child", { text: wrapped("nested", "Literal") });
    const root: ActivityNode = {
      nodeId: "root",
      activityVersionId: "sequence-version",
      inputs: [],
      outputs: [],
      structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [child] } }
    };

    const wire = canonicalizeStateForWire(stateOf(root)).rootActivity!;
    const nested = (wire.structure!.payload.activities as ActivityNode[])[0];

    expect(nested.inputs).toEqual([
      { referenceKey: "Text", value: { value: "nested", expressionType: "Literal" } }
    ]);
  });

  it("does not treat flowchart connections as activities", () => {
    const root: ActivityNode = {
      nodeId: "root",
      activityVersionId: "flowchart-version",
      inputs: [],
      outputs: [],
      structure: {
        kind: "elsa.flowchart.structure",
        schemaVersion: "1.0.0",
        payload: {
          activities: [writeLine("a", { text: wrapped("hi", "Literal") })],
          connections: [{ id: "c1", source: { nodeId: "a", port: "Done" }, target: { nodeId: "b" } }]
        }
      }
    };

    const wire = canonicalizeStateForWire(stateOf(root)).rootActivity!;

    expect(wire.structure!.payload.connections).toEqual(root.structure!.payload.connections);
  });

  it("round-trips canonical inputs back into editable top-level wrapped values", () => {
    const state = stateOf(writeLine("write-one", { text: wrapped("Hello World!", "Literal") }));

    const restored = expandStateFromWire(canonicalizeStateForWire(state)).rootActivity!;

    expect(restored.inputs).toEqual([]);
    expect(restored.text).toEqual({ typeName: "", expression: { type: "Literal", value: "Hello World!" } });
  });
});

function stateOf(rootActivity: ActivityNode): WorkflowDefinitionState {
  return { rootActivity };
}

function writeLine(nodeId: string, properties: Record<string, unknown>): ActivityNode {
  return {
    nodeId,
    activityVersionId: `${nodeId}-version`,
    inputs: [],
    outputs: [],
    structure: null,
    ...properties
  };
}

function wrapped(value: unknown, type: string) {
  return { typeName: "System.String", expression: { type, value } };
}
