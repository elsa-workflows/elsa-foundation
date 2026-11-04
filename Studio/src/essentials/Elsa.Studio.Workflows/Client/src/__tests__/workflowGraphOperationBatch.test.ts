import { describe, expect, it } from "vitest";
import {
  applyWorkflowDesignerPosition,
  applyWorkflowOperationBatch,
  classifyWorkflowOperationRisk,
  parseFrozenWorkflowBatch,
  WorkflowBatchRefusal,
  type FrozenWorkflowBatch,
  type FrozenWorkflowOperation
} from "../workflowGraphOperationBatch";
import type { ActivityNode } from "../workflowTypes";
import { flowchartActivity } from "./fixtures";
import { batchCatalog as catalog, flowSlot, nestedDraft, retired, sequenceSlot, writer } from "./batchFixtures";

describe("strict workflow operation batch applier", () => {
  it("applies every kind together to one copy, in a nested scope, and leaves the original untouched", () => {
    const draft = nestedDraft();
    const before = structuredClone(draft);

    const result = applyWorkflowOperationBatch(draft, batch(
      op({ kind: "add", nodeId: "greet", activityVersionId: writer.activityVersionId, activityTypeKey: writer.activityTypeKey, version: "1.0.0", scopePath: ["flow", "seq"], slot: sequenceSlot, position: { x: 300.4, y: 10 } }),
      op({ kind: "set-property", nodeId: "greet", property: "text", inputName: "Text", typeName: "String", value: { literal: "Hello" } }),
      op({ kind: "update", nodeId: "a", fields: { displayName: "Say hi", canStartWorkflow: true }, startInput: "canStartWorkflow" }),
      op({ kind: "position", nodeId: "b", x: 500, y: 600 }),
      op({ kind: "connect", source: "a", target: "seq", sourcePort: "Done", targetPort: null, scopeOwnerId: "flow", slot: flowSlot }),
      op({ kind: "disconnect", source: "a", target: "b", sourcePort: "Done" }, "destructive"),
      op({ kind: "set-property", nodeId: "b", property: "apiKey", inputName: "ApiKey", typeName: "String", value: { secretReference: { name: "smtp", typeName: "ApiKey", scope: null } } })
    ), catalog);

    expect(draft).toEqual(before);
    expect(result.appliedCount).toBe(7);
    expect(result.addedNodeIds).toEqual(["greet"]);
    const root = result.draft.state.rootActivity!;
    const seq = child(root, "seq");
    const greet = (seq.structure!.payload.activities as ActivityNode[]).find(node => node.nodeId === "greet")!;
    expect(greet).toMatchObject({ activityVersionId: writer.activityVersionId, text: { typeName: "String", expression: { type: "Literal", value: "Hello" } } });
    expect(child(root, "a")).toMatchObject({ activityVersionId: writer.activityVersionId, canStartWorkflow: { expression: { type: "Literal", value: true } } });
    expect(child(root, "b")).toMatchObject({ apiKey: { expression: { type: "Secret", value: { name: "smtp", typeName: "ApiKey" } } } });
    const connections = root.structure!.payload.connections as Array<{ source: { nodeId: string }; target: { nodeId: string } }>;
    expect(connections.map(connection => [connection.source.nodeId, connection.target.nodeId])).toEqual([["a", "seq"]]);
    expect(result.draft.layout).toEqual(expect.arrayContaining([{ nodeId: "greet", x: 300, y: 40 }, expect.objectContaining({ nodeId: "b", x: 500, y: 600 })]));
    expect(result.draft.activityPresentation).toEqual([{ nodeId: "seq", displayName: "Inner sequence" }, { nodeId: "a", displayName: "Say hi", description: undefined }]);
    expect(result.draft.sourceVersionId).toBeNull();
  });

  it("refuses the whole batch when a later operation fails, so nothing is applied", () => {
    const draft = nestedDraft();
    const before = structuredClone(draft);
    expect(() => applyWorkflowOperationBatch(draft, batch(
      op({ kind: "position", nodeId: "a", x: 1, y: 1 }),
      op({ kind: "position", nodeId: "missing", x: 1, y: 1 })
    ), catalog)).toThrow(/^Operation 1: No activity 'missing'/);
    expect(draft).toEqual(before);
  });

  it.each([
    ["an unknown field", { index: 0, kind: "position", risk: "reviewRequired", nodeId: "a", x: 1, y: 2, extra: true }, "Unknown field"],
    ["a missing property name", { index: 0, kind: "set-property", risk: "reviewRequired", nodeId: "a", inputName: "Text", typeName: "String", value: { literal: "x" } }, "Missing field(s) 'property'"],
    ["a missing slot", { index: 0, kind: "add", risk: "reviewRequired", nodeId: "n", activityVersionId: writer.activityVersionId, activityTypeKey: writer.activityTypeKey, version: "1.0.0", scopePath: ["flow"], position: null }, "'slot'"],
    ["a type or version rewrite", { index: 0, kind: "update", risk: "reviewRequired", nodeId: "a", fields: { activityVersionId: "other" }, startInput: null }, "never rewritten"],
    ["a structural property", { index: 0, kind: "set-property", risk: "reviewRequired", nodeId: "a", property: "structure", inputName: "Structure", typeName: "Object", value: { literal: "x" } }, "never rewritten"],
    ["an expression value", { index: 0, kind: "set-property", risk: "reviewRequired", nodeId: "a", property: "text", inputName: "Text", typeName: "String", value: { expression: "1" } }, "exactly one of"],
    ["an unknown kind", { index: 0, kind: "rewrite", risk: "reviewRequired", nodeId: "a" }, "Unsupported operation kind"],
    ["a reordered index", { index: 3, kind: "remove", risk: "destructive", nodeId: "a" }, "index"]
  ])("refuses %s in the strict frozen schema", (_name, operation, message) => {
    expect(() => parseFrozenWorkflowBatch({ schemaVersion: "1", highComplexity: false, risk: "reviewRequired", operations: [operation] }))
      .toThrow(WorkflowBatchRefusal);
    expect(() => parseFrozenWorkflowBatch({ schemaVersion: "1", highComplexity: false, risk: "reviewRequired", operations: [operation] })).toThrow(message);
  });

  it("refuses an unknown top-level field and an unsupported schema version", () => {
    expect(() => parseFrozenWorkflowBatch({ ...batch(op({ kind: "remove", nodeId: "a" }, "destructive")), extra: 1 })).toThrow("Unknown field");
    expect(() => parseFrozenWorkflowBatch({ ...batch(op({ kind: "remove", nodeId: "a" }, "destructive")), schemaVersion: "2" })).toThrow("schema 1");
  });

  it("writes only a declared input and refuses an undeclared one or a credential literal", () => {
    expect(() => applyWorkflowOperationBatch(nestedDraft(), batch(op({ kind: "set-property", nodeId: "a", property: "value", inputName: "Value", typeName: "String", value: { literal: "x" } })), catalog))
      .toThrow("not a declared input");
    expect(() => applyWorkflowOperationBatch(nestedDraft(), batch(op({ kind: "set-property", nodeId: "a", property: "apiKey", inputName: "ApiKey", typeName: "String", value: { literal: "CANARY" } })), catalog))
      .toThrow("credential input");
  });

  it("removes a subtree with its connections and side tables, and never reuses a removed id", () => {
    const draft = nestedDraft();
    const result = applyWorkflowOperationBatch(draft, batch(op({ kind: "remove", nodeId: "seq" }, "destructive")), catalog);
    expect(result.removedNodeIds.sort()).toEqual(["inner", "seq"]);
    expect(result.draft.layout.map(record => record.nodeId)).not.toContain("inner");
    expect(result.draft.activityPresentation?.map(record => record.nodeId)).not.toContain("seq");
    expect(() => applyWorkflowOperationBatch(draft, batch(
      op({ kind: "remove", nodeId: "seq" }, "destructive"),
      op({ kind: "add", nodeId: "inner", activityVersionId: writer.activityVersionId, activityTypeKey: writer.activityTypeKey, version: "1.0.0", scopePath: ["flow"], slot: flowSlot, position: null })
    ), catalog)).toThrow("already used");
  });

  it("refuses a non-addable activity, an unknown slot and a scope path that is not a containment chain", () => {
    const add = (fields: Partial<Extract<FrozenWorkflowOperation, { kind: "add" }>>) => op({ kind: "add", nodeId: "n", activityVersionId: writer.activityVersionId,
      activityTypeKey: writer.activityTypeKey, version: "1.0.0", scopePath: ["flow"], slot: flowSlot, position: null, ...fields });
    expect(() => applyWorkflowOperationBatch(nestedDraft(), batch(add({ activityVersionId: retired.activityVersionId, activityTypeKey: retired.activityTypeKey })), catalog)).toThrow("cannot be added");
    expect(() => applyWorkflowOperationBatch(nestedDraft(), batch(add({ slot: "nope" })), catalog)).toThrow("no slot");
    expect(() => applyWorkflowOperationBatch(nestedDraft(), batch(add({ scopePath: ["flow", "inner"] })), catalog)).toThrow("containment chain");
  });

  it("installs a new root only through set-root, once, classifies replacing a root as destructive, and fills it in a later batch", () => {
    const addRoot = op({ kind: "add", nodeId: "fresh", activityVersionId: flowchartActivity.activityVersionId, activityTypeKey: flowchartActivity.activityTypeKey, version: "1.0.0", scopePath: [], slot: "root", position: null });
    const frozen = batch(addRoot, op({ kind: "set-root", nodeId: "fresh", replacesRoot: true }, "destructive"));
    const result = applyWorkflowOperationBatch(nestedDraft(), frozen, catalog);
    expect(result.draft.state.rootActivity?.nodeId).toBe("fresh");
    expect(result.removedNodeIds).toEqual(expect.arrayContaining(["flow", "a", "b", "seq", "inner"]));
    expect(classifyWorkflowOperationRisk(nestedDraft(), frozen)).toEqual(["reviewRequired", "destructive"]);
    expect(() => applyWorkflowOperationBatch(nestedDraft(), batch(addRoot), catalog)).toThrow("never installed");
    // As the server prepares it: nothing goes into an activity this batch adds, and a batch installs one root.
    expect(() => applyWorkflowOperationBatch(nestedDraft(), batch(...frozen.operations, op({ kind: "add", nodeId: "first", activityVersionId: writer.activityVersionId,
      activityTypeKey: writer.activityTypeKey, version: "1.0.0", scopePath: ["fresh"], slot: flowSlot, position: null })), catalog)).toThrow("added by this batch");
  });

  it("changes only the authored value of an existing input, keeping its wrapper's other fields", () => {
    const draft = nestedDraft();
    const a = (draft.state.rootActivity!.structure!.payload.activities as ActivityNode[])[0];
    a.text = { typeName: "String", expression: { type: "Literal", value: "Old" }, memoryReference: { id: "m1" }, conversion: { mode: "Strict" }, argumentExtras: { keep: true } };
    const result = applyWorkflowOperationBatch(draft, batch(op({ kind: "set-property", nodeId: "a", property: "text", inputName: "Text", typeName: "String", value: { literal: "New" } })), catalog);
    expect(child(result.draft.state.rootActivity!, "a").text).toEqual({ typeName: "String", expression: { type: "Literal", value: "New" },
      memoryReference: { id: "m1" }, conversion: { mode: "Strict" }, argumentExtras: { keep: true } });
  });

  it("classifies removals and disconnections as destructive and everything else as review-required", () => {
    expect(classifyWorkflowOperationRisk(nestedDraft(), batch(
      op({ kind: "position", nodeId: "a", x: 1, y: 1 }),
      op({ kind: "remove", nodeId: "b" }, "destructive"),
      op({ kind: "disconnect", source: "a", target: "b", sourcePort: "Done" }, "destructive")
    ))).toEqual(["reviewRequired", "destructive", "destructive"]);
  });

  it("keeps the layout tool's exact move, preserving authored layout metadata and row order", () => {
    const draft = nestedDraft();
    draft.layout = [{ nodeId: "a", x: 120, y: 160, width: 260, height: 140, additionalProperties: { custom: { color: "blue" } } }, { nodeId: "b", x: 20, y: 40, width: 200 }];
    const before = structuredClone(draft);
    const result = applyWorkflowDesignerPosition(draft, "a", 480, 320, catalog);
    expect(result.draft.layout).toEqual([{ ...before.layout[0], x: 480, y: 320 }, before.layout[1]]);
    expect(result.draft.state).toEqual(before.state);
  });
});

function op<T extends Omit<FrozenWorkflowOperation, "index" | "risk">>(operation: T, risk: "reviewRequired" | "destructive" = "reviewRequired") {
  return { index: -1, risk, ...operation } as unknown as FrozenWorkflowOperation;
}

function batch(...operations: FrozenWorkflowOperation[]): FrozenWorkflowBatch {
  const indexed = operations.map((operation, index) => ({ ...operation, index }));
  return { schemaVersion: "1", highComplexity: false, risk: indexed.some(item => item.risk === "destructive") ? "destructive" : "reviewRequired", operations: indexed };
}

function child(root: ActivityNode, nodeId: string): ActivityNode {
  return (root.structure!.payload.activities as ActivityNode[]).find(node => node.nodeId === nodeId)!;
}
