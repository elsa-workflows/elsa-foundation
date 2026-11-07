import { describe, expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentTransaction, bpmnNamespaces, readBpmnDocument } from "../bpmn/bpmnDocument";
import { planBpmnDocumentSyntaxChange } from "../bpmn/bpmnDocumentSyntaxChange";

const leaf = (nodeId: string): ActivityNode => ({ nodeId, activityVersionId: `exact-${nodeId}`, inputs: [], outputs: [], vendor: { retained: true } });
function owner(): ActivityNode {
  const sequence = { ...leaf("sequence"), structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [leaf("nested")] } } };
  return { ...leaf("owner"), structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    documentXml: `<b:definitions xmlns:b="${bpmnNamespaces.model}" targetNamespace="urn:policy"><b:process id="P"><b:task id="T"/><b:subProcess id="S"><b:task id="C"/></b:subProcess></b:process><b:process id="P2"><b:task id="Other"/></b:process></b:definitions>`,
    processId: "P", variables: [], activities: [sequence, leaf("listener"), leaf("child"), leaf("other")],
    bindings: [{ elementId: "T", childNodeId: "sequence", listenerNodeId: "listener", opaque: "exact" }, { elementId: "C", childNodeId: "child" }, { elementId: "Other", childNodeId: "other" }],
    scopeHeaders: [{ scopeElementId: "S", nodeId: "scope", activityVersionId: "exact-process", inputs: [], outputs: [], variables: [], extra: "exact" },
      { scopeElementId: "P2", nodeId: "process2", activityVersionId: "exact-process2", inputs: [], outputs: [], variables: [] }],
    vendor: { retained: true }
  } } };
}
const apply = (before: ActivityNode, plan: ReturnType<typeof planBpmnDocumentSyntaxChange>) => readBpmnDocument(applyBpmnDocumentTransaction(before, plan.transaction));

