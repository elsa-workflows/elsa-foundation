import { describe, expect, it } from "vitest";
import {
  WORKFLOW_SCOPE_ID,
  isWorkflowScopeRef,
  makeVariableReference,
  readContainerVariables,
  readVariableReference,
  scopedVariableSignature,
  shadowingWarningMap,
  supportsScopedVariables,
  writeContainerVariables
} from "../scopedVariables";
import type { ActivityCatalogItem, ActivityNode, VariableDefinition, WorkflowDefinitionState } from "../workflowTypes";

const variable = (referenceKey: string, name: string): VariableDefinition => ({
  referenceKey,
  name,
  type: { alias: "String", collectionKind: "Single" },
  storageDriverType: null,
  default: null
});

const leaf = (nodeId: string): ActivityNode => ({ nodeId, activityVersionId: "leaf-v1", inputs: [], outputs: [] });

const sequence = (nodeId: string, activities: ActivityNode[], variables: VariableDefinition[] = []): ActivityNode => ({
  nodeId,
  activityVersionId: "seq-v1",
  inputs: [],
  outputs: [],
  structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities, variables } }
});

const forEachCatalog: ActivityCatalogItem = {
  activityVersionId: "foreach-v1",
  activityTypeKey: "Elsa.Activities.ControlFlow.Activities.ForEach",
  version: "1.0.0",
  category: "Control Flow",
  displayName: "For Each",
  executionType: "Action",
  inputs: [],
  outputs: [],
  designFacets: [{
    kind: "elsa.foreach.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "sequence",
      supportsScopedVariables: true,
      slots: [{ name: "Body", property: "body", displayName: "Body", cardinality: "single" }],
      initialPayload: { body: null }
    }
  }]
};

const forEach = (nodeId: string, body: ActivityNode | null): ActivityNode => ({
  nodeId,
  activityVersionId: forEachCatalog.activityVersionId,
  inputs: [],
  outputs: [],
  structure: { kind: "elsa.foreach.structure", schemaVersion: "1.0.0", payload: { body } }
});

describe("supportsScopedVariables", () => {
  it("is true for Sequence and Flowchart containers, false otherwise", () => {
    expect(supportsScopedVariables(sequence("s", []))).toBe(true);
    expect(supportsScopedVariables({ ...leaf("f"), structure: { kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: {} } })).toBe(true);
    expect(supportsScopedVariables(leaf("x"))).toBe(false);
    expect(supportsScopedVariables(null)).toBe(false);
  });

  it("uses structure design facets for scoped-variable support", () => {
    expect(supportsScopedVariables(forEach("foreach", null), forEachCatalog)).toBe(true);
  });
});

describe("container variable read/write", () => {
  it("reads declared container variables", () => {
    expect(readContainerVariables(sequence("s", [], [variable("v1", "Count")])).map(v => v.name)).toEqual(["Count"]);
  });

  it("returns empty for non-containers", () => {
    expect(readContainerVariables(leaf("x"))).toEqual([]);
  });

  it("writes container variables immutably, preserving other payload keys and the original node", () => {
    const node = sequence("s", [leaf("a")], [variable("v1", "Count")]);
    const updated = writeContainerVariables(node, [variable("v2", "Total")]);

    expect(readContainerVariables(updated).map(v => v.name)).toEqual(["Total"]);
    expect((updated.structure!.payload.activities as ActivityNode[]).map(a => a.nodeId)).toEqual(["a"]);
    expect(readContainerVariables(node).map(v => v.name)).toEqual(["Count"]); // original untouched
  });

  it("is a no-op for a node without an owned structure", () => {
    const node = leaf("x");
    expect(writeContainerVariables(node, [variable("v", "V")])).toBe(node);
  });
});

