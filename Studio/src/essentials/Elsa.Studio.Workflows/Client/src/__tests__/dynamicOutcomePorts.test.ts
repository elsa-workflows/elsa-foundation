import { describe, expect, it } from "vitest";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import type { WorkflowDefinitionState } from "../workflowTypes";
import { allowsMultipleOutcomes, buildCanvas, getActivitySourcePorts } from "../workflowAdapter";
import { flowchartStructureKind } from "../flowchartStartNode";
import { writeLine } from "./fixtures";
import type { ActivityNode } from "../workflowTypes";
const catalog = { ...writeLine, inputs: [{ referenceKey: "Actions", name: "Actions" }], designFacets: [{ kind: "elsa.outcomes", schemaVersion: "1", payload: { ports: [], dynamicOutcomes: { kind: "valuePerItem", source: "Actions", unmatched: "Unmatched" } } }] };
const node = (value: unknown): ActivityNode => ({ nodeId: "task", activityVersionId: "task-v1", inputs: [], outputs: [], Actions: { expression: { type: "Literal", value } } });
// A legacy catalog: its facet has no `selectsOnEveryCompletion`, so the old shape (items + "Unmatched") holds.
describe("authored collection outcome ports", () => {
  it("reads fixed collection defaults advertised with Object syntax", () => {
    const defaults = { ...catalog, inputs: [{ referenceKey: "Actions", name: "Actions", defaultSyntax: "Object", defaultValue: '["Approve","Reject"]' }] };
    expect(getActivitySourcePorts({ nodeId: "task", activityVersionId: "v", inputs: [], outputs: [] }, defaults).map(x => x.name)).toEqual(["Approve", "Reject", "Unmatched"]);
  });
  it("reflects edits, ordinal distinct names and numeric literals exactly", () => {
    expect(getActivitySourcePorts(node(["Approve", "approve", "承認", "Approve", "", 202]), catalog).map(x => x.name)).toEqual(["Approve", "approve", "承認", "202", "Unmatched"]);
    expect(getActivitySourcePorts(node(["Reject"]), catalog).map(x => x.name)).toEqual(["Reject", "Unmatched"]);
  });
  it("preserves ports through the real canonical wire and reload transformations", () => {
    const state = { rootActivity: node(["Approve", "Reject"]), variables: [], inputs: [], outputs: [] } as unknown as WorkflowDefinitionState;
    const wire = canonicalizeStateForWire(state);
    expect(getActivitySourcePorts(wire.rootActivity!, catalog).map(x => x.name)).toEqual(["Approve", "Reject", "Unmatched"]);
    const reloaded = expandStateFromWire(wire);
    expect(getActivitySourcePorts(reloaded.rootActivity!, catalog).map(x => x.name)).toEqual(["Approve", "Reject", "Unmatched"]);
    const object = { nodeId: "task", activityVersionId: "v", inputs: [{ referenceKey: "Actions", value: { expressionType: "Object", value: '["Approve"]' } }], outputs: [] };
    expect(getActivitySourcePorts(object, catalog).map(x => x.name)).toEqual(["Approve", "Unmatched"]);
  });
  it("reads canonical wire JSON and refuses executable expressions as literal ports", () => {
    const wire = { nodeId: "task", activityVersionId: "v", inputs: [{ referenceKey: "Actions", value: { expressionType: "Literal", value: '["Approve"]' } }], outputs: [] };
    expect(getActivitySourcePorts(wire, catalog).map(x => x.name)).toEqual(["Approve", "Unmatched"]);
    expect(getActivitySourcePorts({ ...node(["Approve"]), Actions: { expression: { type: "JavaScript", value: '["Approve"]' } } }, catalog).map(x => x.name)).toEqual(["Done"]);
    expect(getActivitySourcePorts(node([]), catalog).map(x => x.name)).toEqual(["Done"]);
  });
});

const outcomePort = (name: string) => ({ name, type: "Flow" });
const runJavaScript = { ...writeLine, activityVersionId: "js-v1", inputs: [{ referenceKey: "PossibleOutcomes", name: "PossibleOutcomes" }], designFacets: [{ kind: "elsa.outcomes", schemaVersion: "1", payload: {
  ports: [outcomePort("Done")],
  dynamicOutcomes: { kind: "valuePerItem", source: "PossibleOutcomes", selectsOnEveryCompletion: false },
  multipleOutcomes: { kind: "always" } } }] };
// Static ports reach the no-items path through the catalog item's own `ports`, as before this spec.
const sendHttpRequest = { ...writeLine, activityVersionId: "http-v1", ports: [outcomePort("Done"), outcomePort("Failed"), outcomePort("Timeout")], inputs: [{ referenceKey: "ExpectedStatusCodes", name: "ExpectedStatusCodes" }], designFacets: [{ kind: "elsa.outcomes", schemaVersion: "1", payload: {
  ports: [outcomePort("Done"), outcomePort("Failed"), outcomePort("Timeout")],
  dynamicOutcomes: { kind: "valuePerItem", source: "ExpectedStatusCodes", selectsOnEveryCompletion: true, unmatched: "Unmatched status code" } } }] };