describe("parent-owned standard syntax/native configuration policy", () => {
  it("deletes a bound shape, its native child subtree and listener in one source transaction", () => {
    const before = owner(), original = structuredClone(before);
    const plan = planBpmnDocumentSyntaxChange(before, [{ kind: "remove", target: { elementId: "T" } }]);
    expect([...plan.removedNodeIds].sort()).toEqual(["sequence", "nested", "listener"].sort());
    expect(plan.removedElementIds).toEqual(["T"]);
    expect(plan.unqualifiedOwnershipIds).toEqual([]);
    const result = apply(before, plan);
    expect(result.byId.has("T")).toBe(false);
    expect(result.payload.activities.map(activity => activity.nodeId)).toEqual(["child", "other"]);
    expect(result.payload.bindings).toEqual(readBpmnDocument(before).payload.bindings.slice(1));
    expect(result.payload.vendor).toEqual({ retained: true });
    expect(before).toEqual(original);
  });

  it("deletes a standard body with its exact header and all descendant native bindings", () => {
    const before = owner();
    const plan = planBpmnDocumentSyntaxChange(before, [{ kind: "remove", target: { elementId: "S" } }]);
    expect([...plan.removedNodeIds].sort()).toEqual(["child", "scope"]);
    expect([...plan.removedElementIds].sort()).toEqual(["S", "C"].sort());
    expect(apply(before, plan).payload.scopeHeaders?.map(header => header.nodeId)).toEqual(["process2"]);
    expect(apply(before, plan).payload.activities.map(activity => activity.nodeId)).toEqual(["sequence", "listener", "other"]);
  });

  it("keeps unrelated independent processes and their exact configuration", () => {
    const before = owner(), parsed = readBpmnDocument(before);
    const plan = planBpmnDocumentSyntaxChange(before, [{ kind: "remove", target: { elementId: "S" } }]);
    const after = apply(before, plan);
    expect(after.byId.get("Other")?.parentElement?.id).toBe("P2");
    expect(after.payload.activities.at(-1)).toEqual(parsed.payload.activities.at(-1));
    expect(after.payload.bindings.at(-1)).toEqual(parsed.payload.bindings.at(-1));
    expect(after.payload.scopeHeaders?.at(-1)).toEqual(parsed.payload.scopeHeaders?.at(-1));
  });

  it("retains exact binding intent when a task type changes", () => {
    const before = owner(), parsed = readBpmnDocument(before);
    const plan = planBpmnDocumentSyntaxChange(before, [{ kind: "replaceType", target: { elementId: "T" }, localName: "serviceTask" }]);
    expect(plan.transaction.configuration).toBeUndefined();
    const after = apply(before, plan);
    expect(after.byId.get("T")?.localName).toBe("serviceTask");
    expect(after.payload.activities).toEqual(parsed.payload.activities);
    expect(after.payload.bindings).toEqual(parsed.payload.bindings);
  });

  it("does not drop native configuration to make an incompatible replacement appear valid", () => {
    const before = owner(), original = structuredClone(before);
    expect(() => planBpmnDocumentSyntaxChange(before, [{ kind: "replaceType", target: { elementId: "T" }, localName: "textAnnotation" }])).toThrow("native bindings");
    expect(before).toEqual(original);
  });

  it("retains native identities and scoped references during a standard reparent", () => {
    const before = owner(), parsed = readBpmnDocument(before);
    const plan = planBpmnDocumentSyntaxChange(before, [{ kind: "reparent", target: { elementId: "T" }, parent: { elementId: "S" } }]);
    const after = apply(before, plan);
    expect(after.byId.get("T")?.parentElement?.id).toBe("S");
    expect(after.payload.activities).toEqual(parsed.payload.activities);
    expect(after.payload.scopeHeaders).toEqual(parsed.payload.scopeHeaders);
    expect(plan.removedNodeIds).toEqual([]);
  });

  it("creates standard syntax without inventing an Elsa version, identity or binding", () => {
    const before = owner();
    const plan = planBpmnDocumentSyntaxChange(before, [{ kind: "append", parent: { elementId: "P" }, xml: `<b:serviceTask xmlns:b="${bpmnNamespaces.model}" id="New"/>` }]);
    expect(plan.transaction.configuration).toBeUndefined();
    expect(apply(before, plan).payload.activities).toEqual(readBpmnDocument(before).payload.activities);
    expect(apply(before, plan).payload.bindings.some(binding => binding.elementId === "New")).toBe(false);
  });

  it("reports unqualified removed native ownership for host refusal before side-table cleanup", () => {
    const before = owner();
    const data = before.structure!.payload;
    data.activities = (data.activities as ActivityNode[]).map(activity => activity.nodeId === "sequence" ? { ...activity, structure: {
      kind: "vendor.unknown", schemaVersion: "1", payload: { opaque: { nodeId: "not-guessed" } }
    } } : activity);
    const plan = planBpmnDocumentSyntaxChange(before, [{ kind: "remove", target: { elementId: "T" } }]);
    expect(plan.unqualifiedOwnershipIds).toEqual(["sequence"]);
    expect(plan.removedNodeIds).not.toContain("not-guessed");
    expect(apply(before, plan).payload.activities.map(activity => activity.nodeId)).toEqual(["child", "other"]);
  });

  it("does not demand ownership of retained opaque native structures for unrelated XML edits", () => {
    const before = owner();
    (before.structure!.payload.activities as ActivityNode[])[0].structure = { kind: "vendor.unknown", schemaVersion: "1", payload: { opaque: true } };
    const plan = planBpmnDocumentSyntaxChange(before, [{ kind: "attribute", target: { elementId: "Other" }, name: "name", value: "Renamed" }]);
    expect(plan.unqualifiedOwnershipIds).toEqual([]);
    expect(apply(before, plan).payload.activities).toEqual(readBpmnDocument(before).payload.activities);
  });
});
