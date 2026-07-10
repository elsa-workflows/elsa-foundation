import { describe, expect, it } from "vitest";
import {
  applyRuntimeOverlays,
  buildUnsupportedActivityCanvas,
  buildCanvas,
  createActivityNode,
  findNodeScopePath,
  flowchartStructureKind,
  getActivityDesignerSupport,
  getActivityDisplay,
  getChildSlots,
  normalizeActivityStructures,
  readStructureDesignFacet,
  replaceSlotActivities,
  resolveScope,
  sequenceStructureKind,
  spliceWorkflowEdge,
  syncCanvasToScope,
  updateScopeActivities,
  withFlowchartConnections,
  type CanvasScope,
  type ScopeFrame
} from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { writeLine } from "./fixtures";

// The sequence/flowchart variants below deliberately have NO design facets, and this file's forEach
// facet declares mode "sequence" — they exercise the legacy/fallback structure paths, unlike the
// real-backend shapes in ./fixtures. Only the base writeLine is shared.
const sequenceActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-sequence-v1",
  activityTypeKey: "Elsa.Activities.Sequence.Activities.Sequence",
  category: "Composition",
  displayName: "Sequence"
};

const flowchartActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-flowchart-v1",
  activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
  category: "Composition",
  displayName: "Flowchart"
};

const forEachActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-for-each-v1",
  activityTypeKey: "Elsa.Activities.ControlFlow.Activities.ForEach",
  category: "Control Flow",
  displayName: "For Each",
  designFacets: [{
    kind: "elsa.foreach.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "sequence",
      supportsScopedVariables: true,
      slots: [{
        name: "Body",
        property: "body",
        displayName: "Body",
        cardinality: "single"
      }],
      initialPayload: { body: null }
    }
  }]
};