const switchActivity = { ...writeLine, activityVersionId: "switch-v1", designFacets: [{ kind: "elsa.outcomes", schemaVersion: "1", payload: {
  ports: [], multipleOutcomes: { kind: "whenInputEquals", input: "mode", value: "MatchAny" } } }] };
const literal = (nodeId: string, activityVersionId: string, key: string, value: unknown): ActivityNode =>
  ({ nodeId, activityVersionId, inputs: [], outputs: [], [key]: { expression: { type: "Literal", value } } });

describe("outcome sets resolved at design time (spec 1208)", () => {
  it("gives RunJavaScript Done plus its possible outcomes, without Unmatched (US6 scenario 1)", () => {
    expect(getActivitySourcePorts(literal("js", "js-v1", "PossibleOutcomes", ["Approved", "Rejected"]), runJavaScript).map(x => x.name))
      .toEqual(["Done", "Approved", "Rejected"]);
  });
  it("drops Done from a SendHttpRequest with expected status codes and keeps its unmatched status code (FR-012a)", () => {
    expect(getActivitySourcePorts(literal("http", "http-v1", "ExpectedStatusCodes", [200, 404]), sendHttpRequest).map(x => x.name))
      .toEqual(["Failed", "Timeout", "200", "404", "Unmatched status code"]);
  });
  it("keeps the static ports when no items are configured", () => {
    expect(getActivitySourcePorts(literal("http", "http-v1", "ExpectedStatusCodes", []), sendHttpRequest).map(x => x.name)).toEqual(["Done", "Failed", "Timeout"]);
    expect(getActivitySourcePorts({ nodeId: "js", activityVersionId: "js-v1", inputs: [], outputs: [] }, runJavaScript).map(x => x.name)).toEqual(["Done"]);
  });
  it("keeps the legacy shape for a catalog without selectsOnEveryCompletion", () => {
    const legacy = { ...sendHttpRequest, designFacets: [{ kind: "elsa.outcomes", schemaVersion: "1", payload: {
      ports: [outcomePort("Done"), outcomePort("Failed")], dynamicOutcomes: { kind: "valuePerItem", source: "ExpectedStatusCodes" } } }] };
    expect(getActivitySourcePorts(literal("http", "http-v1", "ExpectedStatusCodes", [200]), legacy).map(x => x.name)).toEqual(["Done", "Failed", "200", "Unmatched"]);
  });
  it("records whether a completion may select several outcomes (FR-014)", () => {
    expect(allowsMultipleOutcomes(literal("js", "js-v1", "PossibleOutcomes", []), runJavaScript)).toBe(true);
    expect(allowsMultipleOutcomes(literal("http", "http-v1", "ExpectedStatusCodes", [200]), sendHttpRequest)).toBe(false);
    const switchNode = (mode?: string): ActivityNode => ({ nodeId: "switch", activityVersionId: "switch-v1", inputs: [], outputs: [],
      structure: { kind: "elsa.switch.structure", schemaVersion: "1", payload: mode ? { mode, cases: [] } : { cases: [] } } });
    expect(allowsMultipleOutcomes(switchNode("MatchAny"), switchActivity)).toBe(true);
    expect(allowsMultipleOutcomes(switchNode("MatchFirst"), switchActivity)).toBe(false);
    expect(allowsMultipleOutcomes(switchNode(), switchActivity)).toBe(false);
    expect(allowsMultipleOutcomes(literal("switch", "switch-v1", "mode", "MatchAny"), switchActivity)).toBe(true);
    expect(allowsMultipleOutcomes({ ...literal("switch", "switch-v1", "mode", "MatchAny"), mode: { expression: { type: "JavaScript", value: "'MatchAny'" } } }, switchActivity)).toBe(false);
  });
  it("keeps a connection on a vanished port visible and flags it (US6 scenario 3)", () => {
    const js = literal("js", "js-v1", "PossibleOutcomes", ["Approved"]);
    const target: ActivityNode = { nodeId: "next", activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] };
    const owner: ActivityNode = { nodeId: "root", activityVersionId: "flowchart-v1", inputs: [], outputs: [], structure: { kind: flowchartStructureKind, schemaVersion: "1.0.0", payload: {
      activities: [js, target],
      connections: [
        { id: "kept", source: { nodeId: "js", port: "Approved" }, target: { nodeId: "next" } },
        { id: "gone", source: { nodeId: "js", port: "Rejected" }, target: { nodeId: "next" } }
      ] } } };
    const canvas = buildCanvas({ owner, slot: { id: "activities", label: "Activities", property: "activities", cardinality: "many", mode: "flowchart", activities: [js, target] } }, [runJavaScript, writeLine], []);
    const kept = canvas.edges.find(edge => edge.id === "kept")!;
    const gone = canvas.edges.find(edge => edge.id === "gone")!;
    expect(kept.data?.missingPort).toBeUndefined();
    expect(gone.data?.missingPort).toBe(true);
    expect(gone.sourceHandle).toBe("Rejected");
    expect(gone.className).toContain("wf-edge-missing-port");
    const ports = canvas.nodes.find(node => node.id === "js")!.data.sourcePorts;
    expect(ports.map(port => [port.name, !!port.missing])).toEqual([["Done", false], ["Approved", false], ["Rejected", true]]);
    expect(canvas.nodes.find(node => node.id === "js")!.data.allowsMultipleOutcomes).toBe(true);
  });
});
