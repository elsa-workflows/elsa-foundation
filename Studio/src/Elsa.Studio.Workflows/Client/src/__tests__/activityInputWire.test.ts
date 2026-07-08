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

  it("sends structured literals (collections and objects) as Object expressions the backend can deserialize", () => {
    const state = stateOf(writeLine("write-one", {
      lines: wrapped(["Hello", "world"], "Literal"),
      config: wrapped({ enabled: true }, "Literal")
    }));

    const node = canonicalizeStateForWire(state).rootActivity!;

    // A scalar literal converter can't turn the JSON string form of a list/object into ICollection<T> or a
    // complex type, so structured literal values ride the "Object" expression instead of "Literal".
    expect(node.inputs).toEqual([
      { referenceKey: "Lines", value: { value: '["Hello","world"]', expressionType: "Object" } },
      { referenceKey: "Config", value: { value: '{"enabled":true}', expressionType: "Object" } }
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

  it("canonicalizes inputs on a single child-slot activity", () => {
    const body = writeLine("body", { text: wrapped("Item X", "Literal") });
    const root: ActivityNode = {
      nodeId: "foreach",
      activityVersionId: "foreach-version",
      inputs: [],
      outputs: [],
      structure: { kind: "elsa.foreach.structure", schemaVersion: "1.0.0", payload: { body } }
    };

    const wire = canonicalizeStateForWire(stateOf(root)).rootActivity!;
    const nested = wire.structure!.payload.body as ActivityNode;

    expect(nested.inputs).toEqual([
      { referenceKey: "Text", value: { value: "Item X", expressionType: "Literal" } }
    ]);
    expect("text" in nested).toBe(false);
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

describe("argument collection wire adapter", () => {
  it("emits the canonical type shape for variables/inputs/outputs and drops isArray/assembly", () => {
    const state: WorkflowDefinitionState = {
      variables: [{ referenceKey: "v1", name: "Items", type: { alias: "String", collectionKind: "List" }, storageDriverType: "Elsa.Memory.MemoryStorageDriver", default: null }],
      inputs: [{ name: "Tags", type: { alias: "String", collectionKind: "HashSet" }, displayName: "Tags", storageDriverType: null }],
      outputs: [{ name: "Result", type: { alias: "Boolean", collectionKind: "Single" }, displayName: "Result" }]
    };

    const wire = canonicalizeStateForWire(state);

    expect(wire.variables![0]).toMatchObject({
      referenceKey: "v1",
      name: "Items",
      type: { alias: "String", collectionKind: "List" },
      storageDriverType: "Elsa.Memory.MemoryStorageDriver"
    });
    expect(wire.inputs![0]).toMatchObject({ name: "Tags", type: { alias: "String", collectionKind: "HashSet" }, storageDriverType: null });
    expect(wire.outputs![0]).toMatchObject({ name: "Result", type: { alias: "Boolean", collectionKind: "Single" } });
    // Outputs are minimal — no storage driver.
    expect("storageDriverType" in (wire.outputs![0] as object)).toBe(false);
  });

  it("backfills a referenceKey and drops the Elsa-3 input-default leftovers", () => {
    const state: WorkflowDefinitionState = {
      inputs: [{ name: "OrderId", type: { alias: "String", collectionKind: "Single" }, displayName: "OrderId", defaultValue: "x", defaultSyntax: "Literal", isReadOnly: true }] as unknown[],
      outputs: [{ name: "Result", type: { alias: "Boolean", collectionKind: "Single" }, displayName: "Result" }] as unknown[]
    };

    const wire = canonicalizeStateForWire(state);
    const input = wire.inputs![0] as Record<string, unknown>;
    const output = wire.outputs![0] as Record<string, unknown>;

    expect(typeof input.referenceKey).toBe("string");
    expect((input.referenceKey as string).length).toBeGreaterThan(0);
    expect(typeof output.referenceKey).toBe("string");
    expect("defaultValue" in input).toBe(false);
    expect("defaultSyntax" in input).toBe(false);
    expect("isReadOnly" in input).toBe(false);
  });

  it("preserves an existing referenceKey instead of regenerating it (idempotent)", () => {
    const state: WorkflowDefinitionState = {
      inputs: [{ referenceKey: "in-1", name: "OrderId", type: { alias: "String", collectionKind: "Single" }, displayName: "OrderId" }] as unknown[]
    };

    const once = canonicalizeStateForWire(state).inputs![0] as Record<string, unknown>;
    const twice = canonicalizeStateForWire(canonicalizeStateForWire(state)).inputs![0] as Record<string, unknown>;

    expect(once.referenceKey).toBe("in-1");
    expect(twice.referenceKey).toBe("in-1");
  });

  it("converts a legacy variable (typeInformation + isArray + object storage) on read", () => {
    const state: WorkflowDefinitionState = {
      variables: [{
        referenceKey: "v1",
        name: "Numbers",
        typeInformation: { typeName: "Int32", namespace: "System", assemblyName: "System.Private.CoreLib", assemblyVersion: "" },
        isArray: true,
        storageDriverType: { typeName: "MemoryStorageDriver", namespace: "Elsa.Memory" },
        default: null
      }] as unknown[]
    };

    const expanded = expandStateFromWire(state);
    const variable = expanded.variables![0] as Record<string, unknown>;

    expect(variable.type).toEqual({ alias: "Int32", collectionKind: "Array" });
    expect(variable.storageDriverType).toBe("Elsa.Memory.MemoryStorageDriver");
    expect("typeInformation" in variable).toBe(false);
    expect("isArray" in variable).toBe(false);
  });

  it("converts a legacy input (bare-string type + isArray) and preserves an unknown alias", () => {
    const state: WorkflowDefinitionState = {
      inputs: [{ name: "Blob", type: "Some.Unresolved.Type", isArray: true, displayName: "Blob" }] as unknown[]
    };

    const input = expandStateFromWire(state).inputs![0] as Record<string, unknown>;

    expect(input.type).toEqual({ alias: "Some.Unresolved.Type", collectionKind: "Array" });
    expect("isArray" in input).toBe(false);
  });

  it("normalizes container-scoped variable declarations in the activity tree", () => {
    const root: ActivityNode = {
      nodeId: "root",
      activityVersionId: "sequence-version",
      inputs: [],
      outputs: [],
      structure: {
        kind: "elsa.sequence.structure",
        schemaVersion: "1.0.0",
        payload: {
          activities: [],
          variables: [{ referenceKey: "c1", name: "Scoped", typeInformation: { typeName: "Boolean", namespace: "System" }, isArray: false }]
        }
      }
    };

    const wire = canonicalizeStateForWire(stateOf(root)).rootActivity!;
    const scoped = (wire.structure!.payload.variables as Record<string, unknown>[])[0];

    expect(scoped.type).toEqual({ alias: "Boolean", collectionKind: "Single" });
    expect("typeInformation" in scoped).toBe(false);
  });

  it("leaves state without argument collections untouched", () => {
    const state = stateOf(writeLine("root", {}));
    expect(canonicalizeStateForWire(state).variables).toBeUndefined();
    expect(canonicalizeStateForWire(state).inputs).toBeUndefined();
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