describe("workflow adapter", () => {
  it("uses friendly activity display names and falls back from fully-qualified names", () => {
    expect(getActivityDisplay(writeLine)).toBe("Write Line");
    expect(getActivityDisplay({
      ...writeLine,
      displayName: "Elsa.Activities.Primitives.Activities.WriteLine"
    })).toBe("Write Line");
  });

  it("discovers generic embedded activity arrays without activity-name hardcoding", () => {
    const activity: ActivityNode = {
      nodeId: "custom",
      activityVersionId: "custom-version",
      inputs: [],
      outputs: [],
      structure: {
        kind: "acme.custom.structure",
        schemaVersion: "1.0.0",
        payload: {
          primaryBranch: [node("a")],
          metadata: { ignored: true }
        }
      }
    };

    const slots = getChildSlots(activity);

    expect(slots).toHaveLength(1);
    expect(slots[0]).toMatchObject({
      property: "primaryBranch",
      label: "Primary Branch",
      mode: "generic"
    });
  });

  it("creates structure from activity structure design facets", () => {
    const activity = createActivityNode(forEachActivity, "foreach");

    expect(readStructureDesignFacet(forEachActivity)).toMatchObject({
      kind: "elsa.foreach.structure",
      schemaVersion: "1.0.0",
      payload: {
        mode: "sequence",
        supportsScopedVariables: true,
        slots: [expect.objectContaining({ property: "body", cardinality: "single" })]
      }
    });
    expect(activity.structure).toEqual({
      kind: "elsa.foreach.structure",
      schemaVersion: "1.0.0",
      payload: { body: null }
    });
  });

  it("returns facet-declared single child slots when the payload value is null", () => {
    const activity = createActivityNode(forEachActivity, "foreach");

    const slots = getChildSlots(activity, forEachActivity);

    expect(slots).toEqual([expect.objectContaining({
      id: "elsa.foreach.structure:body",
      label: "Body",
      property: "body",
      cardinality: "single",
      mode: "sequence",
      activities: []
    })]);
  });

  it("does not infer generic child slots from null payload fields without a facet", () => {
    const activity: ActivityNode = {
      nodeId: "custom",
      activityVersionId: "custom-version",
      inputs: [],
      outputs: [],
      structure: {
        kind: "acme.custom.structure",
        schemaVersion: "1.0.0",
        payload: { body: null }
      }
    };

    expect(getChildSlots(activity)).toEqual([]);
  });

  it("stores a single slot child as an object instead of an array", () => {
    const activity = createActivityNode(forEachActivity, "foreach");
    const child = node("body-child");
    const slot = getChildSlots(activity, forEachActivity)[0];

    const updated = replaceSlotActivities(activity, slot, [child]);

    expect(updated.structure?.payload.body).toEqual(child);
  });

  it("keeps Sequence and Flowchart structure fallbacks for older catalogs", () => {
    expect(createActivityNode(sequenceActivity, "sequence").structure).toEqual({
      kind: sequenceStructureKind,
      schemaVersion: "1.0.0",
      payload: { activities: [] }
    });
    expect(createActivityNode(flowchartActivity, "flowchart").structure).toEqual({
      kind: flowchartStructureKind,
      schemaVersion: "1.0.0",
      payload: {
        activities: [],
        connections: [],
        startNodeId: null,
        nodeMetadata: {},
        connectionMetadata: {}
      }
    });
  });

  it("normalizes existing nodes with null structure from the catalog facet", () => {
    const root = node("foreach");
    root.activityVersionId = forEachActivity.activityVersionId;

    const normalized = normalizeActivityStructures(root, [forEachActivity]);

    expect(normalized?.structure).toEqual({
      kind: "elsa.foreach.structure",
      schemaVersion: "1.0.0",
      payload: { body: null }
    });
    expect(getChildSlots(normalized!, forEachActivity)[0]).toMatchObject({ label: "Body", cardinality: "single" });
  });

  it("updates repeatable collection child slots without replacing authored collection items", () => {
    const switchActivity: ActivityCatalogItem = {
      ...writeLine,
      activityVersionId: "activity-switch-v1",
      activityTypeKey: "Elsa.Activities.ControlFlow.Activities.Switch",
      displayName: "Switch",
      designFacets: [{
        kind: "elsa.switch.structure",
        schemaVersion: "1.0.0",
        payload: {
          mode: "sequence",
          supportsScopedVariables: false,
          slots: [{
            name: "Case",
            property: "case",
            displayName: "Case",
            cardinality: "single",
            collectionProperty: "cases",
            childProperty: "activity",
            labelProperty: "name",
            slotNameTemplate: "Case {label}"
          }],
          initialPayload: { cases: [] }
        }
      }]
    };
    const root: ActivityNode = {
      nodeId: "switch",
      activityVersionId: switchActivity.activityVersionId,
      inputs: [],
      outputs: [],
      structure: {
        kind: "elsa.switch.structure",
        schemaVersion: "1.0.0",
        payload: {
          cases: [
            { name: "A", expression: "a", activity: null },
            { name: "B", expression: "b", activity: null },
            { name: "B", expression: "b2", activity: node("old") }
          ]
        }
      }
    };
    const body = node("new");

    const slots = getChildSlots(root, switchActivity);
    const updated = replaceSlotActivities(root, slots[2], [body]);

    expect(new Set(slots.map(slot => slot.id)).size).toBe(3);
    expect(slots.map(slot => slot.label)).toEqual(["Case A", "Case B", "Case B"]);
    expect(updated.structure?.payload.cases).toEqual([
      { name: "A", expression: "a", activity: null },
      { name: "B", expression: "b", activity: null },
      { name: "B", expression: "b2", activity: body }
    ]);
  });

  it("resolves explicit designer support for flowchart and sequence activities", () => {
    expect(getActivityDesignerSupport(createActivityNode(flowchartActivity, "flowchart"), flowchartActivity)).toBe("flowchart");
    expect(getActivityDesignerSupport(createActivityNode(sequenceActivity, "sequence"), sequenceActivity)).toBe("sequence");
    expect(getActivityDesignerSupport(node("write-line"), writeLine)).toBe("unsupported");
  });

  it("builds a fixed unsupported activity canvas without flow ports", () => {
    const activity: ActivityNode = {
      nodeId: "unsupported-root",
      activityVersionId: writeLine.activityVersionId,
      inputs: [],
      outputs: [],
      structure: {
        kind: "acme.unsupported.structure",
        schemaVersion: "1.0.0",
        payload: {
          embedded: [node("child")]
        }
      }
    };

    const canvas = buildUnsupportedActivityCanvas(activity, [writeLine], [{ nodeId: "unsupported-root", x: 40, y: 80 }]);

    expect(canvas.edges).toEqual([]);
    expect(canvas.nodes).toHaveLength(1);
    expect(canvas.nodes[0]).toMatchObject({
      id: "unsupported-root",
      draggable: false,
      deletable: false,
      connectable: false,
      position: { x: 40, y: 80 },
      data: {
        label: "Write Line",
        category: "Primitives",
        executionType: "Action",
        icon: "terminal",
        suppressFlowPorts: true,
        sourcePorts: [],
        childSlots: [expect.objectContaining({ property: "embedded", activities: [expect.objectContaining({ nodeId: "child" })] })]
      }
    });
  });

  it("uses explicit known activity icon metadata before fallback icon resolution", () => {
    const canvas = buildUnsupportedActivityCanvas(node("explicit-icon"), [{ ...writeLine, icon: "trigger" }], []);

    expect(canvas.nodes[0].data.icon).toBe("trigger");
  });

  it("uses the Sequence adapter to preserve ordered child activities", () => {
    const root = sequenceRoot([node("b"), node("a")]);
    const scope = firstScope(root);
    const canvas = buildCanvas(scope, [writeLine], [
      { nodeId: "a", x: 10, y: 0 },
      { nodeId: "b", x: 300, y: 0 }
    ]);

    const updated = syncCanvasToScope(scope, canvas.nodes, canvas.edges);
    const activities = getChildSlots(updated)[0].activities;

    expect(activities.map(activity => activity.nodeId)).toEqual(["a", "b"]);
  });

  it("replaces nested slot activities through the generic scope path", () => {
    const childSequence = createActivityNode(sequenceActivity, "sequence-child");
    const root = sequenceRoot([childSequence]);
    const replacement = [node("nested")];

    // enterSlot(childSequence, "<its own primary slot>") — the frame's slotId is a slot of ownerNodeId.
    const updated = updateScopeActivities(root, [enterSlotFrame("sequence-child", `${sequenceStructureKind}:activities`)], replacement);

    const sequence = getChildSlots(updated)[0].activities[0];
    expect(getChildSlots(sequence)[0].activities.map(activity => activity.nodeId)).toEqual(["nested"]);
  });

  it("builds Flowchart edges from V4 connection endpoints", () => {
    const root: ActivityNode = {
      nodeId: "root",
      activityVersionId: "flowchart-version",
      inputs: [],
      outputs: [],
      structure: {
        kind: flowchartStructureKind,
        schemaVersion: "1.0.0",
        payload: {
          activities: [node("a"), node("b")],
          connections: [{ source: { nodeId: "a", port: "Done" }, target: { nodeId: "b", port: "Done" } }]
        }
      }
    };

    const canvas = buildCanvas(firstScope(root), [writeLine], []);

    expect(canvas.edges).toHaveLength(1);
    expect(canvas.edges[0]).toMatchObject({ source: "a", target: "b", sourceHandle: "Done" });
    expect(canvas.edges[0].targetHandle).toBeUndefined();
  });

  it("preserves flowchart connection metadata and vertices while syncing ports", () => {
    const root: ActivityNode = {
      nodeId: "root",
      activityVersionId: "flowchart-version",
      inputs: [],
      outputs: [],
      structure: {
        kind: flowchartStructureKind,
        schemaVersion: "1.0.0",
        payload: {
          activities: [node("a"), node("b")],
          connections: [{
            id: "connection-1",
            source: { nodeId: "a", port: "Approved" },
            target: { nodeId: "b" },
            metadata: { existing: true },
            vertices: [{ x: 10, y: 20 }]
          }]
        }
      }
    };
    const canvas = buildCanvas(firstScope(root), [writeLine], []);
    const updated = withFlowchartConnections(root, [{
      ...canvas.edges[0],
      sourceHandle: "Rejected",
      data: { vertices: [{ x: 40.4, y: 50.6 }] }
    }]);

    expect(updated.structure?.payload.connections).toEqual([{
      id: "connection-1",
      source: { nodeId: "a", port: "Rejected" },
      target: { nodeId: "b" },
      metadata: { existing: true },
      vertices: [{ x: 40, y: 51 }]
    }]);
  });

  it("splices flowchart edges through a new Done connection", () => {
    const edges = [{
      id: "connection-1",
      source: "a",
      target: "b",
      sourceHandle: "Approved",
      type: "workflow"
    }];

    const spliced = spliceWorkflowEdge(edges, edges[0], "inserted");

    expect(spliced).toHaveLength(2);
    expect(spliced[0]).toMatchObject({ source: "a", target: "inserted", sourceHandle: "Approved" });
    expect(spliced[1]).toMatchObject({ source: "inserted", target: "b", sourceHandle: "Done" });
  });

  it("adds runtime overlays and selected incident state to matching graph nodes", () => {
    const canvas = buildCanvas(firstScope(sequenceRoot([node("write-line-1")])), [writeLine], []);
    const nodes = applyRuntimeOverlays(canvas.nodes, [activityExecution()], [incident()], "incident-1");

    expect(nodes[0]).toMatchObject({
      id: "write-line-1",
      selected: true,
      data: {
        runtime: {
          status: "Faulted",
          activityExecutionId: "activity-execution-1",
          incidentCount: 1,
          hasBlockingIncident: true,
          selected: true
        }
      }
    });
  });
});

