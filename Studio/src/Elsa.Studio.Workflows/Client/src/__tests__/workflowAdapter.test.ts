import { describe, expect, it } from "vitest";
import {
  buildUnsupportedActivityCanvas,
  buildCanvas,
  createActivityNode,
  flowchartStructureKind,
  getActivityDesignerSupport,
  getActivityDisplay,
  getChildSlots,
  sequenceStructureKind,
  spliceWorkflowEdge,
  syncCanvasToScope,
  updateScopeActivities,
  withFlowchartConnections,
  type CanvasScope
} from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";

const writeLine: ActivityCatalogItem = {
  activityVersionId: "activity-write-line-v1",
  activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
  version: "1.0.0",
  category: "Primitives",
  displayName: "Write Line",
  description: null,
  executionType: "Action",
  inputs: [],
  outputs: [],
  designFacets: []
};

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
        suppressFlowPorts: true,
        sourcePorts: [],
        childSlots: [expect.objectContaining({ property: "embedded", activities: [expect.objectContaining({ nodeId: "child" })] })]
      }
    });
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

    const updated = updateScopeActivities(root, [{
      ownerNodeId: "sequence-child",
      slotId: `${sequenceStructureKind}:activities`,
      label: "Sequence / Activities"
    }], replacement);

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
});

function sequenceRoot(activities: ActivityNode[]): ActivityNode {
  return {
    nodeId: "root",
    activityVersionId: "sequence-root",
    inputs: [],
    outputs: [],
    structure: {
      kind: sequenceStructureKind,
      schemaVersion: "1.0.0",
      payload: { activities }
    }
  };
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