describe("variable references", () => {
  it("builds workflow-scope references with the explicit sentinel", () => {
    expect(makeVariableReference("k", null)).toEqual({ referenceKey: "k", declaringScopeId: WORKFLOW_SCOPE_ID });
    expect(makeVariableReference("k", "workflow")).toEqual({ referenceKey: "k", declaringScopeId: WORKFLOW_SCOPE_ID });
  });

  it("preserves a container declaring scope id", () => {
    expect(makeVariableReference("k", "node-1")).toEqual({ referenceKey: "k", declaringScopeId: "node-1" });
  });

  it("classifies workflow scope", () => {
    expect(isWorkflowScopeRef({ declaringScopeId: null })).toBe(true);
    expect(isWorkflowScopeRef({ declaringScopeId: "workflow" })).toBe(true);
    expect(isWorkflowScopeRef({ declaringScopeId: "node-1" })).toBe(false);
  });

  it("reads references from object, JSON-string, and bare-key forms", () => {
    expect(readVariableReference({ referenceKey: "k", declaringScopeId: "n" })).toEqual({ referenceKey: "k", declaringScopeId: "n" });
    expect(readVariableReference('{"referenceKey":"k","declaringScopeId":"n"}')).toEqual({ referenceKey: "k", declaringScopeId: "n" });
    expect(readVariableReference("bare")).toEqual({ referenceKey: "bare", declaringScopeId: null });
    expect(readVariableReference("")).toBeNull();
    expect(readVariableReference(null)).toBeNull();
  });
});

describe("scopedVariableSignature", () => {
  const base: WorkflowDefinitionState = {
    variables: [variable("w", "Flag")],
    rootActivity: sequence("root", [leaf("a")], [variable("c", "Counter")])
  };

  it("is stable across edits that do not affect visibility", () => {
    expect(scopedVariableSignature({ ...base, inputs: [{}] })).toBe(scopedVariableSignature(base));
  });

  it("changes when a declaration name changes", () => {
    const renamed: WorkflowDefinitionState = { ...base, rootActivity: sequence("root", [leaf("a")], [variable("c", "Renamed")]) };
    expect(scopedVariableSignature(renamed)).not.toBe(scopedVariableSignature(base));
  });

  it("changes when the tree shape changes", () => {
    const moved: WorkflowDefinitionState = { ...base, rootActivity: sequence("root", [leaf("a"), leaf("b")], [variable("c", "Counter")]) };
    expect(scopedVariableSignature(moved)).not.toBe(scopedVariableSignature(base));
  });

  it("changes when a node's structure contract changes", () => {
    const changedRoot = { ...base.rootActivity!, activityVersionId: "sequence-v2", structure: { ...base.rootActivity!.structure!, kind: "custom.structure" } };
    expect(scopedVariableSignature({ ...base, rootActivity: changedRoot })).not.toBe(scopedVariableSignature(base));
  });

  it("changes when a node's structure schema version changes", () => {
    const changedRoot = { ...base.rootActivity!, structure: { ...base.rootActivity!.structure!, schemaVersion: "2" } };
    expect(scopedVariableSignature({ ...base, rootActivity: changedRoot })).not.toBe(scopedVariableSignature(base));
  });

  it("uses catalog facets when computing child tree shape", () => {
    const empty: WorkflowDefinitionState = { variables: [], rootActivity: forEach("foreach", null) };
    const withBody: WorkflowDefinitionState = { variables: [], rootActivity: forEach("foreach", leaf("body")) };

    expect(scopedVariableSignature(withBody, [forEachCatalog])).not.toBe(scopedVariableSignature(empty, [forEachCatalog]));
  });
});

describe("shadowingWarningMap", () => {
  it("maps reference keys to advisories for the given scope only", () => {
    const warnings = [
      { scopeId: "node-1", shadowedScopeId: "workflow", name: "Count", referenceKey: "k1" },
      { scopeId: "node-2", shadowedScopeId: "workflow", name: "Other", referenceKey: "k2" }
    ];
    const map = shadowingWarningMap(warnings, "node-1");
    expect(map.get("k1")).toContain("Count");
    expect(map.has("k2")).toBe(false);
  });
});