// A two-slot generic container: a facet declaring `primary` and `secondary` single-cardinality slots.
// Used to prove non-primary slots are addressable through a scope frame.
const twoSlotActivity: ActivityCatalogItem = {
  ...writeLine,
  activityVersionId: "activity-two-slot-v1",
  activityTypeKey: "Acme.Activities.TwoSlot",
  displayName: "Two Slot",
  designFacets: [{
    kind: "acme.two-slot.structure",
    schemaVersion: "1.0.0",
    payload: {
      mode: "sequence",
      supportsScopedVariables: false,
      slots: [
        { name: "Primary", property: "primary", displayName: "Primary", cardinality: "single" },
        { name: "Secondary", property: "secondary", displayName: "Secondary", cardinality: "single" }
      ],
      initialPayload: { primary: null, secondary: null }
    }
  }]
};

describe("scope frames (target semantics)", () => {
  it("resolves a ForEach body slot nested inside a Flowchart root", () => {
    const forEach = createActivityNode(forEachActivity, "foreach");
    const root = flowchartRoot([forEach]);
    const catalog = [flowchartActivity, forEachActivity];
    const frame = enterSlotFrame("foreach", "elsa.foreach.structure:body");

    const scope = resolveScope(root, [frame], catalog);

    expect(scope?.owner.nodeId).toBe("foreach");
    expect(scope?.slot).toMatchObject({ id: "elsa.foreach.structure:body", property: "body", cardinality: "single" });
  });

  it("writes a ForEach body as a single object and round-trips through resolveScope", () => {
    const forEach = createActivityNode(forEachActivity, "foreach");
    const root = flowchartRoot([forEach]);
    const catalog = [flowchartActivity, forEachActivity];
    const frame = enterSlotFrame("foreach", "elsa.foreach.structure:body");

    const updated = updateScopeActivities(root, [frame], [node("body-child")], catalog);

    const updatedForEach = getChildSlots(updated, catalog)[0].activities[0];
    // Single-cardinality slots store one object, never an array.
    expect(updatedForEach.structure?.payload.body).toMatchObject({ nodeId: "body-child" });
    expect(Array.isArray(updatedForEach.structure?.payload.body)).toBe(false);

    const scope = resolveScope(updated, [frame], catalog);
    expect(scope?.slot.activities.map(activity => activity.nodeId)).toEqual(["body-child"]);
  });

  it("still resolves same-kind nesting (Flowchart-in-Flowchart, Sequence-in-Sequence)", () => {
    const flowchartChild = createActivityNode(flowchartActivity, "flow-child");
    const flowchartTree = flowchartRoot([flowchartChild]);
    const flowchartScope = resolveScope(
      flowchartTree,
      [enterSlotFrame("flow-child", `${flowchartStructureKind}:activities`)],
      [flowchartActivity]
    );
    expect(flowchartScope?.owner.nodeId).toBe("flow-child");
    expect(flowchartScope?.slot.id).toBe(`${flowchartStructureKind}:activities`);

    const sequenceChild = createActivityNode(sequenceActivity, "seq-child");
    const sequenceTree = sequenceRoot([sequenceChild]);
    const sequenceScope = resolveScope(
      sequenceTree,
      [enterSlotFrame("seq-child", `${sequenceStructureKind}:activities`)],
      [sequenceActivity]
    );
    expect(sequenceScope?.owner.nodeId).toBe("seq-child");
    expect(sequenceScope?.slot.id).toBe(`${sequenceStructureKind}:activities`);
  });

  it("addresses the second slot of a multi-slot container", () => {
    const twoSlot = createActivityNode(twoSlotActivity, "two-slot");
    const root = flowchartRoot([twoSlot]);
    const catalog = [flowchartActivity, twoSlotActivity];
    const secondFrame = enterSlotFrame("two-slot", "acme.two-slot.structure:secondary");

    const scope = resolveScope(root, [secondFrame], catalog);
    expect(scope?.slot).toMatchObject({ id: "acme.two-slot.structure:secondary", property: "secondary" });

    const updated = updateScopeActivities(root, [secondFrame], [node("into-secondary")], catalog);
    const updatedTwoSlot = getChildSlots(updated, catalog)[0].activities[0];
    expect(updatedTwoSlot.structure?.payload.secondary).toMatchObject({ nodeId: "into-secondary" });
    // The primary slot is untouched by writing to the secondary one.
    expect(updatedTwoSlot.structure?.payload.primary).toBeNull();
  });

  it("returns null when a frame's owner or named slot no longer resolves", () => {
    const forEach = createActivityNode(forEachActivity, "foreach");
    const root = flowchartRoot([forEach]);
    const catalog = [flowchartActivity, forEachActivity];

    expect(resolveScope(root, [enterSlotFrame("ghost", "elsa.foreach.structure:body")], catalog)).toBeNull();
    expect(resolveScope(root, [enterSlotFrame("foreach", "elsa.foreach.structure:missing")], catalog)).toBeNull();
  });

  it("finds a scope path for a node nested inside a ForEach body two levels deep and surfaces it", () => {
    const deepLeaf = node("deep");
    const innerSequence = sequenceNode("inner", [deepLeaf]);
    const forEach = createActivityNode(forEachActivity, "foreach");
    forEach.structure = { ...forEach.structure!, payload: { body: innerSequence } };
    const root = flowchartRoot([forEach]);
    const catalog = [flowchartActivity, forEachActivity];

    const path = findNodeScopePath(root, "deep", activity => activity.nodeId, catalog);

    expect(path).not.toBeNull();
    // The last frame names the container that directly holds the node and the slot holding it.
    expect(path?.at(-1)).toMatchObject({ ownerNodeId: "inner", slotId: `${sequenceStructureKind}:activities` });
    const scope = resolveScope(root, path!, catalog);
    expect(scope?.slot.activities.map(activity => activity.nodeId)).toContain("deep");
  });
});

