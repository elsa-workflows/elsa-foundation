import { describe, expect, it } from "vitest";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import type { WorkflowDefinitionState } from "../workflowTypes";
import { getActivitySourcePorts } from "../workflowAdapter";
import { writeLine } from "./fixtures";
import type { ActivityNode } from "../workflowTypes";
const catalog = { ...writeLine, inputs: [{ referenceKey: "Actions", name: "Actions" }], designFacets: [{ kind: "elsa.outcomes", schemaVersion: "1", payload: { ports: [], dynamicOutcomes: { kind: "valuePerItem", source: "Actions", unmatched: "Unmatched" } } }] };
const node = (value: unknown): ActivityNode => ({ nodeId: "task", activityVersionId: "task-v1", inputs: [], outputs: [], Actions: { expression: { type: "Literal", value } } });
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
