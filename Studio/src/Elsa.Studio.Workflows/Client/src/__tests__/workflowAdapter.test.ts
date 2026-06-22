import { describe, expect, it } from "vitest";
import {
  buildCanvas,
  createActivityNode,
  flowchartStructureKind,
  getActivityDisplay,
  getChildSlots,
  sequenceStructureKind,
  syncCanvasToScope,
  updateScopeActivities,
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
    expect(canvas.edges[0]).toMatchObject({ source: "a", target: "b" });
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