function sequenceRoot(activities: ActivityNode[]): ActivityNode {
  return sequenceNode("root", activities, "sequence-root");
}

function sequenceNode(nodeId: string, activities: ActivityNode[], activityVersionId = sequenceActivity.activityVersionId): ActivityNode {
  return {
    nodeId,
    activityVersionId,
    inputs: [],
    outputs: [],
    structure: {
      kind: sequenceStructureKind,
      schemaVersion: "1.0.0",
      payload: { activities }
    }
  };
}

function flowchartRoot(activities: ActivityNode[]): ActivityNode {
  return {
    nodeId: "root",
    activityVersionId: flowchartActivity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: {
      kind: flowchartStructureKind,
      schemaVersion: "1.0.0",
      payload: { activities, connections: [] }
    }
  };
}

// Builds the frame workflowDocument.enterSlot emits: descend into `ownerNodeId`, view its own `slotId`.
function enterSlotFrame(ownerNodeId: string, slotId: string): ScopeFrame {
  return { ownerNodeId, slotId, label: `${ownerNodeId} / ${slotId}` };
}

function firstScope(owner: ActivityNode): CanvasScope {
  return { owner, slot: getChildSlots(owner)[0] };
}

function node(nodeId: string): ActivityNode {
  return {
    nodeId,
    activityVersionId: writeLine.activityVersionId,
    inputs: [],
    outputs: [],
    structure: null
  };
}

