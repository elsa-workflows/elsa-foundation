import { describe, expect, it } from "vitest";
import { createActivityNode, getActivitySourcePorts, hasSwitchCaseOutgoingConnections, updateActivity, flowchartEdges } from "../workflowAdapter";
import { bpmnActivity, collectionActivity, flowchartActivity, flowchartNode, leafNode } from "./fixtures";
import { reconcileSwitchOutcomeConnections } from "../switchOutcomePorts";
import type { ActivityNode } from "../workflowTypes";

function subject(labels = ["Approved", "Rejected"], schema = "2.0.0") {
  const node = createActivityNode(collectionActivity("switch"), "switch");
  node.structure!.schemaVersion = schema;
  node.structure!.payload.cases = labels.map((match, i) => ({ match, activity: null,
    ...(schema === "2.0.0" ? { caseId: `case-${i}`, condition: { value: { expressionType: "Literal", value: true } } } : {}) }));
  return node;
}
function graph(node: ActivityNode, port = "Approved") {
  const root = flowchartNode("root", [node, leafNode("target")]);
  root.structure!.payload.connections = [
    { id: "case-wire", source: { nodeId: "switch", port, future: true }, target: { nodeId: "target" }, vertices: [{ x: 1, y: 2 }], extra: "keep" },
    { id: "default-wire", source: { nodeId: "switch", port: "Default" }, target: { nodeId: "target" } }
  ];
  root.structure!.payload.connectionMetadata = { "case-wire": { configuration: { keep: "yes" } }, "default-wire": { label: "fallback" } };
  return root;
}
function change(root: ActivityNode, cases: unknown[]) {
  return updateActivity(root, "switch", node => ({ ...node,
    structure: { ...node.structure!, payload: { ...node.structure!.payload, cases } } }), [flowchartActivity, collectionActivity("switch")]);
}
function items(node: ActivityNode) { return node.structure!.payload.cases as Record<string, unknown>[]; }
function wires(root: ActivityNode) { return root.structure!.payload.connections as { id: string; source: { nodeId: string; port: string }; target: { nodeId: string } }[]; }