function activityExecution() {
  return {
    activityExecutionId: "activity-execution-1",
    workflowExecutionId: "wfexec-1",
    executableNodeId: "write-line-1",
    authoredActivityId: "write-line-1",
    activityType: "Elsa.Activities.Primitives.Activities.WriteLine",
    activityTypeVersion: "1.0.0",
    status: "Faulted",
    subStatus: null,
    scheduledAt: "2026-06-18T01:00:01Z",
    startedAt: "2026-06-18T01:00:01Z",
    completedAt: "2026-06-18T01:00:02Z",
    schedulingActivityExecutionId: null,
    parentActivityExecutionId: null,
    branchId: null,
    iterationId: null,
    callStackDepth: 0,
    bookmarkIds: [],
    incidentIds: ["incident-1"],
    faultCount: 1,
    aggregateFaultCount: 0,
    metadata: {}
  };
}

function incident() {
  return {
    incidentId: "incident-1",
    workflowExecutionId: "wfexec-1",
    activityExecutionId: "activity-execution-1",
    executableNodeId: "write-line-1",
    severity: "Error",
    status: "Open",
    resolutionAction: "None",
    failureType: "ActivityFaulted",
    message: "Required service is not registered.",
    createdAt: "2026-06-18T01:00:02Z",
    resolvedAt: null,
    isBlocking: true,
    metadata: {}
  };
}