describe("structured Switch outcome ports", () => {
  it.each(["1.0.0", "2.0.0"])("projects human-readable case ports from schema %s", schema => {
    expect(getActivitySourcePorts(subject(undefined, schema)).map(port => port.displayName)).toEqual(["Approved", "Rejected", "Default", "Break"]);
  });
  it("uses the runtime's normalization and shares reserved outcomes", () => {
    expect(getActivitySourcePorts(subject([" Approved ", "Approved", "", " ", "Done", "Default", "Break"]))).toEqual([
      { name: "Approved", displayName: "Approved" }, { name: "Done", displayName: "Done" },
      { name: "Default", displayName: "Default" }, { name: "Break", displayName: "Break" }
    ]);
  });
  it("renames a connected case by stable identity in one immutable update", () => {
    const node = subject(), root = graph(node);
    const next = change(root, items(node).map((item, i) => i === 0 ? { ...item, match: "Accepted" } : item));
    expect(wires(next)[0]).toEqual({ ...((root.structure!.payload.connections as unknown[])[0] as object),
      source: { nodeId: "switch", port: "Accepted", future: true } });
    expect(next.structure!.payload.connectionMetadata).toEqual(root.structure!.payload.connectionMetadata);
    expect(wires(root)[0].source.port).toBe("Approved");
    expect(flowchartEdges(next)[0].label).toBe("Accepted");
  });
  it("keeps wires through reordering and removes only a deleted case's connections", () => {
    const node = subject(), root = graph(node);
    expect(wires(change(root, [...items(node)].reverse()))).toEqual(wires(root));
    const removed = change(root, items(node).slice(1));
    expect(wires(removed).map(wire => wire.id)).toEqual(["default-wire"]);
    expect(removed.structure!.payload.connectionMetadata).toEqual({ "default-wire": { label: "fallback" } });
  });
  it("does not retarget a deleted case to the case that reuses its old label", () => {
    const node = subject(), root = graph(node);
    const next = change(root, [{ ...items(node)[1], match: "Approved" }]);
    expect(wires(next).map(wire => wire.id)).toEqual(["default-wire"]);
  });
  it("preserves both destinations when a shared runtime outcome splits after renaming", () => {
    const node = subject(["Default"]), root = graph(node, "Default");
    const next = change(root, [{ ...items(node)[0], match: "Approved" }]);
    expect(wires(next).map(wire => wire.source.port).sort()).toEqual(["Approved", "Approved", "Default", "Default"]);
    expect(wires(next).filter(wire => wire.source.port === "Default").map(wire => wire.id)).toEqual(["case-wire", "default-wire"]);
    expect(wires(next).every(wire => wire.target.nodeId === "target")).toBe(true);
    expect(new Set(wires(next).map(wire => wire.id)).size).toBe(4);
  });
  it("keeps legacy label connections through rename and condition upgrade", () => {
    const node = subject(undefined, "1.0.0"), root = graph(node);
    const renamed = change(root, items(node).map((item, i) => i === 0 ? { ...item, match: "Accepted" } : item));
    expect(wires(renamed)[0].source.port).toBe("Accepted");
    const upgraded = updateActivity(root, "switch", () => subject(), [flowchartActivity, collectionActivity("switch")]);
    expect(wires(upgraded)).toEqual(wires(root));
  });
  it("does not interpret unknown legacy caseId metadata as a condition-case identity", () => {
    const node = subject(undefined, "1.0.0");
    node.structure!.payload.cases = items(node).map(item => ({ ...item, caseId: "opaque-metadata" }));
    const root = graph(node);
    const next = change(root, items(node).map((item, i) => i === 0 ? { ...item, match: "Accepted" } : item));
    expect(wires(next)[0].source.port).toBe("Accepted");
    expect((next.structure!.payload.activities as ActivityNode[])[0].structure!.payload.cases).toEqual([
      { match: "Accepted", activity: null, caseId: "opaque-metadata" },
      { match: "Rejected", activity: null, caseId: "opaque-metadata" }
    ]);
  });
  it("keeps invalid or unsupported documents diagnosable without pruning wires", () => {
    const node = subject(), root = graph(node);
    expect(wires(change(root, [{ match: "other", activity: null }]))).toEqual(wires(root));
    node.structure!.schemaVersion = "future";
    expect(getActivitySourcePorts(node)).toEqual([{ name: "Done", displayName: "Done" }]);
  });
  it("removes a wire when its label becomes blank, without inventing a Done outcome", () => {
    const node = subject(), root = graph(node);
    const next = change(root, items(node).map((item, i) => i === 0 ? { ...item, match: " " } : item));
    expect(wires(next).map(wire => wire.id)).toEqual(["default-wire"]);
    expect(getActivitySourcePorts(subject([" "])).map(port => port.name)).toEqual(["Default", "Break"]);
  });
  it("warns before removing connected outcomes, while retaining shared Default wires", () => {
    const node = subject(), root = graph(node);
    expect(hasSwitchCaseOutgoingConnections(root, node, 0)).toBe(true);
    expect(hasSwitchCaseOutgoingConnections(root, node, 1)).toBe(false);
    const shared = subject(["Default"]);
    expect(hasSwitchCaseOutgoingConnections(graph(shared, "Default"), shared, 0)).toBe(false);
  });
  it("reconciles existing explicit BPMN outcome conditions without changing native default flows", () => {
    const node = subject(), next = { ...node, structure: { ...node.structure!, payload: { ...node.structure!.payload,
      cases: items(node).map((item, i) => i === 0 ? { ...item, match: "Accepted" } : item) } } };
    const scope = createActivityNode(bpmnActivity, "process");
    scope.structure!.payload = { elements: [{ elementId: "task", childNodeId: "switch", defaultFlowId: "fallback-by-id" }], sequenceFlows: [
      { flowId: "selected", sourceRef: "task", targetRef: "end", conditionOutcome: "Approved", extra: "keep" },
      { flowId: "fallback", sourceRef: "task", targetRef: "end", conditionOutcome: "Approved", isDefault: true },
      { flowId: "fallback-by-id", sourceRef: "task", targetRef: "end", conditionOutcome: "Approved" }
    ] };
    const result = reconcileSwitchOutcomeConnections(scope, [node], [next]);
    expect(result.structure!.payload.sequenceFlows).toEqual([
      { flowId: "selected", sourceRef: "task", targetRef: "end", conditionOutcome: "Accepted", extra: "keep" },
      { flowId: "fallback", sourceRef: "task", targetRef: "end", conditionOutcome: "Approved", isDefault: true },
      { flowId: "fallback-by-id", sourceRef: "task", targetRef: "end", conditionOutcome: "Approved" }
    ]);
  });
  it("keeps the original wires when condition IDs are invalid or duplicated", () => {
    const node = subject(), root = graph(node);
    expect(wires(change(root, items(node).map(item => ({ ...item, caseId: "duplicate" }))))).toEqual(wires(root));
  });
  it("keeps readable ports in read-only canvas snapshots without mutating the node", () => {
    const node = subject(), before = structuredClone(node);
    expect(getActivitySourcePorts(node)).toHaveLength(4);
    expect(node).toEqual(before);
  });
});
